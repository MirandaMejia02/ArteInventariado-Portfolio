using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public sealed class AporteCajaConfiguration : IEntityTypeConfiguration<AporteCaja>
{
    public void Configure(EntityTypeBuilder<AporteCaja> entity)
    {
        entity.ToTable("aportes_caja", table =>
        {
            table.HasCheckConstraint("CK_aportes_caja_monto_positivo", "monto_centavos > 0");
            table.HasCheckConstraint("CK_aportes_caja_motivo_obligatorio", "length(trim(motivo)) > 0");
        });

        entity.HasKey(item => item.AporteCajaId);
        entity.Property(item => item.AporteCajaId).HasColumnName("aporte_caja_id");
        entity.Property(item => item.CajaId).HasColumnName("caja_id").IsRequired();
        entity.Property(item => item.UsuarioId).HasColumnName("usuario_id").IsRequired();
        entity.Property(item => item.MontoCentavos).HasColumnName("monto_centavos").IsRequired();
        entity.Property(item => item.Motivo).HasColumnName("motivo").HasMaxLength(300).IsRequired();
        entity.Property(item => item.FechaAporte).HasColumnName("fecha_aporte").IsRequired();

        entity.HasIndex(item => item.CajaId);
        entity.HasIndex(item => item.UsuarioId);
        entity.HasIndex(item => item.FechaAporte);

        entity.HasOne(item => item.Caja)
            .WithMany(cash => cash.Aportes)
            .HasForeignKey(item => item.CajaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(item => item.Usuario)
            .WithMany(user => user.AportesCaja)
            .HasForeignKey(item => item.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
