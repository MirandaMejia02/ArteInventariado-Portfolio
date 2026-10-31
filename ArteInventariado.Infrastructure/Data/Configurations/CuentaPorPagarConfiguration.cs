using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class CuentaPorPagarConfiguration : IEntityTypeConfiguration<CuentaPorPagar>
{
    public void Configure(EntityTypeBuilder<CuentaPorPagar> entity)
    {
        entity.ToTable("cuentas_por_pagar", table =>
        {
            table.HasCheckConstraint("CK_cuentas_pagar_total_no_negativo", "total_original_centavos >= 0");
            table.HasCheckConstraint("CK_cuentas_pagar_pagado_no_negativo", "total_pagado_centavos >= 0");
            table.HasCheckConstraint("CK_cuentas_pagar_saldo_no_negativo", "saldo_pendiente_centavos >= 0");
            table.HasCheckConstraint("CK_cuentas_pagar_totales_cuadran", "total_pagado_centavos + saldo_pendiente_centavos = total_original_centavos");
        });

        entity.HasKey(c => c.CuentaPorPagarId);
        entity.Property(c => c.CuentaPorPagarId).HasColumnName("cuenta_por_pagar_id");
        entity.Property(c => c.CodigoCuenta).HasColumnName("codigo_cuenta").HasMaxLength(40).IsRequired();
        entity.Property(c => c.CompraId).HasColumnName("compra_id").IsRequired();
        entity.Property(c => c.ProveedorId).HasColumnName("proveedor_id").IsRequired();
        entity.Property(c => c.UsuarioCreadorId).HasColumnName("usuario_creador_id").IsRequired();
        entity.Property(c => c.TotalOriginalCentavos).HasColumnName("total_original_centavos").IsRequired();
        entity.Property(c => c.TotalPagadoCentavos).HasColumnName("total_pagado_centavos").IsRequired();
        entity.Property(c => c.SaldoPendienteCentavos).HasColumnName("saldo_pendiente_centavos").IsRequired();
        entity.Property(c => c.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();
        entity.Property(c => c.FechaVencimiento).HasColumnName("fecha_vencimiento").IsRequired();
        entity.Property(c => c.Estado).HasColumnName("estado").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(c => c.FechaPago).HasColumnName("fecha_pago");
        entity.Property(c => c.FechaActualizacion).HasColumnName("fecha_actualizacion").IsRequired().IsConcurrencyToken();
        entity.Property(c => c.Observaciones).HasColumnName("observaciones").HasMaxLength(1000);

        entity.HasIndex(c => c.CodigoCuenta).IsUnique();
        entity.HasIndex(c => c.CompraId).IsUnique();
        entity.HasIndex(c => c.ProveedorId);
        entity.HasIndex(c => c.UsuarioCreadorId);
        entity.HasIndex(c => c.Estado);
        entity.HasIndex(c => c.FechaVencimiento);

        entity.HasOne(c => c.Compra).WithOne(c => c.CuentaPorPagar).HasForeignKey<CuentaPorPagar>(c => c.CompraId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(c => c.Proveedor).WithMany().HasForeignKey(c => c.ProveedorId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(c => c.UsuarioCreador).WithMany().HasForeignKey(c => c.UsuarioCreadorId).OnDelete(DeleteBehavior.Restrict);
    }
}
