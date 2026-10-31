using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class ChangePasswordForm : Form
{
    private readonly IAccountService _accountService;
    private readonly bool _required;
    private readonly TextBox _txtCurrent = new();
    private readonly TextBox _txtNew = new();
    private readonly TextBox _txtConfirm = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnCancel = new();

    public ChangePasswordForm(IAccountService accountService, bool required = false)
    {
        _accountService = accountService;
        _required = required;

        BuildUi();
    }

    private void BuildUi()
    {
        Text = _required ? "Cambio de contraseña requerido" : "Cambiar contraseña";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(540, 500);
        MinimumSize = new Size(500, 470);
        ControlBox = !_required;
        UITheme.StyleForm(this);

        var message = new Label
        {
            Text = _required
                ? "Debe establecer la contraseña antes de continuar."
                : "Cambie la contraseña de su usuario actual.",
            Dock = DockStyle.Fill
        };
        UITheme.StyleStatusLabel(message, _required ? UITheme.WarningColor : UITheme.SecondaryColor);

        ConfigurePassword(_txtCurrent);
        ConfigurePassword(_txtNew);
        ConfigurePassword(_txtConfirm);

        _btnSave.Text = "Guardar";
        _btnSave.Click += BtnSave_Click;
        UITheme.StylePrimaryButton(_btnSave);
        UITheme.EnsureButtonContentFits(_btnSave, 130, 42, 36);
        _btnSave.Size = _btnSave.MinimumSize;

        _btnCancel.Text = "Cancelar";
        _btnCancel.Enabled = !_required;
        _btnCancel.Click += (_, _) => Close();
        UITheme.StyleOutlineButton(_btnCancel);
        UITheme.EnsureButtonContentFits(_btnCancel, 130, 42, 36);
        _btnCancel.Size = _btnCancel.MinimumSize;

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.SurfaceColor
        };
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.Controls.Add(message, 0, 0);
        fields.Controls.Add(VisualLayout.CreateField("Contraseña actual", _txtCurrent), 0, 1);
        fields.Controls.Add(VisualLayout.CreateField("Nueva contraseña", _txtNew), 0, 2);
        fields.Controls.Add(VisualLayout.CreateField("Confirmar nueva contraseña", _txtConfirm), 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(UITheme.SpaceLg, UITheme.SpaceSm, UITheme.SpaceLg, UITheme.SpaceSm)
        };
        _btnCancel.Margin = new Padding(UITheme.SpaceSm, 0, 0, 0);
        actions.Controls.Add(_btnCancel);
        actions.Controls.Add(_btnSave);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(VisualLayout.CreatePageHeader(
            _required ? "Cambio obligatorio de contraseña" : "Cambiar contraseña",
            _required ? "Protege la cuenta antes de continuar al sistema." : "Actualiza la contraseña de tu sesión actual."), 0, 0);
        root.Controls.Add(fields, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        AcceptButton = _btnSave;
        if (!_required)
        {
            CancelButton = _btnCancel;
        }
    }

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        try
        {
            _btnSave.Enabled = false;
            await _accountService.ChangeCurrentPasswordAsync(new ChangeCurrentPasswordRequest(
                _txtCurrent.Text,
                _txtNew.Text,
                _txtConfirm.Text));

            MessageBox.Show("Contraseña actualizada.", "Cambiar contraseña", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cambiar contraseña", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed)
            {
                _btnSave.Enabled = true;
            }
        }
    }

    private static void ConfigurePassword(TextBox textBox)
    {
        textBox.UseSystemPasswordChar = true;
    }
}
