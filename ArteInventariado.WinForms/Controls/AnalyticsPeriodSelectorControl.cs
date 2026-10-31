using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms.Controls;

internal sealed class AnalyticsPeriodSelectorControl : DashboardSurfacePanel
{
    private readonly ComboBox _period = new();
    private readonly ComboBox _comparison = new();
    private readonly DateTimePicker _from = new();
    private readonly DateTimePicker _to = new();
    private readonly Button _refresh = new();
    private readonly Label _fromLabel = new();
    private readonly Label _toLabel = new();

    public event EventHandler? RefreshRequested;

    public AnalyticsPeriodSelectorControl()
    {
        Name = "AnalyticsPeriodSelector";
        Height = 78;
        Dock = DockStyle.Top;
        BackColor = UITheme.SurfaceColor;
        Margin = new Padding(0, 4, 0, 6);
        Padding = new Padding(16, 8, 16, 8);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddLabeledControl(layout, "Período", _period, 0);
        AddLabeledControl(layout, "Comparar con", _comparison, 1);
        _fromLabel.Text = "Desde";
        _toLabel.Text = "Hasta";
        StyleLabel(_fromLabel);
        StyleLabel(_toLabel);
        layout.Controls.Add(_fromLabel, 2, 0);
        layout.Controls.Add(_toLabel, 3, 0);
        ConfigureDate(_from);
        ConfigureDate(_to);
        layout.Controls.Add(_from, 2, 1);
        layout.Controls.Add(_to, 3, 1);

        _refresh.Text = "Actualizar";
        _refresh.Name = "RefreshAnalyticsButton";
        _refresh.Dock = DockStyle.Fill;
        _refresh.Margin = new Padding(8, 2, 0, 0);
        UITheme.StylePrimaryButton(_refresh);
        _refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        layout.Controls.Add(_refresh, 4, 1);

        _period.DropDownStyle = ComboBoxStyle.DropDownList;
        _period.Name = "AnalyticsPeriodComboBox";
        _period.Dock = DockStyle.Fill;
        _period.DataSource = PeriodItems;
        _period.DisplayMember = nameof(Choice<AnalyticsPeriodKind>.Text);
        _period.ValueMember = nameof(Choice<AnalyticsPeriodKind>.Value);
        _period.SelectedValue = AnalyticsPeriodKind.CurrentMonth;
        _period.SelectedValueChanged += (_, _) => UpdateDateVisibility();

        _comparison.DropDownStyle = ComboBoxStyle.DropDownList;
        _comparison.Name = "AnalyticsComparisonComboBox";
        _comparison.Dock = DockStyle.Fill;
        _comparison.DataSource = new[]
        {
            new Choice<AnalyticsComparisonKind>(AnalyticsComparisonKind.PreviousEquivalent, "Período anterior equivalente"),
            new Choice<AnalyticsComparisonKind>(AnalyticsComparisonKind.SamePeriodPreviousYear, "Mismo período del año anterior")
        };
        _comparison.DisplayMember = nameof(Choice<AnalyticsComparisonKind>.Text);
        _comparison.ValueMember = nameof(Choice<AnalyticsComparisonKind>.Value);

        _from.Value = new DateTime(DateTime.Today.Year, 1, 1);
        _to.Value = DateTime.Today;
        Controls.Add(layout);
        UpdateDateVisibility();
    }

    public AnalyticsQueryRequest BuildRequest()
    {
        var kind = _period.SelectedValue is AnalyticsPeriodKind value
            ? value
            : AnalyticsPeriodKind.CurrentMonth;
        var comparison = _comparison.SelectedValue is AnalyticsComparisonKind comparisonValue
            ? comparisonValue
            : AnalyticsComparisonKind.PreviousEquivalent;
        var period = kind switch
        {
            AnalyticsPeriodKind.CustomRange => new AnalyticsPeriodRequest(
                kind,
                DateOnly.FromDateTime(_from.Value.Date),
                DateOnly.FromDateTime(_to.Value.Date)),
            AnalyticsPeriodKind.MultipleYears => new AnalyticsPeriodRequest(
                kind,
                StartYear: _from.Value.Year,
                EndYear: _to.Value.Year),
            _ => new AnalyticsPeriodRequest(kind)
        };
        return new AnalyticsQueryRequest(period, comparison);
    }

