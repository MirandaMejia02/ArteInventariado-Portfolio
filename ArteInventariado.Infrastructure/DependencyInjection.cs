using ArteInventariado.Application.Interfaces;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Infrastructure.Updater;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.CloudBackup;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddArteInventariadoInfrastructure(
        this IServiceCollection services,
        ArteInventariadoPathProvider? paths = null,
        bool freshMachineRecovery = false)
    {
        // Keep v1.11's per-user layout as the default for compatibility. An
        // explicit machine-wide provider from the protected installer is
        // shared by every runtime consumer so paths cannot drift apart.
        services.AddSingleton(_ => paths ?? ArteInventariadoPathProvider.CreateDefault());
        services.AddSingleton(provider => BackupStoragePaths.CreateDefault(
            provider.GetRequiredService<ArteInventariadoPathProvider>()));
        services.AddSingleton(provider => ProductAssetStoragePaths.CreateDefault(
            provider.GetRequiredService<ArteInventariadoPathProvider>()));
        services.AddSingleton<BackupPackageEngine>();
        services.AddSingleton<MachineWideBootstrapService>();
        services.AddSingleton<MachineWideAclService>();
        services.AddSingleton<MachineWideInstallationService>();
        services.AddSingleton<CloudBackupConfigurationStore>();
        services.AddSingleton<CloudBackupUploadStateStore>();
        services.AddSingleton<ICloudBackupSecretStore, DpapiCloudBackupSecretStore>();
        services.AddScoped<CloudBackupProvisioningAuthorizationService>();
        services.AddScoped<CloudBackupProvisioningService>();
        // G2 recovery is a separate, read-only trust boundary.  The
        // authenticator is stateless; authorization and orchestration remain
        // scoped so they use the current authenticated session and DB lease.
        services.AddSingleton<IInstallationStateClassifier, InstallationStateClassifier>();
        if (freshMachineRecovery)
        {
            services.AddScoped<ICloudRecoveryAuthorizationService, FreshMachineCloudRecoveryAuthorizationService>();
        }
        else
        {
            services.AddScoped<ICloudRecoveryAuthorizationService, CloudRecoveryAuthorizationService>();
        }
        services.AddSingleton<ICloudRecoveryAuthenticator, AwsStsCloudRecoveryAuthenticator>();
        services.AddSingleton<CloudRecoveryUacCoordinator>();
        services.AddSingleton<IProtectedRestoreService>(provider =>
            new ProtectedRestoreService(
                provider.GetRequiredService<ArteInventariadoPathProvider>(),
                provider.GetRequiredService<IServiceScopeFactory>()));
        services.AddSingleton<IProtectedBackupService>(provider =>
            new ProtectedBackupService(
                provider.GetRequiredService<ArteInventariadoPathProvider>(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                new ProtectedBackupProcessLauncher()));
        services.AddScoped<CloudRecoveryService>(provider =>
            new CloudRecoveryService(
                provider.GetRequiredService<ArteInventariadoPathProvider>(),
                provider.GetRequiredService<CloudBackupConfigurationStore>(),
                provider.GetRequiredService<BackupPackageEngine>(),
                provider.GetRequiredService<ICloudRecoveryAuthorizationService>(),
                provider.GetRequiredService<ICloudRecoveryAuthenticator>(),
                provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<ICloudRecoveryService>(provider =>
            provider.GetRequiredService<CloudRecoveryService>());
        services.AddScoped<IDisasterRecoveryBootstrapService>(provider =>
            new DisasterRecoveryBootstrapService(
                provider.GetRequiredService<ArteInventariadoPathProvider>(),
                provider.GetRequiredService<IInstallationStateClassifier>(),
                provider.GetRequiredService<CloudRecoveryService>(),
                provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IMachineWideCloudBackupTaskScheduler, MachineWideCloudBackupTaskScheduler>();
        services.AddSingleton<MachineWideStationModeService>();
        services.AddSingleton(provider => UpdateRuntimePaths.Create(
            provider.GetRequiredService<ArteInventariadoPathProvider>()));
        services.AddSingleton(provider => UpdateApplicationInfo.CreateDefault(
            provider.GetRequiredService<ArteInventariadoPathProvider>()));
        services.AddSingleton<UpdatePackageValidator>();
        services.AddSingleton(provider => new UpdateStateStore(
            provider.GetRequiredService<UpdateRuntimePaths>().StatePath));
        services.AddSingleton(provider => new UpdateBackupService(
            provider.GetRequiredService<BackupPackageEngine>()));
        services.AddSingleton<UpdateStationGuard>();
        services.AddSingleton<UpdateOperationalPreflight>();
        services.AddSingleton(provider => new UpdateEventLog(
            provider.GetRequiredService<UpdateRuntimePaths>().LogDirectory));
        services.AddSingleton(provider => new LocalStationConfigurationStore(
            provider.GetRequiredService<ArteInventariadoPathProvider>()));
        services.AddSingleton<IInstallationContext>(provider =>
            new InstallationContext(provider.GetRequiredService<LocalStationConfigurationStore>()));
        services.AddSingleton(provider => new StationEventLog(
            provider.GetRequiredService<ArteInventariadoPathProvider>()));
        services.AddScoped<IAdminReauthenticationService, AdminReauthenticationService>();
        services.AddSingleton(provider => new PendingRestoreStore(
            provider.GetRequiredService<BackupStoragePaths>().PendingRestorePath));
        services.AddSingleton<IPendingRestoreProcessor>(provider =>
            new PendingRestoreProcessor(
                provider.GetRequiredService<BackupStoragePaths>(),
                provider.GetRequiredService<PendingRestoreStore>(),
                provider.GetRequiredService<BackupPackageEngine>(),
                provider.GetRequiredService<IInstallationContext>(),
                provider.GetRequiredService<StationEventLog>()));
        services.AddSingleton<InstallationWriteGuardInterceptor>();
        services.AddDbContextFactory<AppDbContext>((provider, options) =>
        {
            var installation = provider.GetRequiredService<IInstallationContext>();
            var connection = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePathProvider.GetDatabasePath(
                    provider.GetRequiredService<ArteInventariadoPathProvider>()),
                Mode = installation.Status is
                {
                    State: Application.Models.StationConfigurationState.Ready,
                    Mode: Application.Models.InstallationMode.Consulta
                }
                    ? SqliteOpenMode.ReadOnly
                    : SqliteOpenMode.ReadWriteCreate,
                DefaultTimeout = 15
            };
            options
                .UseSqlite(connection.ToString())
                .AddInterceptors(provider.GetRequiredService<InstallationWriteGuardInterceptor>());
        });

        services.AddSingleton<ICurrentSession, CurrentSession>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IPrincipalProvisioningService, PrincipalProvisioningService>();
        services.AddScoped<IEmployeePermissionService, EmployeePermissionService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerQueryService, CustomerQueryService>();
        services.AddScoped<IEmployeeActivityQueryService, EmployeeActivityQueryService>();
        services.AddScoped<IEmployeeSalesQueryService, EmployeeSalesQueryService>();
        services.AddScoped<IEmployeePaymentService, EmployeePaymentService>();
        services.AddScoped<IEmployeePaymentQueryService, EmployeePaymentQueryService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ISupplierQueryService, SupplierQueryService>();
        services.AddScoped<IPurchaseService, PurchaseService>();
        services.AddScoped<IPurchaseQueryService, PurchaseQueryService>();
        services.AddScoped<IPurchaseReceptionService, PurchaseReceptionService>();
        services.AddScoped<IPurchasePaymentService, PurchasePaymentService>();
        services.AddScoped<IAccountsPayableService, AccountsPayableService>();
        services.AddScoped<IAccountsPayableQueryService, AccountsPayableQueryService>();
        services.AddScoped<IQuotationService, QuotationService>();
        services.AddScoped<IQuotationQueryService, QuotationQueryService>();
        services.AddScoped<IQuotationDocumentService, QuotationDocumentService>();
        services.AddScoped<ICustomOrderService, CustomOrderService>();
        services.AddScoped<ICustomOrderQueryService, CustomOrderQueryService>();
        services.AddScoped<ICustomOrderDocumentService, CustomOrderDocumentService>();
        services.AddScoped<ICustomOrderFinanceService, CustomOrderFinanceService>();
        services.AddScoped<ICustomOrderFinanceQueryService, CustomOrderFinanceQueryService>();
        services.AddScoped<ICustomOrderFinanceDocumentService, CustomOrderFinanceDocumentService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ISubcategoryService, SubcategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IProductCatalogService, ProductCatalogService>();
        services.AddScoped<IBarcodeService, BarcodeService>();
        services.AddSingleton<ICode128BarcodeRenderer, Code128BarcodeRenderer>();
        services.AddSingleton<IQrCodeRenderer, QrCodeRenderer>();
        services.AddScoped<IPosCatalogService, PosCatalogService>();
        services.AddScoped<IEmployeeInventoryLookupService, EmployeeInventoryLookupService>();
        services.AddScoped<IProductAssetService, ProductAssetService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ICashService, CashService>();
        services.AddScoped<ICashMovementQueryService, CashMovementQueryService>();
        services.AddScoped<ICommercialAlertQueryService, CommercialAlertQueryService>();
        services.AddScoped<IDashboardQueryService, DashboardQueryService>();
        services.AddScoped<IInventoryValuationQueryService, InventoryValuationQueryService>();
        services.AddScoped<ISalesAnalyticsQueryService, SalesAnalyticsQueryService>();
        services.AddScoped<IFinancialAnalyticsQueryService, FinancialAnalyticsQueryService>();
        services.AddScoped<ICommercialAnalyticsQueryService, CommercialAnalyticsQueryService>();
        services.AddScoped<IProductProfitabilityQueryService, ProductProfitabilityQueryService>();
        services.AddScoped<AdvancedReportQueryService>();
        services.AddScoped<IAdvancedReportQueryService>(provider => provider.GetRequiredService<AdvancedReportQueryService>());
        services.AddScoped<IAdvancedReportFilterOptionsService>(provider => provider.GetRequiredService<AdvancedReportQueryService>());
        services.AddScoped<IAdvancedReportExportService, AdvancedReportExportService>();
        services.AddScoped<IStationSettingsService>(provider =>
            new StationSettingsService(
                provider.GetRequiredService<LocalStationConfigurationStore>(),
                provider.GetRequiredService<ICurrentSession>(),
                provider.GetRequiredService<IInstallationContext>(),
                provider.GetRequiredService<StationEventLog>(),
                provider.GetRequiredService<ArteInventariadoPathProvider>()));
        services.AddScoped<IReceiptDocumentService, ReceiptDocumentService>();
        services.AddSingleton<IReceiptPrinter, UnconfiguredReceiptPrinter>();
        services.AddSingleton<ILabelPrinter, UnconfiguredLabelPrinter>();
        services.AddSingleton<ICashDrawer, UnconfiguredCashDrawer>();
        services.AddScoped<IPeripheralOutputCoordinator, PeripheralOutputCoordinator>();
        services.AddScoped<IReportPathProvider, ReportPathProvider>();
        services.AddScoped<ICommercialOperationsReportService, CommercialOperationsReportService>();
        services.AddScoped<CreditService>();
        services.AddScoped<ICreditService>(provider => provider.GetRequiredService<CreditService>());
        services.AddScoped<ICreditAlertQueryService, CreditAlertQueryService>();
        services.AddScoped<IAccountPaymentService, AccountPaymentService>();
        services.AddScoped<IAccountPaymentQueryService, AccountPaymentQueryService>();
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<ISalesQueryService, SalesQueryService>();
        services.AddScoped<IMySalesTodayQueryService, MySalesTodayQueryService>();
        services.AddScoped<ISaleCorrectionQueryService, SaleCorrectionQueryService>();
        services.AddScoped<IReturnService, ReturnService>();
        services.AddScoped<ISaleChangeService, SaleChangeService>();
        services.AddScoped<ISaleCancellationService, SaleCancellationService>();
        services.AddScoped<IMonthlyReportExportService, MonthlyReportExportService>();
        services.AddScoped<IProductImportService, ProductImportService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<ISystemConfigService, SystemConfigService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IInstallationManagementService>(provider =>
            new InstallationManagementService(
                provider.GetRequiredService<LocalStationConfigurationStore>(),
                provider.GetRequiredService<IInstallationContext>(),
                provider.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                provider.GetRequiredService<ICurrentSession>(),
                provider.GetRequiredService<IAuditService>(),
                provider.GetRequiredService<StationEventLog>(),
                provider.GetRequiredService<MachineWideStationModeService>().ChangeModeAsync));
        services.AddScoped<IBackupMaintenanceService>(provider =>
            new BackupMaintenanceService(
                provider.GetRequiredService<BackupStoragePaths>(),
                provider.GetRequiredService<BackupPackageEngine>(),
                provider.GetRequiredService<LocalStationConfigurationStore>(),
                provider.GetRequiredService<PendingRestoreStore>(),
                provider.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                provider.GetRequiredService<IInstallationContext>(),
                provider.GetRequiredService<IEmployeePermissionService>(),
                provider.GetRequiredService<ICurrentSession>(),
                provider.GetRequiredService<StationEventLog>(),
                provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IUpdateService, UpdateService>();
        services.AddScoped<INotificationService, NullNotificationService>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
