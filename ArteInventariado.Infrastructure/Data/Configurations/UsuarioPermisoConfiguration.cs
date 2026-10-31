using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class UsuarioPermisoConfiguration : IEntityTypeConfiguration<UsuarioPermiso>
{
    public void Configure(EntityTypeBuilder<UsuarioPermiso> entity)
    {
        entity.ToTable("usuario_permisos");

        entity.HasKey(p => p.UsuarioPermisoId);

        entity.Property(p => p.UsuarioPermisoId).HasColumnName("usuario_permiso_id");
        entity.Property(p => p.UsuarioId).HasColumnName("usuario_id").IsRequired();
        entity.Property(p => p.Permiso)
            .HasColumnName("permiso")
            .HasMaxLength(80)
            .HasConversion<string>()
            .IsRequired();
        entity.Property(p => p.Habilitado).HasColumnName("habilitado").IsRequired();
        entity.Property(p => p.FechaActualizacion).HasColumnName("fecha_actualizacion").IsRequired();

        entity.HasIndex(p => new { p.UsuarioId, p.Permiso }).IsUnique();
        entity.HasIndex(p => p.Permiso);

        entity.HasOne(p => p.Usuario)
            .WithMany(u => u.Permisos)
            .HasForeignKey(p => p.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
