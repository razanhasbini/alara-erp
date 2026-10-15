using AlaraERP.Infrastructure.Identity;
using AlaraERP.Infrastructure.Persistence;
using Npgsql;

namespace AlaraERP.Api.Cli;

/// <summary>
/// Operational commands run with <c>dotnet run --project src/AlaraERP.Api -- &lt;command&gt;</c>.
/// They run instead of the web server and exit.
/// </summary>
public static class CliCommands
{
    public static readonly string[] Names = ["migrate", "bootstrap-admin"];

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
                case "bootstrap-admin":
                    return await BootstrapAdminAsync(app, config, cts.Token) ? 0 : 1;
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
            throw new InvalidOperationException("Set Bootstrap:AdminPassword via user-secrets or run interactively.");
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
