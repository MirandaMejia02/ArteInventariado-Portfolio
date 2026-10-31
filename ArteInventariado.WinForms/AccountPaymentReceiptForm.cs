namespace ArteInventariado.WinForms;

public class AccountPaymentReceiptForm : Form
{
    private readonly AccountPaymentReceiptViewModel _receipt;
    private readonly bool _canNewPayment;
    private readonly TextBox _txtPreview = new();
    private readonly Button _btnSavePdf = new();
    private readonly Button _btnNewPayment = new();
    private readonly Button _btnClose = new();

    public AccountPaymentReceiptForm(AccountPaymentReceiptViewModel receipt, bool canNewPayment)
    {
        _receipt = receipt;
        _canNewPayment = canNewPayment;
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Abono registrado";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 660);
        MinimumSize = new Size(820, 560);
        MaximizeBox = true;
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildActions(), 0, 2);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16, 8, 16, 8),
            Margin = new Padding(0, 0, 0, 10)
        };

        var title = new Label
        {
            Text = "Abono registrado",
            Dock = DockStyle.Left,
            Width = 320,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(title);

        var subtitle = new Label
        {
            Text = "Comprobante interno de abono",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.MutedTextColor
        };

        header.Controls.Add(subtitle);
        header.Controls.Add(title);
        return header;
    }

    private Control BuildBody()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(BuildSummary(), 0, 0);
        layout.Controls.Add(BuildPreview(), 1, 0);
        return layout;
    }

    private Control BuildSummary()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 14,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 10, 0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(panel, 0, "Codigo", _receipt.CodigoAbono);
        AddRow(panel, 1, "Fecha", SaleReceiptViewModel.ToLocal(_receipt.FechaAbono).ToString("yyyy-MM-dd HH:mm"));
        AddRow(panel, 2, "Cliente", _receipt.Cliente);
        AddRow(panel, 3, "Cuenta", _receipt.CodigoCuenta);
        AddRow(panel, 4, "Venta", _receipt.NumeroVenta);
        AddRow(panel, 5, "Usuario", _receipt.Usuario);
        AddRow(panel, 6, "Metodo", _receipt.MetodoPago.ToString());
        AddRow(panel, 7, "Referencia", _receipt.Referencia ?? "-");
        AddAmountRow(panel, 8, "Saldo anterior", _receipt.SaldoAnteriorCentavos, UITheme.TextDark);
        AddAmountRow(panel, 9, "Monto abonado", _receipt.MontoCentavos, UITheme.SuccessColor);
        AddAmountRow(panel, 10, "A interes", _receipt.AplicadoInteresCentavos, UITheme.TextDark);
        AddAmountRow(panel, 11, "A capital", _receipt.AplicadoCapitalCentavos, UITheme.TextDark);
        AddAmountRow(panel, 12, "Saldo nuevo", _receipt.SaldoNuevoCentavos, UITheme.PrimaryColor);
        AddRow(panel, 13, "Estado", $"{_receipt.EstadoCuenta} / {_receipt.SituacionCuenta}");
        return panel;
    }

    private Control BuildPreview()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14),
            Margin = new Padding(10, 0, 0, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Vista previa del comprobante",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark
        };

        _txtPreview.Dock = DockStyle.Fill;
        _txtPreview.Multiline = true;
        _txtPreview.ReadOnly = true;
        _txtPreview.ScrollBars = ScrollBars.Vertical;
        _txtPreview.Font = new Font("Consolas", 10);
        _txtPreview.BackColor = Color.White;
        _txtPreview.Text = _receipt.BuildReceiptText();

        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(_txtPreview, 0, 1);
        return panel;
    }

    private Control BuildActions()
    {
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };

        ConfigureButton(_btnClose, "Cerrar", 110, (_, _) => Close());
        UITheme.StyleSecondaryButton(_btnClose);
        ConfigureButton(_btnNewPayment, "Nuevo abono", 140, (_, _) =>
        {
            DialogResult = DialogResult.Retry;
            Close();
        });
        _btnNewPayment.Visible = _canNewPayment;
        UITheme.StyleSuccessButton(_btnNewPayment);
        ConfigureButton(_btnSavePdf, "Guardar PDF", 130, (_, _) => SavePdf());
        UITheme.StylePrimaryButton(_btnSavePdf);

        actions.Controls.AddRange([_btnClose, _btnNewPayment, _btnSavePdf]);
        return actions;
    }

    private void SavePdf()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Guardar comprobante de abono en PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = _receipt.SuggestedPdfFileName,
            AddExtension = true,
            DefaultExt = "pdf"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            AccountPaymentReceiptPdfExporter.Save(_receipt, dialog.FileName);
            MessageBox.Show($"Comprobante guardado en:\n{dialog.FileName}", "Comprobante de abono", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Guardar PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, string value)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 7.14f));
        var left = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor,
            AutoEllipsis = true
        };
        var right = new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            AutoEllipsis = true
        };
        layout.Controls.Add(left, 0, row);
        layout.Controls.Add(right, 1, row);
    }

    private static void AddAmountRow(TableLayoutPanel layout, int row, string label, long cents, Color valueColor)
    {
        AddRow(layout, row, label, SaleReceiptViewModel.FormatDollars(cents));
        if (layout.GetControlFromPosition(1, row) is Label value)
        {
            value.ForeColor = valueColor;
        }
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 38;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Click += handler;
    }

}
