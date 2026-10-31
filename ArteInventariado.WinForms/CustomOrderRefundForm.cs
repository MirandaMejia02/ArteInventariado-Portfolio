using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public sealed class CustomOrderRefundForm : Form
{
    private readonly CustomOrderFinanceSummary _summary;
    private readonly ICustomOrderFinanceService _service;
    private readonly ComboBox _method = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _amount = new() { Dock = DockStyle.Fill, DecimalPlaces = 2, Maximum = 1000000 };
    private readonly TextBox _reference = new() { Dock = DockStyle.Fill };
    private readonly TextBox _reason = new() { Dock = DockStyle.Fill, Multiline = true };

    public CustomOrderRefundForm(CustomOrderFinanceSummary summary, ICustomOrderFinanceService service)
    {
        _summary = summary;
        _service = service;
        Text = $"Reembolsar - {summary.CodigoPedido}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 490);
        MinimumSize = new Size(640, 460);
        UITheme.StyleForm(this);
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 8, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        for (var i = 0; i < 3; i++) { root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); }
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label { Text = $"Reembolso de pedido\r\nDisponible: {Money(_summary.PagoNetoCentavos)}", Dock = DockStyle.Fill };
        UITheme.StyleLabelTitle(title);
        root.Controls.Add(title);
        Add(root, "Metodo", _method);
        Add(root, "Monto ($)", _amount);
        Add(root, "Referencia", _reference);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var reason = new GroupBox { Text = "Motivo obligatorio", Dock = DockStyle.Fill, Padding = new Padding(8) };
        reason.Controls.Add(_reason); panel.Controls.Add(reason);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 8, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var cancel = new Button { Text = "Cancelar", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel };
        var save = new Button { Text = "Registrar reembolso", Dock = DockStyle.Fill };
        UITheme.StyleSecondaryButton(cancel); UITheme.StyleDangerButton(save);
        save.Click += async (_, _) => await SaveAsync();
        buttons.Controls.Add(cancel); buttons.Controls.Add(save); panel.Controls.Add(buttons);
        root.Controls.Add(panel); Controls.Add(root);
        _method.DataSource = Enum.GetValues<MetodoPago>().Where(m => m is MetodoPago.EFECTIVO or MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN).ToList();
        _method.SelectedIndexChanged += (_, _) => UpdateReference();
        UpdateReference();
    }

    private async Task SaveAsync()
    {
        try
        {
            await _service.RegisterRefundAsync(new(
                _summary.PedidoPersonalizadoId, Guid.NewGuid(), (MetodoPago)_method.SelectedItem!,
                checked((long)(_amount.Value * 100m)), _reason.Text, Referencia: _reference.Text));
            MessageBox.Show("Reembolso registrado.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void UpdateReference()
    {
        _reference.Enabled = _method.SelectedItem is MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN;
        if (!_reference.Enabled) _reference.Clear();
    }
    private static void Add(TableLayoutPanel root, string text, Control control) { root.Controls.Add(new Label { Text = text, AutoSize = true }); root.Controls.Add(control); }
    private static string Money(long cents) => (cents / 100m).ToString("$0.00");
}
