using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class CotizacionConfiguration : IEntityTypeConfiguration<Cotizacion>
{
    public void Configure(EntityTypeBuilder<Cotizacion> entity)
    {
        entity.ToTable("cotizaciones", table =>
        {
            table.HasCheckConstraint("CK_cotizaciones_codigo_obligatorio", "length(trim(codigo_cotizacion)) > 0");
            table.HasCheckConstraint("CK_cotizaciones_destinatario_obligatorio", "length(trim(nombre_destinatario_snapshot)) > 0");
            table.HasCheckConstraint("CK_cotizaciones_subtotal_no_negativo", "subtotal_centavos >= 0");
            table.HasCheckConstraint("CK_cotizaciones_descuento_no_negativo", "descuento_general_centavos >= 0");
            table.HasCheckConstraint("CK_cotizaciones_descuento_valido", "descuento_general_centavos <= subtotal_centavos");
            table.HasCheckConstraint("CK_cotizaciones_total_no_negativo", "total_centavos >= 0");
            table.HasCheckConstraint("CK_cotizaciones_total_cuadra", "total_centavos = subtotal_centavos - descuento_general_centavos");
            table.HasCheckConstraint("CK_cotizaciones_vencimiento_valido", "fecha_vencimiento >= fecha");
        });

        entity.HasKey(c => c.CotizacionId);
        entity.Property(c => c.CotizacionId).HasColumnName("cotizacion_id");
        entity.Property(c => c.CodigoCotizacion).HasColumnName("codigo_cotizacion").HasMaxLength(30).IsRequired();
        entity.Property(c => c.ClienteId).HasColumnName("cliente_id");
        entity.Property(c => c.NombreDestinatarioSnapshot).HasColumnName("nombre_destinatario_snapshot").HasMaxLength(180).IsRequired();
        entity.Property(c => c.TelefonoSnapshot).HasColumnName("telefono_snapshot").HasMaxLength(40);
        entity.Property(c => c.CorreoSnapshot).HasColumnName("correo_snapshot").HasMaxLength(180);
        entity.Property(c => c.DireccionSnapshot).HasColumnName("direccion_snapshot").HasMaxLength(500);
        entity.Property(c => c.Fecha).HasColumnName("fecha").IsRequired();
        entity.Property(c => c.FechaVencimiento).HasColumnName("fecha_vencimiento").IsRequired();
        entity.Property(c => c.Estado).HasColumnName("estado").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(c => c.SubtotalCentavos).HasColumnName("subtotal_centavos").IsRequired();
        entity.Property(c => c.DescuentoGeneralCentavos).HasColumnName("descuento_general_centavos").IsRequired();
        entity.Property(c => c.TotalCentavos).HasColumnName("total_centavos").IsRequired();
        entity.Property(c => c.CondicionesComerciales).HasColumnName("condiciones_comerciales").HasMaxLength(2000);
        entity.Property(c => c.Observaciones).HasColumnName("observaciones").HasMaxLength(2000);
        entity.Property(c => c.UsuarioCreadorId).HasColumnName("usuario_creador_id");
        entity.Property(c => c.UsuarioUltimaActualizacionId).HasColumnName("usuario_ultima_actualizacion_id");
        entity.Property(c => c.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();
        entity.Property(c => c.FechaActualizacion).HasColumnName("fecha_actualizacion").IsRequired();
        entity.Property(c => c.FechaEmision).HasColumnName("fecha_emision");
        entity.Property(c => c.FechaCierre).HasColumnName("fecha_cierre");
        entity.Property(c => c.MotivoCancelacion).HasColumnName("motivo_cancelacion").HasMaxLength(1000);

        entity.HasIndex(c => c.CodigoCotizacion).IsUnique();
        entity.HasIndex(c => c.ClienteId);
        entity.HasIndex(c => c.Estado);
        entity.HasIndex(c => c.Fecha);
        entity.HasIndex(c => c.FechaVencimiento);

        entity.HasOne(c => c.Cliente)
            .WithMany(c => c.Cotizaciones)
            .HasForeignKey(c => c.ClienteId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(c => c.UsuarioCreador)
            .WithMany(u => u.CotizacionesCreadas)
            .HasForeignKey(c => c.UsuarioCreadorId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(c => c.UsuarioUltimaActualizacion)
            .WithMany(u => u.CotizacionesActualizadas)
            .HasForeignKey(c => c.UsuarioUltimaActualizacionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
