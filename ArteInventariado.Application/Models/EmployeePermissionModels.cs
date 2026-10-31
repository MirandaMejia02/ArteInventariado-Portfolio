using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record EmployeePermissionDefinition(
    PermisoEmpleado Permiso,
    string Grupo,
    string Nombre,
    string Descripcion,
    bool FuncionFutura = false);

public sealed record EmployeePermissionState(
    PermisoEmpleado Permiso,
    string Grupo,
    string Nombre,
    string Descripcion,
    bool Habilitado,
    bool FuncionFutura = false);

public sealed record EmployeePermissionUpdate(
    PermisoEmpleado Permiso,
    bool Habilitado);

public sealed record UpdateEmployeePermissionsRequest(
    int UsuarioId,
    IReadOnlyList<EmployeePermissionUpdate> Permisos);

public sealed record EmployeePermissionAuditChange(
    PermisoEmpleado Permiso,
    bool Antes,
    bool Despues);

public static class EmployeePermissionCatalog
{
    public static IReadOnlyList<EmployeePermissionDefinition> All { get; } =
    [
        new(PermisoEmpleado.POS_ACCEDER, "Punto de venta", "Acceder al POS", "Permite abrir el punto de venta."),
        new(PermisoEmpleado.VENTA_REGISTRAR, "Punto de venta", "Registrar ventas", "Permite guardar ventas desde el POS."),
        new(PermisoEmpleado.DESCUENTO_LINEA_APLICAR, "Punto de venta", "Descuento de linea", "Permite aplicar descuentos por producto."),
        new(PermisoEmpleado.DESCUENTO_GENERAL_APLICAR, "Punto de venta", "Descuento general", "Permite aplicar descuentos generales."),
        new(PermisoEmpleado.PAGO_TRANSFERENCIA_USAR, "Punto de venta", "Usar transferencia", "Permite registrar pagos por transferencia."),
        new(PermisoEmpleado.PAGO_BITCOIN_USAR, "Punto de venta", "Usar Bitcoin", "Permite registrar pagos por Bitcoin."),
        new(PermisoEmpleado.COMPROBANTE_REIMPRIMIR, "Historiales", "Reimprimir comprobantes", "Permite reimprimir tickets o recibos de venta."),
        new(PermisoEmpleado.HISTORIAL_VENTAS_VER, "Historiales", "Ver historial de ventas", "Permite consultar ventas registradas."),
        new(PermisoEmpleado.VENTAS_PROPIAS_DIA_VER, "Historiales", "Ver mis ventas de hoy", "Permite consultar únicamente las ventas del usuario autenticado durante el día empresarial actual."),

        new(PermisoEmpleado.CORRECCIONES_ACCEDER, "Devoluciones y cambios", "Acceder a correcciones", "Permite abrir el modulo de devoluciones y cambios."),
        new(PermisoEmpleado.DEVOLUCION_REGISTRAR, "Devoluciones y cambios", "Registrar devoluciones", "Permite registrar devoluciones operativas."),
        new(PermisoEmpleado.CAMBIO_REGISTRAR, "Devoluciones y cambios", "Registrar cambios", "Permite registrar cambios simples."),
        new(PermisoEmpleado.HISTORIAL_DEVOLUCIONES_VER, "Historiales", "Ver historial de devoluciones", "Permite consultar devoluciones y cambios registrados."),

        new(PermisoEmpleado.CLIENTES_ACCEDER, "Clientes y creditos", "Acceder a clientes", "Permite abrir y consultar el modulo de clientes."),
        new(PermisoEmpleado.CLIENTE_CREAR, "Clientes y creditos", "Crear clientes", "Permite registrar nuevos clientes."),
        new(PermisoEmpleado.CLIENTE_EDITAR, "Clientes y creditos", "Editar clientes", "Permite modificar datos basicos de clientes."),
        new(PermisoEmpleado.CLIENTE_HISTORIAL_VER, "Clientes y creditos", "Ver historial del cliente", "Permite consultar compras asociadas al cliente."),
        new(PermisoEmpleado.CUENTAS_COBRAR_VER, "Clientes y creditos", "Ver cuentas por cobrar", "Permite consultar cuentas por cobrar y saldos de clientes."),
        new(PermisoEmpleado.CREDITO_REGISTRAR, "Clientes y creditos", "Registrar credito", "Permite registrar ventas a credito desde el POS."),
        new(PermisoEmpleado.ABONO_REGISTRAR, "Clientes y creditos", "Registrar abonos", "Permite registrar abonos a cuentas por cobrar."),
        new(PermisoEmpleado.ALERTAS_CREDITO_VER, "Clientes y creditos", "Ver alertas de credito", "Permite consultar vencimientos, mora y riesgos de credito."),

        new(PermisoEmpleado.PROVEEDORES_ACCEDER, "Proveedores y artesanos", "Acceder al modulo", "Permite abrir y consultar proveedores y artesanos."),
        new(PermisoEmpleado.PROVEEDOR_CREAR, "Proveedores y artesanos", "Crear proveedores o artesanos", "Permite registrar nuevos proveedores o artesanos."),
        new(PermisoEmpleado.PROVEEDOR_EDITAR, "Proveedores y artesanos", "Editar datos basicos", "Permite modificar datos basicos de proveedores y artesanos."),
        new(PermisoEmpleado.PROVEEDOR_HISTORIAL_VER, "Proveedores y artesanos", "Ver historial", "Permite consultar historial de cambios de proveedores."),
        new(PermisoEmpleado.PROVEEDOR_PRODUCTOS_GESTIONAR, "Proveedores y artesanos", "Gestionar productos asociados", "Permite asociar o desasociar productos existentes."),

        new(PermisoEmpleado.COMPRAS_ACCEDER, "Compras", "Acceder al modulo de compras", "Permite abrir y consultar compras a proveedores."),
        new(PermisoEmpleado.COMPRA_CREAR, "Compras", "Registrar compra", "Permite registrar compras sin afectar inventario hasta la recepcion."),
        new(PermisoEmpleado.COMPRA_EDITAR, "Compras", "Editar compra", "Permite editar compras que aun no tienen recepciones."),
        new(PermisoEmpleado.COMPRA_RECEPCIONAR, "Compras", "Registrar recepcion", "Permite recibir productos y aumentar inventario."),
        new(PermisoEmpleado.COMPRA_HISTORIAL_VER, "Compras", "Ver historial de compras", "Permite consultar compras, recepciones y trazabilidad."),
        new(PermisoEmpleado.COSTOS_VER, "Compras", "Ver costos", "Permite consultar costos unitarios, promedio e historial de costos."),

        new(PermisoEmpleado.CUENTAS_PAGAR_ACCEDER, "Finanzas de proveedores", "Acceder a cuentas por pagar", "Permite consultar saldos pendientes con proveedores."),
        new(PermisoEmpleado.PAGO_COMPRA_REGISTRAR, "Finanzas de proveedores", "Registrar pago inicial", "Permite registrar pagos al crear o liquidar una compra."),
        new(PermisoEmpleado.ABONO_PROVEEDOR_REGISTRAR, "Finanzas de proveedores", "Registrar abonos", "Permite abonar a cuentas por pagar de proveedores."),
        new(PermisoEmpleado.PAGOS_PROVEEDOR_HISTORIAL_VER, "Finanzas de proveedores", "Ver historial de pagos", "Permite consultar pagos y abonos a proveedores."),
        new(PermisoEmpleado.FINANZAS_PROVEEDOR_VER, "Finanzas de proveedores", "Ver finanzas del proveedor", "Permite ver saldos y cuentas desde el perfil del proveedor."),

        new(PermisoEmpleado.COTIZACIONES_ACCEDER, "Cotizaciones", "Acceder a cotizaciones", "Permite abrir y consultar el modulo de cotizaciones."),
        new(PermisoEmpleado.COTIZACION_CREAR, "Cotizaciones", "Crear cotizaciones", "Permite guardar nuevos borradores."),
        new(PermisoEmpleado.COTIZACION_EDITAR, "Cotizaciones", "Editar borradores", "Permite modificar cotizaciones en estado borrador."),
        new(PermisoEmpleado.COTIZACION_EMITIR, "Cotizaciones", "Emitir cotizaciones", "Permite emitir borradores y cambiar estados comerciales."),
        new(PermisoEmpleado.COTIZACION_HISTORIAL_VER, "Cotizaciones", "Ver historial", "Permite consultar el historial de estados."),
        new(PermisoEmpleado.COTIZACION_CANCELAR, "Cotizaciones", "Cancelar cotizaciones", "Permite cancelar cotizaciones indicando un motivo."),

        new(PermisoEmpleado.PEDIDOS_ACCEDER, "Pedidos personalizados", "Acceder a pedidos", "Permite abrir y consultar pedidos personalizados."),
        new(PermisoEmpleado.PEDIDO_CREAR, "Pedidos personalizados", "Crear pedidos", "Permite crear pedidos y convertir cotizaciones aceptadas."),
        new(PermisoEmpleado.PEDIDO_EDITAR, "Pedidos personalizados", "Editar borradores", "Permite modificar pedidos en estado borrador."),
        new(PermisoEmpleado.PEDIDO_CONFIRMAR, "Pedidos personalizados", "Confirmar pedidos", "Permite congelar los datos comerciales de un borrador."),
        new(PermisoEmpleado.PEDIDO_ESTADO_CAMBIAR, "Pedidos personalizados", "Cambiar estado operativo", "Permite iniciar produccion, marcar listo y registrar entrega."),
        new(PermisoEmpleado.PEDIDO_HISTORIAL_VER, "Pedidos personalizados", "Ver historial", "Permite consultar el historial de estados del pedido."),
        new(PermisoEmpleado.PEDIDO_CANCELAR, "Pedidos personalizados", "Cancelar pedidos", "Permite cancelar pedidos indicando un motivo."),
        new(PermisoEmpleado.PEDIDO_PAGO_REGISTRAR, "Pedidos personalizados", "Registrar pagos", "Permite registrar anticipos, abonos y pagos finales."),
        new(PermisoEmpleado.PEDIDO_PAGOS_HISTORIAL_VER, "Pedidos personalizados", "Ver historial financiero", "Permite consultar pagos, reembolsos y aplicaciones a venta."),
        new(PermisoEmpleado.PEDIDO_REEMBOLSO_REGISTRAR, "Pedidos personalizados", "Registrar reembolsos", "Permite devolver pagos del pedido de forma controlada."),
        new(PermisoEmpleado.PEDIDO_CONVERTIR_VENTA, "Pedidos personalizados", "Convertir en venta", "Permite entregar un pedido creando la venta final."),
        new(PermisoEmpleado.PEDIDO_RESERVA_GESTIONAR, "Pedidos personalizados", "Gestionar reservas", "Permite gestionar las reservas de inventario del pedido."),
        new(PermisoEmpleado.PEDIDO_FINANZAS_VER, "Pedidos personalizados", "Ver finanzas", "Permite consultar total pagado, reembolsado y saldo."),

        new(PermisoEmpleado.ALERTAS_COMERCIALES_VER, "Operaciones comerciales", "Ver alertas comerciales", "Permite consultar alertas calculadas de cotizaciones y pedidos."),
        new(PermisoEmpleado.REPORTES_COMERCIALES_VER, "Operaciones comerciales", "Exportar reportes comerciales", "Permite exportar reportes comerciales y de caja."),
        new(PermisoEmpleado.EMPLEADOS_VER, "Administracion", "Ver empleados", "Permite consultar actividad y ventas de empleados sin modificar sus cuentas."),
        new(PermisoEmpleado.EMPLEADOS_PAGOS_VER, "Administracion", "Ver pagos de empleados", "Permite consultar el historial administrativo de pagos de empleados."),
        new(PermisoEmpleado.EMPLEADOS_PAGOS_REGISTRAR, "Administracion", "Registrar pagos de empleados", "Permite registrar pagos administrativos de empleados."),
        new(PermisoEmpleado.EMPLEADOS_PAGOS_ANULAR, "Administracion", "Anular pagos de empleados", "Permite anular un pago conservando su trazabilidad y motivo."),

        new(PermisoEmpleado.INVENTARIO_GESTIONAR, "Inventario", "Gestionar inventario", "Permite transferir, ajustar y configurar minimos de inventario por SKU y ubicacion."),
        new(PermisoEmpleado.MERMA_REGISTRAR, "Inventario", "Registrar mermas", "Permite descontar inventario por dano o perdida con motivo y trazabilidad."),
        new(PermisoEmpleado.PRODUCTOS_GESTIONAR, "Inventario", "Gestionar productos", "Permite crear y editar productos, SKU e imagenes administradas."),
        new(PermisoEmpleado.ETIQUETAS_GESTIONAR, "Inventario", "Gestionar etiquetas", "Permite generar códigos internos para productos existentes y previsualizar/imprimir etiquetas. No permite modificar productos ni inventario."),
        new(PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR, "Inventario", "Gestionar imágenes de productos", "Permite importar, definir como principal y eliminar imágenes administradas de productos o SKU. No permite modificar productos, inventario ni códigos de barras."),
        new(PermisoEmpleado.PRODUCTOS_COSTO_COMPRA_EDITAR, "Inventario", "Editar costo de compra", "Permite asignar o cambiar manualmente el costo base del producto y los costos override de sus SKU. No bloquea el registro de costos reales en compras a proveedores."),

        new(PermisoEmpleado.CAJA_ABRIR, "Caja", "Abrir caja", "Permite abrir la caja diaria."),
        new(PermisoEmpleado.CAJA_CERRAR, "Caja", "Cerrar caja", "Permite cerrar la caja diaria."),
        new(PermisoEmpleado.GASTO_REGISTRAR, "Caja", "Registrar gastos", "Permite registrar gastos de caja."),
        new(PermisoEmpleado.RETIRO_REGISTRAR, "Caja", "Registrar retiros", "Permite registrar retiros de efectivo."),
        new(PermisoEmpleado.CAJA_TOTAL_ESPERADO_VER, "Caja", "Ver total esperado", "Permite ver el total esperado de caja."),
        new(PermisoEmpleado.CAJA_DIFERENCIAS_VER, "Caja", "Ver diferencias", "Permite revisar diferencias de caja."),
        new(PermisoEmpleado.CAJA_MOVIMIENTOS_DETALLE_VER, "Caja", "Ver detalle de movimientos", "Permite consultar el origen y metodo de cada movimiento de caja."),
        new(PermisoEmpleado.CAJA_APORTE_REGISTRAR, "Caja", "Registrar aporte de efectivo", "Permite registrar dinero agregado físicamente a una caja abierta, indicando monto y motivo. No lo registra como venta ni ingreso comercial."),
        new(PermisoEmpleado.CAJA_HISTORIAL_VER, "Caja", "Ver historial de caja", "Permite consultar cierres por sesión y el resumen acumulado por día, en modo de solo lectura."),

        new(PermisoEmpleado.RESPALDO_CREAR, "Mantenimiento", "Crear respaldos", "Permite crear y validar respaldos de datos."),
        new(PermisoEmpleado.RESPALDO_CONFIGURAR, "Mantenimiento", "Configurar respaldos", "Permite cambiar frecuencia, destino y retencion de respaldos automaticos."),
        new(PermisoEmpleado.RESPALDO_RESTAURAR, "Mantenimiento", "Restaurar respaldos", "Permite preparar una restauracion protegida mediante reinicio.")
    ];

