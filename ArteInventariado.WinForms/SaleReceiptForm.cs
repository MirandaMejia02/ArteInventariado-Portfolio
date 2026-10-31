namespace ArteInventariado.WinForms;

public class SaleReceiptForm : Form
{
    private const int PreferredPanel1MinSize = 430;
    private const int PreferredPanel2MinSize = 340;
    private const double PreferredPanel1Ratio = 0.62d;
    private readonly SaleReceiptViewModel _receipt;
    private readonly string _primaryButtonText;
    private readonly bool _isCompletedSale;
    private readonly DataGridView _gridLines = new();
    private readonly DataGridView _gridPayments = new();
    private readonly TextBox _txtPreview = new();
    private readonly Button _btnSavePdf = new();
    private readonly Button _btnPrintFuture = new();
    private readonly Button _btnEmailFuture = new();
    private readonly Button _btnPrimary = new();
    private bool _primaryActionHandled;

    public SaleReceiptForm(
        SaleReceiptViewModel receipt,
        bool isCompletedSale,
        string primaryButtonText)
    {
        _receipt = receipt;
        _isCompletedSale = isCompletedSale;
        _primaryButtonText = primaryButtonText;

        BuildUi();
    }

    private void BuildUi()
    {
        Text = _isCompletedSale ? "Venta completada" : "Ticket de venta";
        StartPosition = FormStartPosition.CenterParent;
        // Keep enough room for the products table, totals and payment table at
        // normal and high DPI. The minimum also prevents the products section
        // from being compressed into a single clipped row when the form is
        // opened from customer credit or either POS.
        ClientSize = new Size(1280, 820);
        MinimumSize = new Size(1040, 640);
        MaximizeBox = true;
        KeyPreview = true;
        KeyDown += SaleReceiptForm_KeyDown;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            BackColor = UITheme.BackgroundColor,
            AutoSize = false,
            AutoScroll = true,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildActions(), 0, 2);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(18, 10, 18, 10),
            Margin = new Padding(0, 0, 0, 12),
            AutoSize = false,
            MinimumSize = Size.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));

        var title = new Label
        {
            Text = _isCompletedSale ? "Venta completada" : "Ticket de venta",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(title);

        var subtitle = new Label
        {
            Text = "Recibo de venta - Comprobante de compra",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.MutedTextColor,
            AutoSize = false,
            AutoEllipsis = false
        };

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(subtitle, 1, 0);
        return header;
    }

    private Control BuildBody()
    {
        var body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 8,
            BackColor = UITheme.BackgroundColor,
            AutoSize = false,
            MinimumSize = Size.Empty
        };
        body.Panel1.AutoScroll = true;
        body.Panel2.AutoScroll = true;
        body.Resize += (_, _) => ApplySafeSplitterDistance(body);
        body.Layout += (_, _) => ApplySafeSplitterDistance(body);
        Shown += (_, _) => ApplySafeSplitterDistance(body);

        body.Panel1.Controls.Add(BuildSummaryPanel());
        body.Panel2.Controls.Add(BuildPreviewPanel());
        return body;
    }

    private static void ApplySafeSplitterDistance(SplitContainer splitContainer)
    {
        var availableWidth = splitContainer.ClientSize.Width;
        if (availableWidth <= 0)
        {
            return;
        }

        var panel1MinSize = PreferredPanel1MinSize;
        var panel2MinSize = PreferredPanel2MinSize;

        if (availableWidth < panel1MinSize + panel2MinSize)
        {
            panel1MinSize = Math.Max(25, (int)(availableWidth * PreferredPanel1Ratio));
            panel2MinSize = Math.Max(25, availableWidth - panel1MinSize);
        }

        panel1MinSize = Math.Min(panel1MinSize, Math.Max(25, availableWidth - panel2MinSize));
        panel2MinSize = Math.Min(panel2MinSize, Math.Max(25, availableWidth - panel1MinSize));

        if (splitContainer.Panel1MinSize != 25)
        {
            splitContainer.Panel1MinSize = 25;
        }

        if (splitContainer.Panel2MinSize != 25)
        {
            splitContainer.Panel2MinSize = 25;
        }

        var minimum = panel1MinSize;
        var maximum = splitContainer.ClientSize.Width - panel2MinSize;
        if (maximum < minimum)
        {
            return;
        }

        var preferredDistance = (int)(splitContainer.ClientSize.Width * PreferredPanel1Ratio);
        var safeDistance = Math.Clamp(preferredDistance, minimum, maximum);
        if (splitContainer.SplitterDistance != safeDistance)
        {
            splitContainer.SplitterDistance = safeDistance;
        }

        if (splitContainer.Panel1MinSize != panel1MinSize)
        {
            splitContainer.Panel1MinSize = panel1MinSize;
        }

        if (splitContainer.Panel2MinSize != panel2MinSize)
        {
            splitContainer.Panel2MinSize = panel2MinSize;
        }
    }

    private Control BuildSummaryPanel()
    {
        var host = CreateSurfacePanel();
        host.Margin = new Padding(0, 0, 10, 0);
        host.AutoScroll = true;
        host.MinimumSize = Size.Empty;

        var layout = new TableLayoutPanel
        {
            Location = Point.Empty,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UITheme.BackgroundColor,
            Margin = Padding.Empty,
            AutoSize = false,
            MinimumSize = Size.Empty,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Height = 600
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        // Share extra vertical room between products and payments when the
        // completed-sale window is enlarged; keep a usable baseline at its
        // normal size and let the panel scroll only below that baseline.
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 46));

        layout.Controls.Add(BuildMetadataPanel(), 0, 0);
        layout.Controls.Add(BuildLinesPanel(), 0, 1);
        layout.Controls.Add(BuildTotalsPanel(), 0, 2);
        layout.Controls.Add(BuildPaymentsPanel(), 0, 3);

        void ResizeLayout()
        {
            var scrollbarWidth = host.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
            layout.SetBounds(
                0,
                0,
                Math.Max(0, host.ClientSize.Width - scrollbarWidth),
                Math.Max(600, host.ClientSize.Height));
        }

        host.Resize += (_, _) => ResizeLayout();
        host.Controls.Add(layout);
        ResizeLayout();
        host.HorizontalScroll.Enabled = false;
        host.HorizontalScroll.Visible = false;
        return host;
    }

    private Control BuildMetadataPanel()
    {
        var panel = CreateSurfacePanel();
        panel.Margin = new Padding(0, 0, 0, 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(14, 10, 14, 10),
            BackColor = UITheme.SurfaceColor,
            AutoSize = false,
            MinimumSize = Size.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

        AddInfoRow(layout, 0, "Numero de venta", _receipt.NumeroVenta);
        AddInfoRow(layout, 1, "Fecha y hora", SaleReceiptViewModel.ToLocal(_receipt.FechaVenta).ToString("yyyy-MM-dd HH:mm"));
        AddInfoRow(layout, 2, "Empleado", _receipt.Empleado);
        AddInfoRow(layout, 3, "Cliente", _receipt.Cliente);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildLinesPanel()
    {
        ConfigureGrid(_gridLines);
        _gridLines.DataSource = _receipt.Lines.Select(SaleReceiptLineRow.FromLine).ToList();
        AddGridColumn(_gridLines, "Producto", "Producto", 190);
        AddGridColumn(_gridLines, "Cantidad", "Cant.", 62, numeric: true);
        AddGridColumn(_gridLines, "Precio", "Precio", 80, numeric: true);
        AddGridColumn(_gridLines, "Descuento", "Desc.", 80, numeric: true);
        AddGridColumn(_gridLines, "Total", "Total", 90, numeric: true);
        return CreateGridSection("Productos vendidos", _gridLines, new Padding(0, 0, 0, 10));
    }

    private Control BuildTotalsPanel()
    {
        var panel = CreateSurfacePanel();
        panel.Margin = new Padding(0, 0, 0, 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(14, 10, 14, 10),
            BackColor = UITheme.SurfaceColor,
            AutoSize = false,
            MinimumSize = Size.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        for (var i = 0; i < 5; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        }

        AddAmountRow(layout, 0, "Subtotal", _receipt.SubtotalCentavos, UITheme.TextDark);
        AddAmountRow(layout, 1, "Descuentos", _receipt.DescuentoCentavos, UITheme.DangerColor);
        AddAmountRow(layout, 2, "Total", _receipt.TotalCentavos, UITheme.SuccessColor);
        AddAmountRow(layout, 3, "Pagado", _receipt.TotalPagadoCentavos, UITheme.TextDark);
        AddAmountRow(layout, 4, "Vuelto", _receipt.CambioCentavos, UITheme.TextDark);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildPaymentsPanel()
    {
        ConfigureGrid(_gridPayments);
        _gridPayments.DataSource = _receipt.Payments.Select(SaleReceiptPaymentRow.FromPayment).ToList();
        AddGridColumn(_gridPayments, "Metodo", "Metodo", 105);
        AddGridColumn(_gridPayments, "Monto", "Monto", 90, numeric: true);
        AddGridColumn(_gridPayments, "Referencia", "Referencia", 170);
        return CreateGridSection("Métodos de pago", _gridPayments, new Padding(0));
    }

    private Control BuildPreviewPanel()
    {
        var panel = CreateSurfacePanel();
        panel.Margin = new Padding(10, 0, 0, 0);
        panel.Padding = new Padding(14);
        panel.AutoScroll = true;
        panel.MinimumSize = Size.Empty;

        var layout = new TableLayoutPanel
        {
            Location = Point.Empty,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            AutoSize = false,
            MinimumSize = Size.Empty,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Vista previa del ticket",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark
        };

        _txtPreview.Dock = DockStyle.Fill;
        _txtPreview.Multiline = true;
        _txtPreview.ReadOnly = true;
        _txtPreview.ScrollBars = ScrollBars.Both;
        _txtPreview.WordWrap = false;
        _txtPreview.Font = new Font("Consolas", 10, FontStyle.Regular);
        _txtPreview.BackColor = Color.White;
        _txtPreview.ForeColor = UITheme.TextDark;
        _txtPreview.Text = _receipt.BuildTicketText();

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(_txtPreview, 0, 1);

        void ResizeLayout()
        {
            layout.Size = panel.ClientSize;
        }

        panel.Resize += (_, _) => ResizeLayout();
        panel.Controls.Add(layout);
        ResizeLayout();
        panel.HorizontalScroll.Enabled = false;
        panel.HorizontalScroll.Visible = false;
        return panel;
    }

    private Control BuildActions()
    {
        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = UITheme.BackgroundColor,
            Padding = new Padding(0, 14, 0, 4),
            AutoSize = false,
            MinimumSize = Size.Empty
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));

        _btnSavePdf.Text = "Guardar";
        _btnSavePdf.Dock = DockStyle.Fill;
        _btnSavePdf.Margin = new Padding(0, 0, 10, 0);
        _btnSavePdf.Click += (_, _) => SavePdf();
        UITheme.StyleSecondaryButton(_btnSavePdf);

        _btnPrintFuture.Text = "Ticket: no configurado";
        _btnPrintFuture.Enabled = false;
        _btnPrintFuture.Dock = DockStyle.Fill;
        _btnPrintFuture.Margin = new Padding(0, 0, 10, 0);
        UITheme.StyleSecondaryButton(_btnPrintFuture);

        _btnEmailFuture.Text = "Correo futuro";
        _btnEmailFuture.Enabled = false;
        _btnEmailFuture.Dock = DockStyle.Fill;
        _btnEmailFuture.Margin = new Padding(0, 0, 10, 0);
        UITheme.StyleSecondaryButton(_btnEmailFuture);

        _btnPrimary.Text = _primaryButtonText;
        _btnPrimary.Dock = DockStyle.Fill;
        _btnPrimary.Click += (_, _) => CompletePrimaryAction();
        UITheme.StyleSuccessButton(_btnPrimary);

        actions.Controls.Add(new Label(), 0, 0);
        actions.Controls.Add(_btnPrintFuture, 1, 0);
        actions.Controls.Add(_btnEmailFuture, 2, 0);
        actions.Controls.Add(_btnSavePdf, 3, 0);
        actions.Controls.Add(_btnPrimary, 4, 0);
        return actions;
    }

    private void SaleReceiptForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!_isCompletedSale || e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.SuppressKeyPress = true;
        e.Handled = true;
        CompletePrimaryAction();
    }

    private void CompletePrimaryAction()
    {
        if (_primaryActionHandled)
        {
            return;
        }

        _primaryActionHandled = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SavePdf()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Guardar recibo de venta en PDF",
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
            var pdfWatch = System.Diagnostics.Stopwatch.StartNew();
            SaleReceiptPdfExporter.Save(_receipt, dialog.FileName);
            LogReceiptPerformance($"Venta {_receipt.NumeroVenta}: pdf-generation={pdfWatch.ElapsedMilliseconds}ms.");
            MessageBox.Show("Recibo de venta guardado en PDF.", "Ticket de venta", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Guardar PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void LogReceiptPerformance(string message)
    {
        var logMessage = $"[{DateTime.Now:O}] {message}{Environment.NewLine}";
        System.Diagnostics.Trace.TraceInformation(logMessage);

        try
        {
            var logDirectory = ArteInventariado.Infrastructure.ArteInventariadoPathProvider
                .CreateDefault().LogsRoot;
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(Path.Combine(logDirectory, "pos-performance.log"), logMessage);
        }
        catch
        {
            System.Diagnostics.Trace.TraceWarning("Could not write receipt performance log.");
        }
    }

    private static Panel CreateSurfacePanel()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(0)
        };
    }

    private static Control CreateGridSection(string title, DataGridView grid, Padding margin)
    {
        var panel = CreateSurfacePanel();
        panel.Margin = margin;
        panel.Padding = new Padding(12);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            AutoSize = false,
            MinimumSize = Size.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark,
            AutoEllipsis = false
        };

        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Dock = DockStyle.Fill;
        grid.MultiSelect = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.ScrollBars = ScrollBars.Both;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        UITheme.StyleDataGridView(grid);
        grid.ColumnHeadersHeight = 36;
    }

    private static void AddGridColumn(
        DataGridView grid,
        string propertyName,
        string headerText,
        int width,
        bool numeric = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            MinimumWidth = width,
            FillWeight = width
        };
        if (numeric)
        {
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }

        grid.Columns.Add(column);
    }

    private static void AddInfoRow(TableLayoutPanel layout, int row, string labelText, string valueText)
    {
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor,
            AutoEllipsis = false
        };

        var value = new Label
        {
            Text = valueText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            AutoEllipsis = true
        };

        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(value, 1, row);
    }

    private static void AddAmountRow(TableLayoutPanel layout, int row, string labelText, long centavos, Color amountColor)
    {
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor
        };

        var amount = new Label
        {
            Text = SaleReceiptViewModel.FormatDollars(centavos),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = UITheme.NormalBoldFont,
            ForeColor = amountColor,
            AutoEllipsis = false,
            Padding = new Padding(8, 0, 0, 0)
        };

        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(amount, 1, row);
    }

    private sealed record SaleReceiptLineRow(
        string Producto,
        int Cantidad,
        string Precio,
        string Descuento,
        string Total)
    {
        public static SaleReceiptLineRow FromLine(SaleReceiptLine line)
        {
            return new SaleReceiptLineRow(
                line.Producto,
                line.Cantidad,
                SaleReceiptViewModel.FormatDollars(line.PrecioUnitarioCentavos),
                SaleReceiptViewModel.FormatDollars(line.DescuentoCentavos),
                SaleReceiptViewModel.FormatDollars(line.TotalCentavos));
        }
    }

    private sealed record SaleReceiptPaymentRow(
        string Metodo,
        string Monto,
        string Referencia)
    {
        public static SaleReceiptPaymentRow FromPayment(SaleReceiptPayment payment)
        {
            return new SaleReceiptPaymentRow(
                payment.MetodoPago.ToString(),
                SaleReceiptViewModel.FormatDollars(payment.MontoCentavos),
                payment.Referencia ?? string.Empty);
        }
    }
}
