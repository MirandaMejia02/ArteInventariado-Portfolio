using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class CajaConfiguration : IEntityTypeConfiguration<Caja>
{
    public void Configure(EntityTypeBuilder<Caja> entity)
    {
        entity.ToTable("cajas", table =>
        {
            table.HasCheckConstraint("CK_cajas_monto_inicial_no_negativo", "monto_inicial_centavos >= 0");
            table.HasCheckConstraint("CK_cajas_monto_cierre_no_negativo", "monto_cierre_centavos >= 0");
        });

        entity.HasKey(c => c.CajaId);

        entity.Property(c => c.CajaId).HasColumnName("caja_id");
        entity.Property(c => c.FechaOperacion).HasColumnName("fecha_operacion").IsRequired();
        entity.Property(c => c.FechaApertura).HasColumnName("fecha_apertura").IsRequired();
        entity.Property(c => c.FechaCierre).HasColumnName("fecha_cierre");
        entity.Property(c => c.MontoInicialCentavos).HasColumnName("monto_inicial_centavos").IsRequired();
        entity.Property(c => c.MontoCierreCentavos).HasColumnName("monto_cierre_centavos").IsRequired();
        entity.Property(c => c.Estado).HasColumnName("estado").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(c => c.UsuarioAperturaId).HasColumnName("usuario_apertura_id");
        entity.Property(c => c.UsuarioCierreId).HasColumnName("usuario_cierre_id");
        entity.Property(c => c.Observaciones).HasColumnName("observaciones");

        entity.HasIndex(c => c.FechaOperacion);
        entity.HasIndex(c => c.Estado);
        entity.HasIndex(c => c.UsuarioAperturaId);
        entity.HasIndex(c => c.UsuarioCierreId);

        entity.HasOne(c => c.UsuarioApertura)
            .WithMany(u => u.CajasAbiertas)
            .HasForeignKey(c => c.UsuarioAperturaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(c => c.UsuarioCierre)
            .WithMany(u => u.CajasCerradas)
            .HasForeignKey(c => c.UsuarioCierreId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
