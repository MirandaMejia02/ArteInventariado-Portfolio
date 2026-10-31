using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public sealed class InventarioSaldoConfiguration : IEntityTypeConfiguration<InventarioSaldo>
{
    public void Configure(EntityTypeBuilder<InventarioSaldo> entity)
    {
        entity.ToTable("inventario_saldos", table =>
        {
            table.HasCheckConstraint("CK_inventario_saldos_fisico_no_negativo", "existencia_fisica >= 0");
            table.HasCheckConstraint("CK_inventario_saldos_reservado_no_negativo", "reservado >= 0");
            table.HasCheckConstraint("CK_inventario_saldos_reservado_no_supera_fisico", "reservado <= existencia_fisica");
            table.HasCheckConstraint("CK_inventario_saldos_version_positiva", "version > 0");
        });

        entity.HasKey(saldo => saldo.InventarioSaldoId);
        entity.Property(saldo => saldo.InventarioSaldoId).HasColumnName("inventario_saldo_id");
        entity.Property(saldo => saldo.ProductoSkuId).HasColumnName("producto_sku_id");
        entity.Property(saldo => saldo.Ubicacion).HasColumnName("ubicacion").HasMaxLength(20).HasConversion<string>();
        entity.Property(saldo => saldo.ExistenciaFisica).HasColumnName("existencia_fisica");
        entity.Property(saldo => saldo.Reservado).HasColumnName("reservado");
        entity.Property(saldo => saldo.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        entity.Ignore(saldo => saldo.Disponible);

        entity.HasIndex(saldo => new { saldo.ProductoSkuId, saldo.Ubicacion }).IsUnique();
        entity.HasOne(saldo => saldo.ProductoSku)
            .WithMany(sku => sku.SaldosInventario)
            .HasForeignKey(saldo => saldo.ProductoSkuId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
