using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class CompraConfiguration : IEntityTypeConfiguration<Compra>
{
    public void Configure(EntityTypeBuilder<Compra> entity)
    {
        entity.ToTable("compras", table =>
        {
            table.HasCheckConstraint("CK_compras_codigo_obligatorio", "length(trim(codigo_compra)) > 0");
            table.HasCheckConstraint("CK_compras_subtotal_no_negativo", "subtotal_centavos >= 0");
            table.HasCheckConstraint("CK_compras_descuento_no_negativo", "descuento_centavos >= 0");
            table.HasCheckConstraint("CK_compras_total_no_negativo", "total_centavos >= 0");
            table.HasCheckConstraint("CK_compras_descuento_no_supera_subtotal", "descuento_centavos <= subtotal_centavos");
            table.HasCheckConstraint("CK_compras_pagado_no_negativo", "total_pagado_centavos >= 0");
            table.HasCheckConstraint("CK_compras_saldo_no_negativo", "saldo_pendiente_centavos >= 0");
            table.HasCheckConstraint("CK_compras_finanzas_cuadran", "total_pagado_centavos + saldo_pendiente_centavos = total_centavos");
        });

        entity.HasKey(c => c.CompraId);

        entity.Property(c => c.CompraId).HasColumnName("compra_id");
        entity.Property(c => c.CodigoCompra).HasColumnName("codigo_compra").HasMaxLength(50).IsRequired();
        entity.Property(c => c.ProveedorId).HasColumnName("proveedor_id");
        entity.Property(c => c.UsuarioRegistroId).HasColumnName("usuario_registro_id");
        entity.Property(c => c.FechaCompra).HasColumnName("fecha_compra").IsRequired();
        entity.Property(c => c.FechaRegistro).HasColumnName("fecha_registro").IsRequired();
        entity.Property(c => c.EstadoRecepcion).HasColumnName("estado_recepcion").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(c => c.SubtotalCentavos).HasColumnName("subtotal_centavos").IsRequired();
        entity.Property(c => c.DescuentoCentavos).HasColumnName("descuento_centavos").IsRequired();
        entity.Property(c => c.TotalCentavos).HasColumnName("total_centavos").IsRequired();
        entity.Property(c => c.EstadoFinanciero).HasColumnName("estado_financiero").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(c => c.TotalPagadoCentavos).HasColumnName("total_pagado_centavos").IsRequired();
        entity.Property(c => c.SaldoPendienteCentavos).HasColumnName("saldo_pendiente_centavos").IsRequired();
        entity.Property(c => c.Observaciones).HasColumnName("observaciones").HasMaxLength(1000);
        entity.Property(c => c.FechaActualizacion).HasColumnName("fecha_actualizacion").IsRequired();

        entity.HasIndex(c => c.CodigoCompra).IsUnique();
        entity.HasIndex(c => c.ProveedorId);
        entity.HasIndex(c => c.UsuarioRegistroId);
        entity.HasIndex(c => c.FechaCompra);
        entity.HasIndex(c => c.EstadoRecepcion);
        entity.HasIndex(c => c.EstadoFinanciero);

        entity.HasOne(c => c.Proveedor)
            .WithMany(p => p.Compras)
            .HasForeignKey(c => c.ProveedorId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(c => c.UsuarioRegistro)
            .WithMany(u => u.ComprasRegistradas)
            .HasForeignKey(c => c.UsuarioRegistroId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
