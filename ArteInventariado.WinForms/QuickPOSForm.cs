using ArteInventariado.Application.Interfaces;

namespace ArteInventariado.WinForms;

public enum PosPresentationMode
{
    Traditional,
    Quick
}

public sealed class QuickPOSForm : EmployeePOSForm
{
    public QuickPOSForm(
        IProductService productService,
        IPosCatalogService posCatalogService,
        IBarcodeService barcodeService,
        IInventoryService inventoryService,
        IInstallationContext installation,
        ICategoryService categoryService,
        ISaleService saleService,
        ICashService cashService,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        ICreditService creditService,
        ISystemConfigService systemConfigService,
        ICustomerQueryService customerQueryService,
        CashRecoveryCoordinator? cashRecovery = null,
        IProductAssetService? productAssetService = null,
        IReceiptDocumentService? receiptDocuments = null,
        IPeripheralOutputCoordinator? peripheralOutput = null,
        ISubcategoryService? subcategoryService = null)
        : base(
            productService,
            posCatalogService,
            barcodeService,
            inventoryService,
            installation,
            categoryService,
            saleService,
            cashService,
            currentSession,
            permissionService,
            creditService,
            systemConfigService,
            cashRecovery,
            loadDataOnShow: true,
            productAssetService,
            PosPresentationMode.Quick,
            customerQueryService,
            receiptDocuments,
            peripheralOutput,
            subcategoryService)
    {
    }

    internal QuickPOSForm(
        IProductService productService,
        IPosCatalogService posCatalogService,
        IBarcodeService barcodeService,
        IInventoryService inventoryService,
        IInstallationContext installation,
        ICategoryService categoryService,
        ISaleService saleService,
        ICashService cashService,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        ICreditService creditService,
        ISystemConfigService systemConfigService,
        bool loadDataOnShow,
        IProductAssetService? productAssetService = null,
        ICustomerQueryService? customerQueryService = null,
        IReceiptDocumentService? receiptDocuments = null,
        IPeripheralOutputCoordinator? peripheralOutput = null,
        ISubcategoryService? subcategoryService = null)
        : base(
            productService,
            posCatalogService,
            barcodeService,
            inventoryService,
            installation,
            categoryService,
            saleService,
            cashService,
            currentSession,
            permissionService,
            creditService,
            systemConfigService,
            cashRecovery: null,
            loadDataOnShow,
            productAssetService,
            PosPresentationMode.Quick,
            customerQueryService,
            receiptDocuments,
            peripheralOutput,
            subcategoryService)
    {
    }
}
