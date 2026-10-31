using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public sealed class AlertaReposicionInventarioConfiguration : IEntityTypeConfiguration<AlertaReposicionInventario>
{
    public void Configure(EntityTypeBuilder<AlertaReposicionInventario> entity)
    {
        entity.ToTable("alertas_reposicion_inventario", table =>
        {
            table.HasCheckConstraint(
                "CK_alertas_reposicion_descarte_con_motivo",
                "estado <> 'DESCARTADA' OR length(trim(motivo_descarte)) > 0");
        });

        entity.HasKey(alerta => alerta.AlertaReposicionInventarioId);
        entity.Property(alerta => alerta.AlertaReposicionInventarioId).HasColumnName("alerta_reposicion_inventario_id");
        entity.Property(alerta => alerta.ProductoSkuId).HasColumnName("producto_sku_id");
        entity.Property(alerta => alerta.Estado).HasColumnName("estado").HasMaxLength(20).HasConversion<string>();
        entity.Property(alerta => alerta.PospuestaHasta).HasColumnName("pospuesta_hasta");
        entity.Property(alerta => alerta.MotivoDescarte).HasColumnName("motivo_descarte").HasMaxLength(500);
        entity.Property(alerta => alerta.UsuarioActualizacionId).HasColumnName("usuario_actualizacion_id");
        entity.Property(alerta => alerta.FechaCreacion).HasColumnName("fecha_creacion");
        entity.Property(alerta => alerta.FechaActualizacion).HasColumnName("fecha_actualizacion");

        entity.HasIndex(alerta => alerta.ProductoSkuId).IsUnique();
        entity.HasOne(alerta => alerta.ProductoSku)
            .WithOne(sku => sku.AlertaReposicion)
            .HasForeignKey<AlertaReposicionInventario>(alerta => alerta.ProductoSkuId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(alerta => alerta.UsuarioActualizacion)
            .WithMany()
            .HasForeignKey(alerta => alerta.UsuarioActualizacionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
