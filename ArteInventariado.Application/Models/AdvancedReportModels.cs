namespace ArteInventariado.Application.Models;

public enum AdvancedReportGroup
{
    Commercial = 1,
    Inventory = 2,
    Financial = 3,
    Customers = 4,
    Suppliers = 5,
    AuditOperations = 6
}

public enum AdvancedReportKind
{
    Sales = 1,
    Products = 2,
    Categories = 3,
    Inventory = 4,
    InventoryMovements = 5,
    PaymentMethods = 6,
    Cash = 7,
    Expenses = 8,
    Withdrawals = 9,
    Purchases = 10,
    PurchaseReceptions = 11,
    Suppliers = 12,
    Customers = 13,
    AccountsReceivable = 14,
    CustomerPayments = 15,
    AccountsPayable = 16,
    SupplierPayments = 17,
    Returns = 18,
    Refunds = 19,
    Cancellations = 20,
    Quotations = 21,
    CustomOrders = 22,
    GrossProfit = 23,
    CurrentInventoryValue = 24,
    EmployeeOperations = 25,
    AdministrativeActivity = 26,
    ProductProfitability = 27
}

public enum AdvancedReportFilterKind
{
    Search = 1,
    Customer = 2,
    Product = 3,
    Category = 4,
    Supplier = 5,
    User = 6,
    State = 7,
    PaymentMethod = 8,
    CashRegister = 9,
    MovementType = 10,
    Subcategory = 11
}

public enum AdvancedReportValueKind
{
    Text = 1,
    Integer = 2,
    Money = 3,
    Percentage = 4,
    Date = 5,
    DateTime = 6,
    Boolean = 7
}

public sealed record AdvancedReportDefinition(
    AdvancedReportKind Kind,
    AdvancedReportGroup Group,
    string Name,
    string Description,
    bool UsesPeriod,
    IReadOnlyList<AdvancedReportFilterKind> Filters,
    IReadOnlyList<string> SearchableFields);

public sealed record AdvancedReportFilterOption(
    AdvancedReportFilterKind FilterKind,
    int Id,
    string Code,
    string Name,
    int? ParentId = null)
{
    public string DisplayText => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} · {Name}";
}

public sealed record AdvancedReportColumn(
    string Key,
    string Title,
    AdvancedReportValueKind ValueKind = AdvancedReportValueKind.Text,
    bool IncludeInTotal = false);

public sealed record AdvancedReportRow(IReadOnlyDictionary<string, object?> Values)
{
    public object? this[string key] => Values.TryGetValue(key, out var value) ? value : null;
}

public sealed record AdvancedReportSummaryItem(
    string Label,
    object? Value,
    AdvancedReportValueKind ValueKind = AdvancedReportValueKind.Text);

public sealed record AdvancedReportRequest(
    AdvancedReportKind Kind,
    AnalyticsQueryRequest Analytics,
    string? Search = null,
    int? CustomerId = null,
    int? ProductId = null,
    int? CategoryId = null,
    int? SupplierId = null,
    int? UserId = null,
    string? State = null,
    string? PaymentMethod = null,
    int? CashRegisterId = null,
    string? MovementType = null,
    string? SelectedEntityDisplay = null,
    int Page = 1,
    int PageSize = 200,
    ProductProfitabilitySort ProfitabilitySort = ProductProfitabilitySort.UnitsSold,
    bool ProfitabilityDescending = true,
    int? ProfitabilityTop = null,
    IReadOnlyCollection<int>? SelectedProductIds = null,
    int? SubcategoryId = null,
    AnalyticsSubcategoryFilter SubcategoryFilter = AnalyticsSubcategoryFilter.All);

public sealed record AdvancedReportResult(
    AdvancedReportDefinition Definition,
    AnalyticsPeriodDefinition Period,
    IReadOnlyList<string> AppliedFilters,
    IReadOnlyList<AdvancedReportColumn> Columns,
    IReadOnlyList<AdvancedReportRow> Rows,
    int TotalRows,
    IReadOnlyList<AdvancedReportSummaryItem> Summary,
    string? CoverageNote,
    string EmptyMessage);

public sealed record AdvancedReportExportResult(
    string FilePath,
    int ExportedRows,
    DateTime GeneratedAtLocal);

