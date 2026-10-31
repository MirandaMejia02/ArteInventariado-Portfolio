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

public class PurchasePaymentService : IPurchasePaymentService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IAuditService _auditService;
    private readonly IInstallationContext? _installationContext;

    [ActivatorUtilitiesConstructor]
    public PurchasePaymentService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        IAuditService auditService,
        IInstallationContext installationContext)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
        _installationContext = installationContext;
    }

    internal PurchasePaymentService(
        AppDbContext context,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        IAuditService auditService,
        IInstallationContext? installationContext = null)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
        _installationContext = installationContext;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para el pago de compra.");

    public async Task<PurchaseFinancialResult> RegisterInitialPaymentsAsync(
        RegisterInitialPurchasePaymentsRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PAGO_COMPRA_REGISTRAR, cancellationToken);
        _installationContext?.EnsureOperationalWriteAllowed("pago.compra");
        ValidateRequest(request);
        var normalizedObservations = SupplierFinancialRules.NormalizeOptional(request.Observaciones);
        var normalizedPayments = request.Pagos
            .Select(p => p with
            {
                Referencia = SupplierFinancialRules.NormalizeOptional(p.Referencia),
                ResponsableOrigen = SupplierPaymentFundingPolicy.NormalizeResponsible(p.OrigenFondos, p.ResponsableOrigen)
            })
            .ToList();

        foreach (var payment in normalizedPayments)
        {
            SupplierPaymentFundingPolicy.Validate(payment.OrigenFondos, payment.MetodoPago, payment.Referencia, payment.ResponsableOrigen);
        }

        var batchOperationId = request.OperacionId ?? normalizedPayments.FirstOrDefault()?.OperacionId;
        var replay = await TryGetReplayAsync(request.CompraId, normalizedPayments, normalizedObservations, batchOperationId, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var userId = GetCurrentUserId();
        try
        {
            await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            // Re-check inside the transaction to make the batch idempotency
            // safe when two callers race before the first commit.
            var transactionReplay = await TryGetReplayAsync(request.CompraId, normalizedPayments, normalizedObservations, batchOperationId, cancellationToken);
            if (transactionReplay is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return transactionReplay;
            }

            foreach (var payment in normalizedPayments)
            {
                await PaymentMethodAvailability.EnsureAvailableAsync(Context, payment.MetodoPago, cancellationToken);
            }

            var purchase = await Context.Compras
                .Include(c => c.Proveedor)
                .Include(c => c.Pagos)
                    .ThenInclude(p => p.Usuario)
                .Include(c => c.CuentaPorPagar)
                .FirstOrDefaultAsync(c => c.CompraId == request.CompraId, cancellationToken)
                ?? throw new InvalidOperationException("La compra no existe.");

            if (purchase.Pagos.Count > 0)
            {
                throw new InvalidOperationException("La compra ya tiene pagos iniciales. Use un abono para reducir el saldo restante.");
            }

            var account = purchase.CuentaPorPagar ?? await CreateAccountAsync(purchase, userId, request.FechaVencimiento, normalizedObservations, cancellationToken);
            var totalPayment = checked(normalizedPayments.Sum(p => p.MontoCentavos));
            if (totalPayment > purchase.SaldoPendienteCentavos)
            {
                throw new InvalidOperationException("Los pagos iniciales no pueden superar el saldo de la compra.");
            }

            var cashLines = normalizedPayments.Where(p => SupplierPaymentFundingPolicy.AffectsPhysicalCash(p.OrigenFondos)).ToList();
            var cash = cashLines.Count > 0
                ? await GetOpenCashRequiredAsync(cancellationToken)
                : null;
            if (cash is not null)
            {
                await LockCashRowAsync(cash, cancellationToken);
                var available = await CashService.GetAvailablePhysicalCashCentavosAsync(Context, cash, cancellationToken);
                var requestedCash = checked(cashLines.Sum(p => p.MontoCentavos));
                if (requestedCash > available)
                {
                    throw new InvalidOperationException($"El efectivo disponible en caja es {SupplierFinancialRules.FormatCentavos(available)} y no alcanza para este pago de {SupplierFinancialRules.FormatCentavos(requestedCash)}.");
                }
            }
            var now = DateTime.UtcNow;
            var payments = new List<PagoCompra>();
            foreach (var line in normalizedPayments)
            {
                var payment = new PagoCompra
                {
                    CodigoPago = $"TMP-{Guid.NewGuid():N}"[..36],
                    OperacionId = line.OperacionId,
                    OperacionLoteId = batchOperationId,
                    CompraId = purchase.CompraId,
                    UsuarioId = userId,
                    CajaId = SupplierPaymentFundingPolicy.AffectsPhysicalCash(line.OrigenFondos) ? cash!.CajaId : null,
                    MetodoPago = line.MetodoPago,
                    OrigenFondos = line.OrigenFondos,
                    ResponsableOrigen = line.ResponsableOrigen,
                    MontoCentavos = line.MontoCentavos,
                    Referencia = line.Referencia,
                    FechaPago = now,
                    Observaciones = normalizedObservations,
                    EsPagoInicial = true,
                    FechaCreacion = now
                };
                Context.PagosCompra.Add(payment);
                payments.Add(payment);
            }

            var previousState = purchase.EstadoFinanciero;
            purchase.TotalPagadoCentavos = totalPayment;
            purchase.SaldoPendienteCentavos = purchase.TotalCentavos - totalPayment;
            purchase.EstadoFinanciero = SupplierFinancialRules.ResolvePurchaseState(purchase.TotalPagadoCentavos, purchase.SaldoPendienteCentavos);
            purchase.FechaActualizacion = now;

            account.TotalOriginalCentavos = purchase.TotalCentavos;
            account.TotalPagadoCentavos = totalPayment;
            account.SaldoPendienteCentavos = purchase.SaldoPendienteCentavos;
            account.FechaVencimiento = request.FechaVencimiento.Date;
            account.Estado = SupplierFinancialRules.ResolveAccountState(account.TotalPagadoCentavos, account.SaldoPendienteCentavos, account.FechaVencimiento, DateTime.Now);
            account.FechaPago = account.SaldoPendienteCentavos == 0 ? now : null;
            account.FechaActualizacion = now;
            account.Observaciones = normalizedObservations ?? account.Observaciones;

            await Context.SaveChangesAsync(cancellationToken);
            foreach (var payment in payments)
            {
                payment.CodigoPago = $"PGC-{payment.PagoCompraId:000000}";
            }
            await Context.SaveChangesAsync(cancellationToken);

            foreach (var payment in payments)
            {
                await RegisterPaymentAuditAsync(payment, purchase, cancellationToken);
            }
            if (previousState != purchase.EstadoFinanciero)
            {
                await RegisterStateAuditAsync(purchase, previousState, cancellationToken);
            }
            if (account.Estado == EstadoCuentaPorPagar.PAGADA)
            {
                await _auditService.RegistrarAsync(new AuditEntryRequest(
                    userId,
                    TipoBitacora.CUENTA_PAGAR_LIQUIDADA,
                    "cuentas_por_pagar",
                    account.CuentaPorPagarId,
                    $"Cuenta {account.CodigoCuenta} liquidada con pagos iniciales de la compra {purchase.CodigoCompra}."), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            var userName = _currentSession.UsuarioActual?.NombreUsuario ?? string.Empty;
            return ToResult(purchase, account, payments, userName, false);
        }
        catch (DbUpdateException ex) when (SupplierFinancialRules.IsOperationIdUniqueConflict(ex))
        {
            Context.ChangeTracker.Clear();
            return await TryGetReplayAsync(request.CompraId, normalizedPayments, normalizedObservations, batchOperationId, cancellationToken)
                ?? throw new InvalidOperationException("Uno de los identificadores de operacion ya fue utilizado para otro pago.", ex);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new InvalidOperationException("Otra operacion actualizo la caja. El pago no se registro; vuelva a consultar el saldo e intente nuevamente.", ex);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new InvalidOperationException("Otra operacion actualizo la caja. El pago no se registro; vuelva a consultar el saldo e intente nuevamente.", ex);
        }
    }

    private static void ValidateRequest(RegisterInitialPurchasePaymentsRequest request)
    {
        if (request.CompraId <= 0)
        {
            throw new ArgumentException("La compra es obligatoria.", nameof(request));
        }
        if (request.FechaVencimiento == default)
        {
            throw new ArgumentException("La fecha de vencimiento es obligatoria.", nameof(request));
        }
        if (request.Pagos.GroupBy(p => p.OperacionId).Any(g => g.Key == Guid.Empty || g.Count() > 1))
        {
            throw new ArgumentException("Cada pago debe tener un identificador de operacion unico y valido.", nameof(request));
        }
        if (request.Pagos.Any(p => p.MontoCentavos <= 0))
        {
            throw new ArgumentException("Todos los pagos deben ser mayores que cero.", nameof(request));
        }
    }

    private async Task<PurchaseFinancialResult?> TryGetReplayAsync(
        int purchaseId,
        IReadOnlyList<PurchasePaymentLineRequest> requested,
        string? observations,
        Guid? batchOperationId,
        CancellationToken cancellationToken)
    {
        if (requested.Count == 0)
        {
            return null;
        }
        var operationIds = requested.Select(p => p.OperacionId).ToList();
        var existing = await Context.PagosCompra
            .AsNoTracking()
            .Include(p => p.Usuario)
            .Where(p => operationIds.Contains(p.OperacionId) ||
                        (batchOperationId.HasValue && p.OperacionLoteId == batchOperationId.Value))
            .ToListAsync(cancellationToken);
        if (existing.Count == 0)
        {
            return null;
        }
        if (existing.Count != requested.Count)
        {
            throw new InvalidOperationException("La operacion de pago fue registrada solo parcialmente y requiere revision.");
        }
        foreach (var payment in existing)
        {
            var line = requested.Single(p => p.OperacionId == payment.OperacionId);
            if (payment.CompraId != purchaseId || (payment.OperacionLoteId.HasValue && payment.OperacionLoteId != batchOperationId) || payment.MetodoPago != line.MetodoPago ||
                payment.MontoCentavos != line.MontoCentavos || payment.Referencia != line.Referencia ||
                payment.Observaciones != observations || payment.OrigenFondos != line.OrigenFondos ||
                payment.ResponsableOrigen != line.ResponsableOrigen)
            {
                throw new InvalidOperationException("El identificador de operacion ya fue utilizado para otro pago.");
            }
        }
        var purchase = await Context.Compras.AsNoTracking().Include(c => c.Proveedor).SingleAsync(c => c.CompraId == purchaseId, cancellationToken);
        var account = await Context.CuentasPorPagar.AsNoTracking().SingleAsync(c => c.CompraId == purchaseId, cancellationToken);
        return ToResult(purchase, account, existing, string.Empty, true);
    }

    private async Task<CuentaPorPagar> CreateAccountAsync(Compra purchase, int userId, DateTime dueDate, string? observations, CancellationToken cancellationToken)
    {
        var account = new CuentaPorPagar
        {
            CodigoCuenta = $"CPP-{purchase.CompraId:000000}",
            CompraId = purchase.CompraId,
            ProveedorId = purchase.ProveedorId,
            UsuarioCreadorId = userId,
            TotalOriginalCentavos = purchase.TotalCentavos,
            TotalPagadoCentavos = purchase.TotalPagadoCentavos,
            SaldoPendienteCentavos = purchase.SaldoPendienteCentavos,
            FechaCreacion = DateTime.UtcNow,
            FechaVencimiento = dueDate.Date,
            Estado = EstadoCuentaPorPagar.ABIERTA,
            FechaActualizacion = DateTime.UtcNow,
            Observaciones = observations
        };
        Context.CuentasPorPagar.Add(account);
        await Context.SaveChangesAsync(cancellationToken);
        await _auditService.RegistrarAsync(new AuditEntryRequest(
            userId, TipoBitacora.CUENTA_PAGAR_CREADA, "cuentas_por_pagar", account.CuentaPorPagarId,
            $"Cuenta {account.CodigoCuenta} creada para compra {purchase.CodigoCompra}."), cancellationToken);
        return account;
    }

    private async Task<Caja> GetOpenCashRequiredAsync(CancellationToken cancellationToken)
    {
        return await CashRegisterStateQueries.GetCurrentOpenCashAsync(Context, cancellationToken)
            ?? throw new InvalidOperationException("No se puede registrar un pago en efectivo sin caja abierta.");
    }

    private async Task LockCashRowAsync(Caja cash, CancellationToken cancellationToken)
    {
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cajas SET caja_id = caja_id WHERE caja_id = {cash.CajaId};",
            cancellationToken);
    }

    private async Task RegisterPaymentAuditAsync(PagoCompra payment, Compra purchase, CancellationToken cancellationToken)
    {
        await _auditService.RegistrarAsync(new AuditEntryRequest(
            payment.UsuarioId,
            TipoBitacora.PAGO_COMPRA_REGISTRADO,
            "pagos_compra",
            payment.PagoCompraId,
            $"Pago inicial {payment.CodigoPago} para compra {purchase.CodigoCompra}. Metodo {payment.MetodoPago}. Origen {payment.OrigenFondos?.ToString() ?? "No especificado (historico)"}. Responsable {payment.ResponsableOrigen ?? "-"}. Monto {SupplierFinancialRules.FormatCentavos(payment.MontoCentavos)}."), cancellationToken);
        if (payment.CajaId.HasValue)
        {
            await _auditService.RegistrarAsync(new AuditEntryRequest(
                payment.UsuarioId, TipoBitacora.SALIDA_EFECTIVO_PROVEEDOR, "pagos_compra", payment.PagoCompraId,
                $"Salida de efectivo por pago de compra {purchase.CodigoCompra}: {SupplierFinancialRules.FormatCentavos(payment.MontoCentavos)}. Caja {payment.CajaId}."), cancellationToken);
        }
    }

    private Task RegisterStateAuditAsync(Compra purchase, EstadoFinancieroCompra previous, CancellationToken cancellationToken) =>
        _auditService.RegistrarAsync(new AuditEntryRequest(
            GetCurrentUserId(), TipoBitacora.ESTADO_FINANCIERO_COMPRA_MODIFICADO, "compras", purchase.CompraId,
            $"Compra {purchase.CodigoCompra}: estado financiero {previous} -> {purchase.EstadoFinanciero}. Saldo {SupplierFinancialRules.FormatCentavos(purchase.SaldoPendienteCentavos)}."), cancellationToken);

    private int GetCurrentUserId() => _currentSession.UsuarioActual?.UsuarioId
        ?? throw new InvalidOperationException("Debe existir una sesion activa para registrar pagos.");

    private static PurchaseFinancialResult ToResult(
        Compra purchase,
        CuentaPorPagar account,
        IReadOnlyCollection<PagoCompra> payments,
        string userName,
        bool replay) =>
        new(purchase.CompraId, purchase.CodigoCompra, account.CuentaPorPagarId, account.CodigoCuenta,
            purchase.EstadoFinanciero, account.Estado, purchase.TotalCentavos, purchase.TotalPagadoCentavos,
            purchase.SaldoPendienteCentavos, account.FechaVencimiento,
            payments.OrderBy(p => p.FechaPago).Select(p => new PurchasePaymentSummary(
                p.PagoCompraId, p.CodigoPago, p.OperacionId, p.CompraId, purchase.CodigoCompra,
                purchase.Proveedor.NombreRazonSocial, p.Usuario?.NombreUsuario ?? userName, p.CajaId, p.MetodoPago,
                p.MontoCentavos, p.Referencia, p.FechaPago, p.Observaciones, p.EsPagoInicial,
                p.OrigenFondos, p.ResponsableOrigen)).ToList(), replay);
}
