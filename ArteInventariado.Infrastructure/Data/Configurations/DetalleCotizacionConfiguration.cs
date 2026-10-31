using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class DetalleCotizacionConfiguration : IEntityTypeConfiguration<DetalleCotizacion>
{
    public void Configure(EntityTypeBuilder<DetalleCotizacion> entity)
    {
        entity.ToTable("detalle_cotizaciones", table =>
        {
            table.HasCheckConstraint("CK_detalle_cotizaciones_descripcion_obligatoria", "length(trim(nombre_descripcion_snapshot)) > 0");
            table.HasCheckConstraint("CK_detalle_cotizaciones_cantidad_positiva", "cantidad > 0");
            table.HasCheckConstraint("CK_detalle_cotizaciones_precio_no_negativo", "precio_unitario_centavos >= 0");
            table.HasCheckConstraint("CK_detalle_cotizaciones_descuento_no_negativo", "descuento_linea_centavos >= 0");
            table.HasCheckConstraint("CK_detalle_cotizaciones_subtotal_no_negativo", "subtotal_centavos >= 0");
            table.HasCheckConstraint("CK_detalle_cotizaciones_descuento_valido", "descuento_linea_centavos <= cantidad * precio_unitario_centavos");
            table.HasCheckConstraint("CK_detalle_cotizaciones_subtotal_cuadra", "subtotal_centavos = cantidad * precio_unitario_centavos - descuento_linea_centavos");
        });

        entity.HasKey(d => d.DetalleCotizacionId);
        entity.Property(d => d.DetalleCotizacionId).HasColumnName("detalle_cotizacion_id");
        entity.Property(d => d.CotizacionId).HasColumnName("cotizacion_id");
        entity.Property(d => d.ProductoId).HasColumnName("producto_id");
        entity.Property(d => d.CodigoProductoSnapshot).HasColumnName("codigo_producto_snapshot").HasMaxLength(50);
        entity.Property(d => d.NombreDescripcionSnapshot).HasColumnName("nombre_descripcion_snapshot").HasMaxLength(250).IsRequired();
        entity.Property(d => d.Cantidad).HasColumnName("cantidad").IsRequired();
        entity.Property(d => d.PrecioUnitarioCentavos).HasColumnName("precio_unitario_centavos").IsRequired();
        entity.Property(d => d.DescuentoLineaCentavos).HasColumnName("descuento_linea_centavos").IsRequired();
        entity.Property(d => d.SubtotalCentavos).HasColumnName("subtotal_centavos").IsRequired();
        entity.Property(d => d.Observaciones).HasColumnName("observaciones").HasMaxLength(1000);

        entity.HasIndex(d => d.CotizacionId);
        entity.HasIndex(d => d.ProductoId);
        entity.HasOne(d => d.Cotizacion)
            .WithMany(c => c.Detalles)
            .HasForeignKey(d => d.CotizacionId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(d => d.Producto)
            .WithMany(p => p.DetallesCotizacion)
            .HasForeignKey(d => d.ProductoId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
