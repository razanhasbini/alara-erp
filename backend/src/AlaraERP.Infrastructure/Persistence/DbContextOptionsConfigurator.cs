using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Infrastructure.Persistence;

internal static class DbContextOptionsConfigurator
{
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(AlaraDbContext).Assembly.FullName)
                .MigrationsHistoryTable(Schemas.MigrationsHistoryTable, Schemas.MigrationsHistorySchema))
            .UseSnakeCaseNamingConvention();
}
