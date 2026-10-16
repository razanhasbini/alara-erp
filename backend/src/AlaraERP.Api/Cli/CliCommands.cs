using AlaraERP.Infrastructure.Identity;
using AlaraERP.Infrastructure.Persistence;
using AlaraERP.Infrastructure.Seeding;
using AlaraERP.Application.Common.Security;
using Npgsql;

namespace AlaraERP.Api.Cli;

/// <summary>
/// Operational commands run with <c>dotnet run --project src/AlaraERP.Api -- &lt;command&gt;</c>.
/// They run instead of the web server and exit.
/// </summary>
public static class CliCommands
{
    public static readonly string[] Names = ["migrate", "initialize", "bootstrap-admin", "provision-user", "import-catalog"];

    public static async Task<int?> TryRunAsync(WebApplication app, string[] args)
    {
        if (args.Length == 0 || !Names.Contains(args[0])) return null;

        var config = app.Configuration;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("cli");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            switch (args[0])
            {
                case "migrate":
                    await MigrateAsync(app, config, cts.Token);
                    return 0;
                case "initialize":
                    await InitializeAsync(app, config, cts.Token);
                    return 0;
                case "bootstrap-admin":
                    return await BootstrapAdminAsync(app, config, cts.Token) ? 0 : 1;
                case "provision-user":
                    if (args.Length < 6)
                        throw new InvalidOperationException("Usage: provision-user <username> <email> <first-name> <last-name> <role>");
                    var password = PromptSecret("Password: ");
                    if (PromptSecret("Repeat password: ") != password)
                        throw new InvalidOperationException("Passwords do not match.");
                    await using (var scope = app.Services.CreateAsyncScope())
                    {
                        await scope.ServiceProvider.GetRequiredService<UserProvisioner>().RunAsync(
                            new ProvisionUserOptions(args[1], args[2], password, args[3], args[4], args[5]), cts.Token);
                    }
                    Console.WriteLine($"Account '{args[1]}' is ready.");
                    return 0;
                case "import-catalog":
                    if (args.Length < 2)
                        throw new InvalidOperationException("Usage: import-catalog <path-to-catalog.json>");
                    await using (var scope = app.Services.CreateAsyncScope())
                    {
                        var result = await scope.ServiceProvider.GetRequiredService<CatalogImporter>()
                            .ImportAsync(Path.GetFullPath(args[1]), cts.Token);
                        Console.WriteLine($"Categories created: {result.CategoriesCreated}, products: {result.ProductsCreated}, " +
                                          $"bundles: {result.BundlesCreated}, skipped (already present): {result.Skipped}, " +
                                          $"warehouse MAIN created: {result.WarehouseCreated}");
                    }
                    return 0;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Command '{Command}' failed.", args[0]);
            return 1;
        }
        return null;
    }

    private static async Task MigrateAsync(WebApplication app, IConfiguration config, CancellationToken ct)
    {
        var ownerConnection = config.GetConnectionString("Migrations")
            ?? throw new InvalidOperationException("Connection string 'Migrations' (schema owner) is not configured.");
        var appConnection = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' (runtime role) is not configured.");
        var appRole = new NpgsqlConnectionStringBuilder(appConnection).Username
            ?? throw new InvalidOperationException("Connection string 'Default' has no Username.");

        await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync(ownerConnection, appRole, ct);
    }

    /// <summary>
    /// Idempotent container startup: update the schema, optionally import the packaged catalogue, and create
    /// configured initial accounts only when they do not exist. Existing passwords are never reset on restart.
    /// </summary>
    private static async Task InitializeAsync(WebApplication app, IConfiguration config, CancellationToken ct)
    {
        await MigrateAsync(app, config, ct);

        await using var scope = app.Services.CreateAsyncScope();
        if (config.GetValue<bool>("Bootstrap:ImportCatalog"))
        {
            var path = config["Bootstrap:CatalogPath"] ?? "/app/data/catalog/alaraline-catalog.json";
            var result = await scope.ServiceProvider.GetRequiredService<CatalogImporter>()
                .ImportAsync(Path.GetFullPath(path), ct);
            Console.WriteLine($"Catalogue ready: {result.CategoriesCreated} categories, {result.ProductsCreated} products, " +
                              $"{result.BundlesCreated} bundles created; {result.Skipped} already existed.");
        }

        await EnsureConfiguredUserAsync(scope.ServiceProvider, config.GetSection("Bootstrap:SuperAdmin"),
            SystemRoles.SuperAdmin, ct);
        await EnsureConfiguredUserAsync(scope.ServiceProvider, config.GetSection("Bootstrap:Employee"),
            SystemRoles.SalesEmployee, ct);
    }

    private static async Task EnsureConfiguredUserAsync(IServiceProvider services, IConfigurationSection section,
        string defaultRole, CancellationToken ct)
    {
        var userName = section["UserName"];
        var email = section["Email"];
        var password = section["Password"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            return;

        var users = services.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        if (await users.FindByNameAsync(userName.Trim()) is not null || await users.FindByEmailAsync(email.Trim()) is not null)
            return;

        await services.GetRequiredService<UserProvisioner>().RunAsync(new ProvisionUserOptions(
            userName, email, password,
            section["FirstName"] ?? userName,
            section["LastName"] ?? "",
            section["Role"] ?? defaultRole), ct);
    }

    private static async Task<bool> BootstrapAdminAsync(WebApplication app, IConfiguration config, CancellationToken ct)
    {
        var section = config.GetSection("Bootstrap");
        var email = section["AdminEmail"] ?? Prompt("Admin email: ");
        var first = section["AdminFirstName"] ?? Prompt("First name: ");
        var last = section["AdminLastName"] ?? Prompt("Last name: ");
        var password = section["AdminPassword"];
        if (string.IsNullOrEmpty(password))
        {
            password = PromptSecret("Password (min 10 chars, upper, lower, digit): ");
            if (PromptSecret("Repeat password: ") != password)
                throw new InvalidOperationException("Passwords do not match.");
        }

        await using var scope = app.Services.CreateAsyncScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();
        return await bootstrapper.RunAsync(new BootstrapAdminOptions(email, password, first, last), ct);
    }

    private static string Prompt(string label)
    {
        Console.Write(label);
        return Console.ReadLine()?.Trim() is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{label.TrimEnd(':', ' ')} is required.");
    }

    /// <summary>Reads a secret without echoing it, so it never lands in shell history or logs.</summary>
    private static string PromptSecret(string label)
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("Run this command interactively to enter the password securely.");
        Console.Write(label);
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); continue; }
            if (!char.IsControl(key.KeyChar)) chars.Add(key.KeyChar);
        }
        Console.WriteLine();
        return new string(chars.ToArray());
    }
}
