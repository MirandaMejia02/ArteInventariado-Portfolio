using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class CuentaPorCobrarConfiguration : IEntityTypeConfiguration<CuentaPorCobrar>
{
    public void Configure(EntityTypeBuilder<CuentaPorCobrar> entity)
    {
        entity.ToTable("cuentas_por_cobrar", table =>
        {
            table.HasCheckConstraint("CK_cuentas_por_cobrar_capital_no_negativo", "capital_financiado_centavos >= 0");
            table.HasCheckConstraint("CK_cuentas_por_cobrar_interes_no_negativo", "interes_calculado_centavos >= 0");
            table.HasCheckConstraint("CK_cuentas_por_cobrar_total_no_negativo", "total_original_centavos >= 0");
            table.HasCheckConstraint("CK_cuentas_por_cobrar_pagado_no_negativo", "total_pagado_centavos >= 0");
            table.HasCheckConstraint("CK_cuentas_por_cobrar_saldo_no_negativo", "saldo_actual_centavos >= 0");
        });

        entity.HasKey(c => c.CuentaPorCobrarId);

        entity.Property(c => c.CuentaPorCobrarId).HasColumnName("cuenta_por_cobrar_id");
        entity.Property(c => c.CodigoCuenta).HasColumnName("codigo_cuenta").HasMaxLength(40).IsRequired();
        entity.Property(c => c.VentaId).HasColumnName("venta_id").IsRequired();
        entity.Property(c => c.ClienteId).HasColumnName("cliente_id").IsRequired();
        entity.Property(c => c.CajaId).HasColumnName("caja_id").IsRequired();
        entity.Property(c => c.UsuarioRegistroId).HasColumnName("usuario_registro_id").IsRequired();
        entity.Property(c => c.CapitalFinanciadoCentavos).HasColumnName("capital_financiado_centavos").IsRequired();
        entity.Property(c => c.InteresPorcentaje)
            .HasColumnName("interes_porcentaje")
            .HasPrecision(9, 4)
            .IsRequired();
        entity.Property(c => c.InteresCalculadoCentavos).HasColumnName("interes_calculado_centavos").IsRequired();
        entity.Property(c => c.TotalOriginalCentavos).HasColumnName("total_original_centavos").IsRequired();
        entity.Property(c => c.TotalPagadoCentavos).HasColumnName("total_pagado_centavos").IsRequired();
        entity.Property(c => c.SaldoActualCentavos).HasColumnName("saldo_actual_centavos").IsRequired();
        entity.Property(c => c.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();
        entity.Property(c => c.FechaVencimiento).HasColumnName("fecha_vencimiento").IsRequired();
        entity.Property(c => c.Estado).HasColumnName("estado").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(c => c.FechaPago).HasColumnName("fecha_pago");
        entity.Property(c => c.FechaAnulacion).HasColumnName("fecha_anulacion");
        entity.Property(c => c.Observaciones).HasColumnName("observaciones").HasMaxLength(1000);
        entity.Property(c => c.FechaActualizacion).HasColumnName("fecha_actualizacion").IsRequired().IsConcurrencyToken();

        entity.HasIndex(c => c.CodigoCuenta).IsUnique();
        entity.HasIndex(c => c.VentaId).IsUnique();
        entity.HasIndex(c => c.ClienteId);
        entity.HasIndex(c => c.CajaId);
        entity.HasIndex(c => c.UsuarioRegistroId);
        entity.HasIndex(c => c.Estado);
        entity.HasIndex(c => c.FechaVencimiento);

        entity.HasOne(c => c.Venta)
            .WithOne(v => v.CuentaPorCobrar)
            .HasForeignKey<CuentaPorCobrar>(c => c.VentaId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(c => c.Cliente)
            .WithMany(c => c.CuentasPorCobrar)
            .HasForeignKey(c => c.ClienteId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(c => c.Caja)
            .WithMany()
            .HasForeignKey(c => c.CajaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(c => c.UsuarioRegistro)
            .WithMany()
            .HasForeignKey(c => c.UsuarioRegistroId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
