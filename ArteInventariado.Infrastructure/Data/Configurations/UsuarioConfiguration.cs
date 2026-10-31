using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> entity)
    {
        entity.ToTable("usuarios");

        entity.HasKey(u => u.UsuarioId);

        entity.Property(u => u.UsuarioId).HasColumnName("usuario_id");
        entity.Property(u => u.NombreCompleto).HasColumnName("nombre_completo").HasMaxLength(150).IsRequired();
        entity.Property(u => u.NombreUsuario).HasColumnName("nombre_usuario").HasMaxLength(50).IsRequired();
        entity.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired();
        entity.Property(u => u.Rol).HasColumnName("rol").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(u => u.Activo).HasColumnName("activo").IsRequired();
        entity.Property(u => u.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();
        entity.Property(u => u.UltimoLogin).HasColumnName("ultimo_login");

        entity.HasIndex(u => u.NombreUsuario).IsUnique();
    }
}
