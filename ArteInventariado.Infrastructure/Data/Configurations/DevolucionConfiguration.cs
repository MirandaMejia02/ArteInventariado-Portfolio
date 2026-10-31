using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class DevolucionConfiguration : IEntityTypeConfiguration<Devolucion>
{
    public void Configure(EntityTypeBuilder<Devolucion> entity)
    {
        entity.ToTable("devoluciones", table =>
        {
            table.HasCheckConstraint("CK_devoluciones_total_no_negativo", "total_devuelto_centavos >= 0");
            table.HasCheckConstraint("CK_devoluciones_motivo_obligatorio", "length(trim(motivo)) > 0");
        });

        entity.HasKey(d => d.DevolucionId);

        entity.Property(d => d.DevolucionId).HasColumnName("devolucion_id");
        entity.Property(d => d.OperacionId).HasColumnName("operacion_id");
        entity.Property(d => d.NumeroDevolucion).HasColumnName("numero_devolucion").HasMaxLength(40).IsRequired();
        entity.Property(d => d.VentaId).HasColumnName("venta_id");
        entity.Property(d => d.UsuarioId).HasColumnName("usuario_id");
        entity.Property(d => d.CajaId).HasColumnName("caja_id");
        entity.Property(d => d.FechaDevolucion).HasColumnName("fecha_devolucion").IsRequired();
        entity.Property(d => d.TipoCorreccion)
            .HasColumnName("tipo_correccion")
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(ArteInventariado.Domain.Enums.TipoCorreccionVenta.DEVOLUCION)
            .IsRequired();
        entity.Property(d => d.EsErrorRegistroSinOperacionReal)
            .HasColumnName("es_error_registro_sin_operacion_real")
            .HasDefaultValue(false)
            .IsRequired();
        entity.Property(d => d.TotalDevueltoCentavos).HasColumnName("total_devuelto_centavos").IsRequired();
        entity.Property(d => d.Motivo).HasColumnName("motivo").HasMaxLength(300).IsRequired();
        entity.Property(d => d.Observaciones).HasColumnName("observaciones").HasMaxLength(500);

        entity.HasIndex(d => d.NumeroDevolucion).IsUnique();
        entity.HasIndex(d => d.OperacionId).IsUnique();
        entity.HasIndex(d => d.VentaId);
        entity.HasIndex(d => new { d.VentaId, d.TipoCorreccion })
            .HasFilter("tipo_correccion = 'ANULACION'")
            .IsUnique();
        entity.HasIndex(d => d.UsuarioId);
        entity.HasIndex(d => d.CajaId);
        entity.HasIndex(d => d.FechaDevolucion);

        entity.HasOne(d => d.Venta)
            .WithMany()
            .HasForeignKey(d => d.VentaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(d => d.Usuario)
            .WithMany()
            .HasForeignKey(d => d.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(d => d.Caja)
            .WithMany()
            .HasForeignKey(d => d.CajaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
