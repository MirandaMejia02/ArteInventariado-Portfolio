using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class PedidoPersonalizadoConfiguration : IEntityTypeConfiguration<PedidoPersonalizado>
{
    public void Configure(EntityTypeBuilder<PedidoPersonalizado> entity)
    {
        entity.ToTable("pedidos_personalizados", table =>
        {
            table.HasCheckConstraint("CK_pedidos_codigo_obligatorio", "length(trim(codigo_pedido)) > 0");
            table.HasCheckConstraint("CK_pedidos_destinatario_obligatorio", "length(trim(nombre_destinatario_snapshot)) > 0");
            table.HasCheckConstraint("CK_pedidos_fecha_prometida_valida", "fecha_prometida >= fecha_pedido");
            table.HasCheckConstraint("CK_pedidos_subtotal_no_negativo", "subtotal_centavos >= 0");
            table.HasCheckConstraint("CK_pedidos_descuento_valido", "descuento_general_centavos >= 0 AND descuento_general_centavos <= subtotal_centavos");
            table.HasCheckConstraint("CK_pedidos_total_cuadra", "total_estimado_centavos = subtotal_centavos - descuento_general_centavos");
            table.HasCheckConstraint("CK_pedidos_totales_financieros",
                "total_pagado_centavos >= 0 AND total_reembolsado_centavos >= 0 AND saldo_pendiente_centavos >= 0");
        });

        entity.HasKey(p => p.PedidoPersonalizadoId);
        entity.Property(p => p.PedidoPersonalizadoId).HasColumnName("pedido_personalizado_id");
        entity.Property(p => p.CodigoPedido).HasColumnName("codigo_pedido").HasMaxLength(30).IsRequired();
        entity.Property(p => p.CotizacionId).HasColumnName("cotizacion_id");
        entity.Property(p => p.ClienteId).HasColumnName("cliente_id");
        entity.Property(p => p.NombreDestinatarioSnapshot).HasColumnName("nombre_destinatario_snapshot").HasMaxLength(180).IsRequired();
        entity.Property(p => p.TelefonoSnapshot).HasColumnName("telefono_snapshot").HasMaxLength(40);
        entity.Property(p => p.CorreoSnapshot).HasColumnName("correo_snapshot").HasMaxLength(180);
        entity.Property(p => p.DireccionSnapshot).HasColumnName("direccion_snapshot").HasMaxLength(500);
        entity.Property(p => p.Estado).HasColumnName("estado").HasMaxLength(30).HasConversion<string>().IsRequired();
        entity.Property(p => p.Prioridad).HasColumnName("prioridad").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(p => p.FechaPedido).HasColumnName("fecha_pedido").IsRequired();
        entity.Property(p => p.FechaPrometida).HasColumnName("fecha_prometida").IsRequired();
        entity.Property(p => p.FechaInicio).HasColumnName("fecha_inicio");
        entity.Property(p => p.FechaFinalizacion).HasColumnName("fecha_finalizacion");
        entity.Property(p => p.FechaEntregaReal).HasColumnName("fecha_entrega_real");
        entity.Property(p => p.SubtotalCentavos).HasColumnName("subtotal_centavos").IsRequired();
        entity.Property(p => p.DescuentoGeneralCentavos).HasColumnName("descuento_general_centavos").IsRequired();
        entity.Property(p => p.TotalEstimadoCentavos).HasColumnName("total_estimado_centavos").IsRequired();
        entity.Property(p => p.EstadoFinanciero).HasColumnName("estado_financiero").HasMaxLength(30).HasConversion<string>().IsRequired();
        entity.Property(p => p.TotalPagadoCentavos).HasColumnName("total_pagado_centavos").IsRequired();
        entity.Property(p => p.TotalReembolsadoCentavos).HasColumnName("total_reembolsado_centavos").IsRequired();
        entity.Property(p => p.SaldoPendienteCentavos).HasColumnName("saldo_pendiente_centavos").IsRequired();
        entity.Property(p => p.VentaId).HasColumnName("venta_id");
        entity.Property(p => p.OperacionConversionId).HasColumnName("operacion_conversion_id");
        entity.Property(p => p.FechaConversion).HasColumnName("fecha_conversion");
        entity.Property(p => p.UsuarioConversionId).HasColumnName("usuario_conversion_id");
        entity.Property(p => p.DescripcionGeneral).HasColumnName("descripcion_general").HasMaxLength(2000);
        entity.Property(p => p.EspecificacionesGenerales).HasColumnName("especificaciones_generales").HasMaxLength(4000);
        entity.Property(p => p.ObservacionesInternas).HasColumnName("observaciones_internas").HasMaxLength(4000);
        entity.Property(p => p.NotasCliente).HasColumnName("notas_cliente").HasMaxLength(2000);
        entity.Property(p => p.UsuarioCreadorId).HasColumnName("usuario_creador_id");
        entity.Property(p => p.UsuarioUltimaActualizacionId).HasColumnName("usuario_ultima_actualizacion_id");
        entity.Property(p => p.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();
        entity.Property(p => p.FechaActualizacion).HasColumnName("fecha_actualizacion").IsRequired();
        entity.Property(p => p.FechaCancelacion).HasColumnName("fecha_cancelacion");
        entity.Property(p => p.MotivoCancelacion).HasColumnName("motivo_cancelacion").HasMaxLength(1000);

        entity.HasIndex(p => p.CodigoPedido).IsUnique();
        entity.HasIndex(p => p.CotizacionId).IsUnique();
        entity.HasIndex(p => p.ClienteId);
        entity.HasIndex(p => p.Estado);
        entity.HasIndex(p => p.Prioridad);
        entity.HasIndex(p => p.FechaPedido);
        entity.HasIndex(p => p.FechaPrometida);
        entity.HasIndex(p => p.EstadoFinanciero);
        entity.HasIndex(p => p.VentaId).IsUnique();
        entity.HasIndex(p => p.OperacionConversionId).IsUnique();

        entity.HasOne(p => p.Cotizacion).WithOne(c => c.PedidoPersonalizado)
            .HasForeignKey<PedidoPersonalizado>(p => p.CotizacionId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(p => p.Cliente).WithMany(c => c.PedidosPersonalizados)
            .HasForeignKey(p => p.ClienteId).OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(p => p.UsuarioCreador).WithMany(u => u.PedidosPersonalizadosCreados)
            .HasForeignKey(p => p.UsuarioCreadorId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(p => p.UsuarioUltimaActualizacion).WithMany(u => u.PedidosPersonalizadosActualizados)
            .HasForeignKey(p => p.UsuarioUltimaActualizacionId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(p => p.Venta).WithOne()
            .HasForeignKey<PedidoPersonalizado>(p => p.VentaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(p => p.UsuarioConversion).WithMany()
            .HasForeignKey(p => p.UsuarioConversionId).OnDelete(DeleteBehavior.Restrict);
    }
}
