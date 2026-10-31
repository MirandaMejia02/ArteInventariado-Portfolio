using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public enum EmployeeDashboardMoreAction
{
    Etiquetas,
    Clientes,
    HistorialVentas,
    MisVentasHoy,
    HistorialDevoluciones,
    CuentasPorCobrar,
    AlertasCredito,
    Proveedores,
    Compras,
    CuentasPorPagar,
    Cotizaciones,
    PedidosPersonalizados,
    AlertasComerciales,
    ReportesComerciales,
    RentabilidadProductos
}

public sealed record EmployeeDashboardMoreMenuItem(
    EmployeeDashboardMoreAction Action,
    string Text,
    PermisoEmpleado RequiredPermission);

public static class EmployeeDashboardMoreMenuCatalog
{
    public const string EmptyTooltip = "No hay opciones adicionales disponibles.";

    public static IReadOnlyList<EmployeeDashboardMoreMenuItem> Build(
        IReadOnlyDictionary<PermisoEmpleado, bool> permissions)
    {
        var items = new List<EmployeeDashboardMoreMenuItem>();

        AddIfAnyAllowed(
            items,
            permissions,
            [PermisoEmpleado.PRODUCTOS_GESTIONAR, PermisoEmpleado.ETIQUETAS_GESTIONAR, PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR],
            EmployeeDashboardMoreAction.Etiquetas,
            "Productos, etiquetas e imágenes");

        AddIfAllowed(
            items,
            permissions,
            PermisoEmpleado.CLIENTES_ACCEDER,
            EmployeeDashboardMoreAction.Clientes,
            "Clientes");

        AddIfAllowed(
            items,
            permissions,
            PermisoEmpleado.HISTORIAL_VENTAS_VER,
            EmployeeDashboardMoreAction.HistorialVentas,
            "Historial de ventas");

        AddIfAllowed(
            items,
            permissions,
            PermisoEmpleado.VENTAS_PROPIAS_DIA_VER,
            EmployeeDashboardMoreAction.MisVentasHoy,
            "Mis ventas de hoy");

        AddIfAllowed(
            items,
            permissions,
            PermisoEmpleado.HISTORIAL_DEVOLUCIONES_VER,
            EmployeeDashboardMoreAction.HistorialDevoluciones,
            "Historial de devoluciones");

        AddIfAllowed(
            items,
            permissions,
            PermisoEmpleado.CUENTAS_COBRAR_VER,
            EmployeeDashboardMoreAction.CuentasPorCobrar,
            "Cuentas por cobrar");

        AddIfAllowed(
            items,
            permissions,
            PermisoEmpleado.ALERTAS_CREDITO_VER,
            EmployeeDashboardMoreAction.AlertasCredito,
            "Alertas de credito");

        AddIfAllowed(
            items,
            permissions,
            PermisoEmpleado.PROVEEDORES_ACCEDER,
            EmployeeDashboardMoreAction.Proveedores,
            "Proveedores y artesanos");

        AddIfAllowed(
            items,
            permissions,
            PermisoEmpleado.COMPRAS_ACCEDER,
            EmployeeDashboardMoreAction.Compras,
            "Compras");

        AddIfAllowed(
            items,
            permissions,
            PermisoEmpleado.CUENTAS_PAGAR_ACCEDER,
            EmployeeDashboardMoreAction.CuentasPorPagar,
            "Cuentas por pagar");

        AddIfAllowed(
            items,
            permissions,
            PermisoEmpleado.COTIZACIONES_ACCEDER,
            EmployeeDashboardMoreAction.Cotizaciones,
            "Cotizaciones");

        AddIfAllowed(
            items,
            permissions,
            PermisoEmpleado.PEDIDOS_ACCEDER,
            EmployeeDashboardMoreAction.PedidosPersonalizados,
            "Pedidos personalizados");

        AddIfAllowed(
            items,
            permissions,
            PermisoEmpleado.ALERTAS_COMERCIALES_VER,
            EmployeeDashboardMoreAction.AlertasComerciales,
            "Alertas comerciales");

        AddIfAllowed(
            items,
            permissions,
            PermisoEmpleado.REPORTES_COMERCIALES_VER,
            EmployeeDashboardMoreAction.ReportesComerciales,
            "Reportes comerciales");

        AddIfAllAllowed(
            items,
            permissions,
            [PermisoEmpleado.REPORTES_COMERCIALES_VER, PermisoEmpleado.COSTOS_VER],
            EmployeeDashboardMoreAction.RentabilidadProductos,
            "Rentabilidad por producto",
            PermisoEmpleado.REPORTES_COMERCIALES_VER);

        return items;
    }

    private static void AddIfAllowed(
        ICollection<EmployeeDashboardMoreMenuItem> items,
        IReadOnlyDictionary<PermisoEmpleado, bool> permissions,
        PermisoEmpleado permission,
        EmployeeDashboardMoreAction action,
        string text)
    {
        if (!permissions.GetValueOrDefault(permission))
        {
            return;
        }

        items.Add(new EmployeeDashboardMoreMenuItem(action, text, permission));
    }

    private static void AddIfAnyAllowed(
        ICollection<EmployeeDashboardMoreMenuItem> items,
        IReadOnlyDictionary<PermisoEmpleado, bool> permissions,
        IReadOnlyList<PermisoEmpleado> candidates,
        EmployeeDashboardMoreAction action,
        string text)
    {
        var granted = candidates.FirstOrDefault(permissions.GetValueOrDefault);
        if (!permissions.GetValueOrDefault(granted))
        {
            return;
        }

        items.Add(new EmployeeDashboardMoreMenuItem(action, text, granted));
    }

    private static void AddIfAllAllowed(
        ICollection<EmployeeDashboardMoreMenuItem> items,
        IReadOnlyDictionary<PermisoEmpleado, bool> permissions,
        IReadOnlyList<PermisoEmpleado> required,
        EmployeeDashboardMoreAction action,
        string text,
        PermisoEmpleado menuPermission)
    {
        if (required.Any(permission => !permissions.GetValueOrDefault(permission)))
        {
            return;
        }

        items.Add(new EmployeeDashboardMoreMenuItem(action, text, menuPermission));
    }
}
