using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class DetalleCambioVentaConfiguration : IEntityTypeConfiguration<DetalleCambioVenta>
{
    public void Configure(EntityTypeBuilder<DetalleCambioVenta> entity)
    {
        entity.ToTable("detalle_cambios_venta", table =>
        {
            table.HasCheckConstraint("CK_detalle_cambios_venta_cantidad_positiva", "cantidad > 0");
            table.HasCheckConstraint("CK_detalle_cambios_venta_precio_no_negativo", "precio_unitario_centavos >= 0");
            table.HasCheckConstraint("CK_detalle_cambios_venta_subtotal_no_negativo", "subtotal_centavos >= 0");
            table.HasCheckConstraint("CK_detalle_cambios_venta_descuento_no_negativo", "descuento_centavos >= 0");
            table.HasCheckConstraint("CK_detalle_cambios_venta_total_no_negativo", "total_centavos >= 0");
            table.HasCheckConstraint(
                "CK_detalle_cambios_venta_costo_historico_consistente",
                "(costo_unitario_centavos IS NULL AND origen_costo = 'DESCONOCIDO') OR (costo_unitario_centavos >= 0 AND origen_costo <> 'DESCONOCIDO')");
        });

        entity.HasKey(d => d.DetalleCambioVentaId);

        entity.Property(d => d.DetalleCambioVentaId).HasColumnName("detalle_cambio_venta_id");
        entity.Property(d => d.DevolucionId).HasColumnName("devolucion_id");
        entity.Property(d => d.DetalleVentaOrigenId).HasColumnName("detalle_venta_origen_id");
        entity.Property(d => d.ProductoNuevoId).HasColumnName("producto_nuevo_id");
        entity.Property(d => d.ProductoSkuNuevoId).HasColumnName("producto_sku_nuevo_id");
        entity.Property(d => d.Cantidad).HasColumnName("cantidad").IsRequired();
        entity.Property(d => d.PrecioUnitarioCentavos).HasColumnName("precio_unitario_centavos").IsRequired();
        entity.Property(d => d.CostoUnitarioCentavos).HasColumnName("costo_unitario_centavos");
        entity.Property(d => d.OrigenCosto)
            .HasColumnName("origen_costo")
            .HasMaxLength(30)
            .HasConversion<string>()
            .HasDefaultValue(OrigenCostoHistorico.DESCONOCIDO)
            .IsRequired();
        entity.Property(d => d.ProductoCodigoSnapshot)
            .HasColumnName("producto_codigo_snapshot")
            .HasMaxLength(80);
        entity.Property(d => d.ProductoNombreSnapshot)
            .HasColumnName("producto_nombre_snapshot")
            .HasMaxLength(200);
        entity.Property(d => d.CodigoSkuSnapshot).HasColumnName("codigo_sku_snapshot").HasMaxLength(80);
        entity.Property(d => d.NombreVarianteSnapshot).HasColumnName("nombre_variante_snapshot").HasMaxLength(150);
        entity.Property(d => d.CategoriaIdSnapshot).HasColumnName("categoria_id_snapshot");
        entity.Property(d => d.CategoriaNombreSnapshot)
            .HasColumnName("categoria_nombre_snapshot")
            .HasMaxLength(150);
        entity.Property(d => d.SubcategoriaIdSnapshot)
            .HasColumnName("subcategoria_id_snapshot");
        entity.Property(d => d.SubcategoriaNombreSnapshot)
            .HasColumnName("subcategoria_nombre_snapshot")
            .HasMaxLength(120);
        entity.Property(d => d.SubtotalCentavos).HasColumnName("subtotal_centavos").IsRequired();
        entity.Property(d => d.DescuentoCentavos).HasColumnName("descuento_centavos").IsRequired();
        entity.Property(d => d.TotalCentavos).HasColumnName("total_centavos").IsRequired();
        entity.Property(d => d.MovimientoInventarioId).HasColumnName("movimiento_inventario_id");

        entity.HasIndex(d => d.DevolucionId);
        entity.HasIndex(d => d.DetalleVentaOrigenId);
        entity.HasIndex(d => d.ProductoNuevoId);
        entity.HasIndex(d => d.ProductoSkuNuevoId);
        entity.HasIndex(d => d.MovimientoInventarioId);

        entity.HasOne(d => d.Devolucion)
            .WithMany(d => d.DetallesCambio)
            .HasForeignKey(d => d.DevolucionId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(d => d.DetalleVentaOrigen)
            .WithMany()
            .HasForeignKey(d => d.DetalleVentaOrigenId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(d => d.ProductoNuevo)
            .WithMany()
            .HasForeignKey(d => d.ProductoNuevoId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(d => d.ProductoSkuNuevo)
            .WithMany()
            .HasForeignKey(d => d.ProductoSkuNuevoId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(d => d.MovimientoInventario)
            .WithMany()
            .HasForeignKey(d => d.MovimientoInventarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
