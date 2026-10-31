using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class InventarioSaldo
{
    public int InventarioSaldoId { get; set; }

    public int ProductoSkuId { get; set; }

    public ProductoSku ProductoSku { get; set; } = null!;

    public UbicacionInventario Ubicacion { get; set; }

    public int ExistenciaFisica { get; set; }

    public int Reservado { get; set; }

    public int Version { get; set; }

    public int Disponible => ExistenciaFisica - Reservado;
}
