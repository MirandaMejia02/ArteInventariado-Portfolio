using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ArteInventariado.Infrastructure.Updater;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.CloudBackup;

namespace ArteInventariado.WinForms;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        GlobalExceptionHandler.Register();

        if (Environment.GetEnvironmentVariable("ARTE_FULLSCREEN_SMOKE") == "1")
        {
            Environment.ExitCode = FullScreenSmokeRunner.Run(
                Environment.GetEnvironmentVariable("ARTE_FULLSCREEN_SMOKE_OUTPUT"));
            return;
        }

        if (Environment.GetEnvironmentVariable("ARTE_PRODUCT_LAYOUT_SMOKE") == "1")
        {
            Environment.ExitCode = ProductModuleLayoutSmokeRunner.Run(
                Environment.GetEnvironmentVariable("ARTE_PRODUCT_LAYOUT_SMOKE_OUTPUT"));
            return;
        }

        if (Environment.GetEnvironmentVariable("ARTE_PRODUCT_SUBCATEGORY_SMOKE") == "1")
        {
            Environment.ExitCode = ProductSubcategorySmokeRunner.Run(
                Environment.GetEnvironmentVariable("ARTE_PRODUCT_SUBCATEGORY_SMOKE_OUTPUT"));
            return;
        }

        if (Environment.GetEnvironmentVariable("ARTE_POS_BARCODE_SMOKE") == "1")
        {
            Environment.ExitCode = PosBarcodeSmokeRunner.Run(
                Environment.GetEnvironmentVariable("ARTE_POS_BARCODE_SMOKE_OUTPUT"));
            return;
        }

        if (Environment.GetEnvironmentVariable("ARTE_ACCOUNT_PAYMENT_LAYOUT_SMOKE") == "1")
        {
            Environment.ExitCode = AccountPaymentLayoutSmokeRunner.Run();
            return;
        }

        var visualDev = ArteInventariadoPathProvider.IsVisualDev;
        var provisionCloud = args.Any(argument =>
            string.Equals(argument, "--provision-cloud", StringComparison.OrdinalIgnoreCase));
        var instanceMutexName = provisionCloud
            ? "Global\\ArteInventariado.WinForms.CloudProvisioning"
            : visualDev
                ? "Global\\ArteInventariado.WinForms.VisualDev"
                : ApplicationInstanceMutex.InteractiveMutexName;
        bool isFirstInstance;
        using var instanceMutex = provisionCloud || visualDev
            ? new Mutex(initiallyOwned: true, name: instanceMutexName, createdNew: out isFirstInstance)
            : ApplicationInstanceMutex.CreateInteractive(initiallyOwned: true, out isFirstInstance);
        if (!isFirstInstance)
        {
            InteractiveInstanceActivation.TryActivateExisting(visualDev);
            return;
        }

        var updateVerification = !visualDev &&
            Environment.GetEnvironmentVariable("ARTE_UPDATE_VERIFY") == "1";
        var provisionPrincipal = args.Any(argument =>
            string.Equals(argument, "--provision-principal", StringComparison.OrdinalIgnoreCase));
        var databaseResetProvisionArgument = GetArgumentValue(args, "--database-reset-provision");
        Guid? databaseResetOperationId = null;
        if (!string.IsNullOrWhiteSpace(databaseResetProvisionArgument))
        {
            if (!Guid.TryParseExact(databaseResetProvisionArgument, "D", out var parsedOperationId) ||
                parsedOperationId == Guid.Empty ||
                !provisionPrincipal)
            {
                MessageBox.Show(
                    "La autorización temporal para configurar el administrador no es válida.",
                    "Reinicio protegido",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            databaseResetOperationId = parsedOperationId;
        }
        var h2Recovery = args.Any(argument =>
            string.Equals(argument, "--h2-recovery", StringComparison.OrdinalIgnoreCase));

        UpdateRuntimePaths? startupPaths = null;
        try
        {
            startupPaths = UpdateRuntimePaths.CreateFromArguments(args);
            if (startupPaths.PathProvider is { IsMachineWide: true } machineWidePaths)
            {
                var resetPending = MachineWideDatabaseResetJournalStore.HasPending(machineWidePaths);
                var validResetProvision = databaseResetOperationId is { } operationId &&
                    MachineWideDatabaseResetJournalStore.IsProvisioningRequest(machineWidePaths, operationId);
                if (resetPending && !validResetProvision || databaseResetOperationId.HasValue && !validResetProvision)
                {
                    MessageBox.Show(
                        "Hay un reinicio de base de datos pendiente. El uso normal está bloqueado; ejecute Setup --recover-database-reset o complete el flujo protegido.",
                        "Reinicio de base de datos pendiente",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }
            else if (databaseResetOperationId.HasValue)
            {
                MessageBox.Show(
                    "El aprovisionamiento especial solo se admite en una instalación machine-wide protegida.",
                    "Reinicio protegido",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!visualDev && !databaseResetOperationId.HasValue &&
                (UpdateLauncher.TryLaunchRecoveryIfRequired(args) ||
                 UpdateLauncher.TryRedirectToCurrentVersion(args)))
            {
                return;
            }

            if (!visualDev && !updateVerification && !databaseResetOperationId.HasValue)
            {
                var bootstrap = new StableDeploymentBootstrap(startupPaths);
                var result = bootstrap.EnsureInstalled(AppContext.BaseDirectory);
                if (result.InstalledInitialVersion)
                {
                    var startInfo = new ProcessStartInfo(result.LauncherPath)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(result.LauncherPath)!
                    };
                    startupPaths.AddLayoutArguments(startInfo);
                    _ = Process.Start(startInfo);
                    return;
                }
            }

        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("update-startup", ex);
            if (updateVerification)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.ExitCode = 20;
                return;
            }

            MessageBox.Show(
                ex.Message,
                "Recuperacion de actualizacion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (Environment.GetEnvironmentVariable("ARTE_POS_CREDIT_CONTINUATION_SMOKE") == "1")
        {
            Environment.ExitCode = PosBarcodeSmokeRunner.RunCreditContinuation(
                Environment.GetEnvironmentVariable("ARTE_POS_CREDIT_CONTINUATION_SMOKE_OUTPUT"));
            return;
        }

        if (Environment.GetEnvironmentVariable("ARTE_OPERATIONAL_RECOVERY_SMOKE") == "1")
        {
            var logPath = Environment.GetEnvironmentVariable("ARTE_OPERATIONAL_RECOVERY_SMOKE_LOG");
            var result = OperationalRecoverySmokeHarness.Run(logPath);
            if (result == 0)
            {
                var outputDirectory = string.IsNullOrWhiteSpace(logPath)
                    ? null
                    : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(logPath))!, "operational-recovery-layout");
                result = OperationalRecoveryLayoutSmokeRunner.Run(outputDirectory);
            }

            Environment.ExitCode = result;
            return;
        }

        if (Environment.GetEnvironmentVariable("ARTE_BACKUP_LAYOUT_SMOKE") == "1")
        {
            Environment.ExitCode = BackupMaintenanceLayoutSmokeRunner.Run(
                Environment.GetEnvironmentVariable("ARTE_BACKUP_LAYOUT_SMOKE_OUTPUT"));
            return;
        }

        if (Environment.GetEnvironmentVariable("ARTE_BACKUP_RESTORE_SMOKE") == "1")
        {
            Environment.ExitCode = BackupRestoreSmokeHarness.Run(
                Environment.GetEnvironmentVariable("ARTE_BACKUP_RESTORE_SMOKE_LOG"));
            return;
        }

        if (Environment.GetEnvironmentVariable("ARTE_PROTECTED_RESTORE_SMOKE") == "1")
        {
            Environment.ExitCode = ProtectedRestoreSmokeHarness.Run(
                Environment.GetEnvironmentVariable("ARTE_PROTECTED_RESTORE_SMOKE_LOG"));
            return;
        }

        if (Environment.GetEnvironmentVariable("ARTE_SUBCATEGORY_LAYOUT_SMOKE") == "1")
        {
            Environment.ExitCode = SubcategoryLayoutSmokeRunner.Run(
                Environment.GetEnvironmentVariable("ARTE_SUBCATEGORY_LAYOUT_SMOKE_OUTPUT"));
            return;
        }

        if (Environment.GetEnvironmentVariable("ARTE_PERIPHERAL_DOCUMENT_SMOKE") == "1")
        {
            Environment.ExitCode = PeripheralDocumentSmokeRunner.Run(
                Environment.GetEnvironmentVariable("ARTE_PERIPHERAL_DOCUMENT_SMOKE_OUTPUT"));
            return;
        }

        if (Environment.GetEnvironmentVariable("ARTE_BARCODE_HARDENING_SMOKE") == "1")
        {
            Environment.ExitCode = BarcodeHardeningSmokeRunner.Run(
                Environment.GetEnvironmentVariable("ARTE_BARCODE_HARDENING_SMOKE_OUTPUT"));
            return;
        }

        var services = new ServiceCollection();
        if (Environment.GetEnvironmentVariable("ARTE_H2_DISASTER_RECOVERY_SMOKE") == "1")
        {
            Environment.ExitCode = DisasterRecoverySmokeHarness.Run(
                Environment.GetEnvironmentVariable("ARTE_H2_DISASTER_RECOVERY_SMOKE_OUTPUT"));
            return;
        }

        services.AddArteInventariadoInfrastructure(
            startupPaths?.PathProvider,
            freshMachineRecovery: h2Recovery);
        if (databaseResetOperationId is { } resetOperationId)
        {
            services.AddScoped<IPrincipalProvisioningService>(provider => new PrincipalProvisioningService(
                provider.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                provider.GetRequiredService<ArteInventariadoPathProvider>(),
                provider.GetRequiredService<IInstallationContext>(),
                provider.GetRequiredService<IAuditService>(),
                resetOperationId));
        }
        services.AddTransient<LoginForm>();
        services.AddTransient<PrincipalProvisioningForm>();
        services.AddTransient<FreshMachineChoiceForm>();
        services.AddTransient<DisasterRecoveryForm>();
        services.AddTransient<AdminDashboardForm>();
        services.AddTransient<EmployeesForm>();
        services.AddTransient<EmployeeDashboardForm>();
        services.AddTransient<EmployeeInventoryLookupForm>();
        services.AddTransient<SalesHistoryForm>();
        services.AddTransient<MySalesTodayForm>();
        services.AddTransient<SaleCorrectionForm>();
        services.AddTransient<ReturnsHistoryForm>();
        services.AddTransient<ProductsForm>();
        services.AddTransient<CategoriesForm>();
        services.AddTransient<SubcategoriesForm>();
        services.AddTransient<SubcategoryEditForm>();
        services.AddTransient<UsersForm>();
        services.AddTransient<EmployeePermissionsForm>();
        services.AddTransient<CustomersForm>();
        services.AddTransient<SuppliersForm>();
        services.AddTransient<PurchasesForm>();
        services.AddTransient<QuotationsForm>();
        services.AddTransient<CustomOrdersForm>();
        services.AddTransient<AccountsReceivableForm>();
        services.AddTransient<AccountsPayableForm>();
        services.AddTransient<CreditAlertsForm>();
        services.AddTransient<CommercialAlertsForm>();
        services.AddTransient<CashMovementsDetailForm>();
        services.AddTransient<CashRegisterForm>();
        services.AddTransient<EmployeePOSForm>();
        services.AddTransient<QuickPOSForm>();
        services.AddTransient<ReportsForm>();
        services.AddTransient<AnalyticsForm>();
        services.AddTransient<ProductProfitabilityForm>();
        services.AddTransient<ExcelImportForm>();
        services.AddTransient<AuditLogForm>();
        services.AddTransient<SystemConfigForm>();
        services.AddTransient<BackupRestoreForm>();
        services.AddTransient<UpdateCenterForm>();
        services.AddScoped<CashRecoveryCoordinator>();

        RunApplication(
            services,
            updateVerification,
            h2Recovery,
            provisionPrincipal,
            provisionCloud,
            databaseResetOperationId);
    }

    static void RunApplication(
        IServiceCollection services,
        bool updateVerification,
        bool h2Recovery,
        bool provisionPrincipal,
        bool provisionCloud,
        Guid? databaseResetOperationId = null)
    {
        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var principalProvisioningRequired = false;
        UpdateRuntimePaths? verificationRuntimePaths = null;
        string? verificationDatabasePath = null;
        InstallationStatus? verificationStationStatus = null;
        try
        {
            var paths = serviceProvider.GetRequiredService<ArteInventariadoPathProvider>();
            verificationRuntimePaths = UpdateRuntimePaths.Create(paths);

            if (provisionCloud)
            {
                try
                {
                    if (!paths.IsMachineWide || !MachineWideElevation.IsAdministratorOrSystem())
                    {
                        throw new InvalidOperationException(
                            "El aprovisionamiento cloud requiere una instalación machine-wide y un proceso elevado.");
                    }

                    MachineWideStationConfigurationHelper.ValidateManagedRoots(paths);
                    using var provisioningScope = serviceProvider.CreateScope();
                    var authorization = provisioningScope.ServiceProvider
                        .GetRequiredService<CloudBackupProvisioningAuthorizationService>();
                    var grant = authorization.ConsumeHandoffAsync().GetAwaiter().GetResult();
                    var provisioning = provisioningScope.ServiceProvider
                        .GetRequiredService<CloudBackupProvisioningService>();
                    var configurationStore = provisioningScope.ServiceProvider
                        .GetRequiredService<CloudBackupConfigurationStore>();
                    using var cloudProvisioning = new CloudBackupProvisioningForm(
                        provisioning,
                        configurationStore,
                        grant);
                    cloudProvisioning.ShowDialog();
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or IOException)
                {
                    TechnicalErrorLogger.Log("cloud-provisioning", ex);
                    Environment.ExitCode = 40;
                    MessageBox.Show(
                        ex.Message,
                        "Aprovisionamiento cloud",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return;
            }

            var classifier = serviceProvider.GetRequiredService<IInstallationStateClassifier>();
            var assessment = classifier.Classify();
            verificationStationStatus = assessment.Station;
            if (updateVerification)
            {
                MachineWideUpdateDiagnostics.Write(
                    verificationRuntimePaths,
                    "APPLICATION_VERIFY_CLASSIFIED",
                    relatedPath: paths.DatabasePath,
                    detail: $"assessment={assessment.State};station={assessment.Station.State};mode={assessment.Station.Mode?.ToString() ?? "none"};configured={assessment.Station.IsConfigured};operationalWrites={assessment.Station.AllowsOperationalWrites};dbExists={assessment.DatabaseFileExists};problem={assessment.Station.Problem ?? assessment.Problem ?? "none"}");
            }

            if (h2Recovery)
            {
                if (!paths.IsMachineWide ||
                    assessment.State != InstallationLifecycleState.FreshMachine ||
                    !MachineWideElevation.IsAdministrator())
                {
                    throw new InvalidOperationException(
                        "El bootstrap de disaster recovery requiere una máquina FreshMachine y un proceso elevado.");
                }

                using var disasterRecovery = serviceProvider.GetRequiredService<DisasterRecoveryForm>();
                if (disasterRecovery.ShowDialog() == DialogResult.OK)
                {
                    LaunchNormalAfterRecovery(paths);
                }
                return;
            }

            if ((assessment.State is InstallationLifecycleState.ExistingDamagedInstallation or
                 InstallationLifecycleState.MigrationCandidate) && !databaseResetOperationId.HasValue)
            {
                var message = assessment.Problem ??
                    (assessment.State == InstallationLifecycleState.MigrationCandidate
                        ? "Se detectó una instalación legacy reconocida; debe continuar por el flujo de migración."
                        : "La instalación existente requiere reparación; no se creará una base nueva encima.");
                if (updateVerification)
                {
                    throw new InvalidOperationException(message);
                }

                MessageBox.Show(message, "ArteInventariado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (assessment.State == InstallationLifecycleState.RecoveryPending &&
                paths.IsMachineWide &&
                DisasterRecoveryJournalStore.HasPending(paths.RecoveryRoot))
            {
                var message = "Existe un bootstrap de disaster recovery pendiente. Debe resolverse mediante el flujo protegido antes de iniciar otra recuperación.";
                if (updateVerification)
                {
                    throw new InvalidOperationException(message);
                }

                MessageBox.Show(message, "Recuperación pendiente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!updateVerification && assessment.State == InstallationLifecycleState.FreshMachine)
            {
                using var choice = serviceProvider.GetRequiredService<FreshMachineChoiceForm>();
                if (choice.ShowDialog() != DialogResult.OK ||
                    choice.Choice == DisasterRecoveryChoice.Cancelled)
                {
                    return;
                }

                if (choice.Choice == DisasterRecoveryChoice.RecoverExisting)
                {
                    if (!paths.IsMachineWide)
                    {
                        MessageBox.Show(
                            "La recuperación de una máquina nueva requiere una instalación machine-wide protegida.",
                            "Recuperación",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }

                    var executable = Environment.ProcessPath
                        ?? throw new InvalidOperationException("No se pudo resolver el ejecutable de ArteInventariado.");
                    // Do not pass the standard user's LocalAppData root into
                    // the elevated process. The machine-wide helper owns only
                    // Program Files/ProgramData; omitting user-data-root lets
                    // the elevated account resolve its own harmless cache
                    // root and keeps protected-root validation deterministic.
                    var elevationArguments = new List<string>
                    {
                        "--h2-recovery",
                        "--machine-install-root", paths.MachineInstallRoot,
                        "--machine-data-root", paths.MachineDataRoot
                    };
                    var elevation = MachineWideElevation.EnsureElevated(
                        executable,
                        elevationArguments);
                    if (elevation.Outcome == MachineWideElevationOutcome.Cancelled)
                    {
                        MessageBox.Show(
                            "La recuperación fue cancelada en UAC. La máquina continúa fresca y sin datos comerciales.",
                            "Recuperación",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    else if (elevation.Outcome == MachineWideElevationOutcome.RelaunchedElevated)
                    {
                        MessageBox.Show(
                            "Se solicitó UAC. La ventana elevada continuará la selección cloud de forma segura.",
                            "Recuperación",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    else if (elevation.Outcome == MachineWideElevationOutcome.AlreadyElevated)
                    {
                        using var elevatedRecovery = serviceProvider.GetRequiredService<DisasterRecoveryForm>();
                        if (elevatedRecovery.ShowDialog() == DialogResult.OK)
                        {
                            LaunchNormalAfterRecovery(paths);
                        }
                    }

                    return;
                }
            }

            var installation = serviceProvider.GetRequiredService<
                ArteInventariado.Application.Interfaces.IInstallationContext>();
            installation.EnsureInitialized();

            var protectedRestore = serviceProvider.GetRequiredService<
                ArteInventariado.Application.Interfaces.IProtectedRestoreService>();
            if (protectedRestore.IsMachineWide)
            {
                var recoveryStatus = protectedRestore.GetRecoveryStatusAsync()
                    .GetAwaiter()
                    .GetResult();
                if (recoveryStatus.Pending)
                {
                    var recovery = protectedRestore.RecoverPendingAsync()
                        .GetAwaiter()
                        .GetResult();
                    if (recovery is null || recovery.Outcome != ArteInventariado.Application.Models.ProtectedRestoreOutcome.RelaunchedElevated)
                    {
                        var message = recovery?.Message ?? recoveryStatus.Problem ??
                            "Existe una restauracion protegida pendiente y no fue posible recuperar el estado anterior.";
                        if (updateVerification)
                        {
                            throw new InvalidOperationException(message);
                        }

                        MessageBox.Show(
                            message,
                            "Recuperacion protegida",
                            MessageBoxButtons.OK,
                            recovery?.Outcome == ArteInventariado.Application.Models.ProtectedRestoreOutcome.Cancelled
                                ? MessageBoxIcon.Information
                                : MessageBoxIcon.Error);
                        return;
                    }

                    // The helper waits for this process to exit before it
                    // consumes the journal. Never continue startup on the
                    // possibly half-promoted database.
                    return;
                }
            }

            var restore = serviceProvider.GetRequiredService<
                ArteInventariado.Application.Interfaces.IPendingRestoreProcessor>();
            var restoreResult = restore.ProcessPendingRestore();
            if (restoreResult.HadPendingRestore)
            {
                if (updateVerification && !restoreResult.Succeeded)
                {
                    throw new InvalidOperationException(restoreResult.Message);
                }

                if (!updateVerification)
                {
                    MessageBox.Show(
                        restoreResult.Message,
                        restoreResult.Succeeded ? "Restauracion completada" : "Restauracion no aplicada",
                        MessageBoxButtons.OK,
                        restoreResult.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
            }

            var databasePath = paths.DatabasePath;
            verificationDatabasePath = databasePath;
            var databaseExisted = File.Exists(databasePath);
            principalProvisioningRequired = assessment.State == InstallationLifecycleState.PrincipalProvisioningRequired;
            using var initializationScope = serviceProvider.CreateScope();
            var contextFactory = initializationScope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AppDbContext>>();
            using var context = contextFactory.CreateDbContext();
            var migrations = context.Database.GetMigrations().ToList();
            SchemaCompatibilityGuard.EnsureCompatible(databasePath, migrations);
            var pendingMigrations = context.Database.GetPendingMigrations().ToList();
            if (updateVerification)
            {
                MachineWideUpdateDiagnostics.Write(
                    verificationRuntimePaths,
                    "APPLICATION_VERIFY_SCHEMA_BEFORE",
                    relatedPath: databasePath,
                    detail: $"applied={migrations.Count - pendingMigrations.Count};pending={pendingMigrations.Count};latestPending={pendingMigrations.LastOrDefault() ?? "none"};dbExisted={databaseExisted};stationWrites={installation.Status.AllowsOperationalWrites}");
            }

            if (installation.Status.AllowsOperationalWrites || !databaseExisted)
            {
                DatabaseInitializer.InicializarAsync(context)
                    .GetAwaiter()
                    .GetResult();
                principalProvisioningRequired |= !databaseExisted;
            }
            else if (!context.Database.CanConnect())
            {
                throw new InvalidOperationException(
                    "La base existente no esta disponible para esta estacion.");
            }

            var remainingMigrations = context.Database.GetPendingMigrations().ToList();
            if (updateVerification)
            {
                MachineWideUpdateDiagnostics.Write(
                    verificationRuntimePaths,
                    "APPLICATION_VERIFY_SCHEMA_AFTER",
                    relatedPath: databasePath,
                    detail: $"remaining={remainingMigrations.Count};latestPending={remainingMigrations.LastOrDefault() ?? "none"};stationWrites={installation.Status.AllowsOperationalWrites}");
                if (installation.Status.AllowsOperationalWrites && remainingMigrations.Count > 0)
                {
                    throw new InvalidOperationException(
                        "La verificación de actualización terminó sin aplicar todas las migraciones pendientes.");
                }
            }
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("database-initialization", ex);
            if (updateVerification)
            {
                MachineWideUpdateDiagnostics.Write(
                    verificationRuntimePaths,
                    "APPLICATION_VERIFY_FAILED",
                    ex,
                    relatedPath: verificationDatabasePath,
                    detail: $"stationWrites={verificationStationStatus?.AllowsOperationalWrites.ToString() ?? "unknown"}");
                Console.Error.WriteLine(ex.Message);
                Environment.ExitCode = 21;
                return;
            }

            var message = ex is IncompatibleDatabaseException
                ? ex.Message
                : "No fue posible inicializar la base de datos. Revise el registro tecnico de la aplicacion.";
            MessageBox.Show(
                message,
                "ArteInventariado",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );

            return;
        }

        if (updateVerification)
        {
            MachineWideUpdateDiagnostics.Write(
                verificationRuntimePaths,
                "APPLICATION_VERIFY_SUCCEEDED",
                relatedPath: verificationDatabasePath,
                detail: $"stationWrites={verificationStationStatus?.AllowsOperationalWrites.ToString() ?? "unknown"}");
            Environment.ExitCode = 0;
            return;
        }

        using (var provisioningScope = serviceProvider.CreateScope())
        {
            var provisioning = provisioningScope.ServiceProvider
                .GetRequiredService<IPrincipalProvisioningService>();
            var required = principalProvisioningRequired ||
                provisioning.IsRequiredAsync().GetAwaiter().GetResult();
            if (databaseResetOperationId.HasValue && !required)
            {
                MessageBox.Show(
                    "La autorización de reinicio ya no está vigente o la base dejó de estar vacía.",
                    "Reinicio protegido",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                Environment.ExitCode = 41;
                return;
            }

            if (required)
            {
                var paths = serviceProvider.GetRequiredService<ArteInventariadoPathProvider>();
                if (paths.IsMachineWide && !provisionPrincipal && !MachineWideElevation.IsAdministrator())
                {
                    var executable = Environment.ProcessPath
                        ?? throw new InvalidOperationException("No se pudo resolver el ejecutable para la provisión inicial.");
                    var elevationArguments = new List<string>
                    {
                        "--provision-principal",
                        "--machine-install-root", paths.MachineInstallRoot,
                        "--machine-data-root", paths.MachineDataRoot
                    };
                    var elevation = MachineWideElevation.EnsureElevated(
                        executable,
                        elevationArguments);
                    if (elevation.Outcome == MachineWideElevationOutcome.Cancelled)
                    {
                        MessageBox.Show(
                            "La provisión del Administrador Principal fue cancelada en UAC. Puede reintentarse en el siguiente arranque.",
                            "Configuración inicial",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }

                    return;
                }

                using var form = provisioningScope.ServiceProvider.GetRequiredService<PrincipalProvisioningForm>();
                if (form.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                if (databaseResetOperationId.HasValue)
                {
                    // Setup owns the pending journal and verifies/commits the
                    // reset after this short-lived credential UI exits.
                    return;
                }

                // The interactive form may have run in the UAC-elevated
                // continuation.  Do not keep the normal runtime (login and
                // the operational UI) under that token: hand control back
                // to the stable launcher through the unelevated Explorer
                // shell after the owner has completed provisioning.
                if (provisionPrincipal && paths.IsMachineWide)
                {
                    LaunchNormalAfterProvisioning(paths);
                    return;
                }
            }
        }

        using var loginScope = serviceProvider.CreateScope();
        var loginForm = loginScope.ServiceProvider.GetRequiredService<LoginForm>();
        System.Windows.Forms.Application.Run(loginForm);
    }

    private static void LaunchNormalAfterRecovery(ArteInventariadoPathProvider paths)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        UpdateRuntimePaths.Create(paths).AddLayoutArguments(startInfo);
        _ = Process.Start(startInfo);
    }

    private static void LaunchNormalAfterProvisioning(ArteInventariadoPathProvider paths)
    {
        var executable = paths.LauncherPath;
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "No se encontró el launcher estable para devolver el runtime a un proceso no elevado.",
                executable);
        }

        // Explorer is the unelevated shell broker.  Starting the stable
        // launcher through it prevents the elevated first-run/recovery
        // continuation from making the normal WinForms runtime elevated.
        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            // The stable launcher is the canonical machine-wide entry point;
            // it resolves its roots from its protected location without
            // requiring any continuation state on the command line.
            Arguments = $"\"{executable}\""
        };
        _ = Process.Start(startInfo);
    }

    private static string? GetArgumentValue(IReadOnlyList<string> arguments, string name)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (!string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = arguments[index + 1];
            return value.StartsWith("--", StringComparison.Ordinal) ? null : value;
        }

        return null;
    }
}

internal static class InteractiveInstanceActivation
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static void TryActivateExisting(bool visualDev = false)
    {
        var currentId = Environment.ProcessId;
        var processName = Process.GetCurrentProcess().ProcessName;
        var currentPath = visualDev
            ? Path.GetFullPath(Environment.ProcessPath ?? string.Empty)
            : string.Empty;
        var existing = Process.GetProcessesByName(processName)
            .Where(process => process.Id != currentId)
            .OrderBy(process => process.Id)
            .FirstOrDefault(process =>
                process.MainWindowHandle != IntPtr.Zero &&
                (!visualDev || IsSameExecutable(process, currentPath)));
        if (existing is null)
        {
            return;
        }

        const int SwRestore = 9;
        ShowWindowAsync(existing.MainWindowHandle, SwRestore);
        SetForegroundWindow(existing.MainWindowHandle);
        existing.Dispose();
    }

    private static bool IsSameExecutable(Process process, string currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return false;
        }

        try
        {
            var candidate = process.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(candidate) &&
                string.Equals(
                    Path.GetFullPath(candidate),
                    currentPath,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
