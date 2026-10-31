using System.Diagnostics;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class EmployeePermissionsForm : Form
{
    private static readonly string[] GroupOrder =
    [
        "Punto de venta",
        "Devoluciones y cambios",
        "Historiales",
        "Clientes y creditos",
        "Proveedores y artesanos",
        "Compras",
        "Finanzas de proveedores",
        "Cotizaciones",
        "Pedidos personalizados",
        "Operaciones comerciales",
        "Administracion",
        "Inventario",
        "Caja",
        "Mantenimiento"
    ];

    private static readonly IReadOnlyDictionary<PermisoEmpleado, string> PermissionLabels =
        new Dictionary<PermisoEmpleado, string>
        {
            [PermisoEmpleado.POS_ACCEDER] = "Acceder al punto de venta",
            [PermisoEmpleado.VENTA_REGISTRAR] = "Registrar ventas",
            [PermisoEmpleado.DESCUENTO_LINEA_APLICAR] = "Aplicar descuento por producto",
            [PermisoEmpleado.DESCUENTO_GENERAL_APLICAR] = "Aplicar descuento general",
            [PermisoEmpleado.PAGO_TRANSFERENCIA_USAR] = "Usar transferencia",
            [PermisoEmpleado.PAGO_BITCOIN_USAR] = "Usar Bitcoin",
            [PermisoEmpleado.COMPROBANTE_REIMPRIMIR] = "Reimprimir comprobantes",
            [PermisoEmpleado.HISTORIAL_VENTAS_VER] = "Ver historial de ventas",
            [PermisoEmpleado.VENTAS_PROPIAS_DIA_VER] = "Ver mis ventas de hoy",
            [PermisoEmpleado.HISTORIAL_DEVOLUCIONES_VER] = "Ver historial de devoluciones",
            [PermisoEmpleado.CORRECCIONES_ACCEDER] = "Acceder a devoluciones y cambios",
            [PermisoEmpleado.DEVOLUCION_REGISTRAR] = "Registrar devoluciones",
            [PermisoEmpleado.CAMBIO_REGISTRAR] = "Registrar cambios",
            [PermisoEmpleado.CLIENTES_ACCEDER] = "Acceder al modulo de clientes",
            [PermisoEmpleado.CLIENTE_CREAR] = "Crear clientes",
            [PermisoEmpleado.CLIENTE_EDITAR] = "Editar datos basicos",
            [PermisoEmpleado.CLIENTE_HISTORIAL_VER] = "Ver historial del cliente",
            [PermisoEmpleado.CUENTAS_COBRAR_VER] = "Ver cuentas por cobrar",
            [PermisoEmpleado.CREDITO_REGISTRAR] = "Registrar ventas a credito",
            [PermisoEmpleado.ABONO_REGISTRAR] = "Registrar abonos",
            [PermisoEmpleado.ALERTAS_CREDITO_VER] = "Ver alertas de credito",
            [PermisoEmpleado.PROVEEDORES_ACCEDER] = "Acceder al modulo",
            [PermisoEmpleado.PROVEEDOR_CREAR] = "Crear proveedores o artesanos",
            [PermisoEmpleado.PROVEEDOR_EDITAR] = "Editar datos basicos",
            [PermisoEmpleado.PROVEEDOR_HISTORIAL_VER] = "Ver historial",
            [PermisoEmpleado.PROVEEDOR_PRODUCTOS_GESTIONAR] = "Gestionar productos asociados",
            [PermisoEmpleado.COMPRAS_ACCEDER] = "Acceder al modulo de compras",
            [PermisoEmpleado.COMPRA_CREAR] = "Registrar compra",
            [PermisoEmpleado.COMPRA_EDITAR] = "Editar compra sin recepciones",
            [PermisoEmpleado.COMPRA_RECEPCIONAR] = "Registrar recepcion",
            [PermisoEmpleado.COMPRA_HISTORIAL_VER] = "Ver historial de compras",
            [PermisoEmpleado.COSTOS_VER] = "Ver costos",
            [PermisoEmpleado.INVENTARIO_GESTIONAR] = "Gestionar inventario",
            [PermisoEmpleado.MERMA_REGISTRAR] = "Registrar mermas",
            [PermisoEmpleado.PRODUCTOS_GESTIONAR] = "Gestionar productos",
            [PermisoEmpleado.ETIQUETAS_GESTIONAR] = "Gestionar etiquetas",
            [PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR] = "Gestionar imágenes de productos",
            [PermisoEmpleado.PRODUCTOS_COSTO_COMPRA_EDITAR] = "Editar costo de compra",
            [PermisoEmpleado.CAJA_ABRIR] = "Abrir caja",
            [PermisoEmpleado.CAJA_CERRAR] = "Cerrar caja",
            [PermisoEmpleado.GASTO_REGISTRAR] = "Registrar gastos",
            [PermisoEmpleado.RETIRO_REGISTRAR] = "Registrar retiros",
            [PermisoEmpleado.CAJA_TOTAL_ESPERADO_VER] = "Ver total esperado",
            [PermisoEmpleado.CAJA_DIFERENCIAS_VER] = "Ver diferencias de caja",
            [PermisoEmpleado.CAJA_APORTE_REGISTRAR] = "Registrar aporte de efectivo",
            [PermisoEmpleado.CAJA_HISTORIAL_VER] = "Ver historial de caja"
        };

    private static readonly IReadOnlyDictionary<PermisoEmpleado, string> PermissionHelp =
        new Dictionary<PermisoEmpleado, string>
        {
            [PermisoEmpleado.POS_ACCEDER] = "Permite abrir el punto de venta.",
            [PermisoEmpleado.VENTA_REGISTRAR] = "Permite guardar ventas cuando la caja y el pago sean validos.",
            [PermisoEmpleado.DESCUENTO_LINEA_APLICAR] = "Permite aplicar descuentos a productos individuales.",
            [PermisoEmpleado.DESCUENTO_GENERAL_APLICAR] = "Permite aplicar descuentos al total de la venta.",
            [PermisoEmpleado.PAGO_TRANSFERENCIA_USAR] = "Permite registrar pagos por transferencia con referencia.",
            [PermisoEmpleado.PAGO_BITCOIN_USAR] = "Permite registrar pagos por Bitcoin con referencia.",
            [PermisoEmpleado.COMPROBANTE_REIMPRIMIR] = "Permite reimprimir tickets, recibos y comprobantes de compra ya guardados.",
            [PermisoEmpleado.HISTORIAL_VENTAS_VER] = "Permite consultar el historial de ventas.",
            [PermisoEmpleado.VENTAS_PROPIAS_DIA_VER] = "Permite consultar solo tus ventas del día empresarial actual. No muestra ventas de otros usuarios ni permite editar, devolver o reimprimir.",
            [PermisoEmpleado.HISTORIAL_DEVOLUCIONES_VER] = "Permite consultar devoluciones, cambios y reembolsos registrados.",
            [PermisoEmpleado.CORRECCIONES_ACCEDER] = "Permite abrir el panel de devoluciones y cambios.",
            [PermisoEmpleado.DEVOLUCION_REGISTRAR] = "Permite procesar devoluciones normales. No concede permiso para anular ventas.",
            [PermisoEmpleado.CAMBIO_REGISTRAR] = "Permite registrar cambios de producto dentro de las reglas operativas.",
            [PermisoEmpleado.CLIENTES_ACCEDER] = "Permite abrir y consultar el modulo de clientes.",
            [PermisoEmpleado.CLIENTE_CREAR] = "Permite registrar nuevos clientes.",
            [PermisoEmpleado.CLIENTE_EDITAR] = "Permite editar datos basicos de clientes.",
            [PermisoEmpleado.CLIENTE_HISTORIAL_VER] = "Permite consultar compras asociadas al cliente.",
            [PermisoEmpleado.CUENTAS_COBRAR_VER] = "Preparado para consultar cuentas por cobrar.",
            [PermisoEmpleado.CREDITO_REGISTRAR] = "Permite crear creditos unicamente dentro de las politicas y limites configurados.",
            [PermisoEmpleado.ABONO_REGISTRAR] = "Permite registrar abonos a cuentas por cobrar.",
            [PermisoEmpleado.ALERTAS_CREDITO_VER] = "Permite revisar alertas internas de credito.",
            [PermisoEmpleado.PROVEEDORES_ACCEDER] = "Permite abrir y consultar proveedores y artesanos.",
            [PermisoEmpleado.PROVEEDOR_CREAR] = "Permite registrar nuevos proveedores o artesanos.",
            [PermisoEmpleado.PROVEEDOR_EDITAR] = "Permite modificar datos basicos de proveedores y artesanos.",
            [PermisoEmpleado.PROVEEDOR_HISTORIAL_VER] = "Permite consultar historial de cambios de proveedores.",
            [PermisoEmpleado.PROVEEDOR_PRODUCTOS_GESTIONAR] = "Permite asociar o desasociar productos existentes.",
            [PermisoEmpleado.COMPRAS_ACCEDER] = "Permite abrir y consultar compras a proveedores.",
            [PermisoEmpleado.COMPRA_CREAR] = "Permite registrar compras sin afectar inventario hasta recibir.",
            [PermisoEmpleado.COMPRA_EDITAR] = "Permite editar compras que aun no tienen recepciones.",
            [PermisoEmpleado.COMPRA_RECEPCIONAR] = "Permite recibir productos y aumentar inventario.",
            [PermisoEmpleado.COMPRA_HISTORIAL_VER] = "Permite consultar compras, recepciones y trazabilidad.",
            [PermisoEmpleado.COSTOS_VER] = "Permite ver costos unitarios, promedio e historial de costos.",
            [PermisoEmpleado.INVENTARIO_GESTIONAR] = "Permite transferir y ajustar inventario por SKU y ubicación.",
            [PermisoEmpleado.MERMA_REGISTRAR] = "Permite registrar mermas con motivo y trazabilidad.",
            [PermisoEmpleado.PRODUCTOS_GESTIONAR] = "Permite crear y editar productos, SKU e imágenes administradas.",
            [PermisoEmpleado.ETIQUETAS_GESTIONAR] = "Permite generar códigos internos para productos existentes y previsualizar o imprimir etiquetas. No permite modificar productos ni inventario.",
            [PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR] = "Permite importar, marcar como principal y eliminar imágenes de productos o SKU. No permite editar datos, inventario ni códigos de barras.",
            [PermisoEmpleado.PRODUCTOS_COSTO_COMPRA_EDITAR] = "Permite asignar o cambiar manualmente el costo base del producto y los costos de SKU.",
            [PermisoEmpleado.CAJA_ABRIR] = "Permite abrir la caja diaria.",
            [PermisoEmpleado.CAJA_CERRAR] = "Permite cerrar la caja diaria.",
            [PermisoEmpleado.GASTO_REGISTRAR] = "Permite registrar gastos de caja.",
            [PermisoEmpleado.RETIRO_REGISTRAR] = "Permite registrar retiros de efectivo.",
            [PermisoEmpleado.CAJA_TOTAL_ESPERADO_VER] = "Permite ver el total esperado al revisar caja.",
            [PermisoEmpleado.CAJA_DIFERENCIAS_VER] = "Permite ver diferencias de caja.",
            [PermisoEmpleado.CAJA_APORTE_REGISTRAR] = "Permite registrar un aporte físico a una caja abierta con monto y motivo obligatorios; no crea ventas ni ingresos comerciales.",
            [PermisoEmpleado.CAJA_HISTORIAL_VER] = "Permite consultar importes y cierres de todas las sesiones de caja y el resumen diario acumulado. Es solo lectura; no permite abrir, cerrar ni modificar movimientos."
        };

    private readonly IUserService _userService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly ComboBox _cmbEmployees = new();
    private readonly FlowLayoutPanel _toolbarFlow = new();
    private readonly TableLayoutPanel _employeeField = new();
    private readonly FlowLayoutPanel _permissionsFlow = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnRestore = new();
    private readonly Button _btnSafe = new();
    private readonly Button _btnRefresh = new();
    private readonly Label _lblStatus = new();
    private readonly Label _lblHelp = new();
    private readonly ToolTip _toolTip = new();
    private readonly Dictionary<PermisoEmpleado, CheckBox> _permissionChecks = [];
    private readonly Dictionary<PermisoEmpleado, TableLayoutPanel> _permissionCardsByPermission = [];
    private readonly Dictionary<string, Label> _permissionCountLabels = [];
    private readonly Dictionary<string, IReadOnlyList<EmployeePermissionDefinition>> _permissionDefinitionsByGroup = [];
    private readonly Dictionary<PermisoEmpleado, bool> _persistedStates = [];
    private readonly List<TableLayoutPanel> _permissionCards = [];
    private List<UserSummary> _employees = [];
    private int? _currentEmployeeId;
    private bool _loading;
    private bool _dirty;
    private bool _suppressCheckEvents;
    private bool _revertingEmployeeSelection;

    public EmployeePermissionsForm(
        IUserService userService,
        IEmployeePermissionService permissionService)
    {
        _userService = userService;
        _permissionService = permissionService;

        BuildUi();
        Load += async (_, _) => await LoadEmployeesAsync();
        Shown += (_, _) => UpdateResponsiveWidths();
        Resize += (_, _) => UpdateResponsiveWidths();
        FormClosing += EmployeePermissionsForm_FormClosing;
    }

    private void BuildUi()
    {
        Text = "Permisos de empleados";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 740);
        MinimumSize = new Size(980, 620);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(24, 18, 24, 18),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));

        var header = CreateHeader();
        var toolbar = CreateToolbar();

        _permissionsFlow.Dock = DockStyle.Fill;
        _permissionsFlow.AutoScroll = true;
        _permissionsFlow.WrapContents = true;
        _permissionsFlow.FlowDirection = FlowDirection.LeftToRight;
        _permissionsFlow.BackColor = UITheme.BackgroundColor;
        _permissionsFlow.Padding = new Padding(0, 2, 4, 0);
        _permissionsFlow.Margin = new Padding(0, 4, 0, 0);
        _permissionsFlow.SizeChanged += (_, _) => UpdateResponsiveWidths();
        BuildPermissionGroups();

        var footer = CreateFooter();

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(toolbar, 0, 1);
        root.Controls.Add(_permissionsFlow, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);
    }

    private static Control CreateHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 14),
            BackColor = UITheme.BackgroundColor
        };
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Permisos de empleados",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 4)
        };
        UITheme.StyleLabelTitle(title);

        var description = new Label
        {
            Text = "Configure las funciones disponibles para cada empleado.",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor
        };

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(description, 0, 1);
        return header;
    }

    private Control CreateToolbar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 1,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14, 12, 14, 10),
            Margin = new Padding(0, 0, 0, 14)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _toolbarFlow.Dock = DockStyle.Fill;
        _toolbarFlow.AutoSize = true;
        _toolbarFlow.WrapContents = true;
        _toolbarFlow.FlowDirection = FlowDirection.LeftToRight;
        _toolbarFlow.Margin = new Padding(0);
        _toolbarFlow.Padding = new Padding(0);
        _toolbarFlow.BackColor = UITheme.SurfaceColor;

        ConfigureEmployeeField();
        ConfigureButton(_btnRefresh, "Refrescar", 132, async (_, _) => await RefreshAsync(), UITheme.StyleSecondaryButton);
        ConfigureButton(_btnRestore, "Restaurar recomendados", 232, (_, _) => ApplyRecommendedTemplate(), UITheme.StyleSecondaryButton);
        ConfigureButton(_btnSafe, "Permisos operativos seguros", 256, (_, _) => ApplySafeTemplate(), UITheme.StyleSecondaryButton);

        _toolbarFlow.Controls.Add(_employeeField);
        _toolbarFlow.Controls.Add(CreateToolbarButtonField(_btnRefresh));
        _toolbarFlow.Controls.Add(CreateToolbarButtonField(_btnRestore));
        _toolbarFlow.Controls.Add(CreateToolbarButtonField(_btnSafe));
        panel.Controls.Add(_toolbarFlow, 0, 0);

        return panel;
    }

    private Control CreateFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UITheme.BackgroundColor,
            Padding = new Padding(0, 12, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));

        var messages = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 14, 0)
        };
        messages.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        messages.RowStyles.Add(new RowStyle(SizeType.Percent, 52));

        _lblHelp.Dock = DockStyle.Fill;
        _lblHelp.TextAlign = ContentAlignment.BottomLeft;
        _lblHelp.ForeColor = UITheme.TextDark;
        _lblHelp.Font = UITheme.NormalFont;
        _lblHelp.Text = "Seleccione un permiso para ver su descripcion.";

        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.TopLeft;
        _lblStatus.ForeColor = UITheme.MutedTextColor;
        _lblStatus.Font = UITheme.NormalFont;

        ConfigureButton(_btnSave, "Guardar cambios", 190, async (_, _) => await SaveAsync(), UITheme.StyleSuccessButton);
        _btnSave.Size = new Size(190, 42);
        _btnSave.MinimumSize = new Size(190, 42);

        var savePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 18, 0, 16),
            Margin = new Padding(0)
        };
        savePanel.Controls.Add(_btnSave);

        messages.Controls.Add(_lblHelp, 0, 0);
        messages.Controls.Add(_lblStatus, 0, 1);
        footer.Controls.Add(messages, 0, 0);
        footer.Controls.Add(savePanel, 1, 0);

        return footer;
    }

    private void ConfigureEmployeeField()
    {
        _employeeField.Width = 430;
        _employeeField.Height = 66;
        _employeeField.ColumnCount = 1;
        _employeeField.RowCount = 2;
        _employeeField.Margin = new Padding(0, 0, 12, 8);
        _employeeField.BackColor = UITheme.SurfaceColor;
        _employeeField.RowStyles.Clear();
        _employeeField.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        _employeeField.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var employeeLabel = new Label
        {
            Text = "Empleado",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 0, 4)
        };

        _cmbEmployees.Dock = DockStyle.Fill;
        _cmbEmployees.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbEmployees.DisplayMember = nameof(EmployeeOption.Name);
        _cmbEmployees.ValueMember = nameof(EmployeeOption.Id);
        _cmbEmployees.Font = UITheme.NormalFont;
        _cmbEmployees.Margin = new Padding(0);
        _cmbEmployees.SelectedIndexChanged += async (_, _) => await EmployeeSelectionChangedAsync();

        _employeeField.Controls.Add(employeeLabel, 0, 0);
        _employeeField.Controls.Add(_cmbEmployees, 0, 1);
    }

    private void BuildPermissionGroups()
    {
        _permissionChecks.Clear();
        _permissionCardsByPermission.Clear();
        _permissionCountLabels.Clear();
        _permissionDefinitionsByGroup.Clear();
        _permissionCards.Clear();
        _permissionsFlow.Controls.Clear();

        var groupedDefinitions = EmployeePermissionCatalog.All
            .GroupBy(p => p.Grupo)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var groupName in GroupOrder)
        {
            if (!groupedDefinitions.TryGetValue(groupName, out var definitions))
            {
                continue;
            }

            var card = CreatePermissionCard(groupName, definitions);
            _permissionCards.Add(card);
            _permissionsFlow.Controls.Add(card);
        }
    }

    private TableLayoutPanel CreatePermissionCard(string groupName, IReadOnlyCollection<EmployeePermissionDefinition> definitions)
    {
            var card = new TableLayoutPanel
        {
            Width = 540,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = definitions.Count + 2,
            Padding = new Padding(18, 16, 18, 16),
            Margin = new Padding(0, 0, 16, 16),
            BackColor = UITheme.SurfaceColor
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Paint += (_, e) => ControlPaint.DrawBorder(
            e.Graphics,
            card.ClientRectangle,
            UITheme.BorderPinkColor,
            ButtonBorderStyle.Solid);

        _permissionDefinitionsByGroup[groupName] = definitions.ToList();

        var cardHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = UITheme.SurfaceColor
        };
        cardHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cardHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = GetGroupTitle(groupName),
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = Padding.Empty,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var count = new Label
        {
            Text = $"0 / {definitions.Count}",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(UITheme.SpaceSm, 0, 0, 0),
            Padding = new Padding(UITheme.SpaceSm, UITheme.SpaceXs, UITheme.SpaceSm, UITheme.SpaceXs),
            Font = UITheme.CaptionFont,
            ForeColor = UITheme.PrimaryColor,
            BackColor = UITheme.DashboardAccentSoftColor,
            TextAlign = ContentAlignment.MiddleCenter
        };
        _permissionCountLabels[groupName] = count;
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cardHeader.Controls.Add(title, 0, 0);
        cardHeader.Controls.Add(count, 1, 0);
        card.Controls.Add(cardHeader, 0, 0);

        var line = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 1,
            BackColor = UITheme.BorderPinkColor,
            Margin = new Padding(0, 0, 0, 10)
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 11));
        card.Controls.Add(line, 0, 1);

        var row = 2;
        foreach (var definition in definitions)
        {
            var permissionRow = CreatePermissionRow(definition);
            _permissionCardsByPermission[definition.Permiso] = card;
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(permissionRow, 0, row++);
        }

        return card;
    }

    private Control CreatePermissionRow(EmployeePermissionDefinition definition)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 4, 0, 4),
            BackColor = UITheme.SurfaceColor
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var check = new CheckBox
        {
            Dock = DockStyle.Top,
            Width = 22,
            Height = 24,
            Margin = new Padding(0, 1, 6, 0),
            Tag = definition.Permiso
        };
        check.CheckedChanged += PermissionCheck_CheckedChanged;

        var labelText = GetPermissionLabel(definition);
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Top,
            AutoSize = true,
            MinimumSize = new Size(0, 24),
            Margin = new Padding(0, 1, 0, 0),
            Font = UITheme.NormalFont,
            ForeColor = UITheme.TextDark,
            Cursor = Cursors.Hand
        };

        var help = GetPermissionHelp(definition);
        _toolTip.SetToolTip(check, help);
        _toolTip.SetToolTip(label, help);
        _toolTip.SetToolTip(row, help);
        AttachPermissionHelp(row, check, label, help);

        _permissionChecks[definition.Permiso] = check;
        row.Tag = label;
        row.Controls.Add(check, 0, 0);
        row.Controls.Add(label, 1, 0);
        return row;
    }

    private void AttachPermissionHelp(Control row, CheckBox check, Label label, string help)
    {
        void ShowHelp(object? _, EventArgs __) => _lblHelp.Text = help;

        row.MouseEnter += ShowHelp;
        check.MouseEnter += ShowHelp;
        check.Enter += ShowHelp;
        label.MouseEnter += ShowHelp;
        label.Click += (_, _) =>
        {
            if (check.Enabled)
            {
                check.Checked = !check.Checked;
            }
        };
    }

    private void PermissionCheck_CheckedChanged(object? sender, EventArgs e)
    {
        if (_loading || _suppressCheckEvents || sender is not CheckBox check || check.Tag is not PermisoEmpleado permission)
        {
            return;
        }

        if (check.Checked)
        {
            var states = GetCurrentStates();
            var dependencies = EmployeePermissionCatalog.GetTransitiveDependencies(permission)
                .Where(required => !states.GetValueOrDefault(required))
                .ToList();
            EmployeePermissionCatalog.EnableWithDependencies(states, permission);
            ApplyStatesToChecks(states);

            var message = dependencies.Count == 0
                ? null
                : $"Tambien se activo: {string.Join(", ", dependencies.Select(GetPermissionLabel))}.";
            MarkDirty(message);
            return;
        }

        var enabledDependents = EmployeePermissionCatalog.GetTransitiveDependents(permission)
                .Where(IsChecked)
                .ToList();

        if (enabledDependents.Count > 0 &&
            !ConfirmDisableDependents(permission, enabledDependents))
        {
            SetCheck(permission, true);
            return;
        }

        var currentStates = GetCurrentStates();
        EmployeePermissionCatalog.DisableWithDependents(currentStates, permission);
        ApplyStatesToChecks(currentStates);
        MarkDirty();
    }

    private async Task EmployeeSelectionChangedAsync()
    {
        if (_loading || _revertingEmployeeSelection)
        {
            return;
        }

        if (_dirty && !ConfirmDiscardChanges("Existen cambios sin guardar. Desea descartarlos?"))
        {
            RestoreCurrentEmployeeSelection();
            return;
        }

        await LoadSelectedEmployeePermissionsAsync();
    }

    private async Task RefreshAsync()
    {
        if (_dirty && !ConfirmDiscardChanges("Hay cambios sin guardar. Desea descartarlos y refrescar?"))
        {
            return;
        }

        await LoadEmployeesAsync(_currentEmployeeId);
    }

    private void ApplyRecommendedTemplate()
    {
        if (_cmbEmployees.SelectedItem is not EmployeeOption)
        {
            return;
        }

        ApplyStatesToChecks(EmployeePermissionCatalog.BuildTemplateState(EmployeePermissionCatalog.RecommendedNewEmployeePermissions));
        MarkDirty("Plantilla recomendada cargada. Presione Guardar cambios para persistir.");
    }

    private void ApplySafeTemplate()
    {
        if (_cmbEmployees.SelectedItem is not EmployeeOption)
        {
            return;
        }

        ApplyStatesToChecks(EmployeePermissionCatalog.BuildTemplateState(EmployeePermissionCatalog.SafeOperationalPermissions));
        MarkDirty("Permisos operativos seguros cargados. Presione Guardar cambios para persistir.");
    }

    private void EmployeePermissionsForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_dirty && !ConfirmDiscardChanges("Existen cambios sin guardar. Desea descartarlos?"))
        {
            e.Cancel = true;
        }
    }

    private void UpdateResponsiveWidths()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var toolbarWidth = Math.Max(360, _toolbarFlow.ClientSize.Width);
        var employeeWidth = toolbarWidth >= 1040
            ? Math.Max(330, toolbarWidth - 132 - 232 - 256 - 78)
            : Math.Min(540, Math.Max(330, toolbarWidth - 18));
        _employeeField.Width = employeeWidth;

        var available = Math.Max(420, _permissionsFlow.ClientSize.Width - _permissionsFlow.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4);
        var columns = available >= 1500
            ? 4
            : available >= 1050
                ? 3
                : available >= 700
                    ? 2
                    : 1;
        var spacing = 16 * (columns - 1);
        var cardWidth = Math.Max(390, (available - spacing) / columns);

        foreach (var card in _permissionCards)
        {
            card.Width = cardWidth;
            foreach (Control row in card.Controls)
            {
                if (row is TableLayoutPanel { Tag: Label label })
                {
                    label.MaximumSize = new Size(Math.Max(280, cardWidth - card.Padding.Horizontal - 44), 0);
                }
            }
        }
    }

    private static string GetGroupTitle(string groupName)
    {
        return groupName switch
        {
            "Clientes y creditos" => "Clientes y creditos",
            _ => groupName
        };
    }

    private static string GetPermissionLabel(EmployeePermissionDefinition definition)
    {
        var label = PermissionLabels.TryGetValue(definition.Permiso, out var customLabel)
            ? customLabel
            : definition.Nombre;

        return definition.FuncionFutura
            ? $"{label} (funcion futura)"
            : label;
    }

    private static string GetPermissionLabel(PermisoEmpleado permission)
    {
        return PermissionLabels.TryGetValue(permission, out var label)
            ? label
            : EmployeePermissionCatalog.GetDefinition(permission).Nombre;
    }

    private static string GetPermissionHelp(EmployeePermissionDefinition definition)
    {
        return PermissionHelp.TryGetValue(definition.Permiso, out var help)
            ? help
            : definition.Descripcion;
    }

    private async Task LoadEmployeesAsync(int? preferredEmployeeId = null)
    {
        try
        {
            SetBusy(true);
            _employees = (await _userService.GetAllAsync())
                .Where(u => u.Rol == RolUsuario.EMPLEADO && u.Activo)
                .OrderBy(u => u.NombreUsuario)
                .ToList();

            var employeeOptions = _employees
                .Select(u => new EmployeeOption(u.UsuarioId, $"{u.NombreCompleto} ({u.NombreUsuario})"))
                .ToList();
            var selectedId = preferredEmployeeId is not null && employeeOptions.Any(e => e.Id == preferredEmployeeId.Value)
                ? preferredEmployeeId.Value
                : employeeOptions.FirstOrDefault()?.Id;

            _loading = true;
            _cmbEmployees.DataSource = employeeOptions;
            if (selectedId is not null)
            {
                _cmbEmployees.SelectedIndex = employeeOptions.FindIndex(e => e.Id == selectedId.Value);
            }
            _loading = false;

            if (_employees.Count == 0)
            {
                _currentEmployeeId = null;
                _persistedStates.Clear();
                ClearChecks();
                _dirty = false;
                _lblStatus.Text = "No hay empleados disponibles para configurar.";
                return;
            }

            await LoadSelectedEmployeePermissionsAsync();
        }
        catch (Exception ex)
        {
            ShowFriendlyError(ex, "No se pudo cargar la informacion de permisos.");
        }
        finally
        {
            _loading = false;
            SetBusy(false);
        }
    }

    private async Task LoadSelectedEmployeePermissionsAsync()
    {
        if (_loading || _cmbEmployees.SelectedItem is not EmployeeOption option)
        {
            return;
        }

        try
        {
            SetBusy(true);
            await LoadPermissionsForOptionAsync(option, $"Permisos cargados para {option.Name}.");
        }
        catch (Exception ex)
        {
            ShowFriendlyError(ex, "No se pudo cargar la informacion de permisos.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadPermissionsForOptionAsync(EmployeeOption option, string statusMessage)
    {
        var states = await _permissionService.GetPermissionsForUserAsync(option.Id);
        var stateMap = states.ToDictionary(state => state.Permiso, state => state.Habilitado);

        _currentEmployeeId = option.Id;
        _persistedStates.Clear();
        foreach (var (permission, enabled) in stateMap)
        {
            _persistedStates[permission] = enabled;
        }

        var enabledPermissions = stateMap
            .Where(pair => pair.Value)
            .Select(pair => pair.Key)
            .ToHashSet();
        var normalizedStates = EmployeePermissionCatalog.BuildTemplateState(enabledPermissions);
        ApplyStatesToChecks(normalizedStates);
        _dirty = HasCurrentChanges(normalizedStates);
        _lblStatus.Text = _dirty
            ? "Se activaron accesos principales requeridos. Presione Guardar cambios para persistir."
            : statusMessage;
    }

    private async Task SaveAsync()
    {
        if (_cmbEmployees.SelectedItem is not EmployeeOption option)
        {
            return;
        }

        var currentStates = GetCurrentStates();
        if (!_dirty || !HasCurrentChanges(currentStates))
        {
            _dirty = false;
            _lblStatus.Text = "No hay cambios pendientes.";
            MessageBox.Show("No hay cambios pendientes.", "Permisos de empleados", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var validationErrors = EmployeePermissionCatalog.ValidateStateCombination(currentStates);
        if (validationErrors.Count > 0)
        {
            ShowInvalidCombination(currentStates);
            return;
        }

        if (currentStates.Values.All(enabled => !enabled) &&
            MessageBox.Show(
                "Este empleado quedara sin acceso a funciones operativas. Desea continuar?",
                "Permisos de empleados",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            SetBusy(true);
            var updates = currentStates
                .OrderBy(pair => pair.Key)
                .Select(pair => new EmployeePermissionUpdate(pair.Key, pair.Value))
                .ToList();

            await _permissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(option.Id, updates));
            await LoadPermissionsForOptionAsync(option, $"Permisos guardados correctamente para {option.Name}.");
            MessageBox.Show("Permisos guardados correctamente.", "Permisos de empleados", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "No se pudo guardar ningun cambio.";
            ShowFriendlyError(ex, GetFriendlySaveMessage(ex));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ClearChecks()
    {
        ApplyStatesToChecks(EmployeePermissionCatalog.AllPermissions.ToDictionary(permission => permission, _ => false));
    }

    private void ApplyStatesToChecks(IReadOnlyDictionary<PermisoEmpleado, bool> states)
    {
        _suppressCheckEvents = true;
        try
        {
            foreach (var (permission, check) in _permissionChecks)
            {
                check.Checked = states.GetValueOrDefault(permission);
            }
        }
        finally
        {
            _suppressCheckEvents = false;
        }

        foreach (var (groupName, definitions) in _permissionDefinitionsByGroup)
        {
            if (_permissionCountLabels.TryGetValue(groupName, out var count))
            {
                var enabled = definitions.Count(definition => states.GetValueOrDefault(definition.Permiso));
                count.Text = $"{enabled} / {definitions.Count}";
            }
        }
    }

    private Dictionary<PermisoEmpleado, bool> GetCurrentStates()
    {
        return _permissionChecks.ToDictionary(pair => pair.Key, pair => pair.Value.Checked);
    }

    private bool HasCurrentChanges(IReadOnlyDictionary<PermisoEmpleado, bool> currentStates)
    {
        return EmployeePermissionCatalog.AllPermissions.Any(permission =>
            currentStates.GetValueOrDefault(permission) != _persistedStates.GetValueOrDefault(permission));
    }

    private void MarkDirty(string? message = null)
    {
        if (_loading || _suppressCheckEvents)
        {
            return;
        }

        _dirty = true;
        _lblStatus.Text = message ?? "Cambios sin guardar.";
    }

    private bool IsChecked(PermisoEmpleado permission)
    {
        return _permissionChecks.TryGetValue(permission, out var check) && check.Checked;
    }

    private void SetCheck(PermisoEmpleado permission, bool value)
    {
        if (!_permissionChecks.TryGetValue(permission, out var check))
        {
            return;
        }

        _suppressCheckEvents = true;
        try
        {
            check.Checked = value;
        }
        finally
        {
            _suppressCheckEvents = false;
        }
    }

    private bool ConfirmDisableDependents(PermisoEmpleado permission, IReadOnlyList<PermisoEmpleado> dependents)
    {
        var dependentNames = string.Join(", ", dependents.Select(GetPermissionLabel));
        var message = permission switch
        {
            PermisoEmpleado.POS_ACCEDER =>
                "Al desactivar Acceder al punto de venta tambien se desactivaran Registrar ventas, descuentos y metodos de pago. Desea continuar?",
            PermisoEmpleado.CORRECCIONES_ACCEDER =>
                "Al desactivar Acceder a devoluciones y cambios tambien se desactivaran Registrar devoluciones y Registrar cambios. Desea continuar?",
            PermisoEmpleado.CLIENTES_ACCEDER =>
                "Al desactivar Acceder al modulo de clientes tambien se desactivaran sus permisos dependientes. Desea continuar?",
            PermisoEmpleado.PROVEEDORES_ACCEDER =>
                "Al desactivar Acceder al modulo de proveedores tambien se desactivaran sus permisos dependientes. Desea continuar?",
            PermisoEmpleado.CUENTAS_PAGAR_ACCEDER =>
                "Al quitar el acceso a Cuentas por pagar tambien se desactivaran sus funciones relacionadas. Desea continuar?",
            _ =>
                $"Al desactivar {GetPermissionLabel(permission)} tambien se desactivaran: {dependentNames}. Desea continuar?"
        };

        return MessageBox.Show(
            message,
            "Permisos de empleados",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private bool ConfirmDiscardChanges(string message)
    {
        return MessageBox.Show(
            message,
            "Permisos de empleados",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private void RestoreCurrentEmployeeSelection()
    {
        if (_currentEmployeeId is null)
        {
            return;
        }

        _revertingEmployeeSelection = true;
        try
        {
            for (var i = 0; i < _cmbEmployees.Items.Count; i++)
            {
                if (_cmbEmployees.Items[i] is EmployeeOption option && option.Id == _currentEmployeeId.Value)
                {
                    _cmbEmployees.SelectedIndex = i;
                    return;
                }
            }
        }
        finally
        {
            _revertingEmployeeSelection = false;
        }
    }

    private void SetBusy(bool busy)
    {
        var hasEmployees = _employees.Count > 0;
        _btnSave.Enabled = !busy && hasEmployees;
        _btnRestore.Enabled = !busy && hasEmployees;
        _btnSafe.Enabled = !busy && hasEmployees;
        _btnRefresh.Enabled = !busy;
        _cmbEmployees.Enabled = !busy && hasEmployees;
        foreach (var check in _permissionChecks.Values)
        {
            check.Enabled = !busy && hasEmployees;
        }
    }

    private static void ConfigureButton(
        Button button,
        string text,
        int width,
        EventHandler clickHandler,
        Action<Button> style)
    {
        button.Text = text;
        button.Size = new Size(width, 38);
        button.MinimumSize = new Size(width, 38);
        button.Margin = new Padding(0);
        button.Click += clickHandler;
        style(button);
    }

    private static Control CreateToolbarButtonField(Button button)
    {
        var panel = new Panel
        {
            Width = button.Width,
            Height = 66,
            Margin = new Padding(0, 20, 12, 8),
            BackColor = UITheme.SurfaceColor
        };
        button.Dock = DockStyle.Top;
        panel.Controls.Add(button);
        return panel;
    }

    private static void ShowFriendlyError(Exception ex, string userMessage)
    {
        Debug.WriteLine(ex.ToString());
        MessageBox.Show(userMessage, "Permisos de empleados", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static string GetFriendlySaveMessage(Exception ex)
    {
        var message = ex.Message;
        if (message.Contains("ya no esta activo", StringComparison.OrdinalIgnoreCase))
        {
            return "El empleado seleccionado ya no esta activo.";
        }

        if (message.Contains("combinacion", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("requiere", StringComparison.OrdinalIgnoreCase))
        {
            return message.Contains(nameof(PermisoEmpleado.CUENTAS_PAGAR_ACCEDER), StringComparison.OrdinalIgnoreCase)
                ? "Para utilizar funciones de pagos o abonos a proveedores debe activar \"Acceder a cuentas por pagar\"."
                : "La combinacion de permisos no es valida. Active primero el acceso al modulo correspondiente.";
        }

        return "No se pudo guardar ningun cambio.";
    }

    private void ShowInvalidCombination(IReadOnlyDictionary<PermisoEmpleado, bool> states)
    {
        if (!EmployeePermissionCatalog.TryFindMissingDependency(states, out var permission, out var required))
        {
            MessageBox.Show(
                "La combinacion de permisos no es valida. Revise los accesos principales de cada modulo.",
                "Permisos de empleados",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var message = required == PermisoEmpleado.CUENTAS_PAGAR_ACCEDER
            ? "Para utilizar funciones de pagos o abonos a proveedores debe activar \"Acceder a cuentas por pagar\"."
            : $"Para utilizar \"{GetPermissionLabel(permission)}\" debe activar \"{GetPermissionLabel(required)}\".";
        HighlightPermission(required, message);
        MessageBox.Show(message, "Permisos de empleados", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void HighlightPermission(PermisoEmpleado permission, string help)
    {
        if (!_permissionChecks.TryGetValue(permission, out var check) ||
            !_permissionCardsByPermission.TryGetValue(permission, out var card))
        {
            return;
        }

        _permissionsFlow.ScrollControlIntoView(card);
        card.BackColor = UITheme.SoftPinkColor;
        check.Focus();
        _lblHelp.Text = help;
        _toolTip.Show(help, check, 0, check.Height + 4, 3000);

        var timer = new System.Windows.Forms.Timer { Interval = 2200 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            if (!card.IsDisposed)
            {
                card.BackColor = UITheme.SurfaceColor;
                card.Invalidate();
            }
        };
        timer.Start();
    }

    private sealed record EmployeeOption(int Id, string Name);
}
