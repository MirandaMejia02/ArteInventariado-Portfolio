using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class RecepcionCompraDetalleConfiguration : IEntityTypeConfiguration<RecepcionCompraDetalle>
{
    public void Configure(EntityTypeBuilder<RecepcionCompraDetalle> entity)
    {
        entity.ToTable("recepcion_compra_detalles", table =>
        {
            table.HasCheckConstraint("CK_recepcion_compra_detalles_cantidad_positiva", "cantidad_recibida > 0");
            table.HasCheckConstraint("CK_recepcion_compra_detalles_stock_anterior_no_negativo", "stock_anterior >= 0");
            table.HasCheckConstraint("CK_recepcion_compra_detalles_stock_nuevo_no_negativo", "stock_nuevo >= 0");
            table.HasCheckConstraint("CK_recepcion_compra_detalles_costo_unitario_no_negativo", "costo_unitario_centavos >= 0");
            table.HasCheckConstraint("CK_recepcion_compra_detalles_costo_neto_no_negativo", "costo_neto_unitario_centavos IS NULL OR costo_neto_unitario_centavos >= 0");
            table.HasCheckConstraint("CK_recepcion_compra_detalles_descuento_asignado_no_negativo", "descuento_asignado_centavos IS NULL OR descuento_asignado_centavos >= 0");
            table.HasCheckConstraint("CK_recepcion_compra_detalles_costo_promedio_anterior_no_negativo", "costo_promedio_anterior_centavos >= 0");
            table.HasCheckConstraint("CK_recepcion_compra_detalles_costo_promedio_nuevo_no_negativo", "costo_promedio_nuevo_centavos >= 0");
        });

        entity.HasKey(r => r.RecepcionCompraDetalleId);

        entity.Property(r => r.RecepcionCompraDetalleId).HasColumnName("recepcion_compra_detalle_id");
        entity.Property(r => r.RecepcionCompraId).HasColumnName("recepcion_compra_id");
        entity.Property(r => r.DetalleCompraId).HasColumnName("detalle_compra_id");
        entity.Property(r => r.ProductoId).HasColumnName("producto_id");
        entity.Property(r => r.ProductoSkuId).HasColumnName("producto_sku_id");
        entity.Property(r => r.CodigoSkuSnapshot).HasColumnName("codigo_sku_snapshot").HasMaxLength(80);
        entity.Property(r => r.NombreVarianteSnapshot).HasColumnName("nombre_variante_snapshot").HasMaxLength(150);
        entity.Property(r => r.UbicacionDestino).HasColumnName("ubicacion_destino").HasMaxLength(20)
            .HasConversion<string>();
        entity.Property(r => r.CantidadRecibida).HasColumnName("cantidad_recibida").IsRequired();
        entity.Property(r => r.StockAnterior).HasColumnName("stock_anterior").IsRequired();
        entity.Property(r => r.StockNuevo).HasColumnName("stock_nuevo").IsRequired();
        entity.Property(r => r.CostoUnitarioCentavos).HasColumnName("costo_unitario_centavos").IsRequired();
        entity.Property(r => r.CostoNetoUnitarioCentavos).HasColumnName("costo_neto_unitario_centavos");
        entity.Property(r => r.DescuentoAsignadoCentavos).HasColumnName("descuento_asignado_centavos");
        entity.Property(r => r.CostoPromedioAnteriorCentavos).HasColumnName("costo_promedio_anterior_centavos").IsRequired();
        entity.Property(r => r.CostoPromedioNuevoCentavos).HasColumnName("costo_promedio_nuevo_centavos").IsRequired();
        entity.Property(r => r.MovimientoInventarioId).HasColumnName("movimiento_inventario_id").IsRequired();

        entity.HasIndex(r => r.RecepcionCompraId);
        entity.HasIndex(r => r.DetalleCompraId);
        entity.HasIndex(r => r.ProductoId);
        entity.HasIndex(r => r.ProductoSkuId);
        entity.HasIndex(r => r.MovimientoInventarioId).IsUnique();

        entity.HasOne(r => r.RecepcionCompra)
            .WithMany(c => c.Detalles)
            .HasForeignKey(r => r.RecepcionCompraId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(r => r.DetalleCompra)
            .WithMany(d => d.Recepciones)
            .HasForeignKey(r => r.DetalleCompraId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.Producto)
            .WithMany(p => p.RecepcionesCompra)
            .HasForeignKey(r => r.ProductoId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.ProductoSku)
            .WithMany()
            .HasForeignKey(r => r.ProductoSkuId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.MovimientoInventario)
            .WithOne()
            .HasForeignKey<RecepcionCompraDetalle>(r => r.MovimientoInventarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
