using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class HistorialEstadoCotizacionConfiguration : IEntityTypeConfiguration<HistorialEstadoCotizacion>
{
    public void Configure(EntityTypeBuilder<HistorialEstadoCotizacion> entity)
    {
        entity.ToTable("historial_estado_cotizaciones");
        entity.HasKey(h => h.HistorialEstadoCotizacionId);
        entity.Property(h => h.HistorialEstadoCotizacionId).HasColumnName("historial_estado_cotizacion_id");
        entity.Property(h => h.CotizacionId).HasColumnName("cotizacion_id");
        entity.Property(h => h.EstadoAnterior).HasColumnName("estado_anterior").HasMaxLength(20).HasConversion<string>();
        entity.Property(h => h.EstadoNuevo).HasColumnName("estado_nuevo").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(h => h.Motivo).HasColumnName("motivo").HasMaxLength(1000);
        entity.Property(h => h.UsuarioId).HasColumnName("usuario_id");
        entity.Property(h => h.Fecha).HasColumnName("fecha").IsRequired();
        entity.HasIndex(h => h.CotizacionId);
        entity.HasIndex(h => h.Fecha);
        entity.HasOne(h => h.Cotizacion)
            .WithMany(c => c.HistorialEstados)
            .HasForeignKey(h => h.CotizacionId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(h => h.Usuario)
            .WithMany(u => u.HistorialCotizaciones)
            .HasForeignKey(h => h.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
