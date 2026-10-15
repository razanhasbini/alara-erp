using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AlaraERP.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>. It never connects; applying migrations is done by
/// the application's <c>migrate</c> command using the owner connection string.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AlaraDbContext>
{
    public AlaraDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AlaraDbContext>();
        DbContextOptionsConfigurator.Configure(builder, "Host=design-time-only;Database=none");
        return new AlaraDbContext(builder.Options);
    }
}
