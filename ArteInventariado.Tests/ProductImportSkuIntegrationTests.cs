using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class ProductImportSkuIntegrationTests
{
    [Fact]
    public async Task ImportProducts_CreatesSimpleStableSkuAndKeepsUnknownLocationUnassigned()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(), "ArteInventariado-ImportSkuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "import.db");
            var factory = new FileContextFactory(databasePath);
            Usuario admin;
            await using (var setup = factory.CreateDbContext())
            {
                await setup.Database.EnsureCreatedAsync();
                admin = new Usuario
                {
                    NombreUsuario = "admin-import",
                    NombreCompleto = "Admin importacion",
                    PasswordHash = "hash",
                    Rol = RolUsuario.ADMIN,
                    Activo = true,
                    FechaCreacion = DateTime.UtcNow
                };
                setup.Usuarios.Add(admin);
                await setup.SaveChangesAsync();
            }

            var session = new TestSession(new UsuarioSesion(
                admin.UsuarioId, admin.NombreCompleto, admin.NombreUsuario, admin.Rol));
            var service = new ProductImportService(
                factory,
                session,
                new AuditService(factory),
                new PrincipalInstallationContext(),
                new TestStationSettings(temporaryDirectory));
            var workbookPath = await service.CreateProductImportTemplateAsync(temporaryDirectory);
            using (var workbook = new XLWorkbook(workbookPath))
            {
                var sheet = workbook.Worksheet("Productos");
                sheet.Cell(2, 1).Value = "IMP-SKU-001";
                sheet.Cell(2, 2).Value = "Producto importado";
                sheet.Cell(2, 5).Value = 2.50m;
                sheet.Cell(2, 6).Value = 5.00m;
                sheet.Cell(2, 7).Value = 7;
                sheet.Cell(2, 8).Value = 2;
                sheet.Cell(3, 1).Value = "IMP-SKU-001";
                sheet.Cell(3, 2).Value = "Duplicado ignorado";
                workbook.Save();
            }

            var result = await service.ImportProductsAsync(workbookPath);

            Assert.Equal(1, result.ProductosImportados);
            Assert.Equal(1, result.DuplicadosIgnorados);
            await using var verification = factory.CreateDbContext();
            var product = await verification.Productos.AsNoTracking()
                .Include(item => item.Skus).ThenInclude(sku => sku.SaldosInventario)
                .SingleAsync(item => item.CodigoInterno == "IMP-SKU-001");
            Assert.Equal(ModoProducto.SIMPLE, product.Modo);
            var sku = Assert.Single(product.Skus);
            Assert.Equal(product.CodigoInterno, sku.CodigoSku);
            Assert.True(sku.EsPrincipal);
            Assert.Equal(7, sku.SaldosInventario
                .Single(balance => balance.Ubicacion == UbicacionInventario.SIN_ASIGNAR)
                .ExistenciaFisica);
            Assert.Equal(0, sku.SaldosInventario
                .Where(balance => balance.Ubicacion != UbicacionInventario.SIN_ASIGNAR)
                .Sum(balance => balance.ExistenciaFisica));
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private sealed class FileContextFactory(string databasePath) : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;

        public AppDbContext CreateDbContext() => new(_options);
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

    private sealed class PrincipalInstallationContext : IInstallationContext
    {
        public InstallationStatus Status { get; } = new(
            StationConfigurationState.Ready,
            InstallationMode.Principal,
            true,
            Guid.NewGuid(),
            1,
            "product-import-test",
            null);

        public bool AllowsOperationalWrites => true;
        public void EnsureInitialized() { }
        public void EnsureOperationalWriteAllowed(string operationName) { }
    }

    private sealed class TestStationSettings(string directory) : IStationSettingsService
    {
        public Task<LocalReportSettings> GetReportSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalReportSettings(directory, Path.Combine(directory, "settings.json")));

        public Task<string> GetBackupDirectoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(directory);

        public Task<LocalPosSettings> GetPosSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalPosSettings(false, Path.Combine(directory, "settings.json")));

        public Task UpdateReportSettingsAsync(
            UpdateLocalReportSettingsRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdatePosSettingsAsync(
            UpdateLocalPosSettingsRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
