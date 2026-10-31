using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class CashMovementQueryService : ICashMovementQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IEmployeePermissionService _permissions;

    [ActivatorUtilitiesConstructor]
    public CashMovementQueryService(IDbContextFactory<AppDbContext> contextFactory, IEmployeePermissionService permissions)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
    }

    internal CashMovementQueryService(AppDbContext context, IEmployeePermissionService permissions)
    {
        _legacyContext = context;
        _permissions = permissions;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para consultar movimientos de caja.");

    public async Task<CashMovementSearchResult> SearchAsync(
        CashMovementFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureAccessAsync(cancellationToken);
        if (filter.Desde.Date > filter.Hasta.Date)
            throw new ArgumentException("La fecha desde no puede ser posterior a la fecha hasta.", nameof(filter));

        var startUtc = DateTime.SpecifyKind(filter.Desde.Date, DateTimeKind.Local).ToUniversalTime();
        var endUtc = DateTime.SpecifyKind(filter.Hasta.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
        var raw = await LoadMovementsAsync(startUtc, endUtc, cancellationToken);
        var cashSessions = await LoadCashSessionsAsync(
            DateOnly.FromDateTime(filter.Desde.Date),
            DateOnly.FromDateTime(filter.Hasta.Date),
            cancellationToken);
        var summary = BuildSummary(filter.Desde.Date, filter.Hasta.Date, raw, cashSessions);
        var ledger = AddRunningBalances(raw, cashSessions, startUtc, endUtc);
        IEnumerable<MovementData> filtered = ledger;

        if (filter.Tipo.HasValue)
            filtered = filtered.Where(x => x.Row.Tipo == filter.Tipo.Value);
        if (filter.Origen.HasValue)
            filtered = filtered.Where(x => x.Row.Origen == filter.Origen.Value);
        if (filter.Metodo.HasValue)
            filtered = filtered.Where(x => x.Row.Metodo == filter.Metodo.Value);
        if (filter.UsuarioId.HasValue)
            filtered = filtered.Where(x => x.Row.UsuarioId == filter.UsuarioId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            filtered = filtered.Where(x =>
                x.Row.Documento.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Row.Descripcion.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Row.Usuario.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (x.Row.Referencia?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var rows = filtered
            .OrderByDescending(x => x.Row.Fecha)
            .ThenByDescending(x => x.EventOrder)
            .ThenByDescending(x => x.Row.Documento)
            .Select(x => x.Row)
            .ToList();
        var users = ledger.Select(x => new UserFilterOption(x.Row.UsuarioId, x.Row.Usuario))
            .DistinctBy(x => x.UsuarioId)
            .OrderBy(x => x.NombreUsuario)
            .ToList();
        return new(summary, rows, users);
    }

    private async Task<List<MovementData>> LoadMovementsAsync(
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var rows = new List<MovementData>();

        var salePayments = await Context.PagosVenta.ExcludeVoidSalePayments(Context).AsNoTracking()
            .Include(p => p.Venta).ThenInclude(v => v.Usuario)
            .Where(p => p.FechaPago >= startUtc && p.FechaPago < endUtc && p.Origen == OrigenPagoVenta.VENTA)
            .ToListAsync(cancellationToken);
        rows.AddRange(salePayments.Select(p => Entrada(
            p.FechaPago, CashMovementOrigin.VENTA, p.Venta.NumeroVenta,
            "Pago de venta", p.MetodoPago, p.MontoCentavos,
            p.MetodoPago == MetodoPago.EFECTIVO && p.AfectaCaja,
            p.Venta.UsuarioId, p.Venta.Usuario.NombreUsuario, p.Referencia, p.Venta.CajaId,
            SummaryCategory.Sale)));

        var correctionPayments = await Context.PagosCorreccionVenta.AsNoTracking()
            .Include(p => p.Venta)
            .Include(p => p.Devolucion)
            .Include(p => p.Usuario)
            .Where(p => p.FechaPago >= startUtc && p.FechaPago < endUtc)
            .ToListAsync(cancellationToken);
        rows.AddRange(correctionPayments.Select(p => Entrada(
            p.FechaPago, CashMovementOrigin.VENTA, p.Devolucion.NumeroDevolucion,
            $"Cobro por correccion de {p.Venta.NumeroVenta}", p.MetodoPago, p.MontoCentavos,
            p.MetodoPago == MetodoPago.EFECTIVO, p.UsuarioId, p.Usuario.NombreUsuario,
            p.Referencia, p.CajaId, SummaryCategory.Sale)));

        var customerPayments = await Context.AbonosCuentaPorCobrar.AsNoTracking()
            .Include(a => a.CuentaPorCobrar)
            .Include(a => a.Usuario)
            .Where(a => a.FechaAbono >= startUtc && a.FechaAbono < endUtc)
            .ToListAsync(cancellationToken);
        rows.AddRange(customerPayments.Select(a => Entrada(
            a.FechaAbono, CashMovementOrigin.ABONO_CLIENTE, a.CodigoAbono,
            $"Abono de cliente a {a.CuentaPorCobrar.CodigoCuenta}", a.MetodoPago, a.MontoCentavos,
            a.MetodoPago == MetodoPago.EFECTIVO, a.UsuarioId, a.Usuario.NombreUsuario,
            a.Referencia, a.CajaId, SummaryCategory.CustomerPayment)));

        var orderPayments = await Context.PagosPedidoPersonalizado.AsNoTracking()
            .Include(p => p.PedidoPersonalizado)
            .Include(p => p.Usuario)
            .Where(p => p.Fecha >= startUtc && p.Fecha < endUtc)
            .ToListAsync(cancellationToken);
        rows.AddRange(orderPayments.Select(p => Entrada(
            p.Fecha, CashMovementOrigin.PEDIDO, p.CodigoPago,
            $"{Friendly(p.TipoPago)} de {p.PedidoPersonalizado.CodigoPedido}", p.MetodoPago,
            p.MontoCentavos, p.MetodoPago == MetodoPago.EFECTIVO && p.AfectaCaja,
            p.UsuarioId, p.Usuario.NombreUsuario, p.Referencia, p.CajaId,
            p.TipoPago switch
            {
                TipoPagoPedido.ANTICIPO => SummaryCategory.OrderDeposit,
                TipoPagoPedido.ABONO => SummaryCategory.OrderInstallment,
                _ => SummaryCategory.OrderFinalPayment
            })));

        var saleRefunds = await Context.Reembolsos.AsNoTracking()
            .Include(r => r.Devolucion).ThenInclude(d => d.Venta)
            .Include(r => r.Usuario)
            .Where(r => r.FechaReembolso >= startUtc && r.FechaReembolso < endUtc)
            .ToListAsync(cancellationToken);
        rows.AddRange(saleRefunds.Select(r => Salida(
            r.FechaReembolso, CashMovementOrigin.DEVOLUCION, r.Devolucion.NumeroDevolucion,
            $"Reembolso de {r.Devolucion.Venta.NumeroVenta}: {r.Motivo}", ToPaymentMethod(r.MetodoReembolso),
            r.MontoCentavos, r.MetodoReembolso == MetodoReembolso.EFECTIVO,
            r.UsuarioId, r.Usuario.NombreUsuario, r.Referencia, r.CajaId, SummaryCategory.SaleRefund)));

        var orderRefunds = await Context.ReembolsosPedidoPersonalizado.AsNoTracking()
            .Include(r => r.PedidoPersonalizado)
            .Include(r => r.Usuario)
            .Where(r => r.Fecha >= startUtc && r.Fecha < endUtc)
            .ToListAsync(cancellationToken);
        rows.AddRange(orderRefunds.Select(r => Salida(
            r.Fecha, CashMovementOrigin.REEMBOLSO_PEDIDO, r.CodigoReembolso,
            $"Reembolso de {r.PedidoPersonalizado.CodigoPedido}: {r.Motivo}", r.MetodoPago,
            r.MontoCentavos, r.MetodoPago == MetodoPago.EFECTIVO && r.AfectaCaja,
            r.UsuarioId, r.Usuario.NombreUsuario, r.Referencia, r.CajaId, SummaryCategory.OrderRefund)));

        var purchasePayments = await Context.PagosCompra.AsNoTracking()
            .Include(p => p.Compra)
            .Include(p => p.Usuario)
            .Where(p => p.FechaPago >= startUtc && p.FechaPago < endUtc)
            .ToListAsync(cancellationToken);
        rows.AddRange(purchasePayments.Select(p => Salida(
            p.FechaPago, CashMovementOrigin.COMPRA, p.CodigoPago,
            $"Pago inicial de {p.Compra.CodigoCompra}", p.MetodoPago, p.MontoCentavos,
            p.MetodoPago == MetodoPago.EFECTIVO, p.UsuarioId, p.Usuario.NombreUsuario,
            p.Referencia, p.CajaId, SummaryCategory.PurchasePayment)));

        var supplierPayments = await Context.AbonosCuentaPorPagar.AsNoTracking()
            .Include(a => a.CuentaPorPagar)
            .Include(a => a.Usuario)
            .Where(a => a.FechaAbono >= startUtc && a.FechaAbono < endUtc)
            .ToListAsync(cancellationToken);
        rows.AddRange(supplierPayments.Select(a => Salida(
            a.FechaAbono, CashMovementOrigin.PAGO_PROVEEDOR, a.CodigoAbono,
            $"Abono a proveedor en {a.CuentaPorPagar.CodigoCuenta}", a.MetodoPago, a.MontoCentavos,
            a.MetodoPago == MetodoPago.EFECTIVO, a.UsuarioId, a.Usuario.NombreUsuario,
            a.Referencia, a.CajaId, SummaryCategory.SupplierInstallment)));

        var expenses = await Context.Gastos.AsNoTracking().Include(g => g.Usuario)
            .Where(g => g.FechaGasto >= startUtc && g.FechaGasto < endUtc)
            .ToListAsync(cancellationToken);
        rows.AddRange(expenses.Select(g => Salida(
            g.FechaGasto, CashMovementOrigin.GASTO, $"GAS-{g.GastoId:000000}",
            $"{g.Concepto} | Origen: {FundingOriginLabel(g.OrigenFondos)} | " +
            $"Responsable/fuente: {g.ResponsableOrigen ?? "-"} | Efecto en caja: {(g.CajaId.HasValue && (g.MetodoPago is null || g.MetodoPago == MetodoPago.EFECTIVO) ? "sí" : "no")}",
            g.MetodoPago, g.MontoCentavos,
            g.CajaId.HasValue && (g.MetodoPago is null || g.MetodoPago == MetodoPago.EFECTIVO),
            g.UsuarioId, g.Usuario.NombreUsuario, g.Referencia, g.CajaId, SummaryCategory.Expense)));

        var employeePayments = await Context.EmployeePayments.AsNoTracking()
            .Include(payment => payment.User)
            .Include(payment => payment.RegisteredBy)
            .Where(payment => payment.CreatedAtUtc >= startUtc && payment.CreatedAtUtc < endUtc)
            .ToListAsync(cancellationToken);
        rows.AddRange(employeePayments.Select(payment => Salida(
            payment.CreatedAtUtc,
            CashMovementOrigin.PAGO_EMPLEADO,
            $"EMP-{payment.EmployeePaymentId:000000}",
            $"Pago a {payment.User.NombreUsuario} | Método: {payment.MetodoPago?.ToString() ?? "No especificado (histórico)"} | " +
            $"Origen: {FundingOriginLabel(payment.OrigenFondos)} | Responsable/fuente: {payment.ResponsableOrigen ?? "-"} | " +
            $"Efecto en caja: {(payment.CajaId.HasValue ? "sí" : payment.OrigenFondos is null ? "no especificado (histórico)" : "no")}" +
            $"{(payment.Status == EmployeePaymentStatus.Cancelled ? " | Pago anulado administrativamente; la salida original se conserva" : string.Empty)}",
            payment.MetodoPago,
            payment.AmountCentavos,
            payment.CajaId.HasValue,
            payment.RegisteredByUserId,
            payment.RegisteredBy.NombreUsuario,
            payment.Referencia,
            payment.CajaId,
            SummaryCategory.EmployeePayment)));

        var withdrawals = await Context.RetirosCaja.AsNoTracking().Include(r => r.Usuario)
            .Where(r => r.FechaRetiro >= startUtc && r.FechaRetiro < endUtc)
            .ToListAsync(cancellationToken);
        rows.AddRange(withdrawals.Select(r => Salida(
            r.FechaRetiro, CashMovementOrigin.RETIRO, $"RET-{r.RetiroCajaId:000000}",
            r.Motivo, MetodoPago.EFECTIVO, r.MontoCentavos, true,
            r.UsuarioId, r.Usuario.NombreUsuario, null, r.CajaId, SummaryCategory.Withdrawal)));

        var contributions = await Context.AportesCaja.AsNoTracking().Include(item => item.Usuario)
            .Where(item => item.FechaAporte >= startUtc && item.FechaAporte < endUtc)
            .ToListAsync(cancellationToken);
        rows.AddRange(contributions.Select(item => Entrada(
            item.FechaAporte, CashMovementOrigin.APORTE_CAJA, $"APO-{item.AporteCajaId:000000}",
            item.Motivo, MetodoPago.EFECTIVO, item.MontoCentavos, true,
            item.UsuarioId, item.Usuario.NombreUsuario, null, item.CajaId, SummaryCategory.CashContribution)));

        return rows;
    }

    private async Task<List<Caja>> LoadCashSessionsAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken) => await Context.Cajas.AsNoTracking()
            .Include(c => c.UsuarioApertura)
            .Include(c => c.UsuarioCierre)
            .Where(c => c.FechaOperacion >= fromDate && c.FechaOperacion <= toDate)
            .OrderBy(c => c.FechaOperacion)
            .ThenBy(c => c.FechaApertura)
            .ThenBy(c => c.CajaId)
            .ToListAsync(cancellationToken);

    private static CashAdvancedSummary BuildSummary(
        DateTime from,
        DateTime to,
        IReadOnlyList<MovementData> rows,
        IReadOnlyList<Caja> boxes)
    {
        long Sum(SummaryCategory category) => rows
            .Where(x => x.Category == category && x.Row.AfectaEfectivo)
            .Sum(x => x.Row.Tipo == CashMovementType.ENTRADA ? x.Row.EntradaCentavos : x.Row.SalidaCentavos);
        long Electronic(MetodoPago method, CashMovementType type) => rows
            .Where(x => x.Row.Metodo == method && x.Row.Tipo == type)
            .Sum(x => type == CashMovementType.ENTRADA ? x.Row.EntradaCentavos : x.Row.SalidaCentavos);

        var opening = boxes.Count == 0 ? 0 : boxes[0].MontoInicialCentavos;
        var openingAdjustments = 0L;
        for (var index = 1; index < boxes.Count; index++)
        {
            var previous = boxes[index - 1];
            if (previous.Estado != EstadoCaja.CERRADA)
            {
                continue;
            }

            openingAdjustments = checked(
                openingAdjustments + boxes[index].MontoInicialCentavos - previous.MontoCierreCentavos);
        }

        var expected = checked(opening + openingAdjustments + rows.Where(x => x.Row.AfectaEfectivo)
            .Sum(x => x.Row.EntradaCentavos - x.Row.SalidaCentavos));
        var allClosed = boxes.Count > 0 && boxes.All(c => c.Estado == EstadoCaja.CERRADA);
        long? counted = allClosed ? boxes[^1].MontoCierreCentavos : null;

        return new(
            from, to, opening,
            Sum(SummaryCategory.Sale),
            Sum(SummaryCategory.CustomerPayment),
            Sum(SummaryCategory.OrderDeposit),
            Sum(SummaryCategory.OrderInstallment),
            Sum(SummaryCategory.OrderFinalPayment),
            Sum(SummaryCategory.SaleRefund),
            Sum(SummaryCategory.OrderRefund),
            Sum(SummaryCategory.PurchasePayment),
            Sum(SummaryCategory.SupplierInstallment),
            Sum(SummaryCategory.Expense),
            Sum(SummaryCategory.Withdrawal),
            Electronic(MetodoPago.TRANSFERENCIA, CashMovementType.ENTRADA),
            Electronic(MetodoPago.BITCOIN, CashMovementType.ENTRADA),
            Electronic(MetodoPago.TRANSFERENCIA, CashMovementType.SALIDA),
            Electronic(MetodoPago.BITCOIN, CashMovementType.SALIDA),
            expected, counted, counted.HasValue ? counted.Value - expected : null,
            Sum(SummaryCategory.EmployeePayment),
            Sum(SummaryCategory.CashContribution));
    }

    private static List<MovementData> AddRunningBalances(
        IReadOnlyList<MovementData> movements,
        IReadOnlyList<Caja> sessions,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc)
    {
        var result = movements.ToList();
        if (sessions.Count == 0)
        {
            return result;
        }

        long balance = sessions[0].MontoInicialCentavos;
        var previous = sessions[0];
        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            if (index > 0 && previous.Estado == EstadoCaja.CERRADA)
            {
                var openingDelta = checked(session.MontoInicialCentavos - previous.MontoCierreCentavos);
                if (openingDelta != 0)
                {
                    balance = checked(balance + openingDelta);
                    result.Add(new(CreateAdjustment(
                        session.FechaApertura,
                        session,
                        openingDelta,
                        CashMovementOrigin.AJUSTE_APERTURA_CAJA,
                        $"Variación del conteo al abrir caja #{session.CajaId} respecto al cierre de caja #{previous.CajaId}. " +
                        "No representa venta ni gasto; la razón queda en la auditoría de apertura.",
                        balance),
                        SummaryCategory.Reconciliation,
                        EventOrder: -1));
                }
            }

            var sessionMovements = movements
                .Where(m => m.Row.CajaId == session.CajaId)
                .OrderBy(m => m.Row.Fecha)
                .ThenBy(m => m.Row.Documento, StringComparer.Ordinal)
                .ToList();
            long sessionNet = 0;
            foreach (var movement in sessionMovements)
            {
                if (movement.Row.AfectaEfectivo)
                {
                    var delta = checked(movement.Row.EntradaCentavos - movement.Row.SalidaCentavos);
                    balance = checked(balance + delta);
                    sessionNet = checked(sessionNet + delta);
                }

                var indexInResult = result.IndexOf(movement);
                if (indexInResult >= 0)
                {
                    result[indexInResult] = movement with
                    {
                        Row = movement.Row with { EfectivoDespuesCentavos = balance }
                    };
                }
            }

            if (session.Estado == EstadoCaja.CERRADA &&
                session.FechaCierre is { } closedAt &&
                closedAt >= rangeStartUtc && closedAt < rangeEndUtc)
            {
                var sessionExpected = checked(session.MontoInicialCentavos + sessionNet);
                var closingDelta = checked(session.MontoCierreCentavos - sessionExpected);
                if (closingDelta != 0)
                {
                    balance = checked(balance + closingDelta);
                    result.Add(new(CreateAdjustment(
                        session.FechaCierre.Value,
                        session,
                        closingDelta,
                        CashMovementOrigin.AJUSTE_CIERRE_CAJA,
                        $"Diferencia entre el efectivo esperado y el conteo físico al cerrar caja #{session.CajaId}. " +
                        "Es una regularización visible, no altera ventas ni inventario.",
                        balance),
                        SummaryCategory.Reconciliation,
                        EventOrder: 1));
                }
                else
                {
                    balance = session.MontoCierreCentavos;
                }
            }

            previous = session;
        }

        return result;
    }

    private static CashMovementRow CreateAdjustment(
        DateTime date,
        Caja session,
        long delta,
        CashMovementOrigin origin,
        string description,
        long balanceAfter) =>
        new(
            date,
            delta >= 0 ? CashMovementType.ENTRADA : CashMovementType.SALIDA,
            origin,
            $"AJU-CAJA-{session.CajaId:000000}",
            description,
            MetodoPago.EFECTIVO,
            delta > 0 ? delta : 0,
            delta < 0 ? checked(-delta) : 0,
            true,
            origin == CashMovementOrigin.AJUSTE_APERTURA_CAJA
                ? session.UsuarioAperturaId
                : session.UsuarioCierreId ?? session.UsuarioAperturaId,
            origin == CashMovementOrigin.AJUSTE_APERTURA_CAJA
                ? session.UsuarioApertura.NombreUsuario
                : session.UsuarioCierre?.NombreUsuario ?? session.UsuarioApertura.NombreUsuario,
            session.Observaciones,
            session.CajaId,
            balanceAfter);

    private async Task EnsureAccessAsync(CancellationToken cancellationToken)
    {
        var allowed = await _permissions.HasAnyPermissionAsync(
            [PermisoEmpleado.CAJA_MOVIMIENTOS_DETALLE_VER, PermisoEmpleado.REPORTES_COMERCIALES_VER],
            cancellationToken: cancellationToken);
        if (!allowed)
            throw new InvalidOperationException("No tiene permiso para consultar el detalle de caja.");
    }

    private static MovementData Entrada(
        DateTime date, CashMovementOrigin origin, string document, string description,
        MetodoPago? method, long amount, bool physical, int userId, string user,
        string? reference, int? cashId, SummaryCategory category) =>
        new(new(date, CashMovementType.ENTRADA, origin, document, description, method,
            amount, 0, physical, userId, user, reference, cashId), category);

    private static MovementData Salida(
        DateTime date, CashMovementOrigin origin, string document, string description,
        MetodoPago? method, long amount, bool physical, int userId, string user,
        string? reference, int? cashId, SummaryCategory category) =>
        new(new(date, CashMovementType.SALIDA, origin, document, description, method,
            0, amount, physical, userId, user, reference, cashId), category);

    private static MetodoPago ToPaymentMethod(MetodoReembolso method) => method switch
    {
        MetodoReembolso.EFECTIVO => MetodoPago.EFECTIVO,
        MetodoReembolso.TRANSFERENCIA => MetodoPago.TRANSFERENCIA,
        MetodoReembolso.BITCOIN => MetodoPago.BITCOIN,
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    private static string FundingOriginLabel(OrigenFondos? origin) => origin switch
    {
        OrigenFondos.CAJA_FISICA => "Caja física",
        OrigenFondos.FONDOS_EXTERNOS_DUENO => "Fondos externos del dueño",
        OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA => "Fondos del negocio fuera de caja",
        _ => "No especificado (histórico)"
    };

    private static string Friendly(TipoPagoPedido type) => type switch
    {
        TipoPagoPedido.ANTICIPO => "Anticipo",
        TipoPagoPedido.ABONO => "Abono",
        TipoPagoPedido.PAGO_FINAL => "Pago final",
        _ => type.ToString()
    };

    private sealed record MovementData(CashMovementRow Row, SummaryCategory Category, int EventOrder = 0);

    private enum SummaryCategory
    {
        Sale,
        CustomerPayment,
        OrderDeposit,
        OrderInstallment,
        OrderFinalPayment,
        SaleRefund,
        OrderRefund,
        PurchasePayment,
        SupplierInstallment,
        Expense,
        Withdrawal,
        EmployeePayment,
        CashContribution,
        Reconciliation
    }
}
