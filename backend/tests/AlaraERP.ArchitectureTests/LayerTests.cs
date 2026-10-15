using System.Reflection;
using NetArchTest.Rules;

namespace AlaraERP.ArchitectureTests;

public sealed class LayerTests
{
    private static readonly Assembly Domain = typeof(Domain.Common.IAuditable).Assembly;
    private static readonly Assembly Application = typeof(Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.DependencyInjection).Assembly;

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        Assert.True(result.IsSuccessful,
            $"{assembly.GetName().Name} must not depend on {string.Join(", ", forbidden)}. Offenders: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Domain_depends_on_nothing_else_in_the_solution_or_on_frameworks() =>
        AssertNoDependency(Domain,
            "AlaraERP.Application", "AlaraERP.Infrastructure", "AlaraERP.Api",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "FluentValidation");

    [Fact]
    public void Application_does_not_depend_on_infrastructure_web_or_database_driver() =>
        AssertNoDependency(Application,
            "AlaraERP.Infrastructure", "AlaraERP.Api", "Microsoft.AspNetCore", "Npgsql");

    [Fact]
    public void Infrastructure_does_not_depend_on_the_api() =>
        AssertNoDependency(Infrastructure, "AlaraERP.Api");
}
