using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class ReembolsoPedidoPersonalizadoConfiguration : IEntityTypeConfiguration<ReembolsoPedidoPersonalizado>
{
    public void Configure(EntityTypeBuilder<ReembolsoPedidoPersonalizado> entity)
    {
        entity.ToTable("reembolsos_pedido_personalizado", table =>
        {
            table.HasCheckConstraint("CK_reembolso_pedido_monto_positivo", "monto_centavos > 0");
            table.HasCheckConstraint("CK_reembolso_pedido_motivo_obligatorio", "length(trim(motivo)) > 0");
            table.HasCheckConstraint("CK_reembolso_pedido_saldos_no_negativos", "saldo_anterior_centavos >= 0 AND saldo_nuevo_centavos >= 0");
        });

        entity.HasKey(r => r.ReembolsoPedidoPersonalizadoId);
        entity.Property(r => r.ReembolsoPedidoPersonalizadoId).HasColumnName("reembolso_pedido_personalizado_id");
        entity.Property(r => r.CodigoReembolso).HasColumnName("codigo_reembolso").HasMaxLength(30).IsRequired();
        entity.Property(r => r.OperacionId).HasColumnName("operacion_id");
        entity.Property(r => r.PedidoPersonalizadoId).HasColumnName("pedido_personalizado_id");
        entity.Property(r => r.PagoPedidoPersonalizadoId).HasColumnName("pago_pedido_personalizado_id");
        entity.Property(r => r.MetodoPago).HasColumnName("metodo_pago").HasMaxLength(20).HasConversion<string>();
        entity.Property(r => r.MontoCentavos).HasColumnName("monto_centavos");
        entity.Property(r => r.Referencia).HasColumnName("referencia").HasMaxLength(200);
        entity.Property(r => r.Motivo).HasColumnName("motivo").HasMaxLength(1000).IsRequired();
        entity.Property(r => r.Fecha).HasColumnName("fecha");
        entity.Property(r => r.UsuarioId).HasColumnName("usuario_id");
        entity.Property(r => r.AfectaCaja).HasColumnName("afecta_caja");
        entity.Property(r => r.CajaId).HasColumnName("caja_id");
        entity.Property(r => r.SaldoAnteriorCentavos).HasColumnName("saldo_anterior_centavos");
        entity.Property(r => r.SaldoNuevoCentavos).HasColumnName("saldo_nuevo_centavos");
        entity.Property(r => r.FechaCreacion).HasColumnName("fecha_creacion");

        entity.HasIndex(r => r.CodigoReembolso).IsUnique();
        entity.HasIndex(r => r.OperacionId).IsUnique();
        entity.HasIndex(r => r.PedidoPersonalizadoId);
        entity.HasIndex(r => r.PagoPedidoPersonalizadoId);
        entity.HasIndex(r => r.CajaId);
        entity.HasIndex(r => r.Fecha);

        entity.HasOne(r => r.PedidoPersonalizado).WithMany(p => p.Reembolsos)
            .HasForeignKey(r => r.PedidoPersonalizadoId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(r => r.PagoPedidoPersonalizado).WithMany(p => p.Reembolsos)
            .HasForeignKey(r => r.PagoPedidoPersonalizadoId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(r => r.Usuario).WithMany()
            .HasForeignKey(r => r.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(r => r.Caja).WithMany()
            .HasForeignKey(r => r.CajaId).OnDelete(DeleteBehavior.Restrict);
    }
}
