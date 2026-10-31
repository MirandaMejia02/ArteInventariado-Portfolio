using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record AdjustStockRequest(
    int ProductoId,
    int StockNuevo,
    string Motivo);

public sealed record AdjustInventoryCountRequest(
    Guid OperacionId,
    int ProductoSkuId,
    UbicacionInventario Ubicacion,
    int CantidadContada,
    string Motivo,
    string ReauthenticationPassword);

public sealed record InventoryAdjustmentResult(
    Guid OperacionId,
    int ProductoSkuId,
    string CodigoSku,
    UbicacionInventario Ubicacion,
    int ExistenciaAnterior,
    int CantidadContada,
    int Diferencia,
    int Reservado,
    int DisponibleNuevo,
    int? MovimientoInventarioUbicacionId,
    DateTime Fecha);

public sealed record InventoryMovementSummary(
    int MovimientoInventarioId,
    int ProductoId,
    string CodigoInterno,
    string ProductoNombre,
    int UsuarioId,
    TipoMovimientoInventario TipoMovimiento,
    int Cantidad,
    int StockAnterior,
    int StockNuevo,
    string Motivo,
    DateTime FechaMovimiento);

public sealed record InventoryBalanceSummary(
    int ProductoSkuId,
    int ProductoId,
    string CodigoSku,
    string ProductoNombre,
    UbicacionInventario Ubicacion,
    int ExistenciaFisica,
    int Reservado,
    int Disponible,
    int Version);

public sealed record TransferInventoryRequest(
    Guid OperacionId,
    int ProductoSkuId,
    UbicacionInventario Origen,
    UbicacionInventario Destino,
    int Cantidad,
    string Motivo,
    string? Nota = null);

public sealed record RegisterInventoryWasteRequest(
    Guid OperacionId,
    int ProductoSkuId,
    UbicacionInventario Ubicacion,
    int Cantidad,
    string Motivo,
    string? Nota = null);

public sealed record SetExhibitionMinimumRequest(
    int ProductoSkuId,
    int? MinimoOverride);

public sealed record ReplenishmentSummary(
    int ProductoSkuId,
    int ProductoId,
    string CodigoSku,
    string ProductoNombre,
    int MinimoExhibicion,
    int ExistenciaFisicaTotal,
    int ReservadoTotal,
    int DisponibleTotal,
    int DisponibleExhibicion,
    int DisponibleBodega,
    EstadoDisponibilidadInventario Estado,
    EstadoAlertaReposicion? Decision,
    DateTime? PospuestaHasta,
    string? MotivoDescarte);

public sealed record PostponeReplenishmentRequest(
    int ProductoSkuId,
    DateTime PospuestaHasta);

public sealed record DismissReplenishmentRequest(
    int ProductoSkuId,
    string Motivo);

public sealed record StagnantInventoryQuery(
    int DiasSinMovimiento = 90,
    DateTime? FechaCorte = null);

public sealed record StagnantInventorySummary(
    int ProductoSkuId,
    int ProductoId,
    string CodigoSku,
    string ProductoNombre,
    int ExistenciaFisica,
    DateTime? UltimoMovimiento,
    int DiasSinMovimiento,
    bool RequiereRevision);

public sealed record InventoryReconciliationSummary(
    int ProductoId,
    int TotalCanonico,
    int ProyeccionCompatible,
    bool Coincide);
