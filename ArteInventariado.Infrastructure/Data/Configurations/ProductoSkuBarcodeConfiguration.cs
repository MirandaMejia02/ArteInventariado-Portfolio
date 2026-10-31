using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public sealed class ProductoSkuBarcodeConfiguration : IEntityTypeConfiguration<ProductoSkuBarcode>
{
    public void Configure(EntityTypeBuilder<ProductoSkuBarcode> entity)
    {
        entity.ToTable("producto_sku_barcodes", table =>
        {
            table.HasCheckConstraint(
                "CK_producto_sku_barcodes_codigo_valido",
                "length(codigo) BETWEEN 1 AND 128 AND codigo = trim(codigo)");
            table.HasCheckConstraint(
                "CK_producto_sku_barcodes_principal_activo",
                "es_principal = 0 OR activo = 1");
        });

        entity.HasKey(barcode => barcode.ProductoSkuBarcodeId);

        entity.Property(barcode => barcode.ProductoSkuBarcodeId)
            .HasColumnName("producto_sku_barcode_id");
        entity.Property(barcode => barcode.ProductoSkuId)
            .HasColumnName("producto_sku_id")
            .IsRequired();
        entity.Property(barcode => barcode.Codigo)
            .HasColumnName("codigo")
            .HasMaxLength(128)
            .UseCollation("BINARY")
            .IsRequired();
        entity.Property(barcode => barcode.Symbology)
            .HasColumnName("simbologia")
            .HasConversion<int>()
            .HasDefaultValue(BarcodeSymbology.Code128)
            .IsRequired();
        entity.Property(barcode => barcode.Activo)
            .HasColumnName("activo")
            .HasDefaultValue(true)
            .IsRequired();
        entity.Property(barcode => barcode.EsPrincipal)
            .HasColumnName("es_principal")
            .HasDefaultValue(false)
            .IsRequired();

        entity.HasIndex(barcode => barcode.Codigo).IsUnique();
        entity.HasIndex(barcode => barcode.ProductoSkuId);
        entity.HasIndex(barcode => barcode.ProductoSkuId)
            .HasFilter("es_principal = 1")
            .IsUnique();

        entity.HasOne(barcode => barcode.ProductoSku)
            .WithMany(sku => sku.Barcodes)
            .HasForeignKey(barcode => barcode.ProductoSkuId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
