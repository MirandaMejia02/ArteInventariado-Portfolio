using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public sealed class CustomOrderPaymentForm : Form
{
    private readonly CustomOrderFinanceSummary _summary;
    private readonly ICustomOrderFinanceService _service;
    private readonly ComboBox _type = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _method = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _amount = new() { Dock = DockStyle.Fill, DecimalPlaces = 2, Maximum = 1000000 };
    private readonly TextBox _reference = new() { Dock = DockStyle.Fill };
    private readonly TextBox _notes = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };

    public CustomOrderPaymentForm(CustomOrderFinanceSummary summary, ICustomOrderFinanceService service)
    {
        _summary = summary;
        _service = service;
        Text = $"Registrar pago - {summary.CodigoPedido}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 570);
        MinimumSize = new Size(560, 520);
        UITheme.StyleForm(this);
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 11, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        for (var i = 0; i < 4; i++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        }
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label { Text = $"Pago de pedido {_summary.CodigoPedido}", Dock = DockStyle.Fill };
        UITheme.StyleLabelTitle(title);
        root.Controls.Add(title);
        var summary = new Label
        {
            Text = $"Destinatario: {_summary.Destinatario}\r\nTotal: {Money(_summary.TotalCentavos)}    Pagado neto: {Money(_summary.PagoNetoCentavos)}    Saldo: {Money(_summary.SaldoPendienteCentavos)}",
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12),
            Font = UITheme.NormalBoldFont
        };
        root.Controls.Add(summary);
        AddField(root, "Tipo de pago", _type);
        AddField(root, "Metodo", _method);
        AddField(root, "Monto ($)", _amount);
        AddField(root, "Referencia", _reference);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        var notesBox = new GroupBox { Text = "Observaciones", Dock = DockStyle.Fill, Padding = new Padding(8) };
        notesBox.Controls.Add(_notes);
        bottom.Controls.Add(notesBox);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 8, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        var cancel = Button("Cancelar", UITheme.StyleSecondaryButton);
        var fill = Button("Pagar total", UITheme.StylePrimaryButton);
        var save = Button("Registrar pago", UITheme.StyleSuccessButton);
        cancel.DialogResult = DialogResult.Cancel;
        fill.Click += (_, _) => _amount.Value = Math.Min(_amount.Maximum, _summary.SaldoPendienteCentavos / 100m);
        save.Click += async (_, _) => await SaveAsync();
        buttons.Controls.Add(cancel); buttons.Controls.Add(fill); buttons.Controls.Add(save);
        bottom.Controls.Add(buttons);
        root.Controls.Add(bottom);
        Controls.Add(root);

        _type.DataSource = Enum.GetValues<TipoPagoPedido>();
        _method.DataSource = Enum.GetValues<MetodoPago>()
            .Where(m => m is MetodoPago.EFECTIVO or MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN).ToList();
        _method.SelectedIndexChanged += (_, _) => UpdateReference();
        AcceptButton = save;
        CancelButton = cancel;
        UpdateReference();
    }

    private async Task SaveAsync()
    {
        try
        {
            await _service.RegisterPaymentAsync(new(
                _summary.PedidoPersonalizadoId,
                Guid.NewGuid(),
                (TipoPagoPedido)_type.SelectedItem!,
                (MetodoPago)_method.SelectedItem!,
                checked((long)(_amount.Value * 100m)),
                _reference.Text,
                _notes.Text));
            MessageBox.Show("Pago registrado.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void UpdateReference()
    {
        var required = _method.SelectedItem is MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN;
        _reference.Enabled = required;
        if (!required) _reference.Clear();
    }

    private static void AddField(TableLayoutPanel root, string label, Control control)
    {
        root.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
        root.Controls.Add(control);
    }

    private static Button Button(string text, Action<Button> style)
    {
        var button = new Button { Text = text, Dock = DockStyle.Fill, Margin = new Padding(5) };
        style(button);
        return button;
    }

    private static string Money(long cents) => (cents / 100m).ToString("$0.00");
}
