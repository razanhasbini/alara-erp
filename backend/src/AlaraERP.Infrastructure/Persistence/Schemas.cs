namespace AlaraERP.Infrastructure.Persistence;

public static class Schemas
{
    public const string Identity = "identity";
    public const string Catalog = "catalog";
    public const string Inventory = "inventory";
    public const string Sales = "sales";
    public const string Notifications = "notifications";

    public static readonly string[] All = [Identity, Catalog, Inventory, Sales, Notifications];

    public const string MigrationsHistoryTable = "__ef_migrations_history";
    public const string MigrationsHistorySchema = "public";
}
