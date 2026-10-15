using System.Reflection;
using NetArchTest.Rules;

namespace AlaraERP.ArchitectureTests;

/// <summary>
/// Module dependency rules inside the modular monolith. Upstream modules never know about downstream ones;
/// Sales changes stock only through Inventory's public contracts.
///   Catalog  ←  Inventory  ←  Sales        (Dashboard reads everything)
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly Domain = typeof(Domain.Common.IAuditable).Assembly;
    private static readonly Assembly Application = typeof(Application.DependencyInjection).Assembly;

    private static void Assert_(TestResult result, string rule) =>
        Assert.True(result.IsSuccessful, $"{rule}. Offenders: {string.Join(", ", result.FailingTypeNames ?? [])}");

    [Fact]
    public void Catalog_domain_does_not_know_inventory_or_sales() =>
        Assert_(Types.InAssembly(Domain).That().ResideInNamespace("AlaraERP.Domain.Catalog")
            .ShouldNot().HaveDependencyOnAny("AlaraERP.Domain.Inventory", "AlaraERP.Domain.Sales").GetResult(),
            "Catalog domain must not depend on Inventory or Sales");

    [Fact]
    public void Inventory_domain_does_not_know_sales() =>
        Assert_(Types.InAssembly(Domain).That().ResideInNamespace("AlaraERP.Domain.Inventory")
            .ShouldNot().HaveDependencyOn("AlaraERP.Domain.Sales").GetResult(),
            "Inventory domain must not depend on Sales");

    [Fact]
    public void Sales_domain_does_not_know_inventory() =>
        Assert_(Types.InAssembly(Domain).That().ResideInNamespace("AlaraERP.Domain.Sales")
            .ShouldNot().HaveDependencyOn("AlaraERP.Domain.Inventory").GetResult(),
            "Sales domain must not depend on Inventory");

    [Fact]
    public void Catalog_application_does_not_use_inventory_or_sales() =>
        Assert_(Types.InAssembly(Application).That().ResideInNamespace("AlaraERP.Application.Catalog")
            .ShouldNot().HaveDependencyOnAny(
                "AlaraERP.Application.Inventory", "AlaraERP.Application.Sales",
                "AlaraERP.Domain.Inventory", "AlaraERP.Domain.Sales").GetResult(),
            "Catalog application services must not use Inventory or Sales");

    [Fact]
    public void Inventory_application_does_not_use_sales() =>
        Assert_(Types.InAssembly(Application).That().ResideInNamespace("AlaraERP.Application.Inventory")
            .ShouldNot().HaveDependencyOnAny("AlaraERP.Application.Sales", "AlaraERP.Domain.Sales").GetResult(),
            "Inventory application services must not use Sales");

    [Fact]
    public void Sales_reaches_inventory_only_through_its_contracts()
    {
        var sales = Types.InAssembly(Application).That().ResideInNamespace("AlaraERP.Application.Sales").GetTypes();
        var offenders = sales
            .Where(t => Types.InAssembly(Application).That().HaveName(t.Name).And().ResideInNamespace(t.Namespace!)
                .ShouldNot().HaveDependencyOnAny("AlaraERP.Domain.Inventory").GetResult().IsSuccessful == false)
            .Select(t => t.FullName)
            .ToList();
        Assert.True(offenders.Count == 0, $"Sales must not touch Inventory entities: {string.Join(", ", offenders)}");

        var internals = Types.InAssembly(Application).That().ResideInNamespace("AlaraERP.Application.Inventory")
            .And().DoNotResideInNamespace("AlaraERP.Application.Inventory.Contracts")
            .GetTypes().Select(t => t.FullName!).ToArray();
        var result = Types.InAssembly(Application).That().ResideInNamespace("AlaraERP.Application.Sales")
            .ShouldNot().HaveDependencyOnAny(internals).GetResult();
        Assert_(result, "Sales may use Inventory.Contracts only");
    }
}
