using P = AlaraERP.Application.Common.Security.Permissions;

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

        // Costs (unit costs, receipt costs, profit) are for the Super Admin only.
        new(Manager, "Runs day-to-day operations: catalogue, pricing, inventory, orders, payments and reports. Does not see costs.",
            new HashSet<string>
            {
                P.Products.View, P.Products.Create, P.Products.Update, P.Products.Delete, P.Products.ManagePricing,
                P.Inventory.View, P.Inventory.Receive, P.Inventory.Adjust, P.Inventory.Transfer, P.Inventory.ManageThresholds,
                P.Warehouses.Manage,
                P.Orders.View, P.Orders.Create, P.Orders.Update, P.Orders.Cancel, P.Orders.ManagePayments,
                P.DeliveryCompanies.Manage,
                P.Reports.View,
                P.Users.View,
            }),

        new(InventoryManager, "Receives, adjusts and transfers stock.",
            new HashSet<string>
            {
                P.Products.View,
                P.Inventory.View, P.Inventory.Receive, P.Inventory.Adjust, P.Inventory.Transfer,
            }),

        new(SalesEmployee, "Handles the day-to-day catalogue, customers, orders, receipts and stock records.",
            new HashSet<string>
            {
                P.Products.View, P.Products.Create, P.Products.Update,
                P.Inventory.View, P.Inventory.Receive,
                P.Orders.View, P.Orders.Create, P.Orders.Update, P.Orders.Cancel,
            }),

        new(Viewer, "Read-only access to catalogue, inventory, orders and reports.",
            new HashSet<string>
            {
                P.Products.View, P.Inventory.View, P.Orders.View, P.Reports.View,
            }),
    ];

    public static SystemRoleDefinition? Find(string name) =>
        All.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
}
