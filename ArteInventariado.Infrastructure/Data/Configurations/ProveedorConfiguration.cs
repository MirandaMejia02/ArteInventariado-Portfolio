using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class ProveedorConfiguration : IEntityTypeConfiguration<Proveedor>
{
    public void Configure(EntityTypeBuilder<Proveedor> entity)
    {
        entity.ToTable("proveedores", table =>
        {
            table.HasCheckConstraint("CK_proveedores_codigo_obligatorio", "length(trim(codigo_proveedor)) > 0");
            table.HasCheckConstraint("CK_proveedores_nombre_obligatorio", "length(trim(nombre_razon_social)) > 0");
            table.HasCheckConstraint("CK_proveedores_motivo_inactivo", "estado <> 'INACTIVO' OR length(trim(coalesce(motivo_desactivacion, ''))) > 0");
        });

        entity.HasKey(p => p.ProveedorId);

        entity.Property(p => p.ProveedorId).HasColumnName("proveedor_id");
        entity.Property(p => p.CodigoProveedor).HasColumnName("codigo_proveedor").HasMaxLength(50).IsRequired();
        entity.Property(p => p.Tipo).HasColumnName("tipo").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(p => p.NombreRazonSocial).HasColumnName("nombre_razon_social").HasMaxLength(180).IsRequired();
        entity.Property(p => p.NombreNormalizado).HasColumnName("nombre_normalizado").HasMaxLength(180).IsRequired();
        entity.Property(p => p.NombreContacto).HasColumnName("nombre_contacto").HasMaxLength(180);
        entity.Property(p => p.Telefono).HasColumnName("telefono").HasMaxLength(40);
        entity.Property(p => p.TelefonoNormalizado).HasColumnName("telefono_normalizado").HasMaxLength(40);
        entity.Property(p => p.WhatsApp).HasColumnName("whatsapp").HasMaxLength(40);
        entity.Property(p => p.WhatsAppNormalizado).HasColumnName("whatsapp_normalizado").HasMaxLength(40);
        entity.Property(p => p.Correo).HasColumnName("correo").HasMaxLength(180);
        entity.Property(p => p.CorreoNormalizado).HasColumnName("correo_normalizado").HasMaxLength(180);
        entity.Property(p => p.Direccion).HasColumnName("direccion").HasMaxLength(500);
        entity.Property(p => p.DocumentoIdentidad).HasColumnName("documento_identidad").HasMaxLength(40);
        entity.Property(p => p.DocumentoIdentidadNormalizado).HasColumnName("documento_identidad_normalizado").HasMaxLength(40);
        entity.Property(p => p.DocumentoFiscal).HasColumnName("documento_fiscal").HasMaxLength(40);
        entity.Property(p => p.DocumentoFiscalNormalizado).HasColumnName("documento_fiscal_normalizado").HasMaxLength(40);
        entity.Property(p => p.Notas).HasColumnName("notas").HasMaxLength(1000);
        entity.Property(p => p.Estado).HasColumnName("estado").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(p => p.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();
        entity.Property(p => p.FechaActualizacion).HasColumnName("fecha_actualizacion").IsRequired();
        entity.Property(p => p.UsuarioCreacionId).HasColumnName("usuario_creacion_id");
        entity.Property(p => p.UsuarioActualizacionId).HasColumnName("usuario_actualizacion_id");
        entity.Property(p => p.MotivoDesactivacion).HasColumnName("motivo_desactivacion").HasMaxLength(500);
        entity.Property(p => p.FechaDesactivacion).HasColumnName("fecha_desactivacion");

        entity.HasIndex(p => p.CodigoProveedor).IsUnique();
        entity.HasIndex(p => p.NombreNormalizado);
        entity.HasIndex(p => p.Tipo);
        entity.HasIndex(p => p.Estado);
        entity.HasIndex(p => p.TelefonoNormalizado);
        entity.HasIndex(p => p.WhatsAppNormalizado);
        entity.HasIndex(p => p.CorreoNormalizado);
        entity.HasIndex(p => p.DocumentoIdentidadNormalizado)
            .IsUnique()
            .HasFilter("documento_identidad_normalizado IS NOT NULL AND documento_identidad_normalizado <> ''");
        entity.HasIndex(p => p.DocumentoFiscalNormalizado)
            .IsUnique()
            .HasFilter("documento_fiscal_normalizado IS NOT NULL AND documento_fiscal_normalizado <> ''");

        entity.HasOne(p => p.UsuarioCreacion)
            .WithMany(u => u.ProveedoresCreados)
            .HasForeignKey(p => p.UsuarioCreacionId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(p => p.UsuarioActualizacion)
            .WithMany(u => u.ProveedoresActualizados)
            .HasForeignKey(p => p.UsuarioActualizacionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
