using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class BitacoraConfiguration : IEntityTypeConfiguration<Bitacora>
{
    public void Configure(EntityTypeBuilder<Bitacora> entity)
    {
        entity.ToTable("bitacora");

        entity.HasKey(b => b.BitacoraId);

        entity.Property(b => b.BitacoraId).HasColumnName("bitacora_id");
        entity.Property(b => b.UsuarioId).HasColumnName("usuario_id");
        entity.Property(b => b.TipoAccion).HasColumnName("tipo_accion").HasMaxLength(40).HasConversion<string>().IsRequired();
        entity.Property(b => b.Entidad).HasColumnName("entidad").HasMaxLength(80).IsRequired();
        entity.Property(b => b.EntidadId).HasColumnName("entidad_id");
        entity.Property(b => b.Descripcion).HasColumnName("descripcion").IsRequired();
        entity.Property(b => b.DatosAntes).HasColumnName("datos_antes");
        entity.Property(b => b.DatosDespues).HasColumnName("datos_despues");
        entity.Property(b => b.FechaAccion).HasColumnName("fecha_accion").IsRequired();

        entity.HasIndex(b => b.UsuarioId);
        entity.HasIndex(b => b.TipoAccion);
        entity.HasIndex(b => b.FechaAccion);
        entity.HasIndex(b => new { b.Entidad, b.EntidadId });

        entity.HasOne(b => b.Usuario)
            .WithMany(u => u.Bitacoras)
            .HasForeignKey(b => b.UsuarioId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