    public AnalyticsPeriodKind SelectedKind => _period.SelectedValue is AnalyticsPeriodKind value
        ? value
        : AnalyticsPeriodKind.CurrentMonth;

    public void SetKind(AnalyticsPeriodKind kind)
    {
        if (!PeriodItems.Any(item => item.Value == kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        _period.SelectedValue = kind;
        UpdateDateVisibility();
    }

    public void SetBusy(bool busy)
    {
        _refresh.Enabled = !busy;
        _period.Enabled = !busy;
        _comparison.Enabled = !busy;
        _from.Enabled = !busy;
        _to.Enabled = !busy;
    }

    private void UpdateDateVisibility()
    {
        var custom = _period.SelectedValue is AnalyticsPeriodKind.CustomRange or AnalyticsPeriodKind.MultipleYears;
        _from.Visible = custom;
        _to.Visible = custom;
        _fromLabel.Visible = custom;
        _toLabel.Visible = custom;
        _fromLabel.Text = _period.SelectedValue is AnalyticsPeriodKind.MultipleYears ? "Año inicial" : "Desde";
        _toLabel.Text = _period.SelectedValue is AnalyticsPeriodKind.MultipleYears ? "Año final" : "Hasta";
        _from.CustomFormat = _period.SelectedValue is AnalyticsPeriodKind.MultipleYears ? "yyyy" : "dd/MM/yyyy";
        _to.CustomFormat = _period.SelectedValue is AnalyticsPeriodKind.MultipleYears ? "yyyy" : "dd/MM/yyyy";
    }

    private static void AddLabeledControl(TableLayoutPanel layout, string text, ComboBox control, int column)
    {
        var label = new Label { Text = text };
        StyleLabel(label);
        control.Margin = new Padding(0, 0, 8, 0);
        layout.Controls.Add(label, column, 0);
        layout.Controls.Add(control, column, 1);
    }

    private static void StyleLabel(Label label)
    {
        label.Dock = DockStyle.Fill;
        label.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        label.ForeColor = UITheme.TextDark;
        label.TextAlign = ContentAlignment.BottomLeft;
    }

    private static void ConfigureDate(DateTimePicker picker)
    {
        picker.Dock = DockStyle.Fill;
        picker.Format = DateTimePickerFormat.Custom;
        picker.CustomFormat = "dd/MM/yyyy";
        picker.Margin = new Padding(0, 0, 8, 0);
    }

    private sealed record Choice<T>(T Value, string Text);

    private static readonly Choice<AnalyticsPeriodKind>[] PeriodItems =
    [
        new(AnalyticsPeriodKind.Today, "Hoy"),
        new(AnalyticsPeriodKind.Yesterday, "Ayer"),
        new(AnalyticsPeriodKind.CurrentWeek, "Semana actual"),
        new(AnalyticsPeriodKind.CurrentMonth, "Mes actual"),
        new(AnalyticsPeriodKind.LastThreeMonths, "Últimos 3 meses"),
        new(AnalyticsPeriodKind.CurrentQuarter, "Trimestre actual"),
        new(AnalyticsPeriodKind.PreviousQuarter, "Trimestre anterior"),
        new(AnalyticsPeriodKind.LastSixMonths, "Últimos 6 meses"),
        new(AnalyticsPeriodKind.CurrentSemester, "Semestre actual"),
        new(AnalyticsPeriodKind.PreviousSemester, "Semestre anterior"),
        new(AnalyticsPeriodKind.CurrentYear, "Año actual"),
        new(AnalyticsPeriodKind.PreviousYear, "Año anterior"),
        new(AnalyticsPeriodKind.MultipleYears, "Varios años"),
        new(AnalyticsPeriodKind.CustomRange, "Rango personalizado"),
        new(AnalyticsPeriodKind.AllHistory, "Todo el historial")
    ];
}
