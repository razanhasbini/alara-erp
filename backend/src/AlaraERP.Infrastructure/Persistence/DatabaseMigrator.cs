using System.Text.RegularExpressions;
using AlaraERP.Application.Common.Security;
using AlaraERP.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlaraERP.Infrastructure.Persistence;

/// <summary>
/// Applies EF Core migrations as the schema-owner role, grants the runtime role DML-only access,
/// and synchronises the system roles/permissions defined in code. Never drops or resets anything.
/// </summary>
public sealed partial class DatabaseMigrator(ILogger<DatabaseMigrator> logger)
{
    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex SafeIdentifier();

    public async Task MigrateAsync(string ownerConnectionString, string appRole, CancellationToken ct)
    {
        if (!SafeIdentifier().IsMatch(appRole))
            throw new ArgumentException($"Invalid database role name '{appRole}'.", nameof(appRole));

        var options = DbContextOptionsConfigurator
            .Configure(new DbContextOptionsBuilder<AlaraDbContext>(), ownerConnectionString)
            .Options;
        await using var db = new AlaraDbContext((DbContextOptions<AlaraDbContext>)options);

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync(ct);

        await GrantRuntimeAccessAsync(db, appRole, ct);
        await SyncSystemRolesAsync(db, ct);
        logger.LogInformation("Database is up to date.");
    }

    private async Task GrantRuntimeAccessAsync(AlaraDbContext db, string appRole, CancellationToken ct)
    {
        var existing = await db.Database
            .SqlQuery<string>($"SELECT schema_name AS \"Value\" FROM information_schema.schemata")
            .ToListAsync(ct);

        foreach (var schema in Schemas.All.Where(existing.Contains))
        {
            // Identifiers are constants or regex-validated above; they cannot be parameterised in DDL.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"""
                GRANT USAGE ON SCHEMA {schema} TO {appRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schema} TO {appRole};
                GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA {schema} TO {appRole};
                ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {appRole};
                ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO {appRole};
                """, ct);
#pragma warning restore EF1002
        }
        await HardenLedgerTablesAsync(db, appRole, ct);
        logger.LogInformation("Granted DML access on {Schemas} to role {Role}.", string.Join(", ", Schemas.All.Where(existing.Contains)), appRole);
    }

    /// <summary>Append-only audit tables: the runtime role may insert and read, never rewrite history.</summary>
    private static async Task HardenLedgerTablesAsync(AlaraDbContext db, string appRole, CancellationToken ct)
    {
        foreach (var table in AppendOnlyTables)
        {
            var exists = await db.Database
                .SqlQuery<bool>($"SELECT to_regclass({table}) IS NOT NULL AS \"Value\"")
                .SingleAsync(ct);
            if (!exists) continue;
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"REVOKE UPDATE, DELETE ON {table} FROM {appRole};", ct);
#pragma warning restore EF1002
        }
    }

    /// <summary>Tables that must never be updated or deleted by the application.</summary>
    public static readonly string[] AppendOnlyTables = ["inventory.stock_movements", "sales.order_history", "audit.activity_log"];

    private async Task SyncSystemRolesAsync(AlaraDbContext db, CancellationToken ct)
    {
        foreach (var def in SystemRoles.All)
        {
            var normalized = def.Name.ToUpperInvariant();
            var role = await db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == normalized, ct);
            if (role is null)
            {
                role = new AppRole { Id = Guid.CreateVersion7(), Name = def.Name, NormalizedName = normalized };
                db.Roles.Add(role);
                logger.LogInformation("Created role {Role}.", def.Name);
            }
            role.Description = def.Description;
            role.IsSystem = true;
            role.ConcurrencyStamp = Guid.NewGuid().ToString();

            var claims = await db.RoleClaims
                .Where(c => c.RoleId == role.Id && c.ClaimType == Permissions.ClaimType)
                .ToListAsync(ct);
            var current = claims.Select(c => c.ClaimValue!).ToHashSet();

            db.RoleClaims.RemoveRange(claims.Where(c => !def.Permissions.Contains(c.ClaimValue!)));
            foreach (var permission in def.Permissions.Except(current))
                db.RoleClaims.Add(new IdentityRoleClaim<Guid> { RoleId = role.Id, ClaimType = Permissions.ClaimType, ClaimValue = permission });
        }
        await db.SaveChangesAsync(ct);
    }
}
