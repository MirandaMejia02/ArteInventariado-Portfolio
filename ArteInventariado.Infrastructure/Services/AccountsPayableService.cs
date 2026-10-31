using System.Data;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class AccountsPayableService : IAccountsPayableService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IAuditService _auditService;
    private readonly IInstallationContext? _installationContext;

    [ActivatorUtilitiesConstructor]
    public AccountsPayableService(IDbContextFactory<AppDbContext> contextFactory, ICurrentSession currentSession, IEmployeePermissionService permissionService, IAuditService auditService, IInstallationContext installationContext)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
        _installationContext = installationContext;
    }

    internal AccountsPayableService(AppDbContext context, ICurrentSession currentSession, IEmployeePermissionService permissionService, IAuditService auditService, IInstallationContext? installationContext = null)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
        _installationContext = installationContext;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la cuenta por pagar.");

    public async Task<SupplierPaymentResult> RegisterPaymentAsync(RegisterSupplierPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var result = await RegisterPaymentsAsync(new RegisterSupplierPaymentBatchRequest(
            request.OperacionId,
            request.CuentaPorPagarId,
            [new SupplierPaymentLineRequest(request.MontoCentavos, request.MetodoPago, request.Referencia, request.OrigenFondos, request.ResponsableOrigen, request.OperacionId)],
            request.Observaciones), cancellationToken);
        return result.Pagos.Single();
    }

    public async Task<SupplierPaymentBatchResult> RegisterPaymentsAsync(RegisterSupplierPaymentBatchRequest request, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.ABONO_PROVEEDOR_REGISTRAR, cancellationToken);
        _installationContext?.EnsureOperationalWriteAllowed("abono.proveedor");
        var normalized = NormalizeAndValidate(request);

        var replay = await TryGetReplayAsync(normalized, cancellationToken);
        if (replay is not null) return replay;

        var userId = GetCurrentUserId();
        try
        {
            await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            // Re-check inside the transaction so two callers that race before
            // the first commit cannot create the same batch twice.
            var transactionReplay = await TryGetReplayAsync(normalized, cancellationToken);
            if (transactionReplay is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return transactionReplay;
            }

            foreach (var line in normalized.Lines)
                await PaymentMethodAvailability.EnsureAvailableAsync(Context, line.MetodoPago, cancellationToken);

            var account = await Context.CuentasPorPagar
                .Include(c => c.Compra)
                .Include(c => c.Proveedor)
                .FirstOrDefaultAsync(c => c.CuentaPorPagarId == normalized.AccountId, cancellationToken)
                ?? throw new InvalidOperationException("La cuenta por pagar no existe.");
            if (account.Estado == EstadoCuentaPorPagar.PAGADA || account.SaldoPendienteCentavos <= 0)
                throw new InvalidOperationException("La cuenta ya esta pagada y no permite nuevos abonos.");

            var total = checked(normalized.Lines.Sum(line => line.MontoCentavos));
            if (total > account.SaldoPendienteCentavos)
                throw new InvalidOperationException("El abono no puede superar el saldo pendiente.");

            var cashLines = normalized.Lines.Where(line => SupplierPaymentFundingPolicy.AffectsPhysicalCash(line.OrigenFondos)).ToList();
            Caja? cash = null;
            if (cashLines.Count > 0)
            {
                cash = await GetOpenCashRequiredAsync(cancellationToken);
                await LockCashRowAsync(cash, cancellationToken);
                var available = await CashService.GetAvailablePhysicalCashCentavosAsync(Context, cash, cancellationToken);
                var requestedCash = checked(cashLines.Sum(line => line.MontoCentavos));
                if (requestedCash > available)
                    throw new InvalidOperationException($"El efectivo disponible en caja es {SupplierFinancialRules.FormatCentavos(available)} y no alcanza para este abono de {SupplierFinancialRules.FormatCentavos(requestedCash)}.");
            }

            var previousPurchaseState = account.Compra.EstadoFinanciero;
            var previousBalance = account.SaldoPendienteCentavos;
            var now = DateTime.UtcNow;
            var runningBalance = previousBalance;
            var payments = new List<AbonoCuentaPorPagar>(normalized.Lines.Count);
            foreach (var line in normalized.Lines)
            {
                var newBalance = checked(runningBalance - line.MontoCentavos);
                var payment = new AbonoCuentaPorPagar
                {
                    CodigoAbono = $"TMP-{Guid.NewGuid():N}"[..36],
                    OperacionId = line.OperacionLineaId,
                    OperacionLoteId = normalized.OperationId,
                    CuentaPorPagarId = account.CuentaPorPagarId,
                    UsuarioId = userId,
                    CajaId = SupplierPaymentFundingPolicy.AffectsPhysicalCash(line.OrigenFondos) ? cash!.CajaId : null,
                    MetodoPago = line.MetodoPago,
                    OrigenFondos = line.OrigenFondos,
                    ResponsableOrigen = line.ResponsableOrigen,
                    MontoCentavos = line.MontoCentavos,
                    Referencia = line.Referencia,
                    FechaAbono = now,
                    Observaciones = normalized.Observations,
                    SaldoAnteriorCentavos = runningBalance,
                    SaldoNuevoCentavos = newBalance
                };
                Context.AbonosCuentaPorPagar.Add(payment);
                payments.Add(payment);
                runningBalance = newBalance;
            }

            account.TotalPagadoCentavos += total;
            account.SaldoPendienteCentavos = runningBalance;
            account.Estado = SupplierFinancialRules.ResolveAccountState(account.TotalPagadoCentavos, runningBalance, account.FechaVencimiento, DateTime.Now);
            account.FechaPago = runningBalance == 0 ? now : null;
            account.FechaActualizacion = now;
            account.Compra.TotalPagadoCentavos += total;
            account.Compra.SaldoPendienteCentavos = runningBalance;
            account.Compra.EstadoFinanciero = SupplierFinancialRules.ResolvePurchaseState(account.Compra.TotalPagadoCentavos, runningBalance);
            account.Compra.FechaActualizacion = now;

            await Context.SaveChangesAsync(cancellationToken);
            foreach (var payment in payments) payment.CodigoAbono = $"ABP-{payment.AbonoCuentaPorPagarId:000000}";
            await Context.SaveChangesAsync(cancellationToken);
            foreach (var payment in payments) await RegisterPaymentAuditAsync(payment, account, cancellationToken);

            if (previousPurchaseState != account.Compra.EstadoFinanciero)
            {
                await _auditService.RegistrarAsync(new AuditEntryRequest(
                    userId, TipoBitacora.ESTADO_FINANCIERO_COMPRA_MODIFICADO, "compras", account.CompraId,
                    $"Compra {account.Compra.CodigoCompra}: estado financiero {previousPurchaseState} -> {account.Compra.EstadoFinanciero}."), cancellationToken);
            }
            if (account.Estado == EstadoCuentaPorPagar.PAGADA)
            {
                await _auditService.RegistrarAsync(new AuditEntryRequest(
                    userId, TipoBitacora.CUENTA_PAGAR_LIQUIDADA, "cuentas_por_pagar", account.CuentaPorPagarId,
                    $"Cuenta {account.CodigoCuenta} liquidada con el lote {normalized.OperationId:N}."), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            var userName = _currentSession.UsuarioActual?.NombreUsuario ?? string.Empty;
            return new SupplierPaymentBatchResult(
                normalized.OperationId,
                account.CuentaPorPagarId,
                payments.Select(payment => ToResult(payment, account, userName, false)).ToList(),
                previousBalance,
                runningBalance,
                false);
        }
        catch (DbUpdateException ex) when (SupplierFinancialRules.IsOperationIdUniqueConflict(ex))
        {
            Context.ChangeTracker.Clear();
            return await TryGetReplayAsync(normalized, cancellationToken)
                ?? throw new InvalidOperationException("El identificador de operacion ya fue utilizado para otro abono.", ex);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new InvalidOperationException("Otra operacion actualizo la caja. El abono no se registro; vuelva a consultar el saldo e intente nuevamente.", ex);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new InvalidOperationException("Otra operacion actualizo la caja. El abono no se registro; vuelva a consultar el saldo e intente nuevamente.", ex);
        }
    }

    private NormalizedBatch NormalizeAndValidate(RegisterSupplierPaymentBatchRequest request)
    {
        if (request.OperacionId == Guid.Empty) throw new ArgumentException("El identificador de operacion es obligatorio.", nameof(request));
        if (request.CuentaPorPagarId <= 0) throw new ArgumentException("La cuenta por pagar es obligatoria.", nameof(request));
        if (request.Pagos is null || request.Pagos.Count == 0) throw new ArgumentException("Debe indicar al menos una linea de pago.", nameof(request));

        var lines = request.Pagos.Select((line, index) =>
        {
            if (line.MontoCentavos <= 0) throw new ArgumentException("Todos los abonos deben ser mayores que cero.", nameof(request));
            var reference = SupplierFinancialRules.NormalizeOptional(line.Referencia);
            var responsible = SupplierPaymentFundingPolicy.NormalizeResponsible(line.OrigenFondos, line.ResponsableOrigen);
            SupplierPaymentFundingPolicy.Validate(line.OrigenFondos, line.MetodoPago, reference, responsible);
            return new NormalizedLine(
                line.MontoCentavos,
                line.MetodoPago,
                reference,
                line.OrigenFondos,
                responsible,
                line.OperacionLineaId ?? OperationalOperationIds.Derive(request.OperacionId, "supplier-payment-line", index));
        }).ToList();
        if (lines.Select(line => line.OperacionLineaId).Distinct().Count() != lines.Count)
            throw new ArgumentException("Cada linea debe tener un identificador de operacion unico.", nameof(request));

        return new NormalizedBatch(request.OperacionId, request.CuentaPorPagarId, SupplierFinancialRules.NormalizeOptional(request.Observaciones), lines);
    }

    private async Task<SupplierPaymentBatchResult?> TryGetReplayAsync(NormalizedBatch request, CancellationToken cancellationToken)
    {
        var existing = await Context.AbonosCuentaPorPagar
            .AsNoTracking()
            .Include(a => a.CuentaPorPagar).ThenInclude(c => c.Compra)
            .Include(a => a.CuentaPorPagar).ThenInclude(c => c.Proveedor)
            .Include(a => a.Usuario)
            .Where(a => a.OperacionLoteId == request.OperationId ||
                        (request.Lines.Count == 1 && a.OperacionId == request.Lines[0].OperacionLineaId))
            .OrderBy(a => a.AbonoCuentaPorPagarId)
            .ToListAsync(cancellationToken);
        if (existing.Count == 0) return null;
        if (existing.Count != request.Lines.Count)
            throw new InvalidOperationException("La operacion de abono fue registrada solo parcialmente y requiere revision.");

        foreach (var payment in existing)
        {
            var line = request.Lines.SingleOrDefault(item => item.OperacionLineaId == payment.OperacionId);
            if (line is null || payment.CuentaPorPagarId != request.AccountId || payment.MontoCentavos != line.MontoCentavos ||
                payment.MetodoPago != line.MetodoPago || payment.Referencia != line.Referencia ||
                payment.Observaciones != request.Observations || payment.OrigenFondos != line.OrigenFondos ||
                payment.ResponsableOrigen != line.ResponsableOrigen)
            {
                throw new InvalidOperationException("El identificador de operacion ya fue utilizado para otro abono.");
            }
        }

        var first = existing[0];
        var last = existing[^1];
        return new SupplierPaymentBatchResult(
            request.OperationId,
            request.AccountId,
            existing.Select(payment => ToResult(payment, first.CuentaPorPagar, payment.Usuario.NombreUsuario, true)).ToList(),
            existing.Min(payment => payment.SaldoAnteriorCentavos),
            last.SaldoNuevoCentavos,
            true);
    }

    private async Task<Caja> GetOpenCashRequiredAsync(CancellationToken cancellationToken) =>
        await CashRegisterStateQueries.GetCurrentOpenCashAsync(Context, cancellationToken)
        ?? throw new InvalidOperationException("No se puede registrar un abono en efectivo sin caja abierta.");

    private async Task LockCashRowAsync(Caja cash, CancellationToken cancellationToken)
    {
        // A no-op UPDATE obtains SQLite's writer lock for this cash register
        // without introducing a second balance or a new business movement.
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cajas SET caja_id = caja_id WHERE caja_id = {cash.CajaId};",
            cancellationToken);
    }

    private async Task RegisterPaymentAuditAsync(AbonoCuentaPorPagar payment, CuentaPorPagar account, CancellationToken cancellationToken)
    {
        await _auditService.RegistrarAsync(new AuditEntryRequest(
            payment.UsuarioId,
            TipoBitacora.ABONO_PROVEEDOR_REGISTRADO,
            "abonos_cuenta_por_pagar",
            payment.AbonoCuentaPorPagarId,
            $"Abono {payment.CodigoAbono} a cuenta {account.CodigoCuenta}. Compra {account.Compra.CodigoCompra}. Metodo {payment.MetodoPago}. Origen {payment.OrigenFondos?.ToString() ?? "No especificado (historico)"}. Responsable {payment.ResponsableOrigen ?? "-"}. Monto {SupplierFinancialRules.FormatCentavos(payment.MontoCentavos)}. Saldo {SupplierFinancialRules.FormatCentavos(payment.SaldoAnteriorCentavos)} -> {SupplierFinancialRules.FormatCentavos(payment.SaldoNuevoCentavos)}."), cancellationToken);
        if (payment.CajaId.HasValue)
        {
            await _auditService.RegistrarAsync(new AuditEntryRequest(
                payment.UsuarioId,
                TipoBitacora.SALIDA_EFECTIVO_PROVEEDOR,
                "abonos_cuenta_por_pagar",
                payment.AbonoCuentaPorPagarId,
                $"Salida de efectivo por abono {payment.CodigoAbono}: {SupplierFinancialRules.FormatCentavos(payment.MontoCentavos)}. Caja {payment.CajaId}."), cancellationToken);
        }
    }

    private int GetCurrentUserId() => _currentSession.UsuarioActual?.UsuarioId
        ?? throw new InvalidOperationException("Debe existir una sesion activa para registrar abonos.");

    private static SupplierPaymentResult ToResult(AbonoCuentaPorPagar payment, CuentaPorPagar account, string userName, bool replay) =>
        new(payment.AbonoCuentaPorPagarId, payment.CodigoAbono, payment.OperacionId, payment.CuentaPorPagarId,
            account.CodigoCuenta, account.CompraId, account.Compra.CodigoCompra, account.ProveedorId,
            account.Proveedor.NombreRazonSocial, userName, payment.CajaId, payment.MetodoPago,
            payment.MontoCentavos, payment.SaldoAnteriorCentavos, payment.SaldoNuevoCentavos,
            payment.Referencia, payment.Observaciones, payment.FechaAbono, replay,
            payment.OrigenFondos, payment.ResponsableOrigen);

    private sealed record NormalizedBatch(Guid OperationId, int AccountId, string? Observations, IReadOnlyList<NormalizedLine> Lines);
    private sealed record NormalizedLine(long MontoCentavos, MetodoPago MetodoPago, string? Referencia, OrigenFondos OrigenFondos, string? ResponsableOrigen, Guid OperacionLineaId);
}
