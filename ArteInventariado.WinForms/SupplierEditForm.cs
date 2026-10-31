using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class SupplierEditForm : Form
{
    private readonly ISupplierService _supplierService;
    private readonly SupplierDetail? _existing;
    private readonly Action<int>? _openSupplier;
    private readonly ComboBox _cmbTipo = new();
    private readonly TextBox _txtName = new();
    private readonly TextBox _txtContact = new();
    private readonly TextBox _txtPhone = new();
    private readonly TextBox _txtWhatsApp = new();
    private readonly TextBox _txtEmail = new();
    private readonly TextBox _txtAddress = new();
    private readonly TextBox _txtDocument = new();
    private readonly TextBox _txtFiscalDocument = new();
    private readonly TextBox _txtNotes = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnCancel = new();
    private readonly Label _lblStatus = new();

    public SupplierEditForm(
        ISupplierService supplierService,
        SupplierDetail? existing = null,
        Action<int>? openSupplier = null)
    {
        _supplierService = supplierService;
        _existing = existing;
        _openSupplier = openSupplier;

        BuildUi();
        Load += (_, _) => LoadExistingValues();
    }

    public SupplierDetail? SavedSupplier { get; private set; }

    private void BuildUi()
    {
        Text = _existing is null ? "Nuevo proveedor o artesano" : "Editar proveedor o artesano";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 720);
        MinimumSize = new Size(720, 620);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(22)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var header = new Label
        {
            Text = _existing is null ? "Nuevo proveedor o artesano" : $"Editar {_existing.CodigoProveedor}",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 14)
        };
        UITheme.StyleLabelTitle(header);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16),
            Margin = new Padding(0)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (var i = 0; i < 9; i++)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        }

        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _cmbTipo.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTipo.DisplayMember = nameof(TypeOption.Name);
        _cmbTipo.ValueMember = nameof(TypeOption.Value);
        _cmbTipo.Font = UITheme.NormalFont;
        _cmbTipo.DataSource = new List<TypeOption>
        {
            new(TipoProveedor.PROVEEDOR, "Proveedor"),
            new(TipoProveedor.ARTESANO, "Artesano"),
            new(TipoProveedor.AMBOS, "Proveedor y artesano")
        };

        AddComboField(fields, 0, "Tipo *", _cmbTipo);
        AddField(fields, 1, "Nombre o razon comercial *", _txtName);
        AddField(fields, 2, "Nombre de contacto", _txtContact);
        AddField(fields, 3, "Telefono", _txtPhone);
        AddField(fields, 4, "WhatsApp", _txtWhatsApp);
        AddField(fields, 5, "Correo", _txtEmail);
        AddField(fields, 6, "DUI/documento", _txtDocument);
        AddField(fields, 7, "NIT/documento fiscal", _txtFiscalDocument);
        AddField(fields, 8, "Direccion", _txtAddress);

        _txtNotes.Multiline = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        AddField(fields, 9, "Notas", _txtNotes);

        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.AutoSize = false;
        _lblStatus.Height = 34;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.ForeColor = UITheme.MutedTextColor;
        _lblStatus.Font = UITheme.NormalBoldFont;
        _lblStatus.Margin = new Padding(0, 12, 0, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };

        ConfigureButton(_btnCancel, "Cancelar", 110, (_, _) => DialogResult = DialogResult.Cancel);
        UITheme.StyleSecondaryButton(_btnCancel);
        ConfigureButton(_btnSave, "Guardar", 120, async (_, _) => await SaveAsync());
        UITheme.StylePrimaryButton(_btnSave);
        actions.Controls.AddRange([_btnCancel, _btnSave]);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(fields, 0, 1);
        root.Controls.Add(_lblStatus, 0, 2);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);

        AcceptButton = _btnSave;
        CancelButton = _btnCancel;
    }

    private void LoadExistingValues()
    {
        if (_existing is null)
        {
            _cmbTipo.SelectedValue = TipoProveedor.PROVEEDOR;
            _txtName.Focus();
            return;
        }

        _cmbTipo.SelectedValue = _existing.Tipo;
        _txtName.Text = _existing.NombreRazonSocial;
        _txtContact.Text = _existing.NombreContacto ?? string.Empty;
        _txtPhone.Text = _existing.Telefono ?? string.Empty;
        _txtWhatsApp.Text = _existing.WhatsApp ?? string.Empty;
        _txtEmail.Text = _existing.Correo ?? string.Empty;
        _txtAddress.Text = _existing.Direccion ?? string.Empty;
        _txtDocument.Text = _existing.DocumentoIdentidad ?? string.Empty;
        _txtFiscalDocument.Text = _existing.DocumentoFiscal ?? string.Empty;
        _txtNotes.Text = _existing.Notas ?? string.Empty;
    }

    private async Task SaveAsync()
    {
        _btnSave.Enabled = false;
        _lblStatus.Text = "Validando proveedor...";

        try
        {
            var request = BuildRequest(confirmDuplicateWarning: false);
            var duplicates = await _supplierService.FindDuplicatesAsync(request);
            if (duplicates.HasBlockingDuplicate || duplicates.HasWarnings)
            {
                using var warning = new SupplierDuplicateWarningForm(duplicates);
                var result = warning.ShowDialog(this);
                if (result == DialogResult.Retry && warning.SelectedSupplierId.HasValue)
                {
                    _openSupplier?.Invoke(warning.SelectedSupplierId.Value);
                    _lblStatus.Text = "Revise el proveedor existente antes de continuar.";
                    return;
                }

                if (result != DialogResult.OK || !warning.ContinueConfirmed)
                {
                    _lblStatus.Text = "Guardado cancelado.";
                    return;
                }

                request = BuildRequest(confirmDuplicateWarning: true);
            }

            SavedSupplier = _existing is null
                ? await _supplierService.CreateAsync(request)
                : await _supplierService.UpdateAsync(request);

            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _lblStatus.Text = ex.Message;
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _btnSave.Enabled = true;
        }
    }

    private SupplierSaveRequest BuildRequest(bool confirmDuplicateWarning)
    {
        return new SupplierSaveRequest(
            _existing?.ProveedorId,
            _cmbTipo.SelectedValue is TipoProveedor tipo ? tipo : TipoProveedor.PROVEEDOR,
            _txtName.Text,
            NullIfWhiteSpace(_txtContact.Text),
            NullIfWhiteSpace(_txtPhone.Text),
            NullIfWhiteSpace(_txtWhatsApp.Text),
            NullIfWhiteSpace(_txtEmail.Text),
            NullIfWhiteSpace(_txtAddress.Text),
            NullIfWhiteSpace(_txtDocument.Text),
            NullIfWhiteSpace(_txtFiscalDocument.Text),
            NullIfWhiteSpace(_txtNotes.Text),
            confirmDuplicateWarning);
    }

    private static void AddField(TableLayoutPanel layout, int row, string labelText, TextBox textBox)
    {
        var label = BuildLabel(labelText);
        UITheme.StyleTextBox(textBox);
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(textBox, 1, row);
    }

    private static void AddComboField(TableLayoutPanel layout, int row, string labelText, ComboBox comboBox)
    {
        var label = BuildLabel(labelText);
        comboBox.Dock = DockStyle.Fill;
        comboBox.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(comboBox, 1, row);
    }

    private static Label BuildLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 12, 8)
        };
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 36;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Click += handler;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record TypeOption(TipoProveedor Value, string Name);
}
