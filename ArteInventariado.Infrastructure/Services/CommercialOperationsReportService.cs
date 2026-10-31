using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class CommercialOperationsReportService : ICommercialOperationsReportService
{
    private const string CurrencyFormat = "$#,##0.00";
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _session;
    private readonly IEmployeePermissionService _permissions;
    private readonly IAuditService _audit;
    private readonly IReportPathProvider _paths;
    private readonly ICommercialAlertQueryService _alerts;
    private readonly ICashMovementQueryService _cashMovements;

    [ActivatorUtilitiesConstructor]
    public CommercialOperationsReportService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession session,
        IEmployeePermissionService permissions,
        IAuditService audit,
        IReportPathProvider paths,
        ICommercialAlertQueryService alerts,
        ICashMovementQueryService cashMovements)
    {
        _contextFactory = contextFactory;
        _session = session;
        _permissions = permissions;
        _audit = audit;
        _paths = paths;
        _alerts = alerts;
        _cashMovements = cashMovements;
    }

    internal CommercialOperationsReportService(
        AppDbContext context,
        ICurrentSession session,
        IEmployeePermissionService permissions,
        IAuditService audit,
        IReportPathProvider paths,
        ICommercialAlertQueryService alerts,
        ICashMovementQueryService cashMovements)
    {
        _legacyContext = context;
        _session = session;
        _permissions = permissions;
        _audit = audit;
        _paths = paths;
        _alerts = alerts;
        _cashMovements = cashMovements;
    }

    public async Task<ReportExportResult> ExportCommercialOperationsAsync(
        CommercialReportRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        await EnsurePermissionAsync(cancellationToken);
        Validate(request);
        var (startUtc, endUtc) = Range(request);

        var quotations = await lease.Context.Cotizaciones.AsNoTracking()
            .Include(c => c.UsuarioCreador)
            .Where(c => c.Fecha >= startUtc && c.Fecha < endUtc)
            .OrderBy(c => c.Fecha)
            .ToListAsync(cancellationToken);
        var orders = await lease.Context.PedidosPersonalizados.AsNoTracking()
            .Include(p => p.Cotizacion)
            .Include(p => p.Venta)
            .Where(p => p.FechaPedido >= startUtc && p.FechaPedido < endUtc)
            .OrderBy(p => p.FechaPedido)
            .ToListAsync(cancellationToken);
        var orderPayments = await lease.Context.PagosPedidoPersonalizado.AsNoTracking()
            .Include(p => p.PedidoPersonalizado)
            .Include(p => p.Usuario)
            .Include(p => p.VentaAplicada)
            .Where(p => p.Fecha >= startUtc && p.Fecha < endUtc)
            .OrderBy(p => p.Fecha)
            .ToListAsync(cancellationToken);
        var refunds = await lease.Context.ReembolsosPedidoPersonalizado.AsNoTracking()
            .Include(r => r.PedidoPersonalizado)
            .Include(r => r.Usuario)
            .Where(r => r.Fecha >= startUtc && r.Fecha < endUtc)
            .OrderBy(r => r.Fecha)
            .ToListAsync(cancellationToken);
        var reservations = await lease.Context.ReservasInventarioPedido.AsNoTracking()
            .Include(r => r.PedidoPersonalizado)
            .Include(r => r.Producto)
            .Where(r => r.FechaReserva >= startUtc && r.FechaReserva < endUtc)
            .OrderBy(r => r.FechaReserva)
            .ToListAsync(cancellationToken);
        var conversions = await lease.Context.PedidosPersonalizados.AsNoTracking()
            .Include(p => p.Cliente)
            .Include(p => p.Venta)
            .Include(p => p.Pagos)
            .Where(p => p.FechaConversion >= startUtc && p.FechaConversion < endUtc && p.VentaId.HasValue)
            .OrderBy(p => p.FechaConversion)
            .ToListAsync(cancellationToken);
        var convertedSaleIds = conversions.Where(p => p.VentaId.HasValue).Select(p => p.VentaId!.Value).ToList();
        var credits = await lease.Context.CuentasPorCobrar.AsNoTracking()
            .Where(c => convertedSaleIds.Contains(c.VentaId))
            .ToDictionaryAsync(c => c.VentaId, c => c.CapitalFinanciadoCentavos, cancellationToken);
        var currentAlerts = await _alerts.SearchAsync(new CommercialAlertFilter(), cancellationToken);

        using var workbook = new XLWorkbook();
        WriteQuotations(workbook, quotations);
        WriteOrders(workbook, orders);
        WriteOrderPayments(workbook, orderPayments);
        WriteRefunds(workbook, refunds);
        WriteReservations(workbook, reservations);
        WriteConversions(workbook, conversions, credits);
        WriteAlerts(workbook, currentAlerts.Alerts);

        var directory = await _paths.GetDirectoryAsync("Operaciones comerciales", cancellationToken);
        var fileName = $"Reporte_Operaciones_Comerciales_{request.Desde:yyyyMMdd}_{request.Hasta:yyyyMMdd}.xlsx";
        var filePath = UniquePath(directory, fileName);
        workbook.SaveAs(filePath);
        await AuditAsync(TipoBitacora.REPORTE_OPERACIONES_COMERCIALES_EXPORTADO,
            $"Reporte de operaciones comerciales exportado del {request.Desde:yyyy-MM-dd} al {request.Hasta:yyyy-MM-dd}. Archivo: {filePath}",
            cancellationToken);
        return new(filePath, Path.GetFileName(filePath), DateTime.Now, request.Desde.Date, request.Hasta.Date);
    }

    public async Task<ReportExportResult> ExportCashDetailAsync(
        CommercialReportRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        await EnsurePermissionAsync(cancellationToken);
        Validate(request);
        var result = await _cashMovements.SearchAsync(
            new CashMovementFilter(request.Desde, request.Hasta), cancellationToken);

        using var workbook = new XLWorkbook();
        WriteCashSummary(workbook, result.Summary);
        WriteCashMovements(workbook, result.Movements);
        var directory = await _paths.GetDirectoryAsync("Caja", cancellationToken);
        var fileName = $"Reporte_Caja_{request.Desde:yyyyMMdd}_{request.Hasta:yyyyMMdd}.xlsx";
        var filePath = UniquePath(directory, fileName);
        workbook.SaveAs(filePath);
        await AuditAsync(TipoBitacora.REPORTE_CAJA_EXPORTADO,
            $"Reporte de caja exportado del {request.Desde:yyyy-MM-dd} al {request.Hasta:yyyy-MM-dd}. Archivo: {filePath}",
            cancellationToken);
        return new(filePath, Path.GetFileName(filePath), DateTime.Now, request.Desde.Date, request.Hasta.Date);
    }

    private static void WriteQuotations(XLWorkbook workbook, IReadOnlyList<Cotizacion> rows) => WriteSheet(
        workbook, "Cotizaciones",
        ["Codigo", "Fecha", "Vencimiento", "Cliente/destinatario", "Estado", "Subtotal", "Descuento", "Total", "Usuario"],
        rows.Select(c => new object?[]
        {
            c.CodigoCotizacion, Local(c.Fecha), Local(c.FechaVencimiento), c.NombreDestinatarioSnapshot,
            c.Estado.ToString(), Money(c.SubtotalCentavos), Money(c.DescuentoGeneralCentavos), Money(c.TotalCentavos),
            c.UsuarioCreador.NombreUsuario
        }), [6, 7, 8]);

    private static void WriteOrders(XLWorkbook workbook, IReadOnlyList<PedidoPersonalizado> rows) => WriteSheet(
        workbook, "Pedidos",
        ["Codigo", "Cotizacion", "Fecha", "Fecha prometida", "Cliente/destinatario", "Estado operativo", "Estado financiero", "Prioridad", "Total", "Pagado", "Reembolsado", "Saldo"],
        rows.Select(p => new object?[]
        {
            p.CodigoPedido, p.Cotizacion?.CodigoCotizacion, Local(p.FechaPedido), Local(p.FechaPrometida),
            p.NombreDestinatarioSnapshot, p.Estado.ToString(), p.EstadoFinanciero.ToString(), p.Prioridad.ToString(),
            Money(p.TotalEstimadoCentavos), Money(p.TotalPagadoCentavos), Money(p.TotalReembolsadoCentavos), Money(p.SaldoPendienteCentavos)
        }), [9, 10, 11, 12]);

    private static void WriteOrderPayments(XLWorkbook workbook, IReadOnlyList<PagoPedidoPersonalizado> rows) => WriteSheet(
        workbook, "Pagos pedidos",
        ["Codigo", "Pedido", "Fecha", "Tipo", "Metodo", "Referencia", "Monto", "Usuario", "Venta aplicada"],
        rows.Select(p => new object?[]
        {
            p.CodigoPago, p.PedidoPersonalizado.CodigoPedido, Local(p.Fecha), p.TipoPago.ToString(), p.MetodoPago.ToString(),
            p.Referencia, Money(p.MontoCentavos), p.Usuario.NombreUsuario, p.VentaAplicada?.NumeroVenta
        }), [7]);

    private static void WriteRefunds(XLWorkbook workbook, IReadOnlyList<ReembolsoPedidoPersonalizado> rows) => WriteSheet(
        workbook, "Reembolsos",
        ["Codigo", "Pedido", "Fecha", "Metodo", "Monto", "Motivo", "Usuario"],
        rows.Select(r => new object?[]
        {
            r.CodigoReembolso, r.PedidoPersonalizado.CodigoPedido, Local(r.Fecha), r.MetodoPago.ToString(),
            Money(r.MontoCentavos), r.Motivo, r.Usuario.NombreUsuario
        }), [5]);

    private static void WriteReservations(XLWorkbook workbook, IReadOnlyList<ReservaInventarioPedido> rows) => WriteSheet(
        workbook, "Reservas",
        ["Pedido", "Producto", "Cantidad reservada", "Consumida", "Liberada", "Estado"],
        rows.Select(r => new object?[]
        {
            r.PedidoPersonalizado.CodigoPedido, $"{r.Producto.CodigoInterno} - {r.Producto.Nombre}",
            r.CantidadReservada, r.CantidadConsumida, r.CantidadLiberada, r.Estado.ToString()
        }));

    private static void WriteConversions(
        XLWorkbook workbook,
        IReadOnlyList<PedidoPersonalizado> rows,
        IReadOnlyDictionary<int, long> credits) => WriteSheet(
        workbook, "Conversiones",
        ["Pedido", "Venta", "Cliente", "Fecha", "Total pedido", "Pagos previos", "Pago final", "Credito generado"],
        rows.Select(p => new object?[]
        {
            p.CodigoPedido, p.Venta?.NumeroVenta, p.Cliente?.NombreCompleto ?? p.NombreDestinatarioSnapshot,
            p.FechaConversion.HasValue ? Local(p.FechaConversion.Value) : null, Money(p.TotalEstimadoCentavos),
            Money(p.Pagos.Where(x => x.TipoPago != TipoPagoPedido.PAGO_FINAL).Sum(x => x.MontoCentavos)),
            Money(p.Pagos.Where(x => x.TipoPago == TipoPagoPedido.PAGO_FINAL).Sum(x => x.MontoCentavos)),
            Money(p.VentaId.HasValue ? credits.GetValueOrDefault(p.VentaId.Value) : 0)
        }), [5, 6, 7, 8]);

    private static void WriteAlerts(XLWorkbook workbook, IReadOnlyList<CommercialAlertRow> rows) => WriteSheet(
        workbook, "Alertas actuales",
        ["Tipo", "Documento", "Cliente", "Estado", "Fecha relevante", "Dias restantes/atraso", "Saldo", "Descripcion"],
        rows.Select(a => new object?[]
        {
            Friendly(a.Tipo), a.Documento, a.ClienteDestinatario, a.Estado, Local(a.FechaRelevante),
            a.DiasAtraso, Money(a.SaldoCentavos), a.Descripcion
        }), [7]);

    private static void WriteCashSummary(XLWorkbook workbook, CashAdvancedSummary s)
    {
        var rows = new List<object?[]>
        {
            new object?[] { "Periodo", $"{s.Desde:yyyy-MM-dd} a {s.Hasta:yyyy-MM-dd}" },
            new object?[] { "Monto de apertura", Money(s.MontoAperturaCentavos) },
            new object?[] { "Ventas en efectivo", Money(s.VentasEfectivoCentavos) },
            new object?[] { "Abonos de clientes", Money(s.AbonosClientesEfectivoCentavos) },
            new object?[] { "Anticipos de pedidos", Money(s.AnticiposPedidosEfectivoCentavos) },
            new object?[] { "Abonos de pedidos", Money(s.AbonosPedidosEfectivoCentavos) },
            new object?[] { "Pagos finales de pedidos", Money(s.PagosFinalesPedidosEfectivoCentavos) },
            new object?[] { "Reembolsos de ventas", Money(s.ReembolsosVentasEfectivoCentavos) },
            new object?[] { "Reembolsos de pedidos", Money(s.ReembolsosPedidosEfectivoCentavos) },
            new object?[] { "Pagos iniciales a proveedores", Money(s.PagosComprasEfectivoCentavos) },
            new object?[] { "Abonos a proveedores", Money(s.AbonosProveedoresEfectivoCentavos) },
            new object?[] { "Gastos", Money(s.GastosCentavos) },
            new object?[] { "Retiros", Money(s.RetirosCentavos) },
            new object?[] { "Aportes de efectivo a caja (no son venta/ingreso comercial)", Money(s.AportesCajaCentavos) },
            new object?[] { "Transferencias recibidas", Money(s.TransferenciasRecibidasCentavos) },
            new object?[] { "Bitcoin recibido", Money(s.BitcoinRecibidoCentavos) },
            new object?[] { "Transferencias pagadas", Money(s.TransferenciasPagadasCentavos) },
            new object?[] { "Bitcoin pagado", Money(s.BitcoinPagadoCentavos) },
            new object?[] { "Efectivo esperado", Money(s.EfectivoEsperadoCentavos) },
            new object?[] { "Efectivo contado", s.EfectivoContadoCentavos.HasValue ? Money(s.EfectivoContadoCentavos.Value) : null },
            new object?[] { "Diferencia", s.DiferenciaCentavos.HasValue ? Money(s.DiferenciaCentavos.Value) : null }
        };
        WriteSheet(workbook, "Resumen de caja", ["Concepto", "Valor"], rows, [2]);
    }

    private static void WriteCashMovements(XLWorkbook workbook, IReadOnlyList<CashMovementRow> rows) => WriteSheet(
        workbook, "Movimientos",
        ["Fecha y hora", "Tipo", "Origen", "Caja", "Documento", "Descripcion", "Metodo", "Entrada", "Salida", "Saldo despues", "Afecta efectivo", "Usuario", "Referencia"],
        rows.Select(m => new object?[]
        {
            Local(m.Fecha), m.Tipo.ToString(), Friendly(m.Origen), m.CajaId, m.Documento, m.Descripcion,
            m.Metodo?.ToString() ?? "Método no especificado", Money(m.EntradaCentavos), Money(m.SalidaCentavos),
            m.EfectivoDespuesCentavos.HasValue ? Money(m.EfectivoDespuesCentavos.Value) : "",
            m.AfectaEfectivo ? "SI" : "NO", m.Usuario, m.Referencia
        }), [8, 9, 10]);

    private static void WriteSheet(
        XLWorkbook workbook,
        string name,
        IReadOnlyList<string> headers,
        IEnumerable<object?[]> rows,
        IReadOnlyCollection<int>? moneyColumns = null)
    {
        var worksheet = workbook.Worksheets.Add(name);
        for (var column = 1; column <= headers.Count; column++)
        {
            worksheet.Cell(1, column).Value = headers[column - 1];
            worksheet.Cell(1, column).Style.Font.Bold = true;
            worksheet.Cell(1, column).Style.Fill.BackgroundColor = XLColor.FromHtml("#7A2F55");
            worksheet.Cell(1, column).Style.Font.FontColor = XLColor.White;
        }

        var rowNumber = 2;
        foreach (var values in rows)
        {
            for (var column = 1; column <= values.Length; column++)
            {
                SetValue(worksheet.Cell(rowNumber, column), values[column - 1]);
                if (moneyColumns?.Contains(column) == true)
                    worksheet.Cell(rowNumber, column).Style.NumberFormat.Format = CurrencyFormat;
            }
            rowNumber++;
        }

        worksheet.SheetView.FreezeRows(1);
        worksheet.Range(1, 1, Math.Max(1, rowNumber - 1), headers.Count).SetAutoFilter();
        worksheet.Columns().AdjustToContents(8, 45);
        worksheet.Rows().AdjustToContents();
    }

    private static void SetValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null: cell.Clear(); break;
            case string text: cell.Value = text; break;
            case DateTime date: cell.Value = date; cell.Style.DateFormat.Format = "yyyy-MM-dd HH:mm"; break;
            case decimal number: cell.Value = number; break;
            case int number: cell.Value = number; break;
            case long number: cell.Value = number; break;
            case bool flag: cell.Value = flag; break;
            default: cell.Value = value.ToString(); break;
        }
    }

    private async Task EnsurePermissionAsync(CancellationToken cancellationToken) =>
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.REPORTES_COMERCIALES_VER, cancellationToken);

    private async Task AuditAsync(TipoBitacora type, string description, CancellationToken cancellationToken)
    {
        var userId = _session.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa para exportar reportes.");
        await _audit.RegistrarAsync(new AuditEntryRequest(userId, type, "reportes", null, description), cancellationToken);
    }

    private static void Validate(CommercialReportRequest request)
    {
        if (request.Desde.Date > request.Hasta.Date)
            throw new ArgumentException("La fecha desde no puede ser posterior a la fecha hasta.", nameof(request));
        if ((request.Hasta.Date - request.Desde.Date).TotalDays > 3660)
            throw new ArgumentException("El periodo del reporte es demasiado amplio.", nameof(request));
    }

    private static (DateTime StartUtc, DateTime EndUtc) Range(CommercialReportRequest request)
    {
        var start = DateTime.SpecifyKind(request.Desde.Date, DateTimeKind.Local).ToUniversalTime();
        var end = DateTime.SpecifyKind(request.Hasta.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
        return (start, end);
    }

    private static string UniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return path;
        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:HHmmss}{Path.GetExtension(fileName)}");
    }

    private static decimal Money(long cents) => cents / 100m;
    private static DateTime Local(DateTime date) => date.Kind == DateTimeKind.Utc ? date.ToLocalTime() : date;
    private static string Friendly<T>(T value) where T : Enum
    {
        if (value is CashMovementOrigin origin)
        {
            return origin switch
            {
                CashMovementOrigin.AJUSTE_APERTURA_CAJA => "AJUSTE APERTURA CAJA",
                CashMovementOrigin.AJUSTE_CIERRE_CAJA => "AJUSTE CIERRE CAJA",
                _ => origin.ToString().Replace('_', ' ')
            };
        }

        return value.ToString().Replace('_', ' ');
    }
}
