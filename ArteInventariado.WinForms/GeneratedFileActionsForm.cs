using System.Diagnostics;

namespace ArteInventariado.WinForms;

public sealed class GeneratedFileActionsForm : Form
{
    private readonly string _filePath;

    public GeneratedFileActionsForm(string title, string filePath)
    {
        _filePath = filePath;
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(720, 230);
        MinimumSize = new Size(620, 220);
        UITheme.StyleForm(this);
        BuildUi(title);
    }

    private void BuildUi(string title)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            RowCount = 3,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var heading = new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        UITheme.StyleLabelTitle(heading);
        var path = new TextBox
        {
            Text = _filePath,
            ReadOnly = true,
            Multiline = true,
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            ScrollBars = ScrollBars.Vertical
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var close = NewButton("Cerrar", (_, _) => Close(), secondary: true);
        var folder = NewButton("Abrir carpeta", (_, _) => OpenFolder());
        var file = NewButton("Abrir archivo", (_, _) => OpenFile());
        actions.Controls.AddRange([close, folder, file]);

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(path, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
    }

    private static Button NewButton(string text, EventHandler click, bool secondary = false)
    {
        var button = new Button { Text = text, Size = new Size(145, 38), Margin = new Padding(8, 0, 0, 0) };
        button.Click += click;
        if (secondary) UITheme.StyleSecondaryButton(button); else UITheme.StylePrimaryButton(button);
        return button;
    }

    private void OpenFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir el archivo:\n\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_filePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir la carpeta:\n\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
