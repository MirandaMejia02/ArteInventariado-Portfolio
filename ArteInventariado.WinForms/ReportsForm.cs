using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class ReportsForm : Form
{
    private readonly IMonthlyReportExportService _monthlyReports;
    private readonly ICommercialOperationsReportService _commercialReports;
    private readonly ICurrentSession _session;
    private readonly IEmployeePermissionService _permissions;
    private readonly AdvancedReportsControl _advancedReports;
    private readonly ComboBox _month = new();
    private readonly NumericUpDown _year = new();
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = 160 };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = 160 };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _monthlyTab = new("Reporte mensual");
    private readonly TabPage _commercialTab = new("Operaciones y caja");
    private readonly TabPage _advancedTab = new("Reportes avanzados");

    public ReportsForm(
        IMonthlyReportExportService monthlyReports,
        ICommercialOperationsReportService commercialReports,
        ICurrentSession session,
        IEmployeePermissionService permissions,
        IAdvancedReportQueryService advancedQuery,
        IAdvancedReportExportService advancedExport,
        IAdvancedReportFilterOptionsService advancedFilterOptions)
    {
        _monthlyReports = monthlyReports;
        _commercialReports = commercialReports;
        _session = session;
        _permissions = permissions;
        _advancedReports = new AdvancedReportsControl(advancedQuery, advancedExport, advancedFilterOptions, session);
        Text = "Reportes";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1440, 900);
        MinimumSize = new Size(1120, 720);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        UITheme.StyleForm(this);
        BuildUi();
        Load += FormLoad;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            RowCount = 3,
            ColumnCount = 1,
            MinimumSize = Size.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var title = new Label { Text = "Centro de reportes", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        UITheme.StyleLabelTitle(title);
        _monthlyTab.BackColor = UITheme.BackgroundColor;
        _commercialTab.BackColor = UITheme.BackgroundColor;
        _advancedTab.BackColor = UITheme.DashboardBackgroundColor;
        _monthlyTab.Controls.Add(BuildMonthlyTab());
        _commercialTab.Controls.Add(BuildCommercialTab());
        _advancedTab.Controls.Add(_advancedReports);
        _tabs.TabPages.AddRange([_advancedTab, _monthlyTab, _commercialTab]);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var close = new Button { Text = "Cerrar", Size = new Size(132, 40) };
        close.Click += (_, _) => Close();
        UITheme.StyleSecondaryButton(close);
        UITheme.EnsureButtonContentFits(close, 132, 40, 28);
        footer.Controls.Add(close);
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(_tabs, 0, 1);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
    }

    private Control BuildMonthlyTab()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 4, ColumnCount = 1 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = new Label { Text = "Reporte mensual general", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        UITheme.StyleLabelHeader(heading);
        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(0, 4, 0, 4)
        };
        _month.Width = 210;
        _month.DropDownStyle = ComboBoxStyle.DropDownList;
        _month.DataSource = MonthOptions();
        _year.Width = 120;
        _year.Minimum = 2000;
        _year.Maximum = 2100;
        AddField(filters, "Mes", _month);
        AddField(filters, "Ano", _year);
        var export = new Button { Text = "Exportar reporte mensual", Size = new Size(210, 40) };
        UITheme.StylePrimaryButton(export);
        export.Click += async (_, _) => await ExportMonthlyAsync(export);
        var note = new Label
        {
            Text = "Incluye ventas, cartera, inventario, proveedores y movimientos mensuales. Solo ADMIN.",
            Dock = DockStyle.Fill,
            ForeColor = UITheme.MutedTextColor,
            Padding = new Padding(0, 12, 0, 0)
        };
        panel.Controls.Add(heading, 0, 0);
        panel.Controls.Add(filters, 0, 1);
        panel.Controls.Add(export, 0, 2);
        panel.Controls.Add(note, 0, 3);
        return panel;
    }

    private Control BuildCommercialTab()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 4, ColumnCount = 1 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = new Label { Text = "Operaciones comerciales y detalle de caja", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        UITheme.StyleLabelHeader(heading);
        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(0, 4, 0, 4)
        };
        AddField(filters, "Desde", _from);
        AddField(filters, "Hasta", _to);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true
        };
        var commercial = new Button { Text = "Operaciones comerciales", Size = new Size(220, 42) };
        var cash = new Button { Text = "Detalle de caja", Size = new Size(180, 42) };
        UITheme.StylePrimaryButton(commercial);
        UITheme.StyleSecondaryButton(cash);
        commercial.Click += async (_, _) => await ExportCommercialAsync(commercial, cash, cashReport: false);
        cash.Click += async (_, _) => await ExportCommercialAsync(commercial, cash, cashReport: true);
        actions.Controls.AddRange([commercial, cash]);
        var note = new Label
        {
            Text = "Los archivos se guardan bajo Documentos\\ArteInventariado\\Reportes o en la carpeta configurada. La ruta se muestra siempre al finalizar.",
            Dock = DockStyle.Fill,
            ForeColor = UITheme.MutedTextColor,
            Padding = new Padding(0, 12, 0, 0)
        };
        panel.Controls.Add(heading, 0, 0);
        panel.Controls.Add(filters, 0, 1);
        panel.Controls.Add(actions, 0, 2);
        panel.Controls.Add(note, 0, 3);
        return panel;
    }

    private async void FormLoad(object? sender, EventArgs e)
    {
        _month.SelectedIndex = DateTime.Today.Month - 1;
        _year.Value = DateTime.Today.Year;
        _from.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _to.Value = DateTime.Today;
        if (!_session.EsAdmin)
        {
            _tabs.TabPages.Remove(_monthlyTab);
            _tabs.TabPages.Remove(_advancedTab);
        }
        if (!await _permissions.HasPermissionAsync(PermisoEmpleado.REPORTES_COMERCIALES_VER))
            _tabs.TabPages.Remove(_commercialTab);
        if (_tabs.TabPages.Count == 0)
        {
            MessageBox.Show("No tiene permiso para exportar reportes.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
        else if (_session.EsAdmin)
        {
            await _advancedReports.InitializeAsync();
        }
    }

    private async Task ExportMonthlyAsync(Button button)
    {
        try
        {
            button.Enabled = false;
            UseWaitCursor = true;
            var option = (MonthOption)_month.SelectedItem!;
            var result = await _monthlyReports.ExportMonthlyReportAsync(new((int)_year.Value, option.Value));
            using var actions = new GeneratedFileActionsForm("Reporte mensual generado", result.FilePath);
            actions.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo exportar el reporte:\n\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            button.Enabled = true;
            UseWaitCursor = false;
        }
    }

    private async Task ExportCommercialAsync(Button commercialButton, Button cashButton, bool cashReport)
    {
        try
        {
            commercialButton.Enabled = cashButton.Enabled = false;
            UseWaitCursor = true;
            var request = new CommercialReportRequest(_from.Value.Date, _to.Value.Date);
            var result = cashReport
                ? await _commercialReports.ExportCashDetailAsync(request)
                : await _commercialReports.ExportCommercialOperationsAsync(request);
            using var actions = new GeneratedFileActionsForm(
                cashReport ? "Reporte de caja generado" : "Reporte comercial generado",
                result.FilePath);
            actions.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo exportar el reporte:\n\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            commercialButton.Enabled = cashButton.Enabled = true;
            UseWaitCursor = false;
        }
    }

    private static void AddField(FlowLayoutPanel panel, string text, Control control)
    {
        var group = new FlowLayoutPanel
        {
            AutoSize = false,
            Width = Math.Max(control.Width, 150),
            Height = 70,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 20, 0)
        };
        group.Controls.Add(new Label { Text = text, AutoSize = false, Width = group.Width, Height = 24 });
        control.Dock = DockStyle.Top;
        group.Controls.Add(control);
        panel.Controls.Add(group);
    }

    private static List<MonthOption> MonthOptions() =>
    [
        new(1, "Enero"), new(2, "Febrero"), new(3, "Marzo"), new(4, "Abril"),
        new(5, "Mayo"), new(6, "Junio"), new(7, "Julio"), new(8, "Agosto"),
        new(9, "Septiembre"), new(10, "Octubre"), new(11, "Noviembre"), new(12, "Diciembre")
    ];

    private sealed record MonthOption(int Value, string Text)
    {
        public override string ToString() => Text;
    }
}
