using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class PagoCompraConfiguration : IEntityTypeConfiguration<PagoCompra>
{
    public void Configure(EntityTypeBuilder<PagoCompra> entity)
    {
        entity.ToTable("pagos_compra", table =>
        {
            table.HasCheckConstraint("CK_pagos_compra_monto_positivo", "monto_centavos > 0");
            table.HasCheckConstraint("CK_pagos_compra_referencia_requerida", "metodo_pago NOT IN ('TRANSFERENCIA', 'BITCOIN') OR length(trim(coalesce(referencia, ''))) > 0");
        });

        entity.HasKey(p => p.PagoCompraId);
        entity.Property(p => p.PagoCompraId).HasColumnName("pago_compra_id");
        entity.Property(p => p.CodigoPago).HasColumnName("codigo_pago").HasMaxLength(40).IsRequired();
        entity.Property(p => p.OperacionId).HasColumnName("operacion_id").IsRequired();
        entity.Property(p => p.OperacionLoteId).HasColumnName("operacion_lote_id");
        entity.Property(p => p.CompraId).HasColumnName("compra_id").IsRequired();
        entity.Property(p => p.UsuarioId).HasColumnName("usuario_id").IsRequired();
        entity.Property(p => p.CajaId).HasColumnName("caja_id");
        entity.Property(p => p.MetodoPago).HasColumnName("metodo_pago").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(p => p.OrigenFondos).HasColumnName("origen_fondos").HasMaxLength(40).HasConversion<string>();
        entity.Property(p => p.ResponsableOrigen).HasColumnName("responsable_origen").HasMaxLength(160);
        entity.Property(p => p.MontoCentavos).HasColumnName("monto_centavos").IsRequired();
        entity.Property(p => p.Referencia).HasColumnName("referencia").HasMaxLength(120);
        entity.Property(p => p.FechaPago).HasColumnName("fecha_pago").IsRequired();
        entity.Property(p => p.Observaciones).HasColumnName("observaciones").HasMaxLength(1000);
        entity.Property(p => p.EsPagoInicial).HasColumnName("es_pago_inicial").IsRequired();
        entity.Property(p => p.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();

        entity.HasIndex(p => p.CodigoPago).IsUnique();
        entity.HasIndex(p => p.OperacionId).IsUnique();
        entity.HasIndex(p => p.OperacionLoteId);
        entity.HasIndex(p => p.CompraId);
        entity.HasIndex(p => p.UsuarioId);
        entity.HasIndex(p => p.CajaId);
        entity.HasIndex(p => p.FechaPago);
        entity.HasIndex(p => p.MetodoPago);

        entity.HasOne(p => p.Compra).WithMany(c => c.Pagos).HasForeignKey(p => p.CompraId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(p => p.Usuario).WithMany().HasForeignKey(p => p.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(p => p.Caja).WithMany().HasForeignKey(p => p.CajaId).OnDelete(DeleteBehavior.Restrict);
    }
}
