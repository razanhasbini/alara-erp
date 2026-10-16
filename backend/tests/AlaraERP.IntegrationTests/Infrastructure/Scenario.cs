using AlaraERP.Application.Catalog;
using AlaraERP.Application.Inventory;
using AlaraERP.Application.Sales;
using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Sales;
using Npgsql;

namespace AlaraERP.IntegrationTests.Infrastructure;

/// <summary>Builds test data through the public API, exactly as the SPA would.</summary>
public sealed class Scenario(ErpClient client, AlaraApiFactory factory)
{
    public static DateOnly InAYear => DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);

    public async Task<WarehouseDto> WarehouseAsync(string code = "MAIN") =>
        await (await client.PostAsync("/api/warehouses", new WarehouseRequest($"Warehouse {code}", code, null))).OkJsonAsync<WarehouseDto>();

    /// <summary>
    /// Products get a saved cost by default, because stock can only be received with a cost. The cost is written the
    /// way the admin would set it, so scenarios run by roles that can't see costs still work.
    /// </summary>
    public async Task<ProductDetailDto> ProductAsync(string key, decimal price, bool tracksExpiry = true, string? name = null, decimal? standardCost = 4m)
    {
        var product = await (await client.PostAsync("/api/products", new CreateProductRequest(
            Type: ProductType.Individual, Name: name ?? $"Product {key}", Slug: null, CategoryId: null,
            ShortDescription: null, Description: null, HowToUse: null, Ingredients: null, SizeValue: null, SizeUnit: null,
            WeightGrams: null, SellingPrice: price, CompareAtPrice: null, BundleItems: null, StandardCost: null))).OkJsonAsync<ProductDetailDto>();
        if (standardCost is { } cost)
            await ExecuteAsync("UPDATE catalog.products SET standard_cost = @cost WHERE id = @id", ("cost", cost), ("id", product.Id));
        if (!tracksExpiry)
            (await client.PutAsync($"/api/inventory/settings/{product.Id}", new UpdateInventorySettingsRequest(0, false)))
                .EnsureSuccessStatusCode();
        return product;
    }

    public async Task<ProductDetailDto> BundleAsync(string key, decimal price, params (Guid ProductId, int Quantity)[] items) =>
        await (await client.PostAsync("/api/products", new CreateProductRequest(
            Type: ProductType.Bundle, Name: $"Bundle {key}", Slug: null, CategoryId: null,
            ShortDescription: null, Description: null, HowToUse: null, Ingredients: null, SizeValue: null, SizeUnit: null,
            WeightGrams: null, SellingPrice: price, CompareAtPrice: null,
            BundleItems: items.Select(i => new BundleItemInput(i.ProductId, i.Quantity)).ToList()))).OkJsonAsync<ProductDetailDto>();

    public async Task<ReceiptDto> ReceiveAsync(Guid warehouseId, Guid productId, int quantity, DateOnly? expiry = null,
        string? batch = null, decimal? unitCost = null) =>
        await (await client.PostAsync("/api/inventory/receipts", new ReceiveStockRequest(warehouseId, "Supplier", "PO-1", null, null,
            [new ReceiveLineInput(productId, quantity, unitCost, batch, expiry ?? InAYear)]))).OkJsonAsync<ReceiptDto>();

    /// <summary>confirm = sell now; otherwise the order is reserved for a day.</summary>
    public async Task<HttpResponseMessage> CreateOrderAsync(Guid warehouseId, bool confirm, decimal discount = 0,
        decimal? shipping = null, params (Guid ProductId, int Quantity)[] lines) =>
        await client.PostAsync("/api/orders", new CreateOrderRequest(
            OrderSource.WhatsApp, warehouseId, PaymentMethod.CashOnDelivery,
            new RecipientInput("Rima Saad", "+961 70 123 456", "Beirut", "Hamra, Bliss St."),
            discount, shipping, null,
            lines.Select(l => new OrderLineInput(l.ProductId, l.Quantity)).ToList(), confirm,
            ReserveUntil: confirm ? null : DateTimeOffset.UtcNow.AddDays(1)));

    public static DateTimeOffset Tomorrow => DateTimeOffset.UtcNow.AddDays(1);

    public async Task<OrderDetailDto> OrderAsync(Guid warehouseId, bool confirm, params (Guid ProductId, int Quantity)[] lines) =>
        await (await CreateOrderAsync(warehouseId, confirm, lines: lines)).OkJsonAsync<OrderDetailDto>();

    public Task<ProductStockDto> StockAsync(Guid productId) => client.GetJsonAsync<ProductStockDto>($"/api/inventory/products/{productId}");

    public async Task<int> AvailableAsync(Guid productId, Guid? warehouseId = null)
    {
        var url = $"/api/inventory/availability?productIds={productId}" + (warehouseId is { } w ? $"&warehouseId={w}" : "");
        return (await client.GetJsonAsync<List<AvailabilityDto>>(url)).Single().Available;
    }

    /// <summary>Test-only shortcut to simulate the passage of time for a batch.</summary>
    public async Task SetBatchExpiryAsync(Guid batchId, DateOnly expiry)
    {
        await using var conn = new NpgsqlConnection(factory.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE inventory.batches SET expiry_date = @e WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("e", expiry);
        cmd.Parameters.AddWithValue("id", batchId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Test-only: run a statement as the schema owner (e.g. to move a deadline into the past).</summary>
    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(factory.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<long> CountAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(factory.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}

public static class ScenarioResponseExtensions
{
    public static async Task<T> OkJsonAsync<T>(this HttpResponseMessage response)
    {
        await response.EnsureSuccessWithBodyAsync();
        return await response.ReadJsonAsync<T>();
    }
}
