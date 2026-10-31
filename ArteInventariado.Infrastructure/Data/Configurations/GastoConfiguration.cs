using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class GastoConfiguration : IEntityTypeConfiguration<Gasto>
{
    public void Configure(EntityTypeBuilder<Gasto> entity)
    {
        entity.ToTable("gastos", table =>
        {
            table.HasCheckConstraint("CK_gastos_monto_positivo", "monto_centavos > 0");
            table.HasCheckConstraint("CK_gastos_concepto_obligatorio", "length(trim(concepto)) > 0");
        });

        entity.HasKey(g => g.GastoId);

        entity.Property(g => g.GastoId).HasColumnName("gasto_id");
        entity.Property(g => g.CajaId).HasColumnName("caja_id");
        entity.Property(g => g.UsuarioId).HasColumnName("usuario_id");
        entity.Property(g => g.CategoriaGastoId).HasColumnName("categoria_gasto_id");
        entity.Property(g => g.Concepto).HasColumnName("concepto").HasMaxLength(200).IsRequired();
        entity.Property(g => g.MontoCentavos).HasColumnName("monto_centavos").IsRequired();
        entity.Property(g => g.FechaGasto).HasColumnName("fecha_gasto").IsRequired();
        entity.Property(g => g.Observaciones).HasColumnName("observaciones");
        entity.Property(g => g.MetodoPago)
            .HasColumnName("metodo_pago")
            .HasMaxLength(30)
            .HasConversion<string>();
        entity.Property(g => g.Referencia).HasColumnName("referencia").HasMaxLength(180);
        entity.Property(g => g.OperacionId).HasColumnName("operacion_id");
        entity.Property(g => g.OrigenFondos)
            .HasColumnName("origen_fondos")
            .HasMaxLength(40)
            .HasConversion<string>();
        entity.Property(g => g.ResponsableOrigen).HasColumnName("responsable_origen").HasMaxLength(160);

        entity.HasIndex(g => g.CajaId);
        entity.HasIndex(g => g.UsuarioId);
        entity.HasIndex(g => g.CategoriaGastoId);
        entity.HasIndex(g => g.FechaGasto);
        entity.HasIndex(g => g.OperacionId).IsUnique();

        entity.HasOne(g => g.Caja)
            .WithMany(c => c.Gastos)
            .HasForeignKey(g => g.CajaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(g => g.Usuario)
            .WithMany(u => u.Gastos)
            .HasForeignKey(g => g.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(g => g.CategoriaGasto)
            .WithMany(category => category.Gastos)
            .HasForeignKey(g => g.CategoriaGastoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
