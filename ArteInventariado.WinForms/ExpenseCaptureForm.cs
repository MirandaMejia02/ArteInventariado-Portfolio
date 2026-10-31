using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

internal sealed class ExpenseCaptureForm : Form
{
    private readonly ComboBox _category = new();
    private readonly ComboBox _method = new();
    private readonly ComboBox _fundingOrigin = new();
    private readonly TextBox _responsible = new();
    private readonly TextBox _concept = new();
    private readonly TextBox _reference = new();
    private readonly TextBox _notes = new();
    private readonly Guid _operationId = Guid.NewGuid();

    public ExpenseCaptureForm(IReadOnlyList<ExpenseCategoryOption> categories, string initialConcept)
    {
        Text = "Datos del gasto";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 470);
        MinimumSize = new Size(620, 450);
        MaximizeBox = false;
        MinimizeBox = false;
        UITheme.StyleForm(this);

        _category.DropDownStyle = ComboBoxStyle.DropDownList;
        _category.DisplayMember = nameof(ExpenseCategoryOption.Nombre);
        _category.ValueMember = nameof(ExpenseCategoryOption.CategoriaGastoId);
        _category.DataSource = categories.ToList();
        _method.DropDownStyle = ComboBoxStyle.DropDownList;
        _method.DataSource = Enum.GetValues<MetodoPago>();
        _method.SelectedItem = MetodoPago.EFECTIVO;
        _fundingOrigin.DropDownStyle = ComboBoxStyle.DropDownList;
        _fundingOrigin.DisplayMember = nameof(FundingOriginOption.Label);
        _fundingOrigin.Items.AddRange(
        [
            new FundingOriginOption(null, "Seleccione el origen..."),
            new FundingOriginOption(OrigenFondos.CAJA_FISICA, "Caja física"),
            new FundingOriginOption(OrigenFondos.FONDOS_EXTERNOS_DUENO, "Fondos externos del dueño"),
            new FundingOriginOption(OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA, "Fondos del negocio fuera de caja")
        ]);
        _fundingOrigin.SelectedIndex = 0;
        _responsible.PlaceholderText = "Obligatorio si no sale de caja";
        _concept.Text = initialConcept.Trim();
        _reference.PlaceholderText = "Obligatoria para transferencia/Bitcoin";
        _notes.Multiline = true;
        _notes.ScrollBars = ScrollBars.Vertical;

        var save = new Button { Text = "Registrar gasto", DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel };
        UITheme.StylePrimaryButton(save);
        UITheme.StyleSecondaryButton(cancel);
        save.Click += (_, _) => Save();

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 9
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        for (var row = 0; row < 6; row++)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        }
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var cashEffectNotice = new Label
        {
            Text = "Caja física descuenta del cajón. Fondos del dueño o del negocio fuera de caja registran el gasto sin cambiar el efectivo de caja.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.TextDark
        };
        fields.Controls.Add(cashEffectNotice, 0, 0);
        fields.SetColumnSpan(cashEffectNotice, 2);
        AddField(fields, 1, "Categoría", _category);
        AddField(fields, 2, "Método de pago", _method);
        AddField(fields, 3, "Origen de fondos", _fundingOrigin);
        AddField(fields, 4, "Responsable / fuente", _responsible);
        AddField(fields, 5, "Concepto", _concept);
        AddField(fields, 6, "Referencia", _reference);
        AddField(fields, 7, "Notas", _notes);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        save.Size = new Size(155, 36);
        cancel.Size = new Size(110, 36);
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        fields.Controls.Add(actions, 0, 8);
        fields.SetColumnSpan(actions, 2);
        Controls.Add(fields);
        AcceptButton = save;
        CancelButton = cancel;
    }

    public int? CategoryId => (_category.SelectedItem as ExpenseCategoryOption)?.CategoriaGastoId;
    public MetodoPago? PaymentMethod => _method.SelectedItem is MetodoPago method ? method : null;
    public OrigenFondos? FundingOrigin => (_fundingOrigin.SelectedItem as FundingOriginOption)?.Value;
    public string? FundingResponsible => Normalize(_responsible.Text);
    public string Concept => _concept.Text.Trim();
    public string? Reference => Normalize(_reference.Text);
    public string? Notes => Normalize(_notes.Text);
    public Guid OperationId => _operationId;

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_concept.Text))
        {
            MessageBox.Show("El concepto del gasto es obligatorio.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _concept.Focus();
            return;
        }

        if (_category.SelectedItem is null)
        {
            MessageBox.Show("Seleccione una categoría de gasto.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _category.Focus();
            return;
        }

        if (!FundingOrigin.HasValue)
        {
            MessageBox.Show("Seleccione de dónde sale el dinero. Esto define si afecta el efectivo de caja.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _fundingOrigin.Focus();
            return;
        }

        if (FundingOrigin != OrigenFondos.CAJA_FISICA && string.IsNullOrWhiteSpace(FundingResponsible))
        {
            MessageBox.Show("Indique quién aporta o cuál es la fuente de los fondos externos.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _responsible.Focus();
            return;
        }

        if (PaymentMethod is MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN && string.IsNullOrWhiteSpace(Reference))
        {
            MessageBox.Show("La referencia es obligatoria para transferencia y Bitcoin.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _reference.Focus();
            return;
        }

        if (FundingOrigin == OrigenFondos.CAJA_FISICA && PaymentMethod != MetodoPago.EFECTIVO)
        {
            MessageBox.Show("Un gasto de caja física debe registrarse como efectivo. Para transferencia o Bitcoin, seleccione una fuente fuera de caja.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _fundingOrigin.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private static void AddField(TableLayoutPanel panel, int row, string labelText, Control control)
    {
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark
        };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(4);
        panel.Controls.Add(label, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private static string? Normalize(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record FundingOriginOption(OrigenFondos? Value, string Label);
}
