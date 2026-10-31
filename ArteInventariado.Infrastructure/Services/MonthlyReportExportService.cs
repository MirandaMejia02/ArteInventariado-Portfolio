using System.Globalization;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class MonthlyReportExportService : IMonthlyReportExportService
{
    private const string CurrencyFormat = "$#,##0.00";
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IAuditService _auditService;
    private readonly IReportPathProvider _reportPathProvider;

    [ActivatorUtilitiesConstructor]
    public MonthlyReportExportService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IAuditService auditService,
        IReportPathProvider reportPathProvider)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _auditService = auditService;
        _reportPathProvider = reportPathProvider;
    }

    internal MonthlyReportExportService(
        AppDbContext context,
        ICurrentSession currentSession,
        IAuditService auditService)
        : this(context, currentSession, auditService, new ReportPathProvider(context))
    {
    }

    internal MonthlyReportExportService(
        AppDbContext context,
        ICurrentSession currentSession,
        IAuditService auditService,
        IReportPathProvider reportPathProvider)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _auditService = auditService;
        _reportPathProvider = reportPathProvider;
    }

    public async Task<MonthlyReportExportResult> ExportMonthlyReportAsync(
        MonthlyReportExportRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var context = lease.Context;
        ValidateRequest(request);
        EnsureAdminSession();

        var (startUtc, endUtc) = GetMonthUtcRange(request.Year, request.Month);
        var generatedAt = DateTime.Now;

        var ventas = await context.Ventas
            .AsNoTracking()
            .Include(v => v.Usuario)
            .Include(v => v.Pagos)
            .Include(v => v.Detalles)
            .ThenInclude(d => d.Producto)
            .Where(v => v.FechaVenta >= startUtc && v.FechaVenta < endUtc)
            .OrderBy(v => v.FechaVenta)
            .ToListAsync(cancellationToken);

        var reembolsos = await context.Reembolsos
            .AsNoTracking()
            .Include(r => r.Usuario)
            .Where(r => r.FechaReembolso >= startUtc && r.FechaReembolso < endUtc)
            .OrderBy(r => r.FechaReembolso)
            .ToListAsync(cancellationToken);

        var cobrosCorreccion = await context.PagosCorreccionVenta
            .AsNoTracking()
            .Include(p => p.Usuario)
            .Where(p => p.FechaPago >= startUtc && p.FechaPago < endUtc)
            .OrderBy(p => p.FechaPago)
            .ToListAsync(cancellationToken);

        var gastos = await context.Gastos
            .AsNoTracking()
            .Include(g => g.Usuario)
            .Where(g => g.FechaGasto >= startUtc && g.FechaGasto < endUtc)
            .OrderBy(g => g.FechaGasto)
            .ToListAsync(cancellationToken);

        var retiros = await context.RetirosCaja
            .AsNoTracking()
            .Include(r => r.Usuario)
            .Where(r => r.FechaRetiro >= startUtc && r.FechaRetiro < endUtc)
            .OrderBy(r => r.FechaRetiro)
            .ToListAsync(cancellationToken);

        var creditAccounts = await context.CuentasPorCobrar
            .AsNoTracking()
            .Include(c => c.Cliente)
            .Include(c => c.Venta)
            .ThenInclude(v => v.Pagos)
            .Where(c => c.FechaCreacion >= startUtc && c.FechaCreacion < endUtc)
            .OrderBy(c => c.FechaCreacion)
            .ToListAsync(cancellationToken);

        var accountPayments = await context.AbonosCuentaPorCobrar
            .AsNoTracking()
            .Include(a => a.Usuario)
            .Include(a => a.CuentaPorCobrar)
            .ThenInclude(c => c.Cliente)
            .Include(a => a.CuentaPorCobrar)
            .ThenInclude(c => c.Venta)
            .Where(a => a.FechaAbono >= startUtc && a.FechaAbono < endUtc)
            .OrderBy(a => a.FechaAbono)
            .ToListAsync(cancellationToken);

        var portfolioAccounts = await context.CuentasPorCobrar
            .AsNoTracking()
            .Include(c => c.Cliente)
            .Include(c => c.Venta)
            .Include(c => c.Abonos)
            .Where(c => c.FechaCreacion < endUtc)
            .OrderBy(c => c.Cliente.NombreCompleto)
            .ThenBy(c => c.FechaVencimiento)
            .ToListAsync(cancellationToken);

        var morosoDays = await GetMorosoDaysAsync(context, cancellationToken);

        var inventario = await context.Productos
            .AsNoTracking()
            .Include(p => p.Categoria)
            .Include(p => p.Skus)
                .ThenInclude(sku => sku.SaldosInventario)
            .OrderBy(p => p.Nombre)
            .ToListAsync(cancellationToken);

        var purchases = await context.Compras
            .AsNoTracking()
            .Include(c => c.Proveedor)
            .Include(c => c.UsuarioRegistro)
            .Include(c => c.Detalles)
            .Where(c => c.FechaCompra >= startUtc && c.FechaCompra < endUtc)
            .OrderBy(c => c.FechaCompra)
            .ToListAsync(cancellationToken);

        var purchaseReceptions = await context.RecepcionesCompra
            .AsNoTracking()
            .Include(r => r.Compra).ThenInclude(c => c.Proveedor)
            .Include(r => r.UsuarioRecepcion)
            .Include(r => r.Detalles).ThenInclude(d => d.Producto)
            .Where(r => r.FechaRecepcion >= startUtc && r.FechaRecepcion < endUtc)
            .OrderBy(r => r.FechaRecepcion)
            .ToListAsync(cancellationToken);

        var initialSupplierPayments = await context.PagosCompra
            .AsNoTracking()
            .Include(p => p.Compra).ThenInclude(c => c.Proveedor)
            .Include(p => p.Compra).ThenInclude(c => c.CuentaPorPagar)
            .Include(p => p.Usuario)
            .Where(p => p.FechaPago >= startUtc && p.FechaPago < endUtc)
            .OrderBy(p => p.FechaPago)
            .ToListAsync(cancellationToken);

        var supplierPayments = await context.AbonosCuentaPorPagar
            .AsNoTracking()
            .Include(a => a.CuentaPorPagar).ThenInclude(c => c.Compra)
            .Include(a => a.CuentaPorPagar).ThenInclude(c => c.Proveedor)
            .Include(a => a.Usuario)
            .Where(a => a.FechaAbono >= startUtc && a.FechaAbono < endUtc)
            .OrderBy(a => a.FechaAbono)
            .ToListAsync(cancellationToken);

        var employeePayments = await context.EmployeePayments
            .AsNoTracking()
            .Include(payment => payment.User)
            .Include(payment => payment.RegisteredBy)
            .Include(payment => payment.CancelledBy)
            .Where(payment => payment.CreatedAtUtc >= startUtc && payment.CreatedAtUtc < endUtc)
            .OrderBy(payment => payment.CreatedAtUtc)
            .ThenBy(payment => payment.EmployeePaymentId)
            .ToListAsync(cancellationToken);

        var outOfCashPayments = BuildOutOfCashPaymentRows(
            initialSupplierPayments,
            supplierPayments,
            gastos,
            employeePayments);

        var payableAccounts = await context.CuentasPorPagar
            .AsNoTracking()
            .Include(c => c.Compra).ThenInclude(c => c.Pagos)
            .Include(c => c.Proveedor)
            .Include(c => c.UsuarioCreador)
            .Include(c => c.Abonos)
            .Where(c => c.FechaCreacion < endUtc)
            .OrderBy(c => c.FechaVencimiento)
            .ToListAsync(cancellationToken);

        var costHistory = await context.HistorialCostosProducto
            .AsNoTracking()
            .Include(h => h.Producto)
            .Include(h => h.Proveedor)
            .Include(h => h.Compra)
            .Include(h => h.Usuario)
            .Where(h => h.Fecha >= startUtc && h.Fecha < endUtc)
            .OrderBy(h => h.Fecha)
            .ToListAsync(cancellationToken);

        var reportDirectory = await _reportPathProvider.GetRootDirectoryAsync(cancellationToken);
        Directory.CreateDirectory(reportDirectory);

        var monthName = GetMonthName(request.Month);
        var fileName = $"Reporte_Mensual_{monthName}_{request.Year}.xlsx";
        var filePath = Path.Combine(reportDirectory, fileName);

        using var workbook = new XLWorkbook();
        WriteSummarySheet(
            workbook,
            ventas,
            gastos,
            retiros,
            reembolsos,
            cobrosCorreccion,
            creditAccounts,
            accountPayments,
            portfolioAccounts,
            purchases,
            purchaseReceptions,
            initialSupplierPayments,
            supplierPayments,
            outOfCashPayments,
            payableAccounts,
            morosoDays,
            endUtc,
            request,
            generatedAt);
        WriteSalesSheet(workbook, ventas);
        WriteSoldProductsSheet(workbook, ventas);
        WriteCreditSalesSheet(workbook, creditAccounts);
        WriteAccountPaymentsSheet(workbook, accountPayments);
        WriteCurrentPortfolioSheet(workbook, portfolioAccounts, morosoDays, endUtc);
        WriteCashOutputsSheet(workbook, gastos, retiros, reembolsos, cobrosCorreccion);
        WriteInventorySheet(workbook, inventario);
        WritePurchasesSheet(workbook, purchases);
        WritePurchaseReceptionsSheet(workbook, purchaseReceptions);
        WriteSupplierPaymentsSheet(workbook, initialSupplierPayments, supplierPayments);
        WriteEmployeePaymentsSheet(workbook, employeePayments);
        WriteOutOfCashPaymentsSheet(workbook, outOfCashPayments, monthName, request.Year);
        WriteAccountsPayableSheet(workbook, payableAccounts, endUtc);
        WriteCostHistorySheet(workbook, costHistory);

        workbook.SaveAs(filePath);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                _currentSession.UsuarioActual!.UsuarioId,
                TipoBitacora.EXPORTACION_EXCEL,
                "reportes",
                null,
                $"Reporte mensual exportado: {monthName} {request.Year}. Archivo: {filePath}"),
            cancellationToken);

        return new MonthlyReportExportResult(
            filePath,
            fileName,
            request.Year,
            request.Month,
            generatedAt);
    }

    private static void WriteSummarySheet(
        XLWorkbook workbook,
        IReadOnlyList<Venta> ventas,
        IReadOnlyList<Gasto> gastos,
        IReadOnlyList<RetiroCaja> retiros,
        IReadOnlyList<Reembolso> reembolsos,
        IReadOnlyList<PagoCorreccionVenta> cobrosCorreccion,
        IReadOnlyList<CuentaPorCobrar> creditAccounts,
        IReadOnlyList<AbonoCuentaPorCobrar> accountPayments,
        IReadOnlyList<CuentaPorCobrar> portfolioAccounts,
        IReadOnlyList<Compra> purchases,
        IReadOnlyList<RecepcionCompra> purchaseReceptions,
        IReadOnlyList<PagoCompra> initialSupplierPayments,
        IReadOnlyList<AbonoCuentaPorPagar> supplierPayments,
        IReadOnlyList<OutOfCashPaymentReportRow> outOfCashPayments,
        IReadOnlyList<CuentaPorPagar> payableAccounts,
        int morosoDays,
        DateTime reportEndUtc,
        MonthlyReportExportRequest request,
        DateTime generatedAt)
    {
        var ws = workbook.Worksheets.Add("Resumen mensual");
        ws.Cell(1, 1).Value = $"Reporte mensual - {GetMonthName(request.Month)} {request.Year}";
        ws.Range(1, 1, 1, 2).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;

        var totalVentas = ventas.Sum(v => v.TotalCentavos);
        var totalEfectivoVentas = ventas.Sum(GetNetCashCentavos);
        var totalEfectivoCorrecciones = cobrosCorreccion
            .Where(p => p.MetodoPago == MetodoPago.EFECTIVO)
            .Sum(p => p.MontoCentavos);
        var totalAbonosEfectivo = accountPayments
            .Where(a => a.MetodoPago == MetodoPago.EFECTIVO)
            .Sum(a => a.MontoCentavos);
        var totalReembolsosEfectivo = reembolsos
            .Where(r => r.MetodoReembolso == MetodoReembolso.EFECTIVO)
            .Sum(r => r.MontoCentavos);
        var totalEfectivo = totalEfectivoVentas + totalEfectivoCorrecciones + totalAbonosEfectivo - totalReembolsosEfectivo;
        var totalTransferencia = ventas
            .SelectMany(v => v.Pagos)
            .Where(p => p.MetodoPago == MetodoPago.TRANSFERENCIA)
            .Sum(p => p.MontoCentavos) +
            accountPayments.Where(a => a.MetodoPago == MetodoPago.TRANSFERENCIA).Sum(a => a.MontoCentavos);
        var totalBitcoin = ventas
            .SelectMany(v => v.Pagos)
            .Where(p => p.MetodoPago == MetodoPago.BITCOIN)
            .Sum(p => p.MontoCentavos) +
            accountPayments.Where(a => a.MetodoPago == MetodoPago.BITCOIN).Sum(a => a.MontoCentavos);
        var totalCobrosCorreccion = cobrosCorreccion.Sum(p => p.MontoCentavos);
        var totalReembolsos = reembolsos.Sum(r => r.MontoCentavos);
        var totalGastos = gastos.Sum(g => g.MontoCentavos);
        var totalRetiros = retiros.Sum(r => r.MontoCentavos);
        var costoVentas = ventas
            .SelectMany(v => v.Detalles)
            .Sum(d => d.Producto.PrecioCompraCentavos * d.Cantidad);
        var gananciaBruta = totalVentas - costoVentas;
        var capitalFinanciado = creditAccounts.Sum(c => c.CapitalFinanciadoCentavos);
        var interesGenerado = creditAccounts.Sum(c => c.InteresCalculadoCentavos);
        var pagosIniciales = creditAccounts
            .SelectMany(c => c.Venta.Pagos)
            .Sum(p => p.MontoCentavos);
        var totalAbonos = accountPayments.Sum(a => a.MontoCentavos);
        var capitalRecuperado = accountPayments.Sum(a => a.AplicadoCapitalCentavos);
        var interesCobrado = accountPayments.Sum(a => a.AplicadoInteresCentavos);
        var saldoCierre = CalculatePortfolioBalanceAt(portfolioAccounts, reportEndUtc);
        var vencidasCierre = CountPortfolioSituationAt(portfolioAccounts, reportEndUtc, morosoDays, moroso: false);
        var morosasCierre = CountPortfolioSituationAt(portfolioAccounts, reportEndUtc, morosoDays, moroso: true);
        var totalPurchased = purchases.Sum(c => c.TotalCentavos);
        var totalReceived = purchaseReceptions.SelectMany(r => r.Detalles).Sum(d => d.CostoUnitarioCentavos * d.CantidadRecibida);
        var totalSupplierPaid = initialSupplierPayments.Sum(p => p.MontoCentavos) + supplierPayments.Sum(a => a.MontoCentavos);
        var supplierCashPaid = initialSupplierPayments.Where(p => p.MetodoPago == MetodoPago.EFECTIVO).Sum(p => p.MontoCentavos) +
            supplierPayments.Where(a => a.MetodoPago == MetodoPago.EFECTIVO).Sum(a => a.MontoCentavos);
        var supplierTransferPaid = initialSupplierPayments.Where(p => p.MetodoPago == MetodoPago.TRANSFERENCIA).Sum(p => p.MontoCentavos) +
            supplierPayments.Where(a => a.MetodoPago == MetodoPago.TRANSFERENCIA).Sum(a => a.MontoCentavos);
        var supplierBitcoinPaid = initialSupplierPayments.Where(p => p.MetodoPago == MetodoPago.BITCOIN).Sum(p => p.MontoCentavos) +
            supplierPayments.Where(a => a.MetodoPago == MetodoPago.BITCOIN).Sum(a => a.MontoCentavos);
        var supplierBalance = payableAccounts.Sum(a => CalculatePayableBalanceAt(a, reportEndUtc));
        var supplierOverdue = payableAccounts.Count(a => CalculatePayableBalanceAt(a, reportEndUtc) > 0 && a.FechaVencimiento < reportEndUtc);
        var activeOutOfCashPayments = outOfCashPayments.Where(payment => !payment.IsCancelled).ToList();
        var cancelledEmployeePayments = outOfCashPayments.Where(payment => payment.IsCancelled).Sum(payment => payment.AmountCentavos);

        var row = 4;
        WriteMetric(ws, row++, "Total ventas", ToDollars(totalVentas), true);
        WriteMetric(ws, row++, "Total efectivo", ToDollars(totalEfectivo), true);
        WriteMetric(ws, row++, "Total transferencia", ToDollars(totalTransferencia), true);
        WriteMetric(ws, row++, "Total Bitcoin", ToDollars(totalBitcoin), true);
        WriteMetric(ws, row++, "Total gastos", ToDollars(totalGastos), true);
        WriteMetric(ws, row++, "Total retiros", ToDollars(totalRetiros), true);
        WriteMetric(ws, row++, "Cobros por correcciones", ToDollars(totalCobrosCorreccion), true);
        WriteMetric(ws, row++, "Reembolsos", ToDollars(totalReembolsos), true);
        WriteMetric(ws, row++, "Cantidad de ventas", ventas.Count, false);
        WriteMetric(ws, row++, "Ganancia bruta", ToDollars(gananciaBruta), true);
        row++;
        WriteMetric(ws, row++, "Ventas a credito registradas", creditAccounts.Count, false);
        WriteMetric(ws, row++, "Capital financiado", ToDollars(capitalFinanciado), true);
        WriteMetric(ws, row++, "Interes generado", ToDollars(interesGenerado), true);
        WriteMetric(ws, row++, "Pagos iniciales recibidos", ToDollars(pagosIniciales), true);
        WriteMetric(ws, row++, "Abonos recibidos", ToDollars(totalAbonos), true);
        WriteMetric(ws, row++, "Capital recuperado por abonos", ToDollars(capitalRecuperado), true);
        WriteMetric(ws, row++, "Interes cobrado", ToDollars(interesCobrado), true);
        WriteMetric(ws, row++, "Saldo pendiente al cierre", ToDollars(saldoCierre), true);
        WriteMetric(ws, row++, "Cuentas vencidas al cierre", vencidasCierre, false);
        WriteMetric(ws, row++, "Cuentas morosas al cierre", morosasCierre, false);
        row++;
        WriteMetric(ws, row++, "Total comprado a proveedores", ToDollars(totalPurchased), true);
        WriteMetric(ws, row++, "Total recibido de proveedores", ToDollars(totalReceived), true);
        WriteMetric(ws, row++, "Total pagado a proveedores", ToDollars(totalSupplierPaid), true);
        WriteMetric(ws, row++, "Pagado a proveedores en efectivo", ToDollars(supplierCashPaid), true);
        WriteMetric(ws, row++, "Pagado a proveedores por transferencia", ToDollars(supplierTransferPaid), true);
        WriteMetric(ws, row++, "Pagado a proveedores por Bitcoin", ToDollars(supplierBitcoinPaid), true);
        WriteMetric(ws, row++, "Saldo por pagar a proveedores", ToDollars(supplierBalance), true);
        WriteMetric(ws, row++, "Cuentas de proveedor vencidas", supplierOverdue, false);
        row++;
        WriteMetric(ws, row++, "Pagos fuera de caja activos", ToDollars(activeOutOfCashPayments.Sum(payment => payment.AmountCentavos)), true);
        WriteMetric(ws, row++, "Pagos externos de inventario/proveedores", ToDollars(activeOutOfCashPayments.Where(payment => payment.Area == "Inventario").Sum(payment => payment.AmountCentavos)), true);
        WriteMetric(ws, row++, "Gastos pagados fuera de caja", ToDollars(activeOutOfCashPayments.Where(payment => payment.Area == "Gasto").Sum(payment => payment.AmountCentavos)), true);
        WriteMetric(ws, row++, "Pagos a empleados fuera de caja", ToDollars(activeOutOfCashPayments.Where(payment => payment.Area == "Empleado").Sum(payment => payment.AmountCentavos)), true);
        WriteMetric(ws, row++, "Pagos de empleados anulados (informativo; excluidos del total)", ToDollars(cancelledEmployeePayments), true);
        var generatedRow = row;
        WriteMetric(ws, row++, "Fecha de generacion", generatedAt, false);

        row++;
        ws.Cell(row, 1).Value = "Nota";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 2).Value = "La ganancia bruta usa el precio de compra actual del producto; puede variar si el costo cambio despues de la venta.";
        ws.Cell(row, 2).Style.Alignment.WrapText = true;

        ws.Cell(generatedRow, 2).Style.NumberFormat.Format = "yyyy-mm-dd hh:mm";
        AutoFit(ws);
    }

    private static void WriteSalesSheet(XLWorkbook workbook, IReadOnlyList<Venta> ventas)
    {
        var ws = workbook.Worksheets.Add("Ventas del mes");
        WriteHeaders(
            ws,
            "Fecha",
            "Numero de venta",
            "Usuario",
            "Total",
            "Descuento",
            "Estado",
            "Metodo(s) de pago",
            "Efectivo",
            "Transferencia",
            "Bitcoin",
            "Referencias");

        var row = 2;
        foreach (var venta in ventas)
        {
            ws.Cell(row, 1).Value = ToLocal(venta.FechaVenta);
            ws.Cell(row, 2).Value = venta.NumeroVenta;
            ws.Cell(row, 3).Value = venta.Usuario.NombreUsuario;
            ws.Cell(row, 4).Value = ToDollars(venta.TotalCentavos);
            ws.Cell(row, 5).Value = ToDollars(venta.DescuentoCentavos);
            ws.Cell(row, 6).Value = venta.Estado.ToString();
            ws.Cell(row, 7).Value = GetPaymentMethods(venta);
            ws.Cell(row, 8).Value = ToDollars(GetNetCashCentavos(venta));
            ws.Cell(row, 9).Value = ToDollars(GetPaymentAmount(venta, MetodoPago.TRANSFERENCIA));
            ws.Cell(row, 10).Value = ToDollars(GetPaymentAmount(venta, MetodoPago.BITCOIN));
            ws.Cell(row, 11).Value = GetPaymentReferences(venta);
            row++;
        }

        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        ws.Columns(4, 10).Style.NumberFormat.Format = CurrencyFormat;
        AutoFit(ws);
    }

    private static void WriteSoldProductsSheet(XLWorkbook workbook, IReadOnlyList<Venta> ventas)
    {
        var ws = workbook.Worksheets.Add("Detalle productos");
        WriteHeaders(ws, "Fecha", "Numero de venta", "Producto", "Codigo", "Cantidad", "Precio unitario", "Descuento", "Subtotal");

        var row = 2;
        foreach (var venta in ventas)
        {
            foreach (var detalle in venta.Detalles.OrderBy(d => d.DetalleVentaId))
            {
                ws.Cell(row, 1).Value = ToLocal(venta.FechaVenta);
                ws.Cell(row, 2).Value = venta.NumeroVenta;
                ws.Cell(row, 3).Value = detalle.Producto.Nombre;
                ws.Cell(row, 4).Value = detalle.Producto.CodigoInterno;
                ws.Cell(row, 5).Value = detalle.Cantidad;
                ws.Cell(row, 6).Value = ToDollars(detalle.PrecioUnitarioCentavos);
                ws.Cell(row, 7).Value = ToDollars(detalle.DescuentoCentavos);
                ws.Cell(row, 8).Value = ToDollars(detalle.SubtotalCentavos);
                row++;
            }
        }

        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        ws.Columns(6, 8).Style.NumberFormat.Format = CurrencyFormat;
        AutoFit(ws);
    }

    private static void WriteCreditSalesSheet(XLWorkbook workbook, IReadOnlyList<CuentaPorCobrar> accounts)
    {
        var ws = workbook.Worksheets.Add("Ventas a credito");
        WriteHeaders(
            ws,
            "Fecha",
            "Venta",
            "Cuenta",
            "Cliente",
            "Capital",
            "Interes",
            "Total cuenta",
            "Pago inicial",
            "Saldo inicial",
            "Vencimiento",
            "Estado actual");

        var row = 2;
        foreach (var account in accounts)
        {
            var initialPayment = account.Venta.Pagos.Sum(p => p.MontoCentavos);
            ws.Cell(row, 1).Value = ToLocal(account.FechaCreacion);
            ws.Cell(row, 2).Value = account.Venta.NumeroVenta;
            ws.Cell(row, 3).Value = account.CodigoCuenta;
            ws.Cell(row, 4).Value = $"{account.Cliente.CodigoCliente} - {account.Cliente.NombreCompleto}";
            ws.Cell(row, 5).Value = ToDollars(account.CapitalFinanciadoCentavos);
            ws.Cell(row, 6).Value = ToDollars(account.InteresCalculadoCentavos);
            ws.Cell(row, 7).Value = ToDollars(account.TotalOriginalCentavos);
            ws.Cell(row, 8).Value = ToDollars(initialPayment);
            ws.Cell(row, 9).Value = ToDollars(account.TotalOriginalCentavos);
            ws.Cell(row, 10).Value = ToLocal(account.FechaVencimiento);
            ws.Cell(row, 11).Value = account.Estado.ToString();
            row++;
        }

        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        ws.Column(10).Style.DateFormat.Format = "yyyy-mm-dd";
        ws.Columns(5, 9).Style.NumberFormat.Format = CurrencyFormat;
        AutoFit(ws);
    }

    private static void WriteAccountPaymentsSheet(
        XLWorkbook workbook,
        IReadOnlyList<AbonoCuentaPorCobrar> payments)
    {
        var ws = workbook.Worksheets.Add("Abonos");
        WriteHeaders(
            ws,
            "Fecha",
            "Codigo de abono",
            "Cliente",
            "Cuenta",
            "Venta",
            "Metodo",
            "Monto",
            "Interes aplicado",
            "Capital aplicado",
            "Saldo anterior",
            "Saldo nuevo",
            "Usuario",
            "Referencia");

        var row = 2;
        foreach (var payment in payments)
        {
            ws.Cell(row, 1).Value = ToLocal(payment.FechaAbono);
            ws.Cell(row, 2).Value = payment.CodigoAbono;
            ws.Cell(row, 3).Value = $"{payment.CuentaPorCobrar.Cliente.CodigoCliente} - {payment.CuentaPorCobrar.Cliente.NombreCompleto}";
            ws.Cell(row, 4).Value = payment.CuentaPorCobrar.CodigoCuenta;
            ws.Cell(row, 5).Value = payment.CuentaPorCobrar.Venta.NumeroVenta;
            ws.Cell(row, 6).Value = payment.MetodoPago.ToString();
            ws.Cell(row, 7).Value = ToDollars(payment.MontoCentavos);
            ws.Cell(row, 8).Value = ToDollars(payment.AplicadoInteresCentavos);
            ws.Cell(row, 9).Value = ToDollars(payment.AplicadoCapitalCentavos);
            ws.Cell(row, 10).Value = ToDollars(payment.SaldoAnteriorCentavos);
            ws.Cell(row, 11).Value = ToDollars(payment.SaldoNuevoCentavos);
            ws.Cell(row, 12).Value = payment.Usuario.NombreUsuario;
            ws.Cell(row, 13).Value = payment.Referencia ?? "-";
            row++;
        }

        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        ws.Columns(7, 11).Style.NumberFormat.Format = CurrencyFormat;
        AutoFit(ws);
    }

    private static void WriteCurrentPortfolioSheet(
        XLWorkbook workbook,
        IReadOnlyList<CuentaPorCobrar> accounts,
        int morosoDays,
        DateTime reportEndUtc)
    {
        var ws = workbook.Worksheets.Add("Cartera actual");
        WriteHeaders(
            ws,
            "Cliente",
            "Cuenta",
            "Venta",
            "Capital original",
            "Interes original",
            "Pagado",
            "Saldo",
            "Vencimiento",
            "Situacion",
            "Dias de atraso",
            "Ultimo abono");

        var row = 2;
        foreach (var account in accounts.Where(a => CalculateBalanceAt(a, reportEndUtc) > 0))
        {
            var balanceAtEnd = CalculateBalanceAt(account, reportEndUtc);
            var lastPayment = account.Abonos
                .Where(a => a.FechaAbono < reportEndUtc)
                .OrderByDescending(a => a.FechaAbono)
                .Select(a => (DateTime?)a.FechaAbono)
                .FirstOrDefault();

            ws.Cell(row, 1).Value = $"{account.Cliente.CodigoCliente} - {account.Cliente.NombreCompleto}";
            ws.Cell(row, 2).Value = account.CodigoCuenta;
            ws.Cell(row, 3).Value = account.Venta.NumeroVenta;
            ws.Cell(row, 4).Value = ToDollars(account.CapitalFinanciadoCentavos);
            ws.Cell(row, 5).Value = ToDollars(account.InteresCalculadoCentavos);
            ws.Cell(row, 6).Value = ToDollars(account.TotalOriginalCentavos - balanceAtEnd);
            ws.Cell(row, 7).Value = ToDollars(balanceAtEnd);
            ws.Cell(row, 8).Value = ToLocal(account.FechaVencimiento);
            ws.Cell(row, 9).Value = GetPortfolioSituationAt(account, reportEndUtc, morosoDays);
            ws.Cell(row, 10).Value = GetDaysPastDueAt(account, reportEndUtc);
            ws.Cell(row, 11).Value = lastPayment.HasValue
                ? ToLocal(lastPayment.Value).ToString("yyyy-MM-dd")
                : "-";
            row++;
        }

        ws.Column(8).Style.DateFormat.Format = "yyyy-mm-dd";
        ws.Columns(4, 7).Style.NumberFormat.Format = CurrencyFormat;
        AutoFit(ws);
    }

    private static void WriteCashOutputsSheet(
        XLWorkbook workbook,
        IReadOnlyList<Gasto> gastos,
        IReadOnlyList<RetiroCaja> retiros,
        IReadOnlyList<Reembolso> reembolsos,
        IReadOnlyList<PagoCorreccionVenta> cobrosCorreccion)
    {
        var ws = workbook.Worksheets.Add("Gastos y retiros");
        WriteHeaders(ws, "Fecha", "Tipo", "Usuario", "Monto", "Motivo/Concepto", "Método", "Origen de fondos", "Responsable / fuente", "Efecto en caja");

        var outputs = gastos
            .Select(g => new CashOutputRow(
                g.FechaGasto, "Gasto", g.Usuario.NombreUsuario, g.MontoCentavos, g.Concepto,
                g.MetodoPago?.ToString() ?? "Método no especificado",
                FundingOriginLabel(g.OrigenFondos),
                g.ResponsableOrigen ?? "-",
                CashEffectLabel(g.CajaId, g.OrigenFondos)))
            .Concat(retiros.Select(r => new CashOutputRow(
                r.FechaRetiro, "Retiro", r.Usuario.NombreUsuario, r.MontoCentavos, r.Motivo,
                "EFECTIVO", "Caja física", "-", "Sí")))
            .Concat(reembolsos.Select(r => new CashOutputRow(
                r.FechaReembolso, $"Reembolso {r.MetodoReembolso}", r.Usuario.NombreUsuario, r.MontoCentavos, r.Motivo,
                r.MetodoReembolso.ToString(), "No especificado (histórico)", "-",
                r.CajaId.HasValue && r.MetodoReembolso == MetodoReembolso.EFECTIVO ? "Sí" : "No")))
            .Concat(cobrosCorreccion.Select(p => new CashOutputRow(
                p.FechaPago, $"Cobro correccion {p.MetodoPago}", p.Usuario.NombreUsuario, p.MontoCentavos, p.Motivo,
                p.MetodoPago.ToString(), "No especificado (histórico)", "-",
                p.MetodoPago == MetodoPago.EFECTIVO ? "Sí" : "No")))
            .OrderBy(o => o.Fecha)
            .ToList();

        var row = 2;
        foreach (var output in outputs)
        {
            ws.Cell(row, 1).Value = ToLocal(output.Fecha);
            ws.Cell(row, 2).Value = output.Tipo;
            ws.Cell(row, 3).Value = output.Usuario;
            ws.Cell(row, 4).Value = ToDollars(output.MontoCentavos);
            ws.Cell(row, 5).Value = output.Motivo;
            ws.Cell(row, 6).Value = output.Metodo;
            ws.Cell(row, 7).Value = output.OrigenFondos;
            ws.Cell(row, 8).Value = output.ResponsableOrigen;
            ws.Cell(row, 9).Value = output.EfectoCaja;
            row++;
        }

        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        ws.Column(4).Style.NumberFormat.Format = CurrencyFormat;
        AutoFit(ws);
    }

    private static IReadOnlyList<OutOfCashPaymentReportRow> BuildOutOfCashPaymentRows(
        IReadOnlyList<PagoCompra> initialSupplierPayments,
        IReadOnlyList<AbonoCuentaPorPagar> supplierPayments,
        IReadOnlyList<Gasto> expenses,
        IReadOnlyList<EmployeePayment> employeePayments)
    {
        var supplierRows = initialSupplierPayments
            .Where(payment => IsOutsideCash(payment.OrigenFondos))
            .Select(payment => new OutOfCashPaymentReportRow(
                payment.FechaPago,
                "Inventario",
                "Pago inicial a proveedor",
                payment.CodigoPago,
                payment.Compra.Proveedor.NombreRazonSocial,
                $"Compra {payment.Compra.CodigoCompra}",
                payment.MetodoPago,
                payment.OrigenFondos!.Value,
                payment.ResponsableOrigen,
                payment.MontoCentavos,
                false,
                payment.Referencia,
                payment.Usuario.NombreUsuario,
                payment.OperacionId,
                payment.CajaId,
                payment.Observaciones));

        var installmentRows = supplierPayments
            .Where(payment => IsOutsideCash(payment.OrigenFondos))
            .Select(payment => new OutOfCashPaymentReportRow(
                payment.FechaAbono,
                "Inventario",
                "Abono a proveedor",
                payment.CodigoAbono,
                payment.CuentaPorPagar.Proveedor.NombreRazonSocial,
                $"Compra {payment.CuentaPorPagar.Compra.CodigoCompra}; cuenta {payment.CuentaPorPagar.CodigoCuenta}",
                payment.MetodoPago,
                payment.OrigenFondos!.Value,
                payment.ResponsableOrigen,
                payment.MontoCentavos,
                false,
                payment.Referencia,
                payment.Usuario.NombreUsuario,
                payment.OperacionId,
                payment.CajaId,
                payment.Observaciones));

        var expenseRows = expenses
            .Where(expense => IsOutsideCash(expense.OrigenFondos))
            .Select(expense => new OutOfCashPaymentReportRow(
                expense.FechaGasto,
                "Gasto",
                "Gasto operativo",
                $"GAS-{expense.GastoId:D6}",
                "-",
                expense.Concepto,
                expense.MetodoPago,
                expense.OrigenFondos!.Value,
                expense.ResponsableOrigen,
                expense.MontoCentavos,
                false,
                expense.Referencia,
                expense.Usuario.NombreUsuario,
                expense.OperacionId,
                expense.CajaId,
                expense.Observaciones));

        var employeeRows = employeePayments
            .Where(payment => IsOutsideCash(payment.OrigenFondos))
            .Select(payment => new OutOfCashPaymentReportRow(
                payment.CreatedAtUtc,
                "Empleado",
                $"Pago a empleado ({EmployeePaymentTypeLabel(payment.PaymentType)})",
                $"EMP-{payment.EmployeePaymentId:D6}",
                $"{payment.User.NombreCompleto} ({payment.User.NombreUsuario})",
                BuildEmployeePaymentDetail(payment),
                payment.MetodoPago,
                payment.OrigenFondos!.Value,
                payment.ResponsableOrigen,
                payment.AmountCentavos,
                payment.Status == EmployeePaymentStatus.Cancelled,
                payment.Referencia,
                payment.RegisteredBy.NombreUsuario,
                payment.OperacionId,
                payment.CajaId,
                payment.Status == EmployeePaymentStatus.Cancelled
                    ? payment.CancellationReason
                    : payment.Note));

        return supplierRows
            .Concat(installmentRows)
            .Concat(expenseRows)
            .Concat(employeeRows)
            .OrderBy(payment => payment.Date)
            .ThenBy(payment => payment.Area, StringComparer.Ordinal)
            .ThenBy(payment => payment.Document, StringComparer.Ordinal)
            .ToList();
    }

    private static void WriteInventorySheet(XLWorkbook workbook, IReadOnlyList<Producto> productos)
    {
        var ws = workbook.Worksheets.Add("Inventario actual");
        WriteHeaders(ws, "Codigo", "Producto", "Categoria", "Stock actual", "Stock minimo", "Estado", "Precio venta");

        var row = 2;
        foreach (var producto in productos)
        {
            ws.Cell(row, 1).Value = producto.CodigoInterno;
            ws.Cell(row, 2).Value = producto.Nombre;
            ws.Cell(row, 3).Value = producto.Categoria.Nombre;
            ws.Cell(row, 4).Value = producto.Skus.SelectMany(sku => sku.SaldosInventario)
                .Sum(balance => balance.ExistenciaFisica);
            ws.Cell(row, 5).Value = producto.StockMinimo;
            ws.Cell(row, 6).Value = producto.Estado.ToString();
            ws.Cell(row, 7).Value = ToDollars(producto.PrecioVentaCentavos);
            row++;
        }

        ws.Column(7).Style.NumberFormat.Format = CurrencyFormat;
        AutoFit(ws);
    }

    private static void WritePurchasesSheet(XLWorkbook workbook, IReadOnlyList<Compra> purchases)
    {
        var ws = workbook.Worksheets.Add("Compras");
        WriteHeaders(ws, "Fecha", "Compra", "Proveedor", "Estado recepcion", "Estado financiero", "Productos", "Subtotal", "Descuento", "Total", "Pagado", "Saldo", "Usuario");
        var row = 2;
        foreach (var purchase in purchases)
        {
            ws.Cell(row, 1).Value = ToLocal(purchase.FechaCompra);
            ws.Cell(row, 2).Value = purchase.CodigoCompra;
            ws.Cell(row, 3).Value = $"{purchase.Proveedor.CodigoProveedor} - {purchase.Proveedor.NombreRazonSocial}";
            ws.Cell(row, 4).Value = purchase.EstadoRecepcion.ToString();
            ws.Cell(row, 5).Value = purchase.EstadoFinanciero.ToString();
            ws.Cell(row, 6).Value = purchase.Detalles.Count;
            ws.Cell(row, 7).Value = ToDollars(purchase.SubtotalCentavos);
            ws.Cell(row, 8).Value = ToDollars(purchase.DescuentoCentavos);
            ws.Cell(row, 9).Value = ToDollars(purchase.TotalCentavos);
            ws.Cell(row, 10).Value = ToDollars(purchase.TotalPagadoCentavos);
            ws.Cell(row, 11).Value = ToDollars(purchase.SaldoPendienteCentavos);
            ws.Cell(row, 12).Value = purchase.UsuarioRegistro.NombreUsuario;
            row++;
        }
        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd";
        ws.Columns(7, 11).Style.NumberFormat.Format = CurrencyFormat;
        AutoFit(ws);
    }

    private static void WritePurchaseReceptionsSheet(XLWorkbook workbook, IReadOnlyList<RecepcionCompra> receptions)
    {
        var ws = workbook.Worksheets.Add("Recepciones");
        WriteHeaders(ws, "Fecha", "Recepcion", "Compra", "Proveedor", "Producto", "Codigo", "Cantidad", "Costo unitario", "Total recibido", "Stock anterior", "Stock nuevo", "Usuario");
        var row = 2;
        foreach (var reception in receptions)
        foreach (var detail in reception.Detalles)
        {
            ws.Cell(row, 1).Value = ToLocal(reception.FechaRecepcion);
            ws.Cell(row, 2).Value = reception.CodigoRecepcion;
            ws.Cell(row, 3).Value = reception.Compra.CodigoCompra;
            ws.Cell(row, 4).Value = reception.Compra.Proveedor.NombreRazonSocial;
            ws.Cell(row, 5).Value = detail.Producto.Nombre;
            ws.Cell(row, 6).Value = detail.Producto.CodigoInterno;
            ws.Cell(row, 7).Value = detail.CantidadRecibida;
            ws.Cell(row, 8).Value = ToDollars(detail.CostoUnitarioCentavos);
            ws.Cell(row, 9).Value = ToDollars(detail.CostoUnitarioCentavos * detail.CantidadRecibida);
            ws.Cell(row, 10).Value = detail.StockAnterior;
            ws.Cell(row, 11).Value = detail.StockNuevo;
            ws.Cell(row, 12).Value = reception.UsuarioRecepcion.NombreUsuario;
            row++;
        }
        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        ws.Columns(8, 9).Style.NumberFormat.Format = CurrencyFormat;
        AutoFit(ws);
    }

    private static void WriteSupplierPaymentsSheet(XLWorkbook workbook, IReadOnlyList<PagoCompra> initialPayments, IReadOnlyList<AbonoCuentaPorPagar> payments)
    {
        var ws = workbook.Worksheets.Add("Pagos a proveedores");
        WriteHeaders(ws, "Fecha", "Tipo", "Codigo", "Proveedor", "Compra", "Cuenta", "Metodo", "Monto", "Referencia", "Saldo anterior", "Saldo nuevo", "Usuario", "Caja", "Origen de fondos", "Responsable / fuente", "Efecto en caja", "OperacionId");
        var rows = initialPayments.Select(p => new SupplierPaymentReportRow(p.FechaPago, "Pago inicial", p.CodigoPago,
                p.Compra.Proveedor.NombreRazonSocial, p.Compra.CodigoCompra, p.Compra.CuentaPorPagar?.CodigoCuenta ?? "-", p.MetodoPago,
                p.MontoCentavos, p.Referencia, null, null, p.Usuario.NombreUsuario, p.CajaId, p.OrigenFondos,
                p.ResponsableOrigen, p.OperacionId))
            .Concat(payments.Select(a => new SupplierPaymentReportRow(a.FechaAbono, "Abono", a.CodigoAbono,
                a.CuentaPorPagar.Proveedor.NombreRazonSocial, a.CuentaPorPagar.Compra.CodigoCompra,
                a.CuentaPorPagar.CodigoCuenta, a.MetodoPago, a.MontoCentavos, a.Referencia,
                a.SaldoAnteriorCentavos, a.SaldoNuevoCentavos, a.Usuario.NombreUsuario, a.CajaId, a.OrigenFondos,
                a.ResponsableOrigen, a.OperacionId)))
            .OrderBy(p => p.Date).ToList();
        var row = 2;
        foreach (var payment in rows)
        {
            ws.Cell(row, 1).Value = ToLocal(payment.Date); ws.Cell(row, 2).Value = payment.Type;
            ws.Cell(row, 3).Value = payment.Code; ws.Cell(row, 4).Value = payment.Supplier;
            ws.Cell(row, 5).Value = payment.Purchase; ws.Cell(row, 6).Value = payment.Account;
            ws.Cell(row, 7).Value = payment.Method.ToString(); ws.Cell(row, 8).Value = ToDollars(payment.Amount);
            ws.Cell(row, 9).Value = payment.Reference ?? "-";
            if (payment.PreviousBalance.HasValue) ws.Cell(row, 10).Value = ToDollars(payment.PreviousBalance.Value);
            if (payment.NewBalance.HasValue) ws.Cell(row, 11).Value = ToDollars(payment.NewBalance.Value);
            ws.Cell(row, 12).Value = payment.User; ws.Cell(row, 13).Value = payment.CashId?.ToString() ?? "-";
            ws.Cell(row, 14).Value = FundingOriginLabel(payment.FundingOrigin);
            ws.Cell(row, 15).Value = payment.ResponsibleOrigin ?? "-";
            ws.Cell(row, 16).Value = CashEffectLabel(payment.CashId, payment.FundingOrigin);
            ws.Cell(row, 17).Value = payment.OperationId?.ToString("D") ?? "-";
            row++;
        }
        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        ws.Columns(8, 11).Style.NumberFormat.Format = CurrencyFormat;
        AutoFit(ws);
    }

    private static void WriteEmployeePaymentsSheet(XLWorkbook workbook, IReadOnlyList<EmployeePayment> payments)
    {
        var ws = workbook.Worksheets.Add("Pagos a empleados");
        WriteHeaders(
            ws,
            "Fecha",
            "Id pago",
            "Empleado",
            "Usuario",
            "Tipo",
            "Monto",
            "Período desde",
            "Período hasta",
            "Días considerados",
            "Método",
            "Origen de fondos",
            "Responsable / fuente",
            "Efecto en caja",
            "Estado",
            "Registrado por",
            "Anulado por",
            "Fecha anulación",
            "Motivo anulación",
            "Referencia",
            "Nota",
            "OperacionId");

        var row = 2;
        foreach (var payment in payments)
        {
            ws.Cell(row, 1).Value = ToLocal(payment.CreatedAtUtc);
            ws.Cell(row, 2).Value = $"EMP-{payment.EmployeePaymentId:D6}";
            ws.Cell(row, 3).Value = payment.User.NombreCompleto;
            ws.Cell(row, 4).Value = payment.User.NombreUsuario;
            ws.Cell(row, 5).Value = EmployeePaymentTypeLabel(payment.PaymentType);
            ws.Cell(row, 6).Value = ToDollars(payment.AmountCentavos);
            if (payment.PeriodStart.HasValue) ws.Cell(row, 7).Value = payment.PeriodStart.Value.ToDateTime(TimeOnly.MinValue);
            if (payment.PeriodEnd.HasValue) ws.Cell(row, 8).Value = payment.PeriodEnd.Value.ToDateTime(TimeOnly.MinValue);
            if (payment.DaysConsidered.HasValue) ws.Cell(row, 9).Value = payment.DaysConsidered.Value;
            ws.Cell(row, 10).Value = payment.MetodoPago?.ToString() ?? "No especificado (histórico)";
            ws.Cell(row, 11).Value = FundingOriginLabel(payment.OrigenFondos);
            ws.Cell(row, 12).Value = payment.ResponsableOrigen ?? "-";
            ws.Cell(row, 13).Value = CashEffectLabel(payment.CajaId, payment.OrigenFondos);
            ws.Cell(row, 14).Value = EmployeePaymentStatusLabel(payment.Status);
            ws.Cell(row, 15).Value = payment.RegisteredBy.NombreUsuario;
            ws.Cell(row, 16).Value = payment.CancelledBy?.NombreUsuario ?? "-";
            if (payment.CancelledAtUtc.HasValue) ws.Cell(row, 17).Value = ToLocal(payment.CancelledAtUtc.Value);
            ws.Cell(row, 18).Value = payment.CancellationReason ?? "-";
            ws.Cell(row, 19).Value = payment.Referencia ?? "-";
            ws.Cell(row, 20).Value = payment.Note ?? "-";
            ws.Cell(row, 21).Value = payment.OperacionId?.ToString("D") ?? "-";
            row++;
        }

        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        ws.Columns(7, 8).Style.DateFormat.Format = "yyyy-mm-dd";
        ws.Column(6).Style.NumberFormat.Format = CurrencyFormat;
        ws.Column(17).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        AutoFit(ws);
    }

    private static void WriteOutOfCashPaymentsSheet(
        XLWorkbook workbook,
        IReadOnlyList<OutOfCashPaymentReportRow> payments,
        string monthName,
        int year)
    {
        var ws = workbook.Worksheets.Add("Pagos fuera de caja");
        ws.Cell(1, 1).Value = $"Pagos fuera de caja - {monthName} {year}";
        ws.Range(1, 1, 1, 16).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;

        var active = payments.Where(payment => !payment.IsCancelled).ToList();
        var cancelled = payments.Where(payment => payment.IsCancelled).ToList();
        WriteMetric(ws, 3, "Total externo activo", ToDollars(active.Sum(payment => payment.AmountCentavos)), true);
        WriteMetric(ws, 4, "Inventario / pagos a proveedores", ToDollars(active.Where(payment => payment.Area == "Inventario").Sum(payment => payment.AmountCentavos)), true);
        WriteMetric(ws, 5, "Gastos operativos", ToDollars(active.Where(payment => payment.Area == "Gasto").Sum(payment => payment.AmountCentavos)), true);
        WriteMetric(ws, 6, "Pagos a empleados", ToDollars(active.Where(payment => payment.Area == "Empleado").Sum(payment => payment.AmountCentavos)), true);
        WriteMetric(ws, 7, "Fondos externos del dueño", ToDollars(active.Where(payment => payment.FundingOrigin == OrigenFondos.FONDOS_EXTERNOS_DUENO).Sum(payment => payment.AmountCentavos)), true);
        WriteMetric(ws, 8, "Fondos del negocio fuera de caja", ToDollars(active.Where(payment => payment.FundingOrigin == OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA).Sum(payment => payment.AmountCentavos)), true);
        WriteMetric(ws, 9, "Pagos de empleados anulados (no sumados)", ToDollars(cancelled.Sum(payment => payment.AmountCentavos)), true);
        ws.Cell(10, 1).Value = "Nota";
        ws.Cell(10, 1).Style.Font.Bold = true;
        ws.Cell(10, 2).Value = "Solo incluye origen externo explícito. Históricos sin origen no se infieren. El pago directo del dueño a proveedor se muestra como uso en inventario, no como aporte de capital/deposito ni deuda del negocio.";
        ws.Range(10, 2, 10, 16).Merge();
        ws.Cell(10, 2).Style.Alignment.WrapText = true;

        WriteHeadersAt(
            ws,
            12,
            "Fecha",
            "Área",
            "Tipo",
            "Documento",
            "Beneficiario",
            "Detalle / uso",
            "Método",
            "Origen de fondos",
            "Responsable / fuente",
            "Monto",
            "Estado",
            "Efecto en caja",
            "Referencia",
            "Registrado por",
            "OperacionId",
            "Nota");

        var row = 13;
        foreach (var payment in payments)
        {
            ws.Cell(row, 1).Value = ToLocal(payment.Date);
            ws.Cell(row, 2).Value = payment.Area;
            ws.Cell(row, 3).Value = payment.Type;
            ws.Cell(row, 4).Value = payment.Document;
            ws.Cell(row, 5).Value = payment.Beneficiary;
            ws.Cell(row, 6).Value = payment.Detail;
            ws.Cell(row, 7).Value = payment.Method?.ToString() ?? "No especificado (histórico)";
            ws.Cell(row, 8).Value = FundingOriginLabel(payment.FundingOrigin);
            ws.Cell(row, 9).Value = payment.ResponsibleOrigin ?? "-";
            ws.Cell(row, 10).Value = ToDollars(payment.AmountCentavos);
            ws.Cell(row, 11).Value = payment.IsCancelled
                ? "Anulado (no sumado; sin reverso automático)"
                : "Registrado";
            ws.Cell(row, 12).Value = CashEffectLabel(payment.CashId, payment.FundingOrigin);
            ws.Cell(row, 13).Value = payment.Reference ?? "-";
            ws.Cell(row, 14).Value = payment.RegisteredBy;
            ws.Cell(row, 15).Value = payment.OperationId?.ToString("D") ?? "-";
            ws.Cell(row, 16).Value = payment.Note ?? "-";
            row++;
        }

        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        ws.Column(10).Style.NumberFormat.Format = CurrencyFormat;
        ws.Range(10, 2, 10, 16).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        AutoFit(ws);
    }

    private static void WriteAccountsPayableSheet(XLWorkbook workbook, IReadOnlyList<CuentaPorPagar> accounts, DateTime reportEndUtc)
    {
        var ws = workbook.Worksheets.Add("Cuentas por pagar");
        WriteHeaders(ws, "Cuenta", "Compra", "Proveedor", "Fecha", "Vencimiento", "Total", "Pagado al cierre", "Saldo al cierre", "Estado al cierre", "Usuario");
        var row = 2;
        foreach (var account in accounts)
        {
            var balance = CalculatePayableBalanceAt(account, reportEndUtc);
            var paid = account.TotalOriginalCentavos - balance;
            var state = SupplierFinancialRules.ResolveAccountState(paid, balance, account.FechaVencimiento, ToLocal(reportEndUtc));
            ws.Cell(row, 1).Value = account.CodigoCuenta; ws.Cell(row, 2).Value = account.Compra.CodigoCompra;
            ws.Cell(row, 3).Value = $"{account.Proveedor.CodigoProveedor} - {account.Proveedor.NombreRazonSocial}";
            ws.Cell(row, 4).Value = ToLocal(account.FechaCreacion); ws.Cell(row, 5).Value = ToLocal(account.FechaVencimiento);
            ws.Cell(row, 6).Value = ToDollars(account.TotalOriginalCentavos); ws.Cell(row, 7).Value = ToDollars(paid);
            ws.Cell(row, 8).Value = ToDollars(balance); ws.Cell(row, 9).Value = state.ToString(); ws.Cell(row, 10).Value = account.UsuarioCreador.NombreUsuario;
            row++;
        }
        ws.Columns(4, 5).Style.DateFormat.Format = "yyyy-mm-dd";
        ws.Columns(6, 8).Style.NumberFormat.Format = CurrencyFormat;
        AutoFit(ws);
    }

    private static void WriteCostHistorySheet(XLWorkbook workbook, IReadOnlyList<HistorialCostoProducto> history)
    {
        var ws = workbook.Worksheets.Add("Historial de costos");
        WriteHeaders(ws, "Fecha", "Producto", "Codigo", "Proveedor", "Compra", "Costo anterior", "Costo recibido", "Costo promedio nuevo", "Stock anterior", "Cantidad recibida", "Stock nuevo", "Usuario");
        var row = 2;
        foreach (var item in history)
        {
            ws.Cell(row, 1).Value = ToLocal(item.Fecha); ws.Cell(row, 2).Value = item.Producto.Nombre;
            ws.Cell(row, 3).Value = item.Producto.CodigoInterno; ws.Cell(row, 4).Value = item.Proveedor.NombreRazonSocial;
            ws.Cell(row, 5).Value = item.Compra.CodigoCompra; ws.Cell(row, 6).Value = ToDollars(item.CostoAnteriorCentavos);
            ws.Cell(row, 7).Value = ToDollars(item.CostoRecibidoCentavos); ws.Cell(row, 8).Value = ToDollars(item.CostoPromedioNuevoCentavos);
            ws.Cell(row, 9).Value = item.StockAnterior; ws.Cell(row, 10).Value = item.CantidadRecibida;
            ws.Cell(row, 11).Value = item.StockNuevo; ws.Cell(row, 12).Value = item.Usuario.NombreUsuario; row++;
        }
        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        ws.Columns(6, 8).Style.NumberFormat.Format = CurrencyFormat;
        AutoFit(ws);
    }

    private static long CalculatePayableBalanceAt(CuentaPorPagar account, DateTime reportEndUtc)
    {
        var initial = account.Compra.Pagos.Where(p => p.FechaPago < reportEndUtc).Sum(p => p.MontoCentavos);
        var installments = account.Abonos.Where(a => a.FechaAbono < reportEndUtc).Sum(a => a.MontoCentavos);
        return Math.Max(0, account.TotalOriginalCentavos - initial - installments);
    }

    private static void WriteMetric(IXLWorksheet ws, int row, string label, object value, bool isCurrency)
    {
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 2).Value = XLCellValue.FromObject(value);

        if (isCurrency)
        {
            ws.Cell(row, 2).Style.NumberFormat.Format = CurrencyFormat;
        }
    }

    private static void WriteHeaders(IXLWorksheet ws, params string[] headers)
        => WriteHeadersAt(ws, 1, headers);

    private static void WriteHeadersAt(IXLWorksheet ws, int headerRow, params string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(headerRow, i + 1).Value = headers[i];
        }

        var headerRange = ws.Range(headerRow, 1, headerRow, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(217, 225, 242);
        headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.SheetView.FreezeRows(headerRow);
    }

    private static void AutoFit(IXLWorksheet ws)
    {
        ws.Columns().AdjustToContents();

        foreach (var column in ws.ColumnsUsed())
        {
            if (column.Width > 60)
            {
                column.Width = 60;
            }
        }
    }

    private static decimal ToDollars(long centavos)
    {
        return centavos / 100m;
    }

    private static string FundingOriginLabel(OrigenFondos? origin) => origin switch
    {
        OrigenFondos.CAJA_FISICA => "Caja física",
        OrigenFondos.FONDOS_EXTERNOS_DUENO => "Fondos externos del dueño",
        OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA => "Fondos del negocio fuera de caja",
        _ => "No especificado (histórico)"
    };

    private static bool IsOutsideCash(OrigenFondos? origin) =>
        origin is OrigenFondos.FONDOS_EXTERNOS_DUENO or OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA;

    private static string CashEffectLabel(int? cashId, OrigenFondos? origin)
    {
        if (cashId.HasValue)
        {
            return "Sí";
        }

        return origin switch
        {
            OrigenFondos.FONDOS_EXTERNOS_DUENO or OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA => "No",
            OrigenFondos.CAJA_FISICA => "No verificado",
            _ => "Desconocido (histórico)"
        };
    }

    private static string EmployeePaymentTypeLabel(EmployeePaymentType type) => type switch
    {
        EmployeePaymentType.Daily => "Diario",
        EmployeePaymentType.Weekly => "Semanal",
        EmployeePaymentType.Biweekly => "Quincenal",
        EmployeePaymentType.Monthly => "Mensual",
        EmployeePaymentType.Extraordinary => "Extraordinario",
        _ => "No especificado"
    };

    private static string EmployeePaymentStatusLabel(EmployeePaymentStatus status) => status switch
    {
        EmployeePaymentStatus.Active => "Activo",
        EmployeePaymentStatus.Cancelled => "Anulado",
        _ => "No especificado"
    };

    private static string BuildEmployeePaymentDetail(EmployeePayment payment)
    {
        var parts = new List<string> { EmployeePaymentTypeLabel(payment.PaymentType) };
        if (payment.PeriodStart.HasValue || payment.PeriodEnd.HasValue)
        {
            var start = payment.PeriodStart?.ToString("yyyy-MM-dd") ?? "sin inicio";
            var end = payment.PeriodEnd?.ToString("yyyy-MM-dd") ?? "sin fin";
            parts.Add($"período {start} a {end}");
        }

        if (!string.IsNullOrWhiteSpace(payment.Note))
        {
            parts.Add(payment.Note.Trim());
        }

        return string.Join(" · ", parts);
    }

    private static long CalculatePortfolioBalanceAt(
        IReadOnlyList<CuentaPorCobrar> accounts,
        DateTime reportEndUtc)
    {
        return accounts.Sum(account => CalculateBalanceAt(account, reportEndUtc));
    }

    private static long CalculateBalanceAt(CuentaPorCobrar account, DateTime reportEndUtc)
    {
        if (account.Estado == EstadoCuentaPorCobrar.ANULADA &&
            account.FechaAnulacion.HasValue &&
            account.FechaAnulacion.Value < reportEndUtc)
        {
            return 0;
        }

        if (account.Estado == EstadoCuentaPorCobrar.PAGADA &&
            (!account.FechaPago.HasValue || account.FechaPago.Value < reportEndUtc))
        {
            return 0;
        }

        var paidAtEnd = account.Abonos
            .Where(a => a.FechaAbono < reportEndUtc)
            .Sum(a => a.MontoCentavos);

        return Math.Max(0, account.TotalOriginalCentavos - paidAtEnd);
    }

    private static int CountPortfolioSituationAt(
        IReadOnlyList<CuentaPorCobrar> accounts,
        DateTime reportEndUtc,
        int morosoDays,
        bool moroso)
    {
        return accounts.Count(account =>
        {
            var balance = CalculateBalanceAt(account, reportEndUtc);
            if (balance <= 0)
            {
                return false;
            }

            var days = GetDaysPastDueAt(account, reportEndUtc);
            return moroso
                ? days >= Math.Max(1, morosoDays)
                : days > 0 && days < Math.Max(1, morosoDays);
        });
    }

    private static string GetPortfolioSituationAt(
        CuentaPorCobrar account,
        DateTime reportEndUtc,
        int morosoDays)
    {
        var balance = CalculateBalanceAt(account, reportEndUtc);
        if (balance <= 0)
        {
            return "Pagada";
        }

        var days = GetDaysPastDueAt(account, reportEndUtc);
        if (days >= Math.Max(1, morosoDays))
        {
            return "Morosa";
        }

        return days > 0 ? "Vencida" : "Saldo vigente";
    }

    private static int GetDaysPastDueAt(CuentaPorCobrar account, DateTime reportEndUtc)
    {
        var due = ToLocal(account.FechaVencimiento).Date;
        var reference = ToLocal(reportEndUtc).Date;
        return Math.Max(0, (reference - due).Days);
    }

    private static DateTime ToLocal(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Local => value,
            DateTimeKind.Utc => value.ToLocalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime()
        };
    }

    private static long GetNetCashCentavos(Venta venta)
    {
        var efectivoRecibido = venta.Pagos
            .Where(p => p.MetodoPago == MetodoPago.EFECTIVO)
            .Sum(p => p.MontoCentavos);

        if (efectivoRecibido <= 0)
        {
            return 0;
        }

        var totalNoEfectivo = venta.Pagos
            .Where(p => p.MetodoPago != MetodoPago.EFECTIVO)
            .Sum(p => p.MontoCentavos);

        return Math.Max(0, venta.TotalCentavos - totalNoEfectivo);
    }

    private static string GetPaymentMethods(Venta venta)
    {
        return string.Join(", ", venta.Pagos
            .OrderBy(p => p.MetodoPago)
            .Select(p => p.MetodoPago.ToString())
            .Distinct());
    }

    private static long GetPaymentAmount(Venta venta, MetodoPago method)
    {
        return venta.Pagos
            .Where(p => p.MetodoPago == method)
            .Sum(p => p.MontoCentavos);
    }

    private static string GetPaymentReferences(Venta venta)
    {
        return string.Join(
            " | ",
            venta.Pagos
                .Where(p => !string.IsNullOrWhiteSpace(p.Referencia))
                .OrderBy(p => p.MetodoPago)
                .Select(p => $"{p.MetodoPago}: {p.Referencia}"));
    }

    private static (DateTime StartUtc, DateTime EndUtc) GetMonthUtcRange(int year, int month)
    {
        var startLocal = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Local);
        return (startLocal.ToUniversalTime(), startLocal.AddMonths(1).ToUniversalTime());
    }

    private static async Task<int> GetMorosoDaysAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var value = await context.ConfiguracionSistema
            .AsNoTracking()
            .Where(c => c.Clave == CreditConfigurationKeys.DaysPastDueForMoroso)
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(cancellationToken);

        return int.TryParse(value, out var days) && days > 0 ? days : 7;
    }

    private static string GetMonthName(int month)
    {
        var culture = CultureInfo.GetCultureInfo("es-ES");
        return culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetMonthName(month));
    }

    private static void ValidateRequest(MonthlyReportExportRequest request)
    {
        if (request.Month < 1 || request.Month > 12)
        {
            throw new ArgumentException("El mes debe estar entre 1 y 12.", nameof(request));
        }

        if (request.Year < 2000 || request.Year > 2100)
        {
            throw new ArgumentException("El ano debe estar entre 2000 y 2100.", nameof(request));
        }
    }

    private void EnsureAdminSession()
    {
        if (!_currentSession.EsAdmin || _currentSession.UsuarioActual is null)
        {
            throw new InvalidOperationException("Solo un usuario ADMIN puede exportar reportes.");
        }
    }

    private sealed record CashOutputRow(
        DateTime Fecha,
        string Tipo,
        string Usuario,
        long MontoCentavos,
        string Motivo,
        string Metodo,
        string OrigenFondos,
        string ResponsableOrigen,
        string EfectoCaja);

    private sealed record SupplierPaymentReportRow(
        DateTime Date,
        string Type,
        string Code,
        string Supplier,
        string Purchase,
        string Account,
        MetodoPago Method,
        long Amount,
        string? Reference,
        long? PreviousBalance,
        long? NewBalance,
        string User,
        int? CashId,
        OrigenFondos? FundingOrigin,
        string? ResponsibleOrigin,
        Guid? OperationId);

    private sealed record OutOfCashPaymentReportRow(
        DateTime Date,
        string Area,
        string Type,
        string Document,
        string Beneficiary,
        string Detail,
        MetodoPago? Method,
        OrigenFondos FundingOrigin,
        string? ResponsibleOrigin,
        long AmountCentavos,
        bool IsCancelled,
        string? Reference,
        string RegisteredBy,
        Guid? OperationId,
        int? CashId,
        string? Note);
}
