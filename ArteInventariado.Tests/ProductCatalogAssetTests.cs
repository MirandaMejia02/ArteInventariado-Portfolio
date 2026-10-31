using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace ArteInventariado.Tests;

public sealed class ProductCatalogAssetTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task SimpleAndAssortment_KeepOneOperationalSku_AndStockUsesCanonicalBalances()
    {
        await using var fixture = await Fixture.CreateAsync();
        var simple = await fixture.CreateProductAsync("Simple", ModoProducto.SIMPLE, 4);
        var assortment = await fixture.CreateProductAsync("Surtido", ModoProducto.SURTIDO, 0);
        var first = fixture.WriteImage("first.png");
        var second = fixture.WriteImage("second.png");

        await fixture.Assets.ImportAsync(new ImportProductAssetRequest(assortment.ProductoId, first));
        await fixture.Assets.ImportAsync(new ImportProductAssetRequest(assortment.ProductoId, second));
        var stored = await fixture.Context.Productos.Include(product => product.Skus)
            .SingleAsync(product => product.ProductoId == assortment.ProductoId);
        Assert.Single(stored.Skus);
        Assert.Equal(2, await fixture.Context.ProductoAssets.CountAsync(asset => asset.ProductoId == assortment.ProductoId));

        stored = await fixture.Context.Productos.Include(product => product.Skus).ThenInclude(sku => sku.SaldosInventario)
            .SingleAsync(product => product.ProductoId == simple.ProductoId);
        stored.StockActual = 999;
        stored.Skus.Single().SaldosInventario.Single(balance => balance.Ubicacion == UbicacionInventario.EXHIBICION).ExistenciaFisica = 3;
        stored.Skus.Single().SaldosInventario.Single(balance => balance.Ubicacion == UbicacionInventario.SIN_ASIGNAR).ExistenciaFisica = 1;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var summary = (await fixture.Products.GetAllAsync()).Single(product => product.ProductoId == simple.ProductoId);
        Assert.Equal(4, summary.StockActual);
        Assert.Equal(3, summary.StockExhibicion);
        Assert.Equal(1, summary.StockSinAsignar);
    }

    [Fact]
    public async Task VariantSku_HasStableCodeAndOverrides_AndHistoryPreventsDestructiveDelete()
    {
        await using var fixture = await Fixture.CreateAsync();
        var product = await fixture.CreateProductAsync("Variantes", ModoProducto.POR_VARIANTES, 0);
        var sku = await fixture.Catalog.CreateSkuAsync(new CreateProductSkuRequest(
            product.ProductoId, "SKU-ROSA", "Rosa", 1550, 700, 3));

        var updated = await fixture.Catalog.UpdateSkuAsync(new UpdateProductSkuRequest(
            sku.ProductoSkuId, "Rosa floral", 1600, 750, 4));
        Assert.Equal("SKU-ROSA", updated.CodigoSku);
        Assert.Equal(1600, updated.PrecioVentaEfectivoCentavos);
        Assert.Equal(750, updated.CostoEfectivoCentavos);
        await fixture.Catalog.SetSkuActiveAsync(sku.ProductoSkuId, false);
        Assert.False((await fixture.Catalog.GetDetailAsync(product.ProductoId))!.Skus.Single(item => item.ProductoSkuId == sku.ProductoSkuId).Activo);
        await fixture.Catalog.SetSkuActiveAsync(sku.ProductoSkuId, true);

        fixture.Context.MovimientosInventarioUbicacion.Add(new MovimientoInventarioUbicacion
        {
            OperacionId = Guid.NewGuid(), ProductoSkuId = sku.ProductoSkuId,
            TipoOperacion = TipoOperacionInventario.AJUSTE, UbicacionDestino = UbicacionInventario.SIN_ASIGNAR,
            Cantidad = 1, UsuarioId = fixture.UserId, Motivo = "Historia", Fecha = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Catalog.DeleteSkuAsync(sku.ProductoSkuId));
        Assert.Contains("historia", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManagedGallery_SurvivesOriginalRemoval_RegeneratesThumbnail_AndSupportsSkuOwner()
    {
        await using var fixture = await Fixture.CreateAsync();
        var product = await fixture.CreateProductAsync("Galeria", ModoProducto.POR_VARIANTES, 0);
        var sku = await fixture.Catalog.CreateSkuAsync(new CreateProductSkuRequest(product.ProductoId, "SKU-AZUL", "Azul"));
        var source = fixture.WriteImage("external.png");

        var first = await fixture.Assets.ImportAsync(new ImportProductAssetRequest(product.ProductoId, source));
        var second = await fixture.Assets.ImportAsync(new ImportProductAssetRequest(product.ProductoId, source));
        var skuAsset = await fixture.Assets.ImportAsync(new ImportProductAssetRequest(product.ProductoId, source, sku.ProductoSkuId));
        File.Delete(source);
        Assert.True(File.Exists(fixture.Paths.ResolveManagedPath(first.RelativePath)));
        Assert.True(File.Exists(await fixture.Assets.GetThumbnailPathAsync(ProductAssetOwnerKind.Product, first.AssetId, 320)));
        await fixture.Assets.SetPrimaryAsync(ProductAssetOwnerKind.Product, second.AssetId);
        var gallery = await fixture.Assets.GetByProductAsync(product.ProductoId);
        Assert.True(gallery.Single(asset => asset.AssetId == second.AssetId && asset.OwnerKind == ProductAssetOwnerKind.Product).IsPrimary);
        Assert.True(gallery.Single(asset => asset.AssetId == skuAsset.AssetId && asset.OwnerKind == ProductAssetOwnerKind.Sku).IsPrimary);
    }

    [Fact]
    public async Task ImportAndThumbnail_ApplyExifOrientationAndRegenerateLegacyPreview()
    {
        await using var fixture = await Fixture.CreateAsync();
        var product = await fixture.CreateProductAsync("Foto orientada", ModoProducto.SIMPLE, 0);
        var source = fixture.WriteImage("photosync-orientation-6.jpg", CreateExifJpeg(6));

        var imported = await fixture.Assets.ImportAsync(new ImportProductAssetRequest(product.ProductoId, source));

        Assert.Equal(120, imported.Width);
        Assert.Equal(80, imported.Height);
        var managedPath = fixture.Paths.ResolveManagedPath(imported.RelativePath);
        using (var managedStream = File.OpenRead(managedPath))
        using (var managedCodec = SKCodec.Create(managedStream))
        {
            Assert.NotNull(managedCodec);
            Assert.True(managedCodec!.EncodedOrigin == SKEncodedOrigin.Default ||
                managedCodec.EncodedOrigin == SKEncodedOrigin.TopLeft);
        }
        AssertCorners(managedPath, "blue", "red", "yellow", "green");

        // Simulate an asset imported by an earlier build, which still carries its EXIF tag.
        await File.WriteAllBytesAsync(managedPath, await File.ReadAllBytesAsync(source));
        var thumbnailPath = await fixture.Assets.GetThumbnailPathAsync(
            ProductAssetOwnerKind.Product, imported.AssetId, 320);

        Assert.NotNull(thumbnailPath);
        using var thumbnail = SKBitmap.Decode(thumbnailPath);
        Assert.NotNull(thumbnail);
        Assert.Equal(120, thumbnail!.Width);
        Assert.Equal(80, thumbnail.Height);
        AssertCorners(thumbnail, "blue", "red", "yellow", "green");
    }

    [Fact]
    public async Task Import_NormalizesAllExifOrientations()
    {
        await using var fixture = await Fixture.CreateAsync();
        var expectations = new Dictionary<byte, (string TopLeft, string TopRight, string BottomLeft, string BottomRight)>
        {
            [2] = ("green", "red", "yellow", "blue"),
            [3] = ("yellow", "blue", "green", "red"),
            [4] = ("blue", "yellow", "red", "green"),
            [5] = ("red", "blue", "green", "yellow"),
            [6] = ("blue", "red", "yellow", "green"),
            [7] = ("yellow", "green", "blue", "red"),
            [8] = ("green", "yellow", "red", "blue")
        };

        foreach (var (orientation, expected) in expectations)
        {
            var product = await fixture.CreateProductAsync($"EXIF {orientation}", ModoProducto.SIMPLE, 0);
            var source = fixture.WriteImage($"orientation-{orientation}.jpg", CreateExifJpeg(orientation));
            var imported = await fixture.Assets.ImportAsync(new ImportProductAssetRequest(product.ProductoId, source));
            var swapsAxes = orientation >= 5;

            Assert.Equal(swapsAxes ? 120 : 80, imported.Width);
            Assert.Equal(swapsAxes ? 80 : 120, imported.Height);
            AssertCorners(fixture.Paths.ResolveManagedPath(imported.RelativePath),
                expected.TopLeft, expected.TopRight, expected.BottomLeft, expected.BottomRight);
        }
    }

    [Fact]
    public async Task FailedImportLeavesNoCanonicalOrphan_AndCleanupOnlyQuarantinesRecognizedOldFiles()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = fixture.WriteImage("rollback.png");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Assets.ImportAsync(
            new ImportProductAssetRequest(999999, source)));
        Assert.Empty(Directory.Exists(fixture.Paths.ProductAssetsDirectory)
            ? Directory.EnumerateFiles(fixture.Paths.ProductAssetsDirectory) : []);

        Directory.CreateDirectory(fixture.Paths.ProductAssetsDirectory);
        var orphan = Path.Combine(fixture.Paths.ProductAssetsDirectory, $"{Guid.NewGuid():N}.png");
        var unrelated = Path.Combine(fixture.Paths.ProductAssetsDirectory, "do-not-touch.png");
        await File.WriteAllBytesAsync(orphan, TinyPng);
        await File.WriteAllBytesAsync(unrelated, TinyPng);
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-2));
        var cleanup = await fixture.Assets.CleanupOrphansAsync(TimeSpan.FromHours(1));
        Assert.Equal(1, cleanup.Quarantined);
        Assert.False(File.Exists(orphan));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public async Task ConsultaCanReadButAssetWritesAreRejectedBeforeFilesystemMutation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var product = await fixture.CreateProductAsync("Consulta", ModoProducto.SIMPLE, 0);
        var source = fixture.WriteImage("consulta.png");
        var guarded = new ProductAssetService(new SharedFactory(fixture.Connection), new AllowPermissionService(),
            new ReadOnlyInstallationContext(), fixture.Session, fixture.Audit, fixture.Paths);

        Assert.Empty(await guarded.GetByProductAsync(product.ProductoId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => guarded.ImportAsync(
            new ImportProductAssetRequest(product.ProductoId, source)));
        Assert.False(Directory.Exists(fixture.Paths.ProductAssetsDirectory));
    }

    [Fact]
    public async Task ProductImagesPermission_AllowsOnlyGalleryMutations()
    {
        await using var fixture = await Fixture.CreateAsync();
        var product = await fixture.CreateProductAsync("Permiso imagenes", ModoProducto.SIMPLE, 0);
        var firstSource = fixture.WriteImage("images-only-first.png");
        var secondSource = fixture.WriteImage("images-only-second.png");
        var permissions = new PermissionSetService(PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR);
        var installation = new PrincipalInstallationContext();
        var guardedAssets = new ProductAssetService(new SharedFactory(fixture.Connection), permissions,
            installation, fixture.Session, fixture.Audit, fixture.Paths);

        var first = await guardedAssets.ImportAsync(new ImportProductAssetRequest(product.ProductoId, firstSource));
        var second = await guardedAssets.ImportAsync(new ImportProductAssetRequest(product.ProductoId, secondSource));
        await guardedAssets.SetPrimaryAsync(ProductAssetOwnerKind.Product, second.AssetId);
        await guardedAssets.DeleteAsync(ProductAssetOwnerKind.Product, second.AssetId);

        var remaining = Assert.Single(await guardedAssets.GetByProductAsync(product.ProductoId));
        Assert.Equal(first.AssetId, remaining.AssetId);
        Assert.True(remaining.IsPrimary);

        var guardedCatalog = new ProductCatalogService(new SharedFactory(fixture.Connection), fixture.Products,
            permissions, installation, fixture.Session, fixture.Audit);
        await Assert.ThrowsAsync<InvalidOperationException>(() => guardedCatalog.CreateSkuAsync(
            new CreateProductSkuRequest(product.ProductoId, "IMG-NO-SKU", "No permitido")));

        var skuId = await fixture.Context.ProductoSkus.AsNoTracking()
            .Where(sku => sku.ProductoId == product.ProductoId)
            .Select(sku => sku.ProductoSkuId)
            .SingleAsync();
        var guardedBarcodes = new BarcodeService(new SharedFactory(fixture.Connection), permissions,
            installation, fixture.Session, fixture.Audit);
        await Assert.ThrowsAsync<InvalidOperationException>(() => guardedBarcodes.GenerateInternalAsync(new(skuId)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guardedAssets.CleanupOrphansAsync(TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task ProductImagesPermission_IsStillBlockedInConsultaMode()
    {
        await using var fixture = await Fixture.CreateAsync();
        var product = await fixture.CreateProductAsync("Consulta imagenes", ModoProducto.SIMPLE, 0);
        var source = fixture.WriteImage("consulta-images-only.png");
        var guarded = new ProductAssetService(new SharedFactory(fixture.Connection),
            new PermissionSetService(PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR),
            new ReadOnlyInstallationContext(), fixture.Session, fixture.Audit, fixture.Paths);

        await Assert.ThrowsAsync<InvalidOperationException>(() => guarded.ImportAsync(
            new ImportProductAssetRequest(product.ProductoId, source)));
    }

    [Fact]
    public async Task WithoutProductOrImagesPermission_GalleryMutationIsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var product = await fixture.CreateProductAsync("Sin permiso imagenes", ModoProducto.SIMPLE, 0);
        var source = fixture.WriteImage("no-images-permission.png");
        var guarded = new ProductAssetService(new SharedFactory(fixture.Connection), new PermissionSetService(),
            new PrincipalInstallationContext(), fixture.Session, fixture.Audit, fixture.Paths);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => guarded.ImportAsync(
            new ImportProductAssetRequest(product.ProductoId, source)));

        Assert.Contains("Gestionar imágenes de productos", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(fixture.Paths.ProductAssetsDirectory));
    }

    [Fact]
    public async Task ProductManagementPermission_StillAllowsGalleryMutation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var product = await fixture.CreateProductAsync("Admin imagenes", ModoProducto.SIMPLE, 0);
        var source = fixture.WriteImage("product-management-image.png");
        var guarded = new ProductAssetService(new SharedFactory(fixture.Connection),
            new PermissionSetService(PermisoEmpleado.PRODUCTOS_GESTIONAR),
            new PrincipalInstallationContext(), fixture.Session, fixture.Audit, fixture.Paths);

        var imported = await guarded.ImportAsync(new ImportProductAssetRequest(product.ProductoId, source));

        Assert.True(imported.IsPrimary);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, AppDbContext context, ProductService products,
            ProductCatalogService catalog, ProductAssetService assets, TestSession session, AuditService audit,
            ProductAssetStoragePaths paths, int categoryId, int userId, string root)
        { _connection = connection; Context = context; Products = products; Catalog = catalog; Assets = assets;
          Session = session; Audit = audit; Paths = paths; CategoryId = categoryId; UserId = userId; Root = root; }
        public SqliteConnection Connection => _connection;
        public AppDbContext Context { get; } public ProductService Products { get; } public ProductCatalogService Catalog { get; }
        public ProductAssetService Assets { get; } public TestSession Session { get; } public AuditService Audit { get; }
        public ProductAssetStoragePaths Paths { get; } public int CategoryId { get; } public int UserId { get; } public string Root { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var user = new Usuario { NombreCompleto = "Admin", NombreUsuario = $"admin-{Guid.NewGuid():N}", PasswordHash = "hash", Rol = RolUsuario.ADMIN, Activo = true, FechaCreacion = DateTime.UtcNow };
            var category = new Categoria { Nombre = $"Cat-{Guid.NewGuid():N}", Activa = true, FechaCreacion = DateTime.UtcNow };
            context.AddRange(user, category); await context.SaveChangesAsync();
            var session = new TestSession(new UsuarioSesion(user.UsuarioId, user.NombreCompleto, user.NombreUsuario, user.Rol));
            var audit = new AuditService(context); var products = new ProductService(context, audit, session);
            var catalog = new ProductCatalogService(context, products, session, audit);
            var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado.ProductAssets.{Guid.NewGuid():N}");
            var assetsRoot = Path.Combine(root, "Assets");
            var paths = new ProductAssetStoragePaths(assetsRoot, Path.Combine(assetsRoot, "Products"), Path.Combine(assetsRoot, "Skus"),
                Path.Combine(root, "Cache"), Path.Combine(root, "Quarantine"), Path.Combine(root, "Staging"));
            var assets = new ProductAssetService(context, session, audit, paths);
            return new Fixture(connection, context, products, catalog, assets, session, audit, paths, category.CategoriaId, user.UsuarioId, root);
        }

        public Task<ProductSummary> CreateProductAsync(string name, ModoProducto mode, int stock) => Products.CreateAsync(new CreateProductRequest(
            null, name, null, CategoryId, null, 500, 1000, stock, 1, mode));
        public string WriteImage(string name, byte[]? contents = null) { Directory.CreateDirectory(Root); var path = Path.Combine(Root, name); File.WriteAllBytes(path, contents ?? TinyPng); return path; }
        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await _connection.DisposeAsync(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    private static byte[] CreateExifJpeg(byte orientation)
    {
        using var bitmap = new SKBitmap(80, 120);
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = (x < bitmap.Width / 2, y < bitmap.Height / 2) switch
            {
                (true, true) => SKColors.Red,
                (false, true) => SKColors.Green,
                (true, false) => SKColors.Blue,
                _ => SKColors.Yellow
            };
            bitmap.SetPixel(x, y, color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 100)
            ?? throw new InvalidOperationException("No se pudo crear JPEG de prueba.");
        var jpeg = encoded.ToArray();
        var exifHeader = new byte[]
        {
            0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x22,
            0x45, 0x78, 0x69, 0x66, 0x00, 0x00,
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,
            orientation, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
        var result = new byte[exifHeader.Length + jpeg.Length - 2];
        Buffer.BlockCopy(exifHeader, 0, result, 0, exifHeader.Length);
        Buffer.BlockCopy(jpeg, 2, result, exifHeader.Length, jpeg.Length - 2);
        return result;
    }

    private static void AssertDominant(SKColor color, string expected)
    {
        var isExpected = expected switch
        {
            "red" => color.Red > color.Green + 70 && color.Red > color.Blue + 70,
            "yellow" => color.Red > color.Blue + 70 && color.Green > color.Blue + 70,
            "blue" => color.Blue > color.Red + 70 && color.Blue > color.Green + 70,
            "green" => color.Green > color.Red + 70 && color.Green > color.Blue + 70,
            _ => false
        };
        Assert.True(isExpected, $"Se esperaba el canal {expected} dominante, pero se obtuvo {color}.");
    }

    private static void AssertCorners(string imagePath, string topLeft, string topRight,
        string bottomLeft, string bottomRight)
    {
        using var bitmap = SKBitmap.Decode(imagePath);
        Assert.NotNull(bitmap);
        AssertCorners(bitmap!, topLeft, topRight, bottomLeft, bottomRight);
    }

    private static void AssertCorners(SKBitmap bitmap, string topLeft, string topRight,
        string bottomLeft, string bottomRight)
    {
        AssertDominant(bitmap.GetPixel(5, 5), topLeft);
        AssertDominant(bitmap.GetPixel(bitmap.Width - 6, 5), topRight);
        AssertDominant(bitmap.GetPixel(5, bitmap.Height - 6), bottomLeft);
        AssertDominant(bitmap.GetPixel(bitmap.Width - 6, bitmap.Height - 6), bottomRight);
    }

    private sealed class TestSession(UsuarioSesion user) : ICurrentSession
    { public UsuarioSesion? UsuarioActual { get; private set; } = user; public bool EstaAutenticado => true; public bool EsAdmin => true;
      public bool TieneRol(RolUsuario rol) => rol == RolUsuario.ADMIN; public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario; public void CerrarSesion() => UsuarioActual = null; }
    private sealed class SharedFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        public ValueTask<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(CreateDbContext());
    }
    private sealed class ReadOnlyInstallationContext : IInstallationContext
    {
        public InstallationStatus Status { get; } = new(StationConfigurationState.Ready, InstallationMode.Consulta, true, Guid.NewGuid(), 1, "test", null);
        public bool AllowsOperationalWrites => false; public void EnsureInitialized() { }
        public void EnsureOperationalWriteAllowed(string operationName) => throw new InvalidOperationException("Modo Consulta: operacion no permitida.");
    }
    private sealed class PrincipalInstallationContext : IInstallationContext
    {
        public InstallationStatus Status { get; } = new(StationConfigurationState.Ready, InstallationMode.Principal,
            true, Guid.NewGuid(), 1, "product-images-test", null);
        public bool AllowsOperationalWrites => true;
        public void EnsureInitialized() { }
        public void EnsureOperationalWriteAllowed(string operationName) { }
    }
    private sealed class PermissionSetService(params PermisoEmpleado[] enabled) : IEmployeePermissionService
    {
        private readonly HashSet<PermisoEmpleado> _enabled = enabled.ToHashSet();
        public Task<bool> HasPermissionAsync(PermisoEmpleado permiso, int? usuarioId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(_enabled.Contains(permiso));
        public Task<bool> HasAnyPermissionAsync(IReadOnlyCollection<PermisoEmpleado> permisos, int? usuarioId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(permisos.Any(_enabled.Contains));
        public Task EnsurePermissionAsync(PermisoEmpleado permiso, CancellationToken cancellationToken = default) =>
            _enabled.Contains(permiso)
                ? Task.CompletedTask
                : Task.FromException(new InvalidOperationException($"No tiene permiso para: {permiso}."));
        public Task<IReadOnlyList<EmployeePermissionState>> GetPermissionsForUserAsync(int usuarioId,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EmployeePermissionState>>([]);
        public Task UpdatePermissionsAsync(UpdateEmployeePermissionsRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreRecommendedTemplateAsync(int usuarioId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ApplySafeOperationalTemplateAsync(int usuarioId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureRecommendedPermissionsForEmployeeAsync(int usuarioId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class AllowPermissionService : IEmployeePermissionService
    {
        public Task<bool> HasPermissionAsync(PermisoEmpleado permiso, int? usuarioId = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> HasAnyPermissionAsync(IReadOnlyCollection<PermisoEmpleado> permisos, int? usuarioId = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task EnsurePermissionAsync(PermisoEmpleado permiso, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<EmployeePermissionState>> GetPermissionsForUserAsync(int usuarioId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EmployeePermissionState>>([]);
        public Task UpdatePermissionsAsync(UpdateEmployeePermissionsRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreRecommendedTemplateAsync(int usuarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ApplySafeOperationalTemplateAsync(int usuarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureRecommendedPermissionsForEmployeeAsync(int usuarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
