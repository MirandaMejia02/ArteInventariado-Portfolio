using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class ImportacionExcelConfiguration : IEntityTypeConfiguration<ImportacionExcel>
{
    public void Configure(EntityTypeBuilder<ImportacionExcel> entity)
    {
        entity.ToTable("importaciones_excel", table =>
        {
            table.HasCheckConstraint("CK_importaciones_excel_filas_leidas_no_negativo", "filas_leidas >= 0");
            table.HasCheckConstraint("CK_importaciones_excel_filas_importadas_no_negativo", "filas_importadas >= 0");
            table.HasCheckConstraint("CK_importaciones_excel_filas_error_no_negativo", "filas_con_error >= 0");
        });

        entity.HasKey(i => i.ImportacionExcelId);

        entity.Property(i => i.ImportacionExcelId).HasColumnName("importacion_excel_id");
        entity.Property(i => i.NombreArchivo).HasColumnName("nombre_archivo").HasMaxLength(255).IsRequired();
        entity.Property(i => i.RutaArchivo).HasColumnName("ruta_archivo").HasMaxLength(500).IsRequired();
        entity.Property(i => i.UsuarioId).HasColumnName("usuario_id");
        entity.Property(i => i.FechaImportacion).HasColumnName("fecha_importacion").IsRequired();
        entity.Property(i => i.FilasLeidas).HasColumnName("filas_leidas").IsRequired();
        entity.Property(i => i.FilasImportadas).HasColumnName("filas_importadas").IsRequired();
        entity.Property(i => i.FilasConError).HasColumnName("filas_con_error").IsRequired();
        entity.Property(i => i.Estado).HasColumnName("estado").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(i => i.ResumenErrores).HasColumnName("resumen_errores");

        entity.HasIndex(i => i.UsuarioId);
        entity.HasIndex(i => i.FechaImportacion);
        entity.HasIndex(i => i.Estado);

        entity.HasOne(i => i.Usuario)
            .WithMany(u => u.ImportacionesExcel)
            .HasForeignKey(i => i.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
