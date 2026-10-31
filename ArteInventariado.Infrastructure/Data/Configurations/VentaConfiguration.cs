using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class VentaConfiguration : IEntityTypeConfiguration<Venta>
{
    public void Configure(EntityTypeBuilder<Venta> entity)
    {
        entity.ToTable("ventas", table =>
        {
            table.HasCheckConstraint("CK_ventas_subtotal_no_negativo", "subtotal_centavos >= 0");
            table.HasCheckConstraint("CK_ventas_descuento_no_negativo", "descuento_centavos >= 0");
            table.HasCheckConstraint("CK_ventas_total_no_negativo", "total_centavos >= 0");
            table.HasCheckConstraint("CK_ventas_pagado_no_negativo", "pagado_centavos >= 0");
            table.HasCheckConstraint("CK_ventas_cambio_no_negativo", "cambio_centavos >= 0");
        });

        entity.HasKey(v => v.VentaId);

        entity.Property(v => v.VentaId).HasColumnName("venta_id");
        entity.Property(v => v.NumeroVenta).HasColumnName("numero_venta").HasMaxLength(40).IsRequired();
        entity.Property(v => v.CajaId).HasColumnName("caja_id");
        entity.Property(v => v.UsuarioId).HasColumnName("usuario_id");
        entity.Property(v => v.UsuarioNombreSnapshot)
            .HasColumnName("usuario_nombre_snapshot")
            .HasMaxLength(180);
        entity.Property(v => v.ClienteId).HasColumnName("cliente_id");
        entity.Property(v => v.ClienteNombreSnapshot)
            .HasColumnName("cliente_nombre_snapshot")
            .HasMaxLength(180)
            .HasDefaultValue("Consumidor final")
            .IsRequired();
        entity.Property(v => v.FechaVenta).HasColumnName("fecha_venta").IsRequired();
        entity.Property(v => v.SubtotalCentavos).HasColumnName("subtotal_centavos").IsRequired();
        entity.Property(v => v.DescuentoCentavos).HasColumnName("descuento_centavos").IsRequired();
        entity.Property(v => v.DescuentoMotivo).HasColumnName("descuento_motivo").HasMaxLength(300);
        entity.Property(v => v.TotalCentavos).HasColumnName("total_centavos").IsRequired();
        entity.Property(v => v.PagadoCentavos).HasColumnName("pagado_centavos").IsRequired();
        entity.Property(v => v.CambioCentavos).HasColumnName("cambio_centavos").IsRequired();
        entity.Property(v => v.Estado).HasColumnName("estado").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(v => v.Observaciones).HasColumnName("observaciones");

        entity.HasIndex(v => v.NumeroVenta).IsUnique();
        entity.HasIndex(v => v.CajaId);
        entity.HasIndex(v => v.UsuarioId);
        entity.HasIndex(v => v.ClienteId);
        entity.HasIndex(v => v.FechaVenta);

        entity.HasOne(v => v.Caja)
            .WithMany(c => c.Ventas)
            .HasForeignKey(v => v.CajaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(v => v.Usuario)
            .WithMany(u => u.Ventas)
            .HasForeignKey(v => v.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(v => v.Cliente)
            .WithMany(c => c.Ventas)
            .HasForeignKey(v => v.ClienteId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
