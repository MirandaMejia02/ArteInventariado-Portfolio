using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class PagoCorreccionVentaConfiguration : IEntityTypeConfiguration<PagoCorreccionVenta>
{
    public void Configure(EntityTypeBuilder<PagoCorreccionVenta> entity)
    {
        entity.ToTable("pagos_correccion_venta", table =>
        {
            table.HasCheckConstraint("CK_pagos_correccion_venta_monto_positivo", "monto_centavos > 0");
            table.HasCheckConstraint("CK_pagos_correccion_venta_motivo_obligatorio", "length(trim(motivo)) > 0");
        });

        entity.HasKey(p => p.PagoCorreccionVentaId);

        entity.Property(p => p.PagoCorreccionVentaId).HasColumnName("pago_correccion_venta_id");
        entity.Property(p => p.DevolucionId).HasColumnName("devolucion_id");
        entity.Property(p => p.VentaId).HasColumnName("venta_id");
        entity.Property(p => p.UsuarioId).HasColumnName("usuario_id");
        entity.Property(p => p.CajaId).HasColumnName("caja_id");
        entity.Property(p => p.MetodoPago).HasColumnName("metodo_pago").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(p => p.MontoCentavos).HasColumnName("monto_centavos").IsRequired();
        entity.Property(p => p.Motivo).HasColumnName("motivo").HasMaxLength(300).IsRequired();
        entity.Property(p => p.Referencia).HasColumnName("referencia").HasMaxLength(120);
        entity.Property(p => p.FechaPago).HasColumnName("fecha_pago").IsRequired();

        entity.HasIndex(p => p.DevolucionId);
        entity.HasIndex(p => p.VentaId);
        entity.HasIndex(p => p.UsuarioId);
        entity.HasIndex(p => p.CajaId);
        entity.HasIndex(p => p.FechaPago);

        entity.HasOne(p => p.Devolucion)
            .WithMany(d => d.PagosCorreccion)
            .HasForeignKey(p => p.DevolucionId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(p => p.Venta)
            .WithMany()
            .HasForeignKey(p => p.VentaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(p => p.Usuario)
            .WithMany()
            .HasForeignKey(p => p.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(p => p.Caja)
            .WithMany()
            .HasForeignKey(p => p.CajaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