    public static IReadOnlySet<PermisoEmpleado> RecommendedExistingEmployeePermissions { get; } = new HashSet<PermisoEmpleado>
    {
        PermisoEmpleado.POS_ACCEDER,
        PermisoEmpleado.VENTA_REGISTRAR,
        PermisoEmpleado.DESCUENTO_LINEA_APLICAR,
        PermisoEmpleado.DESCUENTO_GENERAL_APLICAR,
        PermisoEmpleado.PAGO_TRANSFERENCIA_USAR,
        PermisoEmpleado.PAGO_BITCOIN_USAR,
        PermisoEmpleado.CORRECCIONES_ACCEDER,
        PermisoEmpleado.DEVOLUCION_REGISTRAR,
        PermisoEmpleado.CAMBIO_REGISTRAR,
        PermisoEmpleado.CAJA_ABRIR,
        PermisoEmpleado.CAJA_CERRAR,
        PermisoEmpleado.GASTO_REGISTRAR,
        PermisoEmpleado.RETIRO_REGISTRAR,
        PermisoEmpleado.CAJA_TOTAL_ESPERADO_VER,
        PermisoEmpleado.CAJA_DIFERENCIAS_VER
    };

    public static IReadOnlySet<PermisoEmpleado> RecommendedNewEmployeePermissions => RecommendedExistingEmployeePermissions;

