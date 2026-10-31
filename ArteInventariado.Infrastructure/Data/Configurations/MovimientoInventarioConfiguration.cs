using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class MovimientoInventarioConfiguration : IEntityTypeConfiguration<MovimientoInventario>
{
    public void Configure(EntityTypeBuilder<MovimientoInventario> entity)
    {
        entity.ToTable("movimientos_inventario", table =>
        {
            table.HasCheckConstraint("CK_movimientos_inventario_cantidad_distinta_cero", "cantidad <> 0");
            table.HasCheckConstraint("CK_movimientos_inventario_stock_anterior_no_negativo", "stock_anterior >= 0");
            table.HasCheckConstraint("CK_movimientos_inventario_stock_nuevo_no_negativo", "stock_nuevo >= 0");
            table.HasCheckConstraint("CK_movimientos_inventario_motivo_obligatorio", "length(trim(motivo)) > 0");
        });

        entity.HasKey(m => m.MovimientoInventarioId);

        entity.Property(m => m.MovimientoInventarioId).HasColumnName("movimiento_inventario_id");
        entity.Property(m => m.ProductoId).HasColumnName("producto_id");
        entity.Property(m => m.VentaId).HasColumnName("venta_id");
        entity.Property(m => m.UsuarioId).HasColumnName("usuario_id");
        entity.Property(m => m.TipoMovimiento).HasColumnName("tipo_movimiento").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(m => m.Cantidad).HasColumnName("cantidad").IsRequired();
        entity.Property(m => m.StockAnterior).HasColumnName("stock_anterior").IsRequired();
        entity.Property(m => m.StockNuevo).HasColumnName("stock_nuevo").IsRequired();
        entity.Property(m => m.Motivo).HasColumnName("motivo").HasMaxLength(300).IsRequired();
        entity.Property(m => m.FechaMovimiento).HasColumnName("fecha_movimiento").IsRequired();

        entity.HasIndex(m => m.ProductoId);
        entity.HasIndex(m => m.VentaId);
        entity.HasIndex(m => m.UsuarioId);
        entity.HasIndex(m => m.FechaMovimiento);

        entity.HasOne(m => m.Producto)
            .WithMany(p => p.MovimientosInventario)
            .HasForeignKey(m => m.ProductoId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(m => m.Venta)
            .WithMany(v => v.MovimientosInventario)
            .HasForeignKey(m => m.VentaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(m => m.Usuario)
            .WithMany(u => u.MovimientosInventario)
            .HasForeignKey(m => m.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
