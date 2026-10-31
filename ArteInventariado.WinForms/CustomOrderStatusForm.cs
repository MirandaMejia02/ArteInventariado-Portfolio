using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public sealed class CustomOrderStatusForm : Form
{
    private readonly ComboBox _states = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly DateTimePicker _date = new() { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm" };
    private readonly TextBox _reason = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        MinimumSize = new Size(0, 64)
    };
    private readonly Label _warning = new() { AutoSize = true, ForeColor = UITheme.DangerColor };
    private readonly EstadoPedidoPersonalizado _current;

    public CustomOrderStateChangeRequest Request(int orderId) => new(
        orderId, (EstadoPedidoPersonalizado)_states.SelectedItem!, _date.Value, _reason.Text);

    public CustomOrderStatusForm(
        string code,
        EstadoPedidoPersonalizado current,
        bool allowOperationalChange,
        bool allowCancel)
    {
        _current = current;
        Text = $"Cambiar estado - {code}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 460);
        MinimumSize = new Size(520, 420);
        UITheme.StyleForm(this);
        BuildUi(code, allowOperationalChange, allowCancel);
    }

    private void BuildUi(string code, bool allowOperationalChange, bool allowCancel)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 9
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var title = new Label { Text = $"Pedido {code}", AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        UITheme.StyleLabelTitle(title);
        layout.Controls.Add(title);
        layout.Controls.Add(new Label { Text = $"Estado actual: {Friendly(_current)}", AutoSize = true, Margin = new Padding(0, 0, 0, 14) });
        layout.Controls.Add(new Label { Text = "Nuevo estado", AutoSize = true });
        layout.Controls.Add(_states);
        layout.Controls.Add(new Label { Text = "Fecha efectiva", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        layout.Controls.Add(_date);
        layout.Controls.Add(new Label { Text = "Motivo u observacion", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        var reasonPanel = new Panel { Dock = DockStyle.Fill };
        _reason.Dock = DockStyle.Fill;
        _warning.Dock = DockStyle.Bottom;
        reasonPanel.Controls.Add(_reason);
        reasonPanel.Controls.Add(_warning);
        layout.Controls.Add(reasonPanel);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 6, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var cancel = new Button { Text = "Volver", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0), DialogResult = DialogResult.Cancel };
        var accept = new Button { Text = "Aplicar cambio", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        UITheme.StyleSecondaryButton(cancel);
        UITheme.StyleSuccessButton(accept);
        accept.Click += (_, _) => ValidateAndAccept();
        buttons.Controls.Add(cancel, 0, 0);
        buttons.Controls.Add(accept, 1, 0);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        var allowed = AllowedTargets(_current)
            .Where(s => s == EstadoPedidoPersonalizado.CANCELADO ? allowCancel : allowOperationalChange)
            .ToArray();
        _states.DataSource = allowed;
        _states.Format += (_, e) => e.Value = Friendly((EstadoPedidoPersonalizado)e.ListItem!);
        _states.SelectedIndexChanged += (_, _) => UpdateWarning();
        AcceptButton = accept;
        CancelButton = cancel;
        UpdateWarning();
    }

    private void UpdateWarning()
    {
        if (_states.SelectedItem is not EstadoPedidoPersonalizado state) return;
        _warning.Text = state switch
        {
            EstadoPedidoPersonalizado.CANCELADO => "La cancelacion requiere un motivo y no permite nuevas modificaciones.",
            EstadoPedidoPersonalizado.ENTREGADO => "La entrega se registra al convertir el pedido en venta.",
            _ => string.Empty
        };
    }

    private void ValidateAndAccept()
    {
        if (_states.SelectedItem is not EstadoPedidoPersonalizado state)
        {
            MessageBox.Show("No hay un cambio de estado disponible.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (state == EstadoPedidoPersonalizado.CANCELADO && string.IsNullOrWhiteSpace(_reason.Text))
        {
            MessageBox.Show("El motivo de cancelacion es obligatorio.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    public static IReadOnlyList<EstadoPedidoPersonalizado> AllowedTargets(EstadoPedidoPersonalizado current)
    {
        var next = current switch
        {
            EstadoPedidoPersonalizado.BORRADOR => EstadoPedidoPersonalizado.CONFIRMADO,
            EstadoPedidoPersonalizado.CONFIRMADO => EstadoPedidoPersonalizado.EN_PRODUCCION,
            EstadoPedidoPersonalizado.EN_PRODUCCION => EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA,
            EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA => (EstadoPedidoPersonalizado?)null,
            _ => (EstadoPedidoPersonalizado?)null
        };
        var result = new List<EstadoPedidoPersonalizado>();
        if (next.HasValue) result.Add(next.Value);
        if (current is not EstadoPedidoPersonalizado.ENTREGADO and not EstadoPedidoPersonalizado.CANCELADO)
            result.Add(EstadoPedidoPersonalizado.CANCELADO);
        return result;
    }

    public static string Friendly(EstadoPedidoPersonalizado state) => state switch
    {
        EstadoPedidoPersonalizado.EN_PRODUCCION => "En produccion",
        EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA => "Listo para entrega",
        _ => state.ToString().Replace('_', ' ').ToLowerInvariant() is var value
            ? char.ToUpperInvariant(value[0]) + value[1..] : state.ToString()
    };
}
