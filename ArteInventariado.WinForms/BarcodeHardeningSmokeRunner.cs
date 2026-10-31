using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using ArteInventariado.Infrastructure.Station;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.WinForms;

/// <summary>
/// Smoke de publicación para el flujo real de RC2. Usa una SQLite temporal y
/// nunca la base de datos del negocio ni la configuración de estación local.
/// </summary>
internal static class BarcodeHardeningSmokeRunner
{
    public static int Run(string? outputDirectory)
    {
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Path.GetTempPath(), "ArteInventariado.BarcodeHardeningSmoke")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var log = new List<string> { $"START={DateTimeOffset.UtcNow:O}" };
        try
        {
            RunAsync(outputDirectory, log).GetAwaiter().GetResult();
            log.Add("RESULT=OK");
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("RESULT=FAILED");
            log.Add(ex.ToString());
            return 1;
        }
        finally
        {
            log.Add($"END={DateTimeOffset.UtcNow:O}");
            File.WriteAllLines(Path.Combine(outputDirectory, "barcode-hardening-smoke.log"), log);
        }
    }

    private static async Task RunAsync(string outputDirectory, ICollection<string> log)
    {
        var sandboxDirectory = Path.Combine(outputDirectory, $"sqlite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandboxDirectory);
        try
        {
            var factory = new SmokeDbContextFactory(Path.Combine(sandboxDirectory, "barcode-smoke.db"));
            var user = await SeedAsync(factory);
            var principal = new SmokeInstallationContext(InstallationMode.Principal);
            var service = CreateService(factory, user, principal);

            var before = await service.GetSkuCandidatesAsync();
            if (before.Count != 2 || before.Any(candidate => !candidate.EsElegibleParaGeneracionInterna))
                throw new InvalidOperationException("La preparación de SKU elegibles del smoke no fue válida.");
            log.Add("Productos/SKU elegibles=2");

            using (var selection = new GenerateMissingBarcodesForm(service, before))
            {
                Render(selection, Path.Combine(outputDirectory, "generar-codigos-faltantes.png"));
            }
            log.Add("UI Generar códigos faltantes=OK");

            var first = await service.GenerateMissingBarcodesAsync(
                new GenerateMissingBarcodesRequest([before[0].ProductoSkuId]));
            if (first.GeneratedCount != 1 || first.ErrorCount != 0 || string.IsNullOrWhiteSpace(first.Items[0].Barcode))
                throw new InvalidOperationException("La generación seleccionada no produjo exactamente un barcode.");
            log.Add("Generación selección parcial=1 OK");

            var remaining = await service.GenerateMissingBarcodesAsync(new GenerateMissingBarcodesRequest());
            if (remaining.GeneratedCount != 1 || remaining.OmittedCount != 1 || remaining.ErrorCount != 0)
                throw new InvalidOperationException("La generación de faltantes no conservó el barcode previo.");
            log.Add("Generación faltantes=1; existente omitido=1 OK");

            var generated = await service.GetSkuCandidatesAsync();
            if (generated.Any(candidate => !candidate.TieneBarcodeActivo))
                throw new InvalidOperationException("Un SKU generado quedó sin barcode activo.");
            var lookup = await service.LookupExactAsync(generated[1].BarcodeActivo!);
            if (lookup is null || lookup.ProductoSkuId != generated[1].ProductoSkuId)
                throw new InvalidOperationException("El lookup exacto del barcode generado no resolvió el SKU correcto.");
            log.Add("POS lookup exacto barcode generado=OK");

            var qr = await service.GenerateQrAsync(new GenerateProductSkuQrRequest(generated[0].ProductoSkuId));
            var qrLookup = await service.LookupExactAsync(qr.Codigo);
            if (qr.Symbology != BarcodeSymbology.Qr || qrLookup?.ProductoSkuId != generated[0].ProductoSkuId)
                throw new InvalidOperationException("El QR no resolvió exactamente el mismo SKU del catálogo.");
            log.Add("POS lookup exacto QR=OK");

            var batch = LabelDataFactory.CreateBatch(
                [
                    (generated[0], 1),
                    (generated[1], 2)
                ],
                ProductLabelProfile.Mm40x30,
                includePrice: true);
            if (batch.TotalCopies != 3)
                throw new InvalidOperationException("El lote de etiquetas no conservó su selección múltiple.");
            var renderer = new Code128BarcodeRenderer();
            var qrRenderer = new QrCodeRenderer();
            foreach (var profile in ProductLabelProfiles.All.Select(item => item.Profile))
            {
                var render = renderer.Render(generated[0].BarcodeActivo!, profile);
                if (render.PngBytes.Length < 100 || render.Value != generated[0].BarcodeActivo)
                    throw new InvalidOperationException($"No se renderizó Code 128 para el perfil {profile}.");
                log.Add($"Code128 {ProductLabelProfiles.Get(profile).DisplayName}=OK");
            }
            foreach (var profile in ProductLabelProfiles.All.Select(item => item.Profile).Where(ProductLabelProfiles.SupportsQr))
            {
                var render = qrRenderer.Render(qr.Codigo, profile);
                if (render.PngBytes.Length < 100 || render.WidthPixels != render.HeightPixels)
                    throw new InvalidOperationException($"No se renderizó QR cuadrado para el perfil {profile}.");
                log.Add($"QR {ProductLabelProfiles.Get(profile).DisplayName}=OK");
            }
            var qrRejectedForSmallProfile = false;
            try { qrRenderer.Render(qr.Codigo, ProductLabelProfile.Mm25x15); }
            catch (InvalidOperationException) { qrRejectedForSmallProfile = true; }
            if (!qrRejectedForSmallProfile)
                throw new InvalidOperationException("El perfil 25 × 15 mm aceptó QR.");
            log.Add("QR 25 × 15 bloqueado=OK");

            using (var selection = new LabelBatchSelectionForm(generated))
            {
                Render(selection, Path.Combine(outputDirectory, "seleccion-etiquetas-lote.png"));
            }
            using (var preview = new LabelPreviewForm(batch, renderer))
            {
                Render(preview, Path.Combine(outputDirectory, "preview-etiquetas-lote.png"));
                if (FindButtons(preview).Any(button => button.Text == "Imprimir etiquetas" && button.Enabled))
                    throw new InvalidOperationException("El smoke no debe habilitar una impresora física inexistente.");
            }
            var qrLabel = new LabelData(
                generated[0].ProductoId,
                generated[0].ProductoSkuId,
                generated[0].ProductoNombre,
                generated[0].CodigoSku,
                generated[0].NombreVariante,
                qr.Codigo,
                BarcodeSymbology.Qr,
                generated[0].PrecioVentaEfectivoCentavos,
                1);
            using (var preview = new LabelPreviewForm(qrLabel, renderer, ProductLabelProfile.Mm30x30, qrRenderer))
            {
                Render(preview, Path.Combine(outputDirectory, "preview-etiqueta-qr.png"));
            }
            log.Add("Selección múltiple y preview lote=OK");

            var consulta = CreateService(factory, user, new SmokeInstallationContext(InstallationMode.Consulta));
            try
            {
                await consulta.GenerateMissingBarcodesAsync(new GenerateMissingBarcodesRequest());
                throw new InvalidOperationException("CONSULTA permitió generar barcodes.");
            }
            catch (InstallationWriteDeniedException)
            {
                log.Add("CONSULTA bloqueada para escritura=OK");
            }
        }
        finally
        {
            if (Directory.Exists(sandboxDirectory))
                Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    private static BarcodeService CreateService(
        IDbContextFactory<AppDbContext> factory,
        UsuarioSesion user,
        IInstallationContext installation) => new(
            factory,
            new SmokePermissionService(),
            installation,
            new SmokeSession(user),
            new AuditService(factory));

    private static async Task<UsuarioSesion> SeedAsync(IDbContextFactory<AppDbContext> factory)
    {
        await using var context = factory.CreateDbContext();
        await context.Database.EnsureCreatedAsync();
        var user = new Usuario
        {
            NombreCompleto = "Administrador smoke barcode",
            NombreUsuario = $"smoke-barcode-{Guid.NewGuid():N}",
            PasswordHash = "smoke",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            FechaCreacion = DateTime.UtcNow
        };
        var category = new Categoria
        {
            Nombre = $"Smoke barcode {Guid.NewGuid():N}",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        context.AddRange(user, category);
        await context.SaveChangesAsync();

        var product = new Producto
        {
            CodigoInterno = $"SMOKE-{Guid.NewGuid():N}",
            Nombre = "Producto smoke barcode",
            CategoriaId = category.CategoriaId,
            Modo = ModoProducto.POR_VARIANTES,
            PrecioCompraCentavos = 500,
            PrecioVentaCentavos = 1200,
            StockActual = 0,
            Estado = EstadoProducto.ACTIVO,
            FechaCreacion = DateTime.UtcNow
        };
        product.Skus.Add(new ProductoSku
        {
            CodigoSku = $"SMOKE-A-{Guid.NewGuid():N}",
            NombreVariante = "Natural",
            Activo = true,
            EsPrincipal = true
        });
        product.Skus.Add(new ProductoSku
        {
            CodigoSku = $"SMOKE-B-{Guid.NewGuid():N}",
            NombreVariante = "Rosa",
            Activo = true,
            EsPrincipal = false
        });
        context.Productos.Add(product);
        await context.SaveChangesAsync();
        return new UsuarioSesion(user.UsuarioId, user.NombreCompleto, user.NombreUsuario, user.Rol);
    }

    private static void Render(Form form, string path)
    {
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        form.PerformLayout();
        using var bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path);
        form.Hide();
    }

    private static IEnumerable<Button> FindButtons(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button) yield return button;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
    }

    private sealed class SmokeDbContextFactory(string databasePath) : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=2;Pooling=False")
            .Options;

        public AppDbContext CreateDbContext() => new(_options);
    }

    private sealed class SmokeSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => UsuarioActual?.Rol == RolUsuario.ADMIN;
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class SmokeInstallationContext(InstallationMode mode) : IInstallationContext
    {
        public InstallationStatus Status { get; } = new(
            StationConfigurationState.Ready, mode, true, Guid.NewGuid(), 1, "barcode-smoke", null);
        public bool AllowsOperationalWrites => Status.AllowsOperationalWrites;
        public void EnsureInitialized() { }
        public void EnsureOperationalWriteAllowed(string operationName)
        {
            if (!AllowsOperationalWrites)
                throw new InstallationWriteDeniedException(InstallationContext.WriteDeniedMessage, operationName);
        }
    }

    private sealed class SmokePermissionService : IEmployeePermissionService
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
