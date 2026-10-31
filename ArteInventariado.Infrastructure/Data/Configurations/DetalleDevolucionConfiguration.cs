using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class DetalleDevolucionConfiguration : IEntityTypeConfiguration<DetalleDevolucion>
{
    public void Configure(EntityTypeBuilder<DetalleDevolucion> entity)
    {
        entity.ToTable("detalle_devoluciones", table =>
        {
            table.HasCheckConstraint("CK_detalle_devoluciones_cantidad_positiva", "cantidad > 0");
            table.HasCheckConstraint("CK_detalle_devoluciones_precio_no_negativo", "precio_unitario_centavos >= 0");
            table.HasCheckConstraint("CK_detalle_devoluciones_descuento_no_negativo", "descuento_proporcional_centavos >= 0");
            table.HasCheckConstraint("CK_detalle_devoluciones_subtotal_no_negativo", "subtotal_devuelto_centavos >= 0");
        });

        entity.HasKey(d => d.DetalleDevolucionId);

        entity.Property(d => d.DetalleDevolucionId).HasColumnName("detalle_devolucion_id");
        entity.Property(d => d.DevolucionId).HasColumnName("devolucion_id");
        entity.Property(d => d.DetalleVentaId).HasColumnName("detalle_venta_id");
        entity.Property(d => d.ProductoId).HasColumnName("producto_id");
        entity.Property(d => d.ProductoSkuId).HasColumnName("producto_sku_id");
        entity.Property(d => d.CodigoSkuSnapshot).HasColumnName("codigo_sku_snapshot").HasMaxLength(80);
        entity.Property(d => d.NombreVarianteSnapshot).HasColumnName("nombre_variante_snapshot").HasMaxLength(150);
        entity.Property(d => d.Cantidad).HasColumnName("cantidad").IsRequired();
        entity.Property(d => d.PrecioUnitarioCentavos).HasColumnName("precio_unitario_centavos").IsRequired();
        entity.Property(d => d.DescuentoProporcionalCentavos).HasColumnName("descuento_proporcional_centavos").IsRequired();
        entity.Property(d => d.SubtotalDevueltoCentavos).HasColumnName("subtotal_devuelto_centavos").IsRequired();
        entity.Property(d => d.CondicionProducto).HasColumnName("condicion_producto").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(d => d.ReintegraStock).HasColumnName("reintegra_stock").IsRequired();
        entity.Property(d => d.UbicacionReintegro).HasColumnName("ubicacion_reintegro").HasMaxLength(20).HasConversion<string>();
        entity.Property(d => d.MovimientoInventarioId).HasColumnName("movimiento_inventario_id");

        entity.HasIndex(d => d.DevolucionId);
        entity.HasIndex(d => d.DetalleVentaId);
        entity.HasIndex(d => d.ProductoId);
        entity.HasIndex(d => d.ProductoSkuId);
        entity.HasIndex(d => d.MovimientoInventarioId);

        entity.HasOne(d => d.Devolucion)
            .WithMany(d => d.Detalles)
            .HasForeignKey(d => d.DevolucionId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(d => d.DetalleVenta)
            .WithMany()
            .HasForeignKey(d => d.DetalleVentaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(d => d.Producto)
            .WithMany()
            .HasForeignKey(d => d.ProductoId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(d => d.ProductoSku)
            .WithMany()
            .HasForeignKey(d => d.ProductoSkuId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(d => d.MovimientoInventario)
            .WithMany()
            .HasForeignKey(d => d.MovimientoInventarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
