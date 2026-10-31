using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public sealed class ProductoSkuAssetConfiguration : IEntityTypeConfiguration<ProductoSkuAsset>
{
    public void Configure(EntityTypeBuilder<ProductoSkuAsset> entity)
    {
        entity.ToTable("producto_sku_assets", table =>
        {
            table.HasCheckConstraint("CK_producto_sku_assets_size", "size_bytes > 0");
            table.HasCheckConstraint("CK_producto_sku_assets_dimensions", "width > 0 AND height > 0");
            table.HasCheckConstraint("CK_producto_sku_assets_order", "orden >= 0");
        });
        entity.HasKey(asset => asset.ProductoSkuAssetId);
        entity.Property(asset => asset.ProductoSkuAssetId).HasColumnName("producto_sku_asset_id");
        entity.Property(asset => asset.ProductoSkuId).HasColumnName("producto_sku_id");
        entity.Property(asset => asset.StorageKey).HasColumnName("storage_key").IsRequired();
        entity.Property(asset => asset.RutaRelativa).HasColumnName("ruta_relativa").HasMaxLength(180).IsRequired();
        entity.Property(asset => asset.NombreOriginal).HasColumnName("nombre_original").HasMaxLength(255).IsRequired();
        entity.Property(asset => asset.MimeType).HasColumnName("mime_type").HasMaxLength(40).IsRequired();
        entity.Property(asset => asset.Extension).HasColumnName("extension").HasMaxLength(8).IsRequired();
        entity.Property(asset => asset.SizeBytes).HasColumnName("size_bytes");
        entity.Property(asset => asset.Width).HasColumnName("width");
        entity.Property(asset => asset.Height).HasColumnName("height");
        entity.Property(asset => asset.Sha256).HasColumnName("sha256").HasMaxLength(64).IsRequired();
        entity.Property(asset => asset.Orden).HasColumnName("orden");
        entity.Property(asset => asset.EsPrincipal).HasColumnName("es_principal");
        entity.Property(asset => asset.FechaCreacion).HasColumnName("fecha_creacion");
        entity.HasIndex(asset => asset.StorageKey).IsUnique();
        entity.HasIndex(asset => new { asset.ProductoSkuId, asset.Orden });
        entity.HasIndex(asset => asset.ProductoSkuId).HasFilter("es_principal = 1").IsUnique();
        entity.HasOne(asset => asset.ProductoSku).WithMany(sku => sku.Assets)
            .HasForeignKey(asset => asset.ProductoSkuId).OnDelete(DeleteBehavior.Cascade);
    }
}
