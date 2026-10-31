using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

internal sealed class CashSessionDetailForm : Form
{
    private readonly DataGridView _grid = new();

    public CashSessionDetailForm(CashSummary session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Text = "Detalle de sesión de caja";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        ClientSize = new Size(1120, 760);
        MinimumSize = new Size(960, 660);
        UITheme.StyleForm(this);
        BuildUi(session);
    }

    private void BuildUi(CashSummary session)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.Controls.Add(BuildHeader(session), 0, 0);
        root.Controls.Add(BuildSessionInfo(session), 0, 1);
        root.Controls.Add(BuildCashTotals(session), 0, 2);
        root.Controls.Add(BuildMovements(session), 0, 3);
        root.Controls.Add(BuildFooter(), 0, 4);
        Controls.Add(root);
    }

    private static Control BuildHeader(CashSummary session)
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12, 2, 12, 2)
        };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        var title = new Label
        {
            Text = $"Detalle de caja #{session.CajaId?.ToString() ?? "—"}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(title);
        var subtitle = new Label
        {
            Text = "Consulta de solo lectura. Este detalle no agrega ni modifica movimientos ni reportes.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.NormalFont
        };
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(subtitle, 0, 1);
        return header;
    }

    private static Control BuildSessionInfo(CashSummary session)
    {
        var info = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 8)
        };
        for (var column = 0; column < 3; column++)
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));

        info.Controls.Add(CreateInfoCard(
            "Fecha operativa · Estado",
            $"{session.FechaOperacion:dd/MM/yyyy} · {session.Estado?.ToString() ?? "—"}"), 0, 0);
        info.Controls.Add(CreateInfoCard(
            "Apertura",
            $"{FormatTime(session.FechaApertura)} · {session.UsuarioApertura ?? "—"}"), 1, 0);
        info.Controls.Add(CreateInfoCard(
            "Cierre",
            $"{FormatTime(session.FechaCierre)} · {session.UsuarioCierre ?? "—"}"), 2, 0);
        return info;
    }

    private static Control BuildCashTotals(CashSummary session)
    {
        var incoming = session.VentasEfectivoCentavos +
                       session.AbonosClientesEfectivoCentavos +
                       session.AnticiposPedidosEfectivoCentavos +
                       session.AbonosPedidosEfectivoCentavos +
                       session.PagosFinalesPedidosEfectivoCentavos +
                       session.TotalAportesCajaCentavos;
        var outgoing = session.ReembolsosVentasEfectivoCentavos +
                       session.ReembolsosPedidosEfectivoCentavos +
                       session.PagosComprasEfectivoCentavos +
                       session.AbonosProveedoresEfectivoCentavos +
                       session.TotalPagosEmpleadosCentavos +
                       session.TotalGastosCentavos +
                       session.TotalRetirosCentavos;

        var totals = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        for (var column = 0; column < 6; column++)
            totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));

        totals.Controls.Add(CreateAmountCard("Apertura", Money(session.MontoInicialCentavos)), 0, 0);
        totals.Controls.Add(CreateAmountCard("Entradas físicas", Money(incoming)), 1, 0);
        totals.Controls.Add(CreateAmountCard("Salidas físicas", Money(outgoing)), 2, 0);
        totals.Controls.Add(CreateAmountCard("Efectivo esperado", Money(session.TotalEsperadoCentavos)), 3, 0);
        totals.Controls.Add(CreateAmountCard("Contado al cierre", OptionalMoney(session.MontoCierreCentavos)), 4, 0);

        var differenceAmount = session.DiferenciaCentavos.HasValue
            ? $"{Money(session.DiferenciaCentavos.Value)}\r\n{DescribeDifference(session.DiferenciaCentavos.Value)}"
            : "Pendiente de conteo";
        totals.Controls.Add(CreateAmountCard(
            "Diferencia",
            differenceAmount,
            "CashSessionDifference"), 5, 0);
        return totals;
    }

    private Control BuildMovements(CashSummary session)
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        var heading = new Label
        {
            Text = "Movimientos registrados y referencia del día",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.SectionTitleFont,
            ForeColor = UITheme.TextDark
        };
        section.Controls.Add(heading, 0, 0);

        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowTemplate.Height = 30;
        _grid.ColumnHeadersHeight = 40;
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CashSessionMovementRow.Tipo),
            HeaderText = "Movimiento",
            FillWeight = 22,
            MinimumWidth = 135,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CashSessionMovementRow.Concepto),
            HeaderText = "Concepto",
            FillWeight = 62,
            MinimumWidth = 260,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CashSessionMovementRow.Monto),
            HeaderText = "Monto",
            FillWeight = 16,
            MinimumWidth = 130,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.DataBindingComplete += (_, _) => HighlightTotals();
        _grid.DataSource = BuildMovementRows(session);
        section.Controls.Add(_grid, 0, 1);
        section.Controls.Add(new Label
        {
            Text = "Transferencias y Bitcoin son referencia diaria: no se atribuyen a esta caja ni forman parte del efectivo esperado.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.CaptionFont,
            AutoEllipsis = true
        }, 0, 2);
        return section;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 8, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        footer.Controls.Add(new Label
        {
            Text = "La diferencia es contado al cierre menos esperado; negativa = faltante, positiva = sobrante.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.MutedTextColor
        }, 0, 0);
        var close = new Button { Text = "Cerrar", Size = new Size(120, 36), Anchor = AnchorStyles.Right };
        close.Click += (_, _) => Close();
        UITheme.StyleSecondaryButton(close);
        footer.Controls.Add(close, 1, 0);
        CancelButton = close;
        return footer;
    }

    private void HighlightTotals()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is not CashSessionMovementRow { EsTotal: true }) continue;
            row.DefaultCellStyle.BackColor = UITheme.DashboardAccentSoftColor;
            row.DefaultCellStyle.Font = UITheme.NormalBoldFont;
        }
    }

    private static IReadOnlyList<CashSessionMovementRow> BuildMovementRows(CashSummary session)
    {
        var rows = new List<CashSessionMovementRow>
        {
            new("Entrada física", "Ventas y correcciones cobradas en efectivo", Money(session.VentasEfectivoCentavos)),
            new("Entrada física", "Abonos de clientes en efectivo", Money(session.AbonosClientesEfectivoCentavos)),
            new("Entrada física", "Anticipos de pedidos en efectivo", Money(session.AnticiposPedidosEfectivoCentavos)),
            new("Entrada física", "Abonos de pedidos en efectivo", Money(session.AbonosPedidosEfectivoCentavos)),
            new("Entrada física", "Pagos finales de pedidos en efectivo", Money(session.PagosFinalesPedidosEfectivoCentavos)),
            new("Aporte de caja", "Aportes de efectivo para cambio (no son ventas ni ingresos comerciales)", Money(session.TotalAportesCajaCentavos)),
            new("Total", "Total de entradas físicas", Money(
                session.VentasEfectivoCentavos +
                session.AbonosClientesEfectivoCentavos +
                session.AnticiposPedidosEfectivoCentavos +
                session.AbonosPedidosEfectivoCentavos +
                session.PagosFinalesPedidosEfectivoCentavos +
                session.TotalAportesCajaCentavos), true),
            new("Salida física", "Reembolsos de ventas en efectivo", Money(session.ReembolsosVentasEfectivoCentavos)),
            new("Salida física", "Reembolsos de pedidos en efectivo", Money(session.ReembolsosPedidosEfectivoCentavos)),
            new("Salida física", "Pagos de compras en efectivo", Money(session.PagosComprasEfectivoCentavos)),
            new("Salida física", "Abonos a proveedores en efectivo", Money(session.AbonosProveedoresEfectivoCentavos)),
            new("Salida física", "Pagos a empleados en efectivo", Money(session.TotalPagosEmpleadosCentavos)),
            new("Salida física", "Gastos pagados en efectivo", Money(session.TotalGastosCentavos)),
            new("Salida física", "Retiros de caja", Money(session.TotalRetirosCentavos)),
            new("Total", "Total de salidas físicas", Money(
                session.ReembolsosVentasEfectivoCentavos +
                session.ReembolsosPedidosEfectivoCentavos +
                session.PagosComprasEfectivoCentavos +
                session.AbonosProveedoresEfectivoCentavos +
                session.TotalPagosEmpleadosCentavos +
                session.TotalGastosCentavos +
                session.TotalRetirosCentavos), true),
            new("Referencia diaria", "Transferencias recibidas (no efectivo)", Money(session.TransferenciasRecibidasCentavos)),
            new("Referencia diaria", "Bitcoin recibido (no efectivo)", Money(session.BitcoinRecibidoCentavos)),
            new("Referencia diaria", "Transferencias pagadas (no efectivo)", Money(session.TransferenciasPagadasCentavos)),
            new("Referencia diaria", "Bitcoin pagado (no efectivo)", Money(session.BitcoinPagadoCentavos))
        };
        return rows;
    }

    private static Control CreateInfoCard(string title, string value)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(10, 6, 10, 6),
            Margin = new Padding(0, 0, 8, 0)
        };
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        card.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.CaptionFont
        }, 0, 0);
        card.Controls.Add(new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = UITheme.NormalBoldFont
        }, 0, 1);
        return card;
    }

    private static Control CreateAmountCard(string title, string amount, string? namePrefix = null)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(7, 5, 7, 5),
            Margin = new Padding(0, 0, 6, 0)
        };
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        card.Controls.Add(new Label
        {
            Name = namePrefix is null ? string.Empty : $"{namePrefix}Title",
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.CaptionFont
        }, 0, 0);
        card.Controls.Add(new Label
        {
            Name = namePrefix is null ? string.Empty : $"{namePrefix}Value",
            Text = amount,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = UITheme.NormalBoldFont,
            AutoEllipsis = true
        }, 0, 1);
        return card;
    }

    private static string DescribeDifference(long difference) => difference switch
    {
        < 0 => "Faltante",
        > 0 => "Sobrante",
        _ => "Cuadrada"
    };

    private static string Money(long centavos) => $"$ {(centavos / 100m):0.00}";

    private static string OptionalMoney(long? centavos) =>
        centavos.HasValue ? Money(centavos.Value) : "Pendiente";

    private static string FormatTime(DateTime? timestamp)
    {
        if (!timestamp.HasValue) return "—";
        var value = timestamp.Value.Kind == DateTimeKind.Utc ? timestamp.Value.ToLocalTime() : timestamp.Value;
        return value.ToString("dd/MM/yyyy HH:mm");
    }

    private sealed record CashSessionMovementRow(string Tipo, string Concepto, string Monto, bool EsTotal = false);
}
