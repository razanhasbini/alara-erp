using AlaraERP.Domain.Notifications;
using AlaraERP.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlaraERP.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications", Schemas.Notifications);
        b.HasKey(n => n.Id);
        b.Property(n => n.Type).HasMaxLength(60).IsRequired();
        b.Property(n => n.Title).HasMaxLength(200).IsRequired();
        b.Property(n => n.Body).HasMaxLength(2000).IsRequired();
        b.Property(n => n.Link).HasMaxLength(300);
        b.Property(n => n.RequiredPermission).HasMaxLength(60);
        b.Property(n => n.ReferenceType).HasMaxLength(30);
        b.HasIndex(n => n.CreatedAt);
        b.HasIndex(n => new { n.ReferenceType, n.ReferenceId });
    }
}

internal sealed class NotificationReadConfiguration : IEntityTypeConfiguration<NotificationRead>
{
    public void Configure(EntityTypeBuilder<NotificationRead> b)
    {
        b.ToTable("notification_reads", Schemas.Notifications);
        b.HasKey(r => new { r.NotificationId, r.UserId });
        b.HasIndex(r => r.UserId);
        b.HasOne<Notification>().WithMany().HasForeignKey(r => r.NotificationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WhatsAppMessageConfiguration : IEntityTypeConfiguration<WhatsAppMessage>
{
    public void Configure(EntityTypeBuilder<WhatsAppMessage> b)
    {
        b.ToTable("whatsapp_messages", Schemas.Notifications, t => t.HasCheckConstraint("ck_whatsapp_messages_attempts", "attempts >= 0"));
        b.HasKey(m => m.Id);
        b.Property(m => m.ToPhone).HasMaxLength(40).IsRequired();
        b.Property(m => m.Body).HasMaxLength(4000).IsRequired();
        b.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(m => m.LastError).HasMaxLength(1000);
        b.Property(m => m.ProviderMessageId).HasMaxLength(200);
        b.HasIndex(m => new { m.Status, m.CreatedAt });
        b.HasIndex(m => m.NotificationId);
        b.HasIndex(m => m.ProviderMessageId);
        b.HasOne<Notification>().WithMany().HasForeignKey(m => m.NotificationId).OnDelete(DeleteBehavior.Cascade);
    }
}
