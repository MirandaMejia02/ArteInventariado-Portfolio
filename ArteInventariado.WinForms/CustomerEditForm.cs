using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

public class CustomerEditForm : Form
{
    private readonly ICustomerService _customerService;
    private readonly CustomerDetail? _existing;
    private readonly Action<int>? _openCustomer;
    private readonly TextBox _txtName = new();
    private readonly TextBox _txtPhone = new();
    private readonly TextBox _txtWhatsApp = new();
    private readonly TextBox _txtEmail = new();
    private readonly TextBox _txtAddress = new();
    private readonly TextBox _txtDocument = new();
    private readonly TextBox _txtNotes = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnCancel = new();
    private readonly Label _lblStatus = new();

    public CustomerEditForm(
        ICustomerService customerService,
        CustomerDetail? existing = null,
        Action<int>? openCustomer = null)
    {
        _customerService = customerService;
        _existing = existing;
        _openCustomer = openCustomer;

        BuildUi();
        Load += (_, _) => LoadExistingValues();
    }

    public CustomerDetail? SavedCustomer { get; private set; }

    private void BuildUi()
    {
        Text = _existing is null ? "Nuevo cliente" : "Editar cliente";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 660);
        MinimumSize = new Size(680, 560);
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
            Text = _existing is null ? "Nuevo cliente" : $"Editar cliente {_existing.CodigoCliente}",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 14)
        };
        UITheme.StyleLabelTitle(header);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16),
            Margin = new Padding(0)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (var i = 0; i < 6; i++)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        }

        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddField(fields, 0, "Nombre completo *", _txtName);
        AddField(fields, 1, "Telefono celular", _txtPhone);
        AddField(fields, 2, "WhatsApp", _txtWhatsApp);
        AddField(fields, 3, "Correo", _txtEmail);
        AddField(fields, 4, "DUI/documento", _txtDocument);
        AddField(fields, 5, "Direccion", _txtAddress);

        _txtNotes.Multiline = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        AddField(fields, 6, "Notas", _txtNotes);

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
            _txtName.Focus();
            return;
        }

        _txtName.Text = _existing.NombreCompleto;
        _txtPhone.Text = _existing.TelefonoCelular ?? string.Empty;
        _txtWhatsApp.Text = _existing.WhatsApp ?? string.Empty;
        _txtEmail.Text = _existing.CorreoElectronico ?? string.Empty;
        _txtAddress.Text = _existing.Direccion ?? string.Empty;
        _txtDocument.Text = _existing.DocumentoIdentidad ?? string.Empty;
        _txtNotes.Text = _existing.Notas ?? string.Empty;
    }

    private async Task SaveAsync()
    {
        _btnSave.Enabled = false;
        _lblStatus.Text = "Validando cliente...";

        try
        {
            var request = BuildRequest(confirmDuplicateWarning: false);
            var duplicates = await _customerService.FindDuplicatesAsync(request);
            if (duplicates.HasBlockingDuplicate || duplicates.HasWarnings)
            {
                using var warning = new CustomerDuplicateWarningForm(duplicates);
                var result = warning.ShowDialog(this);
                if (result == DialogResult.Retry && warning.SelectedCustomerId.HasValue)
                {
                    _openCustomer?.Invoke(warning.SelectedCustomerId.Value);
                    _lblStatus.Text = "Revise el cliente existente antes de continuar.";
                    return;
                }

                if (result != DialogResult.OK || !warning.ContinueConfirmed)
                {
                    _lblStatus.Text = "Guardado cancelado.";
                    return;
                }

                request = BuildRequest(confirmDuplicateWarning: true);
            }

            SavedCustomer = _existing is null
                ? await _customerService.CreateAsync(request)
                : await _customerService.UpdateAsync(request);

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

    private CustomerSaveRequest BuildRequest(bool confirmDuplicateWarning)
    {
        return new CustomerSaveRequest(
            _existing?.ClienteId,
            _txtName.Text,
            NullIfWhiteSpace(_txtPhone.Text),
            NullIfWhiteSpace(_txtWhatsApp.Text),
            NullIfWhiteSpace(_txtEmail.Text),
            NullIfWhiteSpace(_txtAddress.Text),
            NullIfWhiteSpace(_txtDocument.Text),
            NullIfWhiteSpace(_txtNotes.Text),
            confirmDuplicateWarning);
    }

    private static void AddField(TableLayoutPanel layout, int row, string labelText, TextBox textBox)
    {
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 12, 8)
        };

        UITheme.StyleTextBox(textBox);
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(0, 0, 0, 8);

        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(textBox, 1, row);
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
}
