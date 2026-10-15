using System.Net;
using System.Net.Http.Headers;
using AlaraERP.Application.Catalog;
using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Domain.Catalog;
using AlaraERP.IntegrationTests.Infrastructure;

namespace AlaraERP.IntegrationTests.Business;

public sealed class CatalogTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    private static CreateProductRequest Individual(string sku, decimal price, decimal? compareAt = null) =>
        new(ProductType.Individual, "Curl Defining Shampoo", sku, null, null, null, null, null, null, null,
            250, SizeUnit.Ml, 300, price, compareAt, null);

    [Fact]
    public async Task Creates_a_product_with_normalised_sku_and_generated_slug()
    {
        var (client, _) = await ManagerAsync();

        var response = await client.PostAsync("/api/products", Individual("alr-co-sham", 16m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = await response.ReadJsonAsync<ProductDetailDto>();
        Assert.Equal("ALR-CO-SHAM", product.Sku);
        Assert.Equal("curl-defining-shampoo", product.Slug);
        Assert.Equal(16.00m, product.SellingPrice);
        Assert.Equal(250m, product.SizeValue);
        Assert.True(product.IsActive);
    }

    [Fact]
    public async Task Duplicate_sku_is_rejected_regardless_of_case()
    {
        var (client, _) = await ManagerAsync();
        (await client.PostAsync("/api/products", Individual("ALR-DUP", 10m))).EnsureSuccessStatusCode();

        var response = await client.PostAsync("/api/products", Individual("alr-dup", 12m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("products.duplicate_sku", await response.ProblemCodeAsync());
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(20, 15)] // compare-at below selling price
    public async Task Invalid_prices_are_rejected(decimal price, int? compareAt)
    {
        var (client, _) = await ManagerAsync();

        var response = await client.PostAsync("/api/products", Individual($"ALR-P{Guid.NewGuid():N}"[..20], price, compareAt));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Price_with_more_than_two_decimals_is_rejected()
    {
        var (client, _) = await ManagerAsync();

        var response = await client.PostAsync("/api/products", Individual("ALR-CENTS", 10.005m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bundle_lists_components_and_their_full_price_value()
    {
        var (client, s) = await ManagerAsync();
        var shampoo = await s.ProductAsync("ALR-T-SHAM", 16m);
        var cream = await s.ProductAsync("ALR-T-CREAM", 26m);

        var bundle = await s.BundleAsync("ALR-T-SET", 35m, (shampoo.Id, 1), (cream.Id, 1));

        Assert.Equal(ProductType.Bundle, bundle.Type);
        Assert.Equal(2, bundle.BundleItems.Count);
        Assert.Equal(42m, bundle.ComponentsValue);
    }

    [Fact]
    public async Task Bundles_cannot_contain_bundles_or_repeat_a_component()
    {
        var (client, s) = await ManagerAsync();
        var shampoo = await s.ProductAsync("ALR-T-SHAM", 16m);
        var inner = await s.BundleAsync("ALR-T-INNER", 15m, (shampoo.Id, 1));

        var nested = await client.PostAsync("/api/products", new CreateProductRequest(ProductType.Bundle, "Outer", "ALR-T-OUTER",
            null, null, null, null, null, null, null, null, null, null, 20m, null, [new BundleItemInput(inner.Id, 1)]));
        var repeated = await client.PutAsync($"/api/products/{inner.Id}/bundle-items",
            new ReplaceBundleItemsRequest([new BundleItemInput(shampoo.Id, 1), new BundleItemInput(shampoo.Id, 2)]));
        var self = await client.PutAsync($"/api/products/{inner.Id}/bundle-items",
            new ReplaceBundleItemsRequest([new BundleItemInput(inner.Id, 1)]));

        Assert.Equal("bundles.nested_bundle", await nested.ProblemCodeAsync());
        Assert.Equal("bundles.duplicate_component", await repeated.ProblemCodeAsync());
        Assert.Equal("bundles.self_reference", await self.ProblemCodeAsync());
    }

    [Fact]
    public async Task Category_hierarchy_cannot_form_a_cycle()
    {
        var (client, _) = await ManagerAsync();
        var hair = await (await client.PostAsync("/api/categories", new CategoryRequest("Hair Care", null, null, null))).OkJsonAsync<CategoryDto>();
        var curls = await (await client.PostAsync("/api/categories", new CategoryRequest("Curls On", null, null, hair.Id))).OkJsonAsync<CategoryDto>();

        var cycle = await client.PutAsync($"/api/categories/{hair.Id}", new CategoryRequest("Hair Care", null, null, curls.Id));
        var self = await client.PutAsync($"/api/categories/{hair.Id}", new CategoryRequest("Hair Care", null, null, hair.Id));

        Assert.Equal("categories.cycle", await cycle.ProblemCodeAsync());
        Assert.Equal("categories.self_parent", await self.ProblemCodeAsync());
        var list = await client.GetJsonAsync<List<CategoryDto>>("/api/categories");
        Assert.Equal(["Hair Care", "Curls On"], list.Select(c => c.Name));
    }

    [Fact]
    public async Task Products_with_history_cannot_be_deleted_but_unused_ones_can()
    {
        var (client, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var used = await s.ProductAsync("ALR-T-USED", 10m);
        var unused = await s.ProductAsync("ALR-T-UNUSED", 10m);
        await s.ReceiveAsync(warehouse.Id, used.Id, 5);

        var blocked = await client.DeleteAsync($"/api/products/{used.Id}");
        var deleted = await client.DeleteAsync($"/api/products/{unused.Id}");

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("products.in_use", await blocked.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task List_filters_by_search_type_and_category_with_paging()
    {
        var (client, s) = await ManagerAsync();
        var hair = await (await client.PostAsync("/api/categories", new CategoryRequest("Hair Care", null, null, null))).OkJsonAsync<CategoryDto>();
        for (var i = 0; i < 3; i++)
        {
            var p = await s.ProductAsync($"ALR-MIST-{i}", 20m, name: $"Hair Mist {i}");
            var detail = await client.GetJsonAsync<ProductDetailDto>($"/api/products/{p.Id}");
            (await client.PutAsync($"/api/products/{p.Id}", new UpdateProductRequest(detail.Name, detail.Sku, null, hair.Id,
                null, null, null, null, null, null, null, null, detail.SellingPrice, null))).EnsureSuccessStatusCode();
        }
        await s.ProductAsync("ALR-BRUSH", 13m, name: "Curly Hair Brush");

        var mists = await client.GetJsonAsync<PagedResult<ProductListItemDto>>("/api/products?search=mist&pageSize=2&page=2");
        var inCategory = await client.GetJsonAsync<PagedResult<ProductListItemDto>>($"/api/products?categoryId={hair.Id}");

        Assert.Equal(3, mists.TotalCount);
        Assert.Single(mists.Items);
        Assert.Equal(3, inCategory.TotalCount);
        Assert.All(inCategory.Items, p => Assert.Equal("Hair Care", p.CategoryName));
    }

    [Fact]
    public async Task Sales_and_inventory_staff_cannot_edit_the_catalogue()
    {
        using var sales = await SignedInAsAsync(SystemRoles.SalesEmployee);
        using var stock = await SignedInAsAsync(SystemRoles.InventoryManager);

        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsync("/api/products", Individual("ALR-X", 1m))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stock.PostAsync("/api/products", Individual("ALR-Y", 1m))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sales.GetAsync("/api/products")).StatusCode);
    }

    [Fact]
    public async Task Images_are_validated_by_content_stored_and_served()
    {
        var (client, s) = await ManagerAsync();
        var product = await s.ProductAsync("ALR-IMG", 10m);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

        var first = await UploadAsync(client, product.Id, png, "photo.png", "image/png");
        var second = await UploadAsync(client, product.Id, png, "photo2.png", "image/png");
        var fake = await UploadAsync(client, product.Id, "not an image"u8.ToArray(), "evil.png", "image/png");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstImage = await first.ReadJsonAsync<ProductImageDto>();
        var secondImage = await second.ReadJsonAsync<ProductImageDto>();
        Assert.True(firstImage.IsPrimary);
        Assert.False(secondImage.IsPrimary);
        Assert.Equal("images.invalid_type", await fake.ProblemCodeAsync());

        var served = await client.GetAsync($"/api/products/{product.Id}/images/{firstImage.Id}");
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await served.Content.ReadAsByteArrayAsync());

        (await client.PutAsync($"/api/products/{product.Id}/images/{secondImage.Id}/primary", new { })).EnsureSuccessStatusCode();
        var detail = await client.GetJsonAsync<ProductDetailDto>($"/api/products/{product.Id}");
        Assert.Equal(secondImage.Id, detail.Images.Single(i => i.IsPrimary).Id);
    }

    private static Task<HttpResponseMessage> UploadAsync(ErpClient client, Guid productId, byte[] bytes, string name, string type)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        content.Add(file, "file", name);
        return client.Http.PostAsync($"/api/products/{productId}/images", content);
    }
}
