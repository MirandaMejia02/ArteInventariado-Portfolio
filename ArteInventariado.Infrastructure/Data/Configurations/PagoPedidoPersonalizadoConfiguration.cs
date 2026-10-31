using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class PagoPedidoPersonalizadoConfiguration : IEntityTypeConfiguration<PagoPedidoPersonalizado>
{
    public void Configure(EntityTypeBuilder<PagoPedidoPersonalizado> entity)
    {
        entity.ToTable("pagos_pedido_personalizado", table =>
        {
            table.HasCheckConstraint("CK_pago_pedido_monto_positivo", "monto_centavos > 0");
            table.HasCheckConstraint("CK_pago_pedido_saldos_no_negativos", "saldo_anterior_centavos >= 0 AND saldo_nuevo_centavos >= 0");
            table.HasCheckConstraint("CK_pago_pedido_codigo_obligatorio", "length(trim(codigo_pago)) > 0");
        });

        entity.HasKey(p => p.PagoPedidoPersonalizadoId);
        entity.Property(p => p.PagoPedidoPersonalizadoId).HasColumnName("pago_pedido_personalizado_id");
        entity.Property(p => p.CodigoPago).HasColumnName("codigo_pago").HasMaxLength(30).IsRequired();
        entity.Property(p => p.OperacionId).HasColumnName("operacion_id").IsRequired();
        entity.Property(p => p.PedidoPersonalizadoId).HasColumnName("pedido_personalizado_id");
        entity.Property(p => p.TipoPago).HasColumnName("tipo_pago").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(p => p.MetodoPago).HasColumnName("metodo_pago").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(p => p.MontoCentavos).HasColumnName("monto_centavos");
        entity.Property(p => p.Referencia).HasColumnName("referencia").HasMaxLength(200);
        entity.Property(p => p.Fecha).HasColumnName("fecha");
        entity.Property(p => p.UsuarioId).HasColumnName("usuario_id");
        entity.Property(p => p.Observaciones).HasColumnName("observaciones").HasMaxLength(1000);
        entity.Property(p => p.AfectaCaja).HasColumnName("afecta_caja");
        entity.Property(p => p.CajaId).HasColumnName("caja_id");
        entity.Property(p => p.SaldoAnteriorCentavos).HasColumnName("saldo_anterior_centavos");
        entity.Property(p => p.SaldoNuevoCentavos).HasColumnName("saldo_nuevo_centavos");
        entity.Property(p => p.VentaIdAplicada).HasColumnName("venta_id_aplicada");
        entity.Property(p => p.FechaAplicacionVenta).HasColumnName("fecha_aplicacion_venta");
        entity.Property(p => p.FechaCreacion).HasColumnName("fecha_creacion");

        entity.HasIndex(p => p.CodigoPago).IsUnique();
        entity.HasIndex(p => p.OperacionId).IsUnique();
        entity.HasIndex(p => p.PedidoPersonalizadoId);
        entity.HasIndex(p => p.CajaId);
        entity.HasIndex(p => p.VentaIdAplicada);
        entity.HasIndex(p => p.Fecha);

        entity.HasOne(p => p.PedidoPersonalizado).WithMany(p => p.Pagos)
            .HasForeignKey(p => p.PedidoPersonalizadoId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(p => p.Usuario).WithMany()
            .HasForeignKey(p => p.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(p => p.Caja).WithMany()
            .HasForeignKey(p => p.CajaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(p => p.VentaAplicada).WithMany()
            .HasForeignKey(p => p.VentaIdAplicada).OnDelete(DeleteBehavior.Restrict);
    }
}
