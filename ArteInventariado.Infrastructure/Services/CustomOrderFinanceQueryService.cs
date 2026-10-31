using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class CustomOrderFinanceQueryService : ICustomOrderFinanceQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IEmployeePermissionService _permissions;

    [ActivatorUtilitiesConstructor]
    public CustomOrderFinanceQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        IEmployeePermissionService permissions)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
    }

    internal CustomOrderFinanceQueryService(AppDbContext context, IEmployeePermissionService permissions)
    {
        _legacyContext = context;
        _permissions = permissions;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para consultar finanzas del pedido.");

    public async Task<CustomOrderFinanceSummary?> GetSummaryAsync(
        int pedidoPersonalizadoId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_FINANZAS_VER, cancellationToken);
        return await Context.PedidosPersonalizados
            .AsNoTracking()
            .Where(p => p.PedidoPersonalizadoId == pedidoPersonalizadoId)
            .Select(p => new CustomOrderFinanceSummary(
                p.PedidoPersonalizadoId,
                p.CodigoPedido,
                p.NombreDestinatarioSnapshot,
                p.EstadoFinanciero,
                p.TotalEstimadoCentavos,
                p.TotalPagadoCentavos,
                p.TotalReembolsadoCentavos,
                p.TotalPagadoCentavos - p.TotalReembolsadoCentavos,
                p.SaldoPendienteCentavos,
                p.VentaId,
                p.Venta != null ? p.Venta.NumeroVenta : null))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomOrderFinancialHistoryRow>> GetHistoryAsync(
        int pedidoPersonalizadoId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_PAGOS_HISTORIAL_VER, cancellationToken);

        var payments = await Context.PagosPedidoPersonalizado
            .AsNoTracking()
            .Where(p => p.PedidoPersonalizadoId == pedidoPersonalizadoId)
            .Select(p => new CustomOrderFinancialHistoryRow(
                p.Fecha, p.CodigoPago, p.TipoPago.ToString(), p.MetodoPago, p.Referencia,
                p.MontoCentavos, p.SaldoAnteriorCentavos, p.SaldoNuevoCentavos,
                p.Usuario.NombreUsuario, p.VentaIdAplicada,
                p.VentaIdAplicada.HasValue ? "APLICADO_A_VENTA" : "REGISTRADO",
                false, p.PagoPedidoPersonalizadoId))
            .ToListAsync(cancellationToken);

        var refunds = await Context.ReembolsosPedidoPersonalizado
            .AsNoTracking()
            .Where(r => r.PedidoPersonalizadoId == pedidoPersonalizadoId)
            .Select(r => new CustomOrderFinancialHistoryRow(
                r.Fecha, r.CodigoReembolso, "REEMBOLSO", r.MetodoPago, r.Referencia,
                r.MontoCentavos, r.SaldoAnteriorCentavos, r.SaldoNuevoCentavos,
                r.Usuario.NombreUsuario, null, "REGISTRADO", true,
                r.ReembolsoPedidoPersonalizadoId))
            .ToListAsync(cancellationToken);

        return payments.Concat(refunds)
            .OrderByDescending(row => row.Fecha)
            .ThenByDescending(row => row.Codigo)
            .ToList();
    }

    public async Task<IReadOnlyList<CustomOrderReservationResult>> GetReservationsAsync(
        int pedidoPersonalizadoId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDOS_ACCEDER, cancellationToken);
        return await Context.ReservasInventarioPedido
            .AsNoTracking()
            .Where(r => r.PedidoPersonalizadoId == pedidoPersonalizadoId)
            .OrderBy(r => r.ReservaInventarioPedidoId)
            .Select(r => new CustomOrderReservationResult(
                r.ReservaInventarioPedidoId,
                r.DetallePedidoPersonalizadoId,
                r.ProductoId,
                r.Producto.CodigoInterno,
                r.Producto.Nombre,
                r.CantidadReservada,
                r.CantidadConsumida,
                r.CantidadLiberada,
                r.Estado,
                r.FechaReserva))
            .ToListAsync(cancellationToken);
    }

    public async Task<CustomOrderPaymentDocumentData?> GetPaymentDocumentAsync(
        int pagoPedidoPersonalizadoId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_PAGOS_HISTORIAL_VER, cancellationToken);
        return await Context.PagosPedidoPersonalizado
            .AsNoTracking()
            .Where(p => p.PagoPedidoPersonalizadoId == pagoPedidoPersonalizadoId)
            .Select(p => new CustomOrderPaymentDocumentData(
                p.CodigoPago, p.PedidoPersonalizado.CodigoPedido,
                p.PedidoPersonalizado.NombreDestinatarioSnapshot, p.Fecha,
                p.TipoPago.ToString(), p.MetodoPago, p.Referencia, p.MontoCentavos,
                p.SaldoAnteriorCentavos, p.SaldoNuevoCentavos, p.Usuario.NombreUsuario,
                p.Observaciones, false))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CustomOrderPaymentDocumentData?> GetRefundDocumentAsync(
        int reembolsoPedidoPersonalizadoId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_PAGOS_HISTORIAL_VER, cancellationToken);
        return await Context.ReembolsosPedidoPersonalizado
            .AsNoTracking()
            .Where(r => r.ReembolsoPedidoPersonalizadoId == reembolsoPedidoPersonalizadoId)
            .Select(r => new CustomOrderPaymentDocumentData(
                r.CodigoReembolso, r.PedidoPersonalizado.CodigoPedido,
                r.PedidoPersonalizado.NombreDestinatarioSnapshot, r.Fecha,
                "REEMBOLSO", r.MetodoPago, r.Referencia, r.MontoCentavos,
                r.SaldoAnteriorCentavos, r.SaldoNuevoCentavos, r.Usuario.NombreUsuario,
                r.Motivo, true))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
