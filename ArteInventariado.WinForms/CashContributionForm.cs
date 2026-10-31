namespace ArteInventariado.WinForms;

internal sealed class CashContributionForm : Form
{
    private readonly NumericUpDown _amount = new();
    private readonly TextBox _reason = new();

    public CashContributionForm()
    {
        Text = "Aportar efectivo a caja";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(570, 390);
        MinimumSize = new Size(570, 390);
        UITheme.StyleForm(this);
        BuildUi();
    }

    public decimal Amount => _amount.Value;

    public string Reason => _reason.Text.Trim();

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = UITheme.DashboardBackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var title = new Label
        {
            Text = "Registrar dinero agregado físicamente",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(title);
        root.Controls.Add(title, 0, 0);

        root.Controls.Add(new Label
        {
            Text = "Este movimiento aumenta el efectivo esperado de esta caja. No se registra como venta, ingreso comercial, gasto ni movimiento de inventario.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.NormalFont
        }, 0, 1);

        var amountPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        amountPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        amountPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        amountPanel.Controls.Add(new Label
        {
            Text = "Monto del aporte ($)",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _amount.Name = "CashContributionAmount";
        _amount.Dock = DockStyle.Fill;
        _amount.Minimum = 0.01m;
        _amount.Maximum = 9_999_999.99m;
        _amount.DecimalPlaces = 2;
        _amount.ThousandsSeparator = true;
        _amount.Increment = 0.25m;
        amountPanel.Controls.Add(_amount, 0, 1);
        root.Controls.Add(amountPanel, 0, 2);

        var reasonPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        reasonPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        reasonPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        reasonPanel.Controls.Add(new Label
        {
            Text = "Motivo (obligatorio)",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _reason.Name = "CashContributionReason";
        _reason.Dock = DockStyle.Fill;
        _reason.MaxLength = 300;
        _reason.Multiline = true;
        _reason.ScrollBars = ScrollBars.Vertical;
        UITheme.StyleTextBox(_reason);
        reasonPanel.Controls.Add(_reason, 0, 1);
        root.Controls.Add(reasonPanel, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 2, 0, 0)
        };
        var save = new Button { Text = "Registrar aporte" };
        UITheme.StylePrimaryButton(save);
        UITheme.EnsureButtonContentFits(save, 150, 42, 42);
        save.Size = save.MinimumSize;
        save.Click += Save_Click;
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel };
        UITheme.StyleSecondaryButton(cancel);
        UITheme.EnsureButtonContentFits(cancel, 120, 42);
        cancel.Size = cancel.MinimumSize;
        actions.Controls.AddRange([save, cancel]);
        root.Controls.Add(actions, 0, 4);

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(root);
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        if (Amount <= 0)
        {
            MessageBox.Show(this, "El monto debe ser mayor que cero.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _amount.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            MessageBox.Show(this, "Escriba el motivo del aporte.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _reason.Focus();
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"¿Registrar un aporte de ${Amount.ToString("N2", System.Globalization.CultureInfo.CurrentCulture)}?\r\n\r\nMotivo: {Reason}\r\n\r\nQuedará asociado a su usuario y a la caja abierta.",
            "Confirmar aporte de efectivo",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
