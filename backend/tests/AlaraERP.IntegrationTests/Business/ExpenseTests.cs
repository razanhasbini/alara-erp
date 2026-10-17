using System.Net;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Dashboard;
using AlaraERP.Application.Finance;
using AlaraERP.Domain.Finance;
using AlaraERP.IntegrationTests.Infrastructure;

namespace AlaraERP.IntegrationTests.Business;

public sealed class ExpenseTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Bills_are_recorded_edited_deleted_and_totalled_by_category()
    {
        var (admin, _) = await AdminAsync();
        async Task<ExpenseDto> Add(ExpenseCategory category, decimal amount, DateOnly on, string? description = null) =>
            await (await admin.PostAsync("/api/expenses", new ExpenseRequest(category, on, amount, description, null))).OkJsonAsync<ExpenseDto>();

        var wages = await Add(ExpenseCategory.Wages, 400m, Today, "Diana");
        await Add(ExpenseCategory.Electricity, 55.5m, Today);
        var models = await Add(ExpenseCategory.Models, 150m, Today, "Photo shoot");
        await Add(ExpenseCategory.Internet, 30m, Today.AddMonths(-3)); // outside the range below

        var edited = await (await admin.PutAsync($"/api/expenses/{wages.Id}",
            new ExpenseRequest(ExpenseCategory.Wages, Today, 450m, "Diana — this month", "Includes a bonus"))).OkJsonAsync<ExpenseDto>();
        (await admin.DeleteAsync($"/api/expenses/{models.Id}")).EnsureSuccessStatusCode();
        var from = Today.AddDays(-7);
        var list = await admin.GetJsonAsync<ExpenseListDto>($"/api/expenses?from={from:yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        var onlyWages = await admin.GetJsonAsync<ExpenseListDto>($"/api/expenses?from={from:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&category=Wages");

        Assert.Equal((450m, "Diana — this month", "Includes a bonus"), (edited.Amount, edited.Description, edited.Notes));
        Assert.NotNull(edited.RecordedBy);
        Assert.Equal((505.5m, 2), (list.Total, list.TotalCount));
        Assert.Equal(450m, list.ByCategory.Single(c => c.Category == ExpenseCategory.Wages).Amount);
        Assert.Equal(0m, list.ByCategory.Single(c => c.Category == ExpenseCategory.Models).Amount);  // deleted
        Assert.Equal(0m, list.ByCategory.Single(c => c.Category == ExpenseCategory.Internet).Amount); // other month
        Assert.Equal((1, 505.5m), (onlyWages.TotalCount, onlyWages.Total)); // the filter narrows the list, not the totals
    }

    [Fact]
    public async Task Bills_need_a_positive_amount_and_a_date_not_far_ahead()
    {
        var (admin, _) = await AdminAsync();

        var zero = await admin.PostAsync("/api/expenses", new ExpenseRequest(ExpenseCategory.Other, Today, 0m, null, null));
        var farAhead = await admin.PostAsync("/api/expenses", new ExpenseRequest(ExpenseCategory.Wages, Today.AddMonths(4), 10m, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal("expenses.date_too_far", await farAhead.ProblemCodeAsync());
    }

    [Fact]
    public async Task Only_the_admin_sees_bills_and_the_dashboard_shows_this_months_total_to_them()
    {
        var (admin, _) = await AdminAsync();
        (await admin.PostAsync("/api/expenses", new ExpenseRequest(ExpenseCategory.Internet, Today, 35m, "Wi-Fi", null))).EnsureSuccessStatusCode();
        using var manager = await SignedInAsAsync(SystemRoles.Manager);
        using var sales = await SignedInAsAsync(SystemRoles.SalesEmployee);

        var adminDashboard = await admin.GetJsonAsync<DashboardDto>("/api/dashboard");

        Assert.Equal(35m, adminDashboard.Finance!.BillsThisMonth);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/expenses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await sales.PostAsync("/api/expenses", new ExpenseRequest(ExpenseCategory.Other, Today, 5m, null, null))).StatusCode);
        Assert.Null((await manager.GetJsonAsync<DashboardDto>("/api/dashboard")).Finance!.BillsThisMonth);
    }
}
