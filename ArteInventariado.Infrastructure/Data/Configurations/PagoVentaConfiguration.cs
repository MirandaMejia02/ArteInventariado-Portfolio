using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class PagoVentaConfiguration : IEntityTypeConfiguration<PagoVenta>
{
    public void Configure(EntityTypeBuilder<PagoVenta> entity)
    {
        entity.ToTable("pagos_venta", table =>
        {
            table.HasCheckConstraint("CK_pagos_venta_monto_positivo", "monto_centavos > 0");
        });

        entity.HasKey(p => p.PagoVentaId);

        entity.Property(p => p.PagoVentaId).HasColumnName("pago_venta_id");
        entity.Property(p => p.VentaId).HasColumnName("venta_id");
        entity.Property(p => p.MetodoPago).HasColumnName("metodo_pago").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(p => p.MontoCentavos).HasColumnName("monto_centavos").IsRequired();
        entity.Property(p => p.Referencia).HasColumnName("referencia").HasMaxLength(120);
        entity.Property(p => p.FechaPago).HasColumnName("fecha_pago").IsRequired();
        entity.Property(p => p.Origen).HasColumnName("origen").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(p => p.PagoPedidoPersonalizadoId).HasColumnName("pago_pedido_personalizado_id");
        entity.Property(p => p.AfectaCaja).HasColumnName("afecta_caja").IsRequired();

        entity.HasIndex(p => p.VentaId);
        entity.HasIndex(p => p.PagoPedidoPersonalizadoId).IsUnique();

        entity.HasOne(p => p.Venta)
            .WithMany(v => v.Pagos)
            .HasForeignKey(p => p.VentaId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(p => p.PagoPedidoPersonalizado)
            .WithMany()
            .HasForeignKey(p => p.PagoPedidoPersonalizadoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
