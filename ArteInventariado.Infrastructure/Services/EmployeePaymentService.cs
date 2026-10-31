using System.Data;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

/// <summary>
/// Writes administrative employee-payment records. A payment is never
/// deleted; corrections are represented by a cancellation with a reason.
/// </summary>
public sealed class EmployeePaymentService : IEmployeePaymentService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _session;
    private readonly IAuditService _audit;
    private readonly TimeProvider _timeProvider;

    [ActivatorUtilitiesConstructor]
    public EmployeePaymentService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession session,
        IAuditService audit,
        TimeProvider timeProvider)
    {
        _contextFactory = contextFactory;
        _session = session;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    internal EmployeePaymentService(
        AppDbContext context,
        ICurrentSession session,
        IAuditService audit,
        TimeProvider timeProvider)
    {
        _legacyContext = context;
        _session = session;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public Task<bool> CanRegisterAsync(CancellationToken cancellationToken = default)
        => CanAsync(PermisoEmpleado.EMPLEADOS_PAGOS_REGISTRAR, cancellationToken);

    public Task<bool> CanCancelAsync(CancellationToken cancellationToken = default)
        => CanAsync(PermisoEmpleado.EMPLEADOS_PAGOS_ANULAR, cancellationToken);

    public async Task<EmployeePaymentRecord> RegisterPaymentAsync(
        RegisterEmployeePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var method = request.MetodoPago!.Value;
        var origin = request.OrigenFondos!.Value;
        var reference = NormalizeOptional(request.Referencia);
        var responsible = SupplierPaymentFundingPolicy.NormalizeResponsible(origin, request.ResponsableOrigen);
        SupplierPaymentFundingPolicy.Validate(origin, method, reference, responsible);
        var note = NormalizeNote(request.Note);
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var db = lease.Context;
        await EmployeeModuleAccessGuard.EnsurePaymentAsync(
            db, _session, PermisoEmpleado.EMPLEADOS_PAGOS_REGISTRAR, cancellationToken);

        var target = await db.Usuarios
            .AsNoTracking()
            .Where(user => user.UsuarioId == request.UserId)
            .Select(user => new { user.UsuarioId, user.NombreCompleto, user.NombreUsuario })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("El usuario al que corresponde el pago no existe.");

        var registrarId = GetCurrentUserId();
        var registrar = await db.Usuarios
            .AsNoTracking()
            .Where(user => user.UsuarioId == registrarId && user.Activo && user.Rol == RolUsuario.ADMIN)
            .Select(user => new { user.UsuarioId, user.NombreUsuario })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("El administrador actual no existe o esta inactivo.");

        var now = DateTime.SpecifyKind(_timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Utc);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var replay = await FindReplayAsync(
            db, request, note, method, origin, responsible, reference, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await PaymentMethodAvailability.EnsureAvailableAsync(db, method, cancellationToken);

        Caja? cash = null;
        if (SupplierPaymentFundingPolicy.AffectsPhysicalCash(origin))
        {
            cash = await CashRegisterStateQueries.GetCurrentOpenCashAsync(db, cancellationToken)
                ?? throw new InvalidOperationException("No se puede pagar desde caja sin una caja abierta.");
            await LockCashRowAsync(db, cash, cancellationToken);
            var available = await CashService.BuildSummaryAsync(db, cash, cancellationToken);
            if (request.AmountCentavos > available.TotalEsperadoCentavos)
            {
                throw new InvalidOperationException(
                    $"Efectivo insuficiente en caja. Disponible: {FormatCentavos(available.TotalEsperadoCentavos)}; " +
                    $"pago al empleado: {FormatCentavos(request.AmountCentavos)}.");
            }
        }

        var payment = new EmployeePayment
        {
            UserId = target.UsuarioId,
            CajaId = cash?.CajaId,
            AmountCentavos = request.AmountCentavos,
            PaymentType = request.PaymentType,
            MetodoPago = method,
            Referencia = reference,
            OperacionId = request.OperacionId,
            OrigenFondos = origin,
            ResponsableOrigen = responsible,
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            DaysConsidered = request.DaysConsidered,
            Note = note,
            RegisteredByUserId = registrar.UsuarioId,
            CreatedAtUtc = now,
            Status = EmployeePaymentStatus.Active
        };

        db.EmployeePayments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
        await _audit.RegistrarAsync(
            new AuditEntryRequest(
                registrar.UsuarioId,
                TipoBitacora.PAGO_EMPLEADO_REGISTRADO,
                "employee_payments",
                payment.EmployeePaymentId,
                $"Pago de empleado registrado para {target.NombreUsuario} por {FormatCentavos(payment.AmountCentavos)}. " +
                $"Metodo: {method}; origen: {origin}; responsable/fuente: {responsible ?? "-"}; " +
                $"efecto en caja: {(payment.CajaId.HasValue ? "si" : "no")}; operacion: {request.OperacionId:N}."),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToRecord(
            payment,
            target.NombreCompleto,
            target.NombreUsuario,
            registrar.NombreUsuario,
            null,
            null);
    }

    public async Task<EmployeePaymentRecord> CancelPaymentAsync(
        CancelEmployeePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCancellationRequest(request);
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var db = lease.Context;
        await EmployeeModuleAccessGuard.EnsurePaymentAsync(
            db, _session, PermisoEmpleado.EMPLEADOS_PAGOS_ANULAR, cancellationToken);

        var registrarId = GetCurrentUserId();
        var registrar = await db.Usuarios
            .AsNoTracking()
            .Where(user => user.UsuarioId == registrarId && user.Activo && user.Rol == RolUsuario.ADMIN)
            .Select(user => new { user.UsuarioId, user.NombreUsuario })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("El administrador actual no existe o esta inactivo.");

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var payment = await db.EmployeePayments
            .Include(item => item.User)
            .Include(item => item.RegisteredBy)
            .FirstOrDefaultAsync(item => item.EmployeePaymentId == request.EmployeePaymentId, cancellationToken)
            ?? throw new InvalidOperationException("El pago de empleado no existe.");

        if (payment.Status != EmployeePaymentStatus.Active)
        {
            throw new InvalidOperationException("El pago ya esta anulado y no puede anularse nuevamente.");
        }

        var now = DateTime.SpecifyKind(_timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Utc);
        payment.Status = EmployeePaymentStatus.Cancelled;
        payment.CancelledByUserId = registrar.UsuarioId;
        payment.CancelledAtUtc = now;
        payment.CancellationReason = NormalizeRequiredReason(request.Reason);

        await db.SaveChangesAsync(cancellationToken);
        await _audit.RegistrarAsync(
            new AuditEntryRequest(
                registrar.UsuarioId,
                TipoBitacora.PAGO_EMPLEADO_ANULADO,
                "employee_payments",
                payment.EmployeePaymentId,
                $"Pago de empleado anulado administrativamente para {payment.User.NombreUsuario}. " +
                $"La salida de caja no se revierte automaticamente. Motivo: {payment.CancellationReason}"),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToRecord(
            payment,
            payment.User.NombreCompleto,
            payment.User.NombreUsuario,
            payment.RegisteredBy.NombreUsuario,
            registrar.NombreUsuario,
            payment.CancellationReason);
    }

    private int GetCurrentUserId() => _session.UsuarioActual?.UsuarioId
        ?? throw new InvalidOperationException("Debe existir una sesion activa para administrar pagos de empleados.");

    private async Task<bool> CanAsync(
        PermisoEmpleado permission,
        CancellationToken cancellationToken)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        try
        {
            await EmployeeModuleAccessGuard.EnsurePaymentAsync(
                lease.Context, _session, permission, cancellationToken);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void ValidateRequest(RegisterEmployeePaymentRequest request)
    {
        if (request.UserId <= 0)
        {
            throw new ArgumentException("El empleado es obligatorio.", nameof(request));
        }

        if (request.AmountCentavos <= 0)
        {
            throw new ArgumentException("El monto debe ser mayor que cero.", nameof(request));
        }

        if (!Enum.IsDefined(request.PaymentType))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "El tipo de pago no es valido.");
        }

        if (request.MetodoPago is not { } method || !Enum.IsDefined(method))
        {
            throw new ArgumentException("Seleccione un metodo de pago valido.", nameof(request));
        }

        if (request.OrigenFondos is not { } origin || !Enum.IsDefined(origin))
        {
            throw new ArgumentException("Seleccione explicitamente el origen de los fondos.", nameof(request));
        }

        if (request.OperacionId == Guid.Empty)
        {
            throw new ArgumentException("El pago requiere un identificador de operacion valido.", nameof(request));
        }

        if (request.PeriodStart.HasValue && request.PeriodEnd.HasValue &&
            request.PeriodEnd.Value < request.PeriodStart.Value)
        {
            throw new ArgumentException("El fin del periodo no puede ser anterior al inicio.", nameof(request));
        }

        if (request.DaysConsidered is < 0)
        {
            throw new ArgumentException("Los dias considerados no pueden ser negativos.", nameof(request));
        }

        if (request.Note is { Length: > 1000 })
        {
            throw new ArgumentException("La nota no puede superar 1000 caracteres.", nameof(request));
        }

        if (request.Referencia is { Length: > 180 })
        {
            throw new ArgumentException("La referencia no puede superar 180 caracteres.", nameof(request));
        }
    }

    private static void ValidateCancellationRequest(CancelEmployeePaymentRequest request)
    {
        if (request.EmployeePaymentId <= 0)
        {
            throw new ArgumentException("El pago es obligatorio.", nameof(request));
        }

        _ = NormalizeRequiredReason(request.Reason);
    }

    private static string? NormalizeNote(string? note)
        => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Task LockCashRowAsync(AppDbContext db, Caja cash, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cajas SET caja_id = caja_id WHERE caja_id = {cash.CajaId};",
            cancellationToken);

    private async Task<EmployeePaymentRecord?> FindReplayAsync(
        AppDbContext db,
        RegisterEmployeePaymentRequest request,
        string? note,
        MetodoPago method,
        OrigenFondos origin,
        string? responsible,
        string? reference,
        CancellationToken cancellationToken)
    {
        var existing = await db.EmployeePayments.AsNoTracking()
            .Include(payment => payment.User)
            .Include(payment => payment.RegisteredBy)
            .Include(payment => payment.CancelledBy)
            .SingleOrDefaultAsync(payment => payment.OperacionId == request.OperacionId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (existing.UserId != request.UserId || existing.AmountCentavos != request.AmountCentavos ||
            existing.PaymentType != request.PaymentType || existing.PeriodStart != request.PeriodStart ||
            existing.PeriodEnd != request.PeriodEnd || existing.DaysConsidered != request.DaysConsidered ||
            existing.Note != note || existing.MetodoPago != method || existing.OrigenFondos != origin ||
            existing.ResponsableOrigen != responsible || existing.Referencia != reference)
        {
            throw new InvalidOperationException("El identificador de operacion ya fue usado para otro pago de empleado.");
        }

        return ToRecord(
            existing,
            existing.User.NombreCompleto,
            existing.User.NombreUsuario,
            existing.RegisteredBy.NombreUsuario,
            existing.CancelledBy?.NombreUsuario,
            existing.CancellationReason);
    }

    private static string NormalizeRequiredReason(string reason)
    {
        var normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("El motivo de anulacion es obligatorio.", nameof(reason));
        }

        if (normalized.Length > 1000)
        {
            throw new ArgumentException("El motivo de anulacion no puede superar 1000 caracteres.", nameof(reason));
        }

        return normalized;
    }

    private static EmployeePaymentRecord ToRecord(
        EmployeePayment payment,
        string employeeName,
        string employeeUserName,
        string registeredByUserName,
        string? cancelledByUserName,
        string? cancellationReason)
        => new(
            payment.EmployeePaymentId,
            payment.UserId,
            employeeName,
            employeeUserName,
            payment.AmountCentavos,
            payment.PaymentType,
            payment.PeriodStart,
            payment.PeriodEnd,
            payment.DaysConsidered,
            payment.Note,
            payment.RegisteredByUserId,
            registeredByUserName,
            payment.CreatedAtUtc,
            payment.Status,
            payment.CancelledByUserId,
            cancelledByUserName,
            payment.CancelledAtUtc,
            cancellationReason,
            payment.MetodoPago,
            payment.OrigenFondos,
            payment.ResponsableOrigen,
            payment.Referencia,
            payment.CajaId,
            payment.OperacionId);

    private static string FormatCentavos(long amount) => $"${amount / 100m:N2}";
}
