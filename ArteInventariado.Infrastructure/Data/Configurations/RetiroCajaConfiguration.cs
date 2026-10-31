using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class RetiroCajaConfiguration : IEntityTypeConfiguration<RetiroCaja>
{
    public void Configure(EntityTypeBuilder<RetiroCaja> entity)
    {
        entity.ToTable("retiros_caja", table =>
        {
            table.HasCheckConstraint("CK_retiros_caja_monto_positivo", "monto_centavos > 0");
            table.HasCheckConstraint("CK_retiros_caja_motivo_obligatorio", "length(trim(motivo)) > 0");
        });

        entity.HasKey(r => r.RetiroCajaId);

        entity.Property(r => r.RetiroCajaId).HasColumnName("retiro_caja_id");
        entity.Property(r => r.CajaId).HasColumnName("caja_id");
        entity.Property(r => r.UsuarioId).HasColumnName("usuario_id");
        entity.Property(r => r.MontoCentavos).HasColumnName("monto_centavos").IsRequired();
        entity.Property(r => r.Motivo).HasColumnName("motivo").HasMaxLength(300).IsRequired();
        entity.Property(r => r.FechaRetiro).HasColumnName("fecha_retiro").IsRequired();

        entity.HasIndex(r => r.CajaId);
        entity.HasIndex(r => r.UsuarioId);
        entity.HasIndex(r => r.FechaRetiro);

        entity.HasOne(r => r.Caja)
            .WithMany(c => c.Retiros)
            .HasForeignKey(r => r.CajaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.Usuario)
            .WithMany(u => u.RetirosCaja)
            .HasForeignKey(r => r.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
