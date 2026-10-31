using System.Data;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class AccountPaymentService : IAccountPaymentService
{
    private const string ConcurrencyMessage = "La cuenta fue actualizada por otra operacion. Revise el saldo actual e intente nuevamente.";

    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IAuditService _auditService;

    [ActivatorUtilitiesConstructor]
    public AccountPaymentService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        IAuditService auditService)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
    }

    internal AccountPaymentService(
        AppDbContext context,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        IAuditService auditService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
    }

    public async Task<RegisterAccountPaymentResult> RegisterPaymentAsync(
        RegisterAccountPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var context = lease.Context;

        ValidateRequestShape(request);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.ABONO_REGISTRAR, cancellationToken);

        var usuario = await GetActiveCurrentUserAsync(context, cancellationToken);
        var normalizedReference = NormalizeOptional(request.Referencia);
        var normalizedObservations = NormalizeOptional(request.Observaciones);
        ValidatePaymentMethod(request.MetodoPago, normalizedReference);

        var existing = await TryGetExistingResultAsync(
            context,
            request,
            normalizedReference,
            normalizedObservations,
            isReplay: true,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            existing = await TryGetExistingResultAsync(
                context,
                request,
                normalizedReference,
                normalizedObservations,
                isReplay: true,
                cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }

            await PaymentMethodAvailability.EnsureAvailableAsync(context, request.MetodoPago, cancellationToken);

            var account = await context.CuentasPorCobrar
                .Include(c => c.Cliente)
                .Include(c => c.Venta)
                .Include(c => c.Detalles)
                .FirstOrDefaultAsync(c => c.CuentaPorCobrarId == request.CuentaPorCobrarId, cancellationToken)
                ?? throw new InvalidOperationException("La cuenta por cobrar no existe.");

            ValidateAccountCanReceivePayment(account);

            var caja = request.MetodoPago == MetodoPago.EFECTIVO
                ? await GetOpenCashRequiredAsync(context, cancellationToken)
                : null;

            if (request.MontoCentavos > account.SaldoActualCentavos)
            {
                throw new InvalidOperationException("El abono no puede superar el saldo actual de la cuenta.");
            }

            var computation = AccountPaymentCalculator.Calculate(account, request.MontoCentavos);

            var payment = new AbonoCuentaPorCobrar
            {
                CodigoAbono = await GeneratePaymentCodeAsync(context, cancellationToken),
                OperacionId = request.OperacionId,
                CuentaPorCobrarId = account.CuentaPorCobrarId,
                UsuarioId = usuario.UsuarioId,
                CajaId = caja?.CajaId,
                MetodoPago = request.MetodoPago,
                MontoCentavos = request.MontoCentavos,
                AplicadoInteresCentavos = computation.AppliedInterest,
                AplicadoCapitalCentavos = computation.AppliedCapital,
                SaldoAnteriorCentavos = account.SaldoActualCentavos,
                SaldoNuevoCentavos = computation.NewBalance,
                InteresPendienteAnteriorCentavos = computation.InterestPending,
                InteresPendienteNuevoCentavos = computation.NewInterestPending,
                CapitalPendienteAnteriorCentavos = computation.CapitalPending,
                CapitalPendienteNuevoCentavos = computation.NewCapitalPending,
                Referencia = normalizedReference,
                Observaciones = normalizedObservations,
                FechaAbono = DateTime.UtcNow
            };

            context.AbonosCuentaPorCobrar.Add(payment);

            DistributeCapitalPayment(account.Detalles, computation.AppliedCapital);
            account.TotalPagadoCentavos += request.MontoCentavos;
            account.SaldoActualCentavos = computation.NewBalance;
            account.FechaActualizacion = DateTime.UtcNow;
            if (computation.NewBalance == 0)
            {
                account.Estado = EstadoCuentaPorCobrar.PAGADA;
                account.FechaPago = payment.FechaAbono;
            }

            await context.SaveChangesAsync(cancellationToken);
            await RegisterAuditAsync(payment, account, usuario, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToResult(payment, account, usuario, isReplay: false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new InvalidOperationException(ConcurrencyMessage, ex);
        }
        catch (DbUpdateException ex) when (IsOperationIdUniqueConflict(ex))
        {
            context.ChangeTracker.Clear();
            var replay = await TryGetExistingResultAsync(
                context,
                request,
                normalizedReference,
                normalizedObservations,
                isReplay: true,
                cancellationToken);

            if (replay is not null)
            {
                return replay;
            }

            throw;
        }
    }

    private static void ValidateRequestShape(RegisterAccountPaymentRequest request)
    {
        if (request.OperacionId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de operacion del abono es obligatorio.", nameof(request));
        }

        if (request.CuentaPorCobrarId <= 0)
        {
            throw new ArgumentException("La cuenta por cobrar es obligatoria.", nameof(request));
        }

        if (request.MontoCentavos <= 0)
        {
            throw new ArgumentException("El monto del abono debe ser mayor que cero.", nameof(request));
        }
    }

    private static void ValidatePaymentMethod(MetodoPago method, string? reference)
    {
        if (method is not MetodoPago.EFECTIVO and not MetodoPago.TRANSFERENCIA and not MetodoPago.BITCOIN)
        {
            throw new InvalidOperationException("El metodo de pago del abono no esta permitido.");
        }

        if (method is MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN &&
            string.IsNullOrWhiteSpace(reference))
        {
            throw new InvalidOperationException("La referencia es obligatoria para transferencia y Bitcoin.");
        }
    }

    private async Task<Usuario> GetActiveCurrentUserAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var usuarioId = _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa para registrar abonos.");

        var usuario = await context.Usuarios
            .FirstOrDefaultAsync(u => u.UsuarioId == usuarioId && u.Activo, cancellationToken)
            ?? throw new InvalidOperationException("El usuario actual no existe o esta inactivo.");

        if (usuario.Rol is not RolUsuario.ADMIN and not RolUsuario.EMPLEADO)
        {
            throw new InvalidOperationException("El rol actual no puede registrar abonos.");
        }

        return usuario;
    }

    internal static void ValidateAccountCanReceivePayment(CuentaPorCobrar account)
    {
        if (account.Estado == EstadoCuentaPorCobrar.PAGADA)
        {
            throw new InvalidOperationException("La cuenta ya esta pagada y no permite nuevos abonos.");
        }

        if (account.Estado == EstadoCuentaPorCobrar.ANULADA)
        {
            throw new InvalidOperationException("La cuenta esta anulada y no permite abonos.");
        }

        if (account.Estado != EstadoCuentaPorCobrar.ABIERTA)
        {
            throw new InvalidOperationException("La cuenta no esta abierta para recibir abonos.");
        }

        if (account.SaldoActualCentavos <= 0)
        {
            throw new InvalidOperationException("La cuenta no tiene saldo pendiente.");
        }
    }

    private static async Task<Caja> GetOpenCashRequiredAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        return await CashRegisterStateQueries.GetCurrentOpenCashAsync(context, cancellationToken)
            ?? throw new InvalidOperationException("No se puede registrar un abono en efectivo sin caja abierta.");
    }

    private static void DistributeCapitalPayment(IEnumerable<CuentaPorCobrarDetalle> details, long appliedCapital)
    {
        if (appliedCapital <= 0)
        {
            return;
        }

        var eligibleDetails = details
            .Where(d => d.CapitalPendienteCentavos > 0)
            .OrderByDescending(d => d.CapitalPendienteCentavos)
            .ThenBy(d => d.CuentaPorCobrarDetalleId)
            .ToList();

        var totalPending = eligibleDetails.Sum(d => d.CapitalPendienteCentavos);
        if (totalPending < appliedCapital || eligibleDetails.Count == 0)
        {
            throw new InvalidOperationException("La cuenta no tiene capital pendiente suficiente para distribuir el abono.");
        }

        var allocations = eligibleDetails.ToDictionary(d => d.CuentaPorCobrarDetalleId, _ => 0L);
        var allocated = 0L;
        foreach (var detail in eligibleDetails)
        {
            var allocation = appliedCapital * detail.CapitalPendienteCentavos / totalPending;
            allocation = Math.Min(allocation, detail.CapitalPendienteCentavos);
            allocations[detail.CuentaPorCobrarDetalleId] = allocation;
            allocated += allocation;
        }

        // Los centavos residuales se asignan de forma determinista al detalle con mayor capital pendiente y luego por id.
        var remaining = appliedCapital - allocated;
        while (remaining > 0)
        {
            var assignedInPass = false;
            foreach (var detail in eligibleDetails)
            {
                var current = allocations[detail.CuentaPorCobrarDetalleId];
                if (current >= detail.CapitalPendienteCentavos)
                {
                    continue;
                }

                allocations[detail.CuentaPorCobrarDetalleId] = current + 1;
                remaining--;
                assignedInPass = true;
                if (remaining == 0)
                {
                    break;
                }
            }

            if (!assignedInPass)
            {
                throw new InvalidOperationException("No se pudo distribuir exactamente el capital abonado.");
            }
        }

        foreach (var detail in eligibleDetails)
        {
            detail.CapitalPendienteCentavos -= allocations[detail.CuentaPorCobrarDetalleId];
            if (detail.CapitalPendienteCentavos < 0)
            {
                throw new InvalidOperationException("Un detalle financiado quedo con capital pendiente negativo.");
            }
        }
    }

    private static async Task<string> GeneratePaymentCodeAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var prefix = $"ABN-{DateTime.Now:yyyyMMdd}-";
        var countToday = await context.AbonosCuentaPorCobrar
            .CountAsync(a => a.CodigoAbono.StartsWith(prefix), cancellationToken);
        var next = countToday + 1;

        while (true)
        {
            var code = $"{prefix}{next:0000}";
            var exists = await context.AbonosCuentaPorCobrar
                .AnyAsync(a => a.CodigoAbono == code, cancellationToken);
            if (!exists)
            {
                return code;
            }

            next++;
        }
    }

    private async Task<RegisterAccountPaymentResult?> TryGetExistingResultAsync(
        AppDbContext context,
        RegisterAccountPaymentRequest request,
        string? normalizedReference,
        string? normalizedObservations,
        bool isReplay,
        CancellationToken cancellationToken)
    {
        var existing = await context.AbonosCuentaPorCobrar
            .AsNoTracking()
            .Include(a => a.CuentaPorCobrar)
            .ThenInclude(c => c.Cliente)
            .Include(a => a.CuentaPorCobrar)
            .ThenInclude(c => c.Venta)
            .Include(a => a.Usuario)
            .FirstOrDefaultAsync(a => a.OperacionId == request.OperacionId, cancellationToken);

        if (existing is null)
        {
            return null;
        }

        if (existing.CuentaPorCobrarId != request.CuentaPorCobrarId ||
            existing.MontoCentavos != request.MontoCentavos ||
            existing.MetodoPago != request.MetodoPago ||
            !string.Equals(existing.Referencia, normalizedReference, StringComparison.Ordinal) ||
            !string.Equals(existing.Observaciones, normalizedObservations, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("El identificador de operacion ya fue utilizado para otro abono.");
        }

        return ToResult(existing, existing.CuentaPorCobrar, existing.Usuario, isReplay);
    }

    private async Task RegisterAuditAsync(
        AbonoCuentaPorCobrar payment,
        CuentaPorCobrar account,
        Usuario usuario,
        CancellationToken cancellationToken)
    {
        var cajaText = payment.CajaId.HasValue ? $" Caja {payment.CajaId.Value}." : string.Empty;
        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuario.UsuarioId,
                TipoBitacora.ABONO_REGISTRADO,
                "abonos_cuenta_por_cobrar",
                payment.AbonoCuentaPorCobrarId,
                $"Abono {payment.CodigoAbono} registrado para cuenta {account.CodigoCuenta}. Cliente {account.Cliente.CodigoCliente}. Venta {account.Venta.NumeroVenta}. Metodo {payment.MetodoPago}. Monto {FormatCentavos(payment.MontoCentavos)}. Interes {FormatCentavos(payment.AplicadoInteresCentavos)}. Capital {FormatCentavos(payment.AplicadoCapitalCentavos)}. Saldo {FormatCentavos(payment.SaldoAnteriorCentavos)} -> {FormatCentavos(payment.SaldoNuevoCentavos)}.{cajaText}"),
            cancellationToken);
    }

    private static RegisterAccountPaymentResult ToResult(
        AbonoCuentaPorCobrar payment,
        CuentaPorCobrar account,
        Usuario usuario,
        bool isReplay)
    {
        return new RegisterAccountPaymentResult(
            payment.AbonoCuentaPorCobrarId,
            payment.CodigoAbono,
            payment.OperacionId,
            payment.CuentaPorCobrarId,
            account.CodigoCuenta,
            account.ClienteId,
            account.Cliente.NombreCompleto,
            account.VentaId,
            account.Venta.NumeroVenta,
            payment.UsuarioId,
            usuario.NombreUsuario,
            payment.CajaId,
            payment.MetodoPago,
            payment.MontoCentavos,
            payment.AplicadoInteresCentavos,
            payment.AplicadoCapitalCentavos,
            payment.SaldoAnteriorCentavos,
            payment.SaldoNuevoCentavos,
            payment.InteresPendienteAnteriorCentavos,
            payment.InteresPendienteNuevoCentavos,
            payment.CapitalPendienteAnteriorCentavos,
            payment.CapitalPendienteNuevoCentavos,
            payment.Referencia,
            payment.Observaciones,
            payment.FechaAbono,
            isReplay);
    }

    private static bool IsOperationIdUniqueConflict(DbUpdateException ex)
    {
        var message = ex.GetBaseException().Message;
        return message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("operacion_id", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string FormatCentavos(long centavos)
    {
        return (centavos / 100m).ToString("$0.00");
    }

}
