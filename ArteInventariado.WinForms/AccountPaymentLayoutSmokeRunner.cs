using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.WinForms.Controls;
using ArteInventariado.WinForms.Helpers;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WinForms;
using System.Drawing.Printing;

namespace ArteInventariado.WinForms;

internal static class AccountPaymentLayoutSmokeRunner
{
    private static readonly Size[] SmokeSizes =
    [
        new(1040, 720),
        new(1366, 768),
        new(1280, 720),
        new(900, 660),
        new(820, 560)
    ];

    private static readonly Size[] CreditAlertSmokeSizes =
    [
        new(1180, 720),
        new(1600, 900),
        new(1366, 768),
        new(1280, 720),
        new(980, 640)
    ];

    private static readonly Size[] CashHistorySmokeSizes =
    [
        new(1120, 620),
        new(1366, 768)
    ];

    private static readonly Size[] SalesHistorySmokeSizes =
    [
        new(1000, 680),
        new(1366, 768),
        new(1920, 1080)
    ];

    private static readonly Size[] MySalesTodaySmokeSizes =
    [
        new(980, 640),
        new(1280, 800),
        new(1366, 768)
    ];

    private static readonly Size[] CashSessionDetailSmokeSizes =
    [
        new(960, 660),
        new(1120, 760),
        new(1920, 1080)
    ];

    private static readonly Size[] CashOpeningSmokeSizes =
    [
        new(980, 680),
        new(1180, 780)
    ];

    private static readonly Size[] CashContributionSmokeSizes =
    [
        new(570, 390),
        new(720, 480)
    ];

    private static readonly Size[] AnalyticsSmokeSizes =
    [
        new(1280, 720),
        new(1366, 768),
        new(1600, 900),
        new(1920, 1080)
    ];

    private static readonly Size[] PaymentHistorySmokeSizes =
    [
        new(1600, 900),
        new(1366, 768),
        new(1280, 720),
        new(1180, 720),
        new(1100, 650)
    ];

    private static readonly Size[] SupplierAssociationSmokeSizes =
    [
        new(900, 620),
        new(1180, 720),
        new(1280, 720),
        new(1366, 768),
        new(1600, 900)
    ];

    private static readonly Size[] UpdateCenterSmokeSizes =
    [
        new(1280, 720),
        new(1366, 768),
        new(1600, 900),
        new(1920, 1080)
    ];

    private static readonly Size[] ExcelImportSmokeSizes =
    [
        new(1120, 640),
        new(1366, 768),
        new(1920, 1080),
        new(980, 640)
    ];

    private static readonly Size[] EmployeeDashboardSmokeSizes =
    [
        new(1920, 1080),
        new(1366, 768),
        new(1280, 720)
    ];

    private static readonly float[] EmployeeDashboardDpiScales = [1f, 1.25f, 1.5f];

    private static readonly (Size Size, float Scale, string Scenario)[] OneD2BVisualScenarios =
    [
        (new Size(1280, 720), 1f, "1280x720-dpi100"),
        (new Size(1280, 720), 1.5f, "1280x720-dpi150"),
        (new Size(1920, 1080), 1f, "1920x1080-dpi100"),
        (new Size(1920, 1080), 1.5f, "1920x1080-dpi150")
    ];

    public static int Run()
    {
        var errors = new List<string>();
        if (Environment.GetEnvironmentVariable("ARTE_CASH_HISTORY_LAYOUT_SMOKE_ONLY") == "1")
        {
            SmokeForm("CashHistoryForm", () => new CashHistoryForm(new SmokeCashHistoryService(), showAdminCashSummary: true), errors, CashHistorySmokeSizes);
            SmokeForm("CashSessionDetailForm", () => new CashSessionDetailForm(BuildCashSessionDetailSmokeSummary()), errors,
                CashSessionDetailSmokeSizes);
            SmokeForm("SalesHistoryForm", () => new SalesHistoryForm(null!, null!, null!), errors, SalesHistorySmokeSizes);
            SmokeForm("MySalesTodayForm", () => new MySalesTodayForm(new SmokeMySalesTodayQueryService()), errors, MySalesTodaySmokeSizes);
            SmokeSalesHistoryDetailTab(errors);
            SmokeCashDailySummaryCardLayout(errors);
            WriteLog(errors);
            return errors.Count == 0 ? 0 : 1;
        }

        if (Environment.GetEnvironmentVariable("ARTE_CASH_OPENING_LAYOUT_SMOKE_ONLY") == "1")
        {
            SmokeCashOpeningReferenceLayout(errors);
            WriteLog(errors);
            return errors.Count == 0 ? 0 : 1;
        }

        if (Environment.GetEnvironmentVariable("ARTE_CASH_CONTRIBUTION_LAYOUT_SMOKE_ONLY") == "1")
        {
            SmokeForm("CashContributionForm", () => new CashContributionForm(), errors, CashContributionSmokeSizes);
            WriteLog(errors);
            return errors.Count == 0 ? 0 : 1;
        }

        if (Environment.GetEnvironmentVariable("ARTE_ADMIN_DASHBOARD_F8_SMOKE_ONLY") == "1")
        {
            SmokeAdminDashboardF8(errors);
            WriteLog(errors);
            return errors.Count == 0 ? 0 : 1;
        }

        if (Environment.GetEnvironmentVariable("ARTE_1D2B_VISUAL_CAPTURE") == "1")
        {
            SmokeOneD2BVisualCapture(errors);
            WriteLog(errors);
            return errors.Count == 0 ? 0 : 1;
        }

        if (Environment.GetEnvironmentVariable("ARTE_2B_R1_REPORT_SMOKE_ONLY") == "1")
        {
            var outputDirectory = Environment.GetEnvironmentVariable("ARTE_2B_R1_REPORT_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                errors.Add("2B-R1: falta ARTE_2B_R1_REPORT_CAPTURE_DIR para guardar evidencia visual.");
            }
            else
            {
                Directory.CreateDirectory(outputDirectory);
                SmokeAdvancedReportPeriods(errors);
                SmokeAdvancedReportsVisualCapture(errors, outputDirectory);
            }
            WriteLog(errors);
            return errors.Count == 0 ? 0 : 1;
        }

        if (Environment.GetEnvironmentVariable("ARTE_EMPLOYEE_DASHBOARD_SMOKE_ONLY") == "1")
        {
            SmokeEmployeeDashboard(errors);
            WriteLog(errors);
            return errors.Count == 0 ? 0 : 1;
        }

        if (Environment.GetEnvironmentVariable("ARTE_STATION_LAYOUT_SMOKE_ONLY") == "1")
        {
            SmokeStationForms(errors);
            WriteLog(errors);
            return errors.Count == 0 ? 0 : 1;
        }

        if (Environment.GetEnvironmentVariable("ARTE_ANALYTICS_PRODUCTS_SMOKE_ONLY") == "1")
        {
            SmokeAnalyticsProductsBeta(errors);
            WriteLog(errors);
            return errors.Count == 0 ? 0 : 1;
        }

        if (Environment.GetEnvironmentVariable("ARTE_PURCHASE_PRODUCT_SEARCH_SMOKE_ONLY") == "1")
        {
            SmokeForm("PurchaseEditForm", () => new PurchaseEditForm(null!, null!, null!), errors, CreditAlertSmokeSizes);
            SmokeForm("PurchaseProductPickerForm", () => new PurchaseProductPickerForm(BuildPurchaseProducts()), errors,
                CreditAlertSmokeSizes);
            SmokePurchaseProductSearch(errors);
            WriteLog(errors);
            return errors.Count == 0 ? 0 : 1;
        }

        if (Environment.GetEnvironmentVariable("ARTE_COMMERCIAL_SUBCATEGORIES_SMOKE_ONLY") == "1")
        {
            SmokePurchaseProductSearch(errors);
            SmokeSupplierProductSelectors(errors);
            SmokeSupplierProfileProducts(errors);
            SmokeQuotationProductSearch(errors);
            SmokeQuotationProductPickerDpiScaling(errors);
            SmokeCustomOrderProductMapping(errors);
            WriteLog(errors);
            return errors.Count == 0 ? 0 : 1;
        }

        if (Environment.GetEnvironmentVariable("ARTE_ANALYTICS_LAYOUT_SMOKE_ONLY") == "1")
        {
            SmokeForm(
                "AnalyticsForm",
                () => new AnalyticsForm(
                    new SmokeSalesAnalyticsQueryService(),
                    new SmokeFinancialAnalyticsQueryService(),
                    new SmokeCommercialAnalyticsQueryService()),
                errors,
                AnalyticsSmokeSizes);
            SmokeAnalyticsDpiScaling(errors, 1.25f);
            SmokeAnalyticsDpiScaling(errors, 1.50f);
            SmokeAnalyticsCategoryTrendEmptyState(errors);
            WriteLog(errors);
            return errors.Count == 0 ? 0 : 1;
        }

        if (Environment.GetEnvironmentVariable("ARTE_EXCEL_IMPORT_LAYOUT_SMOKE_ONLY") == "1")
        {
            SmokeExcelImportLayout(errors);
            WriteLog(errors);
            return errors.Count == 0 ? 0 : 1;
        }

        SmokeForm("AccountPaymentForm", () => new AccountPaymentForm(1, null!, null!, null!, null!), errors);
        SmokeForm("AccountReceivableDetailForm", () => new AccountReceivableDetailForm(1, null!, null!, null!, null!), errors);
        SmokeForm("AccountPaymentReceiptForm", () => new AccountPaymentReceiptForm(BuildReceipt(), canNewPayment: true), errors);
        SmokeForm("AccountsReceivableForm", () => new AccountsReceivableForm(null!, null!, null!, null!, null!, null!, null!, null!, null!), errors);
        SmokeForm("AccountPaymentsHistoryForm", () => new AccountPaymentsHistoryLayoutSmokeForm(), errors, PaymentHistorySmokeSizes);
        SmokeAccountPaymentsHistoryRepeatedResize(errors);
        SmokeForm("CreditAlertsForm", () => new CreditAlertsLayoutSmokeForm(), errors, CreditAlertSmokeSizes);
        SmokeCreditAlertsRepeatedResize(errors);
        SmokeForm("LoginForm", () => new LoginLayoutSmokeForm(), errors, [new Size(520, 560), new Size(650, 700)]);
        SmokeForm(
            "SystemConfigForm",
            () => new SystemConfigForm(null!, null!, null!, null!, loadSettingsOnShow: false),
            errors,
            [new Size(900, 760), new Size(1280, 900), new Size(1920, 1080)]);
        SmokeForm("CustomersForm", () => new CustomersLayoutSmokeForm(), errors, CreditAlertSmokeSizes);
        SmokeForm("SuppliersForm", () => new SuppliersForm(null!, null!, null!, null!, null!, null!, new SmokeSession()), errors, CreditAlertSmokeSizes);
        SmokeForm("CategoriesForm", () => new CategoriesForm(new SmokeCategoryService()), errors, CreditAlertSmokeSizes);
        SmokeForm("CategoryEditForm", () => new CategoryEditForm(new SmokeCategoryService()), errors);
        var smokeUser = BuildSmokeUser();
        SmokeForm("UsersForm", () => new UsersForm(new SmokeUserService(), new SmokeSession()), errors, CreditAlertSmokeSizes);
        SmokeForm("UserEditForm", () => new UserEditForm(new SmokeUserService(), new SmokeSession()), errors);
        SmokeForm("PasswordChangeForm", () => new PasswordChangeForm(new SmokeUserService(), new SmokeSession(), smokeUser), errors);
        SmokeForm("ChangePasswordForm", () => new ChangePasswordForm(new SmokeAccountService()), errors);
        SmokeForm("ExcelImportForm", () => new ExcelImportForm(new SmokeProductImportService()), errors, CreditAlertSmokeSizes);
        SmokeForm("StockAdjustmentForm", () => new StockAdjustmentForm(null!, BuildSmokeProduct()), errors);
        SmokeForm("SupplierEditForm", () => new SupplierEditForm(null!), errors);
        SmokeForm("SupplierProfileForm", () => new SupplierProfileForm(1, null!, null!, null!, null!, null!, null!), errors, CreditAlertSmokeSizes);
        SmokeForm(
            "SupplierProductAssociationForm",
            () => new SupplierProductAssociationForm(1, new SmokeSupplierService(), new SmokeProductService()),
            errors,
            SupplierAssociationSmokeSizes);
        SmokeSupplierProductAssociationRepeatedResize(errors);
        SmokeSupplierProductSelectors(errors);
        SmokeSupplierProfileProducts(errors);
        SmokeForm("SupplierDuplicateWarningForm", () => new SupplierDuplicateWarningForm(BuildSupplierDuplicateResult()), errors);
        SmokeProductEditFormFocusAndTabOrder(errors);
        SmokeForm("PurchasesForm", () => new PurchasesForm(null!, null!, null!, null!, null!, null!), errors, CreditAlertSmokeSizes);
        SmokeForm("PurchaseEditForm", () => new PurchaseEditForm(null!, null!, null!), errors, CreditAlertSmokeSizes);
        SmokeForm("PurchaseProductPickerForm", () => new PurchaseProductPickerForm(BuildPurchaseProducts()), errors,
            CreditAlertSmokeSizes);
        SmokePurchaseProductSearch(errors);
        SmokeForm("PurchaseReceptionForm", () => new PurchaseReceptionForm(BuildPurchaseDetail(), null!), errors, CreditAlertSmokeSizes);
        SmokeForm("PurchaseDetailForm", () => new PurchaseDetailForm(BuildPurchaseDetail()), errors, CreditAlertSmokeSizes);
        SmokeForm("PurchasePaymentForm", () => new PurchasePaymentForm(BuildPurchaseDetail(), null!), errors, CreditAlertSmokeSizes);
        SmokeForm("AccountsPayableForm", () => new AccountsPayableForm(null!, null!, null!, null!), errors, CreditAlertSmokeSizes);
        SmokeForm("AccountPayableDetailForm", () => new AccountPayableDetailForm(BuildPayableDetail()), errors, CreditAlertSmokeSizes);
        SmokeForm("SupplierPaymentForm", () => new SupplierPaymentForm(BuildPayableDetail(), null!), errors, CreditAlertSmokeSizes);
        SmokeForm("SupplierPaymentsHistoryForm", () => new SupplierPaymentsHistoryForm(null!), errors, CreditAlertSmokeSizes);
        SmokeForm("SupplierPaymentReceiptForm", () => new SupplierPaymentReceiptForm(BuildSupplierPayment()), errors, CreditAlertSmokeSizes);
        SmokeForm("GeneratedPdfActionsForm", () => new GeneratedPdfActionsForm(Path.Combine(Path.GetTempPath(), "comprobante-smoke.pdf")), errors);
        SmokeForm(
            "ReportsForm",
            BuildSmokeReportsForm,
            errors,
            AnalyticsSmokeSizes);
        SmokeForm(
            "AnalyticsForm",
            () => new AnalyticsForm(
                new SmokeSalesAnalyticsQueryService(),
                new SmokeFinancialAnalyticsQueryService(),
                new SmokeCommercialAnalyticsQueryService()),
            errors,
            AnalyticsSmokeSizes);
        SmokeAnalyticsDpiScaling(errors, 1.25f);
        SmokeAnalyticsDpiScaling(errors, 1.50f);
        SmokeReportsDpiScaling(errors, 1.25f);
        SmokeReportsDpiScaling(errors, 1.50f);
        SmokeAdvancedReportPrinting(errors);
        var quotationService = new SmokeQuotationService();
        var permissions = new SmokePermissionService();
        SmokeForm("CommercialAlertsForm", () => new CommercialAlertsForm(new SmokeCommercialAlertQueryService(), permissions, new SmokeServiceProvider()), errors, CreditAlertSmokeSizes);
        SmokeForm("EmployeePermissionsForm", () => new EmployeePermissionsForm(null!, null!), errors, CreditAlertSmokeSizes);
        SmokeForm(
            "EmployeeInventoryLookupForm",
            () => new EmployeeInventoryLookupForm(
                new SmokeEmployeeInventoryLookupService(),
                new SmokeEmployeeInventoryAssetService()),
            errors,
            CreditAlertSmokeSizes);
        SmokeForm("CashMovementsDetailForm", () => new CashMovementsDetailForm(null!, null!, null!), errors, CreditAlertSmokeSizes);
        SmokeForm("CashHistoryForm", () => new CashHistoryForm(null!), errors, CashHistorySmokeSizes);
        SmokeForm("CashSessionDetailForm", () => new CashSessionDetailForm(BuildCashSessionDetailSmokeSummary()), errors,
            CashSessionDetailSmokeSizes);
        SmokeForm("CashRegisterForm", () => new CashRegisterForm(null!, new SmokeSession(), null!, null!, null!), errors, CreditAlertSmokeSizes);
        SmokeCashOpeningReferenceLayout(errors);
        SmokeForm(
            "ExpenseCaptureForm",
            () => new ExpenseCaptureForm(
                [new ExpenseCategoryOption(1, "Sin categoría"), new ExpenseCategoryOption(2, "Transporte")],
                "Gasto de prueba"),
            errors,
            CreditAlertSmokeSizes);
        SmokeForm("GeneratedFileActionsForm", () => new GeneratedFileActionsForm("Reporte generado", Path.Combine(Path.GetTempPath(), "reporte-smoke.xlsx")), errors);
        SmokeForm("QuotationsForm", () => new QuotationsForm(null!, null!, null!, null!, null!, null!), errors, CreditAlertSmokeSizes);
        SmokeForm("QuotationEditForm", () => new QuotationEditForm(quotationService, new SmokeCustomerQueryService(), new SmokeProductService()), errors, CreditAlertSmokeSizes);
        SmokeForm("QuotationDetailForm", () => new QuotationDetailForm(BuildQuotationDetail(), quotationService, new SmokeQuotationDocumentService(), permissions), errors, CreditAlertSmokeSizes);
        SmokeForm("QuotationProductPickerForm", () => new QuotationProductPickerForm(BuildQuotationProducts()), errors, CreditAlertSmokeSizes);
        SmokeQuotationProductSearch(errors);
        SmokeForm("QuotationConversionDateForm", () => new QuotationsForm.QuotationConversionDateForm("COT-000001"), errors, CreditAlertSmokeSizes);
        SmokeForm("CustomOrdersForm", () => new CustomOrdersForm(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!), errors, CreditAlertSmokeSizes);
        SmokeForm("CustomOrderEditForm", () => new CustomOrderEditForm(null!, null!, null!), errors, CreditAlertSmokeSizes);
        SmokeForm("CustomOrderDetailForm", () => new CustomOrderDetailForm(BuildCustomOrderDetail(), null!, null!, null!, null!, null!, null!, null!, null!, null!), errors, CreditAlertSmokeSizes);
        SmokeForm("CustomOrderStatusForm", () => new CustomOrderStatusForm("PED-000001", EstadoPedidoPersonalizado.CONFIRMADO, true, true), errors);
        SmokeForm("CustomOrderPaymentForm", () => new CustomOrderPaymentForm(BuildCustomOrderFinanceSummary(), null!), errors);
        SmokeForm("CustomOrderRefundForm", () => new CustomOrderRefundForm(BuildCustomOrderFinanceSummary(), null!), errors);
        SmokeForm("CustomOrderPaymentsHistoryForm", () => new CustomOrderPaymentsHistoryForm(1, "PED-000001", null!, null!), errors, CreditAlertSmokeSizes);
        SmokeForm(
            "CustomOrderProductMappingForm",
            () => new CustomOrderProductMappingForm(
                BuildCustomOrderConversionPreview().Lines,
                new SmokeCustomOrderFinanceService(),
                new SmokePosCatalogService()),
            errors,
            CreditAlertSmokeSizes);
        SmokeCustomOrderProductMapping(errors);
        SmokeForm("CustomOrderConversionForm", () => new CustomOrderConversionForm(BuildCustomOrderConversionPreview(), null!, null!), errors, CreditAlertSmokeSizes);
        SmokeAffectedVisualDpiScaling(errors, 1.25f);
        SmokeAffectedVisualDpiScaling(errors, 1.50f);
        // Drain asynchronous preview callbacks from the preceding visual forms
        // before exercising the dedicated employee-inventory teardown case.
        PumpUi(TimeSpan.FromMilliseconds(250));
        SmokeEmployeeInventoryAsyncTeardown(errors);
        SmokeStationForms(errors);
        SmokeAdminDashboardSidebarStateTransitions(errors);
        SmokeAdminDashboardDpiScaling(errors, 1.25f);
        SmokeAdminDashboardDpiScaling(errors, 1.50f);

        WriteLog(errors);
        return errors.Count == 0 ? 0 : 1;
    }

    private static UserSummary BuildSmokeUser() => new(
        1,
        "Usuario de prueba visual",
        "usuario-smoke",
        RolUsuario.EMPLEADO,
        true,
        DateTime.UtcNow.AddMonths(-2),
        DateTime.UtcNow.AddHours(-1));

    private static ProductSummary BuildSmokeProduct() => new(
        1,
        "PRD-SMOKE-001",
        "Producto de prueba visual",
        "Producto usado únicamente para validar el layout.",
        1,
        "Categoría smoke",
        null,
        100,
        250,
        12,
        3,
        EstadoProducto.ACTIVO,
        DateTime.UtcNow,
        null);

    private static void SmokeStationForms(List<string> errors)
    {
        SmokeStationConfigurationPaths(errors);
        SmokeStationIdentityVisibility(errors);
        SmokeUpdateCenterForms(errors);
        SmokeUpdateCenterStateTransitions(errors);
        SmokeForm("AdminDashboardForm", BuildSmokeDashboard, errors, CreditAlertSmokeSizes);
        SmokeForm("AdminDashboardForm Consulta", () => BuildSmokeDashboard(InstallationMode.Consulta), errors, CreditAlertSmokeSizes);
        SmokeForm(
            "EmployeeDashboardForm Consulta",
            () => new EmployeeDashboardForm(
                new SmokeAuthService(),
                new SmokeSession(),
                new SmokeServiceProvider(),
                new SmokePermissionService(),
                new SmokeInstallationContext(InstallationMode.Consulta)),
            errors,
            CreditAlertSmokeSizes);
        SmokeForm(
            "StationConfigurationForm first-run",
            () => BuildSmokeStationConfigurationForm(required: true),
            errors,
            CreditAlertSmokeSizes);
        SmokeForm(
            "StationConfigurationForm mode-change",
            () => BuildSmokeStationConfigurationForm(required: false),
            errors,
            CreditAlertSmokeSizes);
    }

