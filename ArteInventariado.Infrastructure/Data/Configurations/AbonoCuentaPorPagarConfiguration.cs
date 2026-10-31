using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class AbonoCuentaPorPagarConfiguration : IEntityTypeConfiguration<AbonoCuentaPorPagar>
{
    public void Configure(EntityTypeBuilder<AbonoCuentaPorPagar> entity)
    {
        entity.ToTable("abonos_cuenta_por_pagar", table =>
        {
            table.HasCheckConstraint("CK_abonos_pagar_monto_positivo", "monto_centavos > 0");
            table.HasCheckConstraint("CK_abonos_pagar_saldo_anterior_no_negativo", "saldo_anterior_centavos >= 0");
            table.HasCheckConstraint("CK_abonos_pagar_saldo_nuevo_no_negativo", "saldo_nuevo_centavos >= 0");
            table.HasCheckConstraint("CK_abonos_pagar_saldo_cuadra", "saldo_anterior_centavos - monto_centavos = saldo_nuevo_centavos");
            table.HasCheckConstraint("CK_abonos_pagar_referencia_requerida", "metodo_pago NOT IN ('TRANSFERENCIA', 'BITCOIN') OR length(trim(coalesce(referencia, ''))) > 0");
        });

        entity.HasKey(a => a.AbonoCuentaPorPagarId);
        entity.Property(a => a.AbonoCuentaPorPagarId).HasColumnName("abono_cuenta_por_pagar_id");
        entity.Property(a => a.CodigoAbono).HasColumnName("codigo_abono").HasMaxLength(40).IsRequired();
        entity.Property(a => a.OperacionId).HasColumnName("operacion_id").IsRequired();
        entity.Property(a => a.OperacionLoteId).HasColumnName("operacion_lote_id");
        entity.Property(a => a.CuentaPorPagarId).HasColumnName("cuenta_por_pagar_id").IsRequired();
        entity.Property(a => a.UsuarioId).HasColumnName("usuario_id").IsRequired();
        entity.Property(a => a.CajaId).HasColumnName("caja_id");
        entity.Property(a => a.MetodoPago).HasColumnName("metodo_pago").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(a => a.OrigenFondos).HasColumnName("origen_fondos").HasMaxLength(40).HasConversion<string>();
        entity.Property(a => a.ResponsableOrigen).HasColumnName("responsable_origen").HasMaxLength(160);
        entity.Property(a => a.MontoCentavos).HasColumnName("monto_centavos").IsRequired();
        entity.Property(a => a.Referencia).HasColumnName("referencia").HasMaxLength(120);
        entity.Property(a => a.FechaAbono).HasColumnName("fecha_abono").IsRequired();
        entity.Property(a => a.Observaciones).HasColumnName("observaciones").HasMaxLength(1000);
        entity.Property(a => a.SaldoAnteriorCentavos).HasColumnName("saldo_anterior_centavos").IsRequired();
        entity.Property(a => a.SaldoNuevoCentavos).HasColumnName("saldo_nuevo_centavos").IsRequired();

        entity.HasIndex(a => a.CodigoAbono).IsUnique();
        entity.HasIndex(a => a.OperacionId).IsUnique();
        entity.HasIndex(a => a.OperacionLoteId);
        entity.HasIndex(a => a.CuentaPorPagarId);
        entity.HasIndex(a => a.UsuarioId);
        entity.HasIndex(a => a.CajaId);
        entity.HasIndex(a => a.FechaAbono);
        entity.HasIndex(a => a.MetodoPago);

        entity.HasOne(a => a.CuentaPorPagar).WithMany(c => c.Abonos).HasForeignKey(a => a.CuentaPorPagarId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(a => a.Usuario).WithMany().HasForeignKey(a => a.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(a => a.Caja).WithMany().HasForeignKey(a => a.CajaId).OnDelete(DeleteBehavior.Restrict);
    }
}
