using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class RecepcionCompraConfiguration : IEntityTypeConfiguration<RecepcionCompra>
{
    public void Configure(EntityTypeBuilder<RecepcionCompra> entity)
    {
        entity.ToTable("recepciones_compra", table =>
        {
            table.HasCheckConstraint("CK_recepciones_compra_codigo_obligatorio", "length(trim(codigo_recepcion)) > 0");
        });

        entity.HasKey(r => r.RecepcionCompraId);

        entity.Property(r => r.RecepcionCompraId).HasColumnName("recepcion_compra_id");
        entity.Property(r => r.CodigoRecepcion).HasColumnName("codigo_recepcion").HasMaxLength(50).IsRequired();
        entity.Property(r => r.OperacionId).HasColumnName("operacion_id").HasConversion<string>().HasMaxLength(36).IsRequired();
        entity.Property(r => r.CompraId).HasColumnName("compra_id");
        entity.Property(r => r.UsuarioRecepcionId).HasColumnName("usuario_recepcion_id");
        entity.Property(r => r.FechaRecepcion).HasColumnName("fecha_recepcion").IsRequired();
        entity.Property(r => r.Observaciones).HasColumnName("observaciones").HasMaxLength(1000);

        entity.HasIndex(r => r.CodigoRecepcion).IsUnique();
        entity.HasIndex(r => r.OperacionId).IsUnique();
        entity.HasIndex(r => r.CompraId);
        entity.HasIndex(r => r.UsuarioRecepcionId);
        entity.HasIndex(r => r.FechaRecepcion);

        entity.HasOne(r => r.Compra)
            .WithMany(c => c.Recepciones)
            .HasForeignKey(r => r.CompraId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.UsuarioRecepcion)
            .WithMany(u => u.RecepcionesCompra)
            .HasForeignKey(r => r.UsuarioRecepcionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