    private static void SmokeEmployeeDashboard(List<string> errors)
    {
        var allPermissions = Enum.GetValues<PermisoEmpleado>().ToHashSet();
        var limitedPermissions = new HashSet<PermisoEmpleado>
        {
            PermisoEmpleado.ETIQUETAS_GESTIONAR,
            PermisoEmpleado.CLIENTES_ACCEDER
        };
        var posOnlyPermissions = new HashSet<PermisoEmpleado>
        {
            PermisoEmpleado.POS_ACCEDER
        };
        var scenarios = new[]
        {
            new EmployeeDashboardSmokeScenario(
                "EMPLEADO completo PRINCIPAL",
                RolUsuario.EMPLEADO,
                InstallationMode.Principal,
                allPermissions,
                QuickPosEnabled: true),
            new EmployeeDashboardSmokeScenario(
                "ADMIN completo PRINCIPAL",
                RolUsuario.ADMIN,
                InstallationMode.Principal,
                allPermissions,
                QuickPosEnabled: true),
            new EmployeeDashboardSmokeScenario(
                "EMPLEADO limitado PRINCIPAL",
                RolUsuario.EMPLEADO,
                InstallationMode.Principal,
                limitedPermissions,
                QuickPosEnabled: true),
            new EmployeeDashboardSmokeScenario(
                "EMPLEADO POS sin rápido",
                RolUsuario.EMPLEADO,
                InstallationMode.Principal,
                posOnlyPermissions,
                QuickPosEnabled: false),
            new EmployeeDashboardSmokeScenario(
                "EMPLEADO completo CONSULTA",
                RolUsuario.EMPLEADO,
                InstallationMode.Consulta,
                allPermissions,
                QuickPosEnabled: true)
        };

        foreach (var scenario in scenarios)
        {
            foreach (var scale in EmployeeDashboardDpiScales)
            {
                foreach (var size in EmployeeDashboardSmokeSizes)
                {
                    var context = $"{scenario.Name} {size.Width}x{size.Height} DPI {scale:P0}";
                    try
                    {
                        using var form = BuildSmokeEmployeeDashboard(scenario);
                        form.AutoScaleMode = AutoScaleMode.None;
                        form.WindowState = FormWindowState.Normal;
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-32000, -32000);
                        form.ShowInTaskbar = false;
                        if (scale != 1f)
                        {
                            form.Scale(new SizeF(scale, scale));
                        }

                        // Keep the physical resolution fixed after scaling so this
                        // exercises the smaller logical work area of high DPI.
                        form.Show();
                        // Windows can clamp an off-screen window to the current
                        // desktop work area while creating its handle. Reapply
                        // the target physical matrix size after Show().
                        form.ClientSize = size;
                        PumpEmployeeDashboardMessages();
                        ForceRootLayout(form);
                        PerformLayoutRecursive(form);
                        ValidateEmployeeDashboard(form, scenario, errors, context);
                        SaveEmployeeDashboardSnapshotIfRequested(form, scenario, size, scale);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"EmployeeDashboardForm {context}: {ex.GetType().Name} - {ex.Message}");
                    }
                }
            }
        }

        SmokeEmployeeDashboardF11(errors, allPermissions);
    }

    private static EmployeeDashboardForm BuildSmokeEmployeeDashboard(EmployeeDashboardSmokeScenario scenario)
    {
        return new EmployeeDashboardForm(
            new SmokeAuthService(),
            new SmokeSession("empleado-smoke", scenario.Role),
            new SmokeEmployeeDashboardServiceProvider(),
            new SmokePermissionService(scenario.Permissions),
            new SmokeInstallationContext(scenario.Mode),
            stationSettingsService: new SmokeStationSettingsService(scenario.QuickPosEnabled));
    }

    private static void ValidateEmployeeDashboard(
        EmployeeDashboardForm form,
        EmployeeDashboardSmokeScenario scenario,
        List<string> errors,
        string context)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        T Field<T>(string name) where T : class =>
            typeof(EmployeeDashboardForm).GetField(name, flags)?.GetValue(form) as T
            ?? throw new InvalidOperationException($"No se encontró {name}.");

        var shell = Field<TableLayoutPanel>("_shell");
        var sidebar = Field<Panel>("_sidebar");
        var navigation = Field<FlowLayoutPanel>("_sidebarNavigation");
        var quickActions = Field<FlowLayoutPanel>("_quickActions");
        var pos = Field<Button>("_btnPos");
        var quickPos = Field<Button>("_btnQuickPos");
        var returns = Field<Button>("_btnReturns");
        var cash = Field<Button>("_btnCash");
        var inventory = Field<Button>("_btnInventory");
        var changePassword = Field<Button>("_btnChangePassword");
        var logout = Field<Button>("_btnLogout");
        var modulesSection = Field<Label>("_modulesSectionLabel");
        var dynamicButtons = Field<List<SidebarNavigationButton>>("_dynamicModuleButtons");
        var quickCards = Field<Dictionary<Button, EmployeeQuickAccessCard>>("_quickAccessCards");
        var mainScroll = GetDescendants(form)
            .OfType<Panel>()
            .FirstOrDefault(panel => panel.Name == "EmployeeDashboardMainScroll")
            ?? throw new InvalidOperationException("No se encontró el scroll del área central.");

        var canUsePos = scenario.Permissions.Contains(PermisoEmpleado.POS_ACCEDER);
        var canReturn = scenario.Permissions.Contains(PermisoEmpleado.CORRECCIONES_ACCEDER);
        var canUseCash = scenario.Permissions.Overlaps(
        [
            PermisoEmpleado.CAJA_ABRIR,
            PermisoEmpleado.CAJA_CERRAR,
            PermisoEmpleado.GASTO_REGISTRAR,
            PermisoEmpleado.RETIRO_REGISTRAR,
            PermisoEmpleado.CAJA_TOTAL_ESPERADO_VER,
            PermisoEmpleado.CAJA_DIFERENCIAS_VER,
            PermisoEmpleado.CAJA_MOVIMIENTOS_DETALLE_VER
        ]);
        var expectedVisibility = new Dictionary<Button, bool>
        {
            [pos] = canUsePos,
            [quickPos] = canUsePos && scenario.QuickPosEnabled,
            [returns] = canReturn,
            [cash] = canUseCash,
            [inventory] = true
        };

        foreach (var (button, expected) in expectedVisibility)
        {
            if (button.Visible != expected)
            {
                errors.Add($"EmployeeDashboardForm {context}: '{button.Text}' Visible={button.Visible}, esperado {expected}.");
            }
            if (!quickCards.TryGetValue(button, out var card))
            {
                errors.Add($"EmployeeDashboardForm {context}: '{button.Text}' no tiene tarjeta equivalente.");
                continue;
            }
            if (card.Visible != expected || card.Enabled != button.Enabled)
            {
                errors.Add($"EmployeeDashboardForm {context}: tarjeta '{button.Text}' no refleja Visible/Enabled del sidebar.");
            }
        }

        var consulta = scenario.Mode == InstallationMode.Consulta;
        foreach (var (button, disabledInConsulta) in new[]
                 {
                     (pos, true),
                     (quickPos, false),
                     (returns, true),
                     (cash, true),
                     (inventory, false),
                     (changePassword, true)
                 })
        {
            var expectedEnabled = !(consulta && disabledInConsulta);
            if (button.Enabled != expectedEnabled)
            {
                errors.Add($"EmployeeDashboardForm {context}: '{button.Text}' Enabled={button.Enabled}, esperado {expectedEnabled}.");
            }
        }

        var permissionStates = Enum.GetValues<PermisoEmpleado>()
            .ToDictionary(permission => permission, scenario.Permissions.Contains);
        var expectedModules = EmployeeDashboardMoreMenuCatalog.Build(permissionStates);
        var actualModules = dynamicButtons
            .Select(button => (Action: (EmployeeDashboardMoreAction)button.Tag!, button.Text))
            .ToList();
        if (!actualModules.SequenceEqual(expectedModules.Select(item => (item.Action, item.Text))))
        {
            errors.Add($"EmployeeDashboardForm {context}: módulos dinámicos no coinciden con EmployeeDashboardMoreMenuCatalog.");
        }
        if (modulesSection.Visible != (expectedModules.Count > 0))
        {
            errors.Add($"EmployeeDashboardForm {context}: visibilidad incorrecta del encabezado de módulos.");
        }

        if (GetDescendants(form).OfType<Button>().Any(button => button.Text.Contains("Más", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"EmployeeDashboardForm {context}: todavía existe un acceso visual 'Más'.");
        }
        if (!navigation.AutoScroll || navigation.WrapContents || navigation.FlowDirection != FlowDirection.TopDown)
        {
            errors.Add($"EmployeeDashboardForm {context}: sidebar no conserva AutoScroll vertical/flujo TopDown.");
        }
        if (navigation.HorizontalScroll.Visible || mainScroll.HorizontalScroll.Visible)
        {
            errors.Add($"EmployeeDashboardForm {context}: apareció scroll horizontal.");
        }

        var columnWidths = shell.GetColumnWidths();
        if (columnWidths.Length != 2 || columnWidths.Any(width => width <= 0) || columnWidths.Sum() > form.ClientSize.Width + 2)
        {
            errors.Add($"EmployeeDashboardForm {context}: columnas raíz inválidas ({string.Join(",", columnWidths)} en {form.ClientSize}).");
        }
        if (!IsInsideAncestor(sidebar, form, horizontalOnly: true) || !IsInsideAncestor(logout, sidebar))
        {
            errors.Add($"EmployeeDashboardForm {context}: sidebar o cierre de sesión quedó fuera del contenedor.");
        }

        foreach (var button in GetDescendants(navigation).OfType<Button>().Where(button => button.Visible))
        {
            if (button.Width <= 0 || button.Height <= 0 || !IsInsideAncestor(button, navigation, horizontalOnly: true))
            {
                errors.Add($"EmployeeDashboardForm {context}: acceso '{button.Text}' cortado horizontalmente o sin área clickeable.");
            }
        }
        foreach (var card in quickCards.Values.Where(card => card.Visible))
        {
            if (card.Width <= 0 || card.Height <= 0 || !IsInsideAncestor(card, quickActions, horizontalOnly: true))
            {
                errors.Add($"EmployeeDashboardForm {context}: tarjeta '{card.Text}' fuera del área de accesos rápidos.");
            }
        }

        if (!GetDescendants(form).OfType<Label>().Any(label => label.Text == formModeName(scenario.Mode)))
        {
            errors.Add($"EmployeeDashboardForm {context}: no se mostró el indicador de estación esperado.");
        }

        static string formModeName(InstallationMode mode) => mode == InstallationMode.Consulta
            ? "Modo Consulta"
            : "Modo Principal";
    }

    private static void SmokeEmployeeDashboardF11(
        List<string> errors,
        IReadOnlySet<PermisoEmpleado> allPermissions)
    {
        var scenario = new EmployeeDashboardSmokeScenario(
            "EMPLEADO F11",
            RolUsuario.EMPLEADO,
            InstallationMode.Principal,
            allPermissions,
            QuickPosEnabled: true);
        EmployeeDashboardForm? form = null;
        try
        {
            form = BuildSmokeEmployeeDashboard(scenario);
            form.WindowState = FormWindowState.Normal;
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = new Rectangle(40, 40, 1280, 720);
            form.ShowInTaskbar = false;
            form.Show();
            PumpEmployeeDashboardMessages();

            var originalBorder = form.FormBorderStyle;
            var originalState = form.WindowState;
            var originalBounds = form.Bounds;
            FullScreenHelper.ToggleFullScreen(form);
            PumpEmployeeDashboardMessages();
            if (!FullScreenHelper.IsFullScreen(form) || form.FormBorderStyle != FormBorderStyle.None || form.TopMost)
            {
                errors.Add("EmployeeDashboardForm F11: no entró en fullscreen limpio o quedó TopMost.");
            }

            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var navigation = (FlowLayoutPanel?)typeof(EmployeeDashboardForm).GetField("_sidebarNavigation", flags)?.GetValue(form)
                ?? throw new InvalidOperationException("No se encontró _sidebarNavigation.");
            var inventory = (Button?)typeof(EmployeeDashboardForm).GetField("_btnInventory", flags)?.GetValue(form)
                ?? throw new InvalidOperationException("No se encontró _btnInventory.");
            var changePassword = (Button?)typeof(EmployeeDashboardForm).GetField("_btnChangePassword", flags)?.GetValue(form)
                ?? throw new InvalidOperationException("No se encontró _btnChangePassword.");

            navigation.AutoScrollPosition = Point.Empty;
            PumpEmployeeDashboardMessages();
            VerifyEmployeeDashboardModal(form, inventory, typeof(EmployeeInventoryLookupForm), errors, "Consultar inventario");

            navigation.AutoScrollPosition = new Point(0, navigation.VerticalScroll.Maximum);
            PumpEmployeeDashboardMessages();
            var scrollBefore = Math.Abs(navigation.AutoScrollPosition.Y);
            VerifyEmployeeDashboardModal(form, changePassword, typeof(ChangePasswordForm), errors, "Cambiar clave");
            var scrollAfter = Math.Abs(navigation.AutoScrollPosition.Y);
            if (scrollBefore > 0 && scrollAfter <= 0)
            {
                errors.Add($"EmployeeDashboardForm F11: el sidebar perdió por completo su posición desplazada ({scrollBefore} -> {scrollAfter}).");
            }

            FullScreenHelper.ExitFullScreen(form);
            PumpEmployeeDashboardMessages();
            if (FullScreenHelper.IsFullScreen(form) || form.FormBorderStyle != originalBorder ||
                form.WindowState != originalState || form.Bounds != originalBounds || form.TopMost)
            {
                errors.Add("EmployeeDashboardForm F11: no restauró exactamente borde/estado/tamaño/posición.");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"EmployeeDashboardForm F11: {ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
            if (form is not null)
            {
                FullScreenHelper.ExitFullScreen(form);
                typeof(EmployeeDashboardForm)
                    .GetField("_applicationCloseReviewed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(form, true);
                form.Close();
                form.Dispose();
            }
        }
    }

    private static void VerifyEmployeeDashboardModal(
        EmployeeDashboardForm owner,
        Button source,
        Type expectedType,
        List<string> errors,
        string name)
    {
        var visible = false;
        var hasOwner = false;
        var hadFocus = false;
        using var closer = new System.Windows.Forms.Timer { Interval = 50 };
        closer.Tick += (_, _) =>
        {
            var dialog = System.Windows.Forms.Application.OpenForms
                .Cast<Form>()
                .FirstOrDefault(open => open.GetType() == expectedType);
            if (dialog is null)
            {
                return;
            }

            visible = dialog.Visible;
            hasOwner = ReferenceEquals(dialog.Owner, owner);
            hadFocus = WaitForFocus(dialog);
            closer.Stop();
            dialog.DialogResult = DialogResult.Cancel;
            dialog.Close();
        };

        closer.Start();
        source.PerformClick();
        PumpEmployeeDashboardMessages();
        if (!visible || !hasOwner || !hadFocus)
        {
            errors.Add($"EmployeeDashboardForm F11/{name}: modal no quedó visible, con Owner y foco correctos.");
        }
        if (!WaitForFocus(owner))
        {
            errors.Add($"EmployeeDashboardForm F11/{name}: el foco no regresó al dashboard.");
        }
    }

    private static bool WaitForFocus(Form expected)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            if (Form.ActiveForm == expected || expected.ContainsFocus)
            {
                return true;
            }

            PumpEmployeeDashboardMessages();
        }

        return Form.ActiveForm == expected || expected.ContainsFocus;
    }

    private static void PumpEmployeeDashboardMessages()
    {
        for (var index = 0; index < 6; index++)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void SaveEmployeeDashboardSnapshotIfRequested(
        EmployeeDashboardForm form,
        EmployeeDashboardSmokeScenario scenario,
        Size size,
        float scale)
    {
        var outputDirectory = Environment.GetEnvironmentVariable("ARTE_EMPLOYEE_DASHBOARD_SMOKE_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDirectory) ||
            scenario.Name != "EMPLEADO completo PRINCIPAL" ||
            !((size == new Size(1920, 1080) && scale == 1f) ||
              (size == new Size(1366, 768) && scale == 1.25f) ||
              (size == new Size(1280, 720) && scale == 1.5f)))
        {
            return;
        }

        Directory.CreateDirectory(outputDirectory);
        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        var scaleName = (int)Math.Round(scale * 100);
        bitmap.Save(Path.Combine(
            outputDirectory,
            $"employee-dashboard-{size.Width}x{size.Height}-dpi{scaleName}.png"));
    }

    private sealed record EmployeeDashboardSmokeScenario(
        string Name,
        RolUsuario Role,
        InstallationMode Mode,
        IReadOnlySet<PermisoEmpleado> Permissions,
        bool QuickPosEnabled);

    private static void SmokeUpdateCenterForms(List<string> errors)
    {
        foreach (var scale in new[] { 1f, 1.25f, 1.50f })
        {
            foreach (var size in UpdateCenterSmokeSizes)
            {
                try
                {
                    using var form = new UpdateCenterForm(new SmokeUpdateService());
                    // The host executing the smoke can itself run at a non-100% DPI.
                    // Disable that implicit transform before applying the requested
                    // scale so each matrix entry represents one, deterministic DPI.
                    form.AutoScaleMode = AutoScaleMode.None;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-32000, -32000);
                    form.ShowInTaskbar = false;
                    form.ClientSize = size;
                    form.CreateControl();
                    if (scale != 1f)
                    {
                        form.Scale(new SizeF(scale, scale));
                        form.ClientSize = new Size(
                            (int)Math.Round(size.Width * scale),
                            (int)Math.Round(size.Height * scale));
                    }

                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    ForceRootLayout(form);
                    form.PerformLayout();
                    PerformLayoutRecursive(form);
                    ValidateUpdateCenterForm(form, errors, $"{size.Width}x{size.Height} DPI {scale:P0} normal");

                    if (scale == 1f)
                    {
                        form.WindowState = FormWindowState.Maximized;
                        System.Windows.Forms.Application.DoEvents();
                        var maximizedSize = form.ClientSize;
                        if (maximizedSize.Width > 1 && maximizedSize.Height > 1)
                        {
                            form.ClientSize = new Size(maximizedSize.Width - 1, maximizedSize.Height - 1);
                            form.ClientSize = maximizedSize;
                        }
                        ForceRootLayout(form);
                        form.PerformLayout();
                        PerformLayoutRecursive(form);
                        ValidateUpdateCenterForm(form, errors, $"{size.Width}x{size.Height} DPI {scale:P0} maximizada");
                        form.WindowState = FormWindowState.Normal;
                    }

                    form.ClientSize = scale == 1f
                        ? size
                        : new Size(
                            (int)Math.Round(size.Width * scale),
                            (int)Math.Round(size.Height * scale));
                    System.Windows.Forms.Application.DoEvents();
                    ForceRootLayout(form);
                    form.PerformLayout();
                    PerformLayoutRecursive(form);
                    ValidateUpdateCenterForm(form, errors, $"{size.Width}x{size.Height} DPI {scale:P0} restaurada");
                    form.Close();
                }
                catch (Exception ex)
                {
                    errors.Add(
                        $"UpdateCenterForm {size.Width}x{size.Height} DPI {scale:P0}: " +
                        $"{ex.GetType().Name} - {ex.Message}");
                }
            }
        }
    }

    private static void ForceRootLayout(Form form)
    {
        if (form.Controls.Count == 0)
        {
            return;
        }

        var root = form.Controls[0];
        root.SuspendLayout();
        try
        {
            root.Dock = DockStyle.None;
            root.Bounds = form.ClientRectangle;
            root.Dock = DockStyle.Fill;
        }
        finally
        {
            root.ResumeLayout(true);
        }
    }

    private static void ValidateUpdateCenterForm(Form form, List<string> errors, string scenario)
    {
        var descendants = GetDescendants(form).ToList();
        var header = descendants.OfType<Label>()
            .FirstOrDefault(label => label.Text.StartsWith("Actualizaciones", StringComparison.Ordinal));
        if (header is null)
        {
            errors.Add($"UpdateCenterForm {scenario}: no se encontro el encabezado.");
        }
        else if (header.Height < header.PreferredHeight)
        {
            errors.Add(
                $"UpdateCenterForm {scenario}: encabezado comprimido. " +
                $"Height={header.Height}, Preferred={header.PreferredHeight}.");
        }

        foreach (var text in new[] { "Validar paquete", "Preparar actualización", "Instalar" })
        {
            ValidateButtonByText("UpdateCenterForm " + scenario, descendants, text, errors);
        }

        foreach (var textBox in descendants.OfType<TextBox>())
        {
            if (textBox.Height < textBox.PreferredHeight)
            {
                errors.Add(
                    $"UpdateCenterForm {scenario}: campo de texto comprimido. " +
                    $"Size={textBox.Width}x{textBox.Height}, Preferred={textBox.PreferredSize.Width}x{textBox.PreferredSize.Height}.");
            }

            if (!IsInsideAncestor(textBox, form))
            {
                errors.Add($"UpdateCenterForm {scenario}: campo de texto fuera del formulario ({BoundsInAncestor(textBox, form)}, client={form.ClientSize}).");
            }
        }

        foreach (var button in descendants.OfType<Button>())
        {
            if (!IsInsideAncestor(button, form))
            {
                errors.Add($"UpdateCenterForm {scenario}: boton fuera del formulario ({button.Text}, {BoundsInAncestor(button, form)}, client={form.ClientSize}).");
            }
        }
    }

    private static void SmokeStationIdentityVisibility(List<string> errors)
    {
        Validate("admin protegido", new SmokeSession("admin", RolUsuario.ADMIN), expectedVisible: true);
        Validate("ADMIN comun", new SmokeSession("delegated-admin", RolUsuario.ADMIN), expectedVisible: false);
        Validate("EMPLEADO", new SmokeSession("admin", RolUsuario.EMPLEADO), expectedVisible: false);

        void Validate(string scenario, SmokeSession session, bool expectedVisible)
        {
            try
            {
                using var form = BuildSmokeDashboard(InstallationMode.Principal, session);
                form.WindowState = FormWindowState.Normal;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.ShowInTaskbar = false;
                form.ClientSize = new Size(1280, 720);
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                var stationButton = (Button?)typeof(AdminDashboardForm)
                    .GetField("_btnStation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(form);
                var modeIndicator = (Label?)typeof(AdminDashboardForm)
                    .GetField("_installationModeLabel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(form);

                if (stationButton is null || stationButton.Visible != expectedVisible)
                {
                    errors.Add($"Identidad de estacion ({scenario}): Visible esperado={expectedVisible}, actual={stationButton?.Visible}.");
                }

                if (modeIndicator is null || !modeIndicator.Visible)
                {
                    errors.Add($"Identidad de estacion ({scenario}): el indicador informativo de modo no permanece visible.");
                }

                form.Close();
            }
            catch (Exception ex)
            {
                errors.Add($"Identidad de estacion ({scenario}): {ex.GetType().Name} - {ex.Message}");
            }
        }
    }

    private static void SmokeStationConfigurationPaths(List<string> errors)
    {
        var localAppData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var executableDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        foreach (var path in new[]
                 {
                     StationConfigurationPathProvider.GetConfigurationPath(),
                     StationConfigurationPathProvider.GetAuditLogPath()
                 })
        {
            var resolved = Path.GetFullPath(path);
            if (!Path.IsPathRooted(resolved) ||
                !resolved.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase) ||
                resolved.StartsWith(executableDirectory, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Station paths: la ruta local no es independiente del ejecutable: {resolved}");
            }
        }
    }

    private static void SmokeAnalyticsProductsBeta(List<string> errors)
    {
        var diagnosticPath = Environment.GetEnvironmentVariable("ARTE_ANALYTICS_DIAGNOSTIC_LOG");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        void Checkpoint(string message)
        {
            var line = $"[{stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}] {message}";
            Console.WriteLine(line);
            Console.Out.Flush();
            if (!string.IsNullOrWhiteSpace(diagnosticPath))
            {
                File.AppendAllText(diagnosticPath, line + Environment.NewLine);
            }
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(diagnosticPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(diagnosticPath)!);
                File.WriteAllText(diagnosticPath, string.Empty);
            }

            var sales = new SmokeSalesAnalyticsQueryService(Checkpoint);
            using var form = new AnalyticsForm(
                sales,
                new SmokeFinancialAnalyticsQueryService(),
                new SmokeCommercialAnalyticsQueryService());
            Checkpoint("Form creado");

            form.WindowState = FormWindowState.Normal;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.ClientSize = new Size(1280, 720);
            form.Tag = "1280x720-dpi100";
            form.Show();
            Checkpoint("Load iniciado");

            var descendants = GetDescendants(form).ToList();
            var productButton = descendants.OfType<Button>()
                .Single(button => Equals(button.Tag, "analytics-section-button") && button.AccessibleName == "Productos");
            var search = descendants.OfType<TextBox>()
                .Single(control => control.Name == "ProductAnalyticsSearchTextBox");
            var grid = descendants.OfType<DataGridView>()
                .Single(control => control.Name == "ProductAnalyticsRankingGrid");
            var chart = descendants.OfType<CartesianChart>()
                .Single(control => control.Name == "ProductAnalyticsTrendChart");
            var chartTitle = descendants.OfType<Label>()
                .Single(control => control.Name == "ProductAnalyticsChartTitle");

            productButton.PerformClick();
            Checkpoint("Navegación Productos");

            WaitForAnalyticsStage(
                "Overview cargado",
                () => grid.Rows.Count == 10,
                TimeSpan.FromSeconds(5),
                Checkpoint);
            if (sales.LastRequestedProductLimit > SalesAnalyticsQueryLimits.MaximumProducts)
            {
                throw new InvalidOperationException(
                    $"AnalyticsForm solicitó {sales.LastRequestedProductLimit} productos; " +
                    $"el máximo publicado es {SalesAnalyticsQueryLimits.MaximumProducts}.");
            }
            Checkpoint($"Límite solicitado por AnalyticsForm: {sales.LastRequestedProductLimit}");
            WaitForAnalyticsStage(
                "KPI enlazados",
                () =>
                {
                    var visibleCards = descendants.OfType<AnalyticsProductMetricCardControl>()
                        .Where(control => control.Visible)
                        .ToList();
                    return visibleCards.Count == 3 && visibleCards.All(control =>
                        GetDescendants(control).OfType<Label>()
                            .Any(label => label.Text == "Lienzo artesanal" || label.Text == "Pulsera tejida"));
                },
                TimeSpan.FromSeconds(2),
                Checkpoint);
            Checkpoint("Ranking enlazado");

            grid.CurrentCell = grid.Rows[0].Cells["Product"];
            grid.Rows[0].Selected = true;
            Checkpoint("Primera fila seleccionada");
            WaitForAnalyticsStage(
                "Gráfica cargada",
                () => chart.Series.Count() == 1 && chartTitle.Text.Contains("PRD-001", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5),
                Checkpoint);

            search.Text = "PRD-002";
            WaitForAnalyticsStage(
                "Búsqueda probada",
                () => grid.Rows.Count == 1 && Convert.ToString(grid.Rows[0].Cells["Code"].Value) == "PRD-002",
                TimeSpan.FromSeconds(3),
                Checkpoint);

            form.ClientSize = new Size(1366, 768);
            form.PerformLayout();
            PerformLayoutRecursive(form);
            Checkpoint("Resize probado");

            form.Scale(new SizeF(1.25f, 1.25f));
            form.ClientSize = new Size(1280, 720);
            form.PerformLayout();
            PerformLayoutRecursive(form);
            Checkpoint("DPI 125% probado");

            WaitForAnalyticsStage(
                "Operaciones del servicio completadas",
                () => sales.PendingCalls == 0,
                TimeSpan.FromSeconds(2),
                Checkpoint);

            form.Close();
            WaitForAnalyticsStage(
                "Form cerrado",
                () => !form.Visible,
                TimeSpan.FromSeconds(2),
                Checkpoint);
            Checkpoint("Smoke terminado");
        }
        catch (Exception ex)
        {
            var message = $"Analytics Productos BETA: {ex.GetType().Name} - {ex.Message}";
            errors.Add(message);
            Checkpoint(message);
            Checkpoint(ex.ToString());
        }
    }

    private static void WaitForAnalyticsStage(
        string stage,
        Func<bool> completed,
        TimeSpan timeout,
        Action<string> checkpoint)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            System.Windows.Forms.Application.DoEvents();
            if (completed())
            {
                checkpoint(stage);
                return;
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException($"La etapa '{stage}' no terminó en {timeout.TotalSeconds:N0} segundos.");
    }

    private static AdminDashboardForm BuildSmokeDashboard() => BuildSmokeDashboard(InstallationMode.Principal);

    private static AdminDashboardForm BuildSmokeDashboard(InstallationMode mode) =>
        BuildSmokeDashboard(mode, new SmokeSession());

    private static AdminDashboardForm BuildSmokeDashboard(InstallationMode mode, SmokeSession session)
    {
        var installation = new SmokeInstallationContext(mode);
        var form = new AdminDashboardForm(
            new SmokeAuthService(),
            session,
            new SmokeCreditAlertQueryService(),
            new SmokeCommercialAlertQueryService(),
            new SmokeDashboardQueryService(),
            new SmokeServiceProvider(),
            installation,
            new SmokeInstallationManagementService(
                installation.Status,
                ProtectedAdministrator.IsProtectedAdministrator(session.UsuarioActual)),
            stationSettingsService: new SmokeStationSettingsService(quickPosEnabled: true));
        typeof(AdminDashboardForm)
            .GetField("_initialPasswordChecked", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(form, true);
        return form;
    }

    private static StationConfigurationForm BuildSmokeStationConfigurationForm(bool required)
    {
        var installation = new SmokeInstallationContext(required ? null : InstallationMode.Principal);
        var profile = new InstallationSetupProfile(
            required ? InstallationSetupKind.NewInstallation : InstallationSetupKind.AlreadyConfigured,
            installation.Status,
            true,
            required
                ? "Instalacion limpia preparada para smoke visual."
                : "Estacion Principal preparada para cambio de modo.");
        return new StationConfigurationForm(
            new SmokeInstallationManagementService(profile.Status, isProtectedAdministrator: true),
            profile,
            required);
    }

    private static ReportsForm BuildSmokeReportsForm() => new(
        null!,
        null!,
        new SmokeSession(),
        new SmokePermissionService(),
        new SmokeAdvancedReportQueryService(),
        new SmokeAdvancedReportExportService(),
        new SmokeAdvancedReportFilterOptionsService());

    private static void SmokeAdminDashboardDpiScaling(List<string> errors, float scale)
    {
        try
        {
            using var form = BuildSmokeDashboard();
            form.WindowState = FormWindowState.Normal;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.ClientSize = new Size(1280, 720);
            form.Scale(new SizeF(scale, scale));
            form.ClientSize = new Size(1280, 720);
            form.Show();
            System.Windows.Forms.Application.DoEvents();
            form.PerformLayout();
            PerformLayoutRecursive(form);
            ValidateAdminDashboardForm(form, errors);
            ValidateAdminDashboardSidebarTransitions(form, errors, $"DPI {scale:P0}", cycles: 1);
        }
        catch (Exception ex)
        {
            errors.Add($"AdminDashboardForm DPI {scale:P0}: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void SmokeAdminDashboardF8(List<string> errors)
    {
        try
        {
            using var form = BuildSmokeDashboard();
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var quickPosEnabled = typeof(AdminDashboardForm).GetField("_quickPosEnabled", flags)
                ?? throw new InvalidOperationException("No se encontro _quickPosEnabled.");

            quickPosEnabled.SetValue(form, true);
            var launchCount = 0;
            var handled = form.SimulateDashboardKeyDownForSmoke(Keys.F8, () => launchCount++);
            if (launchCount != 1 || !handled.Handled || !handled.SuppressKeyPress)
            {
                errors.Add("AdminDashboardForm F8: POS rápido no se abrió una sola vez o la tecla no se consumió.");
            }

            launchCount = 0;
            handled = form.SimulateDashboardKeyDownForSmoke(Keys.F8, () =>
            {
                launchCount++;
                form.SimulateDashboardKeyDownForSmoke(Keys.F8, () => launchCount++);
            });
            if (launchCount != 1 || !handled.Handled || !handled.SuppressKeyPress)
            {
                errors.Add("AdminDashboardForm F8: una invocación repetida abrió más de un POS rápido.");
            }

            quickPosEnabled.SetValue(form, false);
            launchCount = 0;
            handled = form.SimulateDashboardKeyDownForSmoke(Keys.F8, () => launchCount++);
            if (launchCount != 0 || !handled.Handled || !handled.SuppressKeyPress)
            {
                errors.Add("AdminDashboardForm F8: se lanzó POS rápido aunque la preferencia está deshabilitada.");
            }

            handled = form.SimulateDashboardKeyDownForSmoke(Keys.F7, () => launchCount++);
            if (launchCount != 0 || handled.Handled || handled.SuppressKeyPress)
            {
                errors.Add("AdminDashboardForm F8: se alteró el comportamiento de una tecla distinta de F8.");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"AdminDashboardForm F8: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void SmokeAnalyticsDpiScaling(List<string> errors, float scale)
    {
        foreach (var size in AnalyticsSmokeSizes)
        {
            try
            {
                using var form = new AnalyticsForm(
                    new SmokeSalesAnalyticsQueryService(),
                    new SmokeFinancialAnalyticsQueryService(),
                    new SmokeCommercialAnalyticsQueryService(),
                    new SmokeAnalyticsServiceProvider());
                form.WindowState = FormWindowState.Normal;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.ShowInTaskbar = false;
                form.ClientSize = size;
                form.Scale(new SizeF(scale, scale));
                form.ClientSize = size;
                form.Tag = $"{size.Width}x{size.Height}-dpi{scale * 100:N0}";
                form.Show();
                form.ClientSize = size;
                System.Windows.Forms.Application.DoEvents();
                PumpUi(TimeSpan.FromMilliseconds(700));
                form.PerformLayout();
                PerformLayoutRecursive(form);
                ValidateAnalyticsForm(form, errors);
            }
            catch (Exception ex)
            {
                errors.Add(
                    $"AnalyticsForm {size.Width}x{size.Height} DPI {scale:P0}: " +
                    $"{ex.GetType().Name} - {ex.Message}");
            }
        }
    }

    private static void SmokeReportsDpiScaling(List<string> errors, float scale)
    {
        foreach (var size in AnalyticsSmokeSizes)
        {
            var scenario = $"{size.Width}x{size.Height} DPI {scale:P0}";
            var scenarioErrors = new List<string>();
            try
            {
                using var form = BuildSmokeReportsForm();
                form.WindowState = FormWindowState.Normal;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.ShowInTaskbar = false;
                form.ClientSize = size;
                form.Scale(new SizeF(scale, scale));
                form.ClientSize = size;
                form.Tag = $"{size.Width}x{size.Height}-dpi{scale * 100:N0}";
                form.Show();
                NormalizeVisualSurface(form);
                System.Windows.Forms.Application.DoEvents();
                WaitForReportsVisualReady(
                    form,
                    AdvancedReportKind.Sales,
                    scenarioErrors,
                    "carga inicial",
                    requireAllHistory: false,
                    requireCoverage: false);
                form.PerformLayout();
                PerformLayoutRecursive(form);
                ValidateReportsForm(form, scenarioErrors);
                InvokeOnUi(form, () =>
                    SelectAdvancedReportForVisual(form, AdvancedReportKind.CurrentInventoryValue, scenarioErrors));
                WaitForReportsVisualReady(
                    form,
                    AdvancedReportKind.CurrentInventoryValue,
                    scenarioErrors,
                    "refresh async",
                    requireAllHistory: true,
                    requireCoverage: true);
                ValidateAdvancedReportVisual(form, AdvancedReportKind.CurrentInventoryValue, scenarioErrors);
            }
            catch (Exception ex)
            {
                scenarioErrors.Add($"ReportsForm: {ex.GetType().Name} - {ex.Message}");
            }

            errors.AddRange(scenarioErrors.Select(error => $"ReportsForm {scenario}: {error}"));
        }
    }

    private static void SmokeUpdateCenterStateTransitions(List<string> errors)
    {
        var scenarios = new[]
        {
            (UpdateState.ReadyToInstall, (string?)null, "Listo para instalar", true),
            (UpdateState.Installing, (string?)null, "Instalando", false),
            (UpdateState.Success, (string?)null, "Actualización completada", false),
            (UpdateState.Failed, "Fallo de prueba; Rollback completado.", "Instalación revertida; prepare nuevamente", false),
            (UpdateState.RollbackRequired, "No se pudo completar el rollback.", "Recuperación requerida", false)
        };

        foreach (var (state, lastError, expectedText, installEnabled) in scenarios)
        {
            try
            {
                using var form = new UpdateCenterForm(new SmokeUpdateStateService(state, lastError));
                form.AutoScaleMode = AutoScaleMode.None;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.ShowInTaskbar = false;
                form.ClientSize = new Size(1280, 720);
                form.Show();
                PumpUi(TimeSpan.FromMilliseconds(80));

                var stateLabel = (Label?)typeof(UpdateCenterForm)
                    .GetField("_state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(form);
                var install = (Button?)typeof(UpdateCenterForm)
                    .GetField("_install", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(form);
                if (!string.Equals(stateLabel?.Text, expectedText, StringComparison.Ordinal))
                {
                    errors.Add($"UpdateCenter state {state}: texto esperado '{expectedText}', actual '{stateLabel?.Text}'.");
                }

                if (install?.Enabled != installEnabled)
                {
                    errors.Add($"UpdateCenter state {state}: Install.Enabled esperado={installEnabled}, actual={install?.Enabled}.");
                }

                form.Close();
            }
            catch (Exception ex)
            {
                errors.Add($"UpdateCenter state {state}: {ex.GetType().Name} - {ex.Message}");
            }
        }
    }

    private static void SmokeOneD2BVisualCapture(List<string> errors)
    {
        var outputDirectory = Environment.GetEnvironmentVariable("ARTE_1D2B_VISUAL_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            errors.Add("1D-2B: falta ARTE_1D2B_VISUAL_CAPTURE_DIR para guardar evidencia visual.");
            return;
        }

        Directory.CreateDirectory(outputDirectory);
        var analyticsDirectory = Path.Combine(outputDirectory, "analytics");
        Directory.CreateDirectory(analyticsDirectory);
        SmokeAdvancedReportPeriods(errors);
        Environment.SetEnvironmentVariable("ARTE_ANALYTICS_CAPTURE_DIR", analyticsDirectory);
        Environment.SetEnvironmentVariable(
            "ARTE_ANALYTICS_CAPTURE_FILTER",
            string.Join(';', OneD2BVisualScenarios.Select(item => item.Scenario)));

        SmokeForm(
            "AnalyticsForm",
            () => new AnalyticsForm(
                new SmokeSalesAnalyticsQueryService(),
                new SmokeFinancialAnalyticsQueryService(),
                new SmokeCommercialAnalyticsQueryService(),
                new SmokeAnalyticsServiceProvider()),
            errors,
            AnalyticsSmokeSizes);
        SmokeAnalyticsDpiScaling(errors, 1.50f);
        SmokeAdvancedReportsVisualCapture(errors, outputDirectory);
        SmokeProfitabilityVisualCapture(errors, outputDirectory);
    }

    private static void SmokeAdvancedReportsVisualCapture(List<string> errors, string outputDirectory)
    {
        var scenarios = OneD2BVisualScenarios;
        var reports = new[]
        {
            (AdvancedReportKind.Products, "reports-products-subcategories"),
            (AdvancedReportKind.Inventory, "reports-inventory-subcategories"),
            (AdvancedReportKind.ProductProfitability, "reports-historical-subcategories"),
            (AdvancedReportKind.CurrentInventoryValue, "reports-current-valuation")
        };

        foreach (var (kind, filePrefix) in reports)
        {
            foreach (var scenario in scenarios)
            {
                try
                {
                    using var form = BuildSmokeReportsForm();
                    PrepareVisualForm(form, scenario.Size, scenario.Scale, scenario.Scenario);
                    form.Show();
                    NormalizeVisualSurface(form);
                    PumpUi(TimeSpan.FromMilliseconds(500));
                    PerformLayoutRecursive(form);
                    SelectAdvancedReportForVisual(form, kind, errors);
                    ValidateAdvancedReportVisual(form, kind, errors);
                    CaptureFormBitmap(
                        form,
                        Path.Combine(outputDirectory, $"{filePrefix}-{scenario.Scenario}.png"),
                        errors);

                    var grid = GetDescendants(form)
                        .OfType<DataGridView>()
                        .FirstOrDefault(item => item.Name == "AdvancedReportsGrid");
                    if (grid is not null && grid.Columns.Count > 0 && grid.Visible)
                    {
                        TryScrollToLastColumn(grid);
                        CaptureFormBitmap(
                            form,
                            Path.Combine(outputDirectory, $"{filePrefix}-{scenario.Scenario}-scrolled.png"),
                            errors);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(
                        $"Reportes avanzados {kind} {scenario.Scenario}: " +
                        $"{ex.GetType().Name} - {ex.Message}");
                }
            }
        }
    }

    private static void SmokeAdvancedReportPeriods(List<string> errors)
    {
        var service = new SmokeAdvancedReportQueryService();
        var scenarios = new (AnalyticsPeriodRequest Request, string ExpectedDisplay)[]
        {
            (new(AnalyticsPeriodKind.AllHistory), "Todo el historial"),
            (new(AnalyticsPeriodKind.Today), "Hoy"),
            (new(AnalyticsPeriodKind.CurrentWeek), "Semana actual"),
            (new(AnalyticsPeriodKind.CurrentMonth), "Mes actual"),
            (new(AnalyticsPeriodKind.CurrentQuarter), "Trimestre actual"),
            (new(AnalyticsPeriodKind.CurrentYear), "Ano actual"),
            (new(AnalyticsPeriodKind.CustomRange, DateOnly.FromDateTime(DateTime.Today.AddDays(-2)), DateOnly.FromDateTime(DateTime.Today)), "-")
        };

        try
        {
            AdvancedReportResult? baseline = null;
            foreach (var scenario in scenarios)
            {
                var request = new AdvancedReportRequest(
                    AdvancedReportKind.CurrentInventoryValue,
                    new AnalyticsQueryRequest(scenario.Request));
                var result = service.QueryAsync(request).GetAwaiter().GetResult();
                if (result.Period.Kind != scenario.Request.Kind)
                {
                    errors.Add(
                        $"Smoke temporal: el resultado cambió el tipo de período ({scenario.Request.Kind} -> {result.Period.Kind}).");
                }
                if (scenario.ExpectedDisplay != "-" &&
                    !string.Equals(result.Period.DisplayName, scenario.ExpectedDisplay, StringComparison.Ordinal))
                {
                    errors.Add(
                        $"Smoke temporal: se esperaba '{scenario.ExpectedDisplay}' y se obtuvo '{result.Period.DisplayName}'.");
                }
                if (scenario.Request.Kind == AnalyticsPeriodKind.CustomRange &&
                    string.IsNullOrWhiteSpace(result.Period.DisplayName))
                {
                    errors.Add("Smoke temporal: el rango personalizado no tiene una etiqueta de período.");
                }

                if (scenario.Request.Kind == AnalyticsPeriodKind.AllHistory)
                {
                    if (result.Period.Kind != AnalyticsPeriodKind.AllHistory ||
                        !string.Equals(result.Period.DisplayName, "Todo el historial", StringComparison.Ordinal))
                    {
                        errors.Add("Smoke temporal: Todo el historial no se propagó al resultado del reporte.");
                    }
                    baseline = result;
                }
                else if (baseline is not null)
                {
                    var baselineValuation = string.Join(
                        "|",
                        baseline.Rows.Select(row => string.Join(
                            "~",
                            new[] { "stock", "reserved", "available", "investment", "potential", "availableValue", "gain", "margin" }
                                .Select(key => Convert.ToString(row[key], System.Globalization.CultureInfo.InvariantCulture)))));
                    var currentValuation = string.Join(
                        "|",
                        result.Rows.Select(row => string.Join(
                            "~",
                            new[] { "stock", "reserved", "available", "investment", "potential", "availableValue", "gain", "margin" }
                                .Select(key => Convert.ToString(row[key], System.Globalization.CultureInfo.InvariantCulture)))));
                    if (!string.Equals(baselineValuation, currentValuation, StringComparison.Ordinal))
                    {
                        errors.Add(
                            $"Smoke temporal {scenario.Request.Kind}: la valoración actual cambió al cambiar el período histórico.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Smoke temporal: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void SmokeProfitabilityVisualCapture(List<string> errors, string outputDirectory)
    {
        foreach (var scenario in OneD2BVisualScenarios)
        {
            try
            {
                using var form = new ProductProfitabilityForm(
                    new SmokeAdvancedReportQueryService(),
                    new SmokeAdvancedReportExportService(),
                    new SmokeAdvancedReportFilterOptionsService(),
                    new SmokeSession());
                PrepareVisualForm(form, scenario.Size, scenario.Scale, scenario.Scenario);
                form.Show();
                NormalizeVisualSurface(form);
                PumpUi(TimeSpan.FromMilliseconds(700));
                PerformLayoutRecursive(form);
                ValidateProfitabilityVisual(form, errors);
                CaptureFormBitmap(
                    form,
                    Path.Combine(outputDirectory, $"profitability-subcategories-{scenario.Scenario}.png"),
                    errors);

                var grid = GetDescendants(form).OfType<DataGridView>().FirstOrDefault();
                if (grid is not null && grid.Columns.Count > 0 && grid.Visible)
                {
                    TryScrollToLastColumn(grid);
                    CaptureFormBitmap(
                        form,
                        Path.Combine(outputDirectory, $"profitability-subcategories-{scenario.Scenario}-scrolled.png"),
                        errors);
                }
            }
            catch (Exception ex)
            {
                errors.Add(
                    $"Rentabilidad por producto {scenario.Scenario}: " +
                    $"{ex.GetType().Name} - {ex.Message}");
            }
        }
    }

    private static void PrepareVisualForm(Form form, Size clientSize, float scale, string scenario)
    {
        form.WindowState = FormWindowState.Normal;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.ClientSize = clientSize;
        if (scale != 1f)
        {
            form.Scale(new SizeF(scale, scale));
            form.ClientSize = clientSize;
        }
        form.Tag = scenario;
    }

    private static void NormalizeVisualSurface(Form form)
    {
        if (form.Controls.Count == 0)
        {
            return;
        }

        var surface = form.Controls[0];
        surface.Dock = DockStyle.Fill;
        surface.Bounds = new Rectangle(Point.Empty, form.ClientSize);
        surface.PerformLayout();
    }

    private static void SelectAdvancedReportForVisual(
        ReportsForm form,
        AdvancedReportKind kind,
        List<string> errors)
    {
        var advanced = GetDescendants(form).OfType<AdvancedReportsControl>().FirstOrDefault();
        if (advanced is null)
        {
            errors.Add("ReportsForm: no se encontró AdvancedReportsControl para la captura visual.");
            return;
        }

        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var group = typeof(AdvancedReportsControl).GetField("_group", flags)?.GetValue(advanced) as ComboBox;
        var report = typeof(AdvancedReportsControl).GetField("_report", flags)?.GetValue(advanced) as ComboBox;
        var specificFilter = typeof(AdvancedReportsControl).GetField("_specificFilter", flags)?.GetValue(advanced) as ComboBox;
        var specificValue = typeof(AdvancedReportsControl).GetField("_specificValue", flags)?.GetValue(advanced) as ComboBox;
        var refresh = typeof(AdvancedReportsControl).GetMethod("RefreshAsync", flags);
        if (group is null || report is null || specificFilter is null || specificValue is null || refresh is null)
        {
            errors.Add("ReportsForm: no se pudieron inspeccionar los selectores del reporte avanzado.");
            return;
        }

        group.SelectedValue = AdvancedReportGroup.Inventory;
        PumpUi(TimeSpan.FromMilliseconds(100));
        report.SelectedValue = kind;
        PumpUi(TimeSpan.FromMilliseconds(150));
        if (kind is AdvancedReportKind.Products or AdvancedReportKind.Inventory or
            AdvancedReportKind.ProductProfitability or AdvancedReportKind.CurrentInventoryValue)
        {
            specificFilter.SelectedValue = AdvancedReportFilterKind.Subcategory;
            PumpUi(TimeSpan.FromMilliseconds(150));
            if (specificValue.Items.Count > 0)
            {
                specificValue.SelectedIndex = 0;
            }
        }

        var refreshTask = refresh.Invoke(advanced, new object?[] { 1, CancellationToken.None }) as Task;
        if (refreshTask is null)
        {
            errors.Add("ReportsForm: RefreshAsync no devolvió una tarea observable.");
            return;
        }

        refreshTask.GetAwaiter().GetResult();
        PumpUi(TimeSpan.FromMilliseconds(200));
        PerformLayoutRecursive(form);
    }

    private static void WaitForReportsVisualReady(
        ReportsForm form,
        AdvancedReportKind kind,
        List<string> errors,
        string stage,
        bool requireAllHistory,
        bool requireCoverage)
    {
        try
        {
            WaitForAnalyticsStage(
                $"ReportsForm {kind} {stage}",
                () =>
                {
                    var descendants = GetDescendants(form).ToList();
                    var coverage = descendants
                        .OfType<Label>()
                        .FirstOrDefault(label => label.Name == "AdvancedReportCoverageValue");
                    var status = descendants
                        .OfType<Label>()
                        .FirstOrDefault(label => label.Text.StartsWith("Reporte listo", StringComparison.Ordinal));
                    var grid = descendants
                        .OfType<DataGridView>()
                        .FirstOrDefault(item => item.Name == "AdvancedReportsGrid");
                    return (!requireCoverage || coverage is not null) &&
                        status is not null &&
                        (!requireAllHistory || status.Text.Contains("Todo el historial", StringComparison.Ordinal)) &&
                        grid is not null &&
                        grid.Rows.Count > 0;
                },
                TimeSpan.FromSeconds(5),
                _ => { });
        }
        catch (Exception ex)
        {
            errors.Add(
                $"ReportsForm {kind}: la carga {stage} no terminó: " +
                $"{ex.GetType().Name} - {ex.Message}. Estado={DescribeReportsVisualState(form)}");
        }
    }

    private static void InvokeOnUi(Control control, Action action)
    {
        if (control.InvokeRequired)
        {
            control.Invoke(action);
            return;
        }

        action();
    }

    private static string DescribeReportsVisualState(ReportsForm form)
    {
        var advanced = GetDescendants(form).OfType<AdvancedReportsControl>().FirstOrDefault();
        if (advanced is null)
        {
            return "AdvancedReportsControl=ausente";
        }

        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var report = typeof(AdvancedReportsControl).GetField("_report", flags)?.GetValue(advanced) as ComboBox;
        var group = typeof(AdvancedReportsControl).GetField("_group", flags)?.GetValue(advanced) as ComboBox;
        var initialized = typeof(AdvancedReportsControl).GetField("_initialized", flags)?.GetValue(advanced);
        var current = typeof(AdvancedReportsControl).GetField("_current", flags)?.GetValue(advanced);
        var status = typeof(AdvancedReportsControl).GetField("_status", flags)?.GetValue(advanced) as Label;
        var grid = typeof(AdvancedReportsControl).GetField("_grid", flags)?.GetValue(advanced) as DataGridView;
        var summary = typeof(AdvancedReportsControl).GetField("_summary", flags)?.GetValue(advanced) as Control;
        return $"initialized={initialized}, current={current is not null}, group={group?.SelectedValue}, " +
            $"report={report?.SelectedValue}, status='{status?.Text}', rows={grid?.Rows.Count}, " +
            $"visible={advanced.Visible}, size={advanced.ClientSize}, thread={Environment.CurrentManagedThreadId}, " +
            $"formInvoke={form.InvokeRequired}, controlInvoke={advanced.InvokeRequired}, " +
            $"gridInvoke={grid?.InvokeRequired}, summaryInvoke={summary?.InvokeRequired}";
    }

    private static void ValidateAdvancedReportVisual(
        Form form,
        AdvancedReportKind kind,
        List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        var actions = new[] { "Preparar reporte", "Cancelar", "Exportar Excel", "Vista previa / Imprimir" };
        foreach (var text in actions)
        {
            var button = descendants.OfType<Button>().FirstOrDefault(item => item.Text == text);
            if (button is null || !button.Visible)
            {
                errors.Add($"ReportsForm {kind}: acción '{text}' no visible en captura.");
                continue;
            }

            if (!IsInsideAncestor(button, button.Parent!) || !IsInsideAncestor(button, form))
            {
                errors.Add($"ReportsForm {kind}: acción '{text}' fuera de su contenedor ({BoundsInAncestor(button, form)}).");
            }

            var measured = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.NoPadding);
            if (measured.Width > button.ClientSize.Width - button.Padding.Horizontal - 4 ||
                measured.Height > button.ClientSize.Height - button.Padding.Vertical - 2)
            {
                errors.Add($"ReportsForm {kind}: texto de acción '{text}' recortado. Size={button.Size}, Text={measured}.");
            }
        }

        ValidateCoverageSummaryCard(form, kind, errors);

        if (kind == AdvancedReportKind.CurrentInventoryValue)
        {
            var status = descendants.OfType<Label>()
                .FirstOrDefault(label => label.Text.StartsWith("Reporte listo", StringComparison.Ordinal));
            if (status is null || !status.Text.Contains("Todo el historial", StringComparison.Ordinal))
            {
                errors.Add(
                    $"ReportsForm {kind}: el estado inferior no conserva 'Todo el historial'. " +
                    $"Actual='{status?.Text}'.");
            }
        }

        var grid = descendants.OfType<DataGridView>().FirstOrDefault(item => item.Name == "AdvancedReportsGrid");
        if (grid is null || !grid.Visible || grid.Rows.Count == 0 || !IsInsideAncestor(grid, form))
        {
            var inForm = grid is not null && IsInsideAncestor(grid, form);
            errors.Add(
                $"ReportsForm {kind}: tabla visible o contenida ausente en captura. " +
                $"Visible={grid?.Visible}, Rows={grid?.Rows.Count}, Grid={grid?.Bounds}, " +
                $"FormClient={form.ClientSize}, InForm={inForm}.");
            return;
        }

        var headers = grid.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText).ToHashSet(StringComparer.Ordinal);
        if (kind == AdvancedReportKind.Products &&
            (!headers.Contains("Categoría") || !headers.Contains("Subcategoría")))
        {
            errors.Add("ReportsForm Productos: faltan Categoría/Subcategoría en la tabla.");
        }
        if (kind == AdvancedReportKind.Inventory &&
            !new[] { "Producto", "SKU/variante", "Categoría", "Subcategoría", "Exhibición", "Bodega", "Reservado", "Disponible" }
                .All(headers.Contains))
        {
            errors.Add("ReportsForm Inventario: faltan columnas de inventario/subcategoría.");
        }
        if (kind == AdvancedReportKind.ProductProfitability &&
            (!headers.Contains("Categoría") || !headers.Contains("Subcategoría") ||
             !grid.Rows.Cast<DataGridViewRow>().Any(row => Convert.ToString(row.Cells["subcategory"].Value) == "No disponible (histórico)")))
        {
            errors.Add("ReportsForm Rentabilidad: faltan columnas o sentinel histórico de subcategoría.");
        }

        ValidateAdvancedReportGridArea(form, grid, errors);

        foreach (var combo in descendants.OfType<ComboBox>().Where(item => item.Visible))
        {
            if (!IsInsideAncestor(combo, combo.Parent!) || !IsInsideAncestor(combo, form))
            {
                errors.Add($"ReportsForm {kind}: selector fuera de su contenedor ({combo.AccessibleName ?? combo.Text}).");
            }
        }

        if (kind is AdvancedReportKind.Products or AdvancedReportKind.Inventory or AdvancedReportKind.ProductProfitability)
        {
            ValidateSubcategoryTextLayout(form, kind, grid, errors);
        }
        else if (kind == AdvancedReportKind.CurrentInventoryValue)
        {
            ValidateCurrentInventoryValuationVisual(grid, errors);
        }
    }

    private static void ValidateAdvancedReportGridArea(
        Form form,
        DataGridView grid,
        List<string> errors)
    {
        if (!IsInsideAncestor(grid, grid.Parent!) || !IsInsideAncestor(grid, form))
        {
            errors.Add(
                $"ReportsForm: tabla fuera del área cliente. Grid={BoundsInAncestor(grid, form)}, " +
                $"Client={form.ClientSize}.");
            return;
        }

        var rowCount = Math.Min(3, grid.Rows.Cast<DataGridViewRow>().Count(row => !row.IsNewRow));
        if (rowCount == 0)
        {
            return;
        }

        var rowHeight = grid.Rows.Cast<DataGridViewRow>()
            .Where(row => !row.IsNewRow)
            .Select(row => row.Height)
            .FirstOrDefault(height => height > 0);
        var horizontalScrollBarHeight = grid.Controls
            .OfType<HScrollBar>()
            .Where(scrollBar => scrollBar.Visible)
            .Select(scrollBar => scrollBar.Height)
            .FirstOrDefault();
        if (horizontalScrollBarHeight == 0 && (grid.ScrollBars & ScrollBars.Horizontal) != 0)
        {
            horizontalScrollBarHeight = SystemInformation.HorizontalScrollBarHeight;
        }
        var usableHeight = grid.ClientSize.Height - horizontalScrollBarHeight;
        var requiredHeight = grid.ColumnHeadersHeight + (rowHeight * rowCount) + horizontalScrollBarHeight + 2;
        if (usableHeight < grid.ColumnHeadersHeight + (rowHeight * rowCount))
        {
            errors.Add(
                $"ReportsForm: el grid no tiene área útil para encabezado y {rowCount} fila(s) completas. " +
                $"GridClient={grid.ClientSize}, requerido={requiredHeight}, filas={grid.Rows.Count}.");
        }

        for (var index = 0; index < rowCount; index++)
        {
            var rowBounds = grid.GetRowDisplayRectangle(index, cutOverflow: false);
            if (rowBounds.Top < grid.ColumnHeadersHeight || rowBounds.Bottom > usableHeight)
            {
                errors.Add(
                    $"ReportsForm: fila {index + 1} recortada verticalmente. " +
                    $"Bounds={rowBounds}, encabezado={grid.ColumnHeadersHeight}, usable={usableHeight}.");
            }
        }

        var gridBounds = BoundsInAncestor(grid, form);
        foreach (var action in GetDescendants(form).OfType<Button>()
                     .Where(button => button.Visible &&
                         button.Text is "Exportar Excel" or "Vista previa / Imprimir" or "Preparar reporte" or "Cancelar"))
        {
            if (gridBounds.IntersectsWith(BoundsInAncestor(action, form)))
            {
                errors.Add(
                    $"ReportsForm: grid y acción se superponen ({gridBounds} y {BoundsInAncestor(action, form)}).");
            }
        }
    }

    private static void ValidateCurrentInventoryValuationVisual(
        DataGridView grid,
        List<string> errors)
    {
        var headers = grid.Columns.Cast<DataGridViewColumn>()
            .Select(column => column.HeaderText)
            .ToHashSet(StringComparer.Ordinal);
        var required = new[]
        {
            "Stock físico", "Reservado", "Disponible", "Inversión actual",
            "Venta potencial", "Venta disponible", "Ganancia bruta potencial",
            "Margen bruto potencial", "Calidad/cobertura"
        };
        if (!required.All(headers.Contains))
        {
            errors.Add("ReportsForm Valoración actual: faltan columnas de valoración o cobertura.");
        }

        var values = grid.Rows.Cast<DataGridViewRow>()
            .SelectMany(row => row.Cells.Cast<DataGridViewCell>())
            .Select(cell => Convert.ToString(cell.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expected in new[]
        {
            "Pendiente de ubicación", "Precio de venta cero", "No calculable",
            "Legacy sin ubicación"
        })
        {
            if (!values.Any(value => value?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true))
            {
                errors.Add($"ReportsForm Valoración actual: falta evidencia '{expected}'.");
            }
        }
    }

    private static void ValidateSubcategoryTextLayout(
        Form form,
        AdvancedReportKind kind,
        DataGridView grid,
        List<string> errors)
    {
        var specificFilter = GetDescendants(form)
            .OfType<ComboBox>()
            .FirstOrDefault(combo => combo.Name == "AdvancedReportsSpecificFilter");
        if (specificFilter is null || !specificFilter.Visible)
        {
            errors.Add($"ReportsForm {kind}: no se encontró el filtro específico visible.");
        }
        else
        {
            const string expectedFilterText = "Subcategoría";
            var measuredFilter = TextRenderer.MeasureText(
                expectedFilterText,
                specificFilter.Font,
                Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            var arrowAndBorder = SystemInformation.VerticalScrollBarWidth + 8;
            var availableFilterWidth = specificFilter.ClientSize.Width -
                specificFilter.Padding.Horizontal - arrowAndBorder;
            if (!string.Equals(specificFilter.Text, expectedFilterText, StringComparison.Ordinal) ||
                measuredFilter.Width > availableFilterWidth)
            {
                errors.Add(
                    $"ReportsForm {kind}: la opción Subcategoría no cabe completa en el filtro. " +
                    $"Text='{specificFilter.Text}', medida={measuredFilter.Width}, disponible={availableFilterWidth}, " +
                    $"Size={specificFilter.ClientSize}.");
            }
        }

        var subcategory = grid.Columns
            .Cast<DataGridViewColumn>()
            .FirstOrDefault(column => string.Equals(column.Name, "subcategory", StringComparison.OrdinalIgnoreCase));
        if (subcategory is null)
        {
            errors.Add($"ReportsForm {kind}: falta la columna Subcategoría.");
            return;
        }

        const string expectedValue = "No disponible (histórico)";
        var cellFont = subcategory.DefaultCellStyle.Font ?? grid.DefaultCellStyle.Font ?? grid.Font;
        var headerFont = subcategory.HeaderCell.InheritedStyle.Font ??
            grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
        var expectedValueWidth = TextRenderer.MeasureText(
            expectedValue,
            cellFont,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        var headerWidth = TextRenderer.MeasureText(
            subcategory.HeaderText,
            headerFont,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        var availableColumnWidth = subcategory.Width - grid.DefaultCellStyle.Padding.Horizontal - 24;
        var hasExpectedValue = grid.Rows
            .Cast<DataGridViewRow>()
            .Any(row => string.Equals(
                Convert.ToString(row.Cells[subcategory.Index].Value),
                expectedValue,
                StringComparison.Ordinal));
        if (subcategory.AutoSizeMode != DataGridViewAutoSizeColumnMode.None ||
            subcategory.DefaultCellStyle.WrapMode == DataGridViewTriState.True ||
            expectedValueWidth > availableColumnWidth ||
            headerWidth > availableColumnWidth ||
            (kind == AdvancedReportKind.ProductProfitability && !hasExpectedValue))
        {
            errors.Add(
                $"ReportsForm {kind}: texto de Subcategoría recortado o ausente. " +
                $"Header={headerWidth}, Value={expectedValueWidth}, disponible={availableColumnWidth}, " +
                $"Width={subcategory.Width}, AutoSize={subcategory.AutoSizeMode}, " +
                $"Wrap={subcategory.DefaultCellStyle.WrapMode}, HasValue={hasExpectedValue}.");
        }
    }

    private static void ValidateCoverageSummaryCard(
        Form form,
        AdvancedReportKind kind,
        List<string> errors)
    {
        var coverageValue = GetDescendants(form)
            .OfType<Label>()
            .FirstOrDefault(label => label.Name == "AdvancedReportCoverageValue");
        var coverageCard = GetDescendants(form)
            .FirstOrDefault(control => control.Name == "AdvancedReportCoverageSummaryCard");
        if (coverageValue is null || coverageCard is null)
        {
            errors.Add($"ReportsForm {kind}: no se encontró la tarjeta de cobertura.");
            return;
        }

        const string expectedCoverageNote = "Cobertura histórica exacta: 90%.";
        if (!string.Equals(coverageValue.Text, expectedCoverageNote, StringComparison.Ordinal) ||
            coverageValue.Text.Contains("...", StringComparison.Ordinal))
        {
            errors.Add(
                $"ReportsForm {kind}: CoverageNote truncado. " +
                $"Actual='{coverageValue.Text}', Esperado='{expectedCoverageNote}'.");
        }

        if (coverageValue.AutoEllipsis)
        {
            errors.Add($"ReportsForm {kind}: AutoEllipsis sigue activo en CoverageNote.");
        }

        var availableWidth = Math.Max(1, coverageValue.ClientSize.Width);
        var wrapped = TextRenderer.MeasureText(
            coverageValue.Text,
            coverageValue.Font,
            new Size(availableWidth, 1000),
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
        if (wrapped.Width > availableWidth + 1 || wrapped.Height > coverageValue.ClientSize.Height + 1)
        {
            errors.Add(
                $"ReportsForm {kind}: CoverageNote recortado. " +
                $"Text={wrapped}, Client={coverageValue.ClientSize}, Card={coverageCard.Size}.");
        }

        var parent = coverageCard.Parent;
        if (parent is null || !IsInsideAncestor(coverageCard, parent) || !IsInsideAncestor(coverageCard, form))
        {
            errors.Add(
                $"ReportsForm {kind}: tarjeta de cobertura fuera de su contenedor " +
                $"({BoundsInAncestor(coverageCard, form)}, client={form.ClientSize}).");
        }

        if (parent is not null)
        {
            var siblings = parent.Controls
                .Cast<Control>()
                .Where(control => control.Visible)
                .ToArray();
            for (var index = 0; index < siblings.Length; index++)
            {
                for (var next = index + 1; next < siblings.Length; next++)
                {
                    if (siblings[index].Bounds.IntersectsWith(siblings[next].Bounds))
                    {
                        errors.Add(
                            $"ReportsForm {kind}: tarjetas de resumen superpuestas " +
                            $"('{siblings[index].Name}' y '{siblings[next].Name}').");
                        }
                    }
                }

            foreach (var card in siblings)
            {
                var title = card.Controls
                    .Cast<Control>()
                    .OfType<Label>()
                    .FirstOrDefault(label => label.Dock == DockStyle.Top);
                if (title is null)
                {
                    continue;
                }

                var measuredTitle = TextRenderer.MeasureText(
                    title.Text,
                    title.Font,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                if (title.Text.Contains("...", StringComparison.Ordinal) ||
                    measuredTitle.Width > title.ClientSize.Width + 1 ||
                    measuredTitle.Height > title.ClientSize.Height + 1)
                {
                    errors.Add(
                        $"ReportsForm {kind}: título de tarjeta recortado. " +
                        $"Title='{title.Text}', Text={measuredTitle}, Client={title.ClientSize}, Card={card.Size}.");
                }
            }
        }
    }

    private static void ValidateProfitabilityVisual(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        var grid = descendants.OfType<DataGridView>().FirstOrDefault();
        if (grid is null || !grid.Visible || grid.Rows.Count < 3 || !IsInsideAncestor(grid, form))
        {
            var inForm = grid is not null && IsInsideAncestor(grid, form);
            errors.Add(
                "Rentabilidad por producto: tabla con datos no visible o fuera del formulario. " +
                $"Visible={grid?.Visible}, Rows={grid?.Rows.Count}, Grid={grid?.Bounds}, " +
                $"FormClient={form.ClientSize}, InForm={inForm}.");
            return;
        }

        var headers = grid.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText).ToHashSet(StringComparer.Ordinal);
        if (!headers.Contains("Categoría") || !headers.Contains("Subcategoría") ||
            !grid.Rows.Cast<DataGridViewRow>().Any(row => Convert.ToString(row.Cells["subcategory"].Value) == "Sin subcategoría") ||
            !grid.Rows.Cast<DataGridViewRow>().Any(row => Convert.ToString(row.Cells["subcategory"].Value) == "No disponible (histórico)"))
        {
            errors.Add("Rentabilidad por producto: faltan columnas o estados de subcategoría en la tabla.");
        }

        foreach (var text in new[] { "Buscar", "Limpiar", "Exportar todo", "Exportar selección", "Vista previa / PDF", "Cerrar" })
        {
            var button = descendants.OfType<Button>().FirstOrDefault(item => item.Text == text);
            if (button is null || !button.Visible || !IsInsideAncestor(button, form))
            {
                errors.Add($"Rentabilidad por producto: botón '{text}' no está completamente visible.");
            }
        }

        foreach (var combo in descendants.OfType<ComboBox>().Where(item => item.Visible))
        {
            if (!IsInsideAncestor(combo, combo.Parent!) || !IsInsideAncestor(combo, form))
            {
                errors.Add("Rentabilidad por producto: selector de filtros fuera de su contenedor.");
            }
        }
    }

    private static void TryScrollToLastColumn(DataGridView grid)
    {
        try
        {
            var last = grid.Columns.Cast<DataGridViewColumn>()
                .LastOrDefault(column => column.Visible);
            if (last is not null)
            {
                grid.FirstDisplayedScrollingColumnIndex = last.Index;
                grid.HorizontalScrollingOffset = Math.Max(0, grid.HorizontalScrollingOffset);
                PumpUi(TimeSpan.FromMilliseconds(100));
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // La tabla cabe completa; no hay scroll que aplicar.
        }
        catch (InvalidOperationException)
        {
            // El control todavía no tiene una fila/handle desplazable.
        }
    }

    private static void CaptureFormBitmap(Form form, string path, List<string> errors)
    {
        try
        {
            PumpUi(TimeSpan.FromMilliseconds(120));
            PerformLayoutRecursive(form);
            var surface = form.Controls.Count == 0 ? form : form.Controls[0];
            var size = surface.ClientSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                errors.Add($"Captura visual: superficie inválida para {Path.GetFileName(path)} ({size}).");
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            using var bitmap = new Bitmap(size.Width, size.Height);
            surface.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception ex)
        {
            errors.Add($"Captura visual {Path.GetFileName(path)}: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void SmokeAffectedVisualDpiScaling(List<string> errors, float scale)
    {
        var permissions = new SmokePermissionService();
        var forms = new (string Name, Func<Form> Factory)[]
        {
            ("EmployeePermissionsForm", () => new EmployeePermissionsForm(null!, null!)),
            ("AccountsReceivableForm", () => new AccountsReceivableForm(null!, null!, null!, null!, null!, null!, null!, null!, null!)),
            ("EmployeeInventoryLookupForm", () => new EmployeeInventoryLookupForm(
                new SmokeEmployeeInventoryLookupService(),
                new SmokeEmployeeInventoryAssetService())),
            ("CustomOrdersForm", () => new CustomOrdersForm(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)),
            ("CommercialAlertsForm", () => new CommercialAlertsForm(
                new SmokeCommercialAlertQueryService(), permissions, new SmokeServiceProvider()))
        };

        foreach (var (name, factory) in forms)
        {
            try
            {
                using var form = factory();
                form.WindowState = FormWindowState.Normal;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.ShowInTaskbar = false;
                form.ClientSize = new Size(1366, 768);
                form.CreateControl();
                form.Scale(new SizeF(scale, scale));
                form.ClientSize = new Size(1366, 768);
                form.PerformLayout();
                PerformLayoutRecursive(form);
                ValidateForm(name, form, errors);
            }
            catch (Exception ex)
            {
                errors.Add($"{name} DPI {scale:P0}: {ex.GetType().Name} - {ex.Message}");
            }
        }
    }

    private static void SmokeEmployeeInventoryAsyncTeardown(List<string> errors)
    {
        var imagePath = Path.Combine(Path.GetTempPath(), $"employee-inventory-smoke-{Guid.NewGuid():N}.png");
        Exception? uiException = null;
        ThreadExceptionEventHandler handler = (_, eventArgs) => uiException ??= eventArgs.Exception;
        System.Windows.Forms.Application.ThreadException += handler;

        try
        {
            using (var bitmap = new Bitmap(8, 8))
                bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);

            using var form = new EmployeeInventoryLookupForm(
                new SmokeEmployeeInventoryLookupService(),
                new SmokeEmployeeInventoryAssetService(imagePath, delayMilliseconds: 35));
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.ClientSize = new Size(1180, 700);
            form.Show();

            WaitForAnalyticsStage(
                "Consulta inventario cargada",
                () => GetDescendants(form).OfType<DataGridView>().Single().Rows.Count >= 3,
                TimeSpan.FromSeconds(2),
                _ => { });

            var grid = GetDescendants(form).OfType<DataGridView>().Single();
            var visibleColumns = grid.Columns
                .Cast<DataGridViewColumn>()
                .Where(column => column.Visible)
                .ToList();
            if (visibleColumns.Count != 9)
            {
                errors.Add(
                    $"EmployeeInventoryLookupForm: se esperaban nueve columnas visibles, " +
                    $"pero hay {visibleColumns.Count}.");
            }

            foreach (var column in visibleColumns)
            {
                var headerFont = column.HeaderCell.InheritedStyle.Font ??
                    grid.ColumnHeadersDefaultCellStyle.Font ??
                    grid.Font;
                var measuredHeader = TextRenderer.MeasureText(
                    column.HeaderText,
                    headerFont,
                    Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                if (column.Width < measuredHeader.Width + 16)
                {
                    errors.Add(
                        $"EmployeeInventoryLookupForm: el encabezado '{column.HeaderText}' " +
                        $"no cabe ({column.Width}px, requiere {measuredHeader.Width + 16}px).");
                }
            }

            var disponibleColumn = visibleColumns
                .SingleOrDefault(column => column.HeaderText == "Disponible");
            if (disponibleColumn is null)
            {
                errors.Add("EmployeeInventoryLookupForm: no se encontró la columna Disponible.");
            }
            else
            {
                var totalColumnsWidth = visibleColumns
                    .Where(column => !column.Frozen)
                    .Sum(column => column.Width);
                if (totalColumnsWidth <= grid.ClientSize.Width)
                {
                    errors.Add(
                        $"EmployeeInventoryLookupForm: el ancho de columnas ({totalColumnsWidth}px) " +
                        $"no supera el área del grid ({grid.ClientSize.Width}px); no habría scroll horizontal real.");
                }
            }

            var salePriceColumn = visibleColumns.SingleOrDefault(column => column.HeaderText == "Precio de venta");
            if (salePriceColumn is null)
            {
                errors.Add("EmployeeInventoryLookupForm: no se encontró la columna Precio de venta.");
            }
            else if (grid.Rows.Count > 0 && string.IsNullOrWhiteSpace(grid.Rows[0].Cells[salePriceColumn.Index].FormattedValue?.ToString()))
            {
                errors.Add("EmployeeInventoryLookupForm: la proyección de precio de venta quedó vacía.");
            }

            var categoryColumn = grid.Columns
                .Cast<DataGridViewColumn>()
                .SingleOrDefault(column => column.HeaderText == "Categoría");
            var subcategoryColumn = grid.Columns
                .Cast<DataGridViewColumn>()
                .SingleOrDefault(column => column.HeaderText == "Subcategoría");
            if (categoryColumn is null || subcategoryColumn is null)
            {
                errors.Add("EmployeeInventoryLookupForm: el smoke no encontró las columnas de categoría/subcategoría.");
            }
            else
            {
                var categoryValues = grid.Rows
                    .Cast<DataGridViewRow>()
                    .Where(row => !row.IsNewRow)
                    .Select(row => row.Cells[categoryColumn.Index].Value?.ToString())
                    .ToList();
                var subcategoryValues = grid.Rows
                    .Cast<DataGridViewRow>()
                    .Where(row => !row.IsNewRow)
                    .Select(row => row.Cells[subcategoryColumn.Index].Value?.ToString())
                    .ToList();
                if (!categoryValues.Contains("Artesanías", StringComparer.Ordinal))
                {
                    errors.Add("EmployeeInventoryLookupForm: el smoke no observó la categoría proyectada.");
                }

                if (!subcategoryValues.Contains("Sin subcategoría", StringComparer.Ordinal))
                {
                    errors.Add("EmployeeInventoryLookupForm: el smoke no observó el estado 'Sin subcategoría'.");
                }
            }

            var configuredOutputDirectory = Environment.GetEnvironmentVariable(
                "ARTE_ACCOUNT_PAYMENT_LAYOUT_SMOKE_OUTPUT");
            if (!string.IsNullOrWhiteSpace(configuredOutputDirectory))
            {
                Directory.CreateDirectory(configuredOutputDirectory);
                var initialScreenshotPath = Path.Combine(
                    configuredOutputDirectory,
                    "employee-inventory-initial.png");
                using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(initialScreenshotPath, System.Drawing.Imaging.ImageFormat.Png);
            }

            if (disponibleColumn is not null)
            {
                try
                {
                    grid.FirstDisplayedScrollingColumnIndex = disponibleColumn.Index;
                    System.Windows.Forms.Application.DoEvents();
                    grid.PerformLayout();

                    var headerRectangle = grid.GetColumnDisplayRectangle(disponibleColumn.Index, true);
                    if (headerRectangle.Width <= 0 ||
                        headerRectangle.Left < 0 ||
                        headerRectangle.Right > grid.ClientSize.Width)
                    {
                        errors.Add(
                            $"EmployeeInventoryLookupForm: el encabezado Disponible no quedó visible " +
                            $"después del scroll ({headerRectangle}, grid={grid.ClientSize}).");
                    }

                    if (grid.Rows.Count > 0)
                    {
                        var cellRectangle = grid.GetCellDisplayRectangle(disponibleColumn.Index, 0, true);
                        if (cellRectangle.Width <= 0 ||
                            cellRectangle.Left < 0 ||
                            cellRectangle.Right > grid.ClientSize.Width)
                        {
                            errors.Add(
                                $"EmployeeInventoryLookupForm: la celda Disponible no quedó visible " +
                                $"después del scroll ({cellRectangle}, grid={grid.ClientSize}).");
                        }

                        var cellValue = grid.Rows[0].Cells[disponibleColumn.Index].FormattedValue?.ToString();
                        if (string.IsNullOrWhiteSpace(cellValue))
                        {
                            errors.Add("EmployeeInventoryLookupForm: la celda Disponible quedó sin valor tras el scroll.");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(configuredOutputDirectory))
                    {
                        var scrolledScreenshotPath = Path.Combine(
                            configuredOutputDirectory,
                            "employee-inventory-scrolled-disponible.png");
                        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(scrolledScreenshotPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(
                        $"EmployeeInventoryLookupForm: no se pudo desplazar hasta Disponible: " +
                        $"{ex.GetType().Name} - {ex.Message}");
                }
            }

            for (var index = 0; index < Math.Min(3, grid.Rows.Count); index++)
            {
                grid.CurrentCell = grid.Rows[index].Cells[0];
                grid.Rows[index].Selected = true;
                System.Windows.Forms.Application.DoEvents();
            }

            form.Close();
            form.Dispose();
            var until = DateTime.UtcNow.AddMilliseconds(180);
            while (DateTime.UtcNow < until)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(10);
            }

            if (uiException is not null)
            {
                errors.Add(
                    $"EmployeeInventoryLookupForm cierre async: {uiException.GetType().Name} - {uiException.Message}");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"EmployeeInventoryLookupForm cierre async: {ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
            System.Windows.Forms.Application.ThreadException -= handler;
            DeleteTemporarySmokeFile(imagePath, errors);
        }
    }

    private static void DeleteTemporarySmokeFile(string path, List<string> errors)
    {
        const int maximumAttempts = 40;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            if (!File.Exists(path))
                return;

            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
            }

            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(25);
        }

        errors.Add(
            $"Limpieza temporal del smoke: no se pudo eliminar '{Path.GetFileName(path)}' " +
            $"después de {maximumAttempts} intentos: {lastError?.GetType().Name} - {lastError?.Message}");
    }

    private static void SmokeAdminDashboardSidebarStateTransitions(List<string> errors)
    {
        try
        {
            using var form = BuildSmokeDashboard();
            form.WindowState = FormWindowState.Normal;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.ClientSize = new Size(1280, 720);
            form.Show();
            System.Windows.Forms.Application.DoEvents();
            ValidateAdminDashboardSidebarTransitions(form, errors, "20 ciclos", cycles: 20);
        }
        catch (Exception ex)
        {
            errors.Add($"AdminDashboardForm sidebar 20 ciclos: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void ValidateAdminDashboardSidebarTransitions(
        AdminDashboardForm form,
        List<string> errors,
        string scenario,
        int cycles)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var collapseMethod = typeof(AdminDashboardForm).GetMethod("SetSidebarCollapsed", flags)
            ?? throw new InvalidOperationException("No se encontro SetSidebarCollapsed.");
        var shell = (TableLayoutPanel?)typeof(AdminDashboardForm).GetField("_shell", flags)?.GetValue(form)
            ?? throw new InvalidOperationException("No se encontro _shell.");
        var sidebar = (Panel?)typeof(AdminDashboardForm).GetField("_sidebar", flags)?.GetValue(form)
            ?? throw new InvalidOperationException("No se encontro _sidebar.");
        var sidebarLayout = (TableLayoutPanel?)typeof(AdminDashboardForm).GetField("_sidebarLayout", flags)?.GetValue(form)
            ?? throw new InvalidOperationException("No se encontro _sidebarLayout.");
        var logo = (PictureBox?)typeof(AdminDashboardForm).GetField("_sidebarLogo", flags)?.GetValue(form)
            ?? throw new InvalidOperationException("No se encontro _sidebarLogo.");
        var brandLabel = (Label?)typeof(AdminDashboardForm).GetField("_brandLabel", flags)?.GetValue(form)
            ?? throw new InvalidOperationException("No se encontro _brandLabel.");
        var toggle = (Button?)typeof(AdminDashboardForm).GetField("_btnCollapseMenu", flags)?.GetValue(form)
            ?? throw new InvalidOperationException("No se encontro _btnCollapseMenu.");
        var navigation = (FlowLayoutPanel?)typeof(AdminDashboardForm).GetField("_sidebarNavigation", flags)?.GetValue(form)
            ?? throw new InvalidOperationException("No se encontro _sidebarNavigation.");
        var buttons = (IEnumerable<Button>?)typeof(AdminDashboardForm).GetField("_sidebarButtons", flags)?.GetValue(form)
            ?? throw new InvalidOperationException("No se encontro _sidebarButtons.");
        var toolTip = (ToolTip?)typeof(AdminDashboardForm).GetField("_navigationToolTip", flags)?.GetValue(form)
            ?? throw new InvalidOperationException("No se encontro _navigationToolTip.");
        var buttonList = buttons.ToList();

        collapseMethod.Invoke(form, [false]);
        CompleteSidebarLayout(form);
        var expandedWidth = shell.GetColumnWidths()[0];
        ValidateAdminDashboardSidebarState(
            collapsed: false,
            scenario,
            sidebar,
            sidebarLayout,
            logo,
            brandLabel,
            toggle,
            navigation,
            buttonList,
            toolTip,
            errors);

        for (var cycle = 1; cycle <= cycles; cycle++)
        {
            collapseMethod.Invoke(form, [true]);
            CompleteSidebarLayout(form);
            var collapsedWidth = shell.GetColumnWidths()[0];
            if (collapsedWidth >= expandedWidth)
            {
                errors.Add($"AdminDashboardForm sidebar {scenario}: el ancho no disminuyo en el ciclo {cycle} ({expandedWidth} -> {collapsedWidth}).");
            }
            ValidateAdminDashboardSidebarState(
                collapsed: true,
                scenario,
                sidebar,
                sidebarLayout,
                logo,
                brandLabel,
                toggle,
                navigation,
                buttonList,
                toolTip,
                errors);

            collapseMethod.Invoke(form, [false]);
            CompleteSidebarLayout(form);
            if (shell.GetColumnWidths()[0] != expandedWidth)
            {
                errors.Add($"AdminDashboardForm sidebar {scenario}: el ancho expandido no se restauro en el ciclo {cycle}.");
            }
            ValidateAdminDashboardSidebarState(
                collapsed: false,
                scenario,
                sidebar,
                sidebarLayout,
                logo,
                brandLabel,
                toggle,
                navigation,
                buttonList,
                toolTip,
                errors);
        }
    }

    private static void CompleteSidebarLayout(Control form)
    {
        System.Windows.Forms.Application.DoEvents();
        PerformLayoutRecursive(form);
    }

    private static void ValidateAdminDashboardSidebarState(
        bool collapsed,
        string scenario,
        Panel sidebar,
        TableLayoutPanel sidebarLayout,
        PictureBox logo,
        Label brandLabel,
        Button toggle,
        FlowLayoutPanel navigation,
        IReadOnlyList<Button> buttons,
        ToolTip toolTip,
        List<string> errors)
    {
        var state = collapsed ? "collapsed" : "expanded";
        if (!IsInsideAncestor(sidebarLayout, sidebar))
        {
            errors.Add($"AdminDashboardForm sidebar {scenario}/{state}: layout raiz fuera del area visible ({sidebarLayout.Bounds} en {sidebar.ClientSize}).");
        }
        if (!logo.Visible)
        {
            errors.Add($"AdminDashboardForm sidebar {scenario}/{state}: logo oculto.");
        }
        else if (!IsInsideAncestor(logo, sidebar))
        {
            errors.Add($"AdminDashboardForm sidebar {scenario}/{state}: logo fuera del area visible ({BoundsInAncestor(logo, sidebar)} en {sidebar.ClientSize}).");
        }
        if (!toggle.Visible || toggle.Width <= 0 || toggle.Height <= 0)
        {
            errors.Add($"AdminDashboardForm sidebar {scenario}/{state}: toggle oculto o sin area clickeable.");
        }
        else if (!IsInsideAncestor(toggle, sidebar))
        {
            errors.Add($"AdminDashboardForm sidebar {scenario}/{state}: toggle fuera del area visible ({BoundsInAncestor(toggle, sidebar)} en {sidebar.ClientSize}).");
        }
        if (brandLabel.Visible == collapsed)
        {
            errors.Add($"AdminDashboardForm sidebar {scenario}/{state}: visibilidad incorrecta del texto de marca.");
        }
        if (navigation.HorizontalScroll.Visible)
        {
            errors.Add($"AdminDashboardForm sidebar {scenario}/{state}: aparecio scroll horizontal.");
        }

        foreach (var button in buttons)
        {
            if (!button.Visible || button.Width <= 0 || button.Height <= 0)
            {
                errors.Add($"AdminDashboardForm sidebar {scenario}/{state}: acceso oculto o sin area clickeable.");
            }
            if (button is not SidebarNavigationButton navigationButton || string.IsNullOrEmpty(navigationButton.IconGlyph))
            {
                errors.Add($"AdminDashboardForm sidebar {scenario}/{state}: acceso sin icono independiente.");
            }
            if (!IsInsideAncestor(button, sidebar, horizontalOnly: true))
            {
                errors.Add($"AdminDashboardForm sidebar {scenario}/{state}: acceso fuera del area visible ({BoundsInAncestor(button, sidebar)} en {sidebar.ClientSize}).");
            }
            if (collapsed ? button.Text.Length != 0 : button.Text.Length == 0)
            {
                errors.Add($"AdminDashboardForm sidebar {scenario}/{state}: texto de acceso en estado incorrecto.");
            }
            if (collapsed && button is SidebarNavigationButton collapsedButton && !collapsedButton.CenterIcon)
            {
                errors.Add($"AdminDashboardForm sidebar {scenario}/{state}: icono no centrado.");
            }
            if (string.IsNullOrWhiteSpace(toolTip.GetToolTip(button)))
            {
                errors.Add($"AdminDashboardForm sidebar {scenario}/{state}: acceso sin ToolTip.");
            }
        }
    }

    private static bool IsInsideAncestor(Control control, Control ancestor, bool horizontalOnly = false)
    {
        var bounds = BoundsInAncestor(control, ancestor);
        var horizontallyInside = bounds.Left >= 0 && bounds.Right <= ancestor.ClientSize.Width;
        var verticallyInside = bounds.Top >= 0 && bounds.Bottom <= ancestor.ClientSize.Height;
        return horizontallyInside && (horizontalOnly || verticallyInside);
    }

    private static Rectangle BoundsInAncestor(Control control, Control ancestor)
    {
        var location = ancestor.PointToClient(control.PointToScreen(Point.Empty));
        return new Rectangle(location, control.Size);
    }

    private static void SmokeProductEditFormFocusAndTabOrder(List<string> errors)
    {
        try
        {
            using var form = new ProductEditForm(new SmokeProductService(), new SmokeCategoryService());
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Show();
            System.Windows.Forms.Application.DoEvents();

            var fields = new[]
            {
                "_txtNombre", "_txtCodigo", "_cmbCategoria", "_cmbSubcategoria", "_txtDescripcion", "_cmbModo",
                "_numPrecioCompra", "_numPrecioVenta", "_numStockActual",
                "_cmbUbicacionInicial", "_numStockMinimo", "_btnGuardar", "_btnCancelar"
            };
            var controls = fields
                .Select(field => typeof(ProductEditForm)
                    .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(form) as Control)
                .ToArray();

            if (controls.Any(control => control is null))
            {
                errors.Add("ProductEditForm: no se pudieron inspeccionar todos los controles del recorrido TAB.");
                return;
            }
            if (!ReferenceEquals(form.ActiveControl, controls[0]))
            {
                errors.Add($"ProductEditForm: el foco inicial esta en '{form.ActiveControl?.GetType().Name ?? "ningun control"}' y no en Nombre.");
            }
            for (var index = 0; index < controls.Length; index++)
            {
                if (controls[index]!.TabIndex != index)
                {
                    errors.Add($"ProductEditForm: orden TAB incorrecto en {fields[index]} ({controls[index]!.TabIndex}, esperado {index}).");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"ProductEditForm foco/TAB: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void SmokeForm(
        string name,
        Func<Form> factory,
        List<string> errors,
        IReadOnlyList<Size>? sizes = null)
    {
        foreach (var size in sizes ?? SmokeSizes)
        {
            try
            {
                using var form = factory();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.ShowInTaskbar = false;
                if (form is CashRegisterForm)
                {
                    if (form.WindowState != FormWindowState.Maximized)
                    {
                        errors.Add($"{name}: la ventana de Caja diaria no inicia maximizada.");
                    }
                    form.WindowState = FormWindowState.Normal;
                }
                if (form is MySalesTodayForm)
                {
                    if (form.WindowState != FormWindowState.Maximized)
                    {
                        errors.Add($"{name}: la ventana no inicia maximizada.");
                    }
                    form.WindowState = FormWindowState.Normal;
                }
                if (name == "AnalyticsForm")
                {
                    form.WindowState = FormWindowState.Normal;
                }
                form.ClientSize = size;
                if (name == "AnalyticsForm")
                {
                    form.Tag = $"{size.Width}x{size.Height}-dpi100";
                }
                if (name == "AdminDashboardForm")
                {
                    form.WindowState = FormWindowState.Normal;
                    typeof(AdminDashboardForm)
                        .GetField("_initialPasswordChecked", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?.SetValue(form, true);
                }
                if (form is CreditAlertsLayoutSmokeForm
                    or AccountPaymentsHistoryLayoutSmokeForm
                    or PurchasePaymentForm
                    or SupplierPaymentForm || name is "CashHistoryForm" or "QuotationEditForm" or "QuotationDetailForm" or "AdminDashboardForm"
                    or "QuotationProductPickerForm" or "PurchaseProductPickerForm" or "QuotationConversionDateForm" or "CommercialAlertsForm"
                    or "AnalyticsForm" or "ReportsForm" or "ExcelImportForm" or "SystemConfigForm" or "CashSessionDetailForm"
                    or "CashContributionForm" or "MySalesTodayForm")
                {
                    form.Show();
                    if (name == "AnalyticsForm")
                    {
                        form.ClientSize = size;
                    }
                    System.Windows.Forms.Application.DoEvents();
                }
                else
                {
                    form.CreateControl();
                }

                form.PerformLayout();
                PerformLayoutRecursive(form);
                ValidateForm(name, form, errors);
            }
            catch (Exception ex)
            {
                errors.Add($"{name} {size.Width}x{size.Height}: {ex.GetType().Name} - {ex.Message}");
            }
        }
    }

    private static void ValidateForm(string name, Form form, List<string> errors)
    {
        if (name is "CashOpeningReference" or "CashOpeningReferenceAdmin")
        {
            ValidateCashOpeningReferenceForm(form, name == "CashOpeningReferenceAdmin", errors);
        }
        else if (name == "AccountPaymentForm")
        {
            ValidateAccountPaymentForm(form, errors);
        }
        else if (name == "CreditAlertsForm")
        {
            ValidateCreditAlertsForm(form, errors);
        }
        else if (name == "AccountPaymentsHistoryForm")
        {
            ValidateAccountPaymentsHistoryForm(form, errors);
        }
        else if (name == "AccountsReceivableForm")
        {
            ValidateAccountsReceivableForm(form, errors);
        }
        else if (name == "LoginForm")
        {
            ValidateLoginForm(form, errors);
        }
        else if (name == "SystemConfigForm")
        {
            ValidateSystemConfigForm(form, errors);
        }
        else if (name == "AccountsPayableForm")
        {
            ValidateAccountsPayableForm(form, errors);
        }
        else if (name.StartsWith("Supplier", StringComparison.Ordinal))
        {
            ValidateSupplierForms(name, form, errors);
        }
        else if (name.StartsWith("Purchase", StringComparison.Ordinal) || name == "PurchasesForm")
        {
            ValidatePurchaseForms(name, form, errors);
        }
        else if (name == "GeneratedPdfActionsForm")
        {
            var descendants = GetDescendants(form).ToList();
            foreach (var text in new[] { "Abrir PDF", "Abrir carpeta", "Cerrar" })
            {
                ValidateButtonByText(name, descendants, text, errors);
            }
        }
        else if (name.StartsWith("Quotation", StringComparison.Ordinal))
        {
            ValidateQuotationForms(name, form, errors);
        }
        else if (name == "CommercialAlertsForm")
        {
            ValidateCommercialAlertsForm(form, errors);
        }
        else if (name == "EmployeePermissionsForm")
        {
            ValidateEmployeePermissionsForm(form, errors);
        }
        else if (name == "EmployeeInventoryLookupForm")
        {
            ValidateEmployeeInventoryLookupForm(form, errors);
        }
        else if (name == "CashMovementsDetailForm")
        {
            ValidateCashMovementsDetailForm(form, errors);
        }
        else if (name == "CashHistoryForm")
        {
            ValidateCashHistoryForm(form, errors);
        }
        else if (name == "SalesHistoryForm")
        {
            ValidateSalesHistoryForm(form, errors);
        }
        else if (name == "MySalesTodayForm")
        {
            ValidateMySalesTodayForm(form, errors);
        }
        else if (name == "CashSessionDetailForm")
        {
            ValidateCashSessionDetailForm(form, errors);
        }
        else if (name == "CashContributionForm")
        {
            ValidateCashContributionForm(form, errors);
        }
        else if (name.StartsWith("CustomOrder", StringComparison.Ordinal))
        {
            ValidateCustomOrderForms(name, form, errors);
        }
        else if (name == "AdminDashboardForm")
        {
            ValidateAdminDashboardForm(form, errors);
        }
        else if (name == "AnalyticsForm")
        {
            ValidateAnalyticsForm(form, errors);
        }
        else if (name == "ExcelImportForm")
        {
            ValidateExcelImportForm(form, errors);
        }
        else if (name == "ReportsForm")
        {
            ValidateReportsForm(form, errors);
        }
    }

    private static void ValidateLoginForm(Form form, List<string> errors)
    {
        var versionLabel = GetDescendants(form)
            .OfType<Label>()
            .SingleOrDefault(label => label.Name == "lblApplicationVersion");
        if (versionLabel is null)
        {
            errors.Add("LoginForm: no se encontro la identificación dinámica de versión.");
            return;
        }

        if (versionLabel.Text != ApplicationVersion.LoginText)
        {
            errors.Add(
                $"LoginForm: texto de versión inesperado. Actual='{versionLabel.Text}', " +
                $"esperado='{ApplicationVersion.LoginText}'.");
        }
    }

    private static void ValidateSystemConfigForm(Form form, List<string> errors)
    {
        var versionLabel = GetDescendants(form)
            .OfType<Label>()
            .SingleOrDefault(label => label.Name == "lblApplicationVersion");
        if (versionLabel is null)
        {
            errors.Add("SystemConfigForm: no se encontro la información dinámica de versión.");
            return;
        }

        var expected = $"Versión de ArteInventariado: {ApplicationVersion.ProductVersion}";
        if (versionLabel.Text != expected)
        {
            errors.Add(
                $"SystemConfigForm: texto de versión inesperado. Actual='{versionLabel.Text}', " +
                $"esperado='{expected}'.");
        }
    }

    private static void SmokeExcelImportLayout(List<string> errors)
    {
        try
        {
            using var form = new ExcelImportForm(new SmokeProductImportService());
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;

            if (form.ClientSize.Width < 1120 || form.ClientSize.Height < 640)
            {
                errors.Add(
                    $"ExcelImportForm: el tamaño inicial debe conservar al menos 1120x640 y es {form.ClientSize}.");
            }

            var binding = typeof(ExcelImportForm)
                .GetField("_bindingSource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(form) as BindingSource;
            if (binding is null)
            {
                errors.Add("ExcelImportForm: no se encontro la fuente de datos de la vista previa.");
                return;
            }

            binding.DataSource = new[]
            {
                new ProductImportPreviewRow(
                    2,
                    "PRD-SMOKE-001",
                    "Producto de prueba visual",
                    "Categoría smoke",
                    "Descripción smoke",
                    100m,
                    250m,
                    12,
                    3,
                    "Válida",
                    "Observación smoke",
                    true,
                    false,
                    null)
            };

            form.Show();
            System.Windows.Forms.Application.DoEvents();
            foreach (var size in ExcelImportSmokeSizes)
            {
                form.ClientSize = size;
                form.PerformLayout();
                PerformLayoutRecursive(form);
                System.Windows.Forms.Application.DoEvents();
                foreach (var grid in GetDescendants(form).OfType<DataGridView>())
                {
                    grid.PerformLayout();
                }

                ValidateExcelImportForm(form, errors);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"ExcelImportForm layout smoke: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void ValidateExcelImportForm(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        var grid = descendants.OfType<DataGridView>().SingleOrDefault();
        if (grid is null)
        {
            errors.Add("ExcelImportForm: no se encontro la tabla de vista previa.");
            return;
        }

        if (grid.Dock != DockStyle.Fill)
        {
            errors.Add("ExcelImportForm: la tabla de vista previa no ocupa su contenedor.");
        }

        if (grid.Columns.Count != 11)
        {
            errors.Add($"ExcelImportForm: se esperaban 11 columnas y se encontraron {grid.Columns.Count}.");
        }

        if (grid.AutoSizeColumnsMode != DataGridViewAutoSizeColumnsMode.Fill ||
            grid.Columns.Cast<DataGridViewColumn>().Any(column =>
                column.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill))
        {
            errors.Add("ExcelImportForm: las columnas no usan reparto responsive Fill.");
        }

        if (grid.Columns.Cast<DataGridViewColumn>().Any(column => !column.Visible))
        {
            errors.Add("ExcelImportForm: una columna de la vista previa quedó oculta.");
        }

        if (grid.ClientSize.Width <= 0 || grid.ClientSize.Height <= 0)
        {
            errors.Add($"ExcelImportForm: tabla sin área visible en {form.ClientSize}.");
        }
        else if (form.ClientSize.Width >= 1120)
        {
            var columnsWidth = grid.Columns.Cast<DataGridViewColumn>().Sum(column => column.Width);
            if (columnsWidth + 8 < grid.ClientSize.Width)
            {
                errors.Add(
                    $"ExcelImportForm: la tabla no aprovecha el ancho disponible en {form.ClientSize}. " +
                    $"Columnas={columnsWidth}, área={grid.ClientSize.Width}.");
            }
        }

        foreach (var text in new[]
                 {
                     "1 · Generar plantilla",
                     "2 · Seleccionar archivo .xlsx",
                     "3 · Importar filas válidas"
                 })
        {
            ValidateButtonByText("ExcelImportForm", descendants, text, errors);
        }
    }

    private static void ValidateAccountsReceivableForm(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        foreach (var text in new[] { "Cuentas por cobrar", "Saldo pendiente", "Cuentas abiertas", "Vencidas", "Morosas" })
        {
            ValidateLabel(descendants, "AccountsReceivableForm", text, errors);
        }

        foreach (var text in new[]
                 {
                     "Historial", "Ver alertas", "Abrir cliente", "Ver venta",
                     "Ver detalle", "Registrar", "Cerrar", "Limpiar"
                 })
        {
            ValidateButton(descendants, "AccountsReceivableForm", text, 80, 32, errors);
        }

        var grid = descendants.OfType<DataGridView>().FirstOrDefault();
        if (grid is null)
        {
            errors.Add("AccountsReceivableForm: no se encontro la grilla principal.");
        }
        else
        {
            if (grid.Columns.Count != 11)
            {
                errors.Add($"AccountsReceivableForm: se esperaban 11 columnas y se encontraron {grid.Columns.Count}.");
            }

            if (grid.ScrollBars is ScrollBars.Horizontal or ScrollBars.Both)
            {
                errors.Add("AccountsReceivableForm: la grilla habilito scroll horizontal.");
            }
        }

        var kpiFields = new[] { "_lblPendingAmount", "_lblOpenAccounts", "_lblOverdueAccounts", "_lblMorosoAccounts" };
        foreach (var fieldName in kpiFields)
        {
            var label = typeof(AccountsReceivableForm)
                .GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(form) as Label;
            if (label is null || label.TextAlign != ContentAlignment.MiddleCenter)
            {
                errors.Add($"AccountsReceivableForm: KPI {fieldName} no esta centrado.");
            }
            else if (label.Height < label.PreferredHeight)
            {
                errors.Add(
                    $"AccountsReceivableForm: KPI {fieldName} esta cortado. " +
                    $"Height={label.Height}, Preferred={label.PreferredHeight}.");
            }
        }

        var title = descendants.OfType<Label>().FirstOrDefault(label => label.Text == "Cuentas por cobrar");
        var subtitle = descendants.OfType<Label>().FirstOrDefault(label =>
            label.Text.StartsWith("Consulte y supervise", StringComparison.Ordinal));
        foreach (var label in new[] { title, subtitle })
        {
            if (label is not null && label.Height < label.PreferredHeight)
            {
                errors.Add(
                    $"AccountsReceivableForm: texto superior '{label.Text}' esta cortado. " +
                    $"Height={label.Height}, Preferred={label.PreferredHeight}.");
            }
        }

        var includeClosed = descendants.OfType<CheckBox>()
            .FirstOrDefault(checkBox => checkBox.Text == "Incluir cerradas");
        if (includeClosed is null)
        {
            errors.Add("AccountsReceivableForm: no se encontro 'Incluir cerradas'.");
        }
        else if (includeClosed.Width < includeClosed.PreferredSize.Width ||
                 includeClosed.Height < includeClosed.PreferredSize.Height)
        {
            errors.Add(
                $"AccountsReceivableForm: 'Incluir cerradas' esta cortado. " +
                $"Size={includeClosed.Width}x{includeClosed.Height}, " +
                $"Preferred={includeClosed.PreferredSize.Width}x{includeClosed.PreferredSize.Height}.");
        }
    }

    private static void ValidateAccountsPayableForm(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        foreach (var text in new[] { "Cuentas por pagar a proveedores", "Buscar por compra, cuenta o proveedor", "Estado", "Desde", "Hasta" })
        {
            ValidateLabel(descendants, "AccountsPayableForm", text, errors);
        }

        foreach (var (text, width) in new[]
                 {
                     ("Ver detalle", 130),
                     ("Registrar pago", 160),
                     ("Historial de pagos", 180),
                     ("Abrir proveedor", 150),
                     ("Cerrar", 110),
                     ("Limpiar", 120)
                 })
        {
            ValidateButton(descendants, "AccountsPayableForm", text, width, 38, errors);
            var button = descendants.OfType<Button>().FirstOrDefault(control => control.Text == text);
            if (button is not null && button.TextAlign != ContentAlignment.MiddleCenter)
            {
                errors.Add($"AccountsPayableForm: boton '{text}' no esta centrado.");
            }
        }

        var filters = descendants.OfType<TableLayoutPanel>()
            .FirstOrDefault(panel => panel.Name == "AccountsPayableFilters");
        if (filters is null)
        {
            errors.Add("AccountsPayableForm: no se encontro el panel de filtros.");
        }
        else if (filters.RowCount != 2)
        {
            errors.Add($"AccountsPayableForm: el panel de filtros tiene {filters.RowCount} filas, se esperaban 2.");
        }

        var criteria = descendants.OfType<TableLayoutPanel>()
            .FirstOrDefault(panel => panel.Name == "AccountsPayableCriteria");
        if (criteria is null)
        {
            errors.Add("AccountsPayableForm: no se encontro la franja de criterios.");
        }
        else if (criteria.ColumnCount != 5)
        {
            errors.Add($"AccountsPayableForm: la franja de criterios tiene {criteria.ColumnCount} columnas, se esperaban 5.");
        }

        var datePickers = descendants.OfType<DateTimePicker>().ToList();
        if (datePickers.Count != 2)
        {
            errors.Add($"AccountsPayableForm: se esperaban 2 selectores de fecha y se encontraron {datePickers.Count}.");
        }
        else if (datePickers.Any(picker => picker.Width < 140))
        {
            errors.Add("AccountsPayableForm: un selector de fecha quedó comprimido.");
        }

        var stats = descendants.OfType<TableLayoutPanel>()
            .FirstOrDefault(panel => panel.Name == "AccountsPayableStats");
        if (stats is not null && filters is not null && stats.Bounds.IntersectsWith(filters.Bounds))
        {
            errors.Add("AccountsPayableForm: el panel de filtros se superpone con las tarjetas de resumen.");
        }
    }

    private static void ValidateReportsForm(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        if (!descendants.OfType<TabPage>().Any(tab => tab.Text == "Reportes avanzados"))
        {
            errors.Add("ReportsForm: falta el espacio de reportes avanzados.");
        }
        if (!descendants.OfType<DataGridView>().Any(grid => grid.Name == "AdvancedReportsGrid"))
        {
            errors.Add("ReportsForm: falta la tabla reutilizable de reportes avanzados.");
        }
        if (!descendants.OfType<Label>().Any(label => label.Name == "AdvancedReportsEmptyState"))
        {
            errors.Add("ReportsForm: falta el estado vacío profesional.");
        }
        foreach (var text in new[] { "Preparar reporte", "Cancelar", "Exportar Excel", "Vista previa / Imprimir" })
        {
            if (!descendants.OfType<Button>().Any(button => button.Text == text))
            {
                errors.Add($"ReportsForm: falta la acción '{text}'.");
            }
        }

        var grid = descendants.OfType<DataGridView>().FirstOrDefault(item => item.Name == "AdvancedReportsGrid");
        if (grid is not null && grid.Dock != DockStyle.Fill)
        {
            errors.Add("ReportsForm: la tabla avanzada no ocupa su contenedor responsive.");
        }
        foreach (var button in descendants.OfType<Button>().Where(item =>
                     item.Text is "Preparar reporte" or "Cancelar" or "Exportar Excel" or
                         "Vista previa / Imprimir" or "← Anterior" or "Siguiente →"))
        {
            if (button.Width < button.PreferredSize.Width || button.Height < button.PreferredSize.Height)
            {
                errors.Add(
                    $"ReportsForm: botón '{button.Text}' recortado. " +
                    $"Actual={button.Size}, Preferred={button.PreferredSize}.");
            }
        }

        var advanced = descendants.OfType<AdvancedReportsControl>().FirstOrDefault();
        var pageField = typeof(AdvancedReportsControl).GetField(
            "_page",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var searchField = typeof(AdvancedReportsControl).GetField(
            "_search",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var specificValueField = typeof(AdvancedReportsControl).GetField(
            "_specificValue",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var groupField = typeof(AdvancedReportsControl).GetField(
            "_group",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var reportField = typeof(AdvancedReportsControl).GetField(
            "_report",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (advanced is not null && pageField is not null && searchField?.GetValue(advanced) is TextBox search)
        {
            if (groupField?.GetValue(advanced) is ComboBox group &&
                reportField?.GetValue(advanced) is ComboBox report)
            {
                group.SelectedValue = AdvancedReportGroup.Inventory;
                report.SelectedValue = AdvancedReportKind.Products;
            }
            pageField.SetValue(advanced, 3);
            // This contract check only inspects the request.  Disable the
            // debounced callback because this harness has no Application.Run;
            // otherwise its Task.Yield continuation can resume on a pool
            // thread while the smoke switches reports below.
            var liveSearch = typeof(AdvancedReportsControl).GetField(
                "_liveSearch",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(advanced) as LiveSearchController;
            liveSearch?.Dispose();
            search.Text = "smoke-search";
            if (!Equals(pageField.GetValue(advanced), 1))
            {
                errors.Add("ReportsForm: cambiar búsqueda no reinicia la paginación a página 1.");
            }
            search.Enabled = false;
            if (advanced.BuildRequest(1).Search != "smoke-search")
            {
                errors.Add("ReportsForm: el estado busy eliminó SearchText de la solicitud.");
            }
        }
        if (advanced is not null && specificValueField?.GetValue(advanced) is not ComboBox)
        {
            errors.Add("ReportsForm: el filtro específico no usa un selector amigable de entidades.");
        }
    }

    private static void SmokeAdvancedReportPrinting(List<string> errors)
    {
        try
        {
            ValidatePrintPreviewPages(BuildAdvancedPrintReport(1), minimumPages: 1, errors);
            ValidatePrintPreviewPages(BuildAdvancedPrintReport(650), minimumPages: 20, errors);
        }
        catch (Exception ex)
        {
            errors.Add($"AdvancedReportPrintDocument: smoke multipágina falló: {ex.Message}");
        }
    }

    private static void ValidatePrintPreviewPages(
        AdvancedReportResult report,
        int minimumPages,
        List<string> errors)
    {
        using var document = new AdvancedReportPrintDocument(report, "admin-smoke", DateTime.Now);
        document.Prepare();
        if (document.TotalPages < minimumPages)
        {
            errors.Add(
                $"AdvancedReportPrintDocument: esperaba al menos {minimumPages} páginas y preparó {document.TotalPages}.");
            return;
        }

        var firstPreviewPages = CountPreviewPages(document);
        var reopenedPreviewPages = CountPreviewPages(document);
        if (firstPreviewPages != document.TotalPages || reopenedPreviewPages != document.TotalPages)
        {
            errors.Add(
                "AdvancedReportPrintDocument: preparación, preview o reapertura difieren. " +
                $"Preparadas={document.TotalPages}, preview={firstPreviewPages}, reapertura={reopenedPreviewPages}.");
        }
    }

    private static int CountPreviewPages(PrintDocument document)
    {
        var controller = new PreviewPrintController();
        document.PrintController = controller;
        document.Print();
        var pages = controller.GetPreviewPageInfo();
        foreach (var page in pages)
        {
            page.Image.Dispose();
        }
        return pages.Length;
    }

    private static AdvancedReportResult BuildAdvancedPrintReport(int rowCount)
    {
        var start = new DateTimeOffset(DateTime.Today.AddMonths(-3));
        var end = new DateTimeOffset(DateTime.Today.AddDays(1));
        var period = new AnalyticsPeriodDefinition(
            AnalyticsPeriodKind.LastThreeMonths,
            start,
            end,
            start.UtcDateTime,
            end.UtcDateTime,
            AnalyticsGrouping.Day,
            "Últimos 3 meses",
            false);
        var rows = Enumerable.Range(1, rowCount)
            .Select(number => new AdvancedReportRow(new Dictionary<string, object?>
            {
                ["date"] = DateTime.UtcNow.AddMinutes(-number),
                ["user"] = "admin-smoke",
                ["action"] = "ACTUALIZAR",
                ["entity"] = "Producto",
                ["description"] = number % 7 == 0
                    ? $"Descripción multilínea {number}. Se conserva completa y continúa en la página siguiente cuando no cabe en la actual."
                    : $"Actividad administrativa {number}"
            }))
            .ToList();
        return new AdvancedReportResult(
            AdvancedReportCatalog.Get(AdvancedReportKind.AdministrativeActivity),
            period,
            ["Búsqueda: actividad", "Usuario: admin-smoke"],
            [
                new("date", "Fecha", AdvancedReportValueKind.DateTime),
                new("user", "Usuario"),
                new("action", "Acción"),
                new("entity", "Entidad"),
                new("description", "Descripción")
            ],
            rows,
            rows.Count,
            [],
            null,
            "No existe actividad administrativa para los filtros seleccionados.");
    }

    private static void ValidateAnalyticsForm(Form form, List<string> errors)
    {
        PumpUi(TimeSpan.FromMilliseconds(150));
        var descendants = GetDescendants(form).ToList();
        if (descendants.OfType<AnalyticsPeriodSelectorControl>().Count() != 1)
        {
            errors.Add("AnalyticsForm: no se encontro el selector comun de periodo.");
        }
        var expectedSections = new[]
        {
            "Ventas", "Productos", "Categorías", "Finanzas", "Caja", "Comercial", "Compras e inversión",
            "Cuentas por cobrar", "Cuentas por pagar"
        };
        var sectionButtons = descendants
            .OfType<Button>()
            .Where(button => Equals(button.Tag, "analytics-section-button"))
            .ToList();
        if (descendants.OfType<TabControl>().Any() ||
            sectionButtons.Count != expectedSections.Length ||
            !expectedSections.All(expected => sectionButtons.Any(button => button.AccessibleName == expected)))
        {
            errors.Add("AnalyticsForm: la navegación moderna no conserva las nueve secciones de analítica.");
        }
        if (sectionButtons.Count(button =>
                button.BackColor == UITheme.DashboardAccentSoftColor &&
                button.ForeColor == UITheme.PrimaryColor) != 1)
        {
            errors.Add("AnalyticsForm: debe existir exactamente una sección activa con identidad borgoña.");
        }
        if (descendants.OfType<AnalyticsMetricCardControl>().Count() < 7)
        {
            errors.Add("AnalyticsForm: deben conservarse los siete indicadores de ventas.");
        }
        if (descendants.OfType<CartesianChart>().Count() < 3 || descendants.OfType<PieChart>().Count() != 1)
        {
            errors.Add("AnalyticsForm: faltan graficas de ventas, producto o categorias.");
        }
        if (!descendants.OfType<DataGridView>().Any(grid => grid.Name == "CommercialConversionGrid") ||
            !descendants.OfType<DataGridView>().Any(grid => grid.Name == "CommercialCustomerRankingGrid") ||
            !descendants.OfType<CartesianChart>().Any(chart => chart.Name == "CommercialTrendChart"))
        {
            errors.Add("AnalyticsForm: la sección Comercial no conserva conversiones, tendencia y ranking separados.");
        }

        foreach (var title in new[]
        {
            "Antigüedad de cartera", "Clientes con mayor saldo",
            "Antigüedad de obligaciones", "Proveedores con mayor saldo"
        })
        {
            if (!descendants.OfType<Label>().Any(label => label.Text == title))
            {
                errors.Add($"AnalyticsForm: falta la superficie separada '{title}'.");
            }
        }

        var purchaseEmptyState = descendants
            .OfType<Label>()
            .FirstOrDefault(label => label.Name == "PurchaseAnalyticsEmptyState");
        if (purchaseEmptyState?.Text != "No hubo mercancía recibida en este período.")
        {
            errors.Add("AnalyticsForm: falta el empty state profesional de Compras / inversión.");
        }
        if (descendants.OfType<Label>().Count(label => label.Text.StartsWith("Cobertura exacta:", StringComparison.Ordinal)) < 2)
        {
            errors.Add("AnalyticsForm: la cobertura exacta de Finanzas o Compras no permanece visible.");
        }

        var productButton = sectionButtons.FirstOrDefault(button => button.AccessibleName == "Productos");
        productButton?.PerformClick();
        System.Windows.Forms.Application.DoEvents();
        PerformLayoutRecursive(form);
        ValidateAnalyticsProducts(
            form,
            errors,
            runInteractions: Equals(form.Tag, "1280x720-dpi100"));

        foreach (var button in sectionButtons)
        {
            button.PerformClick();
            System.Windows.Forms.Application.DoEvents();
            PerformLayoutRecursive(form);
            foreach (var card in GetDescendants(form).OfType<AnalyticsMetricCardControl>().Where(card => card.Visible))
            {
                if (card.IsValueClipped)
                {
                    errors.Add(
                        $"AnalyticsForm: un monto KPI quedó truncado en '{button.AccessibleName}': " +
                        card.ValueLayoutDiagnostics);
                }
                if (card.HasContentOverflow(out var diagnostics))
                {
                    errors.Add(
                        $"AnalyticsForm: contenido KPI fuera del ClientRectangle en '{button.AccessibleName}': " +
                        diagnostics);
                }
            }
            CaptureAnalyticsSnapshot(form, button.AccessibleName ?? button.Text, errors);
            if (string.Equals(button.AccessibleName, "Categorías", StringComparison.OrdinalIgnoreCase))
            {
                ValidateAnalyticsCategoryTrend(GetDescendants(form).ToList(), errors);
                CaptureAnalyticsCategoriesContentSnapshot(form, errors);
            }
        }

        var sectionHost = descendants.OfType<Panel>().FirstOrDefault(panel => panel.Tag is int);
        if (sectionHost?.HorizontalScroll.Visible == true)
        {
            errors.Add("AnalyticsForm: el espacio de análisis requiere scroll horizontal general.");
        }

        foreach (var control in descendants.Where(control => control.Visible && control is Button or ComboBox or DateTimePicker))
        {
            if (control.Width < 60 || control.Height < 24)
            {
                errors.Add($"AnalyticsForm: control '{control.Text}' comprimido ({control.Width}x{control.Height}).");
            }
        }
    }

    private static void ValidateAnalyticsProducts(Form form, List<string> errors, bool runInteractions)
    {
        var descendants = GetDescendants(form).ToList();
        var search = descendants.OfType<TextBox>().FirstOrDefault(control => control.Name == "ProductAnalyticsSearchTextBox");
        var clear = descendants.OfType<Button>().FirstOrDefault(control => control.Name == "ClearProductAnalyticsSearchButton");
        var ranking = descendants.OfType<ComboBox>().FirstOrDefault(control => control.Name == "ProductAnalyticsRankingComboBox");
        var limit = descendants.OfType<ComboBox>().FirstOrDefault(control => control.Name == "ProductAnalyticsLimitComboBox");
        var grid = descendants.OfType<DataGridView>().FirstOrDefault(control => control.Name == "ProductAnalyticsRankingGrid");
        var chart = descendants.OfType<CartesianChart>().FirstOrDefault(control => control.Name == "ProductAnalyticsTrendChart");
        var chartTitle = descendants.OfType<Label>().FirstOrDefault(control => control.Name == "ProductAnalyticsChartTitle");
        var empty = descendants.OfType<Label>().FirstOrDefault(control => control.Name == "ProductAnalyticsRankingEmptyState");

        if (search is null || clear is null || ranking is null || limit is null || grid is null || chart is null || chartTitle is null || empty is null)
        {
            errors.Add("AnalyticsForm Productos: faltan controles de búsqueda, ranking, límite, tabla, empty state o gráfica.");
            return;
        }
        if (descendants.OfType<AnalyticsProductMetricCardControl>().Count(control => control.Visible) != 3)
        {
            errors.Add("AnalyticsForm Productos: deben existir tres KPI visuales derivados del ranking.");
        }
        if (descendants.OfType<AnalyticsStatusStripControl>().Count(control => control.Visible) != 1)
        {
            errors.Add("AnalyticsForm Productos: falta la franja visible de captura, cobertura y actualización.");
        }
        var expectedHeaders = new[] { "#", "Código", "Producto", "Vendidas", "Ventas netas", "Dev.", "Stock", "Última venta", "Calidad" };
        if (grid.Columns.Count != expectedHeaders.Length ||
            !expectedHeaders.SequenceEqual(grid.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText)))
        {
            errors.Add("AnalyticsForm Productos: el ranking no conserva las nueve columnas visuales aprobadas.");
        }
        if (ranking.Items.Count != 4 || limit.Items.Count != 4 || limit.Text != "Mostrar: Top 10")
        {
            errors.Add("AnalyticsForm Productos: ranking o selector Top 5/10/25/Todos incompleto; Top 10 debe ser el valor inicial.");
        }

        PumpUi(TimeSpan.FromMilliseconds(80));
        if (grid.Rows.Count > 0)
        {
            if (grid.Rows.Count != 10)
            {
                errors.Add($"AnalyticsForm Productos: Top 10 mostró {grid.Rows.Count} filas en lugar de 10.");
            }
            if (Convert.ToString(grid.Rows[0].Cells["NetSales"].Value) != "$920.00")
            {
                errors.Add("AnalyticsForm Productos: las ventas netas no usan formato monetario con dos decimales.");
            }
            if (Convert.ToString(grid.Rows[0].Cells["LastSale"].Value) != "Hoy")
            {
                errors.Add("AnalyticsForm Productos: la última venta de hoy no está humanizada.");
            }
            var qualities = grid.Rows.Cast<DataGridViewRow>()
                .Select(row => Convert.ToString(row.Cells["Quality"].Value))
                .ToHashSet(StringComparer.Ordinal);
            if (!qualities.Contains("Exacto") || !qualities.Contains("Parcial") || !qualities.Contains("Sin snapshot"))
            {
                errors.Add("AnalyticsForm Productos: faltan los estados Exacto/Parcial/Sin snapshot.");
            }
        }
        if (!descendants.OfType<Label>().Any(label => label.Text.StartsWith("Captura activa desde", StringComparison.Ordinal)) ||
            !descendants.OfType<Label>().Any(label => label.Text.StartsWith("Cobertura histórica:", StringComparison.Ordinal)))
        {
            errors.Add("AnalyticsForm Productos: captura o cobertura histórica no permanecen visibles.");
        }
        if (!runInteractions)
        {
            return;
        }

        search.Text = "P";
        search.Text = "PRD";
        search.Text = "PRD-002";
        PumpUi(TimeSpan.FromMilliseconds(750));
        if (grid.Rows.Count != 1 || Convert.ToString(grid.Rows[0].Cells["Code"].Value) != "PRD-002")
        {
            errors.Add("AnalyticsForm Productos: la búsqueda carácter a carácter por código no publicó sólo la coincidencia final.");
        }

        search.Text = "lienzo";
        PumpUi(TimeSpan.FromMilliseconds(750));
        if (grid.Rows.Count != 1 || Convert.ToString(grid.Rows[0].Cells["Product"].Value) != "Lienzo artesanal")
        {
            errors.Add("AnalyticsForm Productos: la búsqueda parcial por nombre no funciona sin distinguir mayúsculas.");
        }

        search.Text = "producto-inexistente";
        PumpUi(TimeSpan.FromMilliseconds(750));
        if (grid.Visible ||
            !empty.Visible ||
            !empty.Text.Contains("No se encontraron productos", StringComparison.Ordinal) ||
            chartTitle.Text != "Sin producto seleccionado")
        {
            errors.Add("AnalyticsForm Productos: cero coincidencias no conserva el estado vacío específico.");
        }

        clear.PerformClick();
        PumpUi(TimeSpan.FromMilliseconds(750));
        if (!grid.Visible || grid.Rows.Count != 10 || !string.IsNullOrEmpty(search.Text))
        {
            errors.Add("AnalyticsForm Productos: limpiar búsqueda no restauró el ranking normal.");
        }

        ranking.SelectedIndex = 2;
        PumpUi(TimeSpan.FromMilliseconds(1200));
        if (grid.Rows.Count == 0 || Convert.ToString(grid.Rows[0].Cells["Code"].Value) != "PRD-002")
        {
            errors.Add("AnalyticsForm Productos: cambiar a Más devueltos no actualizó el orden del ranking.");
        }

        limit.SelectedIndex = 0;
        PumpUi(TimeSpan.FromMilliseconds(1200));
        if (grid.Rows.Count > 5)
        {
            errors.Add("AnalyticsForm Productos: Top 5 no limita la presentación del ranking.");
        }

        ranking.SelectedIndex = 0;
        limit.SelectedIndex = 3;
        PumpUi(TimeSpan.FromMilliseconds(1500));
        if (grid.Rows.Count > 1)
        {
            grid.CurrentCell = grid.Rows[0].Cells["Product"];
            grid.Rows[0].Selected = true;
            PumpUi(TimeSpan.FromMilliseconds(100));
            grid.CurrentCell = grid.Rows[1].Cells["Product"];
            grid.Rows[1].Selected = true;
            PumpUi(TimeSpan.FromMilliseconds(500));
            if (!chartTitle.Text.Contains("PRD-002", StringComparison.Ordinal) || chart.Series.Count() != 1)
            {
                errors.Add(
                    "AnalyticsForm Productos: seleccionar una fila no vinculó encabezado y gráfica del producto. " +
                    $"Fila={grid.CurrentRow?.Cells["Code"].Value}; Título='{chartTitle.Text}'; Series={chart.Series.Count()}.");
            }
        }

        ranking.SelectedIndex = 0;
        limit.SelectedIndex = 1;
        search.Clear();
        PumpUi(TimeSpan.FromMilliseconds(1500));
        if (grid.Rows.Count > 0)
        {
            grid.CurrentCell = grid.Rows[0].Cells["Product"];
            grid.Rows[0].Selected = true;
            PumpUi(TimeSpan.FromMilliseconds(500));
        }
    }

    private static void CaptureAnalyticsSnapshot(
        Form form,
        string sectionName,
        List<string> errors,
        bool preserveScroll = false)
    {
        var outputDirectory = Environment.GetEnvironmentVariable("ARTE_ANALYTICS_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return;
        }

        var scenario = form.Tag as string ?? "analytics";
        var configuredFilters = Environment.GetEnvironmentVariable("ARTE_ANALYTICS_CAPTURE_FILTER");
        if (!string.IsNullOrWhiteSpace(configuredFilters) &&
            !configuredFilters
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(scenario, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            PumpUi(TimeSpan.FromMilliseconds(150));
            if (!preserveScroll)
            {
                PerformLayoutRecursive(form);
            }
            Directory.CreateDirectory(outputDirectory);
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var safeSection = new string(sectionName
                .Select(character => invalidCharacters.Contains(character) ? '-' : character)
                .ToArray())
                .Replace(' ', '-');
            var path = Path.Combine(outputDirectory, $"{scenario}-{safeSection}.png");
            var clientSurface = form.Controls.Count > 0 ? form.Controls[0] : form;
            using var bitmap = new Bitmap(clientSurface.ClientSize.Width, clientSurface.ClientSize.Height);
            clientSurface.DrawToBitmap(bitmap, new Rectangle(Point.Empty, clientSurface.ClientSize));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception ex)
        {
            errors.Add(
                $"AnalyticsForm: no se pudo capturar '{sectionName}' en {scenario}: " +
                $"{ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void ValidateAdminDashboardForm(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        if (descendants.OfType<DashboardHomeControl>().Count() != 1)
        {
            errors.Add("AdminDashboardForm: no se encontro un unico DashboardHomeControl.");
        }
        if (descendants.OfType<DashboardMetricCardControl>().Count() != 5)
        {
            errors.Add("AdminDashboardForm: deben existir exactamente cinco tarjetas principales.");
        }
        if (descendants.OfType<CartesianChart>().Count() != 1 || descendants.OfType<PieChart>().Count() != 1)
        {
            errors.Add("AdminDashboardForm: faltan las graficas de linea o dona.");
        }

        var moduleButtons = descendants.OfType<Button>()
            .Count(button => button.Tag is not null && button.Height >= 38);
        if (moduleButtons < 20)
        {
            errors.Add($"AdminDashboardForm: menu lateral incompleto ({moduleButtons} accesos detectados).");
        }

        foreach (var card in descendants.OfType<DashboardMetricCardControl>())
        {
            if (card.Width < card.MinimumSize.Width || card.Height < card.MinimumSize.Height)
            {
                errors.Add($"AdminDashboardForm: tarjeta comprimida ({card.Width}x{card.Height}).");
            }
        }

        var cards = descendants.OfType<DashboardMetricCardControl>().ToList();
        if (cards.FirstOrDefault()?.Parent is TableLayoutPanel metrics &&
            metrics.ColumnCount == 5 &&
            cards.Select(card => card.Top).Distinct().Count() != 1)
        {
            errors.Add($"AdminDashboardForm: la rejilla de cinco columnas no mantiene las tarjetas en una fila.");
        }
    }

    private static void ValidateCustomOrderForms(string name, Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        var requiresDataGrid = name is not (
            "CustomOrderStatusForm"
            or "CustomOrderPaymentForm"
            or "CustomOrderRefundForm");
        if (requiresDataGrid && !descendants.OfType<DataGridView>().Any())
            errors.Add($"{name}: no se encontro una tabla de datos.");
        foreach (var button in descendants.OfType<Button>().Where(button => button.Visible))
        {
            if (!string.IsNullOrWhiteSpace(button.Text) &&
                (button.Width < Math.Min(70, button.PreferredSize.Width) || button.Height < 30))
                errors.Add($"{name}: boton '{button.Text}' recortado ({button.Width}x{button.Height}).");
        }
        if (name == "CustomOrdersForm")
        {
            foreach (var text in new[]
                     {
                         "Pedidos personalizados",
                         "Controle solicitudes especiales desde su confirmacion hasta la entrega."
                     })
            {
                var label = descendants.OfType<Label>().FirstOrDefault(control => control.Text == text);
                if (label is null || label.Height < label.PreferredHeight)
                {
                    errors.Add(
                        $"{name}: encabezado '{text}' ausente o cortado. " +
                        $"Height={label?.Height}, Preferred={label?.PreferredHeight}.");
                }
            }

            foreach (var text in new[] { "Borradores", "Confirmados", "En produccion", "Listos para entrega", "Atrasados" })
            {
                var title = descendants.OfType<Label>().FirstOrDefault(control => control.Text == text);
                var value = title?.Parent?.Controls.OfType<Label>().FirstOrDefault(control => control != title);
                if (title is null || value is null ||
                    title.Height < title.PreferredHeight || value.Height < value.PreferredHeight)
                {
                    errors.Add($"{name}: tarjeta '{text}' ausente o cortada.");
                }
            }

            foreach (var text in new[] { "Usar fechas", "Solo atrasados" })
            {
                var check = descendants.OfType<CheckBox>().FirstOrDefault(c => c.Text == text);
                if (check is null || check.Width < check.PreferredSize.Width)
                    errors.Add($"{name}: filtro '{text}' ausente o comprimido. Width={check?.Width}, Preferred={check?.PreferredSize.Width}.");
            }
        }

        if (name == "CustomOrderDetailForm")
        {
            foreach (var title in new[]
                     {
                         "Cliente/destinatario", "Contacto", "Fecha del pedido", "Fecha prometida",
                         "Inicio", "Finalizacion", "Entrega", "Total estimado"
                     })
            {
                var value = descendants.OfType<Label>()
                    .FirstOrDefault(label => label.AccessibleName == $"Resumen {title}");
                if (value is null)
                {
                    errors.Add($"{name}: no se encontro el valor de resumen '{title}'.");
                }
                else if (value.Height < 30)
                {
                    errors.Add($"{name}: valor de resumen '{title}' comprimido. Height={value.Height}.");
                }
            }

            foreach (var text in new[]
                     {
                         "Cambiar estado", "Duplicar", "Registrar pago",
                         "Historial de pagos", "Generar PDF", "Volver"
                     })
            {
                ValidateButton(descendants, name, text, 110, 38, errors);
            }
        }
    }

    private static void ValidateEmployeePermissionsForm(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        var checks = descendants.OfType<CheckBox>()
            .Where(check => check.Tag is PermisoEmpleado)
            .ToList();
        if (checks.Count != EmployeePermissionCatalog.AllPermissions.Count)
        {
            errors.Add(
                $"EmployeePermissionsForm: se esperaban {EmployeePermissionCatalog.AllPermissions.Count} permisos visibles y se encontraron {checks.Count}.");
        }

        foreach (var group in new[]
                 {
                     "Punto de venta", "Devoluciones y cambios", "Historiales", "Clientes y creditos",
                     "Proveedores y artesanos", "Compras", "Finanzas de proveedores", "Cotizaciones",
                     "Pedidos personalizados", "Operaciones comerciales", "Administracion", "Caja"
                 })
        {
            if (!descendants.OfType<Label>().Any(label => label.Text == group))
            {
                errors.Add($"EmployeePermissionsForm: no se encontro la tarjeta '{group}'.");
            }
        }

        foreach (var permission in new[]
                 {
                     PermisoEmpleado.CUENTAS_PAGAR_ACCEDER,
                     PermisoEmpleado.PAGO_COMPRA_REGISTRAR,
                     PermisoEmpleado.ABONO_PROVEEDOR_REGISTRAR,
                     PermisoEmpleado.PAGOS_PROVEEDOR_HISTORIAL_VER,
                     PermisoEmpleado.FINANZAS_PROVEEDOR_VER
                 })
        {
            if (!checks.Any(check => Equals(check.Tag, permission)))
            {
                errors.Add($"EmployeePermissionsForm: no se encontro el control para {permission}.");
            }
        }

        var purchasePayment = checks.FirstOrDefault(check =>
            Equals(check.Tag, PermisoEmpleado.PAGO_COMPRA_REGISTRAR));
        var accountsPayable = checks.FirstOrDefault(check =>
            Equals(check.Tag, PermisoEmpleado.CUENTAS_PAGAR_ACCEDER));
        var purchases = checks.FirstOrDefault(check =>
            Equals(check.Tag, PermisoEmpleado.COMPRAS_ACCEDER));
        if (purchasePayment is not null && accountsPayable is not null && purchases is not null)
        {
            purchasePayment.Checked = true;
            if (!accountsPayable.Checked || !purchases.Checked)
            {
                errors.Add(
                    "EmployeePermissionsForm: marcar pago de compra no activo Cuentas por pagar y Compras.");
            }
        }

        var friendlyMessageMethod = typeof(EmployeePermissionsForm).GetMethod(
            "GetFriendlySaveMessage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var friendlyMessage = friendlyMessageMethod?.Invoke(
            null,
            [new InvalidOperationException("PAGO_COMPRA_REGISTRAR requiere CUENTAS_PAGAR_ACCEDER")]) as string;
        if (string.IsNullOrWhiteSpace(friendlyMessage) ||
            friendlyMessage.Contains(nameof(PermisoEmpleado.CUENTAS_PAGAR_ACCEDER), StringComparison.Ordinal))
        {
            errors.Add("EmployeePermissionsForm: el mensaje amigable expone identificadores tecnicos.");
        }
    }

    private static void ValidateEmployeeInventoryLookupForm(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        foreach (var text in new[]
                 {
                     "Consultar inventario",
                     "Consulta de solo lectura por producto, SKU, variante, categoría, subcategoría o código de barras."
                 })
        {
            var label = descendants.OfType<Label>().FirstOrDefault(control => control.Text == text);
            if (label is null)
            {
                errors.Add($"EmployeeInventoryLookupForm: no se encontro '{text}'.");
            }
            else if (label.Height < label.PreferredHeight)
            {
                errors.Add(
                    $"EmployeeInventoryLookupForm: '{text}' esta cortado. " +
                    $"Height={label.Height}, Preferred={label.PreferredHeight}.");
            }
        }

        var grid = descendants.OfType<DataGridView>().SingleOrDefault();
        if (grid is null || grid.Columns.Count != 9)
            errors.Add($"EmployeeInventoryLookupForm: tabla ausente o incompleta ({grid?.Columns.Count ?? 0} columnas).");
        else
        {
            foreach (var header in new[] { "Categoría", "Subcategoría" })
            {
                if (!grid.Columns.Cast<DataGridViewColumn>().Any(column => column.HeaderText == header))
                {
                    errors.Add($"EmployeeInventoryLookupForm: no se encontró la columna '{header}'.");
                }
            }

            if (!grid.ReadOnly || grid.ScrollBars != ScrollBars.Both)
            {
                errors.Add("EmployeeInventoryLookupForm: la tabla dejó de ser solo lectura o perdió el scroll horizontal.");
            }
        }

        var search = descendants.OfType<TextBox>()
            .FirstOrDefault(control => control.Name == "EmployeeInventorySearchTextBox");
        if (search is null || search.Height < search.PreferredHeight)
            errors.Add($"EmployeeInventoryLookupForm: busqueda ausente o comprimida ({search?.Width}x{search?.Height}).");

        if (!descendants.OfType<PictureBox>().Any())
            errors.Add("EmployeeInventoryLookupForm: no se encontro la vista previa de imagen.");
    }

    private static void ValidateCashMovementsDetailForm(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        if (form.MinimumSize.Width < 1120 || form.MinimumSize.Height < 700)
        {
            errors.Add($"CashMovementsDetailForm: MinimumSize insuficiente ({form.MinimumSize.Width}x{form.MinimumSize.Height}).");
        }

        foreach (var text in new[] { "Desde", "Hasta", "Tipo", "Origen", "Metodo", "Usuario", "Buscar" })
        {
            ValidateLabel(descendants, "CashMovementsDetailForm", text, errors);
        }
        foreach (var text in new[] { "Buscar", "Limpiar", "Exportar caja", "Cerrar" })
        {
            ValidateButton(descendants, "CashMovementsDetailForm", text, 100, 36, errors);
        }

        var grid = descendants.OfType<DataGridView>().FirstOrDefault();
        if (grid is null)
        {
            errors.Add("CashMovementsDetailForm: no se encontro la tabla de movimientos.");
        }
        else
        {
            if (grid.Dock != DockStyle.Fill)
            {
                errors.Add("CashMovementsDetailForm: la tabla no ocupa el espacio disponible.");
            }
            if (!grid.ShowCellToolTips)
            {
                errors.Add("CashMovementsDetailForm: los tooltips de celdas no estan habilitados.");
            }
        }
    }

    private static void ValidateCashHistoryForm(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        if (form.MinimumSize.Width < 1120 || form.MinimumSize.Height < 620)
        {
            errors.Add($"CashHistoryForm: MinimumSize insuficiente ({form.MinimumSize.Width}x{form.MinimumSize.Height}).");
        }

        var title = descendants.OfType<Label>()
            .FirstOrDefault(label => label.Text == "Historial de cierres de caja");
        var subtitle = descendants.OfType<Label>()
            .FirstOrDefault(label => label.Text.StartsWith("Consulta de solo lectura", StringComparison.Ordinal));
        if (title is null || subtitle is null || !ReferenceEquals(title.Parent, subtitle.Parent) ||
            title.Bounds.IntersectsWith(subtitle.Bounds))
        {
            errors.Add("CashHistoryForm: el título y el subtítulo se enciman o no están en el encabezado.");
        }

        var period = descendants.OfType<ComboBox>().FirstOrDefault();
        if (period is null || period.Items.Count != 4)
        {
            errors.Add("CashHistoryForm: faltan los períodos 7, 15, 30 días y personalizado.");
        }

        var datePickers = descendants.OfType<DateTimePicker>().ToList();
        if (datePickers.Count != 2 || datePickers.Any(picker =>
                picker.Format != DateTimePickerFormat.Custom || picker.CustomFormat != "dd/MM/yyyy"))
        {
            errors.Add("CashHistoryForm: las fechas deben mostrarse explícitamente como dd/MM/yyyy.");
        }

        foreach (var text in new[] { "Buscar", "Ver detalle", "Cerrar" })
        {
            ValidateButton(descendants, "CashHistoryForm", text, text == "Ver detalle" ? 120 : 100, 36, errors);
        }

        var grid = descendants.OfType<DataGridView>().FirstOrDefault();
        if (grid is null || grid.Dock != DockStyle.Fill || !grid.ReadOnly || grid.Columns.Count != 13 ||
            grid.AutoSizeColumnsMode != DataGridViewAutoSizeColumnsMode.Fill)
        {
            errors.Add("CashHistoryForm: tabla ausente, editable, sin las 13 columnas o sin distribuir el ancho disponible.");
        }
        else
        {
            var minimumColumnsWidth = grid.Columns.Cast<DataGridViewColumn>().Sum(column => column.MinimumWidth);
            var usableGridWidth = grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            if (minimumColumnsWidth > usableGridWidth)
            {
                errors.Add($"CashHistoryForm: las columnas requieren {minimumColumnsWidth}px y exceden los {usableGridWidth}px disponibles al ancho {form.ClientSize.Width}px.");
            }
        }

        var tabs = descendants.OfType<TabControl>().FirstOrDefault();
        if (tabs is null || tabs.TabPages.Count != 2 ||
            tabs.TabPages[0].Text != "Sesiones" || tabs.TabPages[1].Text != "Resumen por día")
        {
            errors.Add("CashHistoryForm: faltan las pestañas de sesiones y resumen por día.");
        }

        var dailyGrid = descendants.OfType<DataGridView>().FirstOrDefault(candidate => candidate.Columns.Count == 9);
        if (dailyGrid is null || !dailyGrid.ReadOnly || dailyGrid.Dock != DockStyle.Fill)
        {
            errors.Add("CashHistoryForm: el resumen diario debe ser una tabla de solo lectura con sus nueve datos.");
        }

        var cashCards = descendants.OfType<TableLayoutPanel>()
            .Where(control => control.AccessibleName is "Último cierre contado de ayer" or "Efectivo teórico hoy")
            .ToList();
        if (cashCards.Count is not (0 or 2) ||
            cashCards.Select(control => control.AccessibleName).Distinct().Count() != cashCards.Count)
        {
            errors.Add("CashHistoryForm: las tarjetas de efectivo esperado deben aparecer juntas solo en la vista administradora.");
        }
        else if (cashCards.Count == 2 && cashCards.Any(card =>
                     !card.Visible || card.Width < 180 || card.Height < 70 || card.Parent is null ||
                     !card.Parent.ClientRectangle.Contains(card.Bounds)))
        {
            var clipped = cashCards.FirstOrDefault(card =>
                !card.Visible || card.Width < 180 || card.Height < 70 || card.Parent is null ||
                !card.Parent.ClientRectangle.Contains(card.Bounds));
            errors.Add($"CashHistoryForm: una tarjeta administradora se recorta o no cabe en el encabezado ({clipped?.AccessibleName}, {clipped?.Bounds}, padre {clipped?.Parent?.ClientRectangle}).");
        }
    }

    private static void ValidateSalesHistoryForm(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        var tabs = descendants.OfType<TabControl>().FirstOrDefault();
        if (tabs is null || tabs.TabPages.Count != 1 || tabs.TabPages[0].Text != "Ventas")
        {
            errors.Add("SalesHistoryForm: los resultados no inician en una pestaña Ventas reutilizable.");
        }

        var salesGrid = descendants.OfType<DataGridView>().FirstOrDefault(grid => grid.Columns.Count == 9);
        if (salesGrid is null || !salesGrid.ReadOnly)
        {
            errors.Add("SalesHistoryForm: falta el historial de ventas de solo lectura.");
        }

        if (!descendants.OfType<Button>().Any(button => button.Text is "Reimprimir" or "Ver ticket"))
        {
            errors.Add("SalesHistoryForm: se perdió la acción existente para ver o reimprimir el ticket.");
        }

        if (!descendants.OfType<Button>().Any(button => button.Text == "Ventas de hoy"))
        {
            errors.Add("SalesHistoryForm: falta el acceso administrativo a Ventas de hoy.");
        }
    }

    private static void ValidateMySalesTodayForm(Form form, List<string> errors)
    {
        var fields = new[] { "_completedCount", "_completedTotal", "_statsNote", "_refresh" };
        foreach (var fieldName in fields)
        {
            var value = typeof(MySalesTodayForm).GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            if (value is null || !GetDescendants(form).Contains((Control)value))
            {
                errors.Add($"MySalesTodayForm: falta el nuevo resumen o la acción '{fieldName}'.");
            }
        }

        var descendants = GetDescendants(form).ToList();
        var expectedValues = new Dictionary<string, string>
        {
            ["_completedCount"] = "2",
            ["_completedTotal"] = "$300.00"
        };
        foreach (var (fieldName, expectedText) in expectedValues)
        {
            if (typeof(MySalesTodayForm).GetField(
                    fieldName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form)
                is not Label label || label.Text != expectedText || !label.Visible)
            {
                errors.Add($"MySalesTodayForm: '{fieldName}' no muestra el valor calculado esperado ({expectedText}).");
                continue;
            }

            var measured = TextRenderer.MeasureText(label.Text, label.Font);
            if (label.ClientSize.Height < measured.Height || label.ClientSize.Width < measured.Width ||
                label.Parent is not { } parent || label.Bottom > parent.ClientSize.Height)
            {
                errors.Add($"MySalesTodayForm: el valor '{label.Text}' queda recortado en {form.ClientSize.Width}x{form.ClientSize.Height}.");
            }
        }

        var refresh = descendants.OfType<Button>().FirstOrDefault(button => button.Text == "Actualizar");
        var refreshTextWidth = refresh is null
            ? 0
            : TextRenderer.MeasureText(refresh.Text, refresh.Font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        if (refresh is null || refresh.ClientSize.Width < refreshTextWidth + 28 || refresh.Height < 40)
        {
            errors.Add($"MySalesTodayForm: el botón Actualizar no deja ver su texto en {form.ClientSize.Width}x{form.ClientSize.Height}.");
        }
    }

    private static void SmokeSalesHistoryDetailTab(List<string> errors)
    {
        var sale = new SalesHistorySummary(
            41,
            DateTime.Now,
            "VTA-SMOKE-0041",
            "usuario-smoke",
            "cliente-smoke",
            2_500,
            100,
            "EFECTIVO",
            9,
            EstadoVenta.COMPLETADA);
        var detail = new SalesHistoryDetail(
            41,
            sale.NumeroVenta,
            sale.FechaVenta,
            sale.Usuario,
            sale.Cliente,
            2_600,
            100,
            2_500,
            3_000,
            500,
            [new SalesHistoryLine("Producto smoke", "SKU-SMOKE-0041", 1, 2_600, 100, 2_600, 2_500)],
            [new SalesHistoryPayment(MetodoPago.EFECTIVO, 3_000, null)]);
        foreach (var size in new[] { new Size(1000, 680), new Size(1260, 820), new Size(1920, 1080) })
        {
            using var host = new Form
            {
                ClientSize = size,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false
            };
            var view = new SalesHistoryDetailForm(sale, detail, null) { Dock = DockStyle.Fill };
            host.Controls.Add(view);

            try
            {
                host.Show();
                System.Windows.Forms.Application.DoEvents();
                var descendants = GetDescendants(view).ToList();
                if (view.Width <= 0 || view.Height <= 0 ||
                    !descendants.OfType<DataGridView>().Any(grid => grid.Columns.Count == 7 && grid.ReadOnly))
                {
                    errors.Add($"SalesHistoryDetailForm {size.Width}x{size.Height}: la pestaña no muestra productos en una tabla legible.");
                }

                ValidateButton(descendants, "SalesHistoryDetailForm", "Cerrar pestaña", 120, 36, errors);
                var labels = descendants.OfType<Label>().ToList();
                if (!labels.Any(label => label.Text.Contains(sale.NumeroVenta, StringComparison.Ordinal)) ||
                    !labels.Any(label => label.Text.Contains("Pagado registrado", StringComparison.Ordinal)))
                {
                    errors.Add($"SalesHistoryDetailForm {size.Width}x{size.Height}: falta identificación de venta o resumen de pago.");
                }

                var expectedAmounts = new[] { "$26.00", "$1.00", "$25.00", "$30.00", "$5.00" };
                var amountLabels = expectedAmounts
                    .Select(amount => labels.FirstOrDefault(label => label.Text == amount && label.Visible))
                    .ToList();
                if (amountLabels.Any(label => label is null || label.Height < TextRenderer.MeasureText(label.Text, label.Font).Height ||
                                               label.Parent is not { } parent || label.Bottom > parent.ClientSize.Height))
                {
                    errors.Add($"SalesHistoryDetailForm {size.Width}x{size.Height}: uno o más importes del resumen están ocultos o cortados.");
                }

                var paymentsGrid = descendants.OfType<DataGridView>()
                    .FirstOrDefault(grid => grid.Columns.Count == 3 && grid.Columns[0].HeaderText == "Método");
                if (paymentsGrid is null || paymentsGrid.Rows.Count != 1 ||
                    paymentsGrid.GetRowDisplayRectangle(0, cutOverflow: true).Height < paymentsGrid.Rows[0].Height)
                {
                    errors.Add($"SalesHistoryDetailForm {size.Width}x{size.Height}: la primera fila de pagos queda cortada.");
                }

                var closeButton = descendants.OfType<Button>().FirstOrDefault(button => button.Text == "Cerrar pestaña");
                if (closeButton is not null && closeButton.Parent is { } footer && closeButton.Bottom > footer.ClientSize.Height)
                {
                    errors.Add($"SalesHistoryDetailForm {size.Width}x{size.Height}: el botón de cierre se sale del pie.");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"SalesHistoryDetailForm {size.Width}x{size.Height}: {ex.GetType().Name} - {ex.Message}");
            }
            finally
            {
                host.Close();
            }
        }
    }

    private static void SmokeCashDailySummaryCardLayout(List<string> errors)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var sizes = new[] { new Size(980, 680), new Size(1180, 720), new Size(1366, 768), new Size(1920, 1080) };
        foreach (var (mode, expectedPermission) in new[]
                 {
                     (CashRegisterMode.Admin, true),
                     (CashRegisterMode.Employee, true),
                     (CashRegisterMode.Employee, false)
                 })
        {
            foreach (var size in sizes)
            {
                try
                {
                    var employeePermissions = new HashSet<PermisoEmpleado> { PermisoEmpleado.CAJA_ABRIR };
                    if (expectedPermission)
                    {
                        employeePermissions.Add(PermisoEmpleado.CAJA_TOTAL_ESPERADO_VER);
                    }
                    var permissions = new SmokePermissionService(employeePermissions);
                    var session = new SmokeSession(
                        mode == CashRegisterMode.Admin ? "admin" : "empleado",
                        mode == CashRegisterMode.Admin ? RolUsuario.ADMIN : RolUsuario.EMPLEADO);
                    using var form = new CashRegisterForm(
                        new SmokeCashHistoryService(), session, permissions, null!, null!, mode);
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-32000, -32000);
                    form.ShowInTaskbar = false;
                    form.WindowState = FormWindowState.Normal;
                    form.ClientSize = size;
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();

                    var dailySummary = typeof(CashRegisterForm).GetField("_lblDailyCashSummary", flags)?.GetValue(form) as Label
                        ?? throw new InvalidOperationException("No se encontró la tarjeta del resumen diario.");
                    var theoreticalAmount = typeof(CashRegisterForm).GetField("_lblTheoreticalCashAmount", flags)?.GetValue(form) as Label
                        ?? throw new InvalidOperationException("No se encontró el monto teórico de caja.");
                    var positionCard = GetDescendants(form).FirstOrDefault(control => control.Name == "CashPositionCard")
                        ?? throw new InvalidOperationException("No se encontró la tarjeta de efectivo teórico.");

                    var host = dailySummary.Parent as TableLayoutPanel;
                    if (mode == CashRegisterMode.Admin && (!dailySummary.Visible || host is null || host.ColumnStyles.Count != 5 ||
                            dailySummary.Width <= 0 || dailySummary.Height <= 0 || dailySummary.Right > host.ClientSize.Width + 1))
                    {
                        errors.Add($"CashRegisterForm ADMIN {size.Width}x{size.Height}: tarjeta diaria recortada o sin sus cinco columnas.");
                    }

                    var expectedAmount = mode == CashRegisterMode.Employee && !expectedPermission
                        ? "Restringido"
                        : "120.00";
                    var employeeHistoryCardVisible = mode == CashRegisterMode.Employee && dailySummary.Visible;
                    var employeeHasUnusedFifthColumn = mode != CashRegisterMode.Employee || host is not null &&
                        host.ColumnStyles.Count == 5 && Math.Abs(host.ColumnStyles[4].Width) < 0.01f;
                    if (host is null || host.ColumnStyles.Count != 5 || employeeHistoryCardVisible ||
                        !employeeHasUnusedFifthColumn || positionCard.Width <= 0 ||
                        positionCard.Height <= 0 || positionCard.Parent is not TableLayoutPanel cardHost ||
                        positionCard.Right > cardHost.ClientSize.Width + 1 || theoreticalAmount.Width <= 0 ||
                        theoreticalAmount.Height <= 0 || !theoreticalAmount.Text.Contains(expectedAmount, StringComparison.Ordinal))
                    {
                        errors.Add($"CashRegisterForm {mode} {size.Width}x{size.Height}: el saldo teórico no se muestra completo y legible " +
                                   $"(amount='{theoreticalAmount.Text}', bounds={positionCard.Bounds}, columns={host?.ColumnStyles.Count}).");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"CashRegisterForm resumen {mode} {size.Width}x{size.Height}: {ex.GetType().Name} - {ex.Message}");
                }
            }
        }
    }

    private sealed class SmokeCashHistoryService : ICashService
    {
        private static CashSummary OpenSummary() => new(
            1,
            DateOnly.FromDateTime(DateTime.Today),
            EstadoCaja.ABIERTA,
            10_000,
            2_000,
            0,
            0,
            0,
            12_000,
            null,
            DateTime.Now,
            null,
            "usuario-smoke",
            null,
            VentasEfectivoCentavos: 2_000);

        public Task<CashSummary> GetTodaySummaryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OpenSummary());

        public Task<CashExpectedBalanceSummary> GetTodayExpectedCashBalanceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CashExpectedBalanceSummary(
                DateOnly.FromDateTime(DateTime.Today), 1, 12_000, false));

        public Task<CashOpeningReference?> GetPreviousDayCashOpeningReferenceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CashOpeningReference?>(null);

        public Task<IReadOnlyList<CashSummary>> GetSessionHistoryAsync(
            DateOnly desde,
            DateOnly hasta,
            CancellationToken cancellationToken = default)
        {
            var summary = OpenSummary();
            IReadOnlyList<CashSummary> sessions = desde <= summary.FechaOperacion && hasta >= summary.FechaOperacion
                ? [summary]
                : [];
            return Task.FromResult(sessions);
        }

        public Task<CashRecoveryStatus?> GetOpenCashRecoveryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CashRecoveryStatus?>(null);

        public Task<CashSummary> ContinueCashAsync(ContinueCashRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashSummary> RegularizeCashAsync(RegularizeCashRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashSummary> RegularizeClosedCashDayAsync(RegularizeClosedCashDayRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashSummary> OpenCashAsync(OpenCashRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashSummary> OpenCashAutomaticallyIfEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(OpenSummary());
        public Task<CashSummary> CloseCashAsync(CloseCashRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashWithdrawalSummary> RegisterWithdrawalAsync(RegisterWithdrawalRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashContributionSummary> RegisterCashContributionAsync(RegisterCashContributionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashExpenseSummary> RegisterExpenseAsync(RegisterExpenseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExpenseCategoryOption>> GetExpenseCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CashWithdrawalSummary>> GetTodayWithdrawalsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CashWithdrawalSummary>>([]);
        public Task<IReadOnlyList<CashExpenseSummary>> GetTodayExpensesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CashExpenseSummary>>([]);
        public Task<IReadOnlyList<CashRefundSummary>> GetTodayRefundsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CashRefundSummary>>([]);
    }

    private sealed class SmokeMySalesTodayQueryService : IMySalesTodayQueryService
    {
        private static readonly IReadOnlyList<MySalesTodaySummary> Sales =
        [
            new(1, "VTA-SMOKE-0001", DateTime.Now, EstadoVenta.COMPLETADA, 0, 20_000, "EFECTIVO"),
            new(2, "VTA-SMOKE-0002", DateTime.Now, EstadoVenta.COMPLETADA, 0, 10_000, "EFECTIVO"),
            new(3, "VTA-SMOKE-0003", DateTime.Now, EstadoVenta.ANULADA, 0, 5_000, "EFECTIVO")
        ];

        public Task<IReadOnlyList<MySalesTodaySummary>> GetSalesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Sales);

        public Task<MySalesTodayDetail?> GetSaleDetailAsync(int ventaId, CancellationToken cancellationToken = default)
        {
            var sale = Sales.FirstOrDefault(item => item.VentaId == ventaId);
            MySalesTodayDetail? detail = sale is null
                ? null
                : new MySalesTodayDetail(
                    sale.VentaId,
                    sale.NumeroVenta,
                    sale.FechaVentaLocal,
                    sale.Estado,
                    sale.TotalCentavos,
                    0,
                    sale.TotalCentavos,
                    [new MySalesTodayLine("Producto smoke", "SKU-SMOKE-0001", 1, sale.TotalCentavos, 0, sale.TotalCentavos)],
                    [new MySalesTodayPayment("EFECTIVO", sale.TotalCentavos)]);
            return Task.FromResult(detail);
        }
    }

    private static void ValidateCashSessionDetailForm(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        if (form.MinimumSize.Width < 960 || form.MinimumSize.Height < 660)
        {
            errors.Add($"CashSessionDetailForm: MinimumSize insuficiente ({form.MinimumSize.Width}x{form.MinimumSize.Height}).");
        }

        if (!descendants.OfType<Label>().Any(label => label.Text == "Detalle de caja #7") ||
            !descendants.OfType<Label>().Any(label => label.Text.StartsWith("Consulta de solo lectura", StringComparison.Ordinal)) ||
            !descendants.OfType<Label>().Any(label => label.Text == "Movimientos registrados y referencia del día"))
        {
            errors.Add("CashSessionDetailForm: faltan el encabezado o el desglose de solo lectura.");
        }

        var grid = descendants.OfType<DataGridView>().FirstOrDefault();
        if (grid is null || grid.Dock != DockStyle.Fill || !grid.ReadOnly || grid.Columns.Count != 3 ||
            grid.AutoSizeColumnsMode != DataGridViewAutoSizeColumnsMode.Fill)
        {
            errors.Add("CashSessionDetailForm: el desglose debe ser una tabla de solo lectura con 3 columnas que llenen el espacio.");
        }
        else if (grid.RowTemplate.Height > 30)
        {
            errors.Add($"CashSessionDetailForm: la altura de fila ({grid.RowTemplate.Height}px) puede ocultar movimientos al maximizar.");
        }

        var differenceTitle = descendants.OfType<Label>()
            .FirstOrDefault(label => label.Name == "CashSessionDifferenceTitle");
        var differenceValue = descendants.OfType<Label>()
            .FirstOrDefault(label => label.Name == "CashSessionDifferenceValue");
        if (differenceTitle is null || differenceValue is null || differenceTitle.Text != "Diferencia" ||
            differenceTitle.ClientSize.Width < TextRenderer.MeasureText(differenceTitle.Text, differenceTitle.Font).Width ||
            differenceValue.ClientSize.Height < TextRenderer.MeasureText("Ag", differenceValue.Font).Height *
                differenceValue.Text.Split('\n').Length)
        {
            errors.Add("CashSessionDetailForm: la tarjeta de diferencia recorta el estado o el importe.");
        }

        if (form.ClientSize.Width >= 1920 && form.ClientSize.Height >= 1080 && grid is not null && grid.Rows.Count > 0 &&
            grid.DisplayedRowCount(false) < grid.Rows.Count)
        {
            errors.Add($"CashSessionDetailForm: la tabla no alcanza a mostrar todos los movimientos a 1920x1080 ({grid.DisplayedRowCount(false)}/{grid.Rows.Count}).");
        }

        ValidateButton(descendants, "CashSessionDetailForm", "Cerrar", 100, 36, errors);
    }

    private static CashSummary BuildCashSessionDetailSmokeSummary() => new(
        CajaId: 7,
        FechaOperacion: DateOnly.FromDateTime(DateTime.Today),
        Estado: EstadoCaja.CERRADA,
        MontoInicialCentavos: 20_000,
        TotalVentasCentavos: 3_000,
        TotalRetirosCentavos: 0,
        TotalGastosCentavos: 0,
        TotalReembolsosCentavos: 0,
        TotalEsperadoCentavos: 23_000,
        MontoCierreCentavos: 23_000,
        FechaApertura: DateTime.Today.AddHours(8),
        FechaCierre: DateTime.Today.AddHours(18),
        UsuarioApertura: "admin",
        UsuarioCierre: "admin",
        VentasEfectivoCentavos: 3_000);

    private static void ValidateCashContributionForm(Form form, List<string> errors)
    {
        if (form.MinimumSize.Width < 570 || form.MinimumSize.Height < 390)
        {
            errors.Add($"Aporte de efectivo: tamaño mínimo insuficiente ({form.MinimumSize.Width}x{form.MinimumSize.Height}).");
        }

        var descendants = GetDescendants(form).ToList();
        var amount = descendants.OfType<NumericUpDown>()
            .FirstOrDefault(control => control.Name == "CashContributionAmount");
        var reason = descendants.OfType<TextBox>()
            .FirstOrDefault(control => control.Name == "CashContributionReason");
        var save = descendants.OfType<Button>().FirstOrDefault(control => control.Text == "Registrar aporte");
        var cancel = descendants.OfType<Button>().FirstOrDefault(control => control.Text == "Cancelar");
        var requiredReasonLabel = descendants.OfType<Label>()
            .FirstOrDefault(control => control.Text == "Motivo (obligatorio)");

        if (amount is null || reason is null || save is null || cancel is null || requiredReasonLabel is null)
        {
            errors.Add("Aporte de efectivo: faltan campos obligatorios o acciones.");
            return;
        }

        if (amount.Minimum <= 0 || amount.DecimalPlaces != 2 || reason.MaxLength != 300 || !reason.Multiline)
        {
            errors.Add("Aporte de efectivo: monto, motivo obligatorio o límite de texto no está configurado correctamente.");
        }

        foreach (var control in new Control[] { amount, reason, save, cancel, requiredReasonLabel })
        {
            if (!control.Visible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0 ||
                control.Bounds.Left < 0 || control.Bounds.Top < 0 ||
                control.Bounds.Right > control.Parent!.ClientSize.Width ||
                control.Bounds.Bottom > control.Parent.ClientSize.Height)
            {
                errors.Add(
                    $"Aporte de efectivo: el control {control.Name}/{control.GetType().Name} se recorta o no está visible " +
                    $"(visible={control.Visible}, bounds={control.Bounds}, parent={control.Parent?.ClientSize}).");
            }
        }

        foreach (var button in new[] { save, cancel })
        {
            if (button.Width < button.PreferredSize.Width || button.Height < button.PreferredSize.Height)
            {
                errors.Add($"Aporte de efectivo: el botón '{button.Text}' está comprimido ({button.Size}, requiere {button.PreferredSize}).");
            }
        }
    }

    private static void SmokeCashOpeningReferenceLayout(List<string> errors)
    {
        SmokeForm(
            "CashOpeningReference",
            () =>
            {
                var form = new CashRegisterForm(
                    null!,
                    new SmokeSession(),
                    null!,
                    null!,
                    null!,
                    CashRegisterMode.Employee);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                foreach (var (fieldName, value) in new (string Name, bool Value)[]
                         {
                             ("_canOpenCash", true),
                             ("_canCloseCash", false),
                             ("_canRegisterExpense", false),
                             ("_canRegisterWithdrawal", true),
                             ("_canRegisterCashContribution", true)
                         })
                {
                    var field = typeof(CashRegisterForm).GetField(fieldName, flags)
                        ?? throw new InvalidOperationException($"No se encontró el campo {fieldName} para el smoke.");
                    field.SetValue(form, value);
                }

                form.UpdatePreviousCashOpeningReference(new CashOpeningReference(
                    42,
                    new DateOnly(2026, 10, 4),
                    12_345,
                    CantidadSesionesIncluidas: 1));
                var updateVisibility = typeof(CashRegisterForm).GetMethod("SetOperationVisibility", flags)
                    ?? throw new InvalidOperationException("No se encontró SetOperationVisibility para el smoke.");
                updateVisibility.Invoke(form, null);
                return form;
            },
            errors,
            CashOpeningSmokeSizes);

        SmokeForm(
            "CashOpeningReferenceAdmin",
            () =>
            {
                var form = new CashRegisterForm(
                    null!,
                    new SmokeSession(),
                    null!,
                    null!,
                    null!,
                    CashRegisterMode.Admin);
                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                foreach (var fieldName in new[]
                         {
                             "_canOpenCash",
                             "_canCloseCash",
                             "_canRegisterExpense",
                             "_canRegisterWithdrawal",
                             "_canRegisterCashContribution"
                         })
                {
                    var field = typeof(CashRegisterForm).GetField(fieldName, flags)
                        ?? throw new InvalidOperationException($"No se encontró el campo {fieldName} para el smoke ADMIN.");
                    field.SetValue(form, true);
                }

                form.UpdatePreviousCashOpeningReference(new CashOpeningReference(
                    42,
                    new DateOnly(2026, 10, 4),
                    12_345,
                    CantidadSesionesIncluidas: 1));
                var updateVisibility = typeof(CashRegisterForm).GetMethod("SetOperationVisibility", flags)
                    ?? throw new InvalidOperationException("No se encontró SetOperationVisibility para el smoke ADMIN.");
                updateVisibility.Invoke(form, null);
                return form;
            },
            errors,
            CashOpeningSmokeSizes);
    }

    private static void ValidateCashOpeningReferenceForm(Form form, bool isAdmin, List<string> errors)
    {
        if (form.MinimumSize.Width < 980 || form.MinimumSize.Height < 680)
        {
            errors.Add($"Caja diaria: MinimumSize insuficiente ({form.MinimumSize.Width}x{form.MinimumSize.Height}).");
        }

        var descendants = GetDescendants(form).ToList();
        var panel = descendants.OfType<TableLayoutPanel>()
            .FirstOrDefault(control => control.Name == "CashOpeningActionPanel");
        var operations = descendants.OfType<TableLayoutPanel>()
            .FirstOrDefault(control => control.Name == "CashOperationsPanel");
        var closingAction = descendants.OfType<Control>()
            .FirstOrDefault(control => control.Name == "CashClosingActionPanel");
        var outputAction = descendants.OfType<Control>()
            .FirstOrDefault(control => control.Name == "CashOutputActionPanel");
        var outputHeader = descendants.OfType<TableLayoutPanel>()
            .FirstOrDefault(control => control.Name == "CashOutputHeaderPanel");
        var outputTitle = descendants.OfType<Label>()
            .FirstOrDefault(control => control.Text == "Salidas y reembolsos registrados");
        var previousClosePanel = descendants.OfType<TableLayoutPanel>()
            .FirstOrDefault(control => control.Name == "CashPreviousCloseReferencePanel");
        var amount = descendants.OfType<NumericUpDown>()
            .FirstOrDefault(control => control.Name == "CashOpeningAmount");
        var closingAmount = descendants.OfType<NumericUpDown>()
            .FirstOrDefault(control => control.Name == "CashClosingAmount");
        var previousClose = descendants.OfType<Label>()
            .FirstOrDefault(control => control.Name == "CashOpeningPreviousCloseLabel");
        var confirmation = descendants.OfType<CheckBox>()
            .FirstOrDefault(control => control.Name == "CashOpeningUsePreviousCloseCheckBox");
        var openButton = descendants.OfType<Button>()
            .FirstOrDefault(control => control.Name == "CashOpenButton");
        var registerOutputButton = descendants.OfType<Button>()
            .FirstOrDefault(control => control.Text == "Registrar salida");
        var contributionButton = descendants.OfType<Button>()
            .FirstOrDefault(control => control.Text == "Aportar efectivo");

        if (panel is null || amount is null || closingAmount is null || previousClose is null ||
            confirmation is null || openButton is null ||
            registerOutputButton is null || contributionButton is null ||
            outputHeader is null || outputTitle is null || previousClosePanel is null)
        {
            errors.Add(
                $"Caja diaria {(isAdmin ? "ADMIN" : "EMPLEADO")}: faltan controles requeridos. " +
                $"panel={panel is not null}; amount={amount is not null}; closingAmount={closingAmount is not null}; " +
                $"previousClose={previousClose is not null}; confirmationPresent={confirmation is not null}; " +
                $"openButton={openButton is not null}; outputButton={registerOutputButton is not null}; " +
                $"contributionButton={contributionButton is not null}; outputHeader={outputHeader is not null}; " +
                $"outputTitle={outputTitle is not null}; referencePanel={previousClosePanel is not null}.");
            return;
        }

        if (operations is null || closingAction is not TableLayoutPanel closingPanel ||
            outputAction is not TableLayoutPanel outputPanel)
        {
            errors.Add("Caja diaria: faltan paneles de acciones para comprobar alineación y distribución.");
        }
        else
        {
            var expectedOutputColumn = isAdmin ? 2 : 1;
            if (operations.GetColumn(panel) != 0 || operations.GetColumn(outputPanel) != expectedOutputColumn ||
                (!isAdmin && (outputPanel.Bounds.Left > operations.ClientSize.Width * 0.4 ||
                              outputPanel.Bounds.Width < operations.ClientSize.Width * 0.55)))
            {
                errors.Add(
                    $"Caja diaria: distribución incorrecta para {(isAdmin ? "ADMIN" : "EMPLEADO")}. " +
                    $"Apertura={operations.GetColumn(panel)}, salida={operations.GetColumn(outputPanel)}, " +
                    $"salida bounds={outputPanel.Bounds}, operaciones={operations.ClientSize}.");
            }

            if (!HasCashActionRows(panel) || !HasCashActionRows(closingPanel) || !HasCashActionRows(outputPanel) ||
                panel.Padding != new Padding(8) || closingPanel.Padding != new Padding(8) ||
                outputPanel.Padding != new Padding(8))
            {
                errors.Add("Caja diaria: los paneles de apertura, cierre y salida no comparten filas y márgenes alineados.");
            }
        }

        const string expectedReferenceText = "Último conteo · 04/10:";
        if (!previousClose.Text.StartsWith(expectedReferenceText, StringComparison.Ordinal) ||
            !confirmation!.Text.Contains("referencia", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Caja diaria: el último conteo físico no está identificado correctamente como referencia/confirmación.");
        }

        if (isAdmin && (amount.Value != 0 || IsPreviousCloseChecked(form)))
        {
            errors.Add("Caja diaria ADMIN: el cierre de ayer debe ser solo referencia y no precargar la apertura.");
        }
        if (contributionButton.Width < 160 || contributionButton.Height < 38)
        {
            errors.Add($"Caja diaria: el botón de aporte quedó demasiado pequeño ({contributionButton.Size}).");
        }
        if (isAdmin && previousClosePanel.ColumnCount != 2)
        {
            errors.Add("Caja diaria ADMIN: la referencia y su control deben conservar las dos columnas del panel.");
        }

        var titleWidth = TextRenderer.MeasureText(outputTitle.Text, outputTitle.Font).Width + 4;
        if (outputTitle.Width < titleWidth || outputTitle.Height < TextRenderer.MeasureText("Ag", outputTitle.Font).Height)
        {
            errors.Add(
                $"Caja diaria: el título de salidas se puede recortar " +
                $"({outputTitle.Width}x{outputTitle.Height}, requiere {titleWidth}px de ancho).");
        }

        var openingControls = new Control[] { amount, openButton };
        var clippedOpeningControls = openingControls.Where(control =>
                !ReferenceEquals(control.Parent, panel) ||
                control.Bounds.Width <= 0 ||
                control.Bounds.Height <= 0 ||
                control.Bounds.Right > panel.ClientSize.Width ||
                control.Bounds.Bottom > panel.ClientSize.Height)
            .ToList();
        if (clippedOpeningControls.Count > 0)
        {
            var details = string.Join("; ", clippedOpeningControls.Select(control =>
                $"{control.Name}={control.Bounds} parentMatch={ReferenceEquals(control.Parent, panel)}"));
            errors.Add(
                $"Caja diaria: un control de apertura se recorta o sale del panel " +
                $"({panel.ClientSize}). {details}");
        }

        var referenceControls = isAdmin
            ? new Control[] { previousClose }
            : new Control[] { previousClose, confirmation! };
        var clippedReferenceControls = referenceControls.Where(control =>
                !ReferenceEquals(control.Parent, previousClosePanel) ||
                control.Bounds.Width <= 0 ||
                control.Bounds.Height <= 0 ||
                control.Bounds.Right > previousClosePanel.ClientSize.Width ||
                control.Bounds.Bottom > previousClosePanel.ClientSize.Height)
            .ToList();
        if (!ReferenceEquals(previousClosePanel.Parent, outputHeader) ||
            previousClosePanel.Bounds.Width <= 0 ||
            previousClosePanel.Bounds.Height < 34 ||
            previousClosePanel.Bounds.Right > outputHeader.ClientSize.Width ||
            previousClosePanel.Bounds.Bottom > outputHeader.ClientSize.Height ||
            clippedReferenceControls.Count > 0)
        {
            var details = string.Join("; ", clippedReferenceControls.Select(control =>
                $"{control.Name}={control.Bounds} parentMatch={ReferenceEquals(control.Parent, previousClosePanel)}"));
            errors.Add(
                $"Caja diaria: el cierre anterior no cabe junto al título de salidas. " +
                $"header={outputHeader.ClientSize}, referencia={previousClosePanel.Bounds}/{previousClosePanel.ClientSize}, controles={details}.");
        }

        if (!isAdmin && previousClose.Bounds.IntersectsWith(confirmation!.Bounds))
        {
            errors.Add("Caja diaria EMPLEADO: el monto del cierre anterior y la confirmación se enciman.");
        }

        if (!isAdmin)
        {
            var checkTextWidth = TextRenderer.MeasureText(confirmation!.Text, confirmation.Font).Width + 22;
            var checkTextHeight = TextRenderer.MeasureText("Ag", confirmation.Font).Height + 2;
            if (confirmation.Width < checkTextWidth || confirmation.Height < checkTextHeight)
            {
                errors.Add(
                    $"Caja diaria EMPLEADO: la confirmación puede recortarse " +
                    $"({confirmation.Width}x{confirmation.Height}, requiere {checkTextWidth}x{checkTextHeight}px).");
            }
        }

        var previousCloseTextWidth = TextRenderer.MeasureText(previousClose.Text, previousClose.Font).Width + 4;
        if (previousClose.Width < previousCloseTextWidth || previousClose.Height < TextRenderer.MeasureText("Ag", previousClose.Font).Height)
        {
            errors.Add(
                $"Caja diaria: el texto del cierre anterior puede recortarse " +
                $"({previousClose.Width}x{previousClose.Height}, requiere {previousCloseTextWidth}px de ancho).");
        }

        if (openButton.Dock != DockStyle.Fill || openButton.Width < panel.ClientSize.Width * 0.85 ||
            openButton.Height < 30 || openButton.Height > 50 ||
            Math.Abs(openButton.Height - registerOutputButton.Height) > 2 ||
            amount.Bounds.Top != closingAmount.Bounds.Top)
        {
            errors.Add(
                $"Caja diaria: apertura no está alineada con cierre/salida " +
                $"(Abrir={openButton.Width}x{openButton.Height}, Registrar={registerOutputButton.Width}x{registerOutputButton.Height}, " +
                $"monto apertura Y={amount.Bounds.Top}, cierre Y={closingAmount.Bounds.Top}).");
        }

        static bool HasCashActionRows(TableLayoutPanel actionPanel) =>
            actionPanel.RowStyles.Count >= 3 &&
            actionPanel.RowStyles[0].SizeType == SizeType.Absolute && actionPanel.RowStyles[0].Height == 28 &&
            actionPanel.RowStyles[1].SizeType == SizeType.Absolute && actionPanel.RowStyles[1].Height == 36 &&
            actionPanel.RowStyles[2].SizeType == SizeType.Absolute && actionPanel.RowStyles[2].Height == 40;

        static bool IsPreviousCloseChecked(Form target)
        {
            var field = typeof(CashRegisterForm).GetField(
                "_chkUsePreviousClose",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("No se encontró la confirmación de continuidad para el smoke.");
            return ((CheckBox)field.GetValue(target)!).Checked;
        }
    }

    private static void ValidateQuotationForms(string name, Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        if (name != "QuotationConversionDateForm" && !descendants.OfType<DataGridView>().Any())
        {
            errors.Add($"{name}: no se encontro la tabla principal.");
        }

        foreach (var button in descendants.OfType<Button>().Where(button => button.Visible))
        {
            if (button.Width < 70 || button.Height < 30)
            {
                errors.Add($"{name}: boton '{button.Text}' recortado ({button.Width}x{button.Height}).");
            }
        }

        if (name == "QuotationDetailForm")
        {
            foreach (var text in new[] { "Cliente", "Codigo del cliente", "Contacto", "Creada por", "Actualizada por", "Subtotal", "Descuento", "Total", "Observaciones" })
            {
                var label = descendants.OfType<Label>().FirstOrDefault(control => control.Text == text);
                if (label is null || label.Height < 18)
                {
                    errors.Add($"{name}: campo de resumen '{text}' ausente o comprimido.");
                    continue;
                }
                if (label.Parent is TableLayoutPanel table)
                {
                    var position = table.GetPositionFromControl(label);
                    var value = table.GetControlFromPosition(position.Column, position.Row + 1) as Label;
                    if (string.IsNullOrWhiteSpace(value?.Text))
                        errors.Add($"{name}: valor de resumen '{text}' vacio.");
                }
            }
        }

        if (name == "QuotationConversionDateForm")
        {
            foreach (var text in new[] { "Cancelar", "Convertir en pedido" })
                ValidateButtonByText(name, descendants, text, errors);
        }
    }

    private static void ValidateCommercialAlertsForm(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        foreach (var text in new[] { "Tipo de alerta", "Estado", "Desde", "Hasta", "Aplicar rango", "Busqueda general" })
        {
            if (!descendants.OfType<Label>().Any(label => label.Text == text))
                errors.Add($"CommercialAlertsForm: filtro '{text}' ausente.");
        }
        foreach (var text in new[] { "Buscar", "Limpiar", "Actualizar", "Cerrar" })
            ValidateButtonByText("CommercialAlertsForm", descendants, text, errors);
        foreach (var picker in descendants.OfType<DateTimePicker>())
        {
            if (picker.Width < 110)
                errors.Add($"CommercialAlertsForm: selector de fecha comprimido ({picker.Width}px).");
        }

        var useDates = descendants.OfType<CheckBox>().FirstOrDefault(checkBox => checkBox.Text == "Usar fechas");
        if (useDates is null || useDates.Height < 36 || useDates.Width < 90)
            errors.Add($"CommercialAlertsForm: 'Usar fechas' ausente o comprimido ({useDates?.Width}x{useDates?.Height}).");

        foreach (var text in new[] { "Buscar", "Limpiar" })
        {
            var button = descendants.OfType<Button>().FirstOrDefault(control => control.Text == text);
            if (button is null || button.Height < 40 || button.TextAlign != ContentAlignment.MiddleCenter)
                errors.Add($"CommercialAlertsForm: boton '{text}' sin alto o alineacion correctos.");
        }
    }

    private static void SmokeQuotationProductSearch(List<string> errors)
    {
        try
        {
            using (var form = ShowProductPicker())
            {
                var (search, searchButton, grid) = ProductPickerControls(form);
                ValidateQuotationProductPickerLayout(form, grid, errors);
                search.Text = "PRD-SMOKE";
                searchButton.PerformClick();
                ValidateCommercialActions(
                    "QuotationProductPickerForm",
                    form,
                    ["X", "Buscar", "Seleccionar", "Cancelar"],
                    errors);
                if (grid.Rows.Count != 1 || grid.Rows[0].Cells[0].Value?.ToString() != "PRD-SMOKE-001")
                    errors.Add("QuotationProductPickerForm: la busqueda por codigo no devolvio el producto esperado.");
                if (!HasGridHeaders(grid, "Categoría", "Subcategoría") ||
                    grid.Rows[0].Cells[2].Value?.ToString() != "Artesanías" ||
                    grid.Rows[0].Cells[3].Value?.ToString() != "Jarrones")
                    errors.Add("QuotationProductPickerForm: no mostro categoria y subcategoria del producto.");
                ValidateCommercialGridScroll("QuotationProductPickerForm", grid, errors);

                var select = GetDescendants(form).OfType<Button>().First(button => button.Name == "SelectQuotationProductButton");
                select.PerformClick();
                if (form.DialogResult != DialogResult.OK || form.SelectedProduct?.Id != 1 ||
                    form.SelectedProduct?.Category != "Artesanías" || form.SelectedProduct?.Subcategory != "Jarrones")
                    errors.Add("QuotationProductPickerForm: Seleccionar no devolvio el producto esperado.");
            }

            using (var form = ShowProductPicker())
            {
                var (search, searchButton, grid) = ProductPickerControls(form);
                search.Text = "Jarrones";
                searchButton.PerformClick();
                SaveCommercialSelectorScreenshot(form, "quotation-product-picker");
                if (grid.Rows.Count != 1 || grid.Rows[0].Cells[3].Value?.ToString() != "Jarrones")
                    errors.Add("QuotationProductPickerForm: la busqueda por subcategoria no devolvio el producto esperado.");
            }

            using (var form = ShowProductPicker())
            {
                var (search, searchButton, grid) = ProductPickerControls(form);
                search.Text = "artesanal";
                searchButton.PerformClick();
                if (grid.Rows.Count != 1 || !grid.Rows[0].Cells[1].Value!.ToString()!.Contains("artesanal", StringComparison.OrdinalIgnoreCase))
                    errors.Add("QuotationProductPickerForm: la busqueda por nombre no devolvio el producto esperado.");
                if (!form.AcceptRow(0, showValidationMessage: false) || form.SelectedProduct?.Id != 1)
                    errors.Add("QuotationProductPickerForm: el doble clic no comparte la seleccion esperada.");
            }

            using (var form = ShowProductPicker())
            {
                var (search, searchButton, _) = ProductPickerControls(form);
                search.Text = "SIN-RESULTADOS";
                searchButton.PerformClick();
                if (form.AcceptSelection(showValidationMessage: false) || form.SelectedProduct is not null)
                    errors.Add("QuotationProductPickerForm: acepto una seleccion inexistente.");
            }

            using (var form = ShowProductPicker())
            {
                if (!form.AcceptRow(0, showValidationMessage: false) || form.SelectedProduct?.Id is not null)
                    errors.Add("QuotationProductPickerForm: la linea descriptiva no esta disponible.");
            }

            using (var form = ShowProductPicker())
            {
                var cancel = GetDescendants(form).OfType<Button>().First(button => button.Name == "CancelQuotationProductButton");
                cancel.PerformClick();
                if (form.DialogResult != DialogResult.Cancel || form.SelectedProduct is not null)
                    errors.Add("QuotationProductPickerForm: Cancelar devolvio o modifico una seleccion.");
            }

            using var editor = new QuotationEditForm(new SmokeQuotationService(), new SmokeCustomerQueryService(), new SmokeProductService())
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false
            };
            PrepareCommercialSmokeForm(editor, new Size(760, 520));
            editor.Show();
            System.Windows.Forms.Application.DoEvents();
            if (editor.ProductOptionCount != 2)
                errors.Add("QuotationEditForm: excluyo del selector un producto activo sin stock.");
            var selected = BuildQuotationProducts()[1];
            editor.ApplySelectedProduct(selected, focusQuantity: false);
            if (editor.SelectedProduct?.Id != selected.Id || editor.CurrentLineDescription != selected.DisplayName || editor.CurrentLinePrice != 1.50m)
                errors.Add("QuotationEditForm: el producto seleccionado no se aplico al editor de linea.");
            var selectedLabel = GetDescendants(editor)
                .OfType<Label>()
                .Single(label => label.Name == "SelectedQuotationProductLabel");
            if (!selectedLabel.Text.Contains("Artesanías", StringComparison.Ordinal) ||
                !selectedLabel.Text.Contains("Jarrones", StringComparison.Ordinal))
                errors.Add("QuotationEditForm: no mostro categoria y subcategoria del producto seleccionado.");
            var add = GetDescendants(editor).OfType<Button>().First(button => button.Name == "AddQuotationLineButton");
            add.PerformClick();
            if (editor.LineCount != 1)
                errors.Add("QuotationEditForm: Agregar linea no creo la linea seleccionada.");
        }
        catch (Exception ex)
        {
            errors.Add($"QuotationProductPickerForm busqueda: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void SmokeQuotationProductPickerDpiScaling(List<string> errors)
    {
        foreach (var scale in new[] { 1f, 1.25f, 1.5f })
        {
            foreach (var targetSize in new[] { new Size(1280, 720), new Size(1920, 1080) })
            {
                try
                {
                    using var form = new QuotationProductPickerForm(BuildQuotationProducts())
                    {
                        StartPosition = FormStartPosition.Manual,
                        Location = new Point(-32000, -32000),
                        ShowInTaskbar = false,
                        AutoScaleMode = AutoScaleMode.None
                    };
                    if (scale != 1f)
                    {
                        form.Scale(new SizeF(scale, scale));
                    }

                    form.ClientSize = targetSize;
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.PerformLayout();
                    PerformLayoutRecursive(form);
                    var (_, _, grid) = ProductPickerControls(form);
                    ValidateQuotationProductPickerLayout(
                        form,
                        grid,
                        errors,
                        $"QuotationProductPickerForm {targetSize.Width}x{targetSize.Height} DPI {scale * 100:0}");
                    SaveCommercialSelectorScreenshot(
                        form,
                        $"quotation-product-picker-{targetSize.Width}x{targetSize.Height}-dpi{scale * 100:0}");
                }
                catch (Exception ex)
                {
                    errors.Add(
                        $"QuotationProductPickerForm {targetSize.Width}x{targetSize.Height} DPI {scale * 100:0}: " +
                        $"{ex.GetType().Name} - {ex.Message}");
                }
            }
        }
    }

    private static void ValidateQuotationProductPickerLayout(
        QuotationProductPickerForm form,
        DataGridView grid,
        List<string> errors,
        string formName = "QuotationProductPickerForm")
    {
        var price = grid.Columns
            .Cast<DataGridViewColumn>()
            .SingleOrDefault(column => column.HeaderText == "Precio de venta");
        if (price is null)
        {
            errors.Add($"{formName}: falta la columna Precio de venta.");
        }
        else
        {
            var headerFont = price.HeaderCell.InheritedStyle.Font ??
                grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
            var requiredHeader = TextRenderer.MeasureText(
                price.HeaderText,
                headerFont,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 16;
            if (price.Width < requiredHeader)
            {
                errors.Add(
                    $"{formName}: el encabezado Precio de venta no cabe " +
                    $"({price.Width}px, requiere {requiredHeader}px).");
            }

            if (grid.Rows.Count > 0)
            {
                try
                {
                    grid.FirstDisplayedScrollingColumnIndex = price.Index;
                    System.Windows.Forms.Application.DoEvents();
                    var viewport = grid.DisplayRectangle;
                    var bounds = grid.GetColumnDisplayRectangle(price.Index, true);
                    if (bounds.Width <= 0 || bounds.Left < viewport.Left || bounds.Right > viewport.Right + 1)
                    {
                        errors.Add(
                            $"{formName}: Precio de venta no es alcanzable mediante scroll " +
                            $"(columna={bounds}, viewport={viewport}).");
                    }
                }
                catch (InvalidOperationException ex)
                {
                    errors.Add($"{formName}: scroll hasta Precio de venta fallo ({ex.Message}).");
                }
            }
        }

        foreach (var text in new[] { "Buscar", "Seleccionar", "Cancelar" })
        {
            var button = GetDescendants(form)
                .OfType<Button>()
                .FirstOrDefault(candidate => candidate.Text == text);
            if (button is null)
            {
                errors.Add($"{formName}: falta el botón '{text}'.");
                continue;
            }

            var bounds = BoundsInAncestor(button, form);
            var measured = TextRenderer.MeasureText(
                button.Text,
                button.Font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (!form.ClientRectangle.Contains(bounds) || button.ClientSize.Width < measured.Width + 16 ||
                button.ClientSize.Height < measured.Height + 8)
            {
                errors.Add($"{formName}: botón '{text}' está fuera o tiene texto recortado.");
            }
        }
    }

    private static void SmokePurchaseProductSearch(List<string> errors)
    {
        try
        {
            using (var form = ShowPurchaseProductPicker())
            {
                var grid = GetDescendants(form).OfType<DataGridView>().Single();
                var search = GetDescendants(form).OfType<TextBox>()
                    .Single(control => control.Name == "PurchaseProductSearchTextBox");
                search.Text = "reze";
                form.SearchNowAsync().GetAwaiter().GetResult();
                ValidateCommercialActions(
                    "PurchaseProductPickerForm",
                    form,
                    ["×", "Seleccionar", "Cancelar"],
                    errors);
                if (form.ResultCount != 1)
                    errors.Add("PurchaseProductPickerForm: la búsqueda parcial por nombre no devolvió reze.");
                if (!HasGridHeaders(grid, "Categoría", "Subcategoría") ||
                    grid.Rows[0].Cells[4].Value?.ToString() != "Jarrones")
                    errors.Add("PurchaseProductPickerForm: no mostro categoria y subcategoria del producto.");
                ValidateCommercialGridScroll("PurchaseProductPickerForm", grid, errors, "Último costo");
                SaveCommercialSelectorScreenshot(form, "purchase-product-picker-scrolled");
                SavePurchaseProductPickerScreenshot(form);
                if (!form.AcceptRow(0, showValidationMessage: false) || form.SelectedProduct?.Code != "PRD-000364")
                    errors.Add("PurchaseProductPickerForm: no seleccionó el producto encontrado por nombre.");
                if (form.SelectedProduct?.DisplaySubcategory != "Jarrones")
                    errors.Add("PurchaseProductPickerForm: la seleccion no conservo la subcategoria.");
            }

            using (var form = ShowPurchaseProductPicker())
            {
                var search = GetDescendants(form).OfType<TextBox>()
                    .Single(control => control.Name == "PurchaseProductSearchTextBox");
                search.Text = "Jarrones";
                form.SearchNowAsync().GetAwaiter().GetResult();
                SaveCommercialSelectorScreenshot(form, "purchase-product-picker");
                if (form.ResultCount != 1 || !form.AcceptRow(0, showValidationMessage: false) ||
                    form.SelectedProduct?.Subcategory != "Jarrones")
                    errors.Add("PurchaseProductPickerForm: la busqueda por subcategoria no devolvio el SKU esperado.");
            }

            using (var form = ShowPurchaseProductPicker())
            {
                var grid = GetDescendants(form).OfType<DataGridView>().Single();
                var search = GetDescendants(form).OfType<TextBox>()
                    .Single(control => control.Name == "PurchaseProductSearchTextBox");
                search.Text = "Sin subcategoría";
                form.SearchNowAsync().GetAwaiter().GetResult();
                if (form.ResultCount != 1 || grid.Rows[0].Cells[4].Value?.ToString() != "Sin subcategoría")
                    errors.Add("PurchaseProductPickerForm: no represento correctamente un producto sin subcategoria.");
            }

            using (var form = ShowPurchaseProductPicker())
            {
                var search = GetDescendants(form).OfType<TextBox>()
                    .Single(control => control.Name == "PurchaseProductSearchTextBox");
                search.Text = "barro negro";
                form.SearchNowAsync().GetAwaiter().GetResult();
                if (form.ResultCount != 1)
                    errors.Add("PurchaseProductPickerForm: la búsqueda por categoría no devolvió el producto esperado.");
            }

            using (var form = ShowPurchaseProductPicker())
            {
                var search = GetDescendants(form).OfType<TextBox>()
                    .Single(control => control.Name == "PurchaseProductSearchTextBox");
                search.Text = "SIN-COINCIDENCIAS";
                form.SearchNowAsync().GetAwaiter().GetResult();
                if (form.ResultCount != 0 || form.AcceptSelection(showValidationMessage: false))
                    errors.Add("PurchaseProductPickerForm: una búsqueda sin resultados conservó o aceptó productos.");
            }

            using var editor = new PurchaseEditForm(null!, null!, null!, loadDataOnShow: false)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false
            };
            PrepareCommercialSmokeForm(editor, new Size(900, 590));
            editor.Show();
            System.Windows.Forms.Application.DoEvents();
            var selected = BuildPurchaseProducts()[0];
            editor.ApplySelectedProduct(selected);
            var selectedLabel = GetDescendants(editor)
                .OfType<Label>()
                .Single(label => label.Name == "SelectedPurchaseProductLabel");
            if (!selectedLabel.Text.Contains("Barro negro", StringComparison.Ordinal) ||
                !selectedLabel.Text.Contains("Jarrones", StringComparison.Ordinal))
                errors.Add("PurchaseEditForm: no mostro categoria y subcategoria del producto seleccionado.");
            var add = GetDescendants(editor).OfType<Button>()
                .Single(button => button.Name == "AddPurchaseLineButton");
            add.PerformClick();
            if (editor.SelectedProduct?.Id != selected.Id || editor.LineCount != 1)
                errors.Add("PurchaseEditForm: el producto seleccionado no se agregó a la compra.");
            SavePurchaseEditorScreenshot(editor);
        }
        catch (Exception ex)
        {
            errors.Add($"PurchaseProductPickerForm búsqueda: {ex}");
        }
    }

    private static PurchaseProductPickerForm ShowPurchaseProductPicker()
    {
        var form = new PurchaseProductPickerForm(BuildPurchaseProducts())
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            ShowInTaskbar = false
        };
        PrepareCommercialSmokeForm(form, new Size(900, 590));
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        form.SearchNowAsync().GetAwaiter().GetResult();
        return form;
    }

    private static void SavePurchaseProductPickerScreenshot(PurchaseProductPickerForm form)
    {
        var output = Environment.GetEnvironmentVariable("ARTE_PURCHASE_PRODUCT_SEARCH_SMOKE_IMAGE");
        if (string.IsNullOrWhiteSpace(output)) return;
        var fullPath = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void SavePurchaseEditorScreenshot(PurchaseEditForm form)
    {
        var output = Environment.GetEnvironmentVariable("ARTE_PURCHASE_EDITOR_SMOKE_IMAGE");
        if (string.IsNullOrWhiteSpace(output)) return;
        var fullPath = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static QuotationProductPickerForm ShowProductPicker()
    {
        var form = new QuotationProductPickerForm(BuildQuotationProducts())
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            ShowInTaskbar = false
        };
        PrepareCommercialSmokeForm(form, new Size(1000, 620));
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        return form;
    }

    private static (TextBox Search, Button SearchButton, DataGridView Grid) ProductPickerControls(QuotationProductPickerForm form)
    {
        var descendants = GetDescendants(form).ToList();
        return (
            descendants.OfType<TextBox>().First(),
            descendants.OfType<Button>().First(button => button.Text == "Buscar"),
            descendants.OfType<DataGridView>().First());
    }

    private static void ValidateAnalyticsCategoryTrend(
        IReadOnlyList<Control> descendants,
        List<string> errors)
    {
        var chart = descendants
            .OfType<CartesianChart>()
            .SingleOrDefault(item => item.Name == "AnalyticsCategoryTrendChart");
        var emptyState = descendants
            .OfType<Label>()
            .SingleOrDefault(item => item.Name == "AnalyticsCategoryTrendEmptyState");
        if (chart is null || emptyState is null)
        {
            errors.Add("AnalyticsForm Categorías: faltan la tendencia o su estado vacío.");
            return;
        }

        var labels = chart.XAxes?.FirstOrDefault()?.Labels?.ToArray() ?? [];
        var series = chart.Series.OfType<LineSeries<long>>().ToArray();
        if (series.Length == 0)
        {
            if (!emptyState.Visible || emptyState.Text != "Sin registros")
            {
                errors.Add("AnalyticsForm Categorías: tendencia sin datos no muestra 'Sin registros'.");
            }
            return;
        }

        if (emptyState.Visible || chart.ClientSize.Height < 120)
        {
            errors.Add(
                $"AnalyticsForm Categorías: tendencia con datos no tiene área útil suficiente " +
                $"(VisibleEmpty={emptyState.Visible}, Height={chart.ClientSize.Height}, " +
                $"Bounds={chart.Bounds}, Parent={chart.Parent?.ClientSize}).");
        }

        var valueSets = series
            .Select(item => item.Values?.ToArray() ?? [])
            .ToArray();
        if (labels.Length < 2 || valueSets.Any(values => values.Length != labels.Length) ||
            !valueSets.SelectMany(values => values).Any(value => value != 0))
        {
            errors.Add(
                $"AnalyticsForm Categorías: puntos inválidos en tendencia " +
                $"(series={series.Length}, labels={labels.Length}, " +
                $"points={string.Join('/', valueSets.Select(values => values.Length))}).");
        }
    }

    private static void SmokeAnalyticsCategoryTrendEmptyState(List<string> errors)
    {
        try
        {
            using var form = new AnalyticsForm(
                new SmokeSalesAnalyticsQueryService(categoryTrendEmpty: true),
                new SmokeFinancialAnalyticsQueryService(),
                new SmokeCommercialAnalyticsQueryService());
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.ClientSize = new Size(1280, 720);
            form.Show();
            PumpUi(TimeSpan.FromMilliseconds(250));
            var categories = GetDescendants(form)
                .OfType<Button>()
                .Single(button => button.AccessibleName == "Categorías");
            categories.PerformClick();
            PumpUi(TimeSpan.FromMilliseconds(100));
            var chart = GetDescendants(form)
                .OfType<CartesianChart>()
                .Single(item => item.Name == "AnalyticsCategoryTrendChart");
            var emptyState = GetDescendants(form)
                .OfType<Label>()
                .Single(item => item.Name == "AnalyticsCategoryTrendEmptyState");
            if (!emptyState.Visible || emptyState.Text != "Sin registros" || chart.Series.Any())
            {
                errors.Add("AnalyticsForm Categorías: el escenario vacío no conserva un estado 'Sin registros' coherente.");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"AnalyticsForm Categorías estado vacío: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static bool HasGridHeaders(DataGridView grid, params string[] headers)
    {
        var actual = grid.Columns
            .Cast<DataGridViewColumn>()
            .Select(column => column.HeaderText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return headers.All(header => actual.Contains(header));
    }

    private static void ValidateCommercialGridScroll(
        string formName,
        DataGridView grid,
        List<string> errors,
        string? expectedLastHeader = null)
    {
        var visibleWidth = grid.DisplayRectangle.Width;
        var contentWidth = grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible);
        if (visibleWidth <= 0 || grid.Columns.Count == 0)
            return;

        if (contentWidth > visibleWidth + 2 && (grid.ScrollBars & ScrollBars.Horizontal) == 0)
        {
            errors.Add($"{formName}: el grid requiere desplazamiento horizontal pero no lo habilita.");
            return;
        }

        if (grid.Rows.Count == 0)
            return;

        var last = grid.Columns
            .Cast<DataGridViewColumn>()
            .Where(column => column.Visible)
            .LastOrDefault();
        if (last is null)
            return;

        foreach (var column in grid.Columns.Cast<DataGridViewColumn>().Where(column => column.Visible))
        {
            var headerFont = column.HeaderCell.InheritedStyle.Font ??
                grid.ColumnHeadersDefaultCellStyle.Font ??
                grid.Font;
            var measuredHeader = TextRenderer.MeasureText(
                column.HeaderText,
                headerFont,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (column.Width < measuredHeader.Width + 16)
            {
                errors.Add(
                    $"{formName}: el encabezado '{column.HeaderText}' no cabe " +
                    $"({column.Width}px, requiere {measuredHeader.Width + 16}px).");
            }
        }

        if (!string.IsNullOrWhiteSpace(expectedLastHeader))
        {
            last = grid.Columns
                .Cast<DataGridViewColumn>()
                .FirstOrDefault(column => column.Visible &&
                    string.Equals(column.HeaderText, expectedLastHeader, StringComparison.OrdinalIgnoreCase));
            if (last is null)
            {
                errors.Add($"{formName}: no se encontró la columna final '{expectedLastHeader}'.");
                return;
            }
        }

        try
        {
            grid.FirstDisplayedScrollingColumnIndex = last.Index;
            System.Windows.Forms.Application.DoEvents();
            grid.PerformLayout();
            var viewport = grid.DisplayRectangle;
            var header = grid.GetColumnDisplayRectangle(last.Index, true);
            if (header.Width <= 0 || header.Left < viewport.Left || header.Right > viewport.Right + 1)
            {
                errors.Add(
                    $"{formName}: no se pudo mostrar completo el encabezado '{last.HeaderText}' " +
                    $"({header}, viewport={viewport}).");
            }

            var cell = grid.Rows.Count > 0
                ? grid.GetCellDisplayRectangle(last.Index, 0, true)
                : Rectangle.Empty;
            if (grid.Rows.Count > 0 &&
                (cell.Width <= 0 || cell.Right > viewport.Right + 1 || cell.Left < viewport.Left - 1))
            {
                errors.Add($"{formName}: no se pudo alcanzar la ultima columna '{last.HeaderText}'.");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"{formName}: desplazamiento a la ultima columna fallo ({ex.GetType().Name}).");
        }
    }

    private static void CaptureAnalyticsCategoriesContentSnapshot(Form form, List<string> errors)
    {
        var sectionHost = GetDescendants(form)
            .OfType<Panel>()
            .FirstOrDefault(panel => panel.Tag is int);
        var subcategoryGrid = GetDescendants(form)
            .OfType<DataGridView>()
            .FirstOrDefault(grid => grid.Name == "AnalyticsSubcategoriesGrid");
        if (sectionHost is null || subcategoryGrid is null)
        {
            errors.Add("AnalyticsForm Categorías: no se encontró el host desplazable o la tabla de subcategorías para la captura de contenido.");
            return;
        }

        try
        {
            sectionHost.AutoScrollPosition = Point.Empty;
            PumpUi(TimeSpan.FromMilliseconds(100));
            PerformLayoutRecursive(form);
            var targetBounds = sectionHost.RectangleToClient(
                subcategoryGrid.RectangleToScreen(subcategoryGrid.ClientRectangle));
            var scrollOffset = Math.Max(0, targetBounds.Bottom - sectionHost.ClientSize.Height + 12);
            sectionHost.AutoScrollPosition = new Point(0, scrollOffset);
            PumpUi(TimeSpan.FromMilliseconds(200));
            CaptureAnalyticsSnapshot(form, "Categorías-contenido", errors, preserveScroll: true);

            if (subcategoryGrid.Rows.Count > 0)
            {
                try
                {
                    subcategoryGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, subcategoryGrid.Rows.Count - 2);
                    PumpUi(TimeSpan.FromMilliseconds(120));
                    CaptureAnalyticsSnapshot(
                        form,
                        "Categorías-subcategorías-scrolled",
                        errors,
                        preserveScroll: true);
                }
                catch (InvalidOperationException)
                {
                    // La tabla puede no requerir desplazamiento vertical.
                }
            }

            var trendChart = GetDescendants(form)
                .OfType<CartesianChart>()
                .FirstOrDefault(chart => chart.Name == "AnalyticsCategoryTrendChart");
            if (trendChart is not null)
            {
                sectionHost.AutoScrollPosition = Point.Empty;
                PumpUi(TimeSpan.FromMilliseconds(100));
                PerformLayoutRecursive(form);
                var trendBounds = sectionHost.RectangleToClient(
                    trendChart.RectangleToScreen(trendChart.ClientRectangle));
                var trendOffset = Math.Max(0, trendBounds.Bottom - sectionHost.ClientSize.Height + 12);
                sectionHost.AutoScrollPosition = new Point(0, trendOffset);
                PumpUi(TimeSpan.FromMilliseconds(180));
                CaptureAnalyticsSnapshot(form, "Categorías-tendencia", errors, preserveScroll: true);
            }
        }
        catch (Exception ex)
        {
            errors.Add(
                $"AnalyticsForm Categorías: no se pudo capturar el contenido desplazado: {ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
            sectionHost.AutoScrollPosition = Point.Empty;
            PumpUi(TimeSpan.FromMilliseconds(100));
            PerformLayoutRecursive(form);
        }
    }

    private static void ValidateCommercialActions(
        string formName,
        Form form,
        IReadOnlyList<string> buttonTexts,
        List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        foreach (var text in buttonTexts)
        {
            var button = descendants
                .OfType<Button>()
                .FirstOrDefault(control => control.Visible &&
                    (control.Text == text ||
                     (text == "X" && control.Text == "×") ||
                     (text == "×" && control.Text == "X") ||
                     ((text == "X" || text == "×") &&
                      (control.Name.Contains("Clear", StringComparison.OrdinalIgnoreCase) ||
                       control.AccessibleName?.Contains("Limpiar", StringComparison.OrdinalIgnoreCase) == true))));
            if (button is null)
            {
                errors.Add($"{formName}: no se encontró el botón '{text}'.");
                continue;
            }

            var parent = button.Parent;
            if (parent is null || !IsInsideAncestor(button, parent))
            {
                errors.Add(
                    $"{formName}: botón '{text}' fuera de su contenedor inmediato " +
                    $"({(parent is null ? "sin padre" : BoundsInAncestor(button, parent).ToString())}, " +
                    $"parent={parent?.ClientSize}).");
            }

            if (!IsInsideAncestor(button, form))
            {
                errors.Add(
                    $"{formName}: botón '{text}' fuera del área cliente del formulario " +
                    $"({BoundsInAncestor(button, form)}, client={form.ClientSize}).");
            }

            var measured = TextRenderer.MeasureText(
                button.Text,
                button.Font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (button.ClientSize.Width < measured.Width + 16 ||
                button.ClientSize.Height < measured.Height + 8)
            {
                errors.Add(
                    $"{formName}: botón '{text}' no contiene su texto " +
                    $"({button.ClientSize}, requiere {measured.Width + 16}x{measured.Height + 8}).");
            }
        }

        var criticalButtons = descendants
            .OfType<Button>()
            .Where(button => button.Visible && buttonTexts.Any(text =>
                button.Text == text ||
                (text == "X" && button.Text == "×") ||
                (text == "×" && button.Text == "X") ||
                ((text == "X" || text == "×") &&
                 (button.Name.Contains("Clear", StringComparison.OrdinalIgnoreCase) ||
                  button.AccessibleName?.Contains("Limpiar", StringComparison.OrdinalIgnoreCase) == true))))
            .ToList();
        for (var leftIndex = 0; leftIndex < criticalButtons.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < criticalButtons.Count; rightIndex++)
            {
                var left = criticalButtons[leftIndex];
                var right = criticalButtons[rightIndex];
                if (!ReferenceEquals(left.Parent, right.Parent))
                    continue;
                if (left.Bounds.IntersectsWith(right.Bounds))
                {
                    errors.Add(
                        $"{formName}: botones '{left.Text}' y '{right.Text}' se solapan " +
                        $"en {left.Parent?.GetType().Name}.");
                }
            }
        }
    }

    private static void SaveCommercialSelectorScreenshot(Control control, string name)
    {
        var outputDirectory = Environment.GetEnvironmentVariable("ARTE_COMMERCIAL_SUBCATEGORIES_SMOKE_DIR");
        if (string.IsNullOrWhiteSpace(outputDirectory)) return;
        var fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);
        var fullPath = Path.Combine(fullDirectory, $"{name}.png");
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void PrepareCommercialSmokeForm(Form form, Size clientSize)
    {
        form.AutoScaleMode = AutoScaleMode.None;
        var configuredSize = Environment.GetEnvironmentVariable("ARTE_COMMERCIAL_SUBCATEGORIES_SMOKE_SIZE");
        if (!string.IsNullOrWhiteSpace(configuredSize))
        {
            var parts = configuredSize.Split('x', 'X');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var width) &&
                int.TryParse(parts[1], out var height) &&
                width > 0 && height > 0)
            {
                clientSize = new Size(width, height);
            }
        }
        var scale = 1f;
        var configuredScale = Environment.GetEnvironmentVariable("ARTE_COMMERCIAL_SUBCATEGORIES_SMOKE_SCALE");
        if (float.TryParse(
                configuredScale,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedScale) && parsedScale is >= 1f and <= 2f)
        {
            scale = parsedScale;
        }

        if (scale != 1f)
            form.Scale(new SizeF(scale, scale));
        form.ClientSize = clientSize;
    }

    private static void ValidateAccountPaymentForm(Form form, List<string> errors)
    {
        var observations = GetDescendants(form)
            .OfType<TextBox>()
            .FirstOrDefault(textBox => textBox.PlaceholderText.Contains("Observaciones", StringComparison.OrdinalIgnoreCase));
        if (observations is null)
        {
            errors.Add("AccountPaymentForm: no se encontro el campo Observaciones.");
            return;
        }

        if (!observations.Multiline || observations.Height < 110)
        {
            errors.Add($"AccountPaymentForm: Observaciones no tiene altura suficiente. Height={observations.Height}, Multiline={observations.Multiline}.");
        }
    }

    private static void ValidateCreditAlertsForm(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        foreach (var text in new[] { "Usar fecha desde", "Usar fecha hasta" })
        {
            var checkBox = descendants
                .OfType<CheckBox>()
                .FirstOrDefault(control => control.Text == text);
            if (checkBox is null)
            {
                errors.Add($"CreditAlertsForm: no se encontro '{text}'.");
                continue;
            }

            if (checkBox.Width < checkBox.PreferredSize.Width)
            {
                errors.Add($"CreditAlertsForm: '{text}' esta comprimido. Width={checkBox.Width}, Preferred={checkBox.PreferredSize.Width}.");
            }
        }

        foreach (var picker in descendants.OfType<DateTimePicker>())
        {
            if (picker.Width < 140)
            {
                errors.Add($"CreditAlertsForm: fecha comprimida. Width={picker.Width}.");
            }
        }

        foreach (var text in new[] { "Abrir cliente", "Abrir cuenta", "Registrar abono", "Actualizar", "Cerrar" })
        {
            var button = descendants
                .OfType<Button>()
                .FirstOrDefault(control => control.Text == text);
            if (button is null)
            {
                errors.Add($"CreditAlertsForm: no se encontro el boton '{text}'.");
                continue;
            }

            if (button.Width < button.PreferredSize.Width)
            {
                errors.Add($"CreditAlertsForm: boton '{text}' comprimido. Width={button.Width}, Preferred={button.PreferredSize.Width}.");
            }
        }
    }

    private static void ValidateAccountPaymentsHistoryForm(Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        foreach (var text in new[] { "Historial de abonos", "Consulta y reapertura de comprobantes internos" })
        {
            var label = descendants
                .OfType<Label>()
                .FirstOrDefault(control => control.Text == text);
            if (label is null)
            {
                errors.Add($"AccountPaymentsHistoryForm: no se encontro el texto '{text}'.");
                continue;
            }

            if (label.Height < label.PreferredHeight)
            {
                errors.Add(
                    $"AccountPaymentsHistoryForm: el texto '{text}' esta comprimido verticalmente. " +
                    $"Height={label.Height}, Preferred={label.PreferredHeight}.");
            }
        }

        var datePickers = descendants.OfType<DateTimePicker>().ToList();
        if (datePickers.Count != 2)
        {
            errors.Add($"AccountPaymentsHistoryForm: se esperaban 2 selectores de fecha y se encontraron {datePickers.Count}.");
        }

        foreach (var picker in datePickers)
        {
            if (picker.Width < 145)
            {
                errors.Add($"AccountPaymentsHistoryForm: fecha comprimida. Width={picker.Width}.");
            }

            if (picker.CustomFormat != "dd/MM/yyyy")
            {
                errors.Add($"AccountPaymentsHistoryForm: formato de fecha inesperado '{picker.CustomFormat}'.");
            }
        }

        var useDates = descendants
            .OfType<CheckBox>()
            .FirstOrDefault(control => control.Text == "Usar fechas");
        if (useDates is null)
        {
            errors.Add("AccountPaymentsHistoryForm: no se encontro 'Usar fechas'.");
        }
        else
        {
            if (useDates.Width < useDates.PreferredSize.Width)
            {
                errors.Add($"AccountPaymentsHistoryForm: 'Usar fechas' esta comprimido. Width={useDates.Width}, Preferred={useDates.PreferredSize.Width}.");
            }

            if (useDates.Height < useDates.PreferredSize.Height)
            {
                errors.Add(
                    $"AccountPaymentsHistoryForm: 'Usar fechas' esta comprimido verticalmente. " +
                    $"Height={useDates.Height}, Preferred={useDates.PreferredSize.Height}.");
            }

            if (datePickers.Any(picker => picker.Enabled))
            {
                errors.Add("AccountPaymentsHistoryForm: las fechas deben iniciar deshabilitadas.");
            }

            useDates.Checked = true;
            System.Windows.Forms.Application.DoEvents();
            if (datePickers.Any(picker => !picker.Enabled))
            {
                errors.Add("AccountPaymentsHistoryForm: activar 'Usar fechas' no habilito ambos selectores.");
            }

            useDates.Checked = false;
        }

        foreach (var text in new[] { "Buscar", "Limpiar", "Ver comprobante", "Cerrar" })
        {
            var button = descendants
                .OfType<Button>()
                .FirstOrDefault(control => control.Text == text);
            if (button is null)
            {
                errors.Add($"AccountPaymentsHistoryForm: no se encontro el boton '{text}'.");
                continue;
            }

            if (button.Width < button.PreferredSize.Width)
            {
                errors.Add($"AccountPaymentsHistoryForm: boton '{text}' comprimido. Width={button.Width}, Preferred={button.PreferredSize.Width}.");
            }

            if (button.Height < button.PreferredSize.Height)
            {
                errors.Add(
                    $"AccountPaymentsHistoryForm: boton '{text}' comprimido verticalmente. " +
                    $"Height={button.Height}, Preferred={button.PreferredSize.Height}.");
            }

            if (button.TextAlign != ContentAlignment.MiddleCenter)
            {
                errors.Add($"AccountPaymentsHistoryForm: boton '{text}' no esta centrado.");
            }
        }

        var emptyState = descendants
            .OfType<Label>()
            .FirstOrDefault(control => control.Text == "No se encontraron abonos con los filtros seleccionados.");
        if (emptyState is null)
        {
            errors.Add("AccountPaymentsHistoryForm: no se encontro el estado vacio.");
        }

        var grid = descendants.OfType<DataGridView>().FirstOrDefault();
        if (grid is null)
        {
            errors.Add("AccountPaymentsHistoryForm: no se encontro la tabla.");
            return;
        }

        var expectedColumns = new[]
        {
            "Fecha", "Codigo", "Cliente", "Cuenta", "Venta", "Metodo",
            "Monto", "Interes", "Capital", "SaldoNuevo", "Usuario"
        };
        foreach (var property in expectedColumns)
        {
            if (!grid.Columns.Cast<DataGridViewColumn>().Any(column => column.DataPropertyName == property))
            {
                errors.Add($"AccountPaymentsHistoryForm: falta la columna '{property}'.");
            }
        }

        foreach (var property in new[] { "Monto", "Interes", "Capital", "SaldoNuevo" })
        {
            var column = grid.Columns
                .Cast<DataGridViewColumn>()
                .FirstOrDefault(item => item.DataPropertyName == property);
            if (column is not null &&
                (column.DefaultCellStyle.Alignment != DataGridViewContentAlignment.MiddleRight ||
                 column.HeaderCell.Style.Alignment != DataGridViewContentAlignment.MiddleCenter))
            {
                errors.Add($"AccountPaymentsHistoryForm: alineacion financiera incorrecta en '{property}'.");
            }
        }

        if (grid.DisplayRectangle.Width > 0 &&
            grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible) > grid.DisplayRectangle.Width + 2)
        {
            errors.Add(
                $"AccountPaymentsHistoryForm: las columnas requieren scroll horizontal. " +
                $"Columns={grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible)}, Display={grid.DisplayRectangle.Width}.");
        }
    }

    private static void SmokeCreditAlertsRepeatedResize(List<string> errors)
    {
        try
        {
            using var form = new CreditAlertsLayoutSmokeForm
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false
            };
            form.Show();
            foreach (var size in CreditAlertSmokeSizes
                .Concat(CreditAlertSmokeSizes.Reverse())
                .Concat(CreditAlertSmokeSizes))
            {
                form.ClientSize = size;
                System.Windows.Forms.Application.DoEvents();
                PerformLayoutRecursive(form);
                ValidateCreditAlertsForm(form, errors);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"CreditAlertsForm redimensionamiento repetido: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void SmokeAccountPaymentsHistoryRepeatedResize(List<string> errors)
    {
        try
        {
            using var form = new AccountPaymentsHistoryLayoutSmokeForm
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false
            };
            form.Show();
            foreach (var size in PaymentHistorySmokeSizes
                .Concat(PaymentHistorySmokeSizes.Reverse())
                .Concat(PaymentHistorySmokeSizes))
            {
                form.ClientSize = size;
                System.Windows.Forms.Application.DoEvents();
                PerformLayoutRecursive(form);
                ValidateAccountPaymentsHistoryForm(form, errors);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"AccountPaymentsHistoryForm redimensionamiento repetido: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void SmokeSupplierProductAssociationRepeatedResize(List<string> errors)
    {
        try
        {
            using var form = new SupplierProductAssociationForm(1, new SmokeSupplierService(), new SmokeProductService())
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false
            };
            form.Show();
            foreach (var size in SupplierAssociationSmokeSizes
                .Concat(SupplierAssociationSmokeSizes.Reverse())
                .Concat(SupplierAssociationSmokeSizes))
            {
                form.ClientSize = size;
                System.Windows.Forms.Application.DoEvents();
                PerformLayoutRecursive(form);
                ValidateSupplierForms("SupplierProductAssociationForm", form, errors);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"SupplierProductAssociationForm redimensionamiento repetido: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void SmokeSupplierProductSelectors(List<string> errors)
    {
        try
        {
            using var form = new SupplierProductAssociationForm(
                1,
                new SmokeSupplierService(),
                new SmokeProductService())
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false,
                ClientSize = new Size(980, 680)
            };
            PrepareCommercialSmokeForm(form, new Size(980, 680));
            form.Show();
            PumpUi(TimeSpan.FromMilliseconds(80));
            PerformLayoutRecursive(form);

            ValidateCommercialActions(
                "SupplierProductAssociationForm",
                form,
                ["Buscar", "Limpiar", "Asociar", "Cancelar"],
                errors);

            var descendants = GetDescendants(form).ToList();
            var grid = descendants.OfType<DataGridView>().Single();
            var search = descendants.OfType<TextBox>().First();
            var searchButton = descendants.OfType<Button>().Single(button => button.Text == "Buscar");
            if (grid.Rows.Count != 1 || !HasGridHeaders(grid, "Código", "Categoría", "Subcategoría") ||
                grid.Rows[0].Cells[3].Value?.ToString() != "Jarrones")
            {
                errors.Add("SupplierProductAssociationForm: no mostro la subcategoria del producto inicial.");
            }
            ValidateCommercialGridScroll("SupplierProductAssociationForm", grid, errors);

            search.Text = "Jarrones";
            searchButton.PerformClick();
            PumpUi(TimeSpan.FromMilliseconds(40));
            if (grid.Rows.Count != 1 || grid.Rows[0].Cells[3].Value?.ToString() != "Jarrones")
                errors.Add("SupplierProductAssociationForm: la busqueda por subcategoria no devolvio el producto esperado.");
            SaveCommercialSelectorScreenshot(form, "supplier-product-association");
        }
        catch (Exception ex)
        {
            errors.Add($"SupplierProductAssociationForm subcategoria: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void SmokeSupplierProfileProducts(List<string> errors)
    {
        try
        {
            var permissions = new SmokePermissionService(
                new HashSet<PermisoEmpleado> { PermisoEmpleado.PROVEEDORES_ACCEDER });
            using var form = new SupplierProfileForm(
                1,
                new SmokeSupplierQueryService(),
                null!,
                permissions,
                null!,
                null!,
                null!)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false,
                ClientSize = new Size(1160, 740)
            };
            PrepareCommercialSmokeForm(form, new Size(1160, 740));
            form.Show();
            PumpUi(TimeSpan.FromMilliseconds(100));
            PerformLayoutRecursive(form);

            var grid = GetDescendants(form).OfType<DataGridView>().First();
            if (grid.Rows.Count != 1 || !HasGridHeaders(grid, "Código", "Categoría", "Subcategoría"))
            {
                errors.Add("SupplierProfileForm: no mostro las columnas de categoria y subcategoria.");
            }
            else if (grid.Rows[0].Cells.Cast<DataGridViewCell>().FirstOrDefault(cell =>
                         grid.Columns[cell.ColumnIndex].HeaderText == "Subcategoría")?.Value?.ToString() != "Jarrones")
            {
                errors.Add("SupplierProfileForm: la fila asociada no mostro la subcategoria esperada.");
            }
            ValidateCommercialGridScroll("SupplierProfileForm", grid, errors);

            SaveCommercialSelectorScreenshot(form, "supplier-profile");
        }
        catch (Exception ex)
        {
            errors.Add($"SupplierProfileForm subcategoria: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void SmokeCustomOrderProductMapping(List<string> errors)
    {
        try
        {
            using var form = new CustomOrderProductMappingForm(
                BuildCustomOrderConversionPreview().Lines,
                new SmokeCustomOrderFinanceService(),
                new SmokePosCatalogService())
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false,
                ClientSize = new Size(900, 560)
            };
            PrepareCommercialSmokeForm(form, new Size(900, 560));
            form.Show();
            PumpUi(TimeSpan.FromMilliseconds(100));
            PerformLayoutRecursive(form);

            ValidateCommercialActions(
                "CustomOrderProductMappingForm",
                form,
                ["Cerrar", "Vincular seleccionado"],
                errors);

            var combo = GetDescendants(form).OfType<ComboBox>().Single();
            if (combo.SelectedItem is not PosSkuSaleItem selected ||
                selected.ProductoId != 1 || selected.ProductoSkuId != 7 ||
                !combo.Text.Contains("Artesanías", StringComparison.Ordinal) ||
                !combo.Text.Contains("Jarrones", StringComparison.Ordinal))
            {
                errors.Add("CustomOrderProductMappingForm: no conservo IDs o clasificacion del SKU seleccionado.");
            }

            SaveCommercialSelectorScreenshot(form, "custom-order-product-mapping");
        }
        catch (Exception ex)
        {
            errors.Add($"CustomOrderProductMappingForm subcategoria: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void PerformLayoutRecursive(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls)
        {
            PerformLayoutRecursive(child);
        }
    }

    private static void PumpUi(TimeSpan duration)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(10);
        }
        System.Windows.Forms.Application.DoEvents();
    }

    private static IEnumerable<Control> GetDescendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in GetDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static QuotationDetail BuildQuotationDetail() => new(
        1,
        "COT-000001",
        null,
        null,
        "Destinatario de prueba",
        "7000-0000",
        "cliente@example.com",
        "Direccion de prueba",
        DateTime.Today,
        DateTime.Today.AddDays(15),
        EstadoCotizacion.BORRADOR,
        2000,
        0,
        2000,
        "Valida por quince dias.",
        "Documento de smoke visual.",
        "admin",
        "admin",
        DateTime.UtcNow,
        DateTime.UtcNow,
        null,
        null,
        null,
        [
            new QuotationLineDetail(1, null, null, "Producto descriptivo", 2, 1000, 0, 2000, null)
        ],
        [
            new QuotationStatusHistoryRow(1, null, EstadoCotizacion.BORRADOR, "Borrador creado.", "admin", DateTime.UtcNow)
        ]);

    private static IReadOnlyList<QuotationProductOption> BuildQuotationProducts() =>
    [
        new(null, "(Linea descriptiva)", null, null, 0),
        new(
            1,
            "PRD-SMOKE-001 - Producto artesanal con nombre extenso",
            "PRD-SMOKE-001",
            "Producto artesanal con nombre extenso",
            150,
            "Artesanías",
            "Jarrones")
    ];

    private static IReadOnlyList<PurchaseProductOption> BuildPurchaseProducts() =>
    [
        new(1, "PRD-000364", "reze", "Barro negro", 0, 100, Subcategory: "Jarrones"),
        new(2, "ART-0168", "Alcancía pequeña", "Alcancías", 12, 250),
        new(3, "ART-0319", "Anillos enchapados", "Joyería", 5, 375, Subcategory: "Anillos")
    ];

    private static CustomOrderDetail BuildCustomOrderDetail() => new(
        1,
        "PED-000001",
        1,
        "COT-000001",
        null,
        null,
        "Destinatario de prueba",
        "7000-0000",
        "cliente@example.com",
        "Direccion de prueba",
        EstadoPedidoPersonalizado.CONFIRMADO,
        PrioridadPedido.ALTA,
        DateTime.Today,
        DateTime.Today.AddDays(15),
        null,
        null,
        null,
        2000,
        0,
        2000,
        "Pedido de prueba visual.",
        "Acabado artesanal especial.",
        "Observacion interna.",
        "Notas para el cliente.",
        "admin",
        "admin",
        DateTime.UtcNow,
        DateTime.UtcNow,
        null,
        null,
        [
            new CustomOrderLineDetail(
                1, null, null, "Producto descriptivo", 2, 1000, 0, 2000,
                "Acabado fino", "Rosa", "20 cm", "Madera", "Texto", "Sin observaciones")
        ],
        [
            new CustomOrderStatusHistoryRow(
                1, null, EstadoPedidoPersonalizado.CONFIRMADO,
                "Pedido confirmado.", "admin", DateTime.UtcNow)
        ]);

    private static CustomOrderFinanceSummary BuildCustomOrderFinanceSummary() => new(
        1, "PED-000001", "Destinatario de prueba",
        EstadoFinancieroPedido.PARCIAL, 2000, 500, 0, 500, 1500, null, null);

    private static CustomOrderConversionPreview BuildCustomOrderConversionPreview() => new(
        BuildCustomOrderFinanceSummary(),
        EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA,
        1,
        true,
        true,
        [
            new CustomOrderConversionLine(
                1, 1, "PRD-SMOKE-001", "Producto de prueba",
                1, 2000, 0, 2000, 10, 1, 0, 10),
            new CustomOrderConversionLine(
                2, null, null, "Linea descriptiva pendiente",
                1, 500, 0, 500, 0, 0, 0, 0)
        ],
        [
            new CustomOrderReservationResult(
                1, 1, 1, "PRD-SMOKE-001", "Producto de prueba",
                1, 0, 0, EstadoReservaInventarioPedido.ACTIVA, DateTime.UtcNow)
        ],
        1);

    private static AccountPaymentReceiptViewModel BuildReceipt()
    {
        return new AccountPaymentReceiptViewModel(
            1,
            "ABN-SMOKE-0001",
            DateTime.UtcNow,
            "CLI-SMOKE - Cliente de prueba visual",
            "CXC-SMOKE-0001",
            "VTA-SMOKE-0001",
            "usuario",
            MetodoPago.EFECTIVO,
            null,
            1500,
            500,
            100,
            400,
            1000,
            EstadoCuentaPorCobrar.ABIERTA,
            DateTime.UtcNow.AddDays(7),
            SituacionCrediticia.SALDO_VIGENTE,
            "Observacion de prueba visual");
    }

    private static PurchaseDetail BuildPurchaseDetail()
    {
        return new PurchaseDetail(
            1,
            "CMP-000001",
            1,
            "PRV-000001",
            "Proveedor de prueba",
            DateTime.Today,
            DateTime.UtcNow,
            EstadoRecepcionCompra.PARCIAL,
            1500,
            0,
            1500,
            "Compra de prueba",
            "admin",
            [
                new PurchaseLineSummary(
                    1,
                    1,
                    "PRD-000001",
                    "Producto de prueba",
                    10,
                    4,
                    6,
                    150,
                    0,
                    1500,
                    "Linea de prueba")
            ],
            [
                new PurchaseReceptionSummary(
                    1,
                    "REC-000001",
                    Guid.NewGuid(),
                    1,
                    "CMP-000001",
                    DateTime.UtcNow,
                    "admin",
                    "Recepcion parcial",
                    [
                        new PurchaseReceptionLineSummary(
                            1,
                            1,
                            1,
                            "PRD-000001",
                            "Producto de prueba",
                            4,
                            10,
                            14,
                            150,
                            100,
                            114,
                            1)
                    ])
            ],
            [
                new CostHistoryRow(
                    1,
                    DateTime.UtcNow,
                    1,
                    "PRD-000001",
                    "Producto de prueba",
                    1,
                    "Proveedor de prueba",
                    1,
                    "CMP-000001",
                    100,
                    150,
                    114,
                    10,
                    4,
                    14,
                    "admin",
                    "Costo de prueba")
            ],
            EstadoFinancieroCompra.PENDIENTE,
            0,
            1500,
            null);
    }

    private static AccountPayableDetail BuildPayableDetail()
    {
        var summary = new AccountPayableSummary(
            1, "CPP-000001", 1, "CMP-000001", 1, "PRV-000001", "Proveedor de prueba",
            DateTime.Today, DateTime.Today.AddDays(30), 1500, 500, 1000,
            EstadoCuentaPorPagar.PARCIAL, "admin", "Cuenta de prueba visual");
        return new AccountPayableDetail(summary, EstadoFinancieroCompra.PARCIAL, [], [BuildSupplierPayment()]);
    }

    private static SupplierPaymentSummary BuildSupplierPayment() => new(
        1, "ABP-000001", Guid.NewGuid(), 1, "CPP-000001", 1, "CMP-000001", 1,
        "PRV-000001", "Proveedor de prueba", "admin", 1, MetodoPago.EFECTIVO,
        500, 1500, 1000, null, "Abono de prueba visual", DateTime.UtcNow);

    private static SupplierDuplicateCheckResult BuildSupplierDuplicateResult()
    {
        return new SupplierDuplicateCheckResult(
        [
            new SupplierDuplicateCandidate(
                1,
                "PRV-000001",
                "Proveedor de prueba",
                "7777-0000",
                "7777-0000",
                "proveedor@example.com",
                "01234567-8",
                "0614-000000-001-1",
                EstadoProveedor.ACTIVO,
                SupplierDuplicateKind.NombreParecido,
                Blocking: false)
        ]);
    }

    private static void ValidateSupplierForms(string name, Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        foreach (var button in descendants.OfType<Button>())
        {
            if (!string.IsNullOrWhiteSpace(button.Text) &&
                button.Width < button.PreferredSize.Width)
            {
                errors.Add($"{name}: boton '{button.Text}' comprimido. Width={button.Width}, Preferred={button.PreferredSize.Width}.");
            }
        }

        if (name == "SuppliersForm")
        {
            foreach (var text in new[] { "Nuevo", "Ver perfil", "Editar", "Desactivar" })
            {
                if (!descendants.OfType<Button>().Any(control => control.Text == text))
                {
                    errors.Add($"SuppliersForm: no se encontro el boton '{text}'.");
                }
            }
        }

        if (name == "SupplierProfileForm")
        {
            foreach (var text in new[] { "Asociar producto", "Marcar principal", "Desasociar producto", "Editar", "Cerrar" })
            {
                if (!descendants.OfType<Button>().Any(control => control.Text == text))
                {
                    errors.Add($"SupplierProfileForm: no se encontro el boton '{text}'.");
                }
            }
        }

        if (name == "SupplierPaymentForm")
        {
            ValidateSummaryValues(name, descendants, 4, errors);
            ValidateButtonByText(name, descendants, "Pagar total", errors);
            var fullPayment = descendants.OfType<Button>().FirstOrDefault(control => control.Text == "Pagar total");
            var amount = descendants.OfType<NumericUpDown>().FirstOrDefault(control => control.Name == "PaymentAmount");
            fullPayment?.PerformClick();
            if (amount?.Value != 10m)
            {
                errors.Add($"SupplierPaymentForm: Pagar total no lleno el saldo esperado. Value={amount?.Value}.");
            }

            var method = descendants.OfType<ComboBox>().FirstOrDefault(control => control.Name == "PaymentMethod");
            var reference = descendants.OfType<TextBox>().FirstOrDefault(control => control.Name == "PaymentReference");
            if (method is null || reference is null)
            {
                errors.Add("SupplierPaymentForm: no se encontraron los controles de metodo y referencia.");
            }
            else
            {
                method.SelectedIndex = 0;
                if (reference.Enabled) errors.Add("SupplierPaymentForm: Efectivo debe deshabilitar Referencia.");
                method.SelectedIndex = 1;
                if (!reference.Enabled) errors.Add("SupplierPaymentForm: Transferencia debe habilitar Referencia.");
                reference.Text = "SMOKE";
                method.SelectedIndex = 0;
                if (reference.Text.Length > 0) errors.Add("SupplierPaymentForm: Efectivo debe limpiar Referencia.");
                method.SelectedIndex = 2;
                if (!reference.Enabled) errors.Add("SupplierPaymentForm: Bitcoin debe habilitar Referencia.");
            }
        }

        if (name == "SupplierPaymentsHistoryForm")
        {
            var grid = descendants.OfType<DataGridView>().FirstOrDefault();
            var expected = new[]
            {
                "Fecha", "Codigo", "Tipo", "Proveedor", "Compra", "Cuenta", "Metodo",
                "Referencia", "Monto", "SaldoAnterior", "SaldoNuevo", "Usuario"
            };
            foreach (var property in expected)
            {
                if (grid is null || !grid.Columns.Cast<DataGridViewColumn>().Any(column => column.DataPropertyName == property))
                {
                    errors.Add($"SupplierPaymentsHistoryForm: falta la columna '{property}'.");
                }
            }

            ValidateButtonByText(name, descendants, "Ver comprobante", errors);
        }

        if (name == "SupplierProductAssociationForm")
        {
            var notes = descendants
                .OfType<TextBox>()
                .FirstOrDefault(control => control.Multiline && control.ScrollBars == ScrollBars.Vertical);
            if (notes is null)
            {
                errors.Add("SupplierProductAssociationForm: no se encontro el campo Notas multilinea.");
            }
            else if (notes.Height < 70)
            {
                errors.Add($"SupplierProductAssociationForm: Notas comprimidas. Height={notes.Height}.");
            }

            var mainCheckBox = descendants
                .OfType<CheckBox>()
                .FirstOrDefault(control => control.Text == "Marcar como proveedor principal del producto");
            if (mainCheckBox is null)
            {
                errors.Add("SupplierProductAssociationForm: no se encontro el checkbox de proveedor principal.");
            }
            else if (mainCheckBox.Width < mainCheckBox.PreferredSize.Width)
            {
                errors.Add(
                    $"SupplierProductAssociationForm: checkbox comprimido. " +
                    $"Width={mainCheckBox.Width}, Preferred={mainCheckBox.PreferredSize.Width}.");
            }

            var grid = descendants.OfType<DataGridView>().FirstOrDefault();
            if (grid is not null &&
                grid.DisplayRectangle.Width > 0 &&
                grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible) > grid.DisplayRectangle.Width + 2)
            {
                if ((grid.ScrollBars & ScrollBars.Horizontal) == 0)
                {
                    errors.Add(
                        $"SupplierProductAssociationForm: las columnas requieren scroll horizontal pero esta deshabilitado. " +
                        $"Columns={grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible)}, Display={grid.DisplayRectangle.Width}.");
                }
            }
        }
    }

    private static void ValidatePurchaseForms(string name, Form form, List<string> errors)
    {
        var descendants = GetDescendants(form).ToList();
        if (name == "PurchasesForm")
        {
            foreach (var text in new[] { "Buscar", "Limpiar" })
            {
                ValidateButton(descendants, name, text, 110, 36, errors);
            }

            foreach (var text in new[]
                     {
                         "Anterior",
                         "Siguiente",
                         "Nueva compra",
                         "Editar",
                         "Ver detalle",
                         "Registrar recepcion",
                         "Cerrar"
                     })
            {
                ValidateButton(descendants, name, text, text == "Registrar recepcion" ? 150 : 90, 36, errors);
            }

            var useDates = descendants
                .OfType<CheckBox>()
                .FirstOrDefault(control => control.Text == "Usar fechas");
            if (useDates is null)
            {
                errors.Add("PurchasesForm: no se encontro 'Usar fechas'.");
            }
            else if (useDates.Width < useDates.PreferredSize.Width || useDates.Height < useDates.PreferredSize.Height)
            {
                errors.Add(
                    $"PurchasesForm: 'Usar fechas' esta comprimido. " +
                    $"Size={useDates.Width}x{useDates.Height}, Preferred={useDates.PreferredSize.Width}x{useDates.PreferredSize.Height}.");
            }

            foreach (var picker in descendants.OfType<DateTimePicker>())
            {
                if (picker.Width < 150)
                {
                    errors.Add($"PurchasesForm: fecha comprimida. Width={picker.Width}.");
                }
            }
        }

        if (name == "PurchasePaymentForm")
        {
            ValidateSummaryValues(name, descendants, 6, errors);
            ValidateButtonByText(name, descendants, "Pagar total", errors);
            var fullPayment = descendants.OfType<Button>().FirstOrDefault(control => control.Text == "Pagar total");
            var cash = descendants.OfType<NumericUpDown>().FirstOrDefault(control => control.Name == "CashAmount");
            fullPayment?.PerformClick();
            if (cash?.Value != 15m)
            {
                errors.Add($"PurchasePaymentForm: Pagar total no lleno el saldo esperado. Value={cash?.Value}.");
            }
            foreach (var controlName in new[] { "TransferReference", "BitcoinReference" })
            {
                var reference = descendants.OfType<TextBox>().FirstOrDefault(control => control.Name == controlName);
                if (reference is null)
                {
                    errors.Add($"PurchasePaymentForm: no se encontro '{controlName}'.");
                }
                else if (reference.Enabled)
                {
                    errors.Add($"PurchasePaymentForm: '{controlName}' debe iniciar deshabilitada sin monto.");
                }
            }
        }

        if (name == "PurchaseDetailForm")
        {
            foreach (var text in new[]
                     {
                         "Estado",
                         "Fecha compra",
                         "Fecha registro",
                         "Usuario",
                         "Subtotal",
                         "Descuento",
                         "Total",
                         "Recepciones",
                         "Observaciones"
                     })
            {
                ValidateLabel(descendants, name, text, errors);
            }
        }

        if (name == "PurchaseReceptionForm")
        {
            foreach (var text in new[] { "Estado", "Comprado", "Recibido", "Pendiente" })
            {
                ValidateLabel(descendants, name, text, errors);
            }
        }
    }

    private static void ValidateButton(
        IReadOnlyList<Control> descendants,
        string formName,
        string text,
        int minimumWidth,
        int minimumHeight,
        List<string> errors)
    {
        var button = descendants
            .OfType<Button>()
            .FirstOrDefault(control => control.Text == text);
        if (button is null)
        {
            errors.Add($"{formName}: no se encontro el boton '{text}'.");
            return;
        }

        var requiredWidth = Math.Max(minimumWidth, button.PreferredSize.Width);
        var requiredHeight = Math.Max(minimumHeight, button.PreferredSize.Height);
        if (button.Width < requiredWidth || button.Height < requiredHeight)
        {
            errors.Add(
                $"{formName}: boton '{text}' comprimido. " +
                $"Size={button.Width}x{button.Height}, Required={requiredWidth}x{requiredHeight}.");
        }
    }

    private static void ValidateLabel(
        IReadOnlyList<Control> descendants,
        string formName,
        string text,
        List<string> errors)
    {
        var label = descendants
            .OfType<Label>()
            .FirstOrDefault(control => control.Text == text);
        if (label is null)
        {
            errors.Add($"{formName}: no se encontro el texto '{text}'.");
            return;
        }

        if (label.Height < Math.Min(label.PreferredHeight, 24))
        {
            errors.Add(
                $"{formName}: texto '{text}' comprimido verticalmente. " +
                $"Height={label.Height}, Preferred={label.PreferredHeight}.");
        }
    }

    private static void ValidateSummaryValues(
        string formName,
        IReadOnlyList<Control> descendants,
        int expectedCount,
        List<string> errors)
    {
        var values = descendants.OfType<Label>()
            .Where(label => label.AccessibleName?.StartsWith("Resumen ", StringComparison.Ordinal) == true)
            .ToList();
        if (values.Count != expectedCount)
        {
            errors.Add($"{formName}: se esperaban {expectedCount} valores de resumen y se encontraron {values.Count}.");
        }

        foreach (var value in values)
        {
            if (value.Height < value.PreferredHeight)
            {
                errors.Add($"{formName}: '{value.AccessibleName}' esta cortado. Height={value.Height}, Preferred={value.PreferredHeight}.");
            }
        }
    }

    private static void ValidateButtonByText(
        string formName,
        IReadOnlyList<Control> descendants,
        string text,
        List<string> errors)
    {
        var button = descendants.OfType<Button>().FirstOrDefault(control => control.Text == text);
        if (button is null)
        {
            errors.Add($"{formName}: no se encontro el boton '{text}'.");
        }
        else if (button.Width < button.PreferredSize.Width || button.Height < button.PreferredSize.Height)
        {
            errors.Add($"{formName}: boton '{text}' comprimido. Size={button.Width}x{button.Height}, Preferred={button.PreferredSize.Width}x{button.PreferredSize.Height}.");
        }
    }

    private static void WriteLog(IReadOnlyList<string> errors)
    {
        var configuredPath = Environment.GetEnvironmentVariable("ARTE_ACCOUNT_PAYMENT_LAYOUT_SMOKE_LOG");
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ArteInventariado",
                "Logs",
                "account-payment-layout-smoke.log")
            : configuredPath;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lines = new List<string>
        {
            $"Account payment layout smoke - {DateTime.Now:O}",
            errors.Count == 0 ? "OK" : "ERRORS"
        };
        lines.AddRange(errors);
        File.WriteAllLines(path, lines);
    }

    private sealed class SmokeUpdateService : IUpdateService
    {
        public Task<UpdateStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new UpdateStatus(
                UpdateState.Idle,
                "1.10.0",
                null,
                null,
                null,
                null,
                null));
        }

        public Task<UpdatePackageValidationResult> ValidatePackageAsync(
            string manifestPath,
            string packagePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new UpdatePackageValidationResult(
                false,
                null,
                packagePath,
                ["Smoke visual: no se valida ningun paquete."],
                null));
        }

        public Task<UpdatePrepareResult> PrepareAsync(
            string manifestPath,
            string packagePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new UpdatePrepareResult(
                false,
                UpdateState.Failed,
                "Smoke visual: no se prepara ninguna actualizacion.",
                null,
                new UpdatePackageValidationResult(
                    false,
                    null,
                    packagePath,
                    ["Smoke visual: no se valida ningun paquete."],
                    null)));
        }

        public string GetUpdaterExecutablePath() => Path.Combine(Path.GetTempPath(), "ArteInventariado.Updater.smoke.exe");
    }

    private sealed class SmokeUpdateStateService(UpdateState state, string? lastError) : IUpdateService
    {
        public Task<UpdateStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new UpdateStatus(
                state,
                "1.14.0",
                state is UpdateState.ReadyToInstall or UpdateState.Installing ? "1.14.1" : null,
                null,
                null,
                null,
                lastError));
        }

        public Task<UpdatePackageValidationResult> ValidatePackageAsync(
            string manifestPath,
            string packagePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke de estados no valida paquetes.");

        public Task<UpdatePrepareResult> PrepareAsync(
            string manifestPath,
            string packagePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke de estados no prepara paquetes.");

        public string GetUpdaterExecutablePath() => Path.Combine(Path.GetTempPath(), "ArteInventariado.Updater.smoke.exe");
    }

    private sealed class SmokeQuotationService : IQuotationService
    {
        public Task<QuotationDetail> CreateDraftAsync(QuotationSaveRequest request, CancellationToken cancellationToken = default) => Task.FromResult(BuildQuotationDetail());
        public Task<QuotationDetail> UpdateDraftAsync(QuotationSaveRequest request, CancellationToken cancellationToken = default) => Task.FromResult(BuildQuotationDetail());
        public Task<QuotationDetail> EmitAsync(int cotizacionId, CancellationToken cancellationToken = default) => Task.FromResult(BuildQuotationDetail());
        public Task<QuotationDetail> ChangeStateAsync(QuotationStateChangeRequest request, CancellationToken cancellationToken = default) => Task.FromResult(BuildQuotationDetail());
        public Task<QuotationDetail> DuplicateAsync(int cotizacionId, CancellationToken cancellationToken = default) => Task.FromResult(BuildQuotationDetail());
        public Task<int> UpdateExpiredAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class SmokeQuotationDocumentService : IQuotationDocumentService
    {
        public Task<string> GeneratePdfAsync(int cotizacionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Path.Combine(Path.GetTempPath(), "cotizacion-smoke.pdf"));
    }

    private sealed class SmokeCustomerQueryService : ICustomerQueryService
    {
        public Task<IReadOnlyList<CustomerSummary>> SearchAsync(CustomerFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerSummary>>([]);
        public Task<CustomerDetail?> GetByIdAsync(int clienteId, CancellationToken cancellationToken = default) => Task.FromResult<CustomerDetail?>(null);
        public Task<CustomerSaleSelection?> GetSaleSelectionAsync(int clienteId, CancellationToken cancellationToken = default) => Task.FromResult<CustomerSaleSelection?>(null);
        public Task<IReadOnlyList<CustomerPurchaseHistoryRow>> GetPurchaseHistoryAsync(int clienteId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerPurchaseHistoryRow>>([]);
        public Task<SalesHistoryDetail?> GetPurchaseDetailAsync(int clienteId, int ventaId, CancellationToken cancellationToken = default) => Task.FromResult<SalesHistoryDetail?>(null);
    }

    private sealed class SmokeCommercialAlertQueryService : ICommercialAlertQueryService
    {
        private static readonly CommercialAlertSummary Summary = new(1, 1, 1, 1, 1, 5);
        public Task<CommercialAlertSearchResult> SearchAsync(CommercialAlertFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CommercialAlertSearchResult(Summary,
            [
                new(
                    CommercialAlertType.COTIZACION_PROXIMA_VENCER,
                    CommercialDocumentType.COTIZACION,
                    1,
                    "COT-000001",
                    "Cliente o destinatario con un nombre deliberadamente largo para validar el tooltip",
                    DateTime.Today.AddDays(2),
                    "EMITIDA",
                    "ALTA",
                    125000,
                    25000,
                    100000,
                    2,
                    "admin",
                    "Descripcion extensa de alerta comercial usada para comprobar ancho, ellipsis y desplazamiento horizontal."),
            ]));
        public Task<CommercialAlertSummary> GetSummaryAsync(CancellationToken cancellationToken = default) => Task.FromResult(Summary);
    }

    private sealed class SmokeSalesAnalyticsQueryService : ISalesAnalyticsQueryService
    {
        private readonly Action<string>? _checkpoint;
        private readonly bool _categoryTrendEmpty;
        private int _pendingCalls;
        private int _lastRequestedProductLimit;

        public SmokeSalesAnalyticsQueryService(
            Action<string>? checkpoint = null,
            bool categoryTrendEmpty = false)
        {
            _checkpoint = checkpoint;
            _categoryTrendEmpty = categoryTrendEmpty;
        }

        public int PendingCalls => Volatile.Read(ref _pendingCalls);
        public int LastRequestedProductLimit => Volatile.Read(ref _lastRequestedProductLimit);

        public Task<SalesAnalyticsReport> GetSalesAsync(
            AnalyticsQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            var trend = BuildTrend();
            var summary = new SalesAnalyticsSummary(
                Metric(245_000, 210_000),
                Metric(12_000, 9_000),
                Metric(8_000, 5_000),
                Metric(225_000, 196_000),
                Metric(12, 10),
                Metric(18_750, 19_600),
                Metric(45, 38));
            return Task.FromResult(new SalesAnalyticsReport(Period(), ComparedPeriod(), summary, Coverage(), trend));
        }

        public Task<ProductAnalyticsReport> GetProductsAsync(
            AnalyticsQueryRequest request,
            int limit = 20,
            CancellationToken cancellationToken = default)
        {
            _checkpoint?.Invoke("GetProductsAsync iniciado");
            Volatile.Write(ref _lastRequestedProductLimit, limit);
            Interlocked.Increment(ref _pendingCalls);
            try
            {
                if (limit is < SalesAnalyticsQueryLimits.MinimumProducts or > SalesAnalyticsQueryLimits.MaximumProducts)
                {
                    throw new ArgumentOutOfRangeException(nameof(limit));
                }

                var rows = new List<ProductAnalyticsRow>
                {
                    new(1, "PRD-001", "Lienzo artesanal", 18, 92_000, 1, 23, 0, HistoricalDataQuality.Exact),
                    new(2, "PRD-002", "Pulsera tejida", 14, 56_000, 2, 18, 1, HistoricalDataQuality.Partial),
                    new(3, "PRD-003", "Juego de madera", 9, 45_000, 0, 12, 2, HistoricalDataQuality.Unavailable)
                };
                rows.AddRange(Enumerable.Range(4, 9).Select(index => new ProductAnalyticsRow(
                    index,
                    $"PRD-{index:000}",
                    $"Producto artesanal {index}",
                    13 - index,
                    (14 - index) * 3_000L,
                    index % 3,
                    8 + index,
                    index,
                    index % 3 == 0
                        ? HistoricalDataQuality.Unavailable
                        : index % 2 == 0
                            ? HistoricalDataQuality.Partial
                            : HistoricalDataQuality.Exact)));
                var report = new ProductAnalyticsReport(
                    Period(), rows, rows.OrderByDescending(row => row.NetSalesCentavos).ToList(),
                    rows.OrderByDescending(row => row.ReturnedUnits).ToList(),
                    [new ProductWithoutSalesRow(4, "PRD-004", "Adorno sin ventas", 7, null)],
                    Coverage());
                _checkpoint?.Invoke("GetProductsAsync completado");
                return Task.FromResult(report);
            }
            finally
            {
                Interlocked.Decrement(ref _pendingCalls);
            }
        }

        public Task<ProductTrendReport> GetProductTrendAsync(
            int productId,
            AnalyticsQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            _checkpoint?.Invoke("Gráfica solicitada");
            Interlocked.Increment(ref _pendingCalls);
            try
            {
                var report = new ProductTrendReport(
                    productId,
                    $"PRD-{productId:000}",
                    "Producto de prueba",
                    Period(),
                    BuildTrend());
                _checkpoint?.Invoke("GetProductTrendAsync completado");
                return Task.FromResult(report);
            }
            finally
            {
                Interlocked.Decrement(ref _pendingCalls);
            }
        }

        public Task<CategoryAnalyticsReport> GetCategoriesAsync(
            AnalyticsQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_categoryTrendEmpty)
            {
                return Task.FromResult(new CategoryAnalyticsReport(
                    Period(),
                    [],
                    [],
                    Coverage(),
                    []));
            }

            var categories = new List<CategoryAnalyticsRow>
            {
                new(1, "Madera", 27, 135_000, 1, 60m, HistoricalDataQuality.Exact),
                new(2, "Textil", 18, 90_000, 2, 40m, HistoricalDataQuality.Exact)
            };
            var starts = Enumerable.Range(0, 7)
                .Select(index => DateTimeOffset.Now.Date.AddDays(index - 6))
                .ToArray();
            var trend = starts.SelectMany((start, index) => new[]
            {
                new CategoryTrendPoint(start, start.ToString("dd/MM"), 1, "Madera", index + 1, 8_000L + (index * 1_000L)),
                new CategoryTrendPoint(start, start.ToString("dd/MM"), 2, "Textil", index, 4_000L + (index * 750L))
            }).ToList();
            var subcategories = new List<SubcategoryAnalyticsRow>
            {
                new(1, "Tallado", 1, "Madera", 12, 70_000, 0, 52m, HistoricalDataQuality.Exact),
                new(null, AnalyticsSubcategoryFilterOptions.ExplicitlyUnassignedLabel, 1, "Madera", 5, 25_000, 1, 18m, HistoricalDataQuality.Partial),
                new(2, "Tejido", 2, "Textil", 9, 45_000, 0, 22m, HistoricalDataQuality.Exact),
                new(null, AnalyticsSubcategoryFilterOptions.HistoricalUnavailableLabel, 2, "Textil", 3, 10_000, 2, 8m, HistoricalDataQuality.Unavailable)
            };
            return Task.FromResult(new CategoryAnalyticsReport(Period(), categories, trend, Coverage(), subcategories));
        }

        private static IReadOnlyList<AnalyticsSeriesPoint> BuildTrend() => Enumerable.Range(0, 7)
            .Select(index =>
            {
                var start = DateTimeOffset.Now.Date.AddDays(index - 6);
                var gross = new long[] { 75_000, 90_000, 105_000, 125_000, 110_000, 145_000, 245_000 }[index];
                return new AnalyticsSeriesPoint(start, start.ToString("dd/MM"), gross, 2_000, 1_000, gross - 3_000, index + 1, index + 3);
            })
            .ToList();

        private static AnalyticsMetricResult Metric(long current, long previous) => new(
            current,
            new MetricComparison(current, previous, previous == 0 ? null : ((current - previous) * 100m) / previous,
                current > previous ? MetricComparisonState.Increase : current < previous ? MetricComparisonState.Decrease : MetricComparisonState.NoChange));

        private static AnalyticsPeriodDefinition Period()
        {
            var start = new DateTimeOffset(DateTime.Today.AddDays(-6));
            var end = new DateTimeOffset(DateTime.Today.AddDays(1));
            return new AnalyticsPeriodDefinition(
                AnalyticsPeriodKind.CustomRange, start, end, start.UtcDateTime, end.UtcDateTime,
                AnalyticsGrouping.Day, "Ultimos 7 dias", false);
        }

        private static AnalyticsPeriodDefinition ComparedPeriod()
        {
            var current = Period();
            var start = current.LocalStart.AddDays(-7);
            var end = current.LocalEndExclusive.AddDays(-7);
            return new AnalyticsPeriodDefinition(
                AnalyticsPeriodKind.CustomRange, start, end, start.UtcDateTime, end.UtcDateTime,
                AnalyticsGrouping.Day, "7 dias anteriores", false);
        }

        private static HistoricalCoverageSummary Coverage()
        {
            var exact = new HistoricalCoverageMetric("Snapshot", 9, 10, 90m, HistoricalDataQuality.Partial);
            return new HistoricalCoverageSummary(DateTime.UtcNow.AddDays(-30), exact, exact, exact, exact);
        }
    }

    private sealed class SmokeAdvancedReportQueryService : IAdvancedReportQueryService
    {
        public Task<AdvancedReportResult> QueryAsync(
            AdvancedReportRequest request,
            CancellationToken cancellationToken = default)
        {
            var period = AnalyticsPeriodFactory.Create(
                request.Analytics.Period,
                DateTimeOffset.Now,
                TimeZoneInfo.Local);
            var definition = AdvancedReportCatalog.Get(request.Kind);
            IReadOnlyList<AdvancedReportColumn> columns;
            IReadOnlyList<AdvancedReportRow> rows;
            IReadOnlyList<AdvancedReportSummaryItem> summary;
            switch (request.Kind)
            {
                case AdvancedReportKind.Products:
                    columns =
                    [
                        new("code", "Código"),
                        new("product", "Producto"),
                        new("category", "Categoría"),
                        new("subcategory", "Subcategoría"),
                        new("stock", "Stock actual", AdvancedReportValueKind.Integer, true)
                    ];
                    rows =
                    [
                        new(new Dictionary<string, object?>
                        {
                            ["code"] = "SKU-001", ["product"] = "Jarrón de madera",
                            ["category"] = "Madera", ["subcategory"] = "Tallado", ["stock"] = 14
                        }),
                        new(new Dictionary<string, object?>
                        {
                            ["code"] = "SKU-002", ["product"] = "Canasta artesanal",
                            ["category"] = "Textil", ["subcategory"] = "Sin subcategoría", ["stock"] = 7
                        })
                    ];
                    summary = [new("Productos", rows.Count, AdvancedReportValueKind.Integer)];
                    break;
                case AdvancedReportKind.Inventory:
                    columns =
                    [
                        new("product", "Producto"),
                        new("sku", "SKU/variante"),
                        new("category", "Categoría"),
                        new("subcategory", "Subcategoría"),
                        new("exhibition", "Exhibición", AdvancedReportValueKind.Integer),
                        new("warehouse", "Bodega", AdvancedReportValueKind.Integer),
                        new("reserved", "Reservado", AdvancedReportValueKind.Integer),
                        new("available", "Disponible", AdvancedReportValueKind.Integer, true)
                    ];
                    rows =
                    [
                        new(new Dictionary<string, object?>
                        {
                            ["product"] = "Jarrón de madera", ["sku"] = "SKU-001",
                            ["category"] = "Madera", ["subcategory"] = "Tallado",
                            ["exhibition"] = 8, ["warehouse"] = 12, ["reserved"] = 2, ["available"] = 18
                        }),
                        new(new Dictionary<string, object?>
                        {
                            ["product"] = "Canasta artesanal", ["sku"] = "SKU-002",
                            ["category"] = "Textil", ["subcategory"] = "Sin subcategoría",
                            ["exhibition"] = 3, ["warehouse"] = 6, ["reserved"] = 1, ["available"] = 8
                        })
                    ];
                    summary = [new("Disponible", 26, AdvancedReportValueKind.Integer)];
                    break;
                case AdvancedReportKind.ProductProfitability:
                    columns =
                    [
                        new("product", "Producto"),
                        new("category", "Categoría"),
                        new("subcategory", "Subcategoría"),
                        new("netSales", "Ventas netas", AdvancedReportValueKind.Money, true),
                        new("cost", "Costo", AdvancedReportValueKind.Money, true),
                        new("gain", "Ganancia", AdvancedReportValueKind.Money, true),
                        new("margin", "Margen", AdvancedReportValueKind.Percentage)
                    ];
                    rows =
                    [
                        new(new Dictionary<string, object?>
                        {
                            ["product"] = "Jarrón de madera", ["category"] = "Madera", ["subcategory"] = "Tallado",
                            ["netSales"] = 700m, ["cost"] = 280m, ["gain"] = 420m, ["margin"] = 60m
                        }),
                        new(new Dictionary<string, object?>
                        {
                            ["product"] = "Canasta artesanal", ["category"] = "Textil", ["subcategory"] = "Sin subcategoría",
                            ["netSales"] = 450m, ["cost"] = 230m, ["gain"] = 220m, ["margin"] = 48.89m
                        }),
                        new(new Dictionary<string, object?>
                        {
                            ["product"] = "Producto histórico", ["category"] = "Madera", ["subcategory"] = "No disponible (histórico)",
                            ["netSales"] = 300m, ["cost"] = null, ["gain"] = null, ["margin"] = null
                        })
                    ];
                    summary =
                    [
                        new("Ventas netas", 1_450m, AdvancedReportValueKind.Money),
                        new("Ganancia calculable", 640m, AdvancedReportValueKind.Money),
                        new("Margen", 55.65m, AdvancedReportValueKind.Percentage)
                    ];
                    break;
                case AdvancedReportKind.CurrentInventoryValue:
                    columns =
                    [
                        new("code", "Código producto"),
                        new("sku", "SKU"),
                        new("product", "Producto"),
                        new("variant", "Variante"),
                        new("category", "Categoría"),
                        new("subcategory", "Subcategoría"),
                        new("state", "Estado producto/SKU"),
                        new("stock", "Stock físico", AdvancedReportValueKind.Integer, true),
                        new("reserved", "Reservado", AdvancedReportValueKind.Integer, true),
                        new("available", "Disponible", AdvancedReportValueKind.Integer, true),
                        new("exhibition", "En exhibición", AdvancedReportValueKind.Integer, true),
                        new("warehouse", "En bodega", AdvancedReportValueKind.Integer, true),
                        new("unassigned", "Sin asignar", AdvancedReportValueKind.Integer, true),
                        new("cost", "Costo efectivo", AdvancedReportValueKind.Money),
                        new("price", "Precio venta efectivo", AdvancedReportValueKind.Money),
                        new("value", "Valor actual", AdvancedReportValueKind.Money, true),
                        new("investment", "Inversión actual", AdvancedReportValueKind.Money, true),
                        new("potential", "Venta potencial", AdvancedReportValueKind.Money, true),
                        new("availableValue", "Venta disponible", AdvancedReportValueKind.Money, true),
                        new("gain", "Ganancia bruta potencial", AdvancedReportValueKind.Money, true),
                        new("margin", "Margen bruto potencial", AdvancedReportValueKind.Percentage),
                        new("locationQuality", "Ubicación"),
                        new("quality", "Calidad/cobertura")
                    ];
                    rows =
                    [
                        new(new Dictionary<string, object?>
                        {
                            ["code"] = "PRD-ACT-001", ["sku"] = "SKU-ACT-001", ["product"] = "Jarrón activo",
                            ["variant"] = "Grande", ["category"] = "Madera", ["subcategory"] = "Tallado",
                            ["state"] = "ACTIVO / SKU activo", ["stock"] = 10, ["reserved"] = 2,
                            ["available"] = 8, ["exhibition"] = 6, ["warehouse"] = 3, ["unassigned"] = 1,
                            ["cost"] = 300m, ["price"] = 500m, ["value"] = 30m, ["investment"] = 30m,
                            ["potential"] = 50m, ["availableValue"] = 40m, ["gain"] = 20m, ["margin"] = 40m,
                            ["locationQuality"] = "Pendiente de ubicación", ["quality"] = "Pendiente de ubicación"
                        }),
                        new(new Dictionary<string, object?>
                        {
                            ["code"] = "PRD-INACT-001", ["sku"] = "SKU-INACT-001", ["product"] = "Producto inactivo",
                            ["variant"] = "", ["category"] = "Madera", ["subcategory"] = "Tallado",
                            ["state"] = "INACTIVO / SKU inactivo", ["stock"] = 4, ["reserved"] = 0,
                            ["available"] = 4, ["exhibition"] = 4, ["warehouse"] = 0, ["unassigned"] = 0,
                            ["cost"] = 250m, ["price"] = 400m, ["value"] = 10m, ["investment"] = 10m,
                            ["potential"] = 16m, ["availableValue"] = 16m, ["gain"] = 6m, ["margin"] = 37.5m,
                            ["locationQuality"] = "Ubicación identificada", ["quality"] = "Producto inactivo · SKU inactivo"
                        }),
                        new(new Dictionary<string, object?>
                        {
                            ["code"] = "PRD-ZERO-001", ["sku"] = "SKU-ZERO-001", ["product"] = "Precio cero",
                            ["variant"] = "", ["category"] = "Textil", ["subcategory"] = "Sin subcategoría",
                            ["state"] = "ACTIVO / SKU activo", ["stock"] = 2, ["reserved"] = 0,
                            ["available"] = 2, ["exhibition"] = 0, ["warehouse"] = 2, ["unassigned"] = 0,
                            ["cost"] = 100m, ["price"] = 0m, ["value"] = 2m, ["investment"] = 2m,
                            ["potential"] = 0m, ["availableValue"] = 0m, ["gain"] = -2m, ["margin"] = null,
                            ["locationQuality"] = "Ubicación identificada", ["quality"] = "Precio de venta cero"
                        }),
                        new(new Dictionary<string, object?>
                        {
                            ["code"] = "PRD-LEG-001", ["sku"] = "Sin SKU (histórico)", ["product"] = "Producto legacy",
                            ["variant"] = "", ["category"] = "Sin categoría", ["subcategory"] = "Sin subcategoría",
                            ["state"] = "ACTIVO", ["stock"] = 3, ["reserved"] = null, ["available"] = null,
                            ["exhibition"] = null, ["warehouse"] = null, ["unassigned"] = null,
                            ["cost"] = 200m, ["price"] = 300m, ["value"] = 6m, ["investment"] = 6m,
                            ["potential"] = 9m, ["availableValue"] = null, ["gain"] = 3m, ["margin"] = 33.33m,
                            ["locationQuality"] = "Legacy sin ubicación", ["quality"] = "Legacy sin SKU/ubicación"
                        })
                    ];
                    summary =
                    [
                        new("Inversión actual", 48m, AdvancedReportValueKind.Money),
                        new("Venta potencial física", 75m, AdvancedReportValueKind.Money),
                        new("Venta disponible", 56m, AdvancedReportValueKind.Money),
                        new("Ganancia bruta potencial", 27m, AdvancedReportValueKind.Money),
                        new("Margen bruto potencial", 36m, AdvancedReportValueKind.Percentage),
                        new("Stock físico total", 19, AdvancedReportValueKind.Integer),
                        new("Reservado total", null, AdvancedReportValueKind.Integer),
                        new("Disponible total", null, AdvancedReportValueKind.Integer)
                    ];
                    break;
                default:
                    columns =
                    [
                        new("period", "Período"),
                        new("net", "Ventas netas", AdvancedReportValueKind.Money, true),
                        new("margin", "Margen", AdvancedReportValueKind.Percentage)
                    ];
                    rows = [new(new Dictionary<string, object?> { ["period"] = "12/08", ["net"] = 2_450m, ["margin"] = 43.25m })];
                    summary = [new("Ventas netas", 2_450m, AdvancedReportValueKind.Money)];
                    break;
            }

            return Task.FromResult(new AdvancedReportResult(
                definition,
                period,
                ["Sin filtros específicos"],
                columns,
                rows,
                rows.Count,
                summary,
                "Cobertura histórica exacta: 90%.",
                "No existen ventas en este período."));
        }
    }

    private sealed class SmokeAdvancedReportExportService : IAdvancedReportExportService
    {
        public Task<AdvancedReportExportResult> ExportExcelAsync(
            AdvancedReportRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdvancedReportExportResult(
                Path.Combine(Path.GetTempPath(), "reporte-avanzado-smoke.xlsx"), 1, DateTime.Now));
    }

    private sealed class SmokeAdvancedReportFilterOptionsService : IAdvancedReportFilterOptionsService
    {
        public Task<IReadOnlyList<AdvancedReportFilterOption>> GetOptionsAsync(
            AdvancedReportKind reportKind,
            AdvancedReportFilterKind filterKind,
            string? search = null,
            int limit = 75,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdvancedReportFilterOption>>(
                filterKind switch
                {
                    AdvancedReportFilterKind.Category =>
                    [
                        new(filterKind, 1, "CAT-001", "Madera"),
                        new(filterKind, 2, "CAT-002", "Textil")
                    ],
                    AdvancedReportFilterKind.Subcategory =>
                    [
                        new(filterKind, 1, "SUB-001", "Tallado", 1),
                        new(filterKind, 2, "SUB-002", "Tejido", 2)
                    ],
                    _ => [new(filterKind, 1, "SMOKE-001", "Opción de prueba")]
                });
    }

    private sealed class SmokeCommercialAnalyticsQueryService : ICommercialAnalyticsQueryService
    {
        public Task<CommercialAnalyticsReport> GetAsync(
            AnalyticsQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            var start = new DateTimeOffset(DateTime.Today.AddDays(-6));
            var end = new DateTimeOffset(DateTime.Today.AddDays(1));
            var period = new AnalyticsPeriodDefinition(
                AnalyticsPeriodKind.CustomRange,
                start,
                end,
                start.UtcDateTime,
                end.UtcDateTime,
                AnalyticsGrouping.Day,
                "Últimos 7 días",
                false);
            var compared = period with
            {
                LocalStart = start.AddDays(-7),
                LocalEndExclusive = end.AddDays(-7),
                UtcStart = start.AddDays(-7).UtcDateTime,
                UtcEndExclusive = end.AddDays(-7).UtcDateTime,
                DisplayName = "7 días anteriores"
            };
            var quotes = Metric(18, 14);
            var quotedAmount = Metric(9_876_543_210, 7_250_000);
            var orders = Metric(7, 5);
            var trend = Enumerable.Range(0, 7).Select(index =>
            {
                var bucket = start.AddDays(index);
                return new CommercialAnalyticsPoint(
                    bucket,
                    bucket.ToString("dd/MM"),
                    index + 1,
                    900_000L + index * 120_000L,
                    Math.Max(0, index - 1),
                    450_000L + index * 75_000L);
            }).ToList();
            return Task.FromResult(new CommercialAnalyticsReport(
                period,
                compared,
                new QuotationCommercialAnalytics(quotes, quotedAmount, 11, 3, 2, 2, 6, 4_200_000, 30.5m),
                new CustomOrderCommercialAnalytics(
                    orders, 2, 2, 1, 1, 1, 1, 5_900_000, 1_800_000, 3_400_000, 250_000, 2_500_000, 3, 96m),
                [
                    new("Cotizaciones aceptadas", 11, 18, 61.11m, true),
                    new("Cotización → pedido", 6, 18, 33.33m, true),
                    new("Cotización → venta directa", 0, 0, null, false, "El flujo real pasa por pedido."),
                    new("Pedido → venta", 3, 7, 42.86m, true),
                    new("Pedidos completados", 1, 7, 14.29m, true),
                    new("Pedidos cancelados", 1, 7, 14.29m, true)
                ],
                trend,
                [new(1, "CLI-001", "Cliente de actividad comercial extensa", 8, 4_600_000, 4, 3_100_000)],
                "Cobertura exacta basada en fechas, estados e historiales registrados."));
        }

        private static AnalyticsMetricResult Metric(long current, long previous) => new(
            current,
            new MetricComparison(
                current,
                previous,
                previous == 0 ? null : (current - previous) * 100m / previous,
                current > previous ? MetricComparisonState.Increase :
                    current < previous ? MetricComparisonState.Decrease : MetricComparisonState.NoChange));
    }

    private sealed class SmokeFinancialAnalyticsQueryService : IFinancialAnalyticsQueryService
    {
        public Task<FinancialAnalyticsReport> GetAsync(
            AnalyticsQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            var start = new DateTimeOffset(DateTime.Today.AddDays(-6));
            var end = new DateTimeOffset(DateTime.Today.AddDays(1));
            var period = new AnalyticsPeriodDefinition(
                AnalyticsPeriodKind.CustomRange,
                start,
                end,
                start.UtcDateTime,
                end.UtcDateTime,
                AnalyticsGrouping.Day,
                "Últimos 7 días",
                false);
            var compared = period with
            {
                LocalStart = start.AddDays(-7),
                LocalEndExclusive = end.AddDays(-7),
                UtcStart = start.AddDays(-7).UtcDateTime,
                UtcEndExclusive = end.AddDays(-7).UtcDateTime,
                DisplayName = "7 días anteriores"
            };
            var coverage = new HistoricalCoverageMetric(
                "Costo histórico",
                9,
                10,
                90m,
                HistoricalDataQuality.Partial);
            var trend = Enumerable.Range(0, 7).Select(index =>
            {
                var bucket = start.AddDays(index);
                return new FinancialAnalyticsPoint(
                    bucket,
                    bucket.ToString("dd/MM"),
                    32_000 + index * 1_000,
                    15_000 + index * 500,
                    17_000 + index * 500,
                    2_500,
                    0);
            }).ToList();
            var money = (long current, long previous) => new AnalyticsMetricResult(
                current,
                new MetricComparison(
                    current,
                    previous,
                    previous == 0 ? null : (current - previous) * 100m / previous,
                    current > previous ? MetricComparisonState.Increase : MetricComparisonState.Decrease));
            var finance = new FinancialOverviewAnalytics(
                money(98_765_432_100, 96_000),
                money(45_678_901_200, 180_000),
                money(53_086_530_900, 90_000),
                money(53_086_530_900, 90_000),
                new DecimalAnalyticsMetricResult(52.2m, 50m, 4.4m, MetricComparisonState.Increase),
                money(18_000, 16_000),
                money(12_345_678_900, 40_000),
                coverage,
                trend,
                [new ExpenseCategoryAnalyticsRow("Transporte", 18_000, 100m)],
                [new ExpenseAnalyticsRow(1, DateTime.UtcNow, "Transporte", "admin", "EFECTIVO", null, "Envío", 18_000)]);
            var cash = new CashAnalyticsSummary(
                10_000, 95_000, 15_000, 5_000, 3_000, 12_000, 18_000, 4_000,
                88_000, 78_000, 87_500, -500, 0, 7);
            var purchases = new PurchaseInvestmentAnalytics(
                money(80_000, 70_000),
                money(0, 40_000),
                money(45_000, 38_000),
                625_000,
                coverage);
            var aging = new List<AgingBucketAnalytics>
            {
                new("Vigente", 2, 50_000),
                new("1-30 días", 1, 20_000)
            };
            var balances = new List<CounterpartyBalanceAnalytics>
            {
                new(1, "CLI-001", "Cliente prueba", 45_000)
            };
            var evolution = trend.Select(point => new BalanceEvolutionPoint(
                point.BucketStart,
                point.Label,
                10_000,
                6_000,
                4_000,
                70_000)).ToList();
            var obligations = new ObligationAnalytics(70_000, 35_000, 28_000, 2, 1, aging, balances, evolution);
            return Task.FromResult(new FinancialAnalyticsReport(
                period,
                compared,
                finance,
                cash,
                purchases,
                obligations,
                obligations));
        }
    }

    private sealed class SmokeDashboardQueryService : IDashboardQueryService
    {
        public Task<DashboardOverview> GetOverviewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DashboardOverview(
                DateTime.UtcNow,
                new DashboardSalesToday(
                    245_000,
                    12,
                    new MetricComparison(245_000, 210_000, 16.7m, MetricComparisonState.Increase)),
                128,
                86,
                new DashboardCashExpected(true, 1, 895_000),
                15));

        public Task<DashboardSalesTrend> GetSalesTrendAsync(
            DashboardSalesRange range,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DashboardSalesTrend(
                range,
                "Ultimos 7 dias",
                Enumerable.Range(0, 7)
                    .Select(index => new DashboardSalesTrendPoint(
                        DateOnly.FromDateTime(DateTime.Today.AddDays(index - 6)),
                        new long[] { 75_000, 165_000, 98_000, 202_000, 110_000, 145_000, 245_000 }[index]))
                    .ToList()));

        public Task<DashboardCategorySales> GetCategorySalesAsync(
            DashboardSalesRange range,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DashboardCategorySales(
                range,
                245_000,
                false,
                [
                    new DashboardCategorySalesPoint(1, "Madera", 98_000, 40),
                    new DashboardCategorySalesPoint(2, "Textil", 61_250, 25),
                    new DashboardCategorySalesPoint(3, "Decoracion", 49_000, 20),
                    new DashboardCategorySalesPoint(4, "Otros", 36_750, 15)
                ]));

        public Task<IReadOnlyList<DashboardTopProduct>> GetTopProductsAsync(
            DashboardSalesRange range,
            int limit = 5,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DashboardTopProduct>>(
            [
                new(1, "PRD-001", "Lienzo artesanal 50x70", 45, 23),
                new(2, "PRD-002", "Pulseras tejidas", 32, 18),
                new(3, "PRD-003", "Juego de madera", 28, 31),
                new(4, "PRD-004", "Cartera decorada", 21, 12),
                new(5, "PRD-005", "Adorno tradicional", 18, 27)
            ]);

        public Task<IReadOnlyList<DashboardRecentActivity>> GetRecentActivityAsync(
            int limit = 5,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DashboardRecentActivity>>(
            [
                new(1, DateTime.UtcNow.AddMinutes(-15), "empleado1", "Venta registrada", "Venta VTA-000125 registrada.", "sale"),
                new(2, DateTime.UtcNow.AddHours(-1), "admin", "Producto agregado", "Lienzo artesanal agregado.", "product"),
                new(3, DateTime.UtcNow.AddHours(-2), "empleado1", "Inicio de sesion", "El usuario inicio sesion.", "user"),
                new(4, DateTime.UtcNow.AddHours(-3), "admin", "Devolucion registrada", "Devolucion DEV-00012 registrada.", "return"),
                new(5, DateTime.UtcNow.AddHours(-4), "admin", "Compra registrada", "Compra CMP-00078 registrada.", "purchase")
            ]);
    }

    private sealed class SmokeCreditAlertQueryService : ICreditAlertQueryService
    {
        private static readonly CreditAlertSettings Settings = new(3, 30, 80, true, true, true, true, true, true, true);
        public Task<CreditAlertSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Settings);
        public Task UpdateSettingsAsync(UpdateCreditAlertSettingsRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CreditAlertSummary> GetSummaryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CreditAlertSummary(2, 0, 1, 1, 0, 0, 0, 0));
        public Task<IReadOnlyList<CreditAlertItem>> GetAlertsAsync(CreditAlertFilter? filter = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CreditAlertItem>>([]);
        public Task<CreditCustomerAlertIndicator> GetCustomerIndicatorAsync(int clienteId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CreditCustomerAlertIndicator(clienteId, 0, string.Empty, null));
    }

    private sealed class SmokeAuthService : IAuthService
    {
        public Task<AuthResult> LoginAsync(string nombreUsuario, string password, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SmokePermissionService(
        IReadOnlySet<PermisoEmpleado>? enabledPermissions = null) : IEmployeePermissionService
    {
        private bool IsAllowed(PermisoEmpleado permission) =>
            enabledPermissions is null || enabledPermissions.Contains(permission);

        public Task<bool> HasPermissionAsync(PermisoEmpleado permiso, int? usuarioId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsAllowed(permiso));

        public Task<bool> HasAnyPermissionAsync(IReadOnlyCollection<PermisoEmpleado> permisos, int? usuarioId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(permisos.Any(IsAllowed));

        public Task EnsurePermissionAsync(PermisoEmpleado permiso, CancellationToken cancellationToken = default) =>
            IsAllowed(permiso)
                ? Task.CompletedTask
                : Task.FromException(new UnauthorizedAccessException($"Permiso smoke denegado: {permiso}."));

        public Task<IReadOnlyList<EmployeePermissionState>> GetPermissionsForUserAsync(int usuarioId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EmployeePermissionState>>([]);
        public Task UpdatePermissionsAsync(UpdateEmployeePermissionsRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreRecommendedTemplateAsync(int usuarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ApplySafeOperationalTemplateAsync(int usuarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureRecommendedPermissionsForEmployeeAsync(int usuarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SmokeServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class SmokeAnalyticsServiceProvider : IServiceProvider
    {
        private readonly SmokeAdvancedReportFilterOptionsService _options = new();

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IAdvancedReportFilterOptionsService) ? _options : null;
    }

    private sealed class SmokeEmployeeDashboardServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(EmployeeInventoryLookupForm))
            {
                return new EmployeeInventoryLookupForm(
                    new SmokeEmployeeInventoryLookupService(),
                    new SmokeEmployeeInventoryAssetService());
            }
            if (serviceType == typeof(IAccountService))
            {
                return new SmokeAccountService();
            }

            return null;
        }
    }

    private sealed class SmokeStationSettingsService(bool quickPosEnabled) : IStationSettingsService
    {
        public Task<LocalReportSettings> GetReportSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalReportSettings("smoke-reports", "smoke-station-settings.json"));

        public Task<string> GetBackupDirectoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("smoke-backups");

        public Task<LocalPosSettings> GetPosSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalPosSettings(quickPosEnabled, "smoke-station-settings.json"));

        public Task UpdateReportSettingsAsync(
            UpdateLocalReportSettingsRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdatePosSettingsAsync(
            UpdateLocalPosSettingsRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SmokeSession(
        string userName = "admin",
        RolUsuario role = RolUsuario.ADMIN) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; } = new(
            1,
            role == RolUsuario.ADMIN ? "Admin smoke" : "Empleado smoke",
            userName,
            role);

        public bool EstaAutenticado => true;
        public bool EsAdmin => role == RolUsuario.ADMIN;
        public bool TieneRol(RolUsuario rol) => rol == role;
        public void IniciarSesion(UsuarioSesion usuario)
        {
        }

        public void CerrarSesion()
        {
        }
    }

    private sealed class SmokeInstallationContext(InstallationMode? mode = InstallationMode.Principal) : IInstallationContext
    {
        public InstallationStatus Status { get; } = new(
            mode.HasValue ? StationConfigurationState.Ready : StationConfigurationState.RequiresSetup,
            mode,
            mode.HasValue,
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            1,
            "smoke-station-settings.json",
            null);

        public bool AllowsOperationalWrites => Status.AllowsOperationalWrites;

        public void EnsureInitialized()
        {
        }

        public void EnsureOperationalWriteAllowed(string operationName)
        {
        }
    }

    private sealed class SmokeInstallationManagementService(
        InstallationStatus status,
        bool isProtectedAdministrator) : IInstallationManagementService
    {
        private readonly InstallationSetupProfile _profile = new(
            status.IsConfigured ? InstallationSetupKind.AlreadyConfigured : InstallationSetupKind.NewInstallation,
            status,
            isProtectedAdministrator,
            "Configurada para smoke visual.");

        public Task<InstallationSetupProfile> GetSetupProfileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_profile);

        public Task<InstallationConfigurationResult> CompleteSetupAsync(
            CompleteInstallationSetupRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no configura la estacion.");

        public Task<InstallationConfigurationResult> ChangeModeAsync(
            ChangeInstallationModeRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no cambia el modo de estacion.");
    }

    private sealed class SmokeEmployeeInventoryLookupService : IEmployeeInventoryLookupService
    {
        private static readonly IReadOnlyList<EmployeeInventoryLookupItem> Items =
        [
            BuildItem(1, "PRD-SMOKE-001", "Alcancía artesanal", 8, 4, "Jarrones"),
            BuildItem(2, "PRD-SMOKE-002", "Bolso tejido", 5, 7, null),
            BuildItem(3, "PRD-SMOKE-003", "Decoración de madera", 3, 9, "Adornos")
        ];

        public Task<IReadOnlyList<EmployeeInventoryLookupItem>> SearchAsync(
            string? searchText = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var search = searchText?.Trim();
            IReadOnlyList<EmployeeInventoryLookupItem> result = string.IsNullOrWhiteSpace(search)
                ? Items
                : Items.Where(item =>
                        item.CodigoInterno.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        item.CodigoSku.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        item.ProductoNombre.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        (item.VarianteNombre?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        item.CategoriaNombre.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        (item.SubcategoriaNombre?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();
            return Task.FromResult(result);
        }

        private static EmployeeInventoryLookupItem BuildItem(
            int id,
            string code,
            string name,
            int exhibition,
            int warehouse,
            string? subcategoryName) => new(
            CodigoInterno: code,
            CodigoSku: code,
            ProductoNombre: name,
            VarianteNombre: null,
            StockFisicoExhibicion: exhibition,
            StockReservadoExhibicion: 0,
            StockFisicoBodega: warehouse,
            StockReservadoBodega: 0,
            StockFisicoSinAsignar: 0,
            StockReservadoSinAsignar: 0,
            ImagenOwnerKind: ProductAssetOwnerKind.Product,
            ImagenAssetId: id,
            ImagenRutaRelativa: null,
            ImagenLocalPath: null,
            CategoriaId: 1,
            CategoriaNombre: "Artesanías",
            SubcategoriaNombre: subcategoryName,
            PrecioVentaEfectivoCentavos: 1500);
    }

    private sealed class SmokeEmployeeInventoryAssetService(
        string? thumbnailPath = null,
        int delayMilliseconds = 0) : IProductAssetService
    {
        public Task<IReadOnlyList<ProductAssetSummary>> GetByProductAsync(
            int productoId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductAssetSummary>>([]);

        public Task<ProductAssetSummary> ImportAsync(
            ImportProductAssetRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no importa imágenes.");

        public Task SetPrimaryAsync(
            ProductAssetOwnerKind ownerKind,
            int assetId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no cambia imágenes.");

        public Task DeleteAsync(
            ProductAssetOwnerKind ownerKind,
            int assetId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no elimina imágenes.");

        public async Task<string?> GetThumbnailPathAsync(
            ProductAssetOwnerKind ownerKind,
            int assetId,
            int maximumPixels = 400,
            CancellationToken cancellationToken = default)
        {
            if (delayMilliseconds > 0)
                await Task.Delay(delayMilliseconds, CancellationToken.None);
            return thumbnailPath;
        }

        public Task<AssetCleanupResult> CleanupOrphansAsync(
            TimeSpan minimumAge,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AssetCleanupResult(0, 0, []));
    }

    private sealed class SmokeProductService : IProductService
    {
        public Task<IReadOnlyList<ProductSummary>> GetAllAsync(
            ProductFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var product = new ProductSummary(
                1,
                "PRD-SMOKE-001",
                "Producto smoke",
                "Producto usado solo para smoke visual",
                1,
                "Categoria smoke",
                null,
                100,
                150,
                0,
                2,
                EstadoProducto.ACTIVO,
                DateTime.UtcNow,
                null,
                SubcategoriaId: 11,
                SubcategoriaNombre: "Jarrones");

            if (filter?.Estado is { } state && product.Estado != state)
                return Task.FromResult<IReadOnlyList<ProductSummary>>([]);
            if (filter?.CategoriaId is { } categoryId && product.CategoriaId != categoryId)
                return Task.FromResult<IReadOnlyList<ProductSummary>>([]);
            if (filter?.SubcategoriaId is { } subcategoryId && product.SubcategoriaId != subcategoryId)
                return Task.FromResult<IReadOnlyList<ProductSummary>>([]);

            var terms = QuotationProductPickerForm.Normalize(filter?.SearchText)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length > 0)
            {
                var searchable = QuotationProductPickerForm.Normalize(
                    $"{product.CodigoInterno} {product.Nombre} {product.Descripcion} " +
                    $"{product.CategoriaNombre} {product.SubcategoriaNombre}");
                if (!terms.All(term => searchable.Contains(term, StringComparison.Ordinal)))
                    return Task.FromResult<IReadOnlyList<ProductSummary>>([]);
            }

            return Task.FromResult<IReadOnlyList<ProductSummary>>([product]);
        }

        public Task<IReadOnlyList<ProductSummary>> SearchByNameAsync(
            string nombre,
            CancellationToken cancellationToken = default) => GetAllAsync(cancellationToken: cancellationToken);

        public Task<IReadOnlyList<ProductSummary>> GetAvailableForSaleAsync(
            ProductFilter? filter = null,
            CancellationToken cancellationToken = default) =>
            GetAllAsync(filter, cancellationToken);

        public Task<ProductSummary?> GetByCodigoInternoAsync(
            string codigoInterno,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ProductSummary?>(null);

        public Task<IReadOnlyList<ProductSummary>> GetLowStockAsync(CancellationToken cancellationToken = default) =>
            GetAllAsync(cancellationToken: cancellationToken);

        public Task<ProductSummary> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no crea productos.");

        public Task<ProductSummary> UpdateAsync(UpdateProductRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no edita productos.");

        public Task SetEstadoAsync(int productoId, EstadoProducto estado, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no cambia estado de productos.");

        public Task DeleteAsync(int productoId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no elimina productos.");
    }

    private sealed class SmokeCategoryService : ICategoryService
    {
        private static readonly CategorySummary Category = new(1, "Categoria smoke", null, true);

        public Task<IReadOnlyList<CategorySummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CategorySummary>>([Category]);

        public Task<IReadOnlyList<CategorySummary>> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CategorySummary>>([Category]);

        public Task<CategorySummary> GetDefaultCategoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Category);

        public Task<CategorySummary> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no crea categorias.");

        public Task<CategorySummary> UpdateAsync(UpdateCategoryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no edita categorias.");

        public Task SetActiveAsync(int categoriaId, bool activa, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no cambia categorias.");

        public Task DeleteAsync(int categoriaId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no elimina categorias.");
    }

    private sealed class SmokeSupplierService : ISupplierService
    {
        public Task<SupplierDetail> CreateAsync(SupplierSaveRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no crea proveedores.");

        public Task<SupplierDetail> UpdateAsync(SupplierSaveRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no edita proveedores.");

        public Task SetStateAsync(SupplierStateChangeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no cambia estado de proveedores.");

        public Task<SupplierDuplicateCheckResult> FindDuplicatesAsync(
            SupplierSaveRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SupplierDuplicateCheckResult([]));

        public Task<SupplierProductSummary> AssociateProductAsync(
            SupplierProductAssociationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SupplierProductSummary(
                1,
                request.ProductoId,
                "PRD-SMOKE-001",
                "Producto smoke",
                "Categoria smoke",
                request.CodigoProductoProveedor,
                request.EsProveedorPrincipal,
                null,
                null,
                true,
                request.Notas,
                "Jarrones"));

        public Task DeactivateProductAssociationAsync(
            int productoProveedorId,
            string motivo,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no desasocia productos.");

        public Task SetMainProductSupplierAsync(
            SupplierProductMainRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no marca proveedor principal.");
    }

    private sealed class SmokeSupplierQueryService : ISupplierQueryService
    {
        private static readonly SupplierProductSummary Product = new(
            1,
            1,
            "PRD-SMOKE-001",
            "Producto smoke",
            "Artesanías",
            "PROV-SMOKE-001",
            true,
            100,
            DateTime.UtcNow.AddDays(-2),
            true,
            "Asociación de prueba",
            "Jarrones");

        private static readonly SupplierDetail Detail = new(
            1,
            "PRV-SMOKE-001",
            TipoProveedor.PROVEEDOR,
            "Proveedor smoke",
            "Contacto smoke",
            "7000-0000",
            "7000-0000",
            "proveedor-smoke@example.com",
            "Dirección smoke",
            null,
            null,
            "Datos de prueba visual",
            EstadoProveedor.ACTIVO,
            DateTime.UtcNow.AddMonths(-1),
            DateTime.UtcNow,
            "admin",
            "admin",
            null,
            null,
            [Product],
            []);

        public Task<IReadOnlyList<SupplierSummary>> SearchAsync(
            SupplierFilter filter,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SupplierSummary>>(
            [new(1, "PRV-SMOKE-001", TipoProveedor.PROVEEDOR, "Proveedor smoke", "Contacto smoke", "7000-0000", null, null, EstadoProveedor.ACTIVO, 1, DateTime.UtcNow.AddDays(-2))]);

        public Task<SupplierDetail?> GetByIdAsync(
            int proveedorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SupplierDetail?>(proveedorId == Detail.ProveedorId ? Detail : null);

        public Task<IReadOnlyList<SupplierProductSummary>> GetProductsAsync(
            int proveedorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SupplierProductSummary>>(proveedorId == Detail.ProveedorId ? Detail.Productos : []);

        public Task<IReadOnlyList<SupplierHistoryEntry>> GetHistoryAsync(
            int proveedorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SupplierHistoryEntry>>(proveedorId == Detail.ProveedorId ? Detail.Historial : []);
    }

    private sealed class SmokePosCatalogService : IPosCatalogService
    {
        private static readonly PosSkuSaleItem Item = new(
            ProductoId: 1,
            ProductoSkuId: 7,
            CodigoInterno: "PRD-SMOKE-001",
            CodigoSku: "SKU-SMOKE-001",
            ProductoNombre: "Producto smoke",
            VarianteNombre: "Tamaño mediano",
            CategoriaId: 1,
            CategoriaNombre: "Artesanías",
            Modo: ModoProducto.POR_VARIANTES,
            Estado: EstadoProducto.ACTIVO,
            SkuActivo: true,
            SkuPrincipal: true,
            PrecioVentaEfectivoCentavos: 1500,
            CostoEfectivoCentavos: 700,
            MinimoExhibicion: 1,
            StockFisicoExhibicion: 4,
            StockReservadoExhibicion: 0,
            StockFisicoBodega: 2,
            StockReservadoBodega: 0,
            StockFisicoSinAsignar: 0,
            StockReservadoSinAsignar: 0,
            ImagenOwnerKind: null,
            ImagenAssetId: null,
            ImagenRutaRelativa: null,
            ImagenLocalPath: null,
            SubcategoriaId: 11,
            SubcategoriaNombre: "Jarrones");

        public Task<IReadOnlyList<PosSkuSaleItem>> SearchAsync(
            PosSkuCatalogFilter? filter = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PosSkuSaleItem>>([Item]);

        public Task<PosSkuSaleItem?> GetBySkuIdAsync(
            int productoSkuId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PosSkuSaleItem?>(productoSkuId == Item.ProductoSkuId ? Item : null);
    }

    private sealed class SmokeCustomOrderFinanceService : ICustomOrderFinanceService
    {
        public Task<CustomOrderPaymentResult> RegisterPaymentAsync(
            CustomOrderPaymentRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no registra pagos.");

        public Task<CustomOrderRefundResult> RegisterRefundAsync(
            CustomOrderRefundRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no registra reembolsos.");

        public Task MapProductAsync(
            CustomOrderProductMappingRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no mapea productos.");

        public Task<CustomOrderConversionPreview> GetConversionPreviewAsync(
            int pedidoPersonalizadoId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(BuildCustomOrderConversionPreview());

        public Task<CustomOrderConversionResult> ConvertToSaleAsync(
            CustomOrderConversionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no convierte pedidos.");
    }

    private sealed class CreditAlertsLayoutSmokeForm : CreditAlertsForm
    {
        public CreditAlertsLayoutSmokeForm()
            : base(null!, null!, null!, null!, null!, null!, null!, null!, null!)
        {
        }

        protected override void OnLoad(EventArgs e)
        {
        }
    }

    private sealed class AccountPaymentsHistoryLayoutSmokeForm : AccountPaymentsHistoryForm
    {
        public AccountPaymentsHistoryLayoutSmokeForm()
            : base(null!, null!)
        {
        }

        protected override void OnLoad(EventArgs e)
        {
        }
    }

    private sealed class LoginLayoutSmokeForm : LoginForm
    {
        public LoginLayoutSmokeForm()
            : base(null!, null!, null!)
        {
        }

        protected override void OnShown(EventArgs e)
        {
        }
    }

    private sealed class CustomersLayoutSmokeForm : CustomersForm
    {
        public CustomersLayoutSmokeForm()
            : base(null!, null!, null!, null!, null!)
        {
        }

        protected override void OnLoad(EventArgs e)
        {
        }
    }

    private sealed class SmokeUserService : IUserService
    {
        public Task<IReadOnlyList<UserSummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserSummary>>([BuildSmokeUser()]);

        public Task<UserSummary?> GetByIdAsync(int usuarioId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserSummary?>(BuildSmokeUser());

        public Task<UserSummary> CreateAsync(
            CreateUserRequest request,
            int? usuarioActualId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no crea usuarios.");

        public Task<UserSummary> UpdateAsync(
            UpdateUserRequest request,
            int? usuarioActualId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no edita usuarios.");

        public Task SetActiveAsync(
            int usuarioId,
            bool activo,
            int? usuarioActualId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no cambia usuarios.");

        public Task ChangePasswordAsync(
            int usuarioId,
            string nuevoPassword,
            int? usuarioActualId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no cambia contraseñas.");
    }

    private sealed class SmokeAccountService : IAccountService
    {
        public Task ChangeCurrentPasswordAsync(
            ChangeCurrentPasswordRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no cambia contraseñas.");

    }

    private sealed class SmokeProductImportService : IProductImportService
    {
        public Task<string> CreateProductImportTemplateAsync(
            string? outputDirectory = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no crea archivos.");

        public Task<ProductImportPreviewResult> PreviewProductsAsync(
            string filePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductImportPreviewResult(filePath, 0, 0, 0, 0, [], []));

        public Task<ProductImportResult> ImportProductsAsync(
            string filePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual: no importa productos.");
    }

}
