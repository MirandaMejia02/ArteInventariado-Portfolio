using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public sealed class SubcategoriaConfiguration : IEntityTypeConfiguration<Subcategoria>
{
    public void Configure(EntityTypeBuilder<Subcategoria> entity)
    {
        entity.ToTable("subcategorias");

        entity.HasKey(subcategory => subcategory.SubcategoriaId);

        entity.Property(subcategory => subcategory.SubcategoriaId)
            .HasColumnName("subcategoria_id");
        entity.Property(subcategory => subcategory.CategoriaId)
            .HasColumnName("categoria_id")
            .IsRequired();
        entity.Property(subcategory => subcategory.Nombre)
            .HasColumnName("nombre")
            .HasMaxLength(120)
            .UseCollation("NOCASE")
            .IsRequired();
        entity.Property(subcategory => subcategory.Descripcion)
            .HasColumnName("descripcion");
        entity.Property(subcategory => subcategory.Activa)
            .HasColumnName("activa")
            .IsRequired();
        entity.Property(subcategory => subcategory.FechaCreacion)
            .HasColumnName("fecha_creacion")
            .IsRequired();
        entity.Property(subcategory => subcategory.FechaModificacion)
            .HasColumnName("fecha_modificacion");

        entity.HasIndex(subcategory => subcategory.CategoriaId);
        entity.HasIndex(subcategory => new { subcategory.CategoriaId, subcategory.Nombre })
            .IsUnique();

        entity.HasOne(subcategory => subcategory.Categoria)
            .WithMany(category => category.Subcategorias)
            .HasForeignKey(subcategory => subcategory.CategoriaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
