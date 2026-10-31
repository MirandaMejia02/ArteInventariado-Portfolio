using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public sealed class ProductoSkuConfiguration : IEntityTypeConfiguration<ProductoSku>
{
    public void Configure(EntityTypeBuilder<ProductoSku> entity)
    {
        entity.ToTable("producto_skus", table =>
        {
            table.HasCheckConstraint(
                "CK_producto_skus_codigo_obligatorio",
                "length(trim(codigo_sku)) > 0");
            table.HasCheckConstraint(
                "CK_producto_skus_nombre_variante_no_vacio",
                "nombre_variante IS NULL OR length(trim(nombre_variante)) > 0");
            table.HasCheckConstraint(
                "CK_producto_skus_precio_override_no_negativo",
                "precio_venta_override_centavos IS NULL OR precio_venta_override_centavos >= 0");
            table.HasCheckConstraint(
                "CK_producto_skus_costo_override_no_negativo",
                "costo_override_centavos IS NULL OR costo_override_centavos >= 0");
            table.HasCheckConstraint(
                "CK_producto_skus_minimo_exhibicion_no_negativo",
                "minimo_exhibicion_override IS NULL OR minimo_exhibicion_override >= 0");
        });

        entity.HasKey(sku => sku.ProductoSkuId);

        entity.Property(sku => sku.ProductoSkuId).HasColumnName("producto_sku_id");
        entity.Property(sku => sku.ProductoId).HasColumnName("producto_id").IsRequired();
        entity.Property(sku => sku.CodigoSku).HasColumnName("codigo_sku").HasMaxLength(50).IsRequired();
        entity.Property(sku => sku.NombreVariante).HasColumnName("nombre_variante").HasMaxLength(150);
        entity.Property(sku => sku.Activo).HasColumnName("activo").HasDefaultValue(true).IsRequired();
        entity.Property(sku => sku.EsPrincipal).HasColumnName("es_principal").HasDefaultValue(false).IsRequired();
        entity.Property(sku => sku.MinimoExhibicionOverride).HasColumnName("minimo_exhibicion_override");
        entity.Property(sku => sku.PrecioVentaOverrideCentavos).HasColumnName("precio_venta_override_centavos");
        entity.Property(sku => sku.CostoOverrideCentavos).HasColumnName("costo_override_centavos");

        entity.HasIndex(sku => sku.CodigoSku).IsUnique();
        entity.HasIndex(sku => sku.ProductoId);
        entity.HasIndex(sku => sku.ProductoId)
            .HasFilter("es_principal = 1")
            .IsUnique();

        entity.HasOne(sku => sku.Producto)
            .WithMany(producto => producto.Skus)
            .HasForeignKey(sku => sku.ProductoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
