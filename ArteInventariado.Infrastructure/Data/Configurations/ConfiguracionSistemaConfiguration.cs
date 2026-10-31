using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class ConfiguracionSistemaConfiguration : IEntityTypeConfiguration<ConfiguracionSistema>
{
    public void Configure(EntityTypeBuilder<ConfiguracionSistema> entity)
    {
        entity.ToTable("configuracion_sistema");

        entity.HasKey(c => c.ConfiguracionSistemaId);

        entity.Property(c => c.ConfiguracionSistemaId).HasColumnName("configuracion_sistema_id");
        entity.Property(c => c.Clave).HasColumnName("clave").HasMaxLength(100).IsRequired();
        entity.Property(c => c.Valor).HasColumnName("valor").IsRequired();
        entity.Property(c => c.Descripcion).HasColumnName("descripcion");
        entity.Property(c => c.FechaModificacion).HasColumnName("fecha_modificacion").IsRequired();

        entity.HasIndex(c => c.Clave).IsUnique();
    }
}
