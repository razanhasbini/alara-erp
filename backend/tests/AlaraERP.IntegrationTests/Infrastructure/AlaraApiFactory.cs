using AlaraERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AlaraERP.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real API against the dedicated PostgreSQL test database. The schema is rebuilt from
/// migrations once per test run; data is truncated before each test. Refuses to touch any database
/// whose name does not end in "_test".
/// </summary>
public sealed class AlaraApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly IConfiguration Secrets = new ConfigurationBuilder()
        .AddUserSecrets<AlaraApiFactory>()
        .AddEnvironmentVariables()
        .Build();

    public string AppConnectionString { get; } = Require("TestApp");
    public string OwnerConnectionString { get; } = Require("TestOwner");

    private static string Require(string name)
    {
        var cs = Secrets.GetConnectionString(name)
                 ?? throw new InvalidOperationException($"ConnectionStrings:{name} is not configured (user-secrets or env ConnectionStrings__{name}).");
        var database = new NpgsqlConnectionStringBuilder(cs).Database ?? "";
        if (!database.EndsWith("_test", StringComparison.Ordinal))
            throw new InvalidOperationException($"Refusing to run tests against database '{database}': its name must end with '_test'.");
        return cs;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = AppConnectionString,
            ["Auth:LoginAttemptsPerMinute"] = "100000",
            // Re-validate the session on every request so deactivation/role tests are deterministic.
            ["Auth:SessionRevalidationInterval"] = "00:00:00",
            ["Logging:LogLevel:Default"] = "Warning",
            // Uploaded test images go to a throwaway folder, never the API's App_Data.
            ["Storage:LocalPath"] = Path.Combine(Path.GetTempPath(), "alara-erp-tests", Guid.NewGuid().ToString("N")),
        }));
    }

    public async Task InitializeAsync()
    {
        await using (var conn = new NpgsqlConnection(OwnerConnectionString))
        {
            await conn.OpenAsync();
            var drop = string.Join(", ", Schemas.All);
            await using var cmd = new NpgsqlCommand(
                $"DROP SCHEMA IF EXISTS {drop} CASCADE; DROP TABLE IF EXISTS {Schemas.MigrationsHistorySchema}.\"{Schemas.MigrationsHistoryTable}\";", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        var appRole = new NpgsqlConnectionStringBuilder(AppConnectionString).Username!;
        await new DatabaseMigrator(NullLogger<DatabaseMigrator>.Instance)
            .MigrateAsync(OwnerConnectionString, appRole, CancellationToken.None);
    }

    /// <summary>Removes all business data and users, keeping roles/permissions (they come from migrate).</summary>
    public async Task ResetDataAsync()
    {
        await using var conn = new NpgsqlConnection(OwnerConnectionString);
        await conn.OpenAsync();
        await using var list = new NpgsqlCommand("""
            SELECT string_agg(format('%I.%I', schemaname, tablename), ', ')
            FROM pg_tables
            WHERE schemaname = ANY(@schemas)
              AND (schemaname, tablename) NOT IN (('identity','roles'), ('identity','role_claims'))
            """, conn);
        list.Parameters.AddWithValue("schemas", Schemas.All);
        if (await list.ExecuteScalarAsync() is string tables)
        {
            await using var truncate = new NpgsqlCommand($"TRUNCATE {tables} RESTART IDENTITY CASCADE;", conn);
            await truncate.ExecuteNonQueryAsync();
        }

        // Most inventory scenarios exercise the long-standing expiry workflow. Production starts
        // with it off; tests opt into it here and feature-toggle tests explicitly switch it off.
        await using var features = new NpgsqlCommand("""
            INSERT INTO inventory.feature_settings (id, expiry_dates_enabled, updated_at)
            VALUES (1, TRUE, now())
            """, conn);
        await features.ExecuteNonQueryAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => base.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<AlaraApiFactory>
{
    public const string Name = "api";
}

/// <summary>Base for integration tests: fresh data per test plus helpers to create employees and sign in.</summary>
[Collection(ApiCollection.Name)]
public abstract class ApiTestBase(AlaraApiFactory factory) : IAsyncLifetime
{
    protected AlaraApiFactory Factory { get; } = factory;

    public virtual Task InitializeAsync() => Factory.ResetDataAsync();
    public virtual Task DisposeAsync() => Task.CompletedTask;

    protected ErpClient NewClient() => new(Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false,
    }));

    protected Task<TestEmployee> CreateEmployeeAsync(params string[] roles) =>
        TestEmployees.CreateAsync(Factory.Services, roles);

    protected async Task<ErpClient> SignedInAsync(TestEmployee employee)
    {
        var client = NewClient();
        var response = await client.LoginAsync(employee.Email, employee.Password);
        response.EnsureSuccessStatusCode();
        return client;
    }

    protected async Task<ErpClient> SignedInAsAsync(params string[] roles) => await SignedInAsync(await CreateEmployeeAsync(roles));

    /// <summary>A Manager session plus a scenario builder: covers catalogue, inventory, orders and payments.</summary>
    protected async Task<(ErpClient Client, Scenario Scenario)> ManagerAsync()
    {
        var client = await SignedInAsAsync(AlaraERP.Application.Common.Security.SystemRoles.Manager);
        return (client, new Scenario(client, Factory));
    }
}
