using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArteInventariado.Infrastructure.Data.Configurations;

public sealed class EmployeeLoginEventConfiguration : IEntityTypeConfiguration<EmployeeLoginEvent>
{
    public void Configure(EntityTypeBuilder<EmployeeLoginEvent> entity)
    {
        entity.ToTable("employee_login_events");
        entity.HasKey(x => x.EmployeeLoginEventId);
        entity.Property(x => x.EmployeeLoginEventId).HasColumnName("employee_login_event_id");
        entity.Property(x => x.UserId).HasColumnName("usuario_id").IsRequired();
        entity.Property(x => x.LoggedInAtUtc).HasColumnName("logged_in_at_utc").IsRequired();
        entity.Property(x => x.InstallationId).HasColumnName("installation_id").HasMaxLength(100);
        entity.Property(x => x.StationMode).HasColumnName("station_mode").HasMaxLength(20);
        entity.HasIndex(x => new { x.UserId, x.LoggedInAtUtc });
        entity.HasOne(x => x.User).WithMany(u => u.LoginEvents).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
