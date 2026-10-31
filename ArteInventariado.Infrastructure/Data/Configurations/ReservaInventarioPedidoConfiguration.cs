using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class ReservaInventarioPedidoConfiguration : IEntityTypeConfiguration<ReservaInventarioPedido>
{
    public void Configure(EntityTypeBuilder<ReservaInventarioPedido> entity)
    {
        entity.ToTable("reservas_inventario_pedido", table =>
        {
            table.HasCheckConstraint("CK_reserva_cantidad_positiva", "cantidad_reservada > 0");
            table.HasCheckConstraint("CK_reserva_cantidades_validas",
                "cantidad_consumida >= 0 AND cantidad_liberada >= 0 AND cantidad_consumida + cantidad_liberada <= cantidad_reservada");
        });

        entity.HasKey(r => r.ReservaInventarioPedidoId);
        entity.Property(r => r.ReservaInventarioPedidoId).HasColumnName("reserva_inventario_pedido_id");
        entity.Property(r => r.PedidoPersonalizadoId).HasColumnName("pedido_personalizado_id");
        entity.Property(r => r.DetallePedidoPersonalizadoId).HasColumnName("detalle_pedido_personalizado_id");
        entity.Property(r => r.ProductoId).HasColumnName("producto_id");
        entity.Property(r => r.ProductoSkuId).HasColumnName("producto_sku_id");
        entity.Property(r => r.Ubicacion).HasColumnName("ubicacion").HasMaxLength(20).HasConversion<string>();
        entity.Property(r => r.CantidadReservada).HasColumnName("cantidad_reservada");
        entity.Property(r => r.CantidadConsumida).HasColumnName("cantidad_consumida");
        entity.Property(r => r.CantidadLiberada).HasColumnName("cantidad_liberada");
        entity.Property(r => r.Estado).HasColumnName("estado").HasMaxLength(20).HasConversion<string>();
        entity.Property(r => r.FechaReserva).HasColumnName("fecha_reserva");
        entity.Property(r => r.FechaConsumo).HasColumnName("fecha_consumo");
        entity.Property(r => r.FechaLiberacion).HasColumnName("fecha_liberacion");
        entity.Property(r => r.UsuarioId).HasColumnName("usuario_id");
        entity.Property(r => r.Motivo).HasColumnName("motivo").HasMaxLength(1000);

        entity.HasIndex(r => new { r.DetallePedidoPersonalizadoId, r.ProductoSkuId, r.Ubicacion }).IsUnique();
        entity.HasIndex(r => r.PedidoPersonalizadoId);
        entity.HasIndex(r => r.ProductoId);
        entity.HasIndex(r => r.ProductoSkuId);
        entity.HasIndex(r => r.Estado);

        entity.HasOne(r => r.PedidoPersonalizado).WithMany(p => p.Reservas)
            .HasForeignKey(r => r.PedidoPersonalizadoId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(r => r.DetallePedidoPersonalizado).WithMany(d => d.Reservas)
            .HasForeignKey(r => r.DetallePedidoPersonalizadoId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(r => r.Producto).WithMany()
            .HasForeignKey(r => r.ProductoId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(r => r.ProductoSku).WithMany()
            .HasForeignKey(r => r.ProductoSkuId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(r => r.Usuario).WithMany()
            .HasForeignKey(r => r.UsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}
