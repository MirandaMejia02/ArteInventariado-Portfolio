using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class ReembolsoConfiguration : IEntityTypeConfiguration<Reembolso>
{
    public void Configure(EntityTypeBuilder<Reembolso> entity)
    {
        entity.ToTable("reembolsos", table =>
        {
            table.HasCheckConstraint("CK_reembolsos_monto_positivo", "monto_centavos > 0");
            table.HasCheckConstraint("CK_reembolsos_motivo_obligatorio", "length(trim(motivo)) > 0");
        });

        entity.HasKey(r => r.ReembolsoId);

        entity.Property(r => r.ReembolsoId).HasColumnName("reembolso_id");
        entity.Property(r => r.DevolucionId).HasColumnName("devolucion_id");
        entity.Property(r => r.UsuarioId).HasColumnName("usuario_id");
        entity.Property(r => r.CajaId).HasColumnName("caja_id");
        entity.Property(r => r.MetodoReembolso).HasColumnName("metodo_reembolso").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(r => r.MontoCentavos).HasColumnName("monto_centavos").IsRequired();
        entity.Property(r => r.Motivo).HasColumnName("motivo").HasMaxLength(300).IsRequired();
        entity.Property(r => r.Referencia).HasColumnName("referencia").HasMaxLength(120);
        entity.Property(r => r.FechaReembolso).HasColumnName("fecha_reembolso").IsRequired();

        entity.HasIndex(r => r.DevolucionId);
        entity.HasIndex(r => r.UsuarioId);
        entity.HasIndex(r => r.CajaId);
        entity.HasIndex(r => r.FechaReembolso);

        entity.HasOne(r => r.Devolucion)
            .WithMany(d => d.Reembolsos)
            .HasForeignKey(r => r.DevolucionId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(r => r.Usuario)
            .WithMany()
            .HasForeignKey(r => r.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.Caja)
            .WithMany()
            .HasForeignKey(r => r.CajaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
