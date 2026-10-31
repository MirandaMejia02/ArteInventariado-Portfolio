using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms.Controls;

internal sealed class AnalyticsMetricCardControl : DashboardSurfacePanel
{
    private const int LogicalMinimumWidth = 235;
    internal const int LogicalMinimumHeight = 132;

    private readonly TableLayoutPanel _layout = new();
    private readonly Label _icon = new();
    private readonly Label _title = new();
    private readonly Label _value = new();
    private readonly Label _comparison = new();

    public AnalyticsMetricCardControl(string title, string? iconGlyph = null)
    {
        AutoSize = false;
        AutoSizeMode = AutoSizeMode.GrowOnly;
        Dock = DockStyle.None;
        Anchor = AnchorStyles.Top | AnchorStyles.Left;
        BackColor = UITheme.SurfaceColor;
        Margin = new Padding(0, 0, 10, 10);
        Padding = new Padding(12, 10, 12, 10);
        MinimumSize = new Size(LogicalMinimumWidth, LogicalMinimumHeight);
        Size = MinimumSize;

        ConfigureIcon(iconGlyph ?? ResolveIcon(title));
        ConfigureTitle(title);
        ConfigureValue();
        ConfigureComparison();
        BuildLayout();

        _value.TextChanged += (_, _) => FitValueFont();
        _comparison.TextChanged += (_, _) => RefreshContentLayout();
        SizeChanged += (_, _) => RefreshContentLayout();
        VisibleChanged += (_, _) => RefreshContentLayout();
    }

    internal bool IsValueClipped => MeasureSingleLine(_value.Text, _value.Font) > _value.ClientSize.Width;

    internal bool HasContentOverflow(out string diagnostics)
    {
        RefreshContentLayout();
        var controls = new[] { _icon, _title, _value, _comparison };
        foreach (var control in controls.Where(control => control.Visible))
        {
            var bounds = control.Bounds;
            if (bounds.Left < _layout.DisplayRectangle.Left ||
                bounds.Top < _layout.DisplayRectangle.Top ||
                bounds.Right > _layout.DisplayRectangle.Right ||
                bounds.Bottom > _layout.DisplayRectangle.Bottom)
            {
                diagnostics = $"'{control.Text}' bounds={bounds} layout={_layout.DisplayRectangle}";
                return true;
            }

            if (control is Label label && label.PreferredHeight > label.ClientSize.Height)
            {
                diagnostics =
                    $"'{label.Text}' alto={label.ClientSize.Height}px preferido={label.PreferredHeight}px " +
                    $"card={ClientSize.Width}x{ClientSize.Height} DPI={DeviceDpi}";
                return true;
            }
        }

        if (_layout.Bounds.Left < DisplayRectangle.Left ||
            _layout.Bounds.Top < DisplayRectangle.Top ||
            _layout.Bounds.Right > DisplayRectangle.Right ||
            _layout.Bounds.Bottom > DisplayRectangle.Bottom)
        {
            diagnostics = $"layout={_layout.Bounds} área={DisplayRectangle}";
            return true;
        }

        diagnostics = string.Empty;
        return false;
    }

