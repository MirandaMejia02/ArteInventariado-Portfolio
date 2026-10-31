using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class AccountsPayableQueryService : IAccountsPayableQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IEmployeePermissionService _permissionService;

    [ActivatorUtilitiesConstructor]
    public AccountsPayableQueryService(IDbContextFactory<AppDbContext> contextFactory, IEmployeePermissionService permissionService)
    {
        _contextFactory = contextFactory;
        _permissionService = permissionService;
    }

    internal AccountsPayableQueryService(AppDbContext context, IEmployeePermissionService permissionService)
    {
        _legacyContext = context;
        _permissionService = permissionService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para consultar cuentas por pagar.");

    public async Task<IReadOnlyList<AccountPayableSummary>> SearchAsync(AccountsPayableFilter filter, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CUENTAS_PAGAR_ACCEDER, cancellationToken);
        var rows = await ApplyBaseFilter(BaseQuery(), filter)
            .OrderByDescending(c => c.FechaCreacion)
            .ThenByDescending(c => c.CuentaPorPagarId)
            .ToListAsync(cancellationToken);

        var effective = rows.Select(ToSummary).Where(r => !filter.Estado.HasValue || r.Estado == filter.Estado.Value);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 250);
        return effective.Skip((page - 1) * pageSize).Take(pageSize).ToList();
    }

    public async Task<AccountsPayableStats> GetStatsAsync(AccountsPayableFilter filter, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CUENTAS_PAGAR_ACCEDER, cancellationToken);
        var rows = (await ApplyBaseFilter(BaseQuery(), filter).ToListAsync(cancellationToken)).Select(ToSummary).ToList();
        if (filter.Estado.HasValue) rows = rows.Where(r => r.Estado == filter.Estado.Value).ToList();
        var today = DateTime.Today;
        var upcoming = today.AddDays(7);
        return new AccountsPayableStats(
            rows.Sum(r => r.SaldoPendienteCentavos),
            rows.Count(r => r.Estado == EstadoCuentaPorPagar.ABIERTA),
            rows.Count(r => r.Estado == EstadoCuentaPorPagar.PARCIAL),
            rows.Count(r => r.Estado == EstadoCuentaPorPagar.VENCIDA),
            rows.Count(r => r.SaldoPendienteCentavos > 0 && r.FechaVencimiento.Date == today),
            rows.Count(r => r.SaldoPendienteCentavos > 0 && r.FechaVencimiento.Date > today && r.FechaVencimiento.Date <= upcoming));
    }

    public async Task<AccountPayableDetail?> GetDetailAsync(int cuentaPorPagarId, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CUENTAS_PAGAR_ACCEDER, cancellationToken);
        var account = await DetailQuery().FirstOrDefaultAsync(c => c.CuentaPorPagarId == cuentaPorPagarId, cancellationToken);
        return account is null ? null : ToDetail(account);
    }

    public async Task<AccountPayableDetail?> GetByPurchaseAsync(int compraId, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CUENTAS_PAGAR_ACCEDER, cancellationToken);
        var account = await DetailQuery().FirstOrDefaultAsync(c => c.CompraId == compraId, cancellationToken);
        return account is null ? null : ToDetail(account);
    }

    public async Task<IReadOnlyList<SupplierPaymentHistoryEntry>> GetPaymentHistoryAsync(SupplierPaymentHistoryFilter filter, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PAGOS_PROVEEDOR_HISTORIAL_VER, cancellationToken);
        var query = DetailQuery();
        if (filter.ProveedorId.HasValue) query = query.Where(a => a.ProveedorId == filter.ProveedorId.Value);
        if (filter.CuentaPorPagarId.HasValue) query = query.Where(a => a.CuentaPorPagarId == filter.CuentaPorPagarId.Value);
        if (filter.CompraId.HasValue) query = query.Where(a => a.CompraId == filter.CompraId.Value);

        var accounts = await query.ToListAsync(cancellationToken);
        var entries = accounts.SelectMany(BuildHistory).AsEnumerable();

        if (filter.Desde.HasValue) entries = entries.Where(entry => entry.Fecha >= filter.Desde.Value.Date);
        if (filter.Hasta.HasValue) entries = entries.Where(entry => entry.Fecha < filter.Hasta.Value.Date.AddDays(1));
        if (filter.MetodoPago.HasValue) entries = entries.Where(entry => entry.MetodoPago == filter.MetodoPago.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            entries = entries.Where(entry =>
                entry.Codigo.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.CodigoCuenta.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.CodigoCompra.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.Proveedor.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.Referencia?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
        }

        return entries
            .GroupBy(entry => (entry.EsPagoInicial, entry.MovimientoId))
            .Select(group => group.First())
            .OrderByDescending(entry => entry.Fecha)
            .ThenByDescending(entry => entry.MovimientoId)
            .ToList();
    }

    public async Task<SupplierPaymentSummary?> GetPaymentAsync(int abonoCuentaPorPagarId, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PAGOS_PROVEEDOR_HISTORIAL_VER, cancellationToken);
        var row = await PaymentQuery().FirstOrDefaultAsync(a => a.AbonoCuentaPorPagarId == abonoCuentaPorPagarId, cancellationToken);
        return row is null ? null : ToPaymentSummary(row);
    }

    public async Task<SupplierFinancialSummary> GetSupplierSummaryAsync(int proveedorId, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.FINANZAS_PROVEEDOR_VER, cancellationToken);
        var accounts = await BaseQuery().Where(c => c.ProveedorId == proveedorId)
            .OrderByDescending(c => c.FechaCreacion).Take(100).ToListAsync(cancellationToken);
        var summaries = accounts.Select(ToSummary).ToList();
        return new SupplierFinancialSummary(
            proveedorId,
            summaries.Sum(c => c.SaldoPendienteCentavos),
            summaries.Count(c => c.Estado is EstadoCuentaPorPagar.ABIERTA or EstadoCuentaPorPagar.PARCIAL or EstadoCuentaPorPagar.VENCIDA),
            summaries.Count(c => c.Estado == EstadoCuentaPorPagar.VENCIDA),
            summaries.Take(20).ToList());
    }

    private IQueryable<CuentaPorPagar> BaseQuery() => Context.CuentasPorPagar.AsNoTracking()
        .Include(c => c.Compra).Include(c => c.Proveedor).Include(c => c.UsuarioCreador);

    private IQueryable<CuentaPorPagar> DetailQuery() => BaseQuery()
        .Include(c => c.Compra).ThenInclude(c => c.Pagos).ThenInclude(p => p.Usuario)
        .Include(c => c.Abonos).ThenInclude(a => a.Usuario);

    private IQueryable<AbonoCuentaPorPagar> PaymentQuery() => Context.AbonosCuentaPorPagar.AsNoTracking()
        .Include(a => a.Usuario).Include(a => a.CuentaPorPagar).ThenInclude(c => c.Compra)
        .Include(a => a.CuentaPorPagar).ThenInclude(c => c.Proveedor);

    private static IQueryable<CuentaPorPagar> ApplyBaseFilter(IQueryable<CuentaPorPagar> query, AccountsPayableFilter filter)
    {
        if (filter.ProveedorId.HasValue) query = query.Where(c => c.ProveedorId == filter.ProveedorId.Value);
        if (filter.UsarFechas)
        {
            if (filter.Desde.HasValue) query = query.Where(c => c.FechaCreacion >= filter.Desde.Value.Date);
            if (filter.Hasta.HasValue) query = query.Where(c => c.FechaCreacion < filter.Hasta.Value.Date.AddDays(1));
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(c => c.CodigoCuenta.Contains(search) || c.Compra.CodigoCompra.Contains(search) ||
                c.Proveedor.CodigoProveedor.Contains(search) || c.Proveedor.NombreRazonSocial.Contains(search));
        }
        return query;
    }

    private static AccountPayableSummary ToSummary(CuentaPorPagar account) => new(
        account.CuentaPorPagarId, account.CodigoCuenta, account.CompraId, account.Compra.CodigoCompra,
        account.ProveedorId, account.Proveedor.CodigoProveedor, account.Proveedor.NombreRazonSocial,
        account.Compra.FechaCompra, account.FechaVencimiento, account.TotalOriginalCentavos,
        account.TotalPagadoCentavos, account.SaldoPendienteCentavos,
        SupplierFinancialRules.ResolveAccountState(account.TotalPagadoCentavos, account.SaldoPendienteCentavos, account.FechaVencimiento, DateTime.Now),
        account.UsuarioCreador.NombreUsuario, GetVisibleObservation(account.Observaciones));

    private static AccountPayableDetail ToDetail(CuentaPorPagar account) => new(
        ToSummary(account), account.Compra.EstadoFinanciero,
        account.Compra.Pagos.OrderBy(p => p.FechaPago).Select(p => new PurchasePaymentSummary(
            p.PagoCompraId, p.CodigoPago, p.OperacionId, p.CompraId, account.Compra.CodigoCompra,
            account.Proveedor.NombreRazonSocial, p.Usuario.NombreUsuario, p.CajaId, p.MetodoPago,
            p.MontoCentavos, p.Referencia, p.FechaPago, p.Observaciones, p.EsPagoInicial,
            p.OrigenFondos, p.ResponsableOrigen)).ToList(),
        account.Abonos.OrderBy(a => a.FechaAbono).Select(a => new SupplierPaymentSummary(
            a.AbonoCuentaPorPagarId, a.CodigoAbono, a.OperacionId, a.CuentaPorPagarId, account.CodigoCuenta,
            account.CompraId, account.Compra.CodigoCompra, account.ProveedorId, account.Proveedor.CodigoProveedor,
            account.Proveedor.NombreRazonSocial, a.Usuario.NombreUsuario, a.CajaId, a.MetodoPago,
            a.MontoCentavos, a.SaldoAnteriorCentavos, a.SaldoNuevoCentavos, a.Referencia,
             a.Observaciones, a.FechaAbono, a.OrigenFondos, a.ResponsableOrigen)).ToList());

    private static SupplierPaymentSummary ToPaymentSummary(AbonoCuentaPorPagar a) => new(
        a.AbonoCuentaPorPagarId, a.CodigoAbono, a.OperacionId, a.CuentaPorPagarId, a.CuentaPorPagar.CodigoCuenta,
        a.CuentaPorPagar.CompraId, a.CuentaPorPagar.Compra.CodigoCompra, a.CuentaPorPagar.ProveedorId,
        a.CuentaPorPagar.Proveedor.CodigoProveedor, a.CuentaPorPagar.Proveedor.NombreRazonSocial,
        a.Usuario.NombreUsuario, a.CajaId, a.MetodoPago, a.MontoCentavos, a.SaldoAnteriorCentavos,
         a.SaldoNuevoCentavos, a.Referencia, a.Observaciones, a.FechaAbono,
         a.OrigenFondos, a.ResponsableOrigen);

    private static IReadOnlyList<SupplierPaymentHistoryEntry> BuildHistory(CuentaPorPagar account)
    {
        var movements = account.Compra.Pagos
            .Select(payment => new HistoryMovement(payment.FechaPago, true, payment.PagoCompraId, payment, null))
            .Concat(account.Abonos.Select(payment => new HistoryMovement(payment.FechaAbono, false, payment.AbonoCuentaPorPagarId, null, payment)))
            .OrderBy(movement => movement.Date)
            .ThenBy(movement => movement.IsInitial ? 0 : 1)
            .ThenBy(movement => movement.Id)
            .ToList();

        var result = new List<SupplierPaymentHistoryEntry>(movements.Count);
        var runningBalance = account.TotalOriginalCentavos;
        foreach (var movement in movements)
        {
            if (movement.InitialPayment is { } initial)
            {
                var previous = runningBalance;
                runningBalance = Math.Max(0, runningBalance - initial.MontoCentavos);
                result.Add(new SupplierPaymentHistoryEntry(
                    true, initial.PagoCompraId, initial.CodigoPago, initial.OperacionId,
                    account.CuentaPorPagarId, account.CodigoCuenta, account.CompraId, account.Compra.CodigoCompra,
                    account.ProveedorId, account.Proveedor.CodigoProveedor, account.Proveedor.NombreRazonSocial,
                    initial.Usuario.NombreUsuario, initial.CajaId, initial.MetodoPago, initial.MontoCentavos,
                     previous, runningBalance, initial.Referencia, initial.Observaciones, initial.FechaPago,
                     initial.OrigenFondos, initial.ResponsableOrigen));
                continue;
            }

            var payment = movement.SupplierPayment!;
            runningBalance = payment.SaldoNuevoCentavos;
            result.Add(new SupplierPaymentHistoryEntry(
                false, payment.AbonoCuentaPorPagarId, payment.CodigoAbono, payment.OperacionId,
                account.CuentaPorPagarId, account.CodigoCuenta, account.CompraId, account.Compra.CodigoCompra,
                account.ProveedorId, account.Proveedor.CodigoProveedor, account.Proveedor.NombreRazonSocial,
                payment.Usuario.NombreUsuario, payment.CajaId, payment.MetodoPago, payment.MontoCentavos,
                payment.SaldoAnteriorCentavos, payment.SaldoNuevoCentavos, payment.Referencia,
                 payment.Observaciones, payment.FechaAbono, payment.OrigenFondos, payment.ResponsableOrigen));
        }

        return result;
    }

    private static string? GetVisibleObservation(string? observation)
    {
        return observation?.StartsWith("Backfill v1.6.0 Bloque C", StringComparison.OrdinalIgnoreCase) == true
            ? "Cuenta generada automáticamente para una compra registrada previamente sin pagos."
            : observation;
    }

    private sealed record HistoryMovement(
        DateTime Date,
        bool IsInitial,
        int Id,
        PagoCompra? InitialPayment,
        AbonoCuentaPorPagar? SupplierPayment);
}
