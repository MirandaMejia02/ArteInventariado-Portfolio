using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class HistorialEstadoPedidoConfiguration : IEntityTypeConfiguration<HistorialEstadoPedido>
{
    public void Configure(EntityTypeBuilder<HistorialEstadoPedido> entity)
    {
        entity.ToTable("historial_estado_pedidos");
        entity.HasKey(h => h.HistorialEstadoPedidoId);
        entity.Property(h => h.HistorialEstadoPedidoId).HasColumnName("historial_estado_pedido_id");
        entity.Property(h => h.PedidoPersonalizadoId).HasColumnName("pedido_personalizado_id");
        entity.Property(h => h.EstadoAnterior).HasColumnName("estado_anterior").HasMaxLength(30).HasConversion<string>();
        entity.Property(h => h.EstadoNuevo).HasColumnName("estado_nuevo").HasMaxLength(30).HasConversion<string>().IsRequired();
        entity.Property(h => h.Motivo).HasColumnName("motivo").HasMaxLength(1000);
        entity.Property(h => h.UsuarioId).HasColumnName("usuario_id");
        entity.Property(h => h.Fecha).HasColumnName("fecha").IsRequired();
        entity.HasIndex(h => h.PedidoPersonalizadoId);
        entity.HasIndex(h => h.Fecha);
        entity.HasOne(h => h.PedidoPersonalizado).WithMany(p => p.HistorialEstados)
            .HasForeignKey(h => h.PedidoPersonalizadoId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(h => h.Usuario).WithMany(u => u.HistorialPedidosPersonalizados)
            .HasForeignKey(h => h.UsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}
