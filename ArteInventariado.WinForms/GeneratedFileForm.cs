using System.Diagnostics;

namespace ArteInventariado.WinForms;

internal sealed class GeneratedFileForm : Form
{
    public GeneratedFileForm(string title, string path)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(650, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        UITheme.StyleForm(this);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 3,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        var heading = new Label { Text = title, AutoSize = true };
        UITheme.StyleLabelHeader(heading);
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 3);
        var file = new TextBox { Text = path, ReadOnly = true, Dock = DockStyle.Fill, Multiline = true };
        UITheme.StyleTextBox(file);
        layout.Controls.Add(file, 0, 1);
        layout.SetColumnSpan(file, 3);
        var open = NewButton("Abrir PDF", UITheme.StylePrimaryButton, () => Open(path));
        var folder = NewButton("Abrir carpeta", UITheme.StyleSecondaryButton,
            () => Open(Path.GetDirectoryName(path) ?? path));
        var close = NewButton("Cerrar", UITheme.StyleSecondaryButton, Close);
        layout.Controls.Add(open, 0, 2);
        layout.Controls.Add(folder, 1, 2);
        layout.Controls.Add(close, 2, 2);
        Controls.Add(layout);
    }

    private static Button NewButton(string text, Action<Button> style, Action action)
    {
        var button = new Button { Text = text, Dock = DockStyle.Fill, Margin = new Padding(5) };
        style(button);
        button.Click += (_, _) => action();
        return button;
    }
    private static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
