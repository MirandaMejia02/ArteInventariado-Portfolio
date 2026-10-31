using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class ProductoProveedorConfiguration : IEntityTypeConfiguration<ProductoProveedor>
{
    public void Configure(EntityTypeBuilder<ProductoProveedor> entity)
    {
        entity.ToTable("producto_proveedor", table =>
        {
            table.HasCheckConstraint("CK_producto_proveedor_ultimo_costo_no_negativo", "ultimo_costo_centavos IS NULL OR ultimo_costo_centavos >= 0");
        });

        entity.HasKey(p => p.ProductoProveedorId);

        entity.Property(p => p.ProductoProveedorId).HasColumnName("producto_proveedor_id");
        entity.Property(p => p.ProductoId).HasColumnName("producto_id");
        entity.Property(p => p.ProveedorId).HasColumnName("proveedor_id");
        entity.Property(p => p.CodigoProductoProveedor).HasColumnName("codigo_producto_proveedor").HasMaxLength(80);
        entity.Property(p => p.EsProveedorPrincipal).HasColumnName("es_proveedor_principal").IsRequired();
        entity.Property(p => p.UltimoCostoCentavos).HasColumnName("ultimo_costo_centavos");
        entity.Property(p => p.FechaUltimaCompra).HasColumnName("fecha_ultima_compra");
        entity.Property(p => p.Activo).HasColumnName("activo").IsRequired();
        entity.Property(p => p.Notas).HasColumnName("notas").HasMaxLength(500);
        entity.Property(p => p.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();
        entity.Property(p => p.FechaActualizacion).HasColumnName("fecha_actualizacion").IsRequired();

        entity.HasIndex(p => new { p.ProductoId, p.ProveedorId }).IsUnique();
        entity.HasIndex(p => p.ProveedorId);
        entity.HasIndex(p => p.ProductoId);
        entity.HasIndex(p => new { p.ProductoId, p.EsProveedorPrincipal, p.Activo })
            .IsUnique()
            .HasFilter("es_proveedor_principal = 1 AND activo = 1");

        entity.HasOne(p => p.Producto)
            .WithMany(p => p.Proveedores)
            .HasForeignKey(p => p.ProductoId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(p => p.Proveedor)
            .WithMany(p => p.Productos)
            .HasForeignKey(p => p.ProveedorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
