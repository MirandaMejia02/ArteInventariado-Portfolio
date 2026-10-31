using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class HistorialCostoProductoConfiguration : IEntityTypeConfiguration<HistorialCostoProducto>
{
    public void Configure(EntityTypeBuilder<HistorialCostoProducto> entity)
    {
        entity.ToTable("historial_costos_producto", table =>
        {
            table.HasCheckConstraint("CK_historial_costos_costo_anterior_no_negativo", "costo_anterior_centavos >= 0");
            table.HasCheckConstraint("CK_historial_costos_costo_recibido_no_negativo", "costo_recibido_centavos >= 0");
            table.HasCheckConstraint("CK_historial_costos_costo_promedio_no_negativo", "costo_promedio_nuevo_centavos >= 0");
            table.HasCheckConstraint("CK_historial_costos_stock_anterior_no_negativo", "stock_anterior >= 0");
            table.HasCheckConstraint("CK_historial_costos_cantidad_recibida_positiva", "cantidad_recibida > 0");
            table.HasCheckConstraint("CK_historial_costos_stock_nuevo_no_negativo", "stock_nuevo >= 0");
        });

        entity.HasKey(h => h.HistorialCostoProductoId);

        entity.Property(h => h.HistorialCostoProductoId).HasColumnName("historial_costo_producto_id");
        entity.Property(h => h.ProductoId).HasColumnName("producto_id");
        entity.Property(h => h.ProveedorId).HasColumnName("proveedor_id");
        entity.Property(h => h.CompraId).HasColumnName("compra_id");
        entity.Property(h => h.DetalleCompraId).HasColumnName("detalle_compra_id");
        entity.Property(h => h.RecepcionCompraId).HasColumnName("recepcion_compra_id");
        entity.Property(h => h.CostoAnteriorCentavos).HasColumnName("costo_anterior_centavos").IsRequired();
        entity.Property(h => h.CostoRecibidoCentavos).HasColumnName("costo_recibido_centavos").IsRequired();
        entity.Property(h => h.CostoPromedioNuevoCentavos).HasColumnName("costo_promedio_nuevo_centavos").IsRequired();
        entity.Property(h => h.StockAnterior).HasColumnName("stock_anterior").IsRequired();
        entity.Property(h => h.CantidadRecibida).HasColumnName("cantidad_recibida").IsRequired();
        entity.Property(h => h.StockNuevo).HasColumnName("stock_nuevo").IsRequired();
        entity.Property(h => h.Fecha).HasColumnName("fecha").IsRequired();
        entity.Property(h => h.UsuarioId).HasColumnName("usuario_id");
        entity.Property(h => h.Observaciones).HasColumnName("observaciones").HasMaxLength(1000);

        entity.HasIndex(h => h.ProductoId);
        entity.HasIndex(h => h.ProveedorId);
        entity.HasIndex(h => h.CompraId);
        entity.HasIndex(h => h.DetalleCompraId);
        entity.HasIndex(h => h.RecepcionCompraId);
        entity.HasIndex(h => h.Fecha);

        entity.HasOne(h => h.Producto)
            .WithMany(p => p.HistorialCostosProducto)
            .HasForeignKey(h => h.ProductoId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(h => h.Proveedor)
            .WithMany(p => p.HistorialCostosProducto)
            .HasForeignKey(h => h.ProveedorId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(h => h.Compra)
            .WithMany()
            .HasForeignKey(h => h.CompraId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(h => h.DetalleCompra)
            .WithMany()
            .HasForeignKey(h => h.DetalleCompraId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(h => h.RecepcionCompra)
            .WithMany()
            .HasForeignKey(h => h.RecepcionCompraId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(h => h.Usuario)
            .WithMany(u => u.HistorialCostosProducto)
            .HasForeignKey(h => h.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
