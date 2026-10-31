using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class AccountPaymentQueryService : IAccountPaymentQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IEmployeePermissionService _permissionService;

    [ActivatorUtilitiesConstructor]
    public AccountPaymentQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        IEmployeePermissionService permissionService)
    {
        _contextFactory = contextFactory;
        _permissionService = permissionService;
    }

    internal AccountPaymentQueryService(
        AppDbContext context,
        IEmployeePermissionService permissionService)
    {
        _legacyContext = context;
        _permissionService = permissionService;
    }

    public async Task<AccountPaymentPreview> PreviewAsync(
        AccountPaymentPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var context = lease.Context;

        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.ABONO_REGISTRAR, cancellationToken);
        ValidateMethod(request.MetodoPago, request.Referencia);

        var account = await GetAccountQuery(context)
            .FirstOrDefaultAsync(c => c.CuentaPorCobrarId == request.CuentaPorCobrarId, cancellationToken)
            ?? throw new InvalidOperationException("La cuenta por cobrar no existe.");

        AccountPaymentService.ValidateAccountCanReceivePayment(account);
        var computation = AccountPaymentCalculator.Calculate(account, request.MontoCentavos);
        var caja = request.MetodoPago == MetodoPago.EFECTIVO
            ? await GetOpenCashRequiredAsync(context, cancellationToken)
            : null;

        return new AccountPaymentPreview(
            account.CuentaPorCobrarId,
            account.CodigoCuenta,
            account.ClienteId,
            account.Cliente.CodigoCliente,
            account.Cliente.NombreCompleto,
            account.VentaId,
            account.Venta.NumeroVenta,
            account.Estado,
            CalculateSituation(account),
            account.FechaVencimiento,
            account.CapitalFinanciadoCentavos,
            account.InteresCalculadoCentavos,
            account.TotalOriginalCentavos,
            account.TotalPagadoCentavos,
            computation.InterestPending,
            computation.CapitalPending,
            account.SaldoActualCentavos,
            request.MontoCentavos,
            computation.AppliedInterest,
            computation.AppliedCapital,
            computation.NewBalance,
            computation.NewBalance == 0 ? EstadoCuentaPorCobrar.PAGADA : EstadoCuentaPorCobrar.ABIERTA,
            caja?.CajaId,
            caja is null ? null : $"Caja {caja.CajaId} - {caja.FechaOperacion:yyyy-MM-dd}");
    }

    public async Task<IReadOnlyList<AccountPaymentSummary>> GetHistoryAsync(
        AccountPaymentHistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var context = lease.Context;

        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CUENTAS_COBRAR_VER, cancellationToken);

        var query = context.AbonosCuentaPorCobrar
            .AsNoTracking()
            .Include(a => a.CuentaPorCobrar)
            .ThenInclude(c => c.Cliente)
            .Include(a => a.CuentaPorCobrar)
            .ThenInclude(c => c.Venta)
            .Include(a => a.Usuario)
            .AsQueryable();

        if (filter.Desde.HasValue)
        {
            query = query.Where(a => a.FechaAbono >= filter.Desde.Value);
        }

        if (filter.Hasta.HasValue)
        {
            query = query.Where(a => a.FechaAbono <= filter.Hasta.Value);
        }

        if (filter.ClienteId.HasValue)
        {
            query = query.Where(a => a.CuentaPorCobrar.ClienteId == filter.ClienteId.Value);
        }

        if (filter.CuentaPorCobrarId.HasValue)
        {
            query = query.Where(a => a.CuentaPorCobrarId == filter.CuentaPorCobrarId.Value);
        }

        if (filter.VentaId.HasValue)
        {
            query = query.Where(a => a.CuentaPorCobrar.VentaId == filter.VentaId.Value);
        }

        if (filter.MetodoPago.HasValue)
        {
            query = query.Where(a => a.MetodoPago == filter.MetodoPago.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.CodigoAbono))
        {
            var code = filter.CodigoAbono.Trim();
            query = query.Where(a => a.CodigoAbono.Contains(code));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(a =>
                a.CodigoAbono.Contains(search) ||
                a.CuentaPorCobrar.CodigoCuenta.Contains(search) ||
                a.CuentaPorCobrar.Venta.NumeroVenta.Contains(search) ||
                a.CuentaPorCobrar.Cliente.CodigoCliente.Contains(search) ||
                a.CuentaPorCobrar.Cliente.NombreCompleto.Contains(search));
        }

        var payments = await query
            .OrderByDescending(a => a.FechaAbono)
            .ThenByDescending(a => a.AbonoCuentaPorCobrarId)
            .Take(1000)
            .ToListAsync(cancellationToken);

        return payments.Select(ToSummary).ToList();
    }

    public async Task<AccountPaymentDetail?> GetDetailAsync(
        int abonoCuentaPorCobrarId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var context = lease.Context;

        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CUENTAS_COBRAR_VER, cancellationToken);

        var payment = await context.AbonosCuentaPorCobrar
            .AsNoTracking()
            .Include(a => a.CuentaPorCobrar)
            .ThenInclude(c => c.Cliente)
            .Include(a => a.CuentaPorCobrar)
            .ThenInclude(c => c.Venta)
            .Include(a => a.Usuario)
            .FirstOrDefaultAsync(a => a.AbonoCuentaPorCobrarId == abonoCuentaPorCobrarId, cancellationToken);

        if (payment is null)
        {
            return null;
        }

        var account = payment.CuentaPorCobrar;
        return new AccountPaymentDetail(
            ToSummary(payment),
            account.Estado,
            CalculateSituation(account),
            account.FechaVencimiento,
            account.CapitalFinanciadoCentavos,
            account.InteresCalculadoCentavos,
            account.TotalOriginalCentavos,
            account.TotalPagadoCentavos,
            payment.InteresPendienteAnteriorCentavos,
            payment.InteresPendienteNuevoCentavos,
            payment.CapitalPendienteAnteriorCentavos,
            payment.CapitalPendienteNuevoCentavos);
    }

    private static IQueryable<CuentaPorCobrar> GetAccountQuery(AppDbContext context)
    {
        return context.CuentasPorCobrar
            .AsNoTracking()
            .Include(c => c.Cliente)
            .Include(c => c.Venta)
            .Include(c => c.Detalles);
    }

    private static async Task<Caja> GetOpenCashRequiredAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        return await CashRegisterStateQueries.OpenCash(context)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Para registrar un abono en efectivo debe existir una caja abierta.");
    }

    private static void ValidateMethod(MetodoPago method, string? reference)
    {
        if (method is not MetodoPago.EFECTIVO and not MetodoPago.TRANSFERENCIA and not MetodoPago.BITCOIN)
        {
            throw new InvalidOperationException("El metodo de pago del abono no esta permitido.");
        }

        if (method is MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN && string.IsNullOrWhiteSpace(reference))
        {
            throw new InvalidOperationException("La referencia es obligatoria para Transferencia y Bitcoin.");
        }
    }

    private static SituacionCrediticia CalculateSituation(CuentaPorCobrar account)
    {
        if (account.Estado != EstadoCuentaPorCobrar.ABIERTA || account.SaldoActualCentavos <= 0)
        {
            return SituacionCrediticia.AL_DIA;
        }

        var lateDays = (DateTime.UtcNow.Date - account.FechaVencimiento.Date).Days;
        if (lateDays >= 7)
        {
            return SituacionCrediticia.MOROSO;
        }

        return lateDays > 0
            ? SituacionCrediticia.VENCIDO
            : SituacionCrediticia.SALDO_VIGENTE;
    }

    private static AccountPaymentSummary ToSummary(AbonoCuentaPorCobrar payment)
    {
        return new AccountPaymentSummary(
            payment.AbonoCuentaPorCobrarId,
            payment.CodigoAbono,
            payment.OperacionId,
            payment.CuentaPorCobrarId,
            payment.CuentaPorCobrar.CodigoCuenta,
            payment.CuentaPorCobrar.ClienteId,
            payment.CuentaPorCobrar.Cliente.CodigoCliente,
            payment.CuentaPorCobrar.Cliente.NombreCompleto,
            payment.CuentaPorCobrar.VentaId,
            payment.CuentaPorCobrar.Venta.NumeroVenta,
            payment.Usuario.NombreUsuario,
            payment.CajaId,
            payment.MetodoPago,
            payment.MontoCentavos,
            payment.AplicadoInteresCentavos,
            payment.AplicadoCapitalCentavos,
            payment.SaldoAnteriorCentavos,
            payment.SaldoNuevoCentavos,
            payment.Referencia,
            payment.Observaciones,
            payment.FechaAbono);
    }
}