public static class AdvancedReportCatalog
{
    public static IReadOnlyList<AdvancedReportDefinition> All { get; } =
    [
        Def(AdvancedReportKind.Sales, AdvancedReportGroup.Commercial, "Ventas", "Ventas brutas, descuentos, devoluciones y ventas netas.", []),
        Def(AdvancedReportKind.PaymentMethods, AdvancedReportGroup.Commercial, "Métodos de pago", "Importes cobrados por método sin duplicar anticipos aplicados.", [AdvancedReportFilterKind.PaymentMethod]),
        Def(AdvancedReportKind.Quotations, AdvancedReportGroup.Commercial, "Cotizaciones", "Cotizaciones, estados, importes y conversión a pedido.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Customer, AdvancedReportFilterKind.User, AdvancedReportFilterKind.State], searchableFields: ["Código de cotización", "destinatario"]),
        Def(AdvancedReportKind.CustomOrders, AdvancedReportGroup.Commercial, "Pedidos personalizados", "Pedidos, cumplimiento, anticipos, saldos y conversión a venta.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Customer, AdvancedReportFilterKind.User, AdvancedReportFilterKind.State], searchableFields: ["Código de pedido", "destinatario"]),
        Def(AdvancedReportKind.Returns, AdvancedReportGroup.Commercial, "Devoluciones", "Devoluciones y cambios registrados.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Customer, AdvancedReportFilterKind.User, AdvancedReportFilterKind.CashRegister], searchableFields: ["Número de devolución", "número de venta", "cliente", "motivo"]),
        Def(AdvancedReportKind.Refunds, AdvancedReportGroup.Commercial, "Reembolsos", "Reembolsos de ventas y pedidos personalizados.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Customer, AdvancedReportFilterKind.User, AdvancedReportFilterKind.PaymentMethod, AdvancedReportFilterKind.CashRegister], searchableFields: ["Documento origen", "cliente o destinatario", "motivo"]),
        Def(AdvancedReportKind.Cancellations, AdvancedReportGroup.Commercial, "Anulaciones", "Ventas anuladas y operaciones canceladas.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Customer, AdvancedReportFilterKind.User], searchableFields: ["Documento", "cliente o destinatario", "motivo u observación"]),

        Def(AdvancedReportKind.Products, AdvancedReportGroup.Inventory, "Productos", "Catálogo de productos y actividad comercial.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Product, AdvancedReportFilterKind.Category, AdvancedReportFilterKind.Subcategory], searchableFields: ["Código", "nombre", "categoría", "subcategoría"]),
        Def(AdvancedReportKind.ProductProfitability, AdvancedReportGroup.Inventory, "Rentabilidad por producto", "Ventas netas, costo histórico, ganancia, margen y cobertura por producto.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Product, AdvancedReportFilterKind.Category, AdvancedReportFilterKind.Subcategory], searchableFields: ["código o SKU", "nombre", "categoría", "subcategoría"]),
        Def(AdvancedReportKind.Categories, AdvancedReportGroup.Inventory, "Categorías y subcategorías", "Ventas y unidades netas por categoría y subcategoría.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Category, AdvancedReportFilterKind.Subcategory], searchableFields: ["categoría", "subcategoría"]),
        Def(AdvancedReportKind.Inventory, AdvancedReportGroup.Inventory, "Inventario", "Salud de inventario por SKU, ubicación, reservas, reposición, inmovilizado y mermas.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Product, AdvancedReportFilterKind.Category, AdvancedReportFilterKind.Subcategory, AdvancedReportFilterKind.State], searchableFields: ["Código de producto", "SKU", "variante", "producto", "categoría", "subcategoría"]),
        Def(AdvancedReportKind.InventoryMovements, AdvancedReportGroup.Inventory, "Movimientos de inventario", "Libro canónico por SKU y ubicación, con fallback histórico sin inventar identidad.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Product, AdvancedReportFilterKind.User, AdvancedReportFilterKind.MovementType], searchableFields: ["Código", "SKU", "variante", "producto", "motivo", "usuario"]),
        Def(AdvancedReportKind.CurrentInventoryValue, AdvancedReportGroup.Inventory, "Valor actual de inventario", "Valor actual, venta potencial y ganancia bruta potencial del stock físico por SKU.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Category, AdvancedReportFilterKind.Subcategory, AdvancedReportFilterKind.State], searchableFields: ["Código de producto", "SKU", "variante", "producto", "categoría", "subcategoría"]),

        Def(AdvancedReportKind.GrossProfit, AdvancedReportGroup.Financial, "Ganancia bruta", "Ventas netas, costo vendido, ganancia, margen y cobertura histórica.", []),
        Def(AdvancedReportKind.Cash, AdvancedReportGroup.Financial, "Caja", "Entradas, salidas, efectivo esperado y diferencias de cierre.", [AdvancedReportFilterKind.CashRegister]),
        Def(AdvancedReportKind.Expenses, AdvancedReportGroup.Financial, "Gastos", "Gastos operativos separados de retiros.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.User, AdvancedReportFilterKind.PaymentMethod, AdvancedReportFilterKind.CashRegister], searchableFields: ["Concepto", "observaciones"]),
        Def(AdvancedReportKind.Withdrawals, AdvancedReportGroup.Financial, "Retiros", "Retiros de caja sin tratarlos como gasto.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.User, AdvancedReportFilterKind.CashRegister], searchableFields: ["Motivo", "usuario"]),

        Def(AdvancedReportKind.Customers, AdvancedReportGroup.Customers, "Clientes", "Directorio y estado de crédito sin datos técnicos sensibles.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Customer, AdvancedReportFilterKind.State], usesPeriod: false, searchableFields: ["Código", "nombre", "teléfono", "correo"]),
        Def(AdvancedReportKind.AccountsReceivable, AdvancedReportGroup.Customers, "Cuentas por cobrar", "Cartera, vencimientos y saldos de clientes.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Customer, AdvancedReportFilterKind.State], searchableFields: ["Cuenta", "venta", "código o nombre del cliente"]),
        Def(AdvancedReportKind.CustomerPayments, AdvancedReportGroup.Customers, "Pagos de clientes", "Abonos de cuentas por cobrar.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Customer, AdvancedReportFilterKind.User, AdvancedReportFilterKind.PaymentMethod, AdvancedReportFilterKind.CashRegister], searchableFields: ["Pago", "cuenta", "código o nombre del cliente"]),

        Def(AdvancedReportKind.Purchases, AdvancedReportGroup.Suppliers, "Compras", "Compras comprometidas, recepción y estado financiero.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Supplier, AdvancedReportFilterKind.User, AdvancedReportFilterKind.State], searchableFields: ["Compra", "código o nombre del proveedor"]),
        Def(AdvancedReportKind.PurchaseReceptions, AdvancedReportGroup.Suppliers, "Recepciones", "Mercancía recibida y responsable de recepción.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Supplier, AdvancedReportFilterKind.Product, AdvancedReportFilterKind.Category, AdvancedReportFilterKind.Subcategory, AdvancedReportFilterKind.User], searchableFields: ["Recepción", "compra", "producto", "proveedor", "categoría", "subcategoría"]),
        Def(AdvancedReportKind.Suppliers, AdvancedReportGroup.Suppliers, "Proveedores", "Directorio y estado de proveedores.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Supplier, AdvancedReportFilterKind.State], usesPeriod: false, searchableFields: ["Código", "nombre", "contacto", "teléfono", "correo"]),
        Def(AdvancedReportKind.AccountsPayable, AdvancedReportGroup.Suppliers, "Cuentas por pagar", "Obligaciones, vencimientos y saldos a proveedores.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Supplier, AdvancedReportFilterKind.State], searchableFields: ["Cuenta", "compra", "código o nombre del proveedor"]),
        Def(AdvancedReportKind.SupplierPayments, AdvancedReportGroup.Suppliers, "Pagos a proveedores", "Pagos iniciales y abonos a cuentas por pagar.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.Supplier, AdvancedReportFilterKind.User, AdvancedReportFilterKind.PaymentMethod, AdvancedReportFilterKind.CashRegister], searchableFields: ["Pago", "cuenta o compra", "código o nombre del proveedor"]),

        Def(AdvancedReportKind.EmployeeOperations, AdvancedReportGroup.AuditOperations, "Operaciones por empleado", "Contexto de ventas, devoluciones, caja y actividad; no es un ranking de productividad.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.User], searchableFields: ["Usuario", "nombre"]),
        Def(AdvancedReportKind.AdministrativeActivity, AdvancedReportGroup.AuditOperations, "Actividad administrativa", "Bitácora resumida sin JSON interno ni datos antes/después.", [AdvancedReportFilterKind.Search, AdvancedReportFilterKind.User], searchableFields: ["Usuario", "entidad", "descripción"])
    ];

    public static AdvancedReportDefinition Get(AdvancedReportKind kind) =>
        All.FirstOrDefault(definition => definition.Kind == kind)
        ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "Reporte no registrado.");

    private static AdvancedReportDefinition Def(
        AdvancedReportKind kind,
        AdvancedReportGroup group,
        string name,
        string description,
        IReadOnlyList<AdvancedReportFilterKind> filters,
        bool usesPeriod = true,
        IReadOnlyList<string>? searchableFields = null) => new(
            kind,
            group,
            name,
            description,
            usesPeriod,
            filters,
            searchableFields ?? []);
}
