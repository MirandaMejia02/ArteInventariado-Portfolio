using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class CashService : ICashService
{
    private const string ConcurrencyMessage =
        "La informacion de caja fue actualizada. Cierre y vuelva a abrir el modulo antes de intentarlo nuevamente.";

    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly INotificationService _notificationService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IInstallationContext? _installationContext;
    private readonly TimeProvider _timeProvider;

    [ActivatorUtilitiesConstructor]
    public CashService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IAuditService auditService,
        INotificationService notificationService,
        IEmployeePermissionService permissionService,
        IInstallationContext installationContext,
        TimeProvider timeProvider)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _ = auditService;
        _notificationService = notificationService;
        _permissionService = permissionService;
        _installationContext = installationContext;
        _timeProvider = timeProvider;
    }

    public CashService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IAuditService auditService,
        INotificationService notificationService,
        IEmployeePermissionService permissionService)
        : this(
            contextFactory,
            currentSession,
            auditService,
            notificationService,
            permissionService,
            installationContext: null!,
            TimeProvider.System)
    {
        _installationContext = null;
    }

    internal CashService(
        AppDbContext context,
        ICurrentSession currentSession,
        IAuditService auditService,
        INotificationService notificationService,
        IEmployeePermissionService permissionService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _ = auditService;
        _notificationService = notificationService;
        _permissionService = permissionService;
        _timeProvider = TimeProvider.System;
    }

    public async Task<CashSummary> GetTodaySummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        var fecha = Today();
        var caja = await CashRegisterStateQueries.GetCurrentOpenCashAsync(context, cancellationToken)
            ?? await GetTodayCashQuery(context).FirstOrDefaultAsync(cancellationToken);

        return caja is null
            ? EmptySummary(fecha)
            : await BuildSummaryAsync(context, caja, cancellationToken);
    }

    public async Task<CashExpectedBalanceSummary> GetTodayExpectedCashBalanceAsync(
        CancellationToken cancellationToken = default)
    {
        await _permissionService.EnsurePermissionAsync(
            PermisoEmpleado.CAJA_TOTAL_ESPERADO_VER,
            cancellationToken);
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        var today = Today();
        var sessions = await context.Cajas
            .AsNoTracking()
            .Where(cash => cash.FechaOperacion == today)
            .OrderBy(cash => cash.FechaApertura)
            .ThenBy(cash => cash.CajaId)
            .ToListAsync(cancellationToken);

        if (sessions.Count == 0)
        {
            return new CashExpectedBalanceSummary(today, 0, null, false);
        }

        var summaries = new List<CashSummary>(sessions.Count);
        foreach (var session in sessions)
        {
            summaries.Add(await BuildSummaryAsync(context, session, cancellationToken));
        }

        var daily = CashDailySummaryCalculator.SummarizeDay(today, summaries);
        return new CashExpectedBalanceSummary(
            today,
            daily.CantidadSesiones,
            daily.EfectivoEsperadoAcumuladoCentavos,
            daily.TieneCierreIntermedioSinConteo);
    }

    public async Task<CashOpeningReference?> GetPreviousDayCashOpeningReferenceAsync(
        CancellationToken cancellationToken = default)
    {
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CAJA_ABRIR, cancellationToken);
        await using var lease = await CreateContextAsync(cancellationToken);
        return await GetPreviousDayCashOpeningReferenceAsync(lease.Context, Today(), cancellationToken);
    }

    private static async Task<CashOpeningReference?> GetPreviousDayCashOpeningReferenceAsync(
        AppDbContext context,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var hasOpenCash = await context.Cajas
            .AnyAsync(cash => cash.Estado == EstadoCaja.ABIERTA, cancellationToken);
        if (hasOpenCash)
        {
            return null;
        }

        var latestClose = await context.Cajas
            .AsNoTracking()
            .Where(cash =>
                cash.FechaOperacion <= today &&
                cash.Estado == EstadoCaja.CERRADA &&
                cash.FechaCierre != null)
            .OrderByDescending(cash => cash.FechaCierre)
            .ThenByDescending(cash => cash.FechaOperacion)
            .ThenByDescending(cash => cash.CajaId)
            .Select(cash => new CashOpeningReference(
                cash.CajaId,
                cash.FechaOperacion,
                cash.MontoCierreCentavos))
            .FirstOrDefaultAsync(cancellationToken);

        if (latestClose is null)
        {
            return null;
        }

        return new CashOpeningReference(
            latestClose.CajaId,
            latestClose.FechaOperacion,
            latestClose.MontoCierreCentavos);
    }

    private static string DescribeCashOpeningReference(CashOpeningReference reference) =>
        $"el último conteo físico de caja #{reference.CajaId} del {reference.FechaOperacion:yyyy-MM-dd}";

    public async Task<IReadOnlyList<CashSummary>> GetSessionHistoryAsync(
        DateOnly desde,
        DateOnly hasta,
        CancellationToken cancellationToken = default)
    {
        if (desde > hasta)
        {
            throw new ArgumentException("La fecha inicial no puede ser posterior a la fecha final.", nameof(desde));
        }

        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CAJA_HISTORIAL_VER, cancellationToken);
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;

        var sessions = await context.Cajas
            .AsNoTracking()
            .Where(cash => cash.FechaOperacion >= desde && cash.FechaOperacion <= hasta)
            .OrderByDescending(cash => cash.FechaOperacion)
            .ThenByDescending(cash => cash.FechaApertura)
            .ThenByDescending(cash => cash.CajaId)
            .ToListAsync(cancellationToken);

        var history = new List<CashSummary>(sessions.Count);
        foreach (var session in sessions)
        {
            history.Add(await BuildSummaryAsync(context, session, cancellationToken));
        }

        return history;
    }

    public async Task<CashRecoveryStatus?> GetOpenCashRecoveryAsync(
        CancellationToken cancellationToken = default)
    {
        _ = GetCurrentUserId();
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        var openCash = CashRegisterStateQueries.OpenCash(context);
        var totalOpen = await openCash.CountAsync(cancellationToken);
        if (totalOpen == 0)
        {
            return null;
        }

        var current = await openCash.FirstAsync(cancellationToken);
        var summary = await BuildSummaryAsync(context, current, cancellationToken);
        return new CashRecoveryStatus(
            summary,
            current.FechaOperacion < Today(),
            totalOpen - 1);
    }

    public async Task<CashSummary> ContinueCashAsync(
        ContinueCashRequest request,
        CancellationToken cancellationToken = default)
    {
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CAJA_ABRIR, cancellationToken);
        EnsureOperationalWriteAllowed("Continuar caja abierta");
        ValidateOperationId(request.OperationId, nameof(request));

        var userId = GetCurrentUserId();
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var cash = await CashRegisterStateQueries.GetCurrentOpenCashAsync(context, cancellationToken);
        if (cash is null || cash.CajaId != request.CajaId)
        {
            throw new InvalidOperationException(
                "La caja seleccionada ya no es la caja abierta operacional. Actualice el estado antes de continuar.");
        }

        var summary = await BuildSummaryAsync(context, cash, cancellationToken);
        var operationToken = OperationToken(request.OperationId);
        var alreadyAudited = await context.Bitacora.AsNoTracking().AnyAsync(
            entry => entry.TipoAccion == TipoBitacora.CAJA_RECUPERADA &&
                     entry.Entidad == "cajas" &&
                     entry.EntidadId == cash.CajaId &&
                     entry.Descripcion.Contains(operationToken),
            cancellationToken);
        if (!alreadyAudited)
        {
            context.Bitacora.Add(AuditEntryFactory.Create(new AuditEntryRequest(
                userId,
                TipoBitacora.CAJA_RECUPERADA,
                "cajas",
                cash.CajaId,
                $"Caja {cash.CajaId} de {cash.FechaOperacion:yyyy-MM-dd} continuada explicitamente. {operationToken}")));
            await SaveCashChangesAsync(context, "cash-recovery-continue", cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return summary;
    }

    public async Task<CashSummary> RegularizeCashAsync(
        RegularizeCashRequest request,
        CancellationToken cancellationToken = default)
    {
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CAJA_CERRAR, cancellationToken);
        EnsureOperationalWriteAllowed("Regularizar caja abierta");
        if (request.CountedCashCentavos < 0)
        {
            throw new ArgumentException("El efectivo contado no puede ser negativo.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException("El motivo de la regularizacion es obligatorio.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("La contrasena actual es obligatoria.", nameof(request));
        }

        ValidateOperationId(request.OperationId, nameof(request));
        var userId = GetCurrentUserId();
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await ReauthenticateCurrentUserAsync(context, userId, request.Password, cancellationToken);

        var cash = await context.Cajas.FirstOrDefaultAsync(
            item => item.CajaId == request.CajaId,
            cancellationToken) ?? throw new InvalidOperationException("La caja seleccionada ya no existe.");
        var operationToken = OperationToken(request.OperationId);
        if (cash.Estado != EstadoCaja.ABIERTA)
        {
            var sameOperation = await context.Bitacora.AsNoTracking().AnyAsync(
                entry => entry.TipoAccion == TipoBitacora.CAJA_REGULARIZADA &&
                         entry.Entidad == "cajas" &&
                         entry.EntidadId == cash.CajaId &&
                         entry.Descripcion.Contains(operationToken),
                cancellationToken);
            if (!sameOperation)
            {
                throw new InvalidOperationException("La caja ya fue cerrada o regularizada por otra operacion.");
            }

            await transaction.CommitAsync(cancellationToken);
            return await BuildSummaryAsync(context, cash, cancellationToken);
        }

        var before = await BuildSummaryAsync(context, cash, cancellationToken);
        var reason = request.Reason.Trim();
        var closedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var closingUser = _currentSession.UsuarioActual?.NombreUsuario;
        cash.Estado = EstadoCaja.CERRADA;
        cash.FechaCierre = closedAt;
        cash.MontoCierreCentavos = request.CountedCashCentavos;
        cash.UsuarioCierreId = userId;
        cash.Observaciones = AppendRecoveryObservation(cash.Observaciones, reason);
        var difference = request.CountedCashCentavos - before.TotalEsperadoCentavos;

        context.Bitacora.Add(AuditEntryFactory.Create(new AuditEntryRequest(
            userId,
            TipoBitacora.CAJA_REGULARIZADA,
            "cajas",
            cash.CajaId,
            $"RECUPERACION de caja {cash.CajaId} para {cash.FechaOperacion:yyyy-MM-dd}. " +
            $"Esperado {FormatCentavos(before.TotalEsperadoCentavos)}; contado {FormatCentavos(request.CountedCashCentavos)}; " +
            $"diferencia {FormatCentavos(difference)}; motivo: {reason}. {operationToken}",
            DatosAntes: $"Estado=ABIERTA; EsperadoCentavos={before.TotalEsperadoCentavos}",
            DatosDespues: $"Estado=CERRADA; Tipo=RECUPERACION; ContadoCentavos={request.CountedCashCentavos}; DiferenciaCentavos={difference}; Motivo={reason}")));
        await SaveCashChangesAsync(context, "cash-recovery-regularize", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        try
        {
            await _notificationService.NotifyAsync(
                new CashNotificationEvent("CAJA_REGULARIZADA", cash.CajaId, "Caja regularizada tras recuperacion.", closedAt),
                cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"La caja {cash.CajaId} fue regularizada, pero la notificacion posterior fallo: {ex.Message}");
        }

        return before with
        {
            Estado = EstadoCaja.CERRADA,
            MontoCierreCentavos = request.CountedCashCentavos,
            FechaCierre = closedAt,
            UsuarioCierre = closingUser
        };
    }

    public async Task<CashSummary> RegularizeClosedCashDayAsync(
        RegularizeClosedCashDayRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_currentSession.EsAdmin)
        {
            throw new InvalidOperationException("La regularización posterior al cierre requiere ADMIN.");
        }

        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CAJA_CERRAR, cancellationToken);
        EnsureOperationalWriteAllowed("Regularizar cierre diario de caja");
        if (request.CountedCashCentavos < 0)
        {
            throw new ArgumentException("El efectivo contado no puede ser negativo.", nameof(request));
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0 || reason.Length > 500)
        {
            throw new ArgumentException("El motivo obligatorio debe tener entre 1 y 500 caracteres.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("La contraseña actual es obligatoria.", nameof(request));
        }

        ValidateOperationId(request.OperationId, nameof(request));
        var userId = GetCurrentUserId();
        var operationToken = OperationToken(request.OperationId);
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await ReauthenticateCurrentUserAsync(context, userId, request.Password, cancellationToken);

        var previousOperation = await context.Bitacora.AsNoTracking()
            .Where(entry => entry.TipoAccion == TipoBitacora.CAJA_REGULARIZADA &&
                entry.Entidad == "cajas" && entry.Descripcion.Contains(operationToken))
            .Select(entry => new { entry.EntidadId, entry.DatosDespues, entry.Descripcion })
            .SingleOrDefaultAsync(cancellationToken);
        if (previousOperation is not null)
        {
            if (!previousOperation.EntidadId.HasValue ||
                previousOperation.DatosDespues is null ||
                !previousOperation.DatosDespues.StartsWith($"Fecha={request.FechaOperacion:yyyy-MM-dd};", StringComparison.Ordinal) ||
                !previousOperation.DatosDespues.Contains($"ContadoCentavos={request.CountedCashCentavos};", StringComparison.Ordinal) ||
                !previousOperation.DatosDespues.EndsWith($"Motivo={reason}", StringComparison.Ordinal) ||
                !previousOperation.Descripcion.StartsWith(
                    $"REGULARIZACION_POST_CIERRE de {request.FechaOperacion:yyyy-MM-dd}.",
                    StringComparison.Ordinal) ||
                !previousOperation.Descripcion.EndsWith(operationToken, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("El identificador de operación ya fue utilizado para otra regularización.");
            }

            var replayedCash = await context.Cajas.FirstOrDefaultAsync(
                cash => cash.CajaId == previousOperation.EntidadId.Value,
                cancellationToken) ?? throw new InvalidOperationException("La caja regularizada ya no existe.");
            await transaction.CommitAsync(cancellationToken);
            return await BuildSummaryAsync(context, replayedCash, cancellationToken);
        }

        var latestHint = await context.Cajas
            .Where(cash => cash.FechaOperacion == request.FechaOperacion)
            .OrderByDescending(cash => cash.FechaApertura)
            .ThenByDescending(cash => cash.CajaId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No hay sesiones de caja para la fecha seleccionada.");
        await LockCashRowAsync(context, latestHint, cancellationToken);

        var sessions = await context.Cajas
            .Where(cash => cash.FechaOperacion == request.FechaOperacion)
            .OrderBy(cash => cash.FechaApertura)
            .ThenBy(cash => cash.CajaId)
            .ToListAsync(cancellationToken);
        if (sessions.Count == 0 || sessions.Any(cash => cash.Estado != EstadoCaja.CERRADA || !cash.FechaCierre.HasValue))
        {
            throw new InvalidOperationException("Para regularizar el cierre del día, primero deben estar cerradas todas sus sesiones.");
        }

        var sessionSummaries = new List<CashSummary>(sessions.Count);
        foreach (var session in sessions)
        {
            sessionSummaries.Add(await BuildSummaryAsync(context, session, cancellationToken));
        }

        var daily = CashDailySummaryCalculator.SummarizeDay(request.FechaOperacion, sessionSummaries);
        var expectedCash = daily.EfectivoEsperadoAcumuladoCentavos
            ?? throw new InvalidOperationException("No se puede calcular el saldo diario porque falta un cierre intermedio.");
        var latestCash = sessions[^1];
        var previousCount = latestCash.MontoCierreCentavos;
        latestCash.MontoCierreCentavos = request.CountedCashCentavos;
        var difference = request.CountedCashCentavos - expectedCash;
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        context.Bitacora.Add(AuditEntryFactory.Create(new AuditEntryRequest(
            userId,
            TipoBitacora.CAJA_REGULARIZADA,
            "cajas",
            latestCash.CajaId,
            $"REGULARIZACION_POST_CIERRE de {request.FechaOperacion:yyyy-MM-dd}. " +
            $"Sesiones={sessions.Count}; cierre anterior={FormatCentavos(previousCount)}; " +
            $"nuevo conteo={FormatCentavos(request.CountedCashCentavos)}; esperado={FormatCentavos(expectedCash)}; " +
            $"diferencia={FormatCentavos(difference)}; motivo: {reason}. {operationToken}",
            DatosAntes: $"Fecha={request.FechaOperacion:yyyy-MM-dd}; Sesiones={sessions.Count}; Estado=CERRADA; CierreCentavos={previousCount}",
            DatosDespues: $"Fecha={request.FechaOperacion:yyyy-MM-dd}; Sesiones={sessions.Count}; Estado=CERRADA; " +
                $"CierreCentavos={request.CountedCashCentavos}; ContadoCentavos={request.CountedCashCentavos}; " +
                $"EsperadoCentavos={expectedCash}; DiferenciaCentavos={difference}; Motivo={reason}")));
        await SaveCashChangesAsync(context, "cash-closed-day-regularize", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        try
        {
            await _notificationService.NotifyAsync(
                new CashNotificationEvent(
                    "CAJA_REGULARIZADA",
                    latestCash.CajaId,
                    "Se regularizó el conteo final de caja del día.",
                    now),
                cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"El cierre diario de caja {request.FechaOperacion:yyyy-MM-dd} fue regularizado, pero la notificación posterior falló: {ex.Message}");
        }

        return await BuildSummaryAsync(context, latestCash, cancellationToken);
    }

    public async Task<CashSummary> OpenCashAutomaticallyIfEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        var enabled = await IsAutomaticOpeningEnabledAsync(context, cancellationToken);
        if (!enabled)
        {
            var current = await CashRegisterStateQueries.GetCurrentOpenCashAsync(context, cancellationToken)
                ?? await GetTodayCashQuery(context).FirstOrDefaultAsync(cancellationToken);
            return current is null
                ? EmptySummary(Today())
                : await BuildSummaryAsync(context, current, cancellationToken);
        }

        var existing = await CashRegisterStateQueries.GetCurrentOpenCashAsync(context, cancellationToken);
        if (existing is not null)
        {
            return await BuildSummaryAsync(context, existing, cancellationToken);
        }

        // A configured automatic opening cannot verify that the prior physical cash
        // remained untouched or explain a changed count. Leave the session closed
        // so an operator can count and explicitly confirm or justify the opening.
        if (await GetPreviousDayCashOpeningReferenceAsync(context, Today(), cancellationToken) is not null)
        {
            return EmptySummary(Today());
        }

        var montoInicial = await GetDefaultInitialAmountAsync(context, cancellationToken);
        return await OpenCashAsync(
            new OpenCashRequest(montoInicial, "Apertura automatica configurada."),
            cancellationToken);
    }

    public async Task<CashSummary> OpenCashAsync(
        OpenCashRequest request,
        CancellationToken cancellationToken = default)
    {
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CAJA_ABRIR, cancellationToken);
        EnsureOperationalWriteAllowed("Abrir caja");

        var openingReason = NormalizeOptional(request.MotivoVariacionCierreAnterior);
        if (openingReason?.Length > 500)
        {
            throw new ArgumentException("El motivo de variación no puede superar 500 caracteres.", nameof(request));
        }

        if (!request.CajaCierreAnteriorId.HasValue &&
            (request.ConfirmoEfectivoIntacto || openingReason is not null))
        {
            throw new ArgumentException(
                "La confirmación o el motivo requieren una referencia de apertura vigente.",
                nameof(request));
        }

        if (request.ConfirmoEfectivoIntacto && openingReason is not null)
        {
            throw new ArgumentException(
                "No puede confirmar que el efectivo sigue intacto y registrar a la vez una variación.",
                nameof(request));
        }

        if (request.MontoInicialCentavos < 0)
        {
            throw new ArgumentException("El monto inicial no puede ser negativo.", nameof(request));
        }

        var usuarioId = GetCurrentUserId();
        var fecha = Today();
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existeCajaAbierta = await context.Cajas
            .AnyAsync(c => c.Estado == EstadoCaja.ABIERTA, cancellationToken);

        if (existeCajaAbierta)
        {
            throw new InvalidOperationException(
                "Ya existe una caja abierta. Debe continuarla o regularizarla antes de abrir otra.");
        }

        var previousClose = await GetPreviousDayCashOpeningReferenceAsync(context, fecha, cancellationToken);
        if (previousClose is null && request.CajaCierreAnteriorId.HasValue)
        {
            throw new InvalidOperationException(
                "La referencia de apertura ya no está disponible. Actualice la caja y vuelva a contar el efectivo.");
        }

        if (previousClose is not null && request.CajaCierreAnteriorId != previousClose.CajaId)
        {
            throw new InvalidOperationException(
                "La referencia de apertura cambió o no fue incluida. Actualice la caja y vuelva a contar el efectivo.");
        }

        if (previousClose is null && openingReason is not null)
        {
            throw new ArgumentException("No hay una referencia de apertura vigente para justificar una variación.", nameof(request));
        }

        var openingReferenceDescription = previousClose is null
            ? null
            : DescribeCashOpeningReference(previousClose);
        var openingDiffersFromReference = previousClose is not null &&
            request.MontoInicialCentavos != previousClose.MontoCierreCentavos;
        if (request.ConfirmoEfectivoIntacto && openingDiffersFromReference)
        {
            throw new InvalidOperationException(
                "El monto no coincide con la referencia de apertura. Quite la confirmación de efectivo intacto y justifique el nuevo conteo.");
        }

        if (openingDiffersFromReference && openingReason is null)
        {
            throw new ArgumentException(
                "Indique el motivo porque el efectivo inicial difiere de la referencia de apertura.",
                nameof(request));
        }

        if (!openingDiffersFromReference && openingReason is not null)
        {
            throw new ArgumentException(
                "Solo se registra un motivo cuando el conteo difiere de la referencia de apertura.",
                nameof(request));
        }

        var openingObservations = NormalizeOptional(request.Observaciones);
        if (previousClose is not null)
        {
            var continuityNote = openingDiffersFromReference
                ? $"[AJUSTE_APERTURA_CAJA] Referencia ({openingReferenceDescription}): {FormatCentavos(previousClose.MontoCierreCentavos)} centavos; " +
                    $"conteo de apertura: " +
                    $"{FormatCentavos(request.MontoInicialCentavos)} centavos; motivo: {openingReason}."
                : request.ConfirmoEfectivoIntacto
                    ? $"[CONTINUIDAD_CAJA] Saldo inicial reutiliza {FormatCentavos(previousClose.MontoCierreCentavos)} centavos según {openingReferenceDescription}; " +
                        "quien abre confirmó que el efectivo no se tocó."
                    : $"[CONTEO_APERTURA_CAJA] Conteo manual de apertura coincide con la referencia {FormatCentavos(previousClose.MontoCierreCentavos)} centavos ({openingReferenceDescription}).";
            openingObservations = string.IsNullOrWhiteSpace(openingObservations)
                ? continuityNote
                : $"{openingObservations}\n{continuityNote}";
        }

        var caja = new Caja
        {
            FechaOperacion = fecha,
            FechaApertura = _timeProvider.GetUtcNow().UtcDateTime,
            MontoInicialCentavos = request.MontoInicialCentavos,
            MontoCierreCentavos = 0,
            Estado = EstadoCaja.ABIERTA,
            UsuarioAperturaId = usuarioId,
            Observaciones = openingObservations
        };

        context.Cajas.Add(caja);
        await SaveCashChangesAsync(context, "cash-open-create", cancellationToken);
        context.Bitacora.Add(AuditEntryFactory.Create(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.CAJA_ABIERTA,
                "cajas",
                caja.CajaId,
                $"Caja abierta para {fecha:yyyy-MM-dd} con monto inicial {FormatCentavos(caja.MontoInicialCentavos)}." +
                (previousClose is null
                    ? string.Empty
                    : openingDiffersFromReference
                        ? $" Variación frente a la referencia de apertura ({openingReferenceDescription}): " +
                            $"{FormatCentavos(caja.MontoInicialCentavos - previousClose.MontoCierreCentavos)} centavos. Motivo: {openingReason}."
                        : request.ConfirmoEfectivoIntacto
                            ? $" Continuidad desde la referencia {openingReferenceDescription}, por {FormatCentavos(previousClose.MontoCierreCentavos)} centavos; efectivo intacto confirmado."
                            : $" Conteo manual coincide con la referencia {openingReferenceDescription}, por {FormatCentavos(previousClose.MontoCierreCentavos)} centavos."))));
        await SaveCashChangesAsync(context, "cash-open-audit", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _notificationService.NotifyAsync(
            new CashNotificationEvent("CAJA_ABIERTA", caja.CajaId, "Caja abierta.", DateTime.UtcNow),
            cancellationToken);

        return await BuildSummaryAsync(context, caja, cancellationToken);
    }

    public async Task<CashSummary> CloseCashAsync(
        CloseCashRequest request,
        CancellationToken cancellationToken = default)
    {
        var incidentReason = NormalizeOptional(request.MotivoImprevisto);
        if (incidentReason is not null && !_currentSession.EsAdmin)
        {
            throw new InvalidOperationException("Solo un ADMIN puede registrar un motivo de incidente imprevisto de caja.");
        }

        if (incidentReason?.Length > 500)
        {
            throw new ArgumentException("El motivo del incidente no puede superar 500 caracteres.", nameof(request));
        }

        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CAJA_CERRAR, cancellationToken);
        EnsureOperationalWriteAllowed("Cerrar caja");

        if (request.MontoCierreCentavos < 0)
        {
            throw new ArgumentException("El monto de cierre no puede ser negativo.", nameof(request));
        }

        var usuarioId = GetCurrentUserId();
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var caja = await CashRegisterStateQueries.GetCurrentOpenCashAsync(context, cancellationToken);

        if (caja is null)
        {
            throw new InvalidOperationException("No existe una caja abierta para cerrar.");
        }

        if (caja.FechaOperacion != Today())
        {
            throw new InvalidOperationException(
                "La caja pertenece a una fecha anterior. Utilice Regularizar / cerrar recuperacion e indique el efectivo contado y el motivo.");
        }

        var before = await BuildSummaryAsync(context, caja, cancellationToken);
        if (incidentReason is not null && request.MontoCierreCentavos == before.TotalEsperadoCentavos)
        {
            throw new ArgumentException(
                "El motivo de incidente imprevisto solo se registra cuando el efectivo contado difiere del esperado.",
                nameof(request));
        }

        caja.Estado = EstadoCaja.CERRADA;
        caja.FechaCierre = _timeProvider.GetUtcNow().UtcDateTime;
        caja.MontoCierreCentavos = request.MontoCierreCentavos;
        caja.UsuarioCierreId = usuarioId;

        var observaciones = NormalizeOptional(request.Observaciones);
        if (!string.IsNullOrWhiteSpace(observaciones))
        {
            caja.Observaciones = observaciones;
        }

        var auditDescription =
            $"Caja cerrada para {caja.FechaOperacion:yyyy-MM-dd} con monto cierre {FormatCentavos(caja.MontoCierreCentavos)}.";
        if (incidentReason is not null)
        {
            auditDescription += $" Incidente imprevisto reportado por ADMIN (solo auditoría): {incidentReason}";
        }

        context.Bitacora.Add(AuditEntryFactory.Create(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.CAJA_CERRADA,
                "cajas",
                caja.CajaId,
                auditDescription)));
        await SaveCashChangesAsync(context, "cash-close", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _notificationService.NotifyAsync(
            new CashNotificationEvent("CAJA_CERRADA", caja.CajaId, "Caja cerrada.", DateTime.UtcNow),
            cancellationToken);

        return await BuildSummaryAsync(context, caja, cancellationToken);
    }

    public async Task<CashWithdrawalSummary> RegisterWithdrawalAsync(
        RegisterWithdrawalRequest request,
        CancellationToken cancellationToken = default)
    {
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.RETIRO_REGISTRAR, cancellationToken);
        EnsureOperationalWriteAllowed("Registrar retiro de caja");

        if (request.MontoCentavos <= 0)
        {
            throw new ArgumentException("El monto del retiro debe ser mayor que cero.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Motivo))
        {
            throw new ArgumentException("El motivo del retiro es obligatorio.", nameof(request));
        }

        var usuarioId = GetCurrentUserId();
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var caja = await CashRegisterStateQueries.GetCurrentOpenCashAsync(context, cancellationToken);

        if (caja is null)
        {
            throw new InvalidOperationException("No se puede registrar retiro sin caja abierta.");
        }

        var retiro = new RetiroCaja
        {
            CajaId = caja.CajaId,
            UsuarioId = usuarioId,
            MontoCentavos = request.MontoCentavos,
            Motivo = request.Motivo.Trim(),
            FechaRetiro = DateTime.UtcNow
        };

        context.RetirosCaja.Add(retiro);
        await SaveCashChangesAsync(context, "cash-withdrawal-create", cancellationToken);
        context.Bitacora.Add(AuditEntryFactory.Create(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.RETIRO_EFECTIVO,
                "retiros_caja",
                retiro.RetiroCajaId,
                $"Retiro de caja registrado por {FormatCentavos(retiro.MontoCentavos)}. Motivo: {retiro.Motivo}")));
        await SaveCashChangesAsync(context, "cash-withdrawal-audit", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _notificationService.NotifyAsync(
            new CashNotificationEvent("RETIRO_CAJA", caja.CajaId, "Retiro de caja registrado.", DateTime.UtcNow),
            cancellationToken);

        return await GetWithdrawalSummaryRequiredAsync(context, retiro.RetiroCajaId, cancellationToken);
    }

    public async Task<CashContributionSummary> RegisterCashContributionAsync(
        RegisterCashContributionRequest request,
        CancellationToken cancellationToken = default)
    {
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CAJA_APORTE_REGISTRAR, cancellationToken);
        EnsureOperationalWriteAllowed("Registrar aporte de efectivo a caja");

        if (request.MontoCentavos <= 0)
        {
            throw new ArgumentException("El monto del aporte debe ser mayor que cero.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Motivo))
        {
            throw new ArgumentException("El motivo del aporte es obligatorio.", nameof(request));
        }

        var reason = request.Motivo.Trim();
        if (reason.Length > 300)
        {
            throw new ArgumentException("El motivo del aporte no puede superar 300 caracteres.", nameof(request));
        }

        var sessionUser = _currentSession.UsuarioActual
            ?? throw new InvalidOperationException("Debe existir una sesion activa para operar caja.");
        var userId = sessionUser.UsuarioId;
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var cash = await CashRegisterStateQueries.GetCurrentOpenCashAsync(context, cancellationToken)
            ?? throw new InvalidOperationException("No se puede registrar un aporte sin una caja abierta.");

        await LockCashRowAsync(context, cash, cancellationToken);
        var stillOpen = await CashRegisterStateQueries.GetCurrentOpenCashAsync(context, cancellationToken);
        if (stillOpen is null || stillOpen.CajaId != cash.CajaId)
        {
            throw new InvalidOperationException(ConcurrencyMessage);
        }

        var contribution = new AporteCaja
        {
            CajaId = cash.CajaId,
            UsuarioId = userId,
            MontoCentavos = request.MontoCentavos,
            Motivo = reason,
            FechaAporte = _timeProvider.GetUtcNow().UtcDateTime
        };

        context.AportesCaja.Add(contribution);
        await SaveCashChangesAsync(context, "cash-contribution-create", cancellationToken);
        context.Bitacora.Add(AuditEntryFactory.Create(
            new AuditEntryRequest(
                userId,
                TipoBitacora.APORTE_EFECTIVO_CAJA,
                "aportes_caja",
                contribution.AporteCajaId,
                $"Aporte de efectivo por {FormatCentavos(contribution.MontoCentavos)} a caja #{cash.CajaId}. Motivo: {reason}")));
        await SaveCashChangesAsync(context, "cash-contribution-audit", cancellationToken);
        var result = new CashContributionSummary(
            contribution.AporteCajaId,
            contribution.CajaId,
            contribution.UsuarioId,
            contribution.MontoCentavos,
            contribution.Motivo,
            contribution.FechaAporte,
            sessionUser.NombreUsuario);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<CashExpenseSummary> RegisterExpenseAsync(
        RegisterExpenseRequest request,
        CancellationToken cancellationToken = default)
    {
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.GASTO_REGISTRAR, cancellationToken);
        EnsureOperationalWriteAllowed("Registrar gasto de caja");

        if (request.MontoCentavos <= 0)
        {
            throw new ArgumentException("El monto del gasto debe ser mayor que cero.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Concepto))
        {
            throw new ArgumentException("El motivo o concepto del gasto es obligatorio.", nameof(request));
        }

        if (request.OperacionId == Guid.Empty)
        {
            throw new ArgumentException("El gasto requiere un identificador de operacion valido.", nameof(request));
        }

        if (!request.MetodoPago.HasValue)
        {
            throw new ArgumentException("Seleccione el metodo de pago del gasto.", nameof(request));
        }

        if (!request.OrigenFondos.HasValue)
        {
            throw new ArgumentException("Seleccione el origen de los fondos del gasto.", nameof(request));
        }

        var concept = request.Concepto.Trim();
        var reference = NormalizeOptional(request.Referencia);
        var observations = NormalizeOptional(request.Observaciones);
        var responsible = SupplierPaymentFundingPolicy.NormalizeResponsible(request.OrigenFondos.Value, request.ResponsableOrigen);
        SupplierPaymentFundingPolicy.Validate(request.OrigenFondos.Value, request.MetodoPago.Value, reference, responsible);

        var usuarioId = GetCurrentUserId();
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var replay = await FindExpenseReplayAsync(
            context,
            request,
            concept,
            observations,
            reference,
            responsible,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await PaymentMethodAvailability.EnsureAvailableAsync(context, request.MetodoPago, cancellationToken);

        if (request.CategoriaGastoId.HasValue)
        {
            var validCategory = await context.CategoriasGasto
                .AsNoTracking()
                .AnyAsync(
                    category => category.CategoriaGastoId == request.CategoriaGastoId.Value && category.Activa,
                    cancellationToken);
            if (!validCategory)
            {
                throw new InvalidOperationException("La categoria de gasto no existe o esta inactiva.");
            }
        }

        Caja? caja = null;
        if (SupplierPaymentFundingPolicy.AffectsPhysicalCash(request.OrigenFondos.Value))
        {
            caja = await CashRegisterStateQueries.GetCurrentOpenCashAsync(context, cancellationToken)
                ?? throw new InvalidOperationException("No se puede registrar un gasto de caja sin caja abierta.");
            await LockCashRowAsync(context, caja, cancellationToken);
            var availableCash = await BuildSummaryAsync(context, caja, cancellationToken);
            if (request.MontoCentavos > availableCash.TotalEsperadoCentavos)
            {
                throw new InvalidOperationException(
                    $"Efectivo insuficiente en caja. Disponible: {FormatCentavos(availableCash.TotalEsperadoCentavos)}; " +
                    $"gasto: {FormatCentavos(request.MontoCentavos)}.");
            }
        }

        var gasto = new Gasto
        {
            CajaId = caja?.CajaId,
            UsuarioId = usuarioId,
            CategoriaGastoId = request.CategoriaGastoId,
            MontoCentavos = request.MontoCentavos,
            Concepto = concept,
            Observaciones = observations,
            MetodoPago = request.MetodoPago.Value,
            Referencia = reference,
            OperacionId = request.OperacionId,
            OrigenFondos = request.OrigenFondos.Value,
            ResponsableOrigen = responsible,
            FechaGasto = _timeProvider.GetUtcNow().UtcDateTime
        };

        context.Gastos.Add(gasto);
        await SaveCashChangesAsync(context, "cash-expense-create", cancellationToken);
        context.Bitacora.Add(AuditEntryFactory.Create(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.GASTO_REGISTRADO,
                "gastos",
                gasto.GastoId,
                $"Gasto registrado por {FormatCentavos(gasto.MontoCentavos)}. Concepto: {gasto.Concepto}. " +
                $"Metodo: {gasto.MetodoPago}; origen: {gasto.OrigenFondos}; responsable/fuente: {gasto.ResponsableOrigen ?? "-"}; " +
                $"efecto en caja: {(gasto.CajaId.HasValue ? "si" : "no")}; operacion: {gasto.OperacionId:N}.")));
        await SaveCashChangesAsync(context, "cash-expense-audit", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _notificationService.NotifyAsync(
            new CashNotificationEvent(
                caja is null ? "GASTO_FUERA_DE_CAJA" : "GASTO_CAJA",
                caja?.CajaId,
                caja is null ? "Gasto registrado fuera de caja." : "Gasto registrado desde caja.",
                _timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken);

        return await GetExpenseSummaryRequiredAsync(context, gasto.GastoId, cancellationToken);
    }

    public async Task<IReadOnlyList<ExpenseCategoryOption>> GetExpenseCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.GASTO_REGISTRAR, cancellationToken);
        await using var lease = await CreateContextAsync(cancellationToken);
        return await lease.Context.CategoriasGasto.AsNoTracking()
            .Where(category => category.Activa)
            .OrderBy(category => category.Nombre)
            .Select(category => new ExpenseCategoryOption(category.CategoriaGastoId, category.Nombre))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CashWithdrawalSummary>> GetTodayWithdrawalsAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        var fecha = Today();
        var openCashId = await CashRegisterStateQueries.OpenCash(context)
            .Select(cash => (int?)cash.CajaId)
            .FirstOrDefaultAsync(cancellationToken);

        return await context.RetirosCaja
            .AsNoTracking()
            .Include(r => r.Usuario)
            .Include(r => r.Caja)
            .Where(r => openCashId.HasValue
                ? r.CajaId == openCashId.Value
                : r.Caja.FechaOperacion == fecha)
            .OrderByDescending(r => r.FechaRetiro)
            .Select(r => new CashWithdrawalSummary(
                r.RetiroCajaId,
                r.CajaId,
                r.UsuarioId,
                r.MontoCentavos,
                r.Motivo,
                r.FechaRetiro,
                r.Usuario.NombreUsuario))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CashExpenseSummary>> GetTodayExpensesAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        var fecha = Today();
        var openCashId = await CashRegisterStateQueries.OpenCash(context)
            .Select(cash => (int?)cash.CajaId)
            .FirstOrDefaultAsync(cancellationToken);
        var (startUtc, endUtc) = GetLocalDayUtcRange(fecha);

        return await context.Gastos
            .AsNoTracking()
            .Include(g => g.Usuario)
            .Include(g => g.Caja)
            .Include(g => g.CategoriaGasto)
            .Where(g => (openCashId.HasValue && g.CajaId == openCashId.Value) ||
                (!openCashId.HasValue && g.CajaId.HasValue && g.Caja!.FechaOperacion == fecha) ||
                (!g.CajaId.HasValue && g.FechaGasto >= startUtc && g.FechaGasto < endUtc))
            .OrderByDescending(g => g.FechaGasto)
            .Select(g => new CashExpenseSummary(
                g.GastoId,
                g.CajaId,
                g.UsuarioId,
                g.MontoCentavos,
                g.Concepto,
                g.FechaGasto,
                g.Usuario.NombreUsuario,
                g.Observaciones,
                g.CategoriaGasto != null ? g.CategoriaGasto.Nombre : "Sin categoría",
                g.MetodoPago,
                g.Referencia,
                g.OrigenFondos,
                g.ResponsableOrigen,
                g.CajaId.HasValue && (!g.MetodoPago.HasValue || g.MetodoPago == MetodoPago.EFECTIVO)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CashRefundSummary>> GetTodayRefundsAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        var fecha = Today();
        var openCashId = await CashRegisterStateQueries.OpenCash(context)
            .Select(cash => (int?)cash.CajaId)
            .FirstOrDefaultAsync(cancellationToken);

        return await context.Reembolsos
            .AsNoTracking()
            .Include(r => r.Usuario)
            .Include(r => r.Caja)
            .Where(r => r.CajaId.HasValue && (openCashId.HasValue
                ? r.CajaId == openCashId.Value
                : r.Caja!.FechaOperacion == fecha))
            .OrderByDescending(r => r.FechaReembolso)
            .Select(r => new CashRefundSummary(
                r.ReembolsoId,
                r.CajaId,
                r.UsuarioId,
                r.MontoCentavos,
                r.Motivo,
                r.FechaReembolso,
                r.Usuario.NombreUsuario,
                r.MetodoReembolso.ToString(),
                r.Referencia))
            .ToListAsync(cancellationToken);
    }

    private async Task<CashWithdrawalSummary> GetWithdrawalSummaryRequiredAsync(
        AppDbContext context,
        int retiroCajaId,
        CancellationToken cancellationToken)
    {
        var retiro = await context.RetirosCaja
            .AsNoTracking()
            .Include(r => r.Usuario)
            .Where(r => r.RetiroCajaId == retiroCajaId)
            .Select(r => new CashWithdrawalSummary(
                r.RetiroCajaId,
                r.CajaId,
                r.UsuarioId,
                r.MontoCentavos,
                r.Motivo,
                r.FechaRetiro,
                r.Usuario.NombreUsuario))
            .FirstOrDefaultAsync(cancellationToken);

        return retiro ?? throw new InvalidOperationException("El retiro no existe.");
    }

    private async Task<CashExpenseSummary> GetExpenseSummaryRequiredAsync(
        AppDbContext context,
        int gastoId,
        CancellationToken cancellationToken)
    {
        var gasto = await context.Gastos
            .AsNoTracking()
            .Include(g => g.Usuario)
            .Include(g => g.CategoriaGasto)
            .Where(g => g.GastoId == gastoId)
            .Select(g => new CashExpenseSummary(
                g.GastoId,
                g.CajaId,
                g.UsuarioId,
                g.MontoCentavos,
                g.Concepto,
                g.FechaGasto,
                g.Usuario.NombreUsuario,
                g.Observaciones,
                g.CategoriaGasto != null ? g.CategoriaGasto.Nombre : "Sin categoría",
                g.MetodoPago,
                g.Referencia,
                g.OrigenFondos,
                g.ResponsableOrigen,
                g.CajaId.HasValue && (!g.MetodoPago.HasValue || g.MetodoPago == MetodoPago.EFECTIVO)))
            .FirstOrDefaultAsync(cancellationToken);

        return gasto ?? throw new InvalidOperationException("El gasto no existe.");
    }

    private IQueryable<Caja> GetTodayCashQuery(AppDbContext context)
    {
        var fecha = Today();

        return context.Cajas
            .AsNoTracking()
            .Where(c => c.FechaOperacion == fecha)
            .OrderByDescending(c => c.Estado == EstadoCaja.ABIERTA)
            .ThenByDescending(c => c.FechaApertura);
    }

    internal static async Task<long> GetAvailablePhysicalCashCentavosAsync(
        AppDbContext context,
        Caja caja,
        CancellationToken cancellationToken)
    {
        var summary = await BuildSummaryAsync(context, caja, cancellationToken);
        return summary.TotalEsperadoCentavos;
    }

    internal static async Task<CashSummary> BuildSummaryAsync(
        AppDbContext context,
        Caja caja,
        CancellationToken cancellationToken)
    {
        var totalVentasEfectivo = await context.PagosVenta
            .ExcludeVoidSalePayments(context)
            .Where(p =>
                p.MetodoPago == MetodoPago.EFECTIVO &&
                p.AfectaCaja &&
                p.Venta.CajaId == caja.CajaId)
            .SumAsync(p => (long?)p.MontoCentavos, cancellationToken) ?? 0;

        var totalPagosPedidoEfectivo = await context.PagosPedidoPersonalizado
            .Where(p => p.CajaId == caja.CajaId && p.MetodoPago == MetodoPago.EFECTIVO && p.AfectaCaja)
            .SumAsync(p => (long?)p.MontoCentavos, cancellationToken) ?? 0;

        var anticiposPedidoEfectivo = await context.PagosPedidoPersonalizado
            .Where(p => p.CajaId == caja.CajaId && p.MetodoPago == MetodoPago.EFECTIVO && p.AfectaCaja && p.TipoPago == TipoPagoPedido.ANTICIPO)
            .SumAsync(p => (long?)p.MontoCentavos, cancellationToken) ?? 0;
        var abonosPedidoEfectivo = await context.PagosPedidoPersonalizado
            .Where(p => p.CajaId == caja.CajaId && p.MetodoPago == MetodoPago.EFECTIVO && p.AfectaCaja && p.TipoPago == TipoPagoPedido.ABONO)
            .SumAsync(p => (long?)p.MontoCentavos, cancellationToken) ?? 0;
        var pagosFinalesPedidoEfectivo = await context.PagosPedidoPersonalizado
            .Where(p => p.CajaId == caja.CajaId && p.MetodoPago == MetodoPago.EFECTIVO && p.AfectaCaja && p.TipoPago == TipoPagoPedido.PAGO_FINAL)
            .SumAsync(p => (long?)p.MontoCentavos, cancellationToken) ?? 0;

        var totalReembolsosPedidoEfectivo = await context.ReembolsosPedidoPersonalizado
            .Where(r => r.CajaId == caja.CajaId && r.MetodoPago == MetodoPago.EFECTIVO && r.AfectaCaja)
            .SumAsync(r => (long?)r.MontoCentavos, cancellationToken) ?? 0;

        var totalCobrosCorreccionEfectivo = await context.PagosCorreccionVenta
            .Where(p => p.CajaId == caja.CajaId && p.MetodoPago == MetodoPago.EFECTIVO)
            .SumAsync(p => (long?)p.MontoCentavos, cancellationToken) ?? 0;

        var totalAbonosEfectivo = await context.AbonosCuentaPorCobrar
            .Where(a => a.CajaId == caja.CajaId && a.MetodoPago == MetodoPago.EFECTIVO)
            .SumAsync(a => (long?)a.MontoCentavos, cancellationToken) ?? 0;

        var totalRetiros = await context.RetirosCaja
            .Where(r => r.CajaId == caja.CajaId)
            .SumAsync(r => (long?)r.MontoCentavos, cancellationToken) ?? 0;

        var totalAportesCaja = await context.AportesCaja
            .Where(contribution => contribution.CajaId == caja.CajaId)
            .SumAsync(contribution => (long?)contribution.MontoCentavos, cancellationToken) ?? 0;

        var totalGastos = await context.Gastos
            .Where(g => g.CajaId == caja.CajaId &&
                (g.MetodoPago == null || g.MetodoPago == MetodoPago.EFECTIVO))
            .SumAsync(g => (long?)g.MontoCentavos, cancellationToken) ?? 0;

        var totalReembolsos = await context.Reembolsos
            .Where(r => r.CajaId == caja.CajaId && r.MetodoReembolso == MetodoReembolso.EFECTIVO)
            .SumAsync(r => (long?)r.MontoCentavos, cancellationToken) ?? 0;

        var totalPagosCompraEfectivo = await context.PagosCompra
            // CajaId is the authoritative physical-cash effect for supplier payments.
            // The funding origin is intentionally independent from the payment method.
            .Where(p => p.CajaId == caja.CajaId)
            .SumAsync(p => (long?)p.MontoCentavos, cancellationToken) ?? 0;

        var totalAbonosProveedorEfectivo = await context.AbonosCuentaPorPagar
            .Where(a => a.CajaId == caja.CajaId)
            .SumAsync(a => (long?)a.MontoCentavos, cancellationToken) ?? 0;

        var totalPagosProveedores = totalPagosCompraEfectivo + totalAbonosProveedorEfectivo;
        var totalPagosEmpleados = await context.EmployeePayments
            .Where(payment => payment.CajaId == caja.CajaId)
            .SumAsync(payment => (long?)payment.AmountCentavos, cancellationToken) ?? 0;

        var transferenciasRecibidas = await GetElectronicIncomeAsync(context, caja.FechaOperacion, MetodoPago.TRANSFERENCIA, cancellationToken);
        var bitcoinRecibido = await GetElectronicIncomeAsync(context, caja.FechaOperacion, MetodoPago.BITCOIN, cancellationToken);
        var transferenciasPagadas = await GetElectronicOutputAsync(context, caja.FechaOperacion, MetodoPago.TRANSFERENCIA, cancellationToken);
        var bitcoinPagado = await GetElectronicOutputAsync(context, caja.FechaOperacion, MetodoPago.BITCOIN, cancellationToken);

        var usuarios = await context.Cajas
            .AsNoTracking()
            .Include(c => c.UsuarioApertura)
            .Include(c => c.UsuarioCierre)
            .FirstAsync(c => c.CajaId == caja.CajaId, cancellationToken);

        return new CashSummary(
            caja.CajaId,
            caja.FechaOperacion,
            caja.Estado,
            caja.MontoInicialCentavos,
            totalVentasEfectivo + totalCobrosCorreccionEfectivo + totalAbonosEfectivo + totalPagosPedidoEfectivo,
            totalRetiros,
            totalGastos,
            totalReembolsos + totalReembolsosPedidoEfectivo,
            caja.MontoInicialCentavos + totalVentasEfectivo + totalCobrosCorreccionEfectivo + totalAbonosEfectivo + totalPagosPedidoEfectivo + totalAportesCaja - totalRetiros - totalGastos - totalReembolsos - totalReembolsosPedidoEfectivo - totalPagosProveedores - totalPagosEmpleados,
            caja.Estado == EstadoCaja.CERRADA ? caja.MontoCierreCentavos : null,
            caja.FechaApertura,
            caja.FechaCierre,
            usuarios.UsuarioApertura.NombreUsuario,
            usuarios.UsuarioCierre?.NombreUsuario,
            totalPagosProveedores,
            totalVentasEfectivo + totalCobrosCorreccionEfectivo,
            totalAbonosEfectivo,
            anticiposPedidoEfectivo,
            abonosPedidoEfectivo,
            pagosFinalesPedidoEfectivo,
            totalReembolsos,
            totalReembolsosPedidoEfectivo,
            totalPagosCompraEfectivo,
            totalAbonosProveedorEfectivo,
            transferenciasRecibidas,
            bitcoinRecibido,
            transferenciasPagadas,
            bitcoinPagado,
            totalPagosEmpleados,
            totalAportesCaja);
    }

    private static async Task<long> GetElectronicIncomeAsync(
        AppDbContext context,
        DateOnly operationDate,
        MetodoPago method,
        CancellationToken cancellationToken)
    {
        var (startUtc, endUtc) = DayUtcRange(operationDate);
        var sales = await context.PagosVenta
            .ExcludeVoidSalePayments(context)
            .Where(p => p.FechaPago >= startUtc && p.FechaPago < endUtc && p.MetodoPago == method && p.Origen == OrigenPagoVenta.VENTA)
            .SumAsync(p => (long?)p.MontoCentavos, cancellationToken) ?? 0;
        var corrections = await context.PagosCorreccionVenta
            .Where(p => p.FechaPago >= startUtc && p.FechaPago < endUtc && p.MetodoPago == method)
            .SumAsync(p => (long?)p.MontoCentavos, cancellationToken) ?? 0;
        var customerPayments = await context.AbonosCuentaPorCobrar
            .Where(p => p.FechaAbono >= startUtc && p.FechaAbono < endUtc && p.MetodoPago == method)
            .SumAsync(p => (long?)p.MontoCentavos, cancellationToken) ?? 0;
        var orderPayments = await context.PagosPedidoPersonalizado
            .Where(p => p.Fecha >= startUtc && p.Fecha < endUtc && p.MetodoPago == method)
            .SumAsync(p => (long?)p.MontoCentavos, cancellationToken) ?? 0;
        return sales + corrections + customerPayments + orderPayments;
    }

    private static async Task<long> GetElectronicOutputAsync(
        AppDbContext context,
        DateOnly operationDate,
        MetodoPago method,
        CancellationToken cancellationToken)
    {
        var (startUtc, endUtc) = DayUtcRange(operationDate);
        var refundMethod = method == MetodoPago.TRANSFERENCIA
            ? MetodoReembolso.TRANSFERENCIA
            : MetodoReembolso.BITCOIN;
        var saleRefunds = await context.Reembolsos
            .Where(r => r.FechaReembolso >= startUtc && r.FechaReembolso < endUtc && r.MetodoReembolso == refundMethod)
            .SumAsync(r => (long?)r.MontoCentavos, cancellationToken) ?? 0;
        var orderRefunds = await context.ReembolsosPedidoPersonalizado
            .Where(r => r.Fecha >= startUtc && r.Fecha < endUtc && r.MetodoPago == method)
            .SumAsync(r => (long?)r.MontoCentavos, cancellationToken) ?? 0;
        var purchases = await context.PagosCompra
            .Where(p => p.FechaPago >= startUtc && p.FechaPago < endUtc && p.MetodoPago == method)
            .SumAsync(p => (long?)p.MontoCentavos, cancellationToken) ?? 0;
        var supplierPayments = await context.AbonosCuentaPorPagar
            .Where(p => p.FechaAbono >= startUtc && p.FechaAbono < endUtc && p.MetodoPago == method)
            .SumAsync(p => (long?)p.MontoCentavos, cancellationToken) ?? 0;
        var expenses = await context.Gastos
            .Where(g => g.FechaGasto >= startUtc && g.FechaGasto < endUtc && g.MetodoPago == method)
            .SumAsync(g => (long?)g.MontoCentavos, cancellationToken) ?? 0;
        var employeePayments = await context.EmployeePayments
            .Where(payment => payment.CreatedAtUtc >= startUtc && payment.CreatedAtUtc < endUtc && payment.MetodoPago == method)
            .SumAsync(payment => (long?)payment.AmountCentavos, cancellationToken) ?? 0;
        return saleRefunds + orderRefunds + purchases + supplierPayments + expenses + employeePayments;
    }

    private static (DateTime StartUtc, DateTime EndUtc) DayUtcRange(DateOnly date)
    {
        var startLocal = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Local);
        return (startLocal.ToUniversalTime(), startLocal.AddDays(1).ToUniversalTime());
    }

    private static async Task<bool> IsAutomaticOpeningEnabledAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var value = await GetConfigurationValueAsync(context, "caja.apertura_automatica", cancellationToken);
        return bool.TryParse(value, out var enabled) && enabled;
    }

    private static async Task<long> GetDefaultInitialAmountAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var value = await GetConfigurationValueAsync(context, "caja.monto_inicial_centavos", cancellationToken);
        return long.TryParse(value, out var amount) && amount > 0 ? amount : 0;
    }

    private static async Task<string?> GetConfigurationValueAsync(
        AppDbContext context,
        string clave,
        CancellationToken cancellationToken)
    {
        return await context.ConfiguracionSistema
            .AsNoTracking()
            .Where(c => c.Clave == clave)
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private int GetCurrentUserId()
    {
        return _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa para operar caja.");
    }

    private ValueTask<AppDbContextLease> CreateContextAsync(CancellationToken cancellationToken) =>
        AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);

    private static Task SaveCashChangesAsync(
        AppDbContext context,
        string operation,
        CancellationToken cancellationToken) =>
        PersistenceGuard.SaveChangesAsync(context, operation, ConcurrencyMessage, cancellationToken);

    private static Task LockCashRowAsync(AppDbContext context, Caja cash, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cajas SET caja_id = caja_id WHERE caja_id = {cash.CajaId};",
            cancellationToken);

    private async Task<CashExpenseSummary?> FindExpenseReplayAsync(
        AppDbContext context,
        RegisterExpenseRequest request,
        string concept,
        string? observations,
        string? reference,
        string? responsible,
        CancellationToken cancellationToken)
    {
        var existing = await context.Gastos.AsNoTracking()
            .Where(expense => expense.OperacionId == request.OperacionId)
            .Select(expense => new
            {
                expense.GastoId,
                expense.MontoCentavos,
                expense.Concepto,
                expense.Observaciones,
                expense.CategoriaGastoId,
                expense.MetodoPago,
                expense.Referencia,
                expense.OrigenFondos,
                expense.ResponsableOrigen
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            return null;
        }

        if (existing.MontoCentavos != request.MontoCentavos || existing.Concepto != concept ||
            existing.Observaciones != observations || existing.CategoriaGastoId != request.CategoriaGastoId ||
            existing.MetodoPago != request.MetodoPago || existing.Referencia != reference ||
            existing.OrigenFondos != request.OrigenFondos || existing.ResponsableOrigen != responsible)
        {
            throw new InvalidOperationException("El identificador de operacion ya fue utilizado para otro gasto.");
        }

        return await GetExpenseSummaryRequiredAsync(context, existing.GastoId, cancellationToken);
    }

    private (DateTime StartUtc, DateTime EndUtc) GetLocalDayUtcRange(DateOnly date)
    {
        var localStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (
            TimeZoneInfo.ConvertTimeToUtc(localStart, _timeProvider.LocalTimeZone),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, _timeProvider.LocalTimeZone));
    }

    private DateOnly Today() => DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

    private static CashSummary EmptySummary(DateOnly fecha)
    {
        return new CashSummary(null, fecha, null, 0, 0, 0, 0, 0, 0, null, null, null, null, null);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string FormatCentavos(long centavos)
    {
        return (centavos / 100m).ToString("0.00");
    }

    private void EnsureOperationalWriteAllowed(string operation) =>
        _installationContext?.EnsureOperationalWriteAllowed(operation);

    private static void ValidateOperationId(Guid operationId, string parameterName)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("La operacion de recuperacion requiere un identificador valido.", parameterName);
        }
    }

    private static string OperationToken(Guid operationId) => $"Operacion={operationId:N}";

    private static string AppendRecoveryObservation(string? current, string reason)
    {
        var recovery = $"[RECUPERACION] {reason}";
        return string.IsNullOrWhiteSpace(current)
            ? recovery
            : $"{current.Trim()}{Environment.NewLine}{recovery}";
    }

    private static async Task ReauthenticateCurrentUserAsync(
        AppDbContext context,
        int userId,
        string password,
        CancellationToken cancellationToken)
    {
        var hash = await context.Usuarios
            .AsNoTracking()
            .Where(user => user.UsuarioId == userId && user.Activo)
            .Select(user => user.PasswordHash)
            .SingleOrDefaultAsync(cancellationToken);
        if (hash is null || !BCrypt.Net.BCrypt.Verify(password, hash))
        {
            throw new InvalidOperationException("La contrasena actual no es correcta.");
        }
    }
}
