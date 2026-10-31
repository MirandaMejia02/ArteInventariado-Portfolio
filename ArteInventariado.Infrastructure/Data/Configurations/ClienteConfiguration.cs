using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public class ClienteConfiguration : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> entity)
    {
        entity.ToTable("clientes");

        entity.HasKey(c => c.ClienteId);

        entity.Property(c => c.ClienteId).HasColumnName("cliente_id");
        entity.Property(c => c.CodigoCliente).HasColumnName("codigo_cliente").HasMaxLength(30).IsRequired();
        entity.Property(c => c.NombreCompleto).HasColumnName("nombre_completo").HasMaxLength(180).IsRequired();
        entity.Property(c => c.NombreNormalizado).HasColumnName("nombre_normalizado").HasMaxLength(220).IsRequired();
        entity.Property(c => c.TelefonoCelular).HasColumnName("telefono_celular").HasMaxLength(40);
        entity.Property(c => c.TelefonoNormalizado).HasColumnName("telefono_normalizado").HasMaxLength(40);
        entity.Property(c => c.WhatsApp).HasColumnName("whatsapp").HasMaxLength(40);
        entity.Property(c => c.CorreoElectronico).HasColumnName("correo_electronico").HasMaxLength(180);
        entity.Property(c => c.CorreoNormalizado).HasColumnName("correo_normalizado").HasMaxLength(180);
        entity.Property(c => c.Direccion).HasColumnName("direccion").HasMaxLength(500);
        entity.Property(c => c.DocumentoIdentidad).HasColumnName("documento_identidad").HasMaxLength(40);
        entity.Property(c => c.DocumentoNormalizado).HasColumnName("documento_normalizado").HasMaxLength(40);
        entity.Property(c => c.Notas).HasColumnName("notas").HasMaxLength(1000);
        entity.Property(c => c.Estado).HasColumnName("estado").HasMaxLength(20).HasConversion<string>().IsRequired();
        entity.Property(c => c.CreditoHabilitado).HasColumnName("credito_habilitado").HasDefaultValue(true).IsRequired();
        entity.Property(c => c.CreditoBloqueoMotivo).HasColumnName("credito_bloqueo_motivo").HasMaxLength(500);
        entity.Property(c => c.CreditoLimiteCentavos).HasColumnName("credito_limite_centavos");
        entity.Property(c => c.CreditoMaximoCuentasActivas).HasColumnName("credito_maximo_cuentas_activas");
        entity.Property(c => c.CreditoPlazoMaximoDias).HasColumnName("credito_plazo_maximo_dias");
        entity.Property(c => c.CreditoPermitirSaldoVigente).HasColumnName("credito_permitir_saldo_vigente");
        entity.Property(c => c.CreditoInteresPorcentaje)
            .HasColumnName("credito_interes_porcentaje")
            .HasPrecision(9, 4);
        entity.Property(c => c.CreditoUsuarioActualizacionId).HasColumnName("credito_usuario_actualizacion_id");
        entity.Property(c => c.CreditoFechaActualizacion).HasColumnName("credito_fecha_actualizacion");
        entity.Property(c => c.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();
        entity.Property(c => c.FechaActualizacion).HasColumnName("fecha_actualizacion").IsRequired();
        entity.Property(c => c.UsuarioCreacionId).HasColumnName("usuario_creacion_id").IsRequired();
        entity.Property(c => c.UsuarioActualizacionId).HasColumnName("usuario_actualizacion_id");

        entity.HasIndex(c => c.CodigoCliente).IsUnique();
        entity.HasIndex(c => c.NombreNormalizado);
        entity.HasIndex(c => c.TelefonoNormalizado);
        entity.HasIndex(c => c.CorreoNormalizado);
        entity.HasIndex(c => c.DocumentoNormalizado).IsUnique().HasFilter("documento_normalizado IS NOT NULL");
        entity.HasIndex(c => c.Estado);

        entity.HasOne(c => c.UsuarioCreacion)
            .WithMany()
            .HasForeignKey(c => c.UsuarioCreacionId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(c => c.UsuarioActualizacion)
            .WithMany()
            .HasForeignKey(c => c.UsuarioActualizacionId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne(c => c.CreditoUsuarioActualizacion)
            .WithMany()
            .HasForeignKey(c => c.CreditoUsuarioActualizacionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
