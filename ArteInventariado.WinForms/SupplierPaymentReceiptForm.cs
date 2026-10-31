using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

public class SupplierPaymentReceiptForm : Form
{
    private readonly SupplierPaymentReceiptViewModel _receipt;

    public SupplierPaymentReceiptForm(SupplierPaymentSummary payment)
    {
        _receipt = SupplierPaymentReceiptViewModel.FromSummary(payment);
        BuildUi();
    }

    public SupplierPaymentReceiptForm(SupplierPaymentHistoryEntry payment)
    {
        _receipt = SupplierPaymentReceiptViewModel.FromHistory(payment);
        BuildUi();
    }

    private void BuildUi()
    {
        Text = $"Comprobante {_receipt.Codigo}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 660);
        MinimumSize = new Size(600, 520);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(22) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var title = new Label { Text = "Comprobante interno de pago", Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        UITheme.StyleLabelTitle(title);
        var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = string.Join(Environment.NewLine, _receipt.BuildReceiptLines()), Font = new Font("Consolas", 11), BackColor = UITheme.SurfaceColor };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        var close = new Button { Text = "Cerrar", Size = new Size(110, 40) };
        UITheme.StyleSecondaryButton(close);
        close.Click += (_, _) => Close();
        var pdf = new Button { Text = "Guardar PDF", Size = new Size(150, 40) };
        UITheme.StylePrimaryButton(pdf);
        pdf.Click += SavePdf;
        actions.Controls.AddRange([close, pdf]);
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(text, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
    }

    private void SavePdf(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog { Filter = "Documento PDF (*.pdf)|*.pdf", FileName = _receipt.SuggestedPdfFileName };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            AccountPaymentReceiptPdfExporter.SaveLines(_receipt.BuildReceiptLines(), dialog.FileName);
            using var result = new GeneratedPdfActionsForm(dialog.FileName);
            result.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Comprobante", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
