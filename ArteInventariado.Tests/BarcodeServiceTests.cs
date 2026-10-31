using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class BarcodeServiceTests
{
    [Fact]
    public async Task SkuWithoutBarcode_IsValidAndReturnsEmptyCollection()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Empty(await fixture.Service.GetBySkuAsync(fixture.PrimarySkuId));
        Assert.Null(await fixture.Service.LookupExactAsync("NO-EXISTE"));
        Assert.Null(await fixture.Service.LookupExactAsync("   "));
    }

    [Theory]
    [InlineData("0000012345678")]
    [InlineData("AbC-09.z_7")]
    public async Task AssignAndLookup_PreserveExactText(string code)
    {
        await using var fixture = await Fixture.CreateAsync();

        var assigned = await fixture.Service.AssignAsync(new(
            fixture.PrimarySkuId,
            $"  {code}  ",
            EsPrincipal: true));
        var found = await fixture.Service.LookupExactAsync(code);

        Assert.Equal(code, assigned.Codigo);
        Assert.NotNull(found);
        Assert.Equal(code, found.Barcode);
        Assert.Equal(fixture.PrimarySkuId, found.ProductoSkuId);
        Assert.Equal(3, found.DisponibleExhibicion);
        Assert.Equal(3, found.DisponibleBodega);
        Assert.Equal(2, found.DisponibleSinAsignar);
        if (code.Any(char.IsLetter))
        {
            Assert.Null(await fixture.Service.LookupExactAsync(code.SwapCase()));
        }
    }

    [Fact]
    public async Task DuplicateCode_IsRejectedByServiceAndDatabaseGlobalUniqueIndex()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.AssignAsync(new(fixture.PrimarySkuId, "GLOBAL-001"));

        var serviceError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.AssignAsync(new(fixture.SecondarySkuId, "GLOBAL-001")));
        Assert.Contains("ya esta asignado", serviceError.Message, StringComparison.OrdinalIgnoreCase);

        fixture.Context.ProductoSkuBarcodes.Add(new ProductoSkuBarcode
        {
            ProductoSkuId = fixture.SecondarySkuId,
            Codigo = "GLOBAL-001",
            Activo = true
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task MultipleCodesPerSku_AreAllowedAndPrincipalRemainsUnique()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.AssignAsync(new(fixture.PrimarySkuId, "COD-A", EsPrincipal: true));
        var second = await fixture.Service.AssignAsync(new(fixture.PrimarySkuId, "COD-B"));
        var third = await fixture.Service.AssignAsync(new(fixture.PrimarySkuId, "COD-C"));

        await fixture.Service.SetPrincipalAsync(second.ProductoSkuBarcodeId);
        var codes = await fixture.Service.GetBySkuAsync(fixture.PrimarySkuId);

        Assert.Equal(3, codes.Count);
        Assert.False(codes.Single(code => code.ProductoSkuBarcodeId == first.ProductoSkuBarcodeId).EsPrincipal);
        Assert.True(codes.Single(code => code.ProductoSkuBarcodeId == second.ProductoSkuBarcodeId).EsPrincipal);
        Assert.False(codes.Single(code => code.ProductoSkuBarcodeId == third.ProductoSkuBarcodeId).EsPrincipal);
        Assert.Single(codes, code => code.Activo && code.EsPrincipal);
        Assert.True(await fixture.Context.Bitacora.AnyAsync(entry =>
            entry.TipoAccion == TipoBitacora.PRODUCTO_BARCODE_PRINCIPAL_MODIFICADO));
    }

    [Fact]
    public async Task DeactivatingPrincipal_RemovesItFromLookupAndClearsPrincipalFlag()
    {
        await using var fixture = await Fixture.CreateAsync();
        var assigned = await fixture.Service.AssignAsync(new(
            fixture.PrimarySkuId,
            "ACTIVE-001",
            EsPrincipal: true));

        await fixture.Service.SetActiveAsync(assigned.ProductoSkuBarcodeId, false);

        Assert.Null(await fixture.Service.LookupExactAsync("ACTIVE-001"));
        var inactive = Assert.Single(await fixture.Service.GetBySkuAsync(fixture.PrimarySkuId));
        Assert.False(inactive.Activo);
        Assert.False(inactive.EsPrincipal);

        await fixture.Service.SetActiveAsync(assigned.ProductoSkuBarcodeId, true);
        Assert.NotNull(await fixture.Service.LookupExactAsync("ACTIVE-001"));
        Assert.False(Assert.Single(await fixture.Service.GetBySkuAsync(fixture.PrimarySkuId)).EsPrincipal);
        Assert.True(await fixture.Context.Bitacora.AnyAsync(entry =>
            entry.TipoAccion == TipoBitacora.PRODUCTO_BARCODE_ESTADO_MODIFICADO));
    }

    [Fact]
    public async Task LookupRejectsInactiveSkuAndInactiveProduct()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.AssignAsync(new(fixture.PrimarySkuId, "LIFECYCLE-001"));

        var sku = await fixture.Context.ProductoSkus.SingleAsync(item => item.ProductoSkuId == fixture.PrimarySkuId);
        sku.Activo = false;
        await fixture.Context.SaveChangesAsync();
        Assert.Null(await fixture.Service.LookupExactAsync("LIFECYCLE-001"));

        sku.Activo = true;
        var product = await fixture.Context.Productos.SingleAsync(item => item.ProductoId == fixture.ProductId);
        product.Estado = EstadoProducto.INACTIVO;
        await fixture.Context.SaveChangesAsync();
        Assert.Null(await fixture.Service.LookupExactAsync("LIFECYCLE-001"));
    }

    [Fact]
    public async Task InternalGeneration_IsCode128SafeAndDoesNotCollide()
    {
        await using var fixture = await Fixture.CreateAsync();
        var generated = new List<ProductSkuBarcodeSummary>();

        for (var index = 0; index < 32; index++)
        {
            generated.Add(await fixture.Service.GenerateInternalAsync(new(fixture.PrimarySkuId)));
        }

        Assert.Equal(32, generated.Select(item => item.Codigo).Distinct(StringComparer.Ordinal).Count());
        Assert.All(generated, item => Assert.Matches("^AI[0-9]{10}$", item.Codigo));
        foreach (var item in generated)
        {
            Assert.NotNull(await fixture.Service.LookupExactAsync(item.Codigo));
        }
        Assert.Equal(32, await fixture.Context.Bitacora.CountAsync(entry =>
            entry.TipoAccion == TipoBitacora.PRODUCTO_BARCODE_GENERADO));
    }

    [Fact]
    public async Task QrAndCode128_ResolveToTheSameSkuWithoutChangingTheSalesBoundary()
    {
        await using var fixture = await Fixture.CreateAsync();
        var code128 = await fixture.Service.AssignAsync(new(
            fixture.PrimarySkuId, "CODE128-SKU-PRIMARY", EsPrincipal: true));
        var qr = await fixture.Service.GenerateQrAsync(new(
            fixture.PrimarySkuId, EsPrincipal: false));

        var fromCode128 = await fixture.Service.LookupExactAsync(code128.Codigo);
        var fromQr = await fixture.Service.LookupExactAsync(qr.Codigo);

        Assert.Equal(BarcodeSymbology.Code128, code128.Symbology);
        Assert.Equal(BarcodeSymbology.Qr, qr.Symbology);
        Assert.Equal($"AI:SKU:SKU-PRIMARY", qr.Codigo);
        Assert.NotNull(fromCode128);
        Assert.NotNull(fromQr);
        Assert.Equal(fixture.PrimarySkuId, fromCode128!.ProductoSkuId);
        Assert.Equal(fromCode128.ProductoSkuId, fromQr!.ProductoSkuId);
    }

    [Fact]
    public async Task QrGeneration_IsIdempotentAndRemainsGloballyUnambiguous()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Service.GenerateQrAsync(new(fixture.PrimarySkuId));
        var second = await fixture.Service.GenerateQrAsync(new(fixture.PrimarySkuId));
        var duplicate = await fixture.Service.AssignAsync(new(
            fixture.SecondarySkuId, "AI:SKU:SKU-SECONDARY", Symbology: BarcodeSymbology.Qr));

        Assert.Equal(first.ProductoSkuBarcodeId, second.ProductoSkuBarcodeId);
        Assert.Equal(BarcodeSymbology.Qr, duplicate.Symbology);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.AssignAsync(new(
            fixture.PrimarySkuId, duplicate.Codigo, Symbology: BarcodeSymbology.Qr)).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task GenerateMissingBarcodes_GeneratesOnlySelectedEligibleSkusAndPreservesExistingCodes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var thirdSkuId = await fixture.AddSkuAsync("SKU-THIRD", "Tercera");
        var manual = await fixture.Service.AssignAsync(new(fixture.PrimarySkuId, "MANUAL-FABRICANTE", EsPrincipal: true));

        var result = await fixture.Service.GenerateMissingBarcodesAsync(
            new GenerateMissingBarcodesRequest([fixture.SecondarySkuId]));

        var item = Assert.Single(result.Items);
        Assert.Equal(fixture.SecondarySkuId, item.Candidate.ProductoSkuId);
        Assert.Equal(MissingBarcodeGenerationStatus.Generated, item.Status);
        Assert.Matches("^AI[0-9]{10}$", item.Barcode);
        Assert.Equal(1, result.GeneratedCount);
        Assert.Empty(await fixture.Service.GetBySkuAsync(thirdSkuId));
        var primaryCodes = await fixture.Service.GetBySkuAsync(fixture.PrimarySkuId);
        Assert.Single(primaryCodes);
        Assert.Equal(manual.Codigo, primaryCodes.Single().Codigo);
        var generated = Assert.Single(await fixture.Service.GetBySkuAsync(fixture.SecondarySkuId));
        Assert.True(generated.Activo);
        Assert.True(generated.EsPrincipal);
        Assert.NotNull(await fixture.Service.LookupExactAsync(item.Barcode!));
        Assert.Contains(await fixture.Context.Bitacora.ToListAsync(), entry =>
            entry.TipoAccion == TipoBitacora.PRODUCTO_BARCODE_GENERADO &&
            entry.Descripcion.Contains("masivamente", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateMissingBarcodes_ConservativelyOmitsInactiveBarcodeHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var historical = await fixture.Service.AssignAsync(new(fixture.SecondarySkuId, "HISTORICO-001", EsPrincipal: true));
        await fixture.Service.SetActiveAsync(historical.ProductoSkuBarcodeId, false);

        var result = await fixture.Service.GenerateMissingBarcodesAsync(
            new GenerateMissingBarcodesRequest([fixture.SecondarySkuId]));

        var item = Assert.Single(result.Items);
        Assert.Equal(MissingBarcodeGenerationStatus.Omitted, item.Status);
        Assert.Contains("histórico", item.Detail, StringComparison.OrdinalIgnoreCase);
        var barcode = Assert.Single(await fixture.Service.GetBySkuAsync(fixture.SecondarySkuId));
        Assert.Equal("HISTORICO-001", barcode.Codigo);
        Assert.False(barcode.Activo);
    }

    [Fact]
    public async Task GenerateMissingBarcodes_RetriesControlledCollisionWithoutFailingTheSku()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string colliding = "AI0000000000";
        const string generated = "AI1111111111";
        await fixture.Service.AssignAsync(new(fixture.PrimarySkuId, colliding));
        var values = new Queue<string>([colliding, generated]);
        var retrying = fixture.CreateService(
            new TestInstallationContext(InstallationMode.Principal),
            codeGenerator: () => values.Dequeue());

        var result = await retrying.GenerateMissingBarcodesAsync(
            new GenerateMissingBarcodesRequest([fixture.SecondarySkuId]));

        var item = Assert.Single(result.Items);
        Assert.Equal(MissingBarcodeGenerationStatus.Generated, item.Status);
        Assert.Equal(generated, item.Barcode);
        Assert.NotNull(await fixture.Service.LookupExactAsync(generated));
    }

    [Fact]
    public async Task GenerateMissingBarcodes_ReportsPartialErrorsWithoutRollingBackOtherSkus()
    {
        await using var fixture = await Fixture.CreateAsync();
        var thirdSkuId = await fixture.AddSkuAsync("SKU-PARTIAL", "Parcial");
        var calls = 0;
        var service = fixture.CreateService(
            new TestInstallationContext(InstallationMode.Principal),
            codeGenerator: () => ++calls == 1
                ? "AI2222222222"
                : throw new InvalidOperationException("fallo controlado"));

        var result = await service.GenerateMissingBarcodesAsync(
            new GenerateMissingBarcodesRequest([fixture.SecondarySkuId, thirdSkuId]));

        Assert.Equal(1, result.GeneratedCount);
        Assert.Equal(1, result.ErrorCount);
        var generatedAcrossBoth = (await fixture.Service.GetBySkuAsync(fixture.SecondarySkuId)).Count +
            (await fixture.Service.GetBySkuAsync(thirdSkuId)).Count;
        Assert.Equal(1, generatedAcrossBoth);
    }

    [Fact]
    public async Task GenerateMissingBarcodes_ReportsExistingCodesAsOmittedWhenThereAreNoEligibleSkus()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.AssignAsync(new(fixture.PrimarySkuId, "ACTIVE-PRIMARY"));
        await fixture.Service.AssignAsync(new(fixture.SecondarySkuId, "ACTIVE-SECONDARY"));

        var result = await fixture.Service.GenerateMissingBarcodesAsync(new GenerateMissingBarcodesRequest());

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(0, result.GeneratedCount);
        Assert.Equal(2, result.OmittedCount);
        Assert.All(result.Items, item => Assert.Equal(MissingBarcodeGenerationStatus.Omitted, item.Status));
    }

    [Fact]
    public async Task ConsultaCanLookupButCannotMutateBarcodeConfiguration()
    {
        await using var fixture = await Fixture.CreateAsync();
        var assigned = await fixture.Service.AssignAsync(new(fixture.PrimarySkuId, "READ-ONLY-001"));
        var consulta = fixture.CreateService(new TestInstallationContext(InstallationMode.Consulta));

        Assert.NotNull(await consulta.LookupExactAsync("READ-ONLY-001"));
        Assert.Single(await consulta.GetBySkuAsync(fixture.PrimarySkuId));
        await Assert.ThrowsAsync<InstallationWriteDeniedException>(() =>
            consulta.AssignAsync(new(fixture.PrimarySkuId, "BLOCKED-001")));
        await Assert.ThrowsAsync<InstallationWriteDeniedException>(() =>
            consulta.GenerateInternalAsync(new(fixture.PrimarySkuId)));
        await Assert.ThrowsAsync<InstallationWriteDeniedException>(() =>
            consulta.GenerateMissingBarcodesAsync(new GenerateMissingBarcodesRequest()));
        await Assert.ThrowsAsync<InstallationWriteDeniedException>(() =>
            consulta.SetPrincipalAsync(assigned.ProductoSkuBarcodeId));
        await Assert.ThrowsAsync<InstallationWriteDeniedException>(() =>
            consulta.SetActiveAsync(assigned.ProductoSkuBarcodeId, false));
        Assert.Single(await fixture.Service.GetBySkuAsync(fixture.PrimarySkuId));
    }

    [Fact]
    public async Task WriteRequiresProductsManagePermission()
    {
        await using var fixture = await Fixture.CreateAsync();
        var denied = fixture.CreateService(
            new TestInstallationContext(InstallationMode.Principal),
            allowWrites: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            denied.AssignAsync(new(fixture.PrimarySkuId, "NO-PERMISSION")));

        Assert.Contains(nameof(PermisoEmpleado.PRODUCTOS_GESTIONAR), error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            denied.GenerateMissingBarcodesAsync(new GenerateMissingBarcodesRequest()));
        Assert.Empty(await fixture.Service.GetBySkuAsync(fixture.PrimarySkuId));
    }

    [Fact]
    public async Task LabelsPermissionCanGenerateWithoutProductManagement()
    {
        await using var fixture = await Fixture.CreateAsync();
        var labelsOnly = fixture.CreateService(
            new TestInstallationContext(InstallationMode.Principal),
            allowWrites: false,
            allowLabels: true);

        var generated = await labelsOnly.GenerateInternalAsync(new(fixture.SecondarySkuId));

        Assert.Matches("^AI[0-9]{10}$", generated.Codigo);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            labelsOnly.AssignAsync(new(fixture.SecondarySkuId, "MANUAL-LABELS-ONLY")));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly TestSession _session;
        private readonly AuditService _audit;

        private Fixture(
            SqliteConnection connection,
            AppDbContext context,
            TestSession session,
            AuditService audit,
            BarcodeService service,
            int productId,
            int primarySkuId,
            int secondarySkuId)
        {
            _connection = connection;
            Context = context;
            _session = session;
            _audit = audit;
            Service = service;
            ProductId = productId;
            PrimarySkuId = primarySkuId;
            SecondarySkuId = secondarySkuId;
        }

        public AppDbContext Context { get; }
        public BarcodeService Service { get; }
        public int ProductId { get; }
        public int PrimarySkuId { get; }
        public int SecondarySkuId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();

            var user = new Usuario
            {
                NombreCompleto = "Administrador barcode",
                NombreUsuario = $"admin-barcode-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            var category = new Categoria
            {
                Nombre = $"Barcode-{Guid.NewGuid():N}",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.AddRange(user, category);
            await context.SaveChangesAsync();

            var product = new Producto
            {
                CodigoInterno = "PRD-BARCODE",
                Nombre = "Producto con barcode",
                CategoriaId = category.CategoriaId,
                Modo = ModoProducto.POR_VARIANTES,
                PrecioCompraCentavos = 700,
                PrecioVentaCentavos = 1500,
                StockActual = 13,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow
            };
            var primary = BuildSku(product, "SKU-PRIMARY", "Principal", primary: true, price: 1700, cost: 800);
            var secondary = BuildSku(product, "SKU-SECONDARY", "Secundaria", primary: false, price: null, cost: null);
            AddBalances(primary, exhibition: 5, exhibitionReserved: 2, warehouse: 4, warehouseReserved: 1, unassigned: 2);
            AddBalances(secondary, exhibition: 2, exhibitionReserved: 0, warehouse: 0, warehouseReserved: 0, unassigned: 0);
            product.Skus.Add(primary);
            product.Skus.Add(secondary);
            context.Productos.Add(product);
            await context.SaveChangesAsync();

            var session = new TestSession(new UsuarioSesion(
                user.UsuarioId,
                user.NombreCompleto,
                user.NombreUsuario,
                user.Rol));
            var audit = new AuditService(context);
            var service = new BarcodeService(
                context,
                new TestPermissionService(allowWrites: true),
                new TestInstallationContext(InstallationMode.Principal),
                session,
                audit);
            return new Fixture(
                connection,
                context,
                session,
                audit,
                service,
                product.ProductoId,
                primary.ProductoSkuId,
                secondary.ProductoSkuId);
        }

        public BarcodeService CreateService(
            IInstallationContext installation,
            bool allowWrites = true,
            bool allowLabels = false,
            Func<string>? codeGenerator = null) =>
            new(Context, new TestPermissionService(allowWrites, allowLabels), installation, _session, _audit, codeGenerator);

        public async Task<int> AddSkuAsync(string code, string variant)
        {
            var sku = new ProductoSku
            {
                ProductoId = ProductId,
                CodigoSku = code,
                NombreVariante = variant,
                Activo = true,
                EsPrincipal = false
            };
            Context.ProductoSkus.Add(sku);
            await Context.SaveChangesAsync();
            return sku.ProductoSkuId;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static ProductoSku BuildSku(
            Producto product,
            string code,
            string variant,
            bool primary,
            long? price,
            long? cost) =>
            new()
            {
                Producto = product,
                CodigoSku = code,
                NombreVariante = variant,
                Activo = true,
                EsPrincipal = primary,
                PrecioVentaOverrideCentavos = price,
                CostoOverrideCentavos = cost
            };

        private static void AddBalances(
            ProductoSku sku,
            int exhibition,
            int exhibitionReserved,
            int warehouse,
            int warehouseReserved,
            int unassigned)
        {
            sku.SaldosInventario.Add(new InventarioSaldo
            {
                ProductoSku = sku,
                Ubicacion = UbicacionInventario.EXHIBICION,
                ExistenciaFisica = exhibition,
                Reservado = exhibitionReserved,
                Version = 1
            });
            sku.SaldosInventario.Add(new InventarioSaldo
            {
                ProductoSku = sku,
                Ubicacion = UbicacionInventario.BODEGA,
                ExistenciaFisica = warehouse,
                Reservado = warehouseReserved,
                Version = 1
            });
            sku.SaldosInventario.Add(new InventarioSaldo
            {
                ProductoSku = sku,
                Ubicacion = UbicacionInventario.SIN_ASIGNAR,
                ExistenciaFisica = unassigned,
                Reservado = 0,
                Version = 1
            });
        }
    }

    private sealed class TestSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => UsuarioActual?.Rol == RolUsuario.ADMIN;
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class TestInstallationContext(InstallationMode mode) : IInstallationContext
    {
        public InstallationStatus Status { get; } = new(
            StationConfigurationState.Ready,
            mode,
            true,
            Guid.NewGuid(),
            1,
            "test",
            null);

        public bool AllowsOperationalWrites => Status.AllowsOperationalWrites;

        public void EnsureInitialized()
        {
        }

        public void EnsureOperationalWriteAllowed(string operationName)
        {
            if (!AllowsOperationalWrites)
            {
                throw new InstallationWriteDeniedException(
                    InstallationContext.WriteDeniedMessage,
                    operationName);
            }
        }
    }

    private sealed class TestPermissionService(bool allowWrites, bool allowLabels = false) : IEmployeePermissionService
    {
        public Task<bool> HasPermissionAsync(
            PermisoEmpleado permiso,
            int? usuarioId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(permiso == PermisoEmpleado.ETIQUETAS_GESTIONAR ? allowLabels : allowWrites);

        public Task<bool> HasAnyPermissionAsync(
            IReadOnlyCollection<PermisoEmpleado> permisos,
            int? usuarioId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(permisos.Any(permiso =>
                permiso == PermisoEmpleado.ETIQUETAS_GESTIONAR ? allowLabels : allowWrites));

        public Task EnsurePermissionAsync(
            PermisoEmpleado permiso,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(PermisoEmpleado.PRODUCTOS_GESTIONAR, permiso);
            return allowWrites
                ? Task.CompletedTask
                : Task.FromException(new InvalidOperationException($"Permiso requerido: {permiso}."));
        }

        public Task<IReadOnlyList<EmployeePermissionState>> GetPermissionsForUserAsync(
            int usuarioId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmployeePermissionState>>([]);

        public Task UpdatePermissionsAsync(
            UpdateEmployeePermissionsRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RestoreRecommendedTemplateAsync(
            int usuarioId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ApplySafeOperationalTemplateAsync(
            int usuarioId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EnsureRecommendedPermissionsForEmployeeAsync(
            int usuarioId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

internal static class BarcodeTestStringExtensions
{
    internal static string SwapCase(this string value) =>
        new(value.Select(character => char.IsUpper(character)
            ? char.ToLowerInvariant(character)
            : char.IsLower(character)
                ? char.ToUpperInvariant(character)
                : character).ToArray());
}
