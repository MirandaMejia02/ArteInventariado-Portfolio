using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class CuentaPorCobrarDetalleConfiguration : IEntityTypeConfiguration<CuentaPorCobrarDetalle>
{
    public void Configure(EntityTypeBuilder<CuentaPorCobrarDetalle> entity)
    {
        entity.ToTable("cuentas_por_cobrar_detalle", table =>
        {
            table.HasCheckConstraint("CK_cuentas_por_cobrar_detalle_cantidad_positiva", "cantidad_financiada > 0");
            table.HasCheckConstraint("CK_cuentas_por_cobrar_detalle_precio_no_negativo", "precio_unitario_centavos >= 0");
            table.HasCheckConstraint("CK_cuentas_por_cobrar_detalle_descuento_linea_no_negativo", "descuento_linea_centavos >= 0");
            table.HasCheckConstraint("CK_cuentas_por_cobrar_detalle_descuento_general_no_negativo", "descuento_general_proporcional_centavos >= 0");
            table.HasCheckConstraint("CK_cuentas_por_cobrar_detalle_subtotal_no_negativo", "subtotal_financiado_centavos >= 0");
            table.HasCheckConstraint("CK_cuentas_por_cobrar_detalle_capital_no_negativo", "capital_pendiente_centavos >= 0");
        });

        entity.HasKey(d => d.CuentaPorCobrarDetalleId);

        entity.Property(d => d.CuentaPorCobrarDetalleId).HasColumnName("cuenta_por_cobrar_detalle_id");
        entity.Property(d => d.CuentaPorCobrarId).HasColumnName("cuenta_por_cobrar_id").IsRequired();
        entity.Property(d => d.DetalleVentaId).HasColumnName("detalle_venta_id").IsRequired();
        entity.Property(d => d.ProductoId).HasColumnName("producto_id").IsRequired();
        entity.Property(d => d.ProductoCodigoSnapshot).HasColumnName("producto_codigo_snapshot").HasMaxLength(80).IsRequired();
        entity.Property(d => d.ProductoNombreSnapshot).HasColumnName("producto_nombre_snapshot").HasMaxLength(180).IsRequired();
        entity.Property(d => d.CantidadFinanciada).HasColumnName("cantidad_financiada").IsRequired();
        entity.Property(d => d.PrecioUnitarioCentavos).HasColumnName("precio_unitario_centavos").IsRequired();
        entity.Property(d => d.DescuentoLineaCentavos).HasColumnName("descuento_linea_centavos").IsRequired();
        entity.Property(d => d.DescuentoGeneralProporcionalCentavos).HasColumnName("descuento_general_proporcional_centavos").IsRequired();
        entity.Property(d => d.SubtotalFinanciadoCentavos).HasColumnName("subtotal_financiado_centavos").IsRequired();
        entity.Property(d => d.CapitalPendienteCentavos).HasColumnName("capital_pendiente_centavos").IsRequired();

        entity.HasIndex(d => d.CuentaPorCobrarId);
        entity.HasIndex(d => d.DetalleVentaId);
        entity.HasIndex(d => d.ProductoId);

        entity.HasOne(d => d.CuentaPorCobrar)
            .WithMany(c => c.Detalles)
            .HasForeignKey(d => d.CuentaPorCobrarId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(d => d.DetalleVenta)
            .WithMany()
            .HasForeignKey(d => d.DetalleVentaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(d => d.Producto)
            .WithMany()
            .HasForeignKey(d => d.ProductoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
