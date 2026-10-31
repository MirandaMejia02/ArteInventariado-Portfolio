using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public partial class EmployeePOSForm
{
    private readonly Label _lblQuickCartCustomer = new();
    private readonly Label _lblQuickCheckoutChange = new();

    private void ShowProductPreview(PosSkuSaleItem item)
    {
        using var preview = new ProductQuickPreviewForm(item, _productAssetService);
        try
        {
            preview.ShowDialog(this);
        }
        finally
        {
            FocusBarcodeBox();
        }
    }

    private void BuildQuickUi()
    {
        Text = "ArteInventariado - Punto de venta rápido";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        ClientSize = new Size(1366, 768);
        MinimumSize = new Size(1180, 700);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        KeyDown += PosForm_KeyDown;
        UITheme.StyleForm(this);
        BackColor = UITheme.BackgroundColor;

        ConfigureQuickSharedInputs();

        var header = BuildQuickHeader();
        var shortcutBar = BuildQuickShortcutBar();
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(14, 12, 14, 10),
            Margin = Padding.Empty,
            BackColor = UITheme.BackgroundColor
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64.5f));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35.5f));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.Controls.Add(BuildQuickCatalog(), 0, 0);
        var cart = BuildQuickCart();
        cart.MinimumSize = new Size(320, 0);
        workspace.Controls.Add(cart, 2, 0);

        void ApplyResponsiveWorkspaceColumns()
        {
            var width = workspace.ClientSize.Width;
            var (catalogPercent, cartPercent) = width switch
            {
                <= 1150 => (55f, 45f),
                <= 1700 => (64.5f, 35.5f),
                _ => (64.5f, 35.5f)
            };

            if (Math.Abs(workspace.ColumnStyles[0].Width - catalogPercent) < 0.1f &&
                Math.Abs(workspace.ColumnStyles[2].Width - cartPercent) < 0.1f)
            {
                return;
            }

            workspace.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, catalogPercent);
            workspace.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, 12);
            workspace.ColumnStyles[2] = new ColumnStyle(SizeType.Percent, cartPercent);
        }

        workspace.SizeChanged += (_, _) => ApplyResponsiveWorkspaceColumns();
        workspace.Layout += (_, _) => ApplyResponsiveWorkspaceColumns();
        ApplyResponsiveWorkspaceColumns();

        Controls.Add(workspace);
        Controls.Add(shortcutBar);
        Controls.Add(header);
        BuildPaymentOverlay();
        UpdatePaymentReferenceStates();
        RecalculateTotals();
    }

    private void ConfigureQuickSharedInputs()
    {
        ConfigureInteger(_numQuantity, 0, 0);
        _numQuantity.Minimum = 1;
        _numQuantity.Value = 1;
        ConfigureMoney(_numLineDiscount, 0, 0);
        ConfigureMoney(_numGeneralDiscount, 0, 0);
        ConfigureMoney(_numCashReceived, 0, 0);
        ConfigureMoney(_numPaymentCashAmount, 0, 0);
        SelectNumericTextOnFocus(_numPaymentCashAmount);
        ConfigureMoney(_numTransferAmount, 0, 0);
        SelectNumericTextOnFocus(_numTransferAmount);
        ConfigureMoney(_numBitcoinAmount, 0, 0);
        SelectNumericTextOnFocus(_numBitcoinAmount);
        UITheme.StyleTextBox(_txtLineDiscountReason);
        UITheme.StyleTextBox(_txtGeneralDiscountReason);
        UITheme.StyleTextBox(_txtTransferReference);
        UITheme.StyleTextBox(_txtBitcoinReference);
        SetupQuickReasons(_cmbQuickLineReason, _txtLineDiscountReason);
        SetupQuickReasons(_cmbQuickGeneralReason, _txtGeneralDiscountReason);

        _numGeneralDiscount.ValueChanged += (_, _) =>
        {
            if (_restoringDraftState)
            {
                return;
            }

            InvalidateAcceptedPayment();
            RecalculateTotals();
        };
        _numCashReceived.ValueChanged += (_, _) => QuickCashPaymentChanged();
        _numCashReceived.TextChanged += (_, _) => QuickCashPaymentChanged();
        _numCashReceived.KeyDown += NumCashReceived_KeyDown;
        _numPaymentCashAmount.ValueChanged += (_, _) => UpdateOverlayTotals();
        _numPaymentCashAmount.TextChanged += (_, _) => UpdateOverlayTotals();
        _numTransferAmount.ValueChanged += (_, _) => PaymentAmountChanged();
        _numTransferAmount.TextChanged += (_, _) => PaymentAmountChanged();
        _numBitcoinAmount.ValueChanged += (_, _) => PaymentAmountChanged();
        _numBitcoinAmount.TextChanged += (_, _) => PaymentAmountChanged();
    }

    private Control BuildQuickHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 88,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(18, 8, 20, 8),
            Margin = Padding.Empty,
            BackColor = UITheme.SurfaceColor
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(UITheme.CreateLogoPictureBox(60), 0, 0);

        var identity = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        identity.Controls.Add(new Label
        {
            Text = "Punto de venta rápido",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = UITheme.PrimaryColor,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        _lblCustomer.Text = "Cliente: Consumidor final";
        _lblCustomer.Dock = DockStyle.Fill;
        _lblCustomer.Font = UITheme.NormalFont;
        _lblCustomer.ForeColor = UITheme.TextDark;
        _lblCustomer.TextAlign = ContentAlignment.TopLeft;
        identity.Controls.Add(_lblCustomer, 0, 1);
        header.Controls.Add(identity, 1, 0);

        var user = _currentSession.UsuarioActual;
        header.Controls.Add(new Label
        {
            Text = user?.NombreCompleto ?? user?.NombreUsuario ?? "Usuario",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            AutoEllipsis = true,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark
        }, 2, 0);

        _lblCashStatus.AutoSize = false;
        _lblCashStatus.Dock = DockStyle.Fill;
        _lblCashStatus.TextAlign = ContentAlignment.MiddleCenter;
        _lblCashStatus.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _lblCashStatus.AutoEllipsis = false;
        _lblCashStatus.Margin = Padding.Empty;
        header.Controls.Add(_lblCashStatus, 4, 0);
        return header;
    }

    private Control BuildQuickCatalog()
    {
        var host = new ResponsiveCapturePanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = UITheme.BackgroundColor
        };

        var capture = new ResponsiveCapturePanel
        {
            Dock = DockStyle.None,
            // The themed action buttons have a 38px minimum height; keep the
            // label plus that minimum inside the capture panel at narrow sizes.
            Size = new Size(0, 56),
            Padding = Padding.Empty,
            BackColor = UITheme.BackgroundColor
        };
        var captureLabel = new Label
        {
            Text = "Escanear código / SKU o escribir y presionar Enter",
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = UITheme.MutedTextColor
        };
        capture.Controls.Add(captureLabel);
        var barcodeInputHost = new Panel
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false
        };
        _txtBarcode.Dock = DockStyle.Fill;
        _txtBarcode.Margin = new Padding(0, 2, 10, 4);
        _txtBarcode.Font = new Font("Segoe UI", 13, FontStyle.Regular);
        _txtBarcode.MaxLength = 128;
        _txtBarcode.PlaceholderText = "Escanear código / SKU o escribir y presionar Enter";
        _txtBarcode.AccessibleName = "Entrada principal de código o escáner HID";
        UITheme.StyleInput(_txtBarcode);
        _txtBarcode.KeyDown += TxtBarcode_KeyDown;
        barcodeInputHost.Controls.Add(_txtBarcode);
        capture.Controls.Add(barcodeInputHost);

        _btnSearch.Text = "Buscar · F2";
        _btnSearch.AutoSize = false;
        _btnSearch.Margin = Padding.Empty;
        _btnSearch.Click += (_, _) => FocusSearchBox();
        UITheme.StylePrimaryButton(_btnSearch);
        _btnSearch.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        capture.Controls.Add(_btnSearch);

        var customerButton = new Button { Text = "Cliente\r\nF3", AutoSize = false, Margin = Padding.Empty };
        customerButton.Click += async (_, _) => await SelectQuickCustomerAsync();
        UITheme.StyleSecondaryButton(customerButton);
        customerButton.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _shortcutToolTip.SetToolTip(customerButton, "Cliente · F3");
        capture.Controls.Add(customerButton);

        void LayoutCapture()
        {
            if (capture.ClientSize.Width <= 0 || capture.ClientSize.Height <= 0)
            {
                return;
            }

            const int labelHeight = 18;
            const int gap = 10;
            var contentHeight = Math.Max(1, capture.ClientSize.Height - labelHeight);
            var searchWidth = Math.Max(
                120,
                TextRenderer.MeasureText(
                    _btnSearch.Text,
                    _btnSearch.Font,
                    Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 24);
            var customerWidth = Math.Max(
                104,
                TextRenderer.MeasureText(
                    "Cliente",
                    customerButton.Font,
                    Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 24);
            var inputWidth = Math.Max(
                1,
                capture.ClientSize.Width - searchWidth - customerWidth - (gap * 2));

            captureLabel.SetBounds(0, 0, capture.ClientSize.Width, labelHeight);
            barcodeInputHost.SetBounds(0, labelHeight, inputWidth, contentHeight);
            _btnSearch.SetBounds(inputWidth + gap, labelHeight, searchWidth, contentHeight);
            customerButton.SetBounds(
                inputWidth + gap + searchWidth + gap,
                labelHeight,
                customerWidth,
                contentHeight);
        }

        capture.Resize += (_, _) => LayoutCapture();
        LayoutCapture();
        host.Controls.Add(capture);

        var filters = new ResponsiveCapturePanel
        {
            Dock = DockStyle.None,
            Size = new Size(0, 136),
            BackColor = UITheme.BackgroundColor
        };
        _txtSearch.Dock = DockStyle.None;
        _txtSearch.AutoSize = false;
        _txtSearch.Margin = new Padding(0, 5, 6, 5);
        _txtSearch.PlaceholderText = "Buscar por nombre, código, SKU, categoría o subcategoría";
        _txtSearch.Font = UITheme.NormalFont;
        UITheme.StyleInput(_txtSearch);
        _txtSearch.KeyDown += TxtSearch_KeyDown;
        filters.Controls.Add(_txtSearch);
        ConfigureFilterCombo(_cmbCategoryFilter, 0, 0, 160);
        _cmbCategoryFilter.Dock = DockStyle.None;
        _cmbCategoryFilter.AutoSize = false;
        _cmbCategoryFilter.Margin = new Padding(2, 5, 4, 5);
        filters.Controls.Add(_cmbCategoryFilter);
        ConfigureFilterCombo(_cmbSubcategoryFilter, 0, 0, 190);
        _cmbSubcategoryFilter.Dock = DockStyle.None;
        _cmbSubcategoryFilter.AutoSize = false;
        _cmbSubcategoryFilter.Margin = new Padding(2, 5, 0, 5);
        filters.Controls.Add(_cmbSubcategoryFilter);
        ConfigureFilterCombo(_cmbStockFilter, 0, 0, 130);
        _cmbStockFilter.Dock = DockStyle.None;
        _cmbStockFilter.AutoSize = false;
        _cmbStockFilter.Margin = new Padding(0, 5, 4, 5);
        filters.Controls.Add(_cmbStockFilter);
        ConfigureFilterCombo(_cmbLocationFilter, 0, 0, 190);
        _cmbLocationFilter.Dock = DockStyle.None;
        _cmbLocationFilter.AutoSize = false;
        _cmbLocationFilter.Margin = new Padding(2, 5, 4, 5);
        filters.Controls.Add(_cmbLocationFilter);
        ConfigureFilterCombo(_cmbSort, 0, 0, 180);
        _cmbSort.Dock = DockStyle.None;
        _cmbSort.AutoSize = false;
        _cmbSort.Margin = new Padding(2, 5, 4, 5);
        filters.Controls.Add(_cmbSort);

        var pagination = new ResponsiveCapturePanel
        {
            Dock = DockStyle.None,
            Size = new Size(0, 48),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = UITheme.BackgroundColor
        };
        _btnPreviousPage.Text = "‹";
        _btnPreviousPage.Dock = DockStyle.None;
        _btnPreviousPage.AutoSize = false;
        _btnPreviousPage.Margin = Padding.Empty;
        _btnPreviousPage.Click += (_, _) => ChangeProductsPage(-1);
        UITheme.StyleSecondaryButton(_btnPreviousPage);
        pagination.Controls.Add(_btnPreviousPage);
        _lblPageInfo.Dock = DockStyle.None;
        _lblPageInfo.TextAlign = ContentAlignment.MiddleCenter;
        _lblPageInfo.Font = UITheme.NormalBoldFont;
        _lblPageInfo.AutoSize = false;
        _lblPageInfo.AutoEllipsis = false;
        _lblPageInfo.MinimumSize = new Size(120, 0);
        _lblPageInfo.Margin = Padding.Empty;
        pagination.Controls.Add(_lblPageInfo);
        _btnNextPage.Text = "›";
        _btnNextPage.Dock = DockStyle.None;
        _btnNextPage.AutoSize = false;
        _btnNextPage.Margin = Padding.Empty;
        _btnNextPage.Click += (_, _) => ChangeProductsPage(1);
        UITheme.StyleSecondaryButton(_btnNextPage);
        pagination.Controls.Add(_btnNextPage);

        void LayoutPagination()
        {
            if (pagination.ClientSize.Width <= 0 || pagination.ClientSize.Height <= 0)
            {
                return;
            }

            var arrowWidth = Math.Max(
                52,
                TextRenderer.MeasureText(
                    "‹",
                    _btnPreviousPage.Font,
                    Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 36);
            var pageWidth = Math.Max(
                120,
                TextRenderer.MeasureText(
                    "Página 00 de 00",
                    _lblPageInfo.Font,
                    Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 12);
            var availablePageWidth = Math.Max(1, pagination.ClientSize.Width - (arrowWidth * 2));
            pageWidth = Math.Min(pageWidth, availablePageWidth);
            _btnPreviousPage.SetBounds(0, 0, arrowWidth, pagination.ClientSize.Height);
            _lblPageInfo.SetBounds(arrowWidth, 0, pageWidth, pagination.ClientSize.Height);
            _btnNextPage.SetBounds(
                pagination.ClientSize.Width - arrowWidth,
                0,
                arrowWidth,
                pagination.ClientSize.Height);
        }

        pagination.Resize += (_, _) => LayoutPagination();
        LayoutPagination();
        filters.Controls.Add(pagination);
        host.Controls.Add(filters);

        _lblScannerStatus.Dock = DockStyle.Fill;
        _lblScannerStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblScannerStatus.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _lblScannerStatus.ForeColor = UITheme.MutedTextColor;
        _lblScannerStatus.AutoEllipsis = true;
        _lblScannerStatus.Dock = DockStyle.None;
        host.Controls.Add(_lblScannerStatus);

        ConfigureProductsPanel();
        _productsPanel.Dock = DockStyle.None;
        _productsPanel.Padding = new Padding(0, 4, 0, 0);
        host.Controls.Add(_productsPanel);

        void LayoutFilters()
        {
            if (filters.ClientSize.Width <= 0 || filters.ClientSize.Height <= 0)
            {
                return;
            }

            var compact = filters.ClientSize.Width < 920;
            var rowHeight = Math.Max(
                34,
                Math.Max(_btnPreviousPage.MinimumSize.Height + 2,
                    Math.Max(
                    _txtSearch.PreferredSize.Height,
                    Math.Max(_cmbCategoryFilter.PreferredSize.Height, _cmbSubcategoryFilter.PreferredSize.Height)) + 6));
            var contentWidth = filters.ClientSize.Width;

            if (compact)
            {
                var halfWidth = Math.Max(1, (contentWidth - 6) / 2);
                var searchHeight = rowHeight;
                _txtSearch.SetBounds(0, 0, contentWidth, searchHeight);
                _cmbCategoryFilter.SetBounds(0, rowHeight, halfWidth, rowHeight);
                _cmbSubcategoryFilter.SetBounds(halfWidth + 6, rowHeight, contentWidth - halfWidth - 6, rowHeight);
                _cmbStockFilter.SetBounds(0, rowHeight * 2, halfWidth, rowHeight);
                _cmbLocationFilter.SetBounds(halfWidth + 6, rowHeight * 2, contentWidth - halfWidth - 6, rowHeight);
                _cmbSort.SetBounds(0, rowHeight * 3, halfWidth, rowHeight);
                pagination.SetBounds(halfWidth + 6, rowHeight * 3, contentWidth - halfWidth - 6, rowHeight);
            }
            else
            {
                var firstWidth = (int)Math.Round(contentWidth * 0.42);
                var secondWidth = (int)Math.Round(contentWidth * 0.23);
                var thirdWidth = contentWidth - firstWidth - secondWidth;
                var firstRowHeight = rowHeight;
                var secondRowTop = firstRowHeight;
                var secondRowHeight = Math.Max(rowHeight, filters.ClientSize.Height - secondRowTop);
                var filterWidth = contentWidth / 4;

                _txtSearch.SetBounds(0, 0, firstWidth, firstRowHeight);
                _cmbCategoryFilter.SetBounds(firstWidth, 0, secondWidth, firstRowHeight);
                _cmbSubcategoryFilter.SetBounds(firstWidth + secondWidth, 0, thirdWidth, firstRowHeight);
                _cmbStockFilter.SetBounds(0, secondRowTop, filterWidth, secondRowHeight);
                _cmbLocationFilter.SetBounds(filterWidth, secondRowTop, filterWidth, secondRowHeight);
                _cmbSort.SetBounds(filterWidth * 2, secondRowTop, filterWidth, secondRowHeight);
                pagination.SetBounds(filterWidth * 3, secondRowTop, contentWidth - (filterWidth * 3), secondRowHeight);
            }
        }

        void LayoutCatalog()
        {
            if (host.ClientSize.Width <= 0 || host.ClientSize.Height <= 0)
            {
                return;
            }

            var compact = host.ClientSize.Width < 920;
            var captureHeight = Math.Max(1, capture.Height);
            var statusHeight = Math.Max(28, _lblScannerStatus.PreferredSize.Height + 8);
            var filterRowHeight = Math.Max(
                34,
                Math.Max(_btnPreviousPage.MinimumSize.Height + 2,
                    Math.Max(
                    _txtSearch.PreferredSize.Height,
                    Math.Max(_cmbCategoryFilter.PreferredSize.Height, _cmbSubcategoryFilter.PreferredSize.Height)) + 6));
            var filtersHeight = compact
                ? filterRowHeight * 4
                : filterRowHeight * 2;
            var remainingHeight = Math.Max(1, host.ClientSize.Height - captureHeight - filtersHeight - statusHeight);

            capture.SetBounds(0, 0, host.ClientSize.Width, captureHeight);
            filters.SetBounds(0, captureHeight, host.ClientSize.Width, filtersHeight);
            _lblScannerStatus.SetBounds(0, captureHeight + filtersHeight, host.ClientSize.Width, statusHeight);
            _productsPanel.SetBounds(
                0,
                captureHeight + filtersHeight + statusHeight,
                host.ClientSize.Width,
                remainingHeight);
            // Reapply the nested capture layout after the parent bounds settle.
            // During narrow-window resizes the first Resize event can run before
            // the final height is committed, leaving the action buttons below
            // the capture panel by a few pixels.
            LayoutCapture();
            LayoutFilters();
        }

        host.Resize += (_, _) => LayoutCatalog();
        filters.Resize += (_, _) => LayoutFilters();
        LayoutCatalog();

        _lblResultsInfo.Visible = false;
        return host;
    }

    private Control BuildQuickCart()
    {
        var surface = CreateSurfacePanel();
        surface.Margin = Padding.Empty;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(14, 8, 22, 12),
            BackColor = UITheme.SurfaceColor
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.Controls.Add(new Label
        {
            Text = "Venta actual",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = UITheme.PrimaryColor,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        ConfigureCartGrid();
        _gridCart.Dock = DockStyle.Fill;
        _gridCart.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(BuildDraftTabs(), 0, 1);
        layout.Controls.Add(_gridCart, 0, 2);
        layout.Controls.Add(BuildQuickLineEditor(), 0, 3);

        var customerRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        customerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        customerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        _lblQuickCartCustomer.Text = "Cliente: Consumidor final";
        _lblQuickCartCustomer.Dock = DockStyle.Fill;
        _lblQuickCartCustomer.TextAlign = ContentAlignment.MiddleLeft;
        _lblQuickCartCustomer.Font = UITheme.NormalFont;
        _lblQuickCartCustomer.AutoEllipsis = true;
        customerRow.Controls.Add(_lblQuickCartCustomer, 0, 0);
        var changeCustomer = new Button { Text = "Cambiar", Dock = DockStyle.Fill, Margin = new Padding(4, 6, 0, 6) };
        changeCustomer.Click += async (_, _) => await SelectQuickCustomerAsync();
        UITheme.StyleOutlineButton(changeCustomer);
        customerRow.Controls.Add(changeCustomer, 1, 0);
        layout.Controls.Add(customerRow, 0, 4);

        var totals = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        totals.RowStyles.Add(new RowStyle(SizeType.Percent, 26));
        totals.RowStyles.Add(new RowStyle(SizeType.Percent, 26));
        totals.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        ConfigureSummaryLabel(_lblSubtotal, UITheme.TextDark, UITheme.NormalBoldFont);
        ConfigureSummaryLabel(_lblDiscounts, UITheme.DangerColor, UITheme.NormalBoldFont);
        ConfigureSummaryLabel(_lblTotal, UITheme.SuccessColor, new Font("Segoe UI", 19, FontStyle.Bold));
        totals.Controls.Add(_lblSubtotal, 0, 0);
        totals.Controls.Add(_lblDiscounts, 0, 1);
        totals.Controls.Add(_lblTotal, 0, 2);
        layout.Controls.Add(totals, 0, 5);

        var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _btnDiscounts.Text = "Descuento · F6";
        _btnDiscounts.Dock = DockStyle.Fill;
        _btnDiscounts.Margin = new Padding(0, 4, 5, 4);
        _btnDiscounts.Click += (_, _) => ShowDiscountOverlay();
        UITheme.StyleSecondaryButton(_btnDiscounts);
        tools.Controls.Add(_btnDiscounts, 0, 0);
        _btnClear.Text = "Cancelar venta · ESC";
        _btnClear.Dock = DockStyle.Fill;
        _btnClear.Margin = new Padding(5, 4, 0, 4);
        _btnClear.Click += (_, _) => ConfirmAndClearQuickCart();
        UITheme.StyleDangerButton(_btnClear);
        tools.Controls.Add(_btnClear, 1, 0);
        layout.Controls.Add(tools, 0, 6);

        _btnRegister.Text = "COBRAR  F8";
        _btnRegister.Dock = DockStyle.Fill;
        _btnRegister.Margin = new Padding(0, 6, 0, 0);
        _btnRegister.Font = new Font("Segoe UI", 16, FontStyle.Bold);
        _btnRegister.Click += (_, _) => ShowQuickCheckoutOverlay();
        UITheme.StyleSuccessButton(_btnRegister);
        layout.Controls.Add(_btnRegister, 0, 7);

        _btnOtherMethods.Visible = false;
        _btnAdd.Visible = false;
        _btnUpdateLine.Visible = false;
        _btnRemove.Visible = false;
        _lblPaid.Visible = false;
        _lblMissing.Visible = false;
        _lblChange.Visible = false;
        _lblQuickCashChange.Visible = false;
        _lblPaymentMethodStatus.Visible = false;
        surface.Controls.Add(layout);
        return surface;
    }

    private Control BuildQuickLineEditor()
    {
        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(0, 4, 0, 4),
            BackColor = UITheme.SoftPinkColor
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        _lblSelectedLine.Text = "Seleccione una línea para cambiar cantidad";
        _lblSelectedLine.Dock = DockStyle.Fill;
        _lblSelectedLine.Font = UITheme.NormalBoldFont;
        _lblSelectedLine.TextAlign = ContentAlignment.MiddleLeft;
        _lblSelectedLine.AutoEllipsis = true;
        editor.Controls.Add(_lblSelectedLine, 0, 0);
        editor.SetColumnSpan(_lblSelectedLine, 4);
        editor.Controls.Add(new Label { Text = "Cantidad · F4", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 1);
        _numQuantity.Dock = DockStyle.Fill;
        _numQuantity.Margin = new Padding(6, 2, 6, 2);
        editor.Controls.Add(_numQuantity, 2, 1);
        var apply = new Button { Text = "Actualizar", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 4, 2) };
        apply.Click += BtnUpdateLine_Click;
        UITheme.StyleSecondaryButton(apply);
        editor.Controls.Add(apply, 3, 1);
        return editor;
    }

    private static Control BuildQuickShortcutBar()
    {
        return new Label
        {
            Text = "F2  Buscar    ·    F3  Cliente    ·    F4  Cantidad    ·    F6  Descuento    ·    F8  Cobrar    ·    ESC  Cancelar",
            Dock = DockStyle.Bottom,
            Height = 42,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = UITheme.SecondaryColor,
            BackColor = UITheme.SurfaceColor
        };
    }

    private sealed class ResponsiveCapturePanel : Panel
    {
        public override Size GetPreferredSize(Size proposedSize) => Size.Empty;
    }

    private bool HandleQuickPosShortcut(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F2:
                FocusSearchBox();
                break;
            case Keys.F3:
                _ = SelectQuickCustomerAsync();
                break;
            case Keys.F4:
                if (_gridCart.Rows.Count > 0 && _gridCart.CurrentRow is null)
                {
                    _gridCart.Rows[0].Selected = true;
                }
                _numQuantity.Focus();
                _numQuantity.Select(0, _numQuantity.Text.Length);
                break;
            case Keys.F6:
                ShowDiscountOverlay();
                break;
            case Keys.F8:
                ShowQuickCheckoutOverlay();
                break;
            case Keys.Escape:
                if (_cart.Count > 0)
                {
                    ConfirmAndClearQuickCart();
                }
                else
                {
                    Close();
                }
                break;
            default:
                return false;
        }

        e.SuppressKeyPress = true;
        e.Handled = true;
        return true;
    }

    private async Task SelectQuickCustomerAsync()
    {
        if (!await _permissionService.HasPermissionAsync(PermisoEmpleado.CLIENTES_ACCEDER))
        {
            MessageBox.Show(
                "No tiene permisos para seleccionar clientes.",
                "Punto de venta rápido",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            FocusBarcodeBox();
            return;
        }

        if (_customerQueryService is null)
        {
            MessageBox.Show("La selección de clientes no está disponible en esta sesión.", "Punto de venta rápido", MessageBoxButtons.OK, MessageBoxIcon.Information);
            FocusBarcodeBox();
            return;
        }

        using var picker = new CustomerSalePickerForm(_customerQueryService);
        if (picker.ShowDialog(this) == DialogResult.OK && picker.SelectedCustomer is not null)
        {
            SetSelectedCustomer(picker.SelectedCustomer);
        }
        else if (picker.DialogResult == DialogResult.OK)
        {
            _selectedCustomerId = null;
            _selectedCustomerName = "Consumidor final";
            UpdateCustomerLabel();
        }
        FocusBarcodeBox();
        await Task.CompletedTask;
    }

    private void ConfirmAndClearQuickCart()
    {
        if (_cart.Count == 0 || MessageBox.Show(
                "¿Cancelar la venta actual y vaciar el carrito?",
                "Punto de venta rápido",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
        {
            ClearCart();
        }
    }

    private void ShowQuickCheckoutOverlay()
    {
        if (_saleProcessing)
        {
            return;
        }

        if (_pendingCreditRequest is not null)
        {
            _ = RegisterCurrentSaleAsync();
            return;
        }

        if (_cart.Count == 0)
        {
            MessageBox.Show("El carrito está vacío.", "Punto de venta rápido", MessageBoxButtons.OK, MessageBoxIcon.Information);
            FocusBarcodeBox();
            return;
        }

        _activeOverlayPanel = PosOverlayPanel.OtherPayments;
        ResetActiveOverlayActions();
        SetOverlayTitle("Cobro rápido");
        SetOverlayActionsVisible(true);
        AddPaymentMethodButtons(includeCashButton: true);
        _numPaymentCashAmount.Value = CentavosToDollars(
            _currentPaymentMethod == "EFECTIVO" ? GetMoneyCentavos(_numCashReceived) : _acceptedCash);
        _numTransferAmount.Value = CentavosToDollars(_acceptedTransfer);
        _txtTransferReference.Text = _acceptedTransferRef;
        _numBitcoinAmount.Value = CentavosToDollars(_acceptedBitcoin);
        _txtBitcoinReference.Text = _acceptedBitcoinRef;

        _pnlPaymentOverlay.Visible = true;
        _pnlPaymentOverlay.BringToFront();
        CenterPaymentModal();
        UpdateOverlayTotals();

        var selected = _currentPaymentMethod;
        if (!IsPaymentMethodAllowed(selected))
        {
            selected = "EFECTIVO";
        }

        foreach (Control control in _pnlPaymentMethodButtons.Controls)
        {
            if (control is Button button && button.Tag is string key && key == selected)
            {
                SelectPaymentMethod(key, button);
                return;
            }
        }

        SelectPaymentMethodByKey("EFECTIVO");
    }

    private void SelectQuickCashPayment(Button selectedButton)
    {
        ReturnToDefaultCashPayment();
        foreach (Control control in _pnlPaymentMethodButtons.Controls)
        {
            if (control is Button button)
            {
                UITheme.StyleSecondaryButton(button);
            }
        }
        UITheme.StylePrimaryButton(selectedButton);

        _numPaymentCashAmount.Value = CentavosToDollars(GetMoneyCentavos(_numCashReceived));
        _pnlPaymentMethodContent.Controls.Clear();
        var cash = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(12, 10, 12, 0)
        };
        cash.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        cash.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        cash.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        cash.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        cash.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        cash.Controls.Add(CreateFieldLabel("Efectivo recibido"), 0, 0);
        cash.Controls.Add(CreateFieldLabel("Vuelto"), 1, 0);
        _numPaymentCashAmount.Dock = DockStyle.Fill;
        _numPaymentCashAmount.Margin = new Padding(0, 4, 18, 4);
        _numPaymentCashAmount.Font = new Font("Segoe UI", 22, FontStyle.Bold);
        cash.Controls.Add(_numPaymentCashAmount, 0, 1);
        _lblQuickCheckoutChange.Dock = DockStyle.Fill;
        _lblQuickCheckoutChange.TextAlign = ContentAlignment.MiddleLeft;
        _lblQuickCheckoutChange.Font = new Font("Segoe UI", 22, FontStyle.Bold);
        _lblQuickCheckoutChange.ForeColor = UITheme.SuccessColor;
        cash.Controls.Add(_lblQuickCheckoutChange, 1, 1);

        var denominations = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0)
        };
        foreach (var amount in new[] { 5m, 10m, 20m, 50m, 100m })
        {
            var button = new Button { Text = $"${amount:0}", Size = new Size(72, 42), Margin = new Padding(0, 0, 7, 0) };
            button.Click += (_, _) => _numPaymentCashAmount.Value = Math.Min(amount, _numPaymentCashAmount.Maximum);
            UITheme.StyleSecondaryButton(button);
            denominations.Controls.Add(button);
        }
        var exact = new Button { Text = "Exacto", Size = new Size(88, 42), Margin = Padding.Empty };
        exact.Click += (_, _) => _numPaymentCashAmount.Value = CentavosToDollars(GetCurrentTotalCentavos());
        UITheme.StyleSecondaryButton(exact);
        denominations.Controls.Add(exact);
        cash.Controls.Add(denominations, 0, 2);
        cash.SetColumnSpan(denominations, 2);
        _pnlPaymentMethodContent.Controls.Add(cash);
        _activeOverlayPrimaryControl = _numPaymentCashAmount;
        UpdateOverlayTotals();
        FocusOverlayPrimaryControl();
    }

    private async Task ConfirmQuickCheckoutAsync()
    {
        if (_saleProcessing || !_pnlPaymentOverlay.Visible)
        {
            return;
        }

        if (_currentPaymentMethod == "EFECTIVO")
        {
            CommitMoneyInput(_numPaymentCashAmount);
            SetCashReceivedCentavos(GetMoneyCentavos(_numPaymentCashAmount), markAsManual: true);
            RefreshPaymentState();
            if (!AcceptedPaymentCoversCurrentTotal(out var cashMessage))
            {
                MessageBox.Show(cashMessage, "Cobro rápido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _numPaymentCashAmount.Focus();
                return;
            }
            HidePaymentOverlay(save: false);
        }
        else
        {
            ValidateAndAcceptPayment();
            if (_pnlPaymentOverlay.Visible)
            {
                return;
            }
        }

        await RegisterCurrentSaleAsync();
        FocusBarcodeBox();
    }
}
