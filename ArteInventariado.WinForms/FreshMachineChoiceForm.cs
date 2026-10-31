using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

/// <summary>
/// Explicit first-run choice. No database, principal or cloud call is
/// created until the owner chooses one of the two paths.
/// </summary>
public sealed class FreshMachineChoiceForm : Form
{
    private readonly Button _newInstallation = new();
    private readonly Button _recover = new();
    private readonly Button _cancel = new();

    public FreshMachineChoiceForm()
    {
        Text = "ArteInventariado - Configuración inicial";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 300);
        BackColor = Color.WhiteSmoke;

        var title = new Label
        {
            Text = "Esta instalación aún no está configurada",
            Dock = DockStyle.Top,
            Height = 52,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Color.FromArgb(196, 35, 112),
            TextAlign = ContentAlignment.MiddleCenter
        };
        var description = new Label
        {
            Text = "Elige cómo deseas iniciar ArteInventariado. No se crea una base de negocio hasta confirmar la opción.",
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(24, 8, 24, 8),
            Font = new Font("Segoe UI", 10),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(72, 10, 72, 16)
        };
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 33.334f));
        ConfigureButton(_newInstallation, "A  Configurar instalación nueva", Color.FromArgb(196, 35, 112));
        ConfigureButton(_recover, "B  Recuperar instalación existente", Color.FromArgb(117, 45, 87));
        ConfigureButton(_cancel, "Cancelar", Color.White, Color.FromArgb(117, 45, 87));
        actions.Controls.Add(_newInstallation, 0, 0);
        actions.Controls.Add(_recover, 0, 1);
        actions.Controls.Add(_cancel, 0, 2);

        Controls.Add(actions);
        Controls.Add(description);
        Controls.Add(title);

        _newInstallation.Click += (_, _) =>
        {
            Choice = DisasterRecoveryChoice.NewInstallation;
            DialogResult = DialogResult.OK;
        };
        _recover.Click += (_, _) =>
        {
            Choice = DisasterRecoveryChoice.RecoverExisting;
            DialogResult = DialogResult.OK;
        };
        _cancel.Click += (_, _) =>
        {
            Choice = DisasterRecoveryChoice.Cancelled;
            DialogResult = DialogResult.Cancel;
        };
        AcceptButton = _newInstallation;
        CancelButton = _cancel;
    }

    public DisasterRecoveryChoice Choice { get; private set; } = DisasterRecoveryChoice.Cancelled;

    private static void ConfigureButton(Button button, string text, Color backColor, Color? foreColor = null)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 4, 0, 4);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = backColor;
        button.ForeColor = foreColor ?? Color.White;
        button.Font = new Font("Segoe UI", 11, FontStyle.Bold);
    }
}
