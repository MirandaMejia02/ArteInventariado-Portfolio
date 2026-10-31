using System.Diagnostics;

namespace ArteInventariado.WinForms;

public class GeneratedPdfActionsForm : Form
{
    private readonly string _filePath;

    public GeneratedPdfActionsForm(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Comprobante guardado";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 220);
        MinimumSize = new Size(560, 210);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(22)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        var title = new Label
        {
            Text = "El comprobante PDF se guardo correctamente.",
            Dock = DockStyle.Fill,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var path = new TextBox
        {
            Text = _filePath,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            Multiline = true,
            BackColor = UITheme.SurfaceColor,
            Font = UITheme.NormalFont
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        actions.Controls.Add(CreateButton("Cerrar", 110, UITheme.StyleSecondaryButton, (_, _) => Close()));
        actions.Controls.Add(CreateButton("Abrir carpeta", 150, UITheme.StyleSecondaryButton, (_, _) => OpenFolder()));
        actions.Controls.Add(CreateButton("Abrir PDF", 135, UITheme.StylePrimaryButton, (_, _) => OpenPdf()));

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(path, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
    }

    private void OpenPdf()
    {
        if (!File.Exists(_filePath))
        {
            MessageBox.Show("El archivo PDF ya no existe en la ruta indicada.", "Abrir PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        OpenWithDefaultApplication(_filePath, "Abrir PDF");
    }

    private void OpenFolder()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            MessageBox.Show("La carpeta del comprobante ya no existe.", "Abrir carpeta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        OpenWithDefaultApplication(directory, "Abrir carpeta");
    }

    private void OpenWithDefaultApplication(string path, string title)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static Button CreateButton(string text, int width, Action<Button> style, EventHandler click)
    {
        var button = new Button { Text = text, Size = new Size(width, 40), Margin = new Padding(8, 0, 0, 0) };
        style(button);
        button.Click += click;
        return button;
    }
}
