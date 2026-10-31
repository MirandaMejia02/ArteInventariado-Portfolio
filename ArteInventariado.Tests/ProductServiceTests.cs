using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class ProductServiceTests
{
    [Fact]
    public async Task CreateAsync_AllowsZeroStockAndTrimsRequiredName()
    {
        await using var fixture = await ProductFixture.CreateAsync();

        var product = await fixture.Service.CreateAsync(fixture.CreateRequest("  Producto futuro  ", stock: 0));

        Assert.Equal("Producto futuro", product.Nombre);
        Assert.Equal(0, product.PrecioCompraCentavos);
        Assert.Equal(0, product.PrecioVentaCentavos);
        Assert.Equal(0, product.StockActual);
        Assert.False(product.TieneImagen);
        Assert.Equal(1, await fixture.Context.Productos.CountAsync());
        var stored = await fixture.Context.Productos
            .AsNoTracking()
            .Include(item => item.Skus)
            .SingleAsync();
        Assert.Equal(ModoProducto.SIMPLE, stored.Modo);
        var sku = Assert.Single(stored.Skus);
        Assert.Equal(stored.CodigoInterno, sku.CodigoSku);
        Assert.True(sku.Activo);
        Assert.Null(sku.PrecioVentaOverrideCentavos);
        Assert.Null(sku.CostoOverrideCentavos);
    }

    [Theory]
    [InlineData(UbicacionInventario.SIN_ASIGNAR)]
    [InlineData(UbicacionInventario.EXHIBICION)]
    [InlineData(UbicacionInventario.BODEGA)]
    public async Task CreateAsync_PlacesInitialStockOnlyInSelectedCanonicalLocation(
        UbicacionInventario initialLocation)
    {
        await using var fixture = await ProductFixture.CreateAsync();

        var product = await fixture.Service.CreateAsync(
            fixture.CreateRequest("Producto ubicado", stock: 7) with
            {
                UbicacionInicial = initialLocation
            });

        fixture.Context.ChangeTracker.Clear();
        var balances = await fixture.Context.InventarioSaldos
            .AsNoTracking()
            .Where(balance => balance.ProductoSku.ProductoId == product.ProductoId)
            .ToListAsync();

        Assert.Equal(3, balances.Count);
        Assert.Equal(7, balances.Sum(balance => balance.ExistenciaFisica));
        Assert.Equal(
            7,
            balances.Single(balance => balance.Ubicacion == initialLocation).ExistenciaFisica);
        Assert.All(
            balances.Where(balance => balance.Ubicacion != initialLocation),
            balance => Assert.Equal(0, balance.ExistenciaFisica));
        Assert.Equal(
            0,
            await fixture.Context.Productos
                .AsNoTracking()
                .Where(item => item.ProductoId == product.ProductoId)
                .Select(item => item.StockActual)
                .SingleAsync());
    }

    [Fact]
    public async Task CreateAsync_DefaultsInitialLocationToUnassigned()
    {
        await using var fixture = await ProductFixture.CreateAsync();

        var product = await fixture.Service.CreateAsync(fixture.CreateRequest("Producto predeterminado", stock: 3));

        var balance = await fixture.Context.InventarioSaldos
            .AsNoTracking()
            .Where(item => item.ProductoSku.ProductoId == product.ProductoId &&
                item.Ubicacion == UbicacionInventario.SIN_ASIGNAR)
            .SingleAsync();

        Assert.Equal(3, balance.ExistenciaFisica);
    }

    [Fact]
    public async Task CreateAsync_ZeroStockDoesNotCreatePositiveBalanceRegardlessOfLocation()
    {
        await using var fixture = await ProductFixture.CreateAsync();

        var product = await fixture.Service.CreateAsync(
            fixture.CreateRequest("Producto sin stock", stock: 0) with
            {
                UbicacionInicial = UbicacionInventario.BODEGA
            });

        var balances = await fixture.Context.InventarioSaldos
            .AsNoTracking()
            .Where(item => item.ProductoSku.ProductoId == product.ProductoId)
            .ToListAsync();

        Assert.Equal(3, balances.Count);
        Assert.All(balances, balance => Assert.Equal(0, balance.ExistenciaFisica));
        Assert.Empty(await fixture.Context.MovimientosInventarioUbicacion
            .AsNoTracking()
            .Where(item => item.ProductoSkuId == balances[0].ProductoSkuId)
            .ToListAsync());
    }

    [Fact]
    public async Task UpdateAsync_DoesNotMoveExistingInitialStock()
    {
        await using var fixture = await ProductFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.CreateRequest("Producto estable", stock: 4) with
            {
                UbicacionInicial = UbicacionInventario.BODEGA
            });
        var before = await fixture.Context.InventarioSaldos
            .AsNoTracking()
            .Where(item => item.ProductoSku.ProductoId == created.ProductoId)
            .OrderBy(item => item.Ubicacion)
            .Select(item => new { item.Ubicacion, item.ExistenciaFisica, item.Reservado })
            .ToListAsync();

        await fixture.Service.UpdateAsync(new UpdateProductRequest(
            created.ProductoId,
            created.CodigoInterno,
            "Producto editado",
            created.Descripcion,
            created.CategoriaId,
            created.ImagenLocalPath,
            created.PrecioCompraCentavos,
            created.PrecioVentaCentavos,
            created.StockMinimo,
            created.Modo,
            created.SubcategoriaId));

        var after = await fixture.Context.InventarioSaldos
            .AsNoTracking()
            .Where(item => item.ProductoSku.ProductoId == created.ProductoId)
            .OrderBy(item => item.Ubicacion)
            .Select(item => new { item.Ubicacion, item.ExistenciaFisica, item.Reservado })
            .ToListAsync();

        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData(ModoProducto.SIMPLE)]
    [InlineData(ModoProducto.SURTIDO)]
    [InlineData(ModoProducto.POR_VARIANTES)]
    public async Task CreateAsync_PreservesProductModeAndAllocatesPrimarySku(
        ModoProducto mode)
    {
        await using var fixture = await ProductFixture.CreateAsync();

        var product = await fixture.Service.CreateAsync(
            fixture.CreateRequest($"Producto {mode}", stock: 2) with
            {
                Modo = mode,
                UbicacionInicial = UbicacionInventario.EXHIBICION
            });

        var stored = await fixture.Context.Productos
            .AsNoTracking()
            .Include(item => item.Skus)
                .ThenInclude(sku => sku.SaldosInventario)
            .SingleAsync(item => item.ProductoId == product.ProductoId);
        var sku = Assert.Single(stored.Skus);

        Assert.Equal(mode, stored.Modo);
        Assert.Equal(2, sku.SaldosInventario
            .Single(balance => balance.Ubicacion == UbicacionInventario.EXHIBICION)
            .ExistenciaFisica);
    }

    [Fact]
    public async Task CreateAsync_RejectsInvalidInitialLocationWithoutWriting()
    {
        await using var fixture = await ProductFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.CreateAsync(
                fixture.CreateRequest("Producto inválido", stock: 5) with
                {
                    UbicacionInicial = (UbicacionInventario)999
                }));

        Assert.Equal("UbicacionInicial", exception.ParamName);
        Assert.Empty(await fixture.Context.Productos.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Context.InventarioSaldos.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_InventoryOrAuditFailureRollsBackProductAndBalances()
    {
        await using var fixture = await ProductFixture.CreateAsync();
        var service = fixture.CreateService(new ThrowingAuditService());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(
                fixture.CreateRequest("Producto con fallo", stock: 6) with
                {
                    UbicacionInicial = UbicacionInventario.EXHIBICION
                }));

        fixture.Context.ChangeTracker.Clear();
        Assert.Empty(await fixture.Context.Productos.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Context.InventarioSaldos.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_RejectsEmptyOrWhitespaceName(string name)
    {
        await using var fixture = await ProductFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Service.CreateAsync(fixture.CreateRequest(name, stock: 0)));

        Assert.StartsWith("El nombre del producto es obligatorio.", exception.Message);
        Assert.Equal("nombre", exception.ParamName);
        Assert.Empty(await fixture.Context.Productos.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_RejectsNullName()
    {
        await using var fixture = await ProductFixture.CreateAsync();
        var request = fixture.CreateRequest("Temporal", stock: 0) with { Nombre = null! };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(request));

        Assert.StartsWith("El nombre del producto es obligatorio.", exception.Message);
        Assert.Equal("nombre", exception.ParamName);
        Assert.Empty(await fixture.Context.Productos.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(0, "El precio de compra es obligatorio.", "PrecioCompraCentavos")]
    [InlineData(1, "El precio de venta es obligatorio.", "PrecioVentaCentavos")]
    [InlineData(2, "La cantidad inicial es obligatoria.", "StockActual")]
    public async Task CreateAsync_RejectsMissingRequiredNumericFieldsBeforeWriting(
        int missingField,
        string expectedMessage,
        string expectedParameter)
    {
        await using var fixture = await ProductFixture.CreateAsync();
        var request = fixture.CreateRequest("Producto incompleto", stock: 0);
        request = missingField switch
        {
            0 => request with { PrecioCompraCentavos = null },
            1 => request with { PrecioVentaCentavos = null },
            2 => request with { StockActual = null },
            _ => throw new ArgumentOutOfRangeException(nameof(missingField))
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Service.CreateAsync(request));

        Assert.StartsWith(expectedMessage, exception.Message);
        Assert.Equal(expectedParameter, exception.ParamName);
        Assert.Empty(await fixture.Context.Productos.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(-1L, 0L, 0, "El precio de compra no puede ser negativo.")]
    [InlineData(0L, -1L, 0, "El precio de venta no puede ser negativo.")]
    [InlineData(0L, 0L, -1, "El stock actual no puede ser negativo.")]
    public async Task CreateAsync_RejectsInvalidNumericFieldsBeforeWriting(
        long purchasePrice,
        long salePrice,
        int initialStock,
        string expectedMessage)
    {
        await using var fixture = await ProductFixture.CreateAsync();
        var request = fixture.CreateRequest("Producto inválido", stock: 0) with
        {
            PrecioCompraCentavos = purchasePrice,
            PrecioVentaCentavos = salePrice,
            StockActual = initialStock
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Service.CreateAsync(request));

        Assert.StartsWith(expectedMessage, exception.Message);
        Assert.Empty(await fixture.Context.Productos.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task UpdateAsync_RejectsRemovingExistingNameAndKeepsStoredValue()
    {
        await using var fixture = await ProductFixture.CreateAsync();
        var existing = await fixture.Service.CreateAsync(fixture.CreateRequest("Producto existente", stock: 0));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.UpdateAsync(new UpdateProductRequest(
            existing.ProductoId,
            existing.CodigoInterno,
            "   ",
            existing.Descripcion,
            existing.CategoriaId,
            existing.ImagenLocalPath,
            existing.PrecioCompraCentavos,
            existing.PrecioVentaCentavos,
            existing.StockMinimo)));

        Assert.StartsWith("El nombre del producto es obligatorio.", exception.Message);
        Assert.Equal("nombre", exception.ParamName);
        fixture.Context.ChangeTracker.Clear();
        var storedName = await fixture.Context.Productos.AsNoTracking()
            .Where(product => product.ProductoId == existing.ProductoId)
            .Select(product => product.Nombre)
            .SingleAsync();
        Assert.Equal("Producto existente", storedName);
    }

    [Fact]
    public async Task CreateAsync_PreservesLeadingZerosInCanonicalSku()
    {
        await using var fixture = await ProductFixture.CreateAsync();
        var request = fixture.CreateRequest("Producto con ceros", stock: 3) with
        {
            CodigoInterno = "000042"
        };

        var product = await fixture.Service.CreateAsync(request);

        Assert.Equal("000042", product.CodigoInterno);
        Assert.Equal(
            "000042",
            await fixture.Context.ProductoSkus
                .Where(sku => sku.ProductoId == product.ProductoId)
                .Select(sku => sku.CodigoSku)
                .SingleAsync());
    }

    [Fact]
    public async Task CreateAsync_RejectsCodeAlreadyOwnedByCanonicalSku()
    {
        await using var fixture = await ProductFixture.CreateAsync();
        var original = await fixture.Service.CreateAsync(
            fixture.CreateRequest("Producto original", stock: 0) with { CodigoInterno = "SKU-ESTABLE" });
        var stored = await fixture.Context.Productos.SingleAsync(item => item.ProductoId == original.ProductoId);
        stored.CodigoInterno = "LEGACY-EDITADO";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CreateAsync(
                fixture.CreateRequest("Producto duplicado", stock: 0) with { CodigoInterno = "SKU-ESTABLE" }));

        Assert.Contains("codigo interno", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await fixture.Context.ProductoSkus.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_KeepsCanonicalSkuCodeStableWhileLegacyCodeTransitions()
    {
        await using var fixture = await ProductFixture.CreateAsync();
        var original = await fixture.Service.CreateAsync(
            fixture.CreateRequest("Producto estable", stock: 0) with { CodigoInterno = "SKU-CANONICO" });

        await fixture.Service.UpdateAsync(new UpdateProductRequest(
            original.ProductoId,
            "CODIGO-LEGADO-NUEVO",
            original.Nombre,
            original.Descripcion,
            original.CategoriaId,
            original.ImagenLocalPath,
            original.PrecioCompraCentavos,
            original.PrecioVentaCentavos,
            original.StockMinimo));

        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(
            "CODIGO-LEGADO-NUEVO",
            await fixture.Context.Productos.Select(item => item.CodigoInterno).SingleAsync());
        Assert.Equal(
            "SKU-CANONICO",
            await fixture.Context.ProductoSkus.Select(sku => sku.CodigoSku).SingleAsync());
    }

    [Fact]
    public async Task GetAllAsync_SearchesCodeNameCategoryAndSkuWithoutFallingBackOnZeroResults()
    {
        await using var fixture = await ProductFixture.CreateAsync();
        var product = await fixture.Service.CreateAsync(fixture.CreateRequest("Reze artesanal", stock: 0) with
        {
            CodigoInterno = "PRD-000364"
        });
        fixture.Context.ProductoSkus.Add(new ProductoSku
        {
            ProductoId = product.ProductoId,
            CodigoSku = "REZE-ROSA-XL",
            NombreVariante = "Rosa grande",
            Activo = true,
            EsPrincipal = false
        });
        await fixture.Context.SaveChangesAsync();
        var category = await fixture.Context.Categorias.Select(item => item.Nombre).SingleAsync();

        Assert.Single(await fixture.Service.GetAllAsync(new ProductFilter(SearchText: "000364")));
        Assert.Single(await fixture.Service.GetAllAsync(new ProductFilter(SearchText: "reze")));
        Assert.Single(await fixture.Service.GetAllAsync(new ProductFilter(SearchText: category)));
        Assert.Single(await fixture.Service.GetAllAsync(new ProductFilter(SearchText: "ROSA-XL")));
        Assert.Single(await fixture.Service.GetAllAsync(new ProductFilter(SearchText: "Rosa grande")));
        Assert.Empty(await fixture.Service.GetAllAsync(new ProductFilter(SearchText: "SIN-COINCIDENCIAS")));
    }

    [Fact]
    public async Task GetAllAsync_ProjectsBarcodeStateFromActiveSkusOnly()
    {
        await using var fixture = await ProductFixture.CreateAsync();
        var complete = await fixture.Service.CreateAsync(fixture.CreateRequest("Completo", stock: 0));
        var noBarcode = await fixture.Service.CreateAsync(fixture.CreateRequest("Sin barcode", stock: 0));
        var pending = await fixture.Service.CreateAsync(
            fixture.CreateRequest("Pendiente", stock: 0) with { Modo = ModoProducto.POR_VARIANTES });
        var sinActive = await fixture.Service.CreateAsync(
            fixture.CreateRequest("Sin activos", stock: 0) with { Modo = ModoProducto.POR_VARIANTES });

        var completeSku = await fixture.Context.ProductoSkus.SingleAsync(sku => sku.ProductoId == complete.ProductoId);
        var noBarcodeSku = await fixture.Context.ProductoSkus.SingleAsync(sku => sku.ProductoId == noBarcode.ProductoId);
        var pendingSku = await fixture.Context.ProductoSkus.SingleAsync(sku => sku.ProductoId == pending.ProductoId);
        var sinActiveSku = await fixture.Context.ProductoSkus.SingleAsync(sku => sku.ProductoId == sinActive.ProductoId);
        pendingSku.EsPrincipal = true;
        sinActiveSku.Activo = false;
        fixture.Context.ProductoSkus.Add(new ProductoSku
        {
            ProductoId = pending.ProductoId,
            CodigoSku = "PENDIENTE-2",
            Activo = true,
            EsPrincipal = false
        });
        fixture.Context.ProductoSkus.Add(new ProductoSku
        {
            ProductoId = sinActive.ProductoId,
            CodigoSku = "SIN-ACTIVO-2",
            Activo = false,
            EsPrincipal = false
        });
        fixture.Context.ProductoSkuBarcodes.AddRange(
            new ProductoSkuBarcode { ProductoSkuId = completeSku.ProductoSkuId, Codigo = "AI-COMPLETO", Activo = true },
            new ProductoSkuBarcode { ProductoSkuId = noBarcodeSku.ProductoSkuId, Codigo = "AI-INACTIVO", Activo = false },
            new ProductoSkuBarcode { ProductoSkuId = pendingSku.ProductoSkuId, Codigo = "AI-PENDIENTE", Activo = true });
        await fixture.Context.SaveChangesAsync();

        var states = (await fixture.Service.GetAllAsync()).ToDictionary(item => item.Nombre, item => item.EstadoBarcode);

        Assert.Equal(EstadoBarcodeProducto.Completo, states["Completo"]);
        Assert.Equal(EstadoBarcodeProducto.SinBarcode, states["Sin barcode"]);
        Assert.Equal(EstadoBarcodeProducto.Pendiente, states["Pendiente"]);
        Assert.Equal(EstadoBarcodeProducto.SinBarcode, states["Sin activos"]);
    }

    private sealed class ProductFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ProductSession _session;

        private ProductFixture(
            SqliteConnection connection,
            AppDbContext context,
            ProductService service,
            ProductSession session,
            int categoryId)
        {
            _connection = connection;
            _session = session;
            Context = context;
            Service = service;
            CategoryId = categoryId;
        }

        public AppDbContext Context { get; }
        public ProductService Service { get; }
        public int CategoryId { get; }

        public static async Task<ProductFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var user = new Usuario
            {
                NombreCompleto = "Administrador de pruebas",
                NombreUsuario = $"admin-product-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            var category = new Categoria
            {
                Nombre = $"Categoria-{Guid.NewGuid():N}",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.AddRange(user, category);
            await context.SaveChangesAsync();

            var session = new ProductSession(new UsuarioSesion(
                user.UsuarioId,
                user.NombreCompleto,
                user.NombreUsuario,
                user.Rol));
            var service = new ProductService(context, new AuditService(context), session);
            return new ProductFixture(connection, context, service, session, category.CategoriaId);
        }

        public CreateProductRequest CreateRequest(string name, int stock) => new(
            CodigoInterno: null,
            Nombre: name,
            Descripcion: null,
            CategoriaId: CategoryId,
            ImagenLocalPath: null,
            PrecioCompraCentavos: 0,
            PrecioVentaCentavos: 0,
            StockActual: stock,
            StockMinimo: 0);

        public ProductService CreateService(IAuditService auditService) =>
            new(Context, auditService, _session);

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class ProductSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => UsuarioActual?.Rol == RolUsuario.ADMIN;
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class ThrowingAuditService : IAuditService
    {
        public Task RegistrarAsync(
            AuditEntryRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fallo simulado de bitacora.");
    }
}