    internal string ValueLayoutDiagnostics =>
        $"'{_value.Text}' font={_value.Font.Size:N1} label={_value.ClientSize.Width}px measured=" +
        $"{MeasureSingleLine(_value.Text, _value.Font)}px display={DisplayRectangle.Width}px card={ClientSize.Width}px";

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width > 0 ? proposedSize.Width : Math.Max(Width, MinimumSize.Width);
        var contentWidth = Math.Max(1, width - Padding.Horizontal);
        ApplyWrappingWidth(contentWidth);
        var preferred = _layout.GetPreferredSize(new Size(contentWidth, 0));
        return new Size(
            Math.Max(MinimumSize.Width, width),
            Math.Max(MinimumSize.Height, preferred.Height + Padding.Vertical));
    }

    public void SetMoney(long centavos, MetricComparison comparison, string? detail = null) =>
        SetValue($"${centavos / 100m:N2}", comparison, detail);

    public void SetNumber(long value, MetricComparison comparison) =>
        SetValue(value.ToString("N0"), comparison);

    public void SetPercentage(DecimalAnalyticsMetricResult metric, string? detail = null)
    {
        _value.Text = metric.Value.HasValue ? $"{metric.Value.Value:N2}%" : "No disponible";
        _comparison.Text = detail ?? metric.State switch
        {
            MetricComparisonState.Increase => $"Sube {metric.ChangePercentage:N1} p. relativos",
            MetricComparisonState.Decrease => $"Baja {Math.Abs(metric.ChangePercentage ?? 0):N1} p. relativos",
            MetricComparisonState.NewMovement => "Nuevo movimiento; sin base anterior",
            MetricComparisonState.NoComparisonBase => "Sin base de comparación",
            _ => "Sin cambios frente al período comparado"
        };
        _comparison.ForeColor = UITheme.MutedTextColor;
    }

    public void SetText(string value, string detail)
    {
        _value.Text = value;
        _comparison.Text = detail;
        _comparison.ForeColor = UITheme.MutedTextColor;
    }

    private void ConfigureIcon(string iconGlyph)
    {
        _icon.Text = iconGlyph;
        _icon.AutoSize = false;
        _icon.MinimumSize = new Size(36, 28);
        _icon.Dock = DockStyle.Fill;
        _icon.Margin = new Padding(0, 0, 8, 2);
        _icon.Padding = Padding.Empty;
        _icon.Font = new Font("Segoe MDL2 Assets", 15f);
        _icon.ForeColor = UITheme.PrimaryColor;
        _icon.BackColor = UITheme.DashboardAccentSoftColor;
        _icon.TextAlign = ContentAlignment.MiddleCenter;
    }

    private void ConfigureTitle(string title)
    {
        _title.Text = title;
        _title.AutoSize = true;
        _title.AutoEllipsis = false;
        _title.Dock = DockStyle.Fill;
        _title.Margin = Padding.Empty;
        _title.Padding = Padding.Empty;
        _title.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _title.ForeColor = UITheme.MutedTextColor;
        _title.TextAlign = ContentAlignment.MiddleLeft;
    }

    private void ConfigureValue()
    {
        _value.Text = "--";
        _value.AutoSize = true;
        _value.AutoEllipsis = false;
        _value.Dock = DockStyle.Fill;
        _value.Margin = new Padding(0, 3, 0, 1);
        _value.Padding = Padding.Empty;
        _value.Font = new Font("Segoe UI", 17f, FontStyle.Bold);
        _value.ForeColor = UITheme.TextDark;
        _value.TextAlign = ContentAlignment.MiddleLeft;
    }

    private void ConfigureComparison()
    {
        _comparison.Text = "Cargando...";
        _comparison.AutoSize = true;
        _comparison.AutoEllipsis = false;
        _comparison.Dock = DockStyle.Fill;
        _comparison.Margin = new Padding(0, 1, 0, 0);
        _comparison.Padding = Padding.Empty;
        _comparison.Font = new Font("Segoe UI", 8.5f);
        _comparison.ForeColor = UITheme.MutedTextColor;
        _comparison.TextAlign = ContentAlignment.TopLeft;
    }

    private void BuildLayout()
    {
        _layout.AutoSize = false;
        _layout.AutoSizeMode = AutoSizeMode.GrowOnly;
        _layout.Dock = DockStyle.Fill;
        _layout.Margin = Padding.Empty;
        _layout.Padding = Padding.Empty;
        _layout.BackColor = Color.Transparent;
        _layout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
        _layout.ColumnCount = 2;
        _layout.RowCount = 5;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _layout.Controls.Add(_icon, 0, 1);
        _layout.Controls.Add(_title, 1, 1);
        _layout.Controls.Add(_value, 0, 2);
        _layout.SetColumnSpan(_value, 2);
        _layout.Controls.Add(_comparison, 0, 3);
        _layout.SetColumnSpan(_comparison, 2);
        Controls.Add(_layout);
    }

    private void SetValue(string value, MetricComparison comparison, string? detail = null)
    {
        _value.Text = value;
        _comparison.Text = detail ?? FormatComparison(comparison);
        _comparison.ForeColor = comparison.State switch
        {
            MetricComparisonState.Increase => UITheme.SuccessColor,
            MetricComparisonState.Decrease => UITheme.DangerColor,
            _ => UITheme.MutedTextColor
        };
    }

    private void RefreshContentLayout()
    {
        if (IsDisposed || ClientSize.Width <= 0)
        {
            return;
        }

        ApplyWrappingWidth(Math.Max(1, DisplayRectangle.Width));
        FitValueFont();
        _layout.PerformLayout();
    }

    private void ApplyWrappingWidth(int contentWidth)
    {
        _title.MaximumSize = new Size(Math.Max(1, contentWidth - _icon.MinimumSize.Width - _icon.Margin.Horizontal), 0);
        _comparison.MaximumSize = new Size(contentWidth, 0);
    }

    private void FitValueFont()
    {
        var availableWidth = Math.Max(0, DisplayRectangle.Width);
        if (availableWidth <= 0)
        {
            return;
        }

        var size = 17f;
        while (size > 9.5f)
        {
            using var candidate = new Font("Segoe UI", size, FontStyle.Bold);
            if (MeasureSingleLine(_value.Text, candidate) <= availableWidth)
            {
                break;
            }
            size -= 0.5f;
        }

        if (Math.Abs(_value.Font.Size - size) > 0.1f)
        {
            _value.Font = new Font("Segoe UI", size, FontStyle.Bold);
        }
    }

    private static int MeasureSingleLine(string text, Font font) =>
        TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;

    private static string ResolveIcon(string title) => title switch
    {
        "Brutas" or "Netas" or "Ventas netas" or "Ventas efectivo" => "\uE7BF",
        "Descuentos" => "\uE8D4",
        "Devoluciones" or "Reembolsos" => "\uE7A7",
        "Transacciones" or "Cuentas pagadas" => "\uE8C7",
        "Ticket" => "\uE8A7",
        "Unidades" or "Inventario actual" or "Inversión actual" or "Venta potencial" or "Venta disponible" => "\uE7B8",
        "Cotizaciones" or "Importe cotizado" or "Aceptadas" => "\uE8A5",
        "Pedidos" or "Saldo pedidos" or "Atrasados" => "\uE8F1",
        "Anticipos" => "\uE8C7",
        "Pedido → venta" => "\uE7BF",
        "Costo vendido" or "Gastos operativos" or "Gastos" => "\uE9D2",
        "Ganancia bruta" or "Margen bruto" or "Ganancia bruta potencial" or "Margen bruto potencial" => "\uE9D9",
        "Inversión inventario" or "Inversión recibida" or "Mercancía recibida" => "\uE719",
        "Efectivo esperado" or "Otras entradas" or "Aportes para caja" or "Movimiento neto" => "\uE8C7",
        "Cobros clientes" => "\uE716",
        "Pagos proveedores" or "Compras comprometidas" => "\uE8CB",
        "Retiros" or "Diferencia cierre" => "\uE74D",
        "Saldo pendiente" or "Creadas en período" or "Pagos del período" => "\uE8A5",
        "Cuentas vencidas" => "\uE823",
        _ => "\uE9D2"
    };

    private static string FormatComparison(MetricComparison comparison) => comparison.State switch
    {
        MetricComparisonState.Increase => $"↑ {comparison.Percentage:N1}% vs. período comparado",
        MetricComparisonState.Decrease => $"↓ {Math.Abs(comparison.Percentage ?? 0):N1}% vs. período comparado",
        MetricComparisonState.NewMovement => "Nuevo movimiento; sin base anterior",
        MetricComparisonState.NoComparisonBase => "Sin base de comparación",
        _ => "Sin cambios frente al período comparado"
    };
}
