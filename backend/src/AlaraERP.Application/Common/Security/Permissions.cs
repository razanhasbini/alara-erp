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
        /// <summary>Fix a miscount: set stock to the number actually counted. Everyday staff work, unlike write-offs.</summary>
        public const string CorrectCounts = "inventory.correct_counts";
        public const string Transfer = "inventory.transfer";
        public const string ManageThresholds = "inventory.manage_thresholds";
    }

    /// <summary>Packaging boxes: counts, contents and alert levels. Everyday staff work, so every role but Viewer manages them.</summary>
    public static class Boxes
    {
        public const string View = "boxes.view";
        public const string Manage = "boxes.manage";
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
        public const string ManagePayments = "orders.manage_payments";
    }

    /// <summary>The activity log: who did what and when. Granted only to Super Admin.</summary>
    public static class Activity
    {
        public const string View = "activity.view";
    }

    /// <summary>Bills and running costs (electricity, internet, models, wages). Granted only to Super Admin.</summary>
    public static class Expenses
    {
        public const string Manage = "expenses.manage";
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
