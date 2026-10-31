using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class AbonoCuentaPorCobrarConfiguration : IEntityTypeConfiguration<AbonoCuentaPorCobrar>
{
    public void Configure(EntityTypeBuilder<AbonoCuentaPorCobrar> entity)
    {
        entity.ToTable("abonos_cuenta_por_cobrar", table =>
        {
            table.HasCheckConstraint("CK_abonos_cuenta_monto_positivo", "monto_centavos > 0");
            table.HasCheckConstraint("CK_abonos_cuenta_interes_no_negativo", "aplicado_interes_centavos >= 0");
            table.HasCheckConstraint("CK_abonos_cuenta_capital_no_negativo", "aplicado_capital_centavos >= 0");
            table.HasCheckConstraint("CK_abonos_cuenta_aplicado_cuadra", "aplicado_interes_centavos + aplicado_capital_centavos = monto_centavos");
            table.HasCheckConstraint("CK_abonos_cuenta_saldo_anterior_no_negativo", "saldo_anterior_centavos >= 0");
            table.HasCheckConstraint("CK_abonos_cuenta_saldo_nuevo_no_negativo", "saldo_nuevo_centavos >= 0");
            table.HasCheckConstraint("CK_abonos_cuenta_saldo_disminuye", "saldo_nuevo_centavos <= saldo_anterior_centavos");
            table.HasCheckConstraint("CK_abonos_cuenta_saldo_cuadra", "saldo_anterior_centavos - monto_centavos = saldo_nuevo_centavos");
            table.HasCheckConstraint("CK_abonos_cuenta_interes_pendiente_no_negativo", "interes_pendiente_anterior_centavos >= 0 AND interes_pendiente_nuevo_centavos >= 0");
            table.HasCheckConstraint("CK_abonos_cuenta_capital_pendiente_no_negativo", "capital_pendiente_anterior_centavos >= 0 AND capital_pendiente_nuevo_centavos >= 0");
            table.HasCheckConstraint("CK_abonos_cuenta_referencia_requerida", "metodo_pago NOT IN ('TRANSFERENCIA', 'BITCOIN') OR length(trim(coalesce(referencia, ''))) > 0");
        });

        entity.HasKey(a => a.AbonoCuentaPorCobrarId);

        entity.Property(a => a.AbonoCuentaPorCobrarId).HasColumnName("abono_cuenta_por_cobrar_id");
        entity.Property(a => a.CodigoAbono).HasColumnName("codigo_abono").HasMaxLength(40).IsRequired();
        entity.Property(a => a.OperacionId).HasColumnName("operacion_id").IsRequired();
        entity.Property(a => a.CuentaPorCobrarId).HasColumnName("cuenta_por_cobrar_id").IsRequired();
        entity.Property(a => a.UsuarioId).HasColumnName("usuario_id").IsRequired();
        entity.Property(a => a.CajaId).HasColumnName("caja_id");
        entity.Property(a => a.MetodoPago).HasColumnName("metodo_pago").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(a => a.MontoCentavos).HasColumnName("monto_centavos").IsRequired();
        entity.Property(a => a.AplicadoInteresCentavos).HasColumnName("aplicado_interes_centavos").IsRequired();
        entity.Property(a => a.AplicadoCapitalCentavos).HasColumnName("aplicado_capital_centavos").IsRequired();
        entity.Property(a => a.SaldoAnteriorCentavos).HasColumnName("saldo_anterior_centavos").IsRequired();
        entity.Property(a => a.SaldoNuevoCentavos).HasColumnName("saldo_nuevo_centavos").IsRequired();
        entity.Property(a => a.InteresPendienteAnteriorCentavos).HasColumnName("interes_pendiente_anterior_centavos").IsRequired();
        entity.Property(a => a.InteresPendienteNuevoCentavos).HasColumnName("interes_pendiente_nuevo_centavos").IsRequired();
        entity.Property(a => a.CapitalPendienteAnteriorCentavos).HasColumnName("capital_pendiente_anterior_centavos").IsRequired();
        entity.Property(a => a.CapitalPendienteNuevoCentavos).HasColumnName("capital_pendiente_nuevo_centavos").IsRequired();
        entity.Property(a => a.Referencia).HasColumnName("referencia").HasMaxLength(120);
        entity.Property(a => a.Observaciones).HasColumnName("observaciones").HasMaxLength(1000);
        entity.Property(a => a.FechaAbono).HasColumnName("fecha_abono").IsRequired();

        entity.HasIndex(a => a.CodigoAbono).IsUnique();
        entity.HasIndex(a => a.OperacionId).IsUnique();
        entity.HasIndex(a => a.CuentaPorCobrarId);
        entity.HasIndex(a => a.UsuarioId);
        entity.HasIndex(a => a.CajaId);
        entity.HasIndex(a => a.FechaAbono);
        entity.HasIndex(a => a.MetodoPago);

        entity.HasOne(a => a.CuentaPorCobrar)
            .WithMany(c => c.Abonos)
            .HasForeignKey(a => a.CuentaPorCobrarId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(a => a.Usuario)
            .WithMany()
            .HasForeignKey(a => a.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(a => a.Caja)
            .WithMany()
            .HasForeignKey(a => a.CajaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
