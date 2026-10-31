using ArteInventariado.Application.Interfaces;
using ArteInventariado.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.WinForms;

internal enum AdminModule
{
    Sales,
    Returns,
    ReturnsHistory,
    Products,
    Categories,
    Subcategories,
    Users,
    Employees,
    EmployeePermissions,
    Customers,
    Suppliers,
    Purchases,
    Quotations,
    CustomOrders,
    AccountsReceivable,
    AccountsPayable,
    Cash,
    Analytics,
    Reports,
    ExcelImport,
    AuditLog,
    Settings,
    CreditAlerts,
    CommercialAlerts
}

internal sealed class AdminNavigationCoordinator(
    IServiceProvider serviceProvider,
    ICurrentSession currentSession,
    IInstallationContext installationContext)
{
    public event EventHandler? ModuleClosed;

    public void Open(AdminModule module, IWin32Window owner)
    {
        EnsureAdminSession();
        using Form form = module switch
        {
            AdminModule.Sales => Resolve<SalesHistoryForm>(),
            AdminModule.Returns => Resolve<SaleCorrectionForm>(),
            AdminModule.ReturnsHistory => Resolve<ReturnsHistoryForm>(),
            AdminModule.Products => Resolve<ProductsForm>(),
            AdminModule.Categories => Resolve<CategoriesForm>(),
            AdminModule.Subcategories => Resolve<SubcategoriesForm>(),
            AdminModule.Users => Resolve<UsersForm>(),
            AdminModule.Employees => Resolve<EmployeesForm>(),
            AdminModule.EmployeePermissions => Resolve<EmployeePermissionsForm>(),
            AdminModule.Customers => Resolve<CustomersForm>(),
            AdminModule.Suppliers => Resolve<SuppliersForm>(),
            AdminModule.Purchases => Resolve<PurchasesForm>(),
            AdminModule.Quotations => Resolve<QuotationsForm>(),
            AdminModule.CustomOrders => Resolve<CustomOrdersForm>(),
            AdminModule.AccountsReceivable => Resolve<AccountsReceivableForm>(),
            AdminModule.AccountsPayable => Resolve<AccountsPayableForm>(),
            AdminModule.Cash => Resolve<CashRegisterForm>(),
            AdminModule.Analytics => Resolve<AnalyticsForm>(),
            AdminModule.Reports => Resolve<ReportsForm>(),
            AdminModule.ExcelImport => Resolve<ExcelImportForm>(),
            AdminModule.AuditLog => Resolve<AuditLogForm>(),
            AdminModule.Settings => Resolve<SystemConfigForm>(),
            AdminModule.CreditAlerts => Resolve<CreditAlertsForm>(),
            AdminModule.CommercialAlerts => Resolve<CommercialAlertsForm>(),
            _ => throw new ArgumentOutOfRangeException(nameof(module), module, null)
        };

        form.ShowDialog(owner);
        ModuleClosed?.Invoke(this, EventArgs.Empty);
    }

    public void OpenNewSale(IWin32Window owner)
    {
        EnsureOperationalWritesAllowed("Nueva venta");
        OpenResolved<EmployeePOSForm>(owner);
    }

    public void OpenQuickSale(IWin32Window owner)
    {
        EnsureAdminSession();
        OpenResolved<QuickPOSForm>(owner);
    }

    public void OpenNewProduct(IWin32Window owner)
    {
        EnsureAdminSession();
        EnsureOperationalWritesAllowed("Nuevo producto");
        using var form = new ProductEditForm(
            serviceProvider.GetRequiredService<IProductService>(),
            serviceProvider.GetRequiredService<ICategoryService>(),
            subcategoryService: serviceProvider.GetRequiredService<ISubcategoryService>(),
            assetService: serviceProvider.GetRequiredService<IProductAssetService>(),
            canManageImages: true);
        form.ShowDialog(owner);
        ModuleClosed?.Invoke(this, EventArgs.Empty);
    }

    public void OpenNewPurchase(IWin32Window owner)
    {
        EnsureAdminSession();
        EnsureOperationalWritesAllowed("Nueva compra");
        using var form = new PurchaseEditForm(
            serviceProvider.GetRequiredService<IPurchaseService>(),
            serviceProvider.GetRequiredService<ISupplierQueryService>(),
            serviceProvider.GetRequiredService<IPosCatalogService>());
        form.ShowDialog(owner);
        ModuleClosed?.Invoke(this, EventArgs.Empty);
    }

    public void OpenNewQuotation(IWin32Window owner)
    {
        EnsureAdminSession();
        EnsureOperationalWritesAllowed("Nueva cotizacion");
        using var form = new QuotationEditForm(
            serviceProvider.GetRequiredService<IQuotationService>(),
            serviceProvider.GetRequiredService<ICustomerQueryService>(),
            serviceProvider.GetRequiredService<IProductService>());
        form.ShowDialog(owner);
        ModuleClosed?.Invoke(this, EventArgs.Empty);
    }

    public void OpenChangePassword(IWin32Window owner)
    {
        EnsureAdminSession();
        EnsureOperationalWritesAllowed("Cambiar contraseña");
        using var form = new ChangePasswordForm(serviceProvider.GetRequiredService<IAccountService>());
        form.ShowDialog(owner);
        ModuleClosed?.Invoke(this, EventArgs.Empty);
    }

    private void OpenResolved<TForm>(IWin32Window owner)
        where TForm : Form
    {
        EnsureAdminSession();
        using var form = Resolve<TForm>();
        form.ShowDialog(owner);
        ModuleClosed?.Invoke(this, EventArgs.Empty);
    }

    private TForm Resolve<TForm>() where TForm : Form => serviceProvider.GetRequiredService<TForm>();

    private void EnsureAdminSession()
    {
        if (!currentSession.EstaAutenticado || !currentSession.TieneRol(RolUsuario.ADMIN))
        {
            throw new InvalidOperationException("Se requiere una sesion ADMIN activa para abrir este modulo.");
        }
    }

    private void EnsureOperationalWritesAllowed(string operation)
        => installationContext.EnsureOperationalWriteAllowed(operation);
}
