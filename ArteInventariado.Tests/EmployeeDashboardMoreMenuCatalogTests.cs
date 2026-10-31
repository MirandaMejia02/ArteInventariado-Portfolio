using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Tests;

public class EmployeeDashboardMoreMenuCatalogTests
{
    [Fact]
    public void Build_WithOnlyCustomersPermission_ShowsOnlyCustomers()
    {
        var items = Build(PermisoEmpleado.CLIENTES_ACCEDER);

        var item = Assert.Single(items);
        Assert.Equal(EmployeeDashboardMoreAction.Clientes, item.Action);
        Assert.Equal("Clientes", item.Text);
    }

    [Fact]
    public void Build_WithOnlyLabelsPermission_ShowsLabels()
    {
        var items = Build(PermisoEmpleado.ETIQUETAS_GESTIONAR);

        var item = Assert.Single(items);
        Assert.Equal(EmployeeDashboardMoreAction.Etiquetas, item.Action);
        Assert.Equal("Productos, etiquetas e imágenes", item.Text);
    }

    [Fact]
    public void Build_WithOnlyProductImagesPermission_ShowsProductWorkspace()
    {
        var items = Build(PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR);

        var item = Assert.Single(items);
        Assert.Equal(EmployeeDashboardMoreAction.Etiquetas, item.Action);
        Assert.Equal("Productos, etiquetas e imágenes", item.Text);
        Assert.Equal(PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR, item.RequiredPermission);
    }

    [Fact]
    public void Build_WithOnlySalesHistoryPermission_ShowsOnlySalesHistory()
    {
        var items = Build(PermisoEmpleado.HISTORIAL_VENTAS_VER);

        var item = Assert.Single(items);
        Assert.Equal(EmployeeDashboardMoreAction.HistorialVentas, item.Action);
        Assert.Equal("Historial de ventas", item.Text);
    }

    [Fact]
    public void Build_WithOnlyReturnsHistoryPermission_ShowsOnlyReturnsHistory()
    {
        var items = Build(PermisoEmpleado.HISTORIAL_DEVOLUCIONES_VER);

        var item = Assert.Single(items);
        Assert.Equal(EmployeeDashboardMoreAction.HistorialDevoluciones, item.Action);
        Assert.Equal("Historial de devoluciones", item.Text);
    }

    [Fact]
    public void Build_WithAllMorePermissions_ShowsAllOptions()
    {
        var items = Build(
            PermisoEmpleado.CLIENTES_ACCEDER,
            PermisoEmpleado.HISTORIAL_VENTAS_VER,
            PermisoEmpleado.HISTORIAL_DEVOLUCIONES_VER,
            PermisoEmpleado.CUENTAS_COBRAR_VER,
            PermisoEmpleado.ALERTAS_CREDITO_VER,
            PermisoEmpleado.PROVEEDORES_ACCEDER);

        Assert.Collection(
            items,
            item => Assert.Equal(EmployeeDashboardMoreAction.Clientes, item.Action),
            item => Assert.Equal(EmployeeDashboardMoreAction.HistorialVentas, item.Action),
            item => Assert.Equal(EmployeeDashboardMoreAction.HistorialDevoluciones, item.Action),
            item => Assert.Equal(EmployeeDashboardMoreAction.CuentasPorCobrar, item.Action),
            item => Assert.Equal(EmployeeDashboardMoreAction.AlertasCredito, item.Action),
            item => Assert.Equal(EmployeeDashboardMoreAction.Proveedores, item.Action));
    }

    [Fact]
    public void Build_WithAccountsReceivablePermission_ShowsAccountsReceivable()
    {
        var items = Build(PermisoEmpleado.CUENTAS_COBRAR_VER);

        var item = Assert.Single(items);
        Assert.Equal(EmployeeDashboardMoreAction.CuentasPorCobrar, item.Action);
        Assert.Equal("Cuentas por cobrar", item.Text);
    }

    [Fact]
    public void Build_WithCreditAlertsPermission_ShowsCreditAlerts()
    {
        var items = Build(PermisoEmpleado.ALERTAS_CREDITO_VER);

        var item = Assert.Single(items);
        Assert.Equal(EmployeeDashboardMoreAction.AlertasCredito, item.Action);
        Assert.Equal("Alertas de credito", item.Text);
    }

    [Fact]
    public void Build_WithSupplierPermission_ShowsSuppliers()
    {
        var items = Build(PermisoEmpleado.PROVEEDORES_ACCEDER);

        var item = Assert.Single(items);
        Assert.Equal(EmployeeDashboardMoreAction.Proveedores, item.Action);
        Assert.Equal("Proveedores y artesanos", item.Text);
    }

    [Fact]
    public void Build_WithQuotationPermission_ShowsQuotations()
    {
        var items = Build(PermisoEmpleado.COTIZACIONES_ACCEDER);

        var item = Assert.Single(items);
        Assert.Equal(EmployeeDashboardMoreAction.Cotizaciones, item.Action);
        Assert.Equal("Cotizaciones", item.Text);
    }

    [Fact]
    public void Build_WithCustomOrdersPermission_ShowsCustomOrders()
    {
        var items = Build(PermisoEmpleado.PEDIDOS_ACCEDER);

        var item = Assert.Single(items);
        Assert.Equal(EmployeeDashboardMoreAction.PedidosPersonalizados, item.Action);
        Assert.Equal("Pedidos personalizados", item.Text);
    }

    [Fact]
    public void Build_WithReportsButWithoutCosts_HidesProductProfitability()
    {
        var items = Build(PermisoEmpleado.REPORTES_COMERCIALES_VER);

        Assert.DoesNotContain(items, item =>
            item.Action == EmployeeDashboardMoreAction.RentabilidadProductos);
    }

    [Fact]
    public void Build_WithReportsAndCosts_ShowsProductProfitability()
    {
        var items = Build(
            PermisoEmpleado.REPORTES_COMERCIALES_VER,
            PermisoEmpleado.COSTOS_VER);

        var item = Assert.Single(
            items,
            option => option.Action == EmployeeDashboardMoreAction.RentabilidadProductos);
        Assert.Equal("Rentabilidad por producto", item.Text);
    }

    [Fact]
    public void Build_WithoutMorePermissions_ReturnsNoOptions()
    {
        var items = Build();

        Assert.Empty(items);
        Assert.Equal("No hay opciones adicionales disponibles.", EmployeeDashboardMoreMenuCatalog.EmptyTooltip);
    }

    private static IReadOnlyList<EmployeeDashboardMoreMenuItem> Build(params PermisoEmpleado[] enabled)
    {
        var states = EmployeePermissionCatalog.AllPermissions.ToDictionary(permission => permission, _ => false);
        foreach (var permission in enabled)
        {
            states[permission] = true;
        }

        return EmployeeDashboardMoreMenuCatalog.Build(states);
    }
}
