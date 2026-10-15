namespace AlaraERP.Infrastructure.Persistence;

public static class Schemas
{
    public const string Identity = "identity";
    public const string Catalog = "catalog";
    public const string Inventory = "inventory";
    public const string Sales = "sales";

    public static readonly string[] All = [Identity, Catalog, Inventory, Sales];

    public const string MigrationsHistoryTable = "__ef_migrations_history";
    public const string MigrationsHistorySchema = "public";
}
