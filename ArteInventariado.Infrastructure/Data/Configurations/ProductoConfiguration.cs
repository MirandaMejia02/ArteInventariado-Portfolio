using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class ProductoConfiguration : IEntityTypeConfiguration<Producto>
{
    public void Configure(EntityTypeBuilder<Producto> entity)
    {
        entity.ToTable("productos", table =>
        {
            table.HasCheckConstraint("CK_productos_precio_compra_no_negativo", "precio_compra_centavos >= 0");
            table.HasCheckConstraint("CK_productos_precio_venta_no_negativo", "precio_venta_centavos >= 0");
            table.HasCheckConstraint("CK_productos_stock_actual_no_negativo", "stock_actual >= 0");
            table.HasCheckConstraint("CK_productos_stock_minimo_no_negativo", "stock_minimo >= 0");
        });

        entity.HasKey(p => p.ProductoId);

        entity.Property(p => p.ProductoId).HasColumnName("producto_id");
        entity.Property(p => p.CodigoInterno).HasColumnName("codigo_interno").HasMaxLength(50).IsRequired();
        entity.Property(p => p.Modo)
            .HasColumnName("modo")
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(ModoProducto.SIMPLE)
            .IsRequired();
        entity.Property(p => p.Nombre).HasColumnName("nombre").HasMaxLength(150).IsRequired();
        entity.Property(p => p.Descripcion).HasColumnName("descripcion");
        entity.Property(p => p.CategoriaId).HasColumnName("categoria_id");
        entity.Property(p => p.SubcategoriaId).HasColumnName("subcategoria_id");
        entity.Property(p => p.ImagenLocalPath).HasColumnName("imagen_local_path").HasMaxLength(500);
        entity.Property(p => p.PrecioCompraCentavos).HasColumnName("precio_compra_centavos").IsRequired();
        entity.Property(p => p.PrecioVentaCentavos).HasColumnName("precio_venta_centavos").IsRequired();
        entity.Property(p => p.StockActual).HasColumnName("stock_actual").IsRequired();
        entity.Property(p => p.StockMinimo).HasColumnName("stock_minimo").IsRequired();
        entity.Property(p => p.Estado).HasColumnName("estado").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(p => p.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();
        entity.Property(p => p.FechaModificacion).HasColumnName("fecha_modificacion");

        entity.HasIndex(p => p.CodigoInterno).IsUnique();
        entity.HasIndex(p => p.Nombre);
        entity.HasIndex(p => p.CategoriaId);
        entity.HasIndex(p => p.SubcategoriaId);
        entity.HasIndex(p => p.Estado);

        entity.HasOne(p => p.Categoria)
            .WithMany(c => c.Productos)
            .HasForeignKey(p => p.CategoriaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(p => p.Subcategoria)
            .WithMany(subcategory => subcategory.Productos)
            .HasForeignKey(p => p.SubcategoriaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
