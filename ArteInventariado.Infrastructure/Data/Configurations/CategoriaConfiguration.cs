using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class CategoriaConfiguration : IEntityTypeConfiguration<Categoria>
{
    public void Configure(EntityTypeBuilder<Categoria> entity)
    {
        entity.ToTable("categorias");

        entity.HasKey(c => c.CategoriaId);

        entity.Property(c => c.CategoriaId).HasColumnName("categoria_id");
        entity.Property(c => c.Nombre).HasColumnName("nombre").HasMaxLength(120).IsRequired();
        entity.Property(c => c.Descripcion).HasColumnName("descripcion");
        entity.Property(c => c.Activa).HasColumnName("activa").IsRequired();
        entity.Property(c => c.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();
        entity.Property(c => c.FechaModificacion).HasColumnName("fecha_modificacion");

        entity.HasIndex(c => c.Nombre).IsUnique();
    }
}
