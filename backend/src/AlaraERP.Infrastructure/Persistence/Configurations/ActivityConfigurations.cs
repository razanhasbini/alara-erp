using AlaraERP.Domain.Activity;
using AlaraERP.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlaraERP.Infrastructure.Persistence.Configurations;

internal sealed class ActivityEntryConfiguration : IEntityTypeConfiguration<ActivityEntry>
{
    public void Configure(EntityTypeBuilder<ActivityEntry> b)
    {
        b.ToTable("activity_log", Schemas.Audit);
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Area).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Summary).HasMaxLength(ActivityEntry.SummaryMaxLength).IsRequired();
        b.Property(e => e.Link).HasMaxLength(300);
        b.HasIndex(e => e.At);
        b.HasIndex(e => new { e.UserId, e.At });
        b.HasIndex(e => new { e.Area, e.At });
        b.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
