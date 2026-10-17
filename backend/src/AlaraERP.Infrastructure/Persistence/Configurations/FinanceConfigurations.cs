using AlaraERP.Domain.Finance;
using AlaraERP.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlaraERP.Infrastructure.Persistence.Configurations;

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        b.ToTable("expenses", Schemas.Finance, t => t.HasCheckConstraint("ck_expenses_amount", "amount > 0"));
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Category).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Amount).HasPrecision(12, 2);
        b.Property(e => e.Description).HasMaxLength(Expense.DescriptionMaxLength);
        b.Property(e => e.Notes).HasMaxLength(2000);
        b.HasIndex(e => e.IncurredOn);
        b.HasIndex(e => new { e.Category, e.IncurredOn });
        b.HasOne<AppUser>().WithMany().HasForeignKey(e => e.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