    public static IReadOnlySet<PermisoEmpleado> SafeOperationalPermissions { get; } = new HashSet<PermisoEmpleado>
    {
        PermisoEmpleado.POS_ACCEDER,
        PermisoEmpleado.VENTA_REGISTRAR,
        PermisoEmpleado.DESCUENTO_LINEA_APLICAR,
        PermisoEmpleado.DESCUENTO_GENERAL_APLICAR,
        PermisoEmpleado.PAGO_TRANSFERENCIA_USAR,
        PermisoEmpleado.PAGO_BITCOIN_USAR,
        PermisoEmpleado.CORRECCIONES_ACCEDER,
        PermisoEmpleado.DEVOLUCION_REGISTRAR,
        PermisoEmpleado.CAMBIO_REGISTRAR
    };

    public static IReadOnlySet<PermisoEmpleado> AllPermissions { get; } =
        All.Select(p => p.Permiso).ToHashSet();

    public static IReadOnlyDictionary<PermisoEmpleado, IReadOnlySet<PermisoEmpleado>> Dependencies { get; } =
        new Dictionary<PermisoEmpleado, IReadOnlySet<PermisoEmpleado>>
        {
            [PermisoEmpleado.VENTA_REGISTRAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.POS_ACCEDER
            },
            [PermisoEmpleado.DESCUENTO_LINEA_APLICAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.POS_ACCEDER,
                PermisoEmpleado.VENTA_REGISTRAR
            },
            [PermisoEmpleado.DESCUENTO_GENERAL_APLICAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.POS_ACCEDER,
                PermisoEmpleado.VENTA_REGISTRAR
            },
            [PermisoEmpleado.PAGO_TRANSFERENCIA_USAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.POS_ACCEDER,
                PermisoEmpleado.VENTA_REGISTRAR
            },
            [PermisoEmpleado.PAGO_BITCOIN_USAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.POS_ACCEDER,
                PermisoEmpleado.VENTA_REGISTRAR
            },
            [PermisoEmpleado.COMPROBANTE_REIMPRIMIR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.HISTORIAL_VENTAS_VER
            },
            [PermisoEmpleado.DEVOLUCION_REGISTRAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.CORRECCIONES_ACCEDER
            },
            [PermisoEmpleado.CAMBIO_REGISTRAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.CORRECCIONES_ACCEDER
            },
            [PermisoEmpleado.CLIENTE_CREAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.CLIENTES_ACCEDER
            },
            [PermisoEmpleado.CLIENTE_EDITAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.CLIENTES_ACCEDER
            },
            [PermisoEmpleado.CLIENTE_HISTORIAL_VER] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.CLIENTES_ACCEDER
            },
            [PermisoEmpleado.CUENTAS_COBRAR_VER] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.CLIENTES_ACCEDER
            },
            [PermisoEmpleado.CREDITO_REGISTRAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.CLIENTES_ACCEDER
            },
            [PermisoEmpleado.ABONO_REGISTRAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.CLIENTES_ACCEDER,
                PermisoEmpleado.CUENTAS_COBRAR_VER
            },
            [PermisoEmpleado.ALERTAS_CREDITO_VER] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.CLIENTES_ACCEDER
            },
            [PermisoEmpleado.PROVEEDOR_CREAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PROVEEDORES_ACCEDER
            },
            [PermisoEmpleado.PROVEEDOR_EDITAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PROVEEDORES_ACCEDER
            },
            [PermisoEmpleado.PROVEEDOR_HISTORIAL_VER] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PROVEEDORES_ACCEDER
            },
            [PermisoEmpleado.PROVEEDOR_PRODUCTOS_GESTIONAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PROVEEDORES_ACCEDER
            },
            [PermisoEmpleado.COMPRA_CREAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.COMPRAS_ACCEDER
            },
            [PermisoEmpleado.COMPRA_EDITAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.COMPRAS_ACCEDER
            },
            [PermisoEmpleado.COMPRA_RECEPCIONAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.COMPRAS_ACCEDER
            },
            [PermisoEmpleado.COMPRA_HISTORIAL_VER] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.COMPRAS_ACCEDER
            },
            [PermisoEmpleado.COSTOS_VER] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.COMPRAS_ACCEDER
            },
            [PermisoEmpleado.CUENTAS_PAGAR_ACCEDER] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.COMPRAS_ACCEDER
            },
            [PermisoEmpleado.PAGO_COMPRA_REGISTRAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.CUENTAS_PAGAR_ACCEDER
            },
            [PermisoEmpleado.ABONO_PROVEEDOR_REGISTRAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.CUENTAS_PAGAR_ACCEDER
            },
            [PermisoEmpleado.PAGOS_PROVEEDOR_HISTORIAL_VER] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.CUENTAS_PAGAR_ACCEDER
            },
            [PermisoEmpleado.FINANZAS_PROVEEDOR_VER] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PROVEEDORES_ACCEDER,
                PermisoEmpleado.CUENTAS_PAGAR_ACCEDER
            },
            [PermisoEmpleado.COTIZACION_CREAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.COTIZACIONES_ACCEDER
            },
            [PermisoEmpleado.COTIZACION_EDITAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.COTIZACIONES_ACCEDER
            },
            [PermisoEmpleado.COTIZACION_EMITIR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.COTIZACIONES_ACCEDER
            },
            [PermisoEmpleado.COTIZACION_HISTORIAL_VER] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.COTIZACIONES_ACCEDER
            },
            [PermisoEmpleado.COTIZACION_CANCELAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.COTIZACIONES_ACCEDER
            },
            [PermisoEmpleado.PEDIDO_CREAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PEDIDOS_ACCEDER
            },
            [PermisoEmpleado.PEDIDO_EDITAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PEDIDOS_ACCEDER
            },
            [PermisoEmpleado.PEDIDO_CONFIRMAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PEDIDOS_ACCEDER
            },
            [PermisoEmpleado.PEDIDO_ESTADO_CAMBIAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PEDIDOS_ACCEDER
            },
            [PermisoEmpleado.PEDIDO_HISTORIAL_VER] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PEDIDOS_ACCEDER
            },
            [PermisoEmpleado.PEDIDO_CANCELAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PEDIDOS_ACCEDER
            },
            [PermisoEmpleado.PEDIDO_PAGO_REGISTRAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PEDIDOS_ACCEDER,
                PermisoEmpleado.PEDIDO_FINANZAS_VER
            },
            [PermisoEmpleado.PEDIDO_PAGOS_HISTORIAL_VER] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PEDIDOS_ACCEDER,
                PermisoEmpleado.PEDIDO_FINANZAS_VER
            },
            [PermisoEmpleado.PEDIDO_REEMBOLSO_REGISTRAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PEDIDOS_ACCEDER,
                PermisoEmpleado.PEDIDO_FINANZAS_VER
            },
            [PermisoEmpleado.PEDIDO_CONVERTIR_VENTA] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PEDIDOS_ACCEDER,
                PermisoEmpleado.PEDIDO_FINANZAS_VER
            },
            [PermisoEmpleado.PEDIDO_RESERVA_GESTIONAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PEDIDOS_ACCEDER
            },
            [PermisoEmpleado.PEDIDO_FINANZAS_VER] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PEDIDOS_ACCEDER
            },
            [PermisoEmpleado.MERMA_REGISTRAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.INVENTARIO_GESTIONAR
            },
            [PermisoEmpleado.PRODUCTOS_COSTO_COMPRA_EDITAR] = new HashSet<PermisoEmpleado>
            {
                PermisoEmpleado.PRODUCTOS_GESTIONAR
            }
        };

    public static IReadOnlyDictionary<PermisoEmpleado, IReadOnlySet<PermisoEmpleado>> Dependents { get; } =
        Dependencies
            .SelectMany(pair => pair.Value.Select(parent => new { Parent = parent, Child = pair.Key }))
            .GroupBy(pair => pair.Parent)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlySet<PermisoEmpleado>)group.Select(pair => pair.Child).ToHashSet());

    public static IReadOnlyDictionary<PermisoEmpleado, bool> BuildTemplateState(
        IReadOnlySet<PermisoEmpleado> enabledPermissions)
    {
        var states = AllPermissions.ToDictionary(permission => permission, _ => false);
        foreach (var permission in enabledPermissions)
        {
            EnableWithDependencies(states, permission);
        }

        return states;
    }

    public static void EnableWithDependencies(
        IDictionary<PermisoEmpleado, bool> states,
        PermisoEmpleado permission)
    {
        EnableWithDependencies(states, permission, []);
    }

    public static void DisableWithDependents(
        IDictionary<PermisoEmpleado, bool> states,
        PermisoEmpleado permission)
    {
        DisableWithDependents(states, permission, []);
    }

    public static IReadOnlySet<PermisoEmpleado> GetTransitiveDependencies(PermisoEmpleado permission)
    {
        var result = new HashSet<PermisoEmpleado>();
        CollectDependencies(permission, result);
        return result;
    }

    public static IReadOnlySet<PermisoEmpleado> GetTransitiveDependents(PermisoEmpleado permission)
    {
        var result = new HashSet<PermisoEmpleado>();
        CollectDependents(permission, result);
        return result;
    }

    public static bool TryFindMissingDependency(
        IReadOnlyDictionary<PermisoEmpleado, bool> states,
        out PermisoEmpleado permission,
        out PermisoEmpleado requiredPermission)
    {
        foreach (var (candidate, requiredPermissions) in Dependencies)
        {
            if (!states.GetValueOrDefault(candidate))
            {
                continue;
            }

            foreach (var required in requiredPermissions)
            {
                if (!states.GetValueOrDefault(required))
                {
                    permission = candidate;
                    requiredPermission = required;
                    return true;
                }
            }
        }

        permission = default;
        requiredPermission = default;
        return false;
    }

    public static IReadOnlyList<string> ValidateStateCombination(
        IReadOnlyDictionary<PermisoEmpleado, bool> states)
    {
        var errors = new List<string>();
        foreach (var permission in AllPermissions)
        {
            if (!states.ContainsKey(permission))
            {
                errors.Add($"Falta el permiso {permission}.");
            }
        }

        foreach (var permission in states.Keys)
        {
            if (!IsAssignable(permission))
            {
                errors.Add($"El permiso {permission} no es asignable.");
            }
        }

        foreach (var (permission, requiredPermissions) in Dependencies)
        {
            if (!states.GetValueOrDefault(permission))
            {
                continue;
            }

            foreach (var requiredPermission in requiredPermissions)
            {
                if (!states.GetValueOrDefault(requiredPermission))
                {
                    errors.Add($"{permission} requiere {requiredPermission}.");
                }
            }
        }

        return errors;
    }

    public static EmployeePermissionDefinition GetDefinition(PermisoEmpleado permiso)
    {
        return All.FirstOrDefault(p => p.Permiso == permiso)
            ?? throw new ArgumentException("El permiso no forma parte del catalogo operativo.", nameof(permiso));
    }

    public static bool IsAssignable(PermisoEmpleado permiso)
    {
        return AllPermissions.Contains(permiso);
    }

    private static void EnableWithDependencies(
        IDictionary<PermisoEmpleado, bool> states,
        PermisoEmpleado permission,
        HashSet<PermisoEmpleado> visited)
    {
        if (!visited.Add(permission))
        {
            return;
        }

        if (Dependencies.TryGetValue(permission, out var dependencies))
        {
            foreach (var dependency in dependencies)
            {
                EnableWithDependencies(states, dependency, visited);
            }
        }

        states[permission] = true;
    }

    private static void DisableWithDependents(
        IDictionary<PermisoEmpleado, bool> states,
        PermisoEmpleado permission,
        HashSet<PermisoEmpleado> visited)
    {
        if (!visited.Add(permission))
        {
            return;
        }

        states[permission] = false;
        if (!Dependents.TryGetValue(permission, out var dependents))
        {
            return;
        }

        foreach (var dependent in dependents)
        {
            DisableWithDependents(states, dependent, visited);
        }
    }

    private static void CollectDependencies(
        PermisoEmpleado permission,
        HashSet<PermisoEmpleado> result)
    {
        if (!Dependencies.TryGetValue(permission, out var dependencies))
        {
            return;
        }

        foreach (var dependency in dependencies)
        {
            if (result.Add(dependency))
            {
                CollectDependencies(dependency, result);
            }
        }
    }

    private static void CollectDependents(
        PermisoEmpleado permission,
        HashSet<PermisoEmpleado> result)
    {
        if (!Dependents.TryGetValue(permission, out var dependents))
        {
            return;
        }

        foreach (var dependent in dependents)
        {
            if (result.Add(dependent))
            {
                CollectDependents(dependent, result);
            }
        }
    }
}
