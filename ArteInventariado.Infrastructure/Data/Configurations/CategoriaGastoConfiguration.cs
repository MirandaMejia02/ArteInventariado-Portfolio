using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public sealed class CategoriaGastoConfiguration : IEntityTypeConfiguration<CategoriaGasto>
{
    public void Configure(EntityTypeBuilder<CategoriaGasto> entity)
    {
        entity.ToTable("categorias_gasto", table =>
            table.HasCheckConstraint(
                "CK_categorias_gasto_nombre_obligatorio",
                "length(trim(nombre)) > 0"));

        entity.HasKey(category => category.CategoriaGastoId);
        entity.Property(category => category.CategoriaGastoId).HasColumnName("categoria_gasto_id");
        entity.Property(category => category.Nombre).HasColumnName("nombre").HasMaxLength(120).IsRequired();
        entity.Property(category => category.Activa).HasColumnName("activa").IsRequired();
        entity.Property(category => category.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();
        entity.HasIndex(category => category.Nombre).IsUnique();
    }
}
