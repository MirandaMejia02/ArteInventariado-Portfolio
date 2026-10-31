using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class MermaConfiguration : IEntityTypeConfiguration<Merma>
{
    public void Configure(EntityTypeBuilder<Merma> entity)
    {
        entity.ToTable("mermas", table =>
        {
            table.HasCheckConstraint("CK_mermas_cantidad_positiva", "cantidad > 0");
            table.HasCheckConstraint("CK_mermas_motivo_obligatorio", "length(trim(motivo)) > 0");
        });

        entity.HasKey(m => m.MermaId);

        entity.Property(m => m.MermaId).HasColumnName("merma_id");
        entity.Property(m => m.DevolucionId).HasColumnName("devolucion_id");
        entity.Property(m => m.DetalleDevolucionId).HasColumnName("detalle_devolucion_id");
        entity.Property(m => m.VentaId).HasColumnName("venta_id");
        entity.Property(m => m.ProductoId).HasColumnName("producto_id");
        entity.Property(m => m.ProductoSkuId).HasColumnName("producto_sku_id");
        entity.Property(m => m.UsuarioId).HasColumnName("usuario_id");
        entity.Property(m => m.CondicionProducto).HasColumnName("condicion_producto").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(m => m.Cantidad).HasColumnName("cantidad").IsRequired();
        entity.Property(m => m.Motivo).HasColumnName("motivo").HasMaxLength(300).IsRequired();
        entity.Property(m => m.FechaMerma).HasColumnName("fecha_merma").IsRequired();

        entity.HasIndex(m => m.DevolucionId);
        entity.HasIndex(m => m.DetalleDevolucionId);
        entity.HasIndex(m => m.VentaId);
        entity.HasIndex(m => m.ProductoId);
        entity.HasIndex(m => m.ProductoSkuId);
        entity.HasIndex(m => m.UsuarioId);
        entity.HasIndex(m => m.FechaMerma);

        entity.HasOne(m => m.Devolucion)
            .WithMany(d => d.Mermas)
            .HasForeignKey(m => m.DevolucionId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(m => m.DetalleDevolucion)
            .WithMany()
            .HasForeignKey(m => m.DetalleDevolucionId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(m => m.Venta)
            .WithMany()
            .HasForeignKey(m => m.VentaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(m => m.Producto)
            .WithMany()
            .HasForeignKey(m => m.ProductoId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(m => m.ProductoSku)
            .WithMany()
            .HasForeignKey(m => m.ProductoSkuId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(m => m.Usuario)
            .WithMany()
            .HasForeignKey(m => m.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
