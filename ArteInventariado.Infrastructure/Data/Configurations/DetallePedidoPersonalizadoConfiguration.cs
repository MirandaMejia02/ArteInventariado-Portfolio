using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class DetallePedidoPersonalizadoConfiguration : IEntityTypeConfiguration<DetallePedidoPersonalizado>
{
    public void Configure(EntityTypeBuilder<DetallePedidoPersonalizado> entity)
    {
        entity.ToTable("detalle_pedidos_personalizados", table =>
        {
            table.HasCheckConstraint("CK_detalle_pedidos_descripcion_obligatoria", "length(trim(nombre_descripcion_snapshot)) > 0");
            table.HasCheckConstraint("CK_detalle_pedidos_cantidad_positiva", "cantidad > 0");
            table.HasCheckConstraint("CK_detalle_pedidos_precio_no_negativo", "precio_unitario_centavos >= 0");
            table.HasCheckConstraint("CK_detalle_pedidos_descuento_valido", "descuento_linea_centavos >= 0 AND descuento_linea_centavos <= cantidad * precio_unitario_centavos");
            table.HasCheckConstraint("CK_detalle_pedidos_subtotal_cuadra", "subtotal_centavos = cantidad * precio_unitario_centavos - descuento_linea_centavos");
        });
        entity.HasKey(d => d.DetallePedidoPersonalizadoId);
        entity.Property(d => d.DetallePedidoPersonalizadoId).HasColumnName("detalle_pedido_personalizado_id");
        entity.Property(d => d.PedidoPersonalizadoId).HasColumnName("pedido_personalizado_id");
        entity.Property(d => d.ProductoId).HasColumnName("producto_id");
        entity.Property(d => d.ProductoSkuId).HasColumnName("producto_sku_id");
        entity.Property(d => d.CodigoProductoSnapshot).HasColumnName("codigo_producto_snapshot").HasMaxLength(50);
        entity.Property(d => d.CodigoSkuSnapshot).HasColumnName("codigo_sku_snapshot").HasMaxLength(80);
        entity.Property(d => d.NombreVarianteSnapshot).HasColumnName("nombre_variante_snapshot").HasMaxLength(150);
        entity.Property(d => d.NombreDescripcionSnapshot).HasColumnName("nombre_descripcion_snapshot").HasMaxLength(250).IsRequired();
        entity.Property(d => d.Cantidad).HasColumnName("cantidad").IsRequired();
        entity.Property(d => d.PrecioUnitarioCentavos).HasColumnName("precio_unitario_centavos").IsRequired();
        entity.Property(d => d.DescuentoLineaCentavos).HasColumnName("descuento_linea_centavos").IsRequired();
        entity.Property(d => d.SubtotalCentavos).HasColumnName("subtotal_centavos").IsRequired();
        entity.Property(d => d.Especificaciones).HasColumnName("especificaciones").HasMaxLength(2000);
        entity.Property(d => d.Color).HasColumnName("color").HasMaxLength(120);
        entity.Property(d => d.Medidas).HasColumnName("medidas").HasMaxLength(250);
        entity.Property(d => d.Material).HasColumnName("material").HasMaxLength(250);
        entity.Property(d => d.TextoPersonalizado).HasColumnName("texto_personalizado").HasMaxLength(1000);
        entity.Property(d => d.Observaciones).HasColumnName("observaciones").HasMaxLength(2000);
        entity.HasIndex(d => d.PedidoPersonalizadoId);
        entity.HasIndex(d => d.ProductoId);
        entity.HasIndex(d => d.ProductoSkuId);
        entity.HasOne(d => d.PedidoPersonalizado).WithMany(p => p.Detalles)
            .HasForeignKey(d => d.PedidoPersonalizadoId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(d => d.Producto).WithMany(p => p.DetallesPedidosPersonalizados)
            .HasForeignKey(d => d.ProductoId).OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(d => d.ProductoSku).WithMany()
            .HasForeignKey(d => d.ProductoSkuId).OnDelete(DeleteBehavior.SetNull);
    }
}
