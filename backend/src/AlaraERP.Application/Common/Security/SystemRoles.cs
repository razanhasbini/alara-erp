using static AlaraERP.Application.Common.Security.Permissions;

namespace AlaraERP.Application.Common.Security;

public sealed record SystemRoleDefinition(string Name, string Description, IReadOnlySet<string> Permissions);

/// <summary>
/// Built-in roles. Their permission sets are defined here and synchronised into the database
/// by the <c>migrate</c> command, so code and data never drift apart.
/// </summary>
public static class SystemRoles
{
    public const string SuperAdmin = "Super Admin";
    public const string Manager = "Manager";
    public const string InventoryManager = "Inventory Manager";
    public const string SalesEmployee = "Sales Employee";
    public const string Viewer = "Viewer";

    public static IReadOnlyList<SystemRoleDefinition> All { get; } =
    [
        new(SuperAdmin, "Full system access, including employee and role administration.",
            Permissions.All.ToHashSet()),

        new(Manager, "Runs day-to-day operations: catalogue, pricing, inventory, orders, payments and reports.",
            new HashSet<string>
            {
                Products.View, Products.Create, Products.Update, Products.Delete, Products.ManagePricing,
                Inventory.View, Inventory.Receive, Inventory.Adjust, Inventory.Transfer, Inventory.ManageThresholds,
                Warehouses.Manage,
                Orders.View, Orders.Create, Orders.Update, Orders.Cancel, Orders.ManagePayments,
                DeliveryCompanies.Manage,
                Reports.View,
                Users.View,
            }),

        new(InventoryManager, "Receives, adjusts and transfers stock.",
            new HashSet<string>
            {
                Products.View,
                Inventory.View, Inventory.Receive, Inventory.Adjust, Inventory.Transfer,
            }),

        new(SalesEmployee, "Takes and processes orders from WhatsApp, Instagram and the website.",
            new HashSet<string>
            {
                Products.View, Inventory.View,
                Orders.View, Orders.Create, Orders.Update, Orders.Cancel,
            }),

        new(Viewer, "Read-only access to catalogue, inventory, orders and reports.",
            new HashSet<string>
            {
                Products.View, Inventory.View, Orders.View, Reports.View,
            }),
    ];

    public static SystemRoleDefinition? Find(string name) =>
        All.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
}
