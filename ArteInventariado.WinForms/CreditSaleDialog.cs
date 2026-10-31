using System.ComponentModel;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class CreditSaleDialog : Form
{
    private const string MethodCash = "EFECTIVO";
    private const string MethodTransfer = "TRANSFERENCIA";
    private const string MethodBitcoin = "BITCOIN";
    private const string MethodMixed = "MIXTO";

    private static readonly Color CreditWarningBackColor = Color.FromArgb(255, 237, 241);
    private static readonly Color CreditWarningForeColor = Color.FromArgb(112, 31, 50);
    private static readonly Color CreditPendingBackColor = Color.FromArgb(255, 249, 224);
    private static readonly Color CreditPendingForeColor = Color.FromArgb(126, 91, 12);
    private static readonly Color CreditClearBackColor = Color.FromArgb(231, 247, 237);
    private static readonly Color CreditClearForeColor = Color.FromArgb(43, 111, 74);

    private readonly ICreditService _creditService;
    private readonly int _clienteId;
    private readonly string _clienteNombre;
    private readonly IReadOnlyList<CreditSaleDialogLine> _lines;
    private readonly long _saleTotalCentavos;
    private readonly bool _canUseTransfer;
    private readonly bool _canUseBitcoin;
    private readonly BindingList<CreditLineRow> _lineRows;
    private readonly BindingSource _linesBinding = new();
    private readonly ToolTip _toolTip = new();

    private readonly DataGridView _gridLines = new();
    private readonly ComboBox _cmbPaymentMethod = new();
    private readonly NumericUpDown _numAdditionalCreditPayment = new();
    private readonly NumericUpDown _numCashApplied = new();
    private readonly NumericUpDown _numCashReceived = new();
    private readonly NumericUpDown _numTransfer = new();
    private readonly TextBox _txtTransferRef = new();
    private readonly NumericUpDown _numBitcoin = new();
    private readonly TextBox _txtBitcoinRef = new();
    private readonly NumericUpDown _numTermDays = new();
    private readonly TextBox _txtObservations = new();

    private readonly Label _lblCustomerState = new();
    private readonly Label _lblPolicyText = new();
    private readonly Label _lblFinancialText = new();
    private readonly Label _lblLimit = new();
    private readonly Label _lblAvailable = new();
    private readonly Label _lblActiveAccounts = new();
    private readonly Label _lblNextDue = new();
    private readonly Label _lblTotalSale = new();
    private readonly Label _lblSelectedCredit = new();
    private readonly Label _lblCashPortion = new();
    private readonly Label _lblAppliedToday = new();
    private readonly Label _lblCashChange = new();
    private readonly Label _lblCapital = new();
    private readonly Label _lblInterest = new();
    private readonly Label _lblAccountTotal = new();
    private readonly Label _lblProjected = new();
    private readonly Label _lblDueDate = new();
    private readonly Label _lblStatus = new();

    private readonly Button _btnAll = new();
    private readonly Button _btnNone = new();
    private readonly Button _btnAccept = new();
    private readonly Button _btnContinueNormal = new();
    private readonly Button _btnCancel = new();

    private CustomerCreditSummary? _summary;
    private bool _syncingPayment;
    private bool _loaded;
    private FinancialState _lastFinancial = FinancialState.Empty;
    private string? _lastValidationMessage;

    public CreditSaleDialog(
        ICreditService creditService,
        int clienteId,
        string clienteNombre,
        IReadOnlyList<CreditSaleDialogLine> lines,
        long saleTotalCentavos,
        IReadOnlyList<SalePaymentRequest> initialPayments,
        bool canUseTransfer,
        bool canUseBitcoin)
    {
        _creditService = creditService;
        _clienteId = clienteId;
        _clienteNombre = clienteNombre;
        _lines = lines;
        _saleTotalCentavos = saleTotalCentavos;
        _canUseTransfer = canUseTransfer;
        _canUseBitcoin = canUseBitcoin;
        _lineRows = new BindingList<CreditLineRow>(_lines.Select(CreditLineRow.FromLine).ToList());

        BuildUi();
        LoadInitialPayments(initialPayments);
        Load += CreditSaleDialog_Load;
    }

    public CreditSaleRequest? CreditRequest { get; private set; }

    public bool ContinueAsNormalSale { get; private set; }

    public IReadOnlyList<SalePaymentRequest> InitialPayments { get; private set; } = [];

    private async void CreditSaleDialog_Load(object? sender, EventArgs e)
    {
        try
        {
            _summary = await _creditService.GetCustomerSummaryAsync(_clienteId);
            _numTermDays.Maximum = Math.Max(1, _summary.Politica.MaxTermDays);
            _numTermDays.Value = Math.Min(7, _numTermDays.Maximum);
            _loaded = true;
            AutoSetDefaultCashPayment();
            RefreshSummary();
        }
        catch (Exception ex)
        {
            _lblStatus.Text = ex.Message;
            MessageBox.Show(ex.Message, "Venta a credito", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Venta a credito";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1240, 820);
        MinimumSize = new Size(800, 460);
        KeyPreview = true;
        KeyDown += CreditSaleDialog_KeyDown;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18),
            AutoSize = false,
            AutoScroll = true,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // Five payment rows need room for their labels and inputs, including
        // Transferencia and Ref. transferencia at 100% DPI.
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 360));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        var title = new Label
        {
            Text = $"Venta a credito - {_clienteNombre}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 6, 0)
        };
        UITheme.StyleLabelTitle(title);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildPaymentPanel(), 0, 2);

        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.ForeColor = UITheme.MutedTextColor;
        _lblStatus.Font = UITheme.NormalBoldFont;
        root.Controls.Add(_lblStatus, 0, 3);
        root.Controls.Add(BuildActions(), 0, 4);
        Controls.Add(root);

        AcceptButton = _btnAccept;
        CancelButton = _btnCancel;
    }

    private Control BuildBody()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.BackgroundColor,
            AutoScroll = true,
            AutoSize = false,
            MinimumSize = Size.Empty
        };

        var body = new TableLayoutPanel
        {
            Location = Point.Empty,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = false,
            MinimumSize = Size.Empty,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Height = 520,
            BackColor = UITheme.BackgroundColor
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        body.Controls.Add(BuildLinesPanel(), 0, 0);
        body.Controls.Add(BuildSummaryPanel(), 1, 0);

        void ResizeBody()
        {
            body.Width = Math.Max(0, host.ClientSize.Width);
        }

        host.Resize += (_, _) => ResizeBody();
        host.Controls.Add(body);
        ResizeBody();
        host.HorizontalScroll.Enabled = false;
        host.HorizontalScroll.Visible = false;
        return host;
    }

    private Control BuildLinesPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12),
            Margin = new Padding(0),
            AutoScroll = true,
            MinimumSize = Size.Empty
        };

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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        ConfigureSmallButton(_btnAll, "Toda la compra", (_, _) => SetAllQuantities());
        ConfigureSmallButton(_btnNone, "Todo al contado", (_, _) => ClearQuantities());
        _toolTip.SetToolTip(_btnAll, "Financia todas las cantidades vendidas.");
        _toolTip.SetToolTip(_btnNone, "Deja todas las cantidades para pago de hoy.");
        actions.Controls.AddRange([_btnAll, _btnNone]);

        ConfigureLinesGrid();
        layout.Controls.Add(actions, 0, 0);
        layout.Controls.Add(_gridLines, 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildSummaryPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14),
            Margin = new Padding(0),
            AutoScroll = true,
            MinimumSize = Size.Empty
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UITheme.SurfaceColor,
            AutoSize = false,
            MinimumSize = Size.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        ConfigureCustomerCreditStatus(_lblCustomerState);
        ConfigureSummaryText(_lblPolicyText);
        ConfigureSummaryText(_lblFinancialText);
        _lblFinancialText.Margin = new Padding(0, 8, 0, 0);

        layout.Controls.Add(_lblCustomerState, 0, 0);
        layout.Controls.Add(_lblPolicyText, 0, 1);
        layout.Controls.Add(_lblFinancialText, 0, 2);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildPaymentPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14),
            Margin = new Padding(0, 12, 0, 0),
            AutoSize = false,
            AutoScroll = true,
            MinimumSize = Size.Empty
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var row = 0; row < 5; row++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        }

        ConfigureCombo();
        ConfigureMoney(_numAdditionalCreditPayment);
        ConfigureMoney(_numCashApplied);
        ConfigureMoney(_numCashReceived);
        ConfigureMoney(_numTransfer);
        ConfigureMoney(_numBitcoin);
        ConfigureInteger(_numTermDays);

        _txtTransferRef.Dock = DockStyle.Fill;
        _txtBitcoinRef.Dock = DockStyle.Fill;
        _txtObservations.Dock = DockStyle.Fill;
        _txtObservations.Multiline = true;
        _txtObservations.ScrollBars = ScrollBars.Vertical;
        UITheme.StyleTextBox(_txtTransferRef);
        UITheme.StyleTextBox(_txtBitcoinRef);
        UITheme.StyleTextBox(_txtObservations);

        _numAdditionalCreditPayment.ValueChanged += (_, _) =>
        {
            SyncPaymentForCurrentMethod();
            RefreshSummary();
        };
        _numCashApplied.ValueChanged += (_, _) => RefreshSummary();
        _numCashReceived.ValueChanged += (_, _) => RefreshSummary();
        _numTransfer.ValueChanged += (_, _) => RefreshSummary();
        _numBitcoin.ValueChanged += (_, _) => RefreshSummary();
        _numTermDays.ValueChanged += (_, _) => RefreshSummary();
        _txtTransferRef.TextChanged += (_, _) => RefreshSummary();
        _txtBitcoinRef.TextChanged += (_, _) => RefreshSummary();

        AddField(panel, 0, 0, "Metodo de pago de hoy", _cmbPaymentMethod);
        AddField(panel, 1, 0, "Abono adicional al credito", _numAdditionalCreditPayment);
        AddField(panel, 0, 1, "Efectivo aplicado", _numCashApplied);
        AddField(panel, 1, 1, "Efectivo recibido", _numCashReceived);
        AddField(panel, 0, 2, "Transferencia", _numTransfer);
        AddField(panel, 1, 2, "Ref. transferencia", _txtTransferRef);
        AddField(panel, 0, 3, "Bitcoin", _numBitcoin);
        AddField(panel, 1, 3, "Ref. Bitcoin", _txtBitcoinRef);
        AddField(panel, 0, 4, "Plazo dias", _numTermDays);
        AddField(panel, 1, 4, "Observaciones", _txtObservations);

        _toolTip.SetToolTip(_numAdditionalCreditPayment, "Reduce el capital que quedara financiado. No genera vuelto.");
        _toolTip.SetToolTip(_numCashReceived, "Dinero entregado por el cliente. El exceso genera vuelto y no se abona al credito.");
        _toolTip.SetToolTip(_numCashApplied, "Parte del pago de hoy que se registrara como efectivo.");
        return panel;
    }

    private Control BuildActions()
    {
        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(0, 10, 0, 0),
            AutoSize = false,
            MinimumSize = Size.Empty
        };

        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        ConfigureButton(_btnCancel, "Cancelar", 130, (_, _) => DialogResult = DialogResult.Cancel);
        UITheme.StyleSecondaryButton(_btnCancel);
        ConfigureButton(_btnContinueNormal, "Continuar venta normal", 230, (_, _) => ContinueNormal());
        UITheme.StyleSecondaryButton(_btnContinueNormal);
        ConfigureButton(_btnAccept, "Confirmar credito", 200, (_, _) => ConfirmCredit());
        UITheme.StyleSuccessButton(_btnAccept);
        _btnCancel.Dock = DockStyle.Fill;
        _btnCancel.Margin = new Padding(0, 0, 10, 0);
        _btnContinueNormal.Dock = DockStyle.Fill;
        _btnContinueNormal.Margin = new Padding(0, 0, 10, 0);
        _btnAccept.Dock = DockStyle.Fill;
        actions.Controls.Add(_btnCancel, 0, 0);
        actions.Controls.Add(_btnContinueNormal, 1, 0);
        actions.Controls.Add(_btnAccept, 2, 0);
        return actions;
    }

    private void ConfigureLinesGrid()
    {
        UITheme.StyleDataGridView(_gridLines);
        _gridLines.Dock = DockStyle.Fill;
        _gridLines.AutoGenerateColumns = false;
        _gridLines.AllowUserToAddRows = false;
        _gridLines.AllowUserToDeleteRows = false;
        _gridLines.RowHeadersVisible = false;
        _gridLines.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridLines.MultiSelect = false;
        _gridLines.DataSource = _linesBinding;
        _gridLines.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _linesBinding.DataSource = _lineRows;
        _gridLines.CellContentClick += GridLines_CellContentClick;
        _gridLines.CellValueChanged += (_, _) => OnFinancedQuantityChanged();
        _gridLines.CellParsing += GridLines_CellParsing;
        _gridLines.CellValidating += GridLines_CellValidating;
        _gridLines.CellEndEdit += (_, _) => NormalizeRowsAndRefresh();
        _gridLines.DataError += GridLines_DataError;
        _gridLines.KeyDown += GridLines_KeyDown;
        _gridLines.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_gridLines.IsCurrentCellDirty)
            {
                _gridLines.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        AddTextColumn("Producto", "Producto", 170, readOnly: true);
        AddTextColumn("CantidadVendida", "Vendida", 62, readOnly: true);
        AddTextColumn("CantidadFinanciada", "Financiar", 72, readOnly: false);
        AddButtonColumn("Decrease", "-", 42);
        AddButtonColumn("Increase", "+", 42);
        AddTextColumn("PagarHoy", "Pagar hoy", 92, readOnly: true, DataGridViewContentAlignment.MiddleRight);
    }

    private void GridLines_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        var row = RowAt(e.RowIndex);
        if (_gridLines.Columns[e.ColumnIndex].Name == "Increase")
        {
            row.CantidadFinanciada = Math.Min(row.CantidadVendida, row.CantidadFinanciada + 1);
            OnFinancedQuantityChanged();
        }
        else if (_gridLines.Columns[e.ColumnIndex].Name == "Decrease")
        {
            row.CantidadFinanciada = Math.Max(0, row.CantidadFinanciada - 1);
            OnFinancedQuantityChanged();
        }
    }

    private void GridLines_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_gridLines.CurrentRow?.DataBoundItem is not CreditLineRow row)
        {
            return;
        }

        if (e.KeyCode == Keys.Space)
        {
            row.CantidadFinanciada = row.CantidadFinanciada == 0 ? row.CantidadVendida : 0;
            e.SuppressKeyPress = true;
            OnFinancedQuantityChanged();
        }
        else if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus)
        {
            row.CantidadFinanciada = Math.Min(row.CantidadVendida, row.CantidadFinanciada + 1);
            e.SuppressKeyPress = true;
            OnFinancedQuantityChanged();
        }
        else if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)
        {
            row.CantidadFinanciada = Math.Max(0, row.CantidadFinanciada - 1);
            e.SuppressKeyPress = true;
            OnFinancedQuantityChanged();
        }
    }

    private void GridLines_CellParsing(object? sender, DataGridViewCellParsingEventArgs e)
    {
        if (!IsFinancedQuantityColumn(e.ColumnIndex))
        {
            return;
        }

        var text = e.Value?.ToString();
        if (string.IsNullOrWhiteSpace(text) || e.Value == DBNull.Value)
        {
            e.Value = 0;
            e.ParsingApplied = true;
            return;
        }

        if (int.TryParse(text, out var parsed))
        {
            e.Value = parsed;
            e.ParsingApplied = true;
        }
    }

    private void GridLines_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (!IsFinancedQuantityColumn(e.ColumnIndex))
        {
            return;
        }

        var text = e.FormattedValue?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!int.TryParse(text, out var parsed))
        {
            e.Cancel = true;
            _lblStatus.Text = "Ingrese una cantidad financiada valida.";
            return;
        }

        var row = RowAt(e.RowIndex);
        if (parsed < 0 || parsed > row.CantidadVendida)
        {
            e.Cancel = true;
            _lblStatus.Text = $"La cantidad financiada debe estar entre 0 y {row.CantidadVendida}.";
        }
    }

    private void GridLines_DataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
        _lblStatus.Text = "La cantidad financiada no es valida. Use cero o una cantidad vendida.";
        NormalizeRowsAndRefresh();
    }

    private void OnFinancedQuantityChanged()
    {
        if (!_loaded || _syncingPayment)
        {
            return;
        }

        NormalizeRows();
        AutoSetDefaultCashPayment();
        RefreshSummary();
        _gridLines.Refresh();
    }

    private void NormalizeRowsAndRefresh()
    {
        NormalizeRows();
        RefreshSummary();
        _gridLines.Refresh();
    }

    private void NormalizeRows()
    {
        foreach (var row in _lineRows)
        {
            row.CantidadFinanciada = Math.Clamp(row.CantidadFinanciada, 0, row.CantidadVendida);
        }
    }

    private void SetAllQuantities()
    {
        foreach (var row in _lineRows)
        {
            row.CantidadFinanciada = row.CantidadVendida;
        }

        AutoSetDefaultCashPayment();
        RefreshSummary();
        _gridLines.Refresh();
    }

    private void ClearQuantities()
    {
        foreach (var row in _lineRows)
        {
            row.CantidadFinanciada = 0;
        }

        AutoSetDefaultCashPayment();
        RefreshSummary();
        _gridLines.Refresh();
    }

    private void ConfigureCombo()
    {
        _cmbPaymentMethod.Dock = DockStyle.Fill;
        _cmbPaymentMethod.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbPaymentMethod.Items.Add(MethodCash);
        if (_canUseTransfer)
        {
            _cmbPaymentMethod.Items.Add(MethodTransfer);
        }

        if (_canUseBitcoin)
        {
            _cmbPaymentMethod.Items.Add(MethodBitcoin);
        }

        if (_canUseTransfer || _canUseBitcoin)
        {
            _cmbPaymentMethod.Items.Add(MethodMixed);
        }

        _cmbPaymentMethod.SelectedItem = MethodCash;
        _cmbPaymentMethod.SelectedIndexChanged += (_, _) =>
        {
            SyncPaymentForCurrentMethod();
            RefreshSummary();
        };
    }

    private void AutoSetDefaultCashPayment()
    {
        if (!_loaded)
        {
            return;
        }

        _syncingPayment = true;
        try
        {
            _numAdditionalCreditPayment.Value = 0;
            _cmbPaymentMethod.SelectedItem = MethodCash;
            var state = CalculateFinancial();
            SetMoney(_numCashApplied, state.PaymentAppliedTodayCentavos);
            SetMoney(_numCashReceived, state.PaymentAppliedTodayCentavos);
            SetMoney(_numTransfer, 0);
            SetMoney(_numBitcoin, 0);
        }
        finally
        {
            _syncingPayment = false;
        }
    }

    private void SyncPaymentForCurrentMethod()
    {
        if (!_loaded || _syncingPayment)
        {
            return;
        }

        _syncingPayment = true;
        try
        {
            var state = CalculateFinancial();
            var due = state.PaymentAppliedTodayCentavos;
            var method = CurrentMethod();

            if (method == MethodCash)
            {
                SetMoney(_numCashApplied, due);
                if (DollarsToCentavos(_numCashReceived.Value) < due)
                {
                    SetMoney(_numCashReceived, due);
                }

                SetMoney(_numTransfer, 0);
                SetMoney(_numBitcoin, 0);
            }
            else if (method == MethodTransfer)
            {
                SetMoney(_numCashApplied, 0);
                SetMoney(_numCashReceived, 0);
                SetMoney(_numTransfer, due);
                SetMoney(_numBitcoin, 0);
            }
            else if (method == MethodBitcoin)
            {
                SetMoney(_numCashApplied, 0);
                SetMoney(_numCashReceived, 0);
                SetMoney(_numTransfer, 0);
                SetMoney(_numBitcoin, due);
            }
            else
            {
                var cash = DollarsToCentavos(_numCashApplied.Value);
                var transfer = DollarsToCentavos(_numTransfer.Value);
                var bitcoin = DollarsToCentavos(_numBitcoin.Value);
                if (cash + transfer + bitcoin == 0 && due > 0)
                {
                    SetMoney(_numCashApplied, due);
                    SetMoney(_numCashReceived, due);
                }

                if (DollarsToCentavos(_numCashReceived.Value) < DollarsToCentavos(_numCashApplied.Value))
                {
                    SetMoney(_numCashReceived, DollarsToCentavos(_numCashApplied.Value));
                }
            }
        }
        finally
        {
            _syncingPayment = false;
        }

        UpdatePaymentControls();
    }

    private void UpdatePaymentControls()
    {
        var method = CurrentMethod();
        var usesCash = method is MethodCash or MethodMixed;
        var usesTransfer = method is MethodTransfer or MethodMixed;
        var usesBitcoin = method is MethodBitcoin or MethodMixed;
        var mixed = method == MethodMixed;

        _numCashApplied.Enabled = mixed;
        _numCashReceived.Enabled = usesCash;
        _numTransfer.Enabled = usesTransfer && (method == MethodMixed || _canUseTransfer);
        _txtTransferRef.Enabled = usesTransfer && _canUseTransfer;
        _numBitcoin.Enabled = usesBitcoin && (method == MethodMixed || _canUseBitcoin);
        _txtBitcoinRef.Enabled = usesBitcoin && _canUseBitcoin;
    }

    private void LoadInitialPayments(IReadOnlyList<SalePaymentRequest> payments)
    {
        if (payments.Count == 0)
        {
            return;
        }

        var cash = payments.Where(p => p.MetodoPago == MetodoPago.EFECTIVO).Sum(p => p.MontoCentavos);
        var transfer = payments.Where(p => p.MetodoPago == MetodoPago.TRANSFERENCIA).Sum(p => p.MontoCentavos);
        var bitcoin = payments.Where(p => p.MetodoPago == MetodoPago.BITCOIN).Sum(p => p.MontoCentavos);

        SetMoney(_numCashApplied, cash);
        SetMoney(_numCashReceived, cash);
        SetMoney(_numTransfer, transfer);
        SetMoney(_numBitcoin, bitcoin);
        _txtTransferRef.Text = payments.FirstOrDefault(p => p.MetodoPago == MetodoPago.TRANSFERENCIA)?.Referencia ?? string.Empty;
        _txtBitcoinRef.Text = payments.FirstOrDefault(p => p.MetodoPago == MetodoPago.BITCOIN)?.Referencia ?? string.Empty;

        var method = payments.Select(p => p.MetodoPago).Distinct().Count() > 1
            ? MethodMixed
            : payments.First().MetodoPago switch
            {
                MetodoPago.TRANSFERENCIA when _canUseTransfer => MethodTransfer,
                MetodoPago.BITCOIN when _canUseBitcoin => MethodBitcoin,
                _ => MethodCash
            };
        if (_cmbPaymentMethod.Items.Contains(method))
        {
            _cmbPaymentMethod.SelectedItem = method;
        }
    }

    private void ConfirmCredit()
    {
        _gridLines.EndEdit();
        NormalizeRows();
        var validation = ValidateCurrentState(allowNormalSale: false);
        RefreshSummary();
        if (!validation.IsValid)
        {
            MessageBox.Show(validation.Message, "Venta a credito", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var selected = GetSelectedLines().ToList();
        var state = CalculateFinancial();
        CreditRequest = new CreditSaleRequest(
            _clienteId,
            (int)_numTermDays.Value,
            selected.Select(line => new CreditSaleLineRequest(
                line.ProductoId,
                line.CantidadFinanciada,
                line.ProductoSkuId)).ToList(),
            string.IsNullOrWhiteSpace(_txtObservations.Text) ? null : _txtObservations.Text.Trim());
        InitialPayments = BuildPayments(useReceivedCash: false, validateReferences: true, state);
        ContinueAsNormalSale = false;
        DialogResult = DialogResult.OK;
    }

    private void ContinueNormal()
    {
        _gridLines.EndEdit();
        NormalizeRows();
        var validation = ValidateCurrentState(allowNormalSale: true);
        RefreshSummary();
        if (!validation.IsValid)
        {
            MessageBox.Show(validation.Message, "Venta normal", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        CreditRequest = null;
        InitialPayments = BuildPayments(useReceivedCash: true, validateReferences: true, CalculateFinancial());
        ContinueAsNormalSale = true;
        DialogResult = DialogResult.OK;
    }

    private void RefreshSummary()
    {
        if (_summary is null)
        {
            _lblCustomerState.Text = "Cargando...";
            _btnAccept.Enabled = false;
            _btnContinueNormal.Visible = false;
            return;
        }

        if (!_syncingPayment)
        {
            UpdatePaymentControls();
        }

        var state = CalculateFinancial();
        _lastFinancial = state;
        var validation = ValidateCurrentState(allowNormalSale: false);
        var normalValidation = ValidateCurrentState(allowNormalSale: true);
        _lastValidationMessage = validation.Message;

        UpdateCustomerCreditStatus(_summary.Situacion);
        _lblLimit.Text = SaleReceiptViewModel.FormatDollars(_summary.LimiteCentavos);
        _lblAvailable.Text = SaleReceiptViewModel.FormatDollars(_summary.CreditoDisponibleCentavos);
        _lblActiveAccounts.Text = $"{_summary.CuentasActivas}/{_summary.MaximoCuentasActivas}";
        _lblNextDue.Text = _summary.ProximoVencimiento.HasValue
            ? SaleReceiptViewModel.ToLocal(_summary.ProximoVencimiento.Value).ToString("yyyy-MM-dd")
            : "-";

        _lblTotalSale.Text = SaleReceiptViewModel.FormatDollars(_saleTotalCentavos);
        _lblSelectedCredit.Text = SaleReceiptViewModel.FormatDollars(state.SelectedCreditCentavos);
        _lblCashPortion.Text = SaleReceiptViewModel.FormatDollars(state.CashPortionCentavos);
        _lblAppliedToday.Text = SaleReceiptViewModel.FormatDollars(state.PaymentAppliedTodayCentavos);
        _lblCashChange.Text = SaleReceiptViewModel.FormatDollars(state.ChangeCentavos);
        _lblCapital.Text = SaleReceiptViewModel.FormatDollars(state.CapitalCentavos);
        _lblInterest.Text = SaleReceiptViewModel.FormatDollars(state.InterestCentavos);
        _lblAccountTotal.Text = SaleReceiptViewModel.FormatDollars(state.AccountTotalCentavos);
        _lblProjected.Text = SaleReceiptViewModel.FormatDollars(state.ProjectedDebtCentavos);
        _lblDueDate.Text = DateTime.Today.AddDays((int)_numTermDays.Value).ToString("yyyy-MM-dd");
        _lblPolicyText.Text =
            $"Estado: {_summary.Situacion}\n" +
            $"Limite: {SaleReceiptViewModel.FormatDollars(_summary.LimiteCentavos)}\n" +
            $"Disponible: {SaleReceiptViewModel.FormatDollars(_summary.CreditoDisponibleCentavos)}\n" +
            $"Cuentas activas: {_summary.CuentasActivas}/{_summary.MaximoCuentasActivas}\n" +
            $"Proximo vencimiento: {(_summary.ProximoVencimiento.HasValue ? SaleReceiptViewModel.ToLocal(_summary.ProximoVencimiento.Value).ToString("yyyy-MM-dd") : "-")}";
        _lblFinancialText.Text =
            $"Total de venta: {SaleReceiptViewModel.FormatDollars(_saleTotalCentavos)}\n" +
            $"Seleccionado a credito: {SaleReceiptViewModel.FormatDollars(state.SelectedCreditCentavos)}\n" +
            $"Parte al contado: {SaleReceiptViewModel.FormatDollars(state.CashPortionCentavos)}\n" +
            $"Abono adicional al credito: {SaleReceiptViewModel.FormatDollars(state.AdditionalCreditPaymentCentavos)}\n" +
            $"Pago aplicado hoy: {SaleReceiptViewModel.FormatDollars(state.PaymentAppliedTodayCentavos)}\n" +
            $"Vuelto: {SaleReceiptViewModel.FormatDollars(state.ChangeCentavos)}\n" +
            $"Capital financiado: {SaleReceiptViewModel.FormatDollars(state.CapitalCentavos)}\n" +
            $"Interes: {SaleReceiptViewModel.FormatDollars(state.InterestCentavos)}\n" +
            $"Total de la cuenta: {SaleReceiptViewModel.FormatDollars(state.AccountTotalCentavos)}\n" +
            $"Saldo proyectado: {SaleReceiptViewModel.FormatDollars(state.ProjectedDebtCentavos)}\n" +
            $"Fecha de vencimiento: {DateTime.Today.AddDays((int)_numTermDays.Value):yyyy-MM-dd}";

        _btnAccept.Enabled = validation.IsValid;
        _btnContinueNormal.Visible = state.CapitalCentavos <= 0 || state.SelectedCreditCentavos <= 0;
        _btnContinueNormal.Enabled = _btnContinueNormal.Visible && normalValidation.IsValid;

        _lblStatus.Text = validation.IsValid
            ? $"Debe pagar hoy: {SaleReceiptViewModel.FormatDollars(state.PaymentAppliedTodayCentavos)}. Capital que quedara financiado: {SaleReceiptViewModel.FormatDollars(state.CapitalCentavos)}."
            : validation.Message;
    }

    private ValidationResult ValidateCurrentState(bool allowNormalSale)
    {
        if (_summary is null)
        {
            return ValidationResult.Invalid("Cargando politica crediticia.");
        }

        var state = CalculateFinancial();
        if (_summary.Situacion is SituacionCrediticia.VENCIDO or SituacionCrediticia.MOROSO)
        {
            return ValidationResult.Invalid($"El cliente tiene credito {_summary.Situacion.ToString().ToLowerInvariant()}.");
        }

        if (!_summary.CreditoHabilitado)
        {
            return ValidationResult.Invalid($"El credito del cliente esta bloqueado: {_summary.CreditoBloqueoMotivo ?? "-"}.");
        }

        if (_numTermDays.Value <= 0 || _numTermDays.Value > _summary.Politica.MaxTermDays)
        {
            return ValidationResult.Invalid($"El plazo no puede superar {_summary.Politica.MaxTermDays} dia(s).");
        }

        if (state.SelectedCreditCentavos <= 0)
        {
            return allowNormalSale
                ? ValidatePaymentForNormalSale(state)
                : ValidationResult.Invalid("No existe importe financiado. Puede continuar como venta normal.");
        }

        if (state.CapitalCentavos <= 0)
        {
            return allowNormalSale
                ? ValidatePaymentForNormalSale(state)
                : ValidationResult.Invalid("El abono adicional cubre todo el credito. Puede continuar como venta normal.");
        }

        if (state.AdditionalCreditPaymentCentavos < 0 || state.AdditionalCreditPaymentCentavos >= state.SelectedCreditCentavos)
        {
            return ValidationResult.Invalid("El abono adicional debe ser menor que el importe seleccionado a credito.");
        }

        if (state.ProjectedDebtCentavos > _summary.LimiteCentavos)
        {
            return ValidationResult.Invalid("El saldo proyectado supera el limite autorizado.");
        }

        if (_summary.CuentasActivas >= _summary.MaximoCuentasActivas)
        {
            return ValidationResult.Invalid("El cliente ya alcanzo el maximo de cuentas activas permitido.");
        }

        if (state.PaymentEnteredCentavos != state.PaymentAppliedTodayCentavos)
        {
            return ValidationResult.Invalid($"Debe pagar hoy exactamente {SaleReceiptViewModel.FormatDollars(state.PaymentAppliedTodayCentavos)} aplicado a caja/metodos.");
        }

        if (state.CashAppliedCentavos > 0 && state.CashReceivedCentavos < state.CashAppliedCentavos)
        {
            return ValidationResult.Invalid($"Efectivo recibido insuficiente. Faltante: {SaleReceiptViewModel.FormatDollars(state.CashAppliedCentavos - state.CashReceivedCentavos)}.");
        }

        if (state.TransferCentavos > 0 && string.IsNullOrWhiteSpace(_txtTransferRef.Text))
        {
            return ValidationResult.Invalid("Ingrese la referencia de transferencia.");
        }

        if (state.BitcoinCentavos > 0 && string.IsNullOrWhiteSpace(_txtBitcoinRef.Text))
        {
            return ValidationResult.Invalid("Ingrese la referencia de Bitcoin.");
        }

        return ValidationResult.Valid();
    }

    private ValidationResult ValidatePaymentForNormalSale(FinancialState state)
    {
        if (state.PaymentEnteredCentavos != _saleTotalCentavos)
        {
            return ValidationResult.Invalid($"Para venta normal debe pagar hoy {SaleReceiptViewModel.FormatDollars(_saleTotalCentavos)}.");
        }

        if (state.CashAppliedCentavos > 0 && state.CashReceivedCentavos < state.CashAppliedCentavos)
        {
            return ValidationResult.Invalid("El efectivo recibido no cubre el pago aplicado.");
        }

        if (state.TransferCentavos > 0 && string.IsNullOrWhiteSpace(_txtTransferRef.Text))
        {
            return ValidationResult.Invalid("Ingrese la referencia de transferencia.");
        }

        if (state.BitcoinCentavos > 0 && string.IsNullOrWhiteSpace(_txtBitcoinRef.Text))
        {
            return ValidationResult.Invalid("Ingrese la referencia de Bitcoin.");
        }

        return ValidationResult.Valid();
    }

    private FinancialState CalculateFinancial()
    {
        var selectedCredit = _lineRows.Sum(line => line.FinancedSubtotalCentavos);
        var cashPortion = Math.Max(0, _saleTotalCentavos - selectedCredit);
        var additional = DollarsToCentavos(_numAdditionalCreditPayment.Value);
        if (selectedCredit > 0 && additional > selectedCredit)
        {
            additional = selectedCredit;
        }

        var paymentAppliedToday = cashPortion + additional;
        var capital = Math.Max(0, selectedCredit - additional);
        var interest = _summary is null
            ? 0
            : (long)Math.Round(capital * _summary.Politica.InterestPercent / 100m, 0, MidpointRounding.AwayFromZero);
        var accountTotal = capital + interest;

        var cashApplied = DollarsToCentavos(_numCashApplied.Value);
        var cashReceived = DollarsToCentavos(_numCashReceived.Value);
        var transfer = DollarsToCentavos(_numTransfer.Value);
        var bitcoin = DollarsToCentavos(_numBitcoin.Value);
        var entered = cashApplied + transfer + bitcoin;
        var change = cashApplied > 0 ? Math.Max(0, cashReceived - cashApplied) : 0;
        var projected = (_summary?.SaldoActualCentavos ?? 0) + accountTotal;

        return new FinancialState(
            selectedCredit,
            cashPortion,
            additional,
            paymentAppliedToday,
            capital,
            interest,
            accountTotal,
            projected,
            cashApplied,
            cashReceived,
            transfer,
            bitcoin,
            entered,
            change);
    }

    private IReadOnlyList<SalePaymentRequest> BuildPayments(
        bool useReceivedCash,
        bool validateReferences,
        FinancialState state)
    {
        var payments = new List<SalePaymentRequest>();
        var cashToSend = useReceivedCash && state.CashAppliedCentavos > 0
            ? state.CashReceivedCentavos
            : state.CashAppliedCentavos;

        if (cashToSend > 0)
        {
            payments.Add(new SalePaymentRequest(MetodoPago.EFECTIVO, cashToSend));
        }

        if (state.TransferCentavos > 0)
        {
            if (validateReferences && string.IsNullOrWhiteSpace(_txtTransferRef.Text))
            {
                throw new InvalidOperationException("Ingrese la referencia de transferencia.");
            }

            payments.Add(new SalePaymentRequest(MetodoPago.TRANSFERENCIA, state.TransferCentavos, _txtTransferRef.Text.Trim()));
        }

        if (state.BitcoinCentavos > 0)
        {
            if (validateReferences && string.IsNullOrWhiteSpace(_txtBitcoinRef.Text))
            {
                throw new InvalidOperationException("Ingrese la referencia de Bitcoin.");
            }

            payments.Add(new SalePaymentRequest(MetodoPago.BITCOIN, state.BitcoinCentavos, _txtBitcoinRef.Text.Trim()));
        }

        return payments;
    }

    private IEnumerable<CreditLineRow> GetSelectedLines()
    {
        return _lineRows.Where(line => line.CantidadFinanciada > 0);
    }

    private CreditLineRow RowAt(int rowIndex)
    {
        return _gridLines.Rows[rowIndex].DataBoundItem as CreditLineRow
            ?? throw new InvalidOperationException("No se pudo leer la linea de credito seleccionada.");
    }

    private string CurrentMethod()
    {
        return _cmbPaymentMethod.SelectedItem?.ToString() ?? MethodCash;
    }

    private bool IsFinancedQuantityColumn(int columnIndex)
    {
        return columnIndex >= 0 &&
            _gridLines.Columns[columnIndex].DataPropertyName == nameof(CreditLineRow.CantidadFinanciada);
    }

    private void CreditSaleDialog_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.Enter && _btnAccept.Enabled)
        {
            ConfirmCredit();
            e.SuppressKeyPress = true;
        }
    }

    private void AddTextColumn(
        string property,
        string header,
        int width,
        bool readOnly,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        _gridLines.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = property,
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = width,
            FillWeight = width,
            ReadOnly = readOnly,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = alignment,
                NullValue = property == nameof(CreditLineRow.CantidadFinanciada) ? "0" : null
            }
        });
    }

    private void AddButtonColumn(string name, string text, int width)
    {
        _gridLines.Columns.Add(new DataGridViewButtonColumn
        {
            Name = name,
            HeaderText = text,
            Text = text,
            UseColumnTextForButtonValue = true,
            MinimumWidth = width,
            FillWeight = width,
            ReadOnly = true
        });
    }

    private static void AddField(TableLayoutPanel panel, int column, int row, string labelText, Control control)
    {
        var container = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 8),
            BackColor = UITheme.SurfaceColor,
            AutoSize = false,
            MinimumSize = Size.Empty
        };

        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            AutoEllipsis = false,
            Padding = new Padding(0, 0, 2, 0)
        };

        control.Dock = DockStyle.Fill;
        container.Controls.Add(control);
        container.Controls.Add(label);
        panel.Controls.Add(container, column, row);
    }

    private static void AddMetric(TableLayoutPanel panel, int column, int row, string labelText, Label value)
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 10, 6),
            BackColor = UITheme.SurfaceColor
        };
        container.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = UITheme.MutedTextColor,
            AutoEllipsis = true
        };

        value.Text = "-";
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleLeft;
        value.Font = UITheme.NormalBoldFont;
        value.ForeColor = UITheme.TextDark;
        value.AutoEllipsis = true;

        container.Controls.Add(label, 0, 0);
        container.Controls.Add(value, 0, 1);
        panel.Controls.Add(container, column, row);
    }

    private static void ConfigureSummaryText(Label label)
    {
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.TopLeft;
        label.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        label.ForeColor = UITheme.TextDark;
        label.BackColor = UITheme.SurfaceColor;
        label.Padding = new Padding(0);
        label.AutoEllipsis = false;
    }

    private static void ConfigureCustomerCreditStatus(Label label)
    {
        label.Dock = DockStyle.Fill;
        label.AutoSize = false;
        label.AutoEllipsis = false;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Font = UITheme.NormalBoldFont;
        label.BackColor = UITheme.SurfaceColor;
        label.ForeColor = UITheme.TextDark;
        label.Padding = new Padding(10, 8, 10, 8);
        label.BorderStyle = BorderStyle.FixedSingle;
    }

    private void UpdateCustomerCreditStatus(SituacionCrediticia situation)
    {
        var presentation = situation switch
        {
            SituacionCrediticia.MOROSO or SituacionCrediticia.VENCIDO =>
                ("Este cliente tiene crédito moroso. No se puede realizar una venta al crédito. Comuníquese con el administrador.",
                    CreditWarningBackColor, CreditWarningForeColor),
            SituacionCrediticia.SALDO_VIGENTE =>
                ("Este cliente tiene un crédito pendiente, pero está al día con sus abonos.",
                    CreditPendingBackColor, CreditPendingForeColor),
            _ =>
                ("Este cliente no tiene ningún crédito pendiente.",
                    CreditClearBackColor, CreditClearForeColor)
        };

        _lblCustomerState.Text = presentation.Item1;
        _lblCustomerState.BackColor = presentation.Item2;
        _lblCustomerState.ForeColor = presentation.Item3;
    }

    private static void ConfigureMoney(NumericUpDown control)
    {
        control.DecimalPlaces = 2;
        control.Minimum = 0;
        control.Maximum = 999999;
        control.ThousandsSeparator = true;
        control.Dock = DockStyle.Fill;
        control.Font = UITheme.NormalFont;
    }

    private static void ConfigureInteger(NumericUpDown control)
    {
        control.DecimalPlaces = 0;
        control.Minimum = 1;
        control.Maximum = 365;
        control.Dock = DockStyle.Fill;
        control.Font = UITheme.NormalFont;
    }

    private static void SetMoney(NumericUpDown control, long centavos)
    {
        control.Value = Math.Clamp(centavos / 100m, control.Minimum, control.Maximum);
    }

    private static long DollarsToCentavos(decimal dollars)
    {
        return (long)Math.Round(dollars * 100m, MidpointRounding.AwayFromZero);
    }

    private static void ConfigureSmallButton(Button button, string text, EventHandler handler)
    {
        button.Text = text;
        button.Width = 190;
        button.Height = 34;
        button.Margin = new Padding(0, 0, 8, 8);
        button.AutoSize = false;
        button.AutoEllipsis = false;
        button.Padding = new Padding(6, 0, 6, 0);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Click += handler;
        UITheme.StyleSecondaryButton(button);
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 38;
        button.Margin = new Padding(8, 0, 0, 0);
        button.AutoSize = false;
        button.AutoEllipsis = false;
        button.Padding = new Padding(6, 0, 6, 0);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Click += handler;
    }

    private sealed record FinancialState(
        long SelectedCreditCentavos,
        long CashPortionCentavos,
        long AdditionalCreditPaymentCentavos,
        long PaymentAppliedTodayCentavos,
        long CapitalCentavos,
        long InterestCentavos,
        long AccountTotalCentavos,
        long ProjectedDebtCentavos,
        long CashAppliedCentavos,
        long CashReceivedCentavos,
        long TransferCentavos,
        long BitcoinCentavos,
        long PaymentEnteredCentavos,
        long ChangeCentavos)
    {
        public static FinancialState Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private sealed record ValidationResult(bool IsValid, string Message)
    {
        public static ValidationResult Valid() => new(true, string.Empty);

        public static ValidationResult Invalid(string message) => new(false, message);
    }

    private sealed class CreditLineRow
    {
        private int _cantidadFinanciada;

        public int ProductoId { get; init; }

        public int? ProductoSkuId { get; init; }

        public string Producto { get; init; } = string.Empty;

        public int CantidadVendida { get; init; }

        public int CantidadFinanciada
        {
            get => _cantidadFinanciada;
            set => _cantidadFinanciada = Math.Clamp(value, 0, CantidadVendida);
        }

        public long TotalAfterDiscountCentavos { get; init; }

        public long FinancedSubtotalCentavos =>
            CantidadFinanciada == CantidadVendida
                ? TotalAfterDiscountCentavos
                : TotalAfterDiscountCentavos * CantidadFinanciada / CantidadVendida;

        public string PagarHoy => SaleReceiptViewModel.FormatDollars(Math.Max(0, TotalAfterDiscountCentavos - FinancedSubtotalCentavos));

        public static CreditLineRow FromLine(CreditSaleDialogLine line)
        {
            return new CreditLineRow
            {
                ProductoId = line.ProductoId,
                ProductoSkuId = line.ProductoSkuId,
                Producto = $"{line.Producto} ({line.Codigo})",
                CantidadVendida = line.CantidadVendida,
                CantidadFinanciada = line.CantidadVendida,
                TotalAfterDiscountCentavos = line.TotalAfterDiscountCentavos
            };
        }
    }
}

public sealed record CreditSaleDialogLine(
    int ProductoId,
    string Producto,
    string Codigo,
    int CantidadVendida,
    long PrecioUnitarioCentavos,
    long SubtotalCentavos,
    long DescuentoLineaCentavos,
    long DescuentoGeneralCentavos,
    long TotalAfterDiscountCentavos,
    int? ProductoSkuId = null);
