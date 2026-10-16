using System.Reflection;

namespace AlaraERP.Application.Common.Security;

/// <summary>
/// Named permissions. Stored as role claims of type <see cref="ClaimType"/> and enforced by the API.
/// Never rename an existing value without a data migration — the strings are persisted.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public static class Products
    {
        public const string View = "products.view";
        public const string Create = "products.create";
        public const string Update = "products.update";
        public const string Delete = "products.delete";
        public const string ManagePricing = "products.manage_pricing";
    }

    public static class Inventory
    {
        public const string View = "inventory.view";
        public const string Receive = "inventory.receive";
        public const string Adjust = "inventory.adjust";
        public const string Transfer = "inventory.transfer";
        public const string ManageThresholds = "inventory.manage_thresholds";
    }

    public static class Warehouses
    {
        public const string Manage = "warehouses.manage";
    }

    public static class Orders
    {
        public const string View = "orders.view";
        public const string Create = "orders.create";
        public const string Update = "orders.update";
        public const string Cancel = "orders.cancel";
        /// <summary>Hard-delete test orders. Intentionally granted only through Super Admin's full permission set.</summary>
        public const string Delete = "orders.delete";
        public const string ManagePayments = "orders.manage_payments";
    }

    public static class DeliveryCompanies
    {
        public const string Manage = "delivery_companies.manage";
    }

    public static class Reports
    {
        public const string View = "reports.view";
    }

    /// <summary>Purchase costs, cost of goods and profit. Deliberately separate from reports.view.</summary>
    public static class Costs
    {
        public const string View = "costs.view";
    }

    public static class Users
    {
        public const string View = "users.view";
        public const string Create = "users.create";
        public const string Update = "users.update";
        public const string AssignRoles = "users.assign_roles";
    }

    /// <summary>Every permission defined above.</summary>
    public static IReadOnlyList<string> All { get; } = typeof(Permissions)
        .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
        .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .OrderBy(p => p, StringComparer.Ordinal)
        .ToArray();
}
