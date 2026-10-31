using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public sealed class EmployeePaymentConfiguration : IEntityTypeConfiguration<EmployeePayment>
{
    public void Configure(EntityTypeBuilder<EmployeePayment> entity)
    {
        entity.ToTable("employee_payments", table =>
        {
            table.HasCheckConstraint("CK_employee_payments_amount_positive", "amount_centavos > 0");
            table.HasCheckConstraint(
                "CK_employee_payments_period_order",
                "period_start IS NULL OR period_end IS NULL OR period_end >= period_start");
            table.HasCheckConstraint(
                "CK_employee_payments_days_non_negative",
                "days_considered IS NULL OR days_considered >= 0");
            table.HasCheckConstraint(
                "CK_employee_payments_cancelled_state",
                "status <> 'Cancelled' OR (cancelled_by_user_id IS NOT NULL AND cancelled_at_utc IS NOT NULL AND length(trim(coalesce(cancellation_reason, ''))) > 0)");
            table.HasCheckConstraint(
                "CK_employee_payments_funding_effect",
                "origen_fondos IS NULL OR " +
                "(origen_fondos = 'CAJA_FISICA' AND caja_id IS NOT NULL AND metodo_pago = 'EFECTIVO') OR " +
                "(origen_fondos IN ('FONDOS_EXTERNOS_DUENO', 'FONDOS_NEGOCIO_FUERA_CAJA') AND caja_id IS NULL " +
                "AND length(trim(coalesce(responsable_origen, ''))) > 0 AND metodo_pago IS NOT NULL)");
        });

        entity.HasKey(payment => payment.EmployeePaymentId);
        entity.Property(payment => payment.EmployeePaymentId)
            .HasColumnName("employee_payment_id");
        entity.Property(payment => payment.UserId)
            .HasColumnName("usuario_id")
            .IsRequired();
        entity.Property(payment => payment.CajaId)
            .HasColumnName("caja_id");
        entity.Property(payment => payment.AmountCentavos)
            .HasColumnName("amount_centavos")
            .IsRequired();
        entity.Property(payment => payment.PaymentType)
            .HasColumnName("payment_type")
            .HasMaxLength(24)
            .HasConversion<string>()
            .IsRequired();
        entity.Property(payment => payment.MetodoPago)
            .HasColumnName("metodo_pago")
            .HasMaxLength(30)
            .HasConversion<string>();
        entity.Property(payment => payment.Referencia)
            .HasColumnName("referencia")
            .HasMaxLength(180);
        entity.Property(payment => payment.OperacionId)
            .HasColumnName("operacion_id");
        entity.Property(payment => payment.OrigenFondos)
            .HasColumnName("origen_fondos")
            .HasMaxLength(40)
            .HasConversion<string>();
        entity.Property(payment => payment.ResponsableOrigen)
            .HasColumnName("responsable_origen")
            .HasMaxLength(160);
        entity.Property(payment => payment.PeriodStart)
            .HasColumnName("period_start");
        entity.Property(payment => payment.PeriodEnd)
            .HasColumnName("period_end");
        entity.Property(payment => payment.DaysConsidered)
            .HasColumnName("days_considered");
        entity.Property(payment => payment.Note)
            .HasColumnName("note")
            .HasMaxLength(1000);
        entity.Property(payment => payment.RegisteredByUserId)
            .HasColumnName("registered_by_user_id")
            .IsRequired();
        entity.Property(payment => payment.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();
        entity.Property(payment => payment.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion<string>()
            .IsRequired();
        entity.Property(payment => payment.CancelledByUserId)
            .HasColumnName("cancelled_by_user_id");
        entity.Property(payment => payment.CancelledAtUtc)
            .HasColumnName("cancelled_at_utc");
        entity.Property(payment => payment.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(1000);

        entity.HasIndex(payment => new { payment.UserId, payment.CreatedAtUtc });
        entity.HasIndex(payment => new { payment.PeriodStart, payment.PeriodEnd });
        entity.HasIndex(payment => payment.Status);
        entity.HasIndex(payment => payment.CajaId);
        entity.HasIndex(payment => payment.OperacionId).IsUnique();

        entity.HasOne(payment => payment.Caja)
            .WithMany(cash => cash.EmployeePayments)
            .HasForeignKey(payment => payment.CajaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(payment => payment.User)
            .WithMany(user => user.EmployeePayments)
            .HasForeignKey(payment => payment.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(payment => payment.RegisteredBy)
            .WithMany()
            .HasForeignKey(payment => payment.RegisteredByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(payment => payment.CancelledBy)
            .WithMany()
            .HasForeignKey(payment => payment.CancelledByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
