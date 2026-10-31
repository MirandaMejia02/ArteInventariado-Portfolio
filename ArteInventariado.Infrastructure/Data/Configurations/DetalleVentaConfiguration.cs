using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class DetalleVentaConfiguration : IEntityTypeConfiguration<DetalleVenta>
{
    public void Configure(EntityTypeBuilder<DetalleVenta> entity)
    {
        entity.ToTable("detalle_ventas", table =>
        {
            table.HasCheckConstraint("CK_detalle_ventas_cantidad_positiva", "cantidad > 0");
            table.HasCheckConstraint("CK_detalle_ventas_precio_unitario_no_negativo", "precio_unitario_centavos >= 0");
            table.HasCheckConstraint("CK_detalle_ventas_descuento_no_negativo", "descuento_centavos >= 0");
            table.HasCheckConstraint("CK_detalle_ventas_total_no_negativo", "total_centavos >= 0");
            table.HasCheckConstraint(
                "CK_detalle_ventas_costo_historico_consistente",
                "(costo_unitario_centavos IS NULL AND origen_costo = 'DESCONOCIDO') OR (costo_unitario_centavos >= 0 AND origen_costo <> 'DESCONOCIDO')");
            table.HasCheckConstraint(
                "CK_detalle_ventas_descuento_general_no_negativo",
                "descuento_general_proporcional_centavos IS NULL OR descuento_general_proporcional_centavos >= 0");
            table.HasCheckConstraint(
                "CK_detalle_ventas_descuento_motivo_obligatorio",
                "descuento_centavos = 0 OR length(trim(coalesce(descuento_motivo, ''))) > 0");
        });

        entity.HasKey(d => d.DetalleVentaId);

        entity.Property(d => d.DetalleVentaId).HasColumnName("detalle_venta_id");
        entity.Property(d => d.VentaId).HasColumnName("venta_id");
        entity.Property(d => d.ProductoId).HasColumnName("producto_id");
        entity.Property(d => d.ProductoSkuId).HasColumnName("producto_sku_id");
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
        entity.Property(d => d.CodigoSkuSnapshot)
            .HasColumnName("codigo_sku_snapshot")
            .HasMaxLength(50);
        entity.Property(d => d.NombreVarianteSnapshot)
            .HasColumnName("nombre_variante_snapshot")
            .HasMaxLength(150);
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
        entity.Property(d => d.DescuentoGeneralProporcionalCentavos)
            .HasColumnName("descuento_general_proporcional_centavos");
        entity.Property(d => d.DescuentoMotivo).HasColumnName("descuento_motivo").HasMaxLength(300);
        entity.Property(d => d.TotalCentavos).HasColumnName("total_centavos").IsRequired();

        entity.HasIndex(d => d.VentaId);
        entity.HasIndex(d => d.ProductoId);
        entity.HasIndex(d => d.ProductoSkuId);

        entity.HasOne(d => d.Venta)
            .WithMany(v => v.Detalles)
            .HasForeignKey(d => d.VentaId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(d => d.Producto)
            .WithMany(p => p.DetallesVenta)
            .HasForeignKey(d => d.ProductoId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(d => d.ProductoSku)
            .WithMany(sku => sku.DetallesVenta)
            .HasForeignKey(d => d.ProductoSkuId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
