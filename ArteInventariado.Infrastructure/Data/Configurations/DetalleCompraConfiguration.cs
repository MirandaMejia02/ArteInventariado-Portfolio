using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class DetalleCompraConfiguration : IEntityTypeConfiguration<DetalleCompra>
{
    public void Configure(EntityTypeBuilder<DetalleCompra> entity)
    {
        entity.ToTable("detalle_compras", table =>
        {
            table.HasCheckConstraint("CK_detalle_compras_cantidad_comprada_positiva", "cantidad_comprada > 0");
            table.HasCheckConstraint("CK_detalle_compras_cantidad_recibida_no_negativa", "cantidad_recibida >= 0");
            table.HasCheckConstraint("CK_detalle_compras_cantidad_recibida_no_supera_comprada", "cantidad_recibida <= cantidad_comprada");
            table.HasCheckConstraint("CK_detalle_compras_costo_unitario_no_negativo", "costo_unitario_centavos >= 0");
            table.HasCheckConstraint("CK_detalle_compras_descuento_linea_no_negativo", "descuento_linea_centavos >= 0");
            table.HasCheckConstraint("CK_detalle_compras_subtotal_no_negativo", "subtotal_centavos >= 0");
        });

        entity.HasKey(d => d.DetalleCompraId);

        entity.Property(d => d.DetalleCompraId).HasColumnName("detalle_compra_id");
        entity.Property(d => d.CompraId).HasColumnName("compra_id");
        entity.Property(d => d.ProductoId).HasColumnName("producto_id");
        entity.Property(d => d.ProductoSkuId).HasColumnName("producto_sku_id");
        entity.Property(d => d.CodigoProductoSnapshot).HasColumnName("codigo_producto_snapshot").HasMaxLength(50).IsRequired();
        entity.Property(d => d.NombreProductoSnapshot).HasColumnName("nombre_producto_snapshot").HasMaxLength(150).IsRequired();
        entity.Property(d => d.CodigoSkuSnapshot).HasColumnName("codigo_sku_snapshot").HasMaxLength(80);
        entity.Property(d => d.NombreVarianteSnapshot).HasColumnName("nombre_variante_snapshot").HasMaxLength(150);
        entity.Property(d => d.CantidadComprada).HasColumnName("cantidad_comprada").IsRequired();
        entity.Property(d => d.CantidadRecibida).HasColumnName("cantidad_recibida").IsRequired();
        entity.Property(d => d.CostoUnitarioCentavos).HasColumnName("costo_unitario_centavos").IsRequired();
        entity.Property(d => d.DescuentoLineaCentavos).HasColumnName("descuento_linea_centavos").IsRequired();
        entity.Property(d => d.SubtotalCentavos).HasColumnName("subtotal_centavos").IsRequired();
        entity.Property(d => d.Observaciones).HasColumnName("observaciones").HasMaxLength(500);

        entity.HasIndex(d => d.CompraId);
        entity.HasIndex(d => d.ProductoId);
        entity.HasIndex(d => d.ProductoSkuId);
        entity.HasIndex(d => new { d.CompraId, d.ProductoSkuId }).IsUnique();

        entity.HasOne(d => d.Compra)
            .WithMany(c => c.Detalles)
            .HasForeignKey(d => d.CompraId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(d => d.Producto)
            .WithMany(p => p.DetallesCompra)
            .HasForeignKey(d => d.ProductoId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(d => d.ProductoSku)
            .WithMany()
            .HasForeignKey(d => d.ProductoSkuId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
