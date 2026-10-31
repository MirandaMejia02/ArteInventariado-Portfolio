using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Updater;
using System.Reflection;

namespace ArteInventariado.Setup;

internal static class Program
{
    private static string ProductVersion =>
        typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException("Setup no declara una versión informativa.");

    private static int Main(string[] args)
    {
        try
        {
            if (args.Any(argument => string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase)))
            {
                PrintUsage();
                return 0;
            }

            if (args.Any(argument => string.Equals(argument, "--version", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"ArteInventariado Setup {ProductVersion} (self-contained win-x64)");
                return 0;
            }

            if (!MachineWideElevation.IsAdministrator())
            {
                Console.Error.WriteLine("El instalador requiere consentimiento UAC de un administrador.");
                return 2;
            }

            var command = ParseCommand(args);
            var paths = ArteInventariadoPathProvider.CreateMachineWideDefault();
            var service = new MachineWideSetupService();

            return command.Kind switch
            {
                SetupCommandKind.Install => Install(service, paths, command),
                SetupCommandKind.ApplyUpdate => ApplyUpdate(paths, command),
                SetupCommandKind.Uninstall => Uninstall(service, paths, command),
                SetupCommandKind.ResetDatabase => ResetBusinessDatabase(paths),
                SetupCommandKind.RecoverDatabaseReset => RecoverDatabaseReset(paths),
                SetupCommandKind.Exit => 0,
                _ => 2
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            Console.Error.WriteLine($"Diagnóstico: {MachineWideUpdateDiagnostics.GetLogPath(UpdateRuntimePaths.CreateMachineWideDefault())}");
            return 1;
        }
        finally
        {
            if (args.Length == 0 && !Console.IsInputRedirected)
            {
                Console.WriteLine("Presione Enter para cerrar Setup.");
                Console.ReadLine();
            }
        }
    }

    private static int ApplyUpdate(ArteInventariadoPathProvider paths, SetupCommand command)
    {
        var bundled = ResolveBundledUpdate();
        var manifestPath = command.UpdateManifestPath ?? bundled.ManifestPath;
        var packagePath = command.UpdatePackagePath ?? bundled.PackagePath;
        var phrase = $"APLICAR {ProductVersion}";

        Console.WriteLine($"Setup de recuperación ArteInventariado {ProductVersion}");
        Console.WriteLine("La actualización conservará la DB del negocio y la identidad de la estación.");
        Console.WriteLine("Se creará un PRE_UPDATE protegido antes de instalar. No es una instalación limpia.");
        Console.WriteLine($"Manifest: {Path.GetFullPath(manifestPath)}");
        Console.WriteLine($"Paquete:  {Path.GetFullPath(packagePath)}");
        Console.Write($"Para confirmar, escriba exactamente {phrase}: ");
        if (!string.Equals(Console.ReadLine()?.Trim(), phrase, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Cancelado. No se creó staging ni se modificó la instalación.");
            return 2;
        }

        var result = new MachineWideSetupUpdateService()
            .ApplyAsync(paths, manifestPath, packagePath, ProductVersion)
            .GetAwaiter()
            .GetResult();

        Console.WriteLine($"Resultado del updater: {result.InstallerResult}");
        Console.WriteLine($"Estado final: {result.FinalState}");
        Console.WriteLine($"CURRENT: {result.CurrentVersionPath}");
        Console.WriteLine($"PRE_UPDATE conservado: {result.PreUpdateBackupPath}");
        if (result.InstallerResult == 0)
        {
            Console.WriteLine("Actualización completada y versión actual confirmada. Abra ArteInventariado desde el acceso directo.");
        }
        else if (result.InstallerResult == 2)
        {
            Console.WriteLine("La instalación se revirtió; la versión anterior quedó activa y la DB fue restaurada desde PRE_UPDATE.");
        }
        else
        {
            Console.WriteLine("La recuperación no concluyó. No borre Recovery ni reinstale limpio; comparta el diagnóstico con soporte.");
        }

        return result.InstallerResult;
    }

    private static (string ManifestPath, string PackagePath) ResolveBundledUpdate()
    {
        var updateDirectory = Path.Combine(AppContext.BaseDirectory, "update");
        var manifestPath = Path.Combine(updateDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Este Setup no contiene un paquete de actualización integrado.", manifestPath);
        }

        var manifest = System.Text.Json.JsonSerializer.Deserialize<ArteInventariado.Application.Models.UpdateManifest>(
            File.ReadAllText(manifestPath),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("El manifest integrado está vacío o mal formado.");
        if (string.IsNullOrWhiteSpace(manifest.PackagePath) ||
            !string.Equals(Path.GetFileName(manifest.PackagePath), manifest.PackagePath, StringComparison.Ordinal))
        {
            throw new InvalidDataException("PackagePath del manifest integrado no es un nombre de archivo simple.");
        }

        return (manifestPath, Path.Combine(updateDirectory, manifest.PackagePath));
    }

    private static int Install(
        MachineWideSetupService service,
        ArteInventariadoPathProvider paths,
        SetupCommand command)
    {
        var payload = command.PayloadDirectory ??
            Path.Combine(AppContext.BaseDirectory, "payload");
        var request = new MachineWideSetupRequest(
            payload,
            CleanInstall: command.CleanInstall,
            ConfirmedCleanInstall: command.ConfirmedCleanInstall,
            ProvisionPrincipal: !command.DeferPrincipal,
            StartLauncherAfterInstall: !command.NoLaunch,
            ExpectedVersion: ProductVersion);

        Console.WriteLine($"ArteInventariado Setup {ProductVersion}");
        Console.WriteLine($"Payload: {Path.GetFullPath(payload)}");
        if (request.CleanInstall)
        {
            Console.WriteLine(MachineWideSetupService.CleanInstallConfirmationText);
        }

        var result = service.InstallAsync(paths, request).GetAwaiter().GetResult();
        Console.WriteLine("Instalación completada.");
        Console.WriteLine($"Versión: {result.Version}");
        Console.WriteLine($"DB nueva: {(result.DatabaseCreated ? "sí" : "no (reparación)")}");
        Console.WriteLine($"Administrador Principal: {(result.PrincipalProvisioned ? "configurado" : "pendiente")}");
        Console.WriteLine($"Estación: {(result.StationConfiguredPrincipal ? "PRINCIPAL" : "pendiente")}");
        Console.WriteLine($"BackupRunner SYSTEM: {(result.BackupTaskInstalled ? "preparado" : "no requerido")}");
        Console.WriteLine($"CloudBackupRunner SYSTEM: {(result.CloudTaskInstalled ? "preparado" : "no requerido")}");
        Console.WriteLine($"DB: {result.DatabasePath}");
        return 0;
    }

    private static int Uninstall(
        MachineWideSetupService service,
        ArteInventariadoPathProvider paths,
        SetupCommand command)
    {
        if (command.PurgeData && !command.ConfirmedCleanInstall)
        {
            RequireCleanConfirmation();
        }

        var result = service.Uninstall(paths, command.PurgeData, command.ConfirmedCleanInstall);
        Console.WriteLine("Desinstalación completada.");
        Console.WriteLine($"Instalación eliminada: {(result.InstallationRemoved ? "sí" : "no")}");
        Console.WriteLine($"Datos eliminados: {(result.DataRemoved ? "sí" : "no (conservados)")}");
        Console.WriteLine($"Datos de usuario eliminados: {(result.UserDataRemoved ? "sí" : "no (conservados)")}");
        return 0;
    }

    private static int ResetBusinessDatabase(ArteInventariadoPathProvider paths)
    {
        Console.WriteLine(MachineWideBusinessDatabaseResetService.ConfirmationText);
        Console.Write($"Escriba {MachineWideBusinessDatabaseResetService.ConfirmationPhrase} para continuar: ");
        var confirmation = Console.ReadLine();
        var result = new MachineWideBusinessDatabaseResetService()
            .ResetBusinessDatabase(paths, confirmation);
        Console.WriteLine("Reinicio de la base del negocio completado.");
        Console.WriteLine($"InstallationId conservada: {result.InstallationId:D}");
        Console.WriteLine($"Respaldo protegido: {result.BackupPath}");
        Console.WriteLine($"SHA-256: {result.BackupSha256}");
        Console.WriteLine($"Base nueva: {result.DatabasePath}");
        Console.WriteLine(result.LauncherStarted
            ? "Se abrió el launcher para iniciar la aplicación."
            : "Abra ArteInventariado desde su acceso directo cuando quiera empezar.");
        return 0;
    }

    private static int RecoverDatabaseReset(ArteInventariadoPathProvider paths)
    {
        new MachineWideBusinessDatabaseResetService().RecoverPendingReset(paths);
        Console.WriteLine("Recuperación del reinicio de DB completada. La base anterior se restauró, o el reinicio ya estaba confirmado.");
        return 0;
    }

    private static SetupCommand ParseCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return InteractiveCommand();
        }

        var clean = Has(args, "--clean-install");
        var uninstall = Has(args, "--uninstall");
        var applyUpdate = Has(args, "--apply-update");
        var resetDatabase = Has(args, "--reset-database-only");
        var recoverDatabaseReset = Has(args, "--recover-database-reset");
        var repair = Has(args, "--repair") || Has(args, "--install");
        var selectedCommands = new[] { clean, uninstall, applyUpdate, repair, resetDatabase, recoverDatabaseReset }
            .Count(value => value);
        if (selectedCommands > 1)
        {
            throw new ArgumentException("Seleccione una sola operación de Setup por ejecución.");
        }

        if (resetDatabase)
        {
            return new SetupCommand(SetupCommandKind.ResetDatabase, false, false, false, false, false, null);
        }

        if (recoverDatabaseReset)
        {
            return new SetupCommand(SetupCommandKind.RecoverDatabaseReset, false, false, false, false, false, null);
        }

        if (applyUpdate)
        {
            var bundled = ResolveBundledUpdate();
            return new SetupCommand(
                SetupCommandKind.ApplyUpdate,
                false,
                false,
                false,
                false,
                false,
                null,
                GetValue(args, "--manifest") ?? bundled.ManifestPath,
                GetValue(args, "--package") ?? bundled.PackagePath);
        }

        if (!clean && !uninstall && !repair)
        {
            throw new ArgumentException("Indique --install, --repair, --clean-install, --uninstall, --apply-update, --reset-database-only o --recover-database-reset.");
        }

        return new SetupCommand(
            uninstall ? SetupCommandKind.Uninstall : SetupCommandKind.Install,
            clean,
            Has(args, "--confirm-clean-install"),
            Has(args, "--defer-principal"),
            Has(args, "--no-launcher"),
            Has(args, "--purge-data"),
            GetValue(args, "--payload"));
    }

    private static SetupCommand InteractiveCommand()
    {
        var bundledManifest = Path.Combine(AppContext.BaseDirectory, "update", "manifest.json");
        if (File.Exists(bundledManifest))
        {
            Console.WriteLine("ArteInventariado - Setup de recuperación machine-wide");
            Console.WriteLine($"Versión del Setup: {ProductVersion}");
            Console.WriteLine("1. Aplicar la actualización integrada (conservar DB y estación)");
            Console.WriteLine("2. Salir sin cambios");
            Console.Write("Seleccione una opción: ");
            return Console.ReadLine()?.Trim() switch
            {
                "1" => new SetupCommand(SetupCommandKind.ApplyUpdate, false, false, false, false, false, null),
                "2" => new SetupCommand(SetupCommandKind.Exit, false, false, false, false, false, null),
                _ => throw new OperationCanceledException("Operación cancelada por el usuario.")
            };
        }

        Console.WriteLine("ArteInventariado - Instalador machine-wide");
        Console.WriteLine("1. Instalar o reparar (conservar DB existente)");
        Console.WriteLine("2. Instalación limpia: elimina la instalación y DB local existentes");
        Console.WriteLine("3. Desinstalar (conservar datos locales)");
        Console.WriteLine("4. Salir");
        Console.Write("Seleccione una opción: ");
        var choice = Console.ReadLine()?.Trim();
        return choice switch
        {
            "1" => new SetupCommand(SetupCommandKind.Install, false, false, false, false, false, null),
            "2" => new SetupCommand(SetupCommandKind.Install, true, ReadCleanConfirmation(), false, false, false, null),
            "3" => new SetupCommand(SetupCommandKind.Uninstall, false, false, false, false, false, null),
            "4" => new SetupCommand(SetupCommandKind.Exit, false, false, false, false, false, null),
            _ => throw new OperationCanceledException("Operación cancelada por el usuario.")
        };
    }

    private static bool ReadCleanConfirmation()
    {
        Console.WriteLine(MachineWideSetupService.CleanInstallConfirmationText);
        Console.Write("Escriba CONFIRMAR para continuar: ");
        return MachineWideSetupService.IsCleanConfirmationValid(Console.ReadLine());
    }

    private static void RequireCleanConfirmation()
    {
        Console.WriteLine(MachineWideSetupService.CleanInstallConfirmationText);
        Console.Write("Escriba CONFIRMAR para continuar: ");
        if (!MachineWideSetupService.IsCleanConfirmationValid(Console.ReadLine()))
        {
            throw new CleanInstallConfirmationRequiredException();
        }
    }

    private static bool Has(string[] args, string name) =>
        args.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

    private static string? GetValue(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = args[index + 1];
            if (value.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Falta el valor de {name}.");
            }

            return value;
        }

        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine($"ArteInventariado Setup {ProductVersion}");
        Console.WriteLine("Uso: setup.exe [--install|--repair|--clean-install|--apply-update|--reset-database-only|--recover-database-reset] [opciones]");
        Console.WriteLine("      setup.exe --uninstall [--purge-data --confirm-clean-install]");
        Console.WriteLine("Opciones:");
        Console.WriteLine("  --payload <directorio>       Payload publicado junto al setup (por defecto .\\payload)");
        Console.WriteLine("  --confirm-clean-install      Confirma el borrado explícito de instalación y DB local");
        Console.WriteLine("  --defer-principal            Deja el alta del Administrador Principal para el primer arranque");
        Console.WriteLine("  --no-launcher                No abrir Launcher al terminar");
        Console.WriteLine("  --apply-update               Aplicar el paquete update\\manifest.json integrado; pide confirmación escrita");
        Console.WriteLine("  --manifest <archivo>         Manifest alternativo para --apply-update");
        Console.WriteLine("  --package <archivo>          ZIP alternativo para --apply-update");
        Console.WriteLine("  --purge-data                 En desinstalación, borrar también ProgramData y LocalAppData");
        Console.WriteLine("  --reset-database-only        Respaldar y crear una DB de negocio nueva; conserva estación y assets");
        Console.WriteLine("  --recover-database-reset     Reanudar el rollback/limpieza de un reinicio interrumpido");
    }

    private enum SetupCommandKind
    {
        Install,
        ApplyUpdate,
        Uninstall,
        ResetDatabase,
        RecoverDatabaseReset,
        Exit
    }

    private sealed record SetupCommand(
        SetupCommandKind Kind,
        bool CleanInstall,
        bool ConfirmedCleanInstall,
        bool DeferPrincipal,
        bool NoLaunch,
        bool PurgeData,
        string? PayloadDirectory,
        string? UpdateManifestPath = null,
        string? UpdatePackagePath = null);
}
