using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public sealed class MovimientoInventarioUbicacionConfiguration : IEntityTypeConfiguration<MovimientoInventarioUbicacion>
{
    public void Configure(EntityTypeBuilder<MovimientoInventarioUbicacion> entity)
    {
        entity.ToTable("movimientos_inventario_ubicacion", table =>
        {
            table.HasCheckConstraint("CK_movimientos_inventario_ubicacion_cantidad_positiva", "cantidad > 0");
            table.HasCheckConstraint(
                "CK_movimientos_inventario_ubicacion_transferencia_valida",
                "tipo_operacion <> 'TRANSFERENCIA' OR (ubicacion_origen IS NOT NULL AND ubicacion_destino IS NOT NULL AND ubicacion_origen <> ubicacion_destino)");
        });

        entity.HasKey(movimiento => movimiento.MovimientoInventarioUbicacionId);
        entity.Property(movimiento => movimiento.MovimientoInventarioUbicacionId).HasColumnName("movimiento_inventario_ubicacion_id");
        entity.Property(movimiento => movimiento.OperacionId).HasColumnName("operacion_id");
        entity.Property(movimiento => movimiento.ProductoSkuId).HasColumnName("producto_sku_id");
        entity.Property(movimiento => movimiento.TipoOperacion).HasColumnName("tipo_operacion").HasMaxLength(30).HasConversion<string>();
        entity.Property(movimiento => movimiento.UbicacionOrigen).HasColumnName("ubicacion_origen").HasMaxLength(20).HasConversion<string>();
        entity.Property(movimiento => movimiento.UbicacionDestino).HasColumnName("ubicacion_destino").HasMaxLength(20).HasConversion<string>();
        entity.Property(movimiento => movimiento.Cantidad).HasColumnName("cantidad");
        entity.Property(movimiento => movimiento.UsuarioId).HasColumnName("usuario_id");
        entity.Property(movimiento => movimiento.Motivo).HasColumnName("motivo").HasMaxLength(500).IsRequired();
        entity.Property(movimiento => movimiento.Nota).HasColumnName("nota").HasMaxLength(1000);
        entity.Property(movimiento => movimiento.Fecha).HasColumnName("fecha");

        entity.HasIndex(movimiento => movimiento.OperacionId).IsUnique();
        entity.HasIndex(movimiento => new { movimiento.ProductoSkuId, movimiento.Fecha });
        entity.HasOne(movimiento => movimiento.ProductoSku)
            .WithMany(sku => sku.MovimientosInventario)
            .HasForeignKey(movimiento => movimiento.ProductoSkuId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(movimiento => movimiento.Usuario)
            .WithMany()
            .HasForeignKey(movimiento => movimiento.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
