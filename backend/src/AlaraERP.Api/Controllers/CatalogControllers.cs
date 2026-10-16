using AlaraERP.Api.Security;
using AlaraERP.Application.Catalog;
using AlaraERP.Application.Common;
using Microsoft.AspNetCore.Mvc;
using static AlaraERP.Application.Common.Security.Permissions;

namespace AlaraERP.Api.Controllers;

[Route("api/categories")]
public sealed class CategoriesController(CategoryService categories) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Products.View)]
    public Task<IReadOnlyList<CategoryDto>> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        categories.ListAsync(includeInactive, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Products.View)]
    public Task<CategoryDto> Get(Guid id, CancellationToken ct) => categories.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Products.Create)]
    [ProducesResponseType<CategoryDto>(201)]
    public async Task<IActionResult> Create(CategoryRequest request, CancellationToken ct)
    {
        var category = await categories.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = category.Id }, category);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Products.Update)]
    public Task<CategoryDto> Update(Guid id, CategoryRequest request, CancellationToken ct) => categories.UpdateAsync(id, request, ct);
}

[Route("api/products")]
public sealed class ProductsController(ProductService products, ProductImageService images) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Products.View)]
    public Task<PagedResult<ProductListItemDto>> List([FromQuery] ProductListQuery query, CancellationToken ct) =>
        products.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Products.View)]
    public Task<ProductDetailDto> Get(Guid id, CancellationToken ct) => products.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Products.Create)]
    [ProducesResponseType<ProductDetailDto>(201)]
    public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken ct)
    {
        var product = await products.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    /// <summary>Price fields additionally require <c>products.manage_pricing</c> when they change.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Products.Update)]
    public Task<ProductDetailDto> Update(Guid id, UpdateProductRequest request, CancellationToken ct) => products.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Products.Update)]
    public Task<ProductDetailDto> Activate(Guid id, CancellationToken ct) => products.SetActiveAsync(id, true, ct);

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Products.Update)]
    public Task<ProductDetailDto> Deactivate(Guid id, CancellationToken ct) => products.SetActiveAsync(id, false, ct);

    /// <summary>Only products with no stock, order or bundle history can be deleted.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Products.Delete)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/bundle-items")]
    [HasPermission(Products.Update)]
    public Task<ProductDetailDto> ReplaceBundleItems(Guid id, ReplaceBundleItemsRequest request, CancellationToken ct) =>
        products.ReplaceBundleItemsAsync(id, request, ct);

    [HttpPost("{id:guid}/images")]
    [HasPermission(Products.Update)]
    [RequestSizeLimit(ProductImageService.MaxBytes + 64 * 1024)]
    [ProducesResponseType<ProductImageDto>(201)]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var image = await images.UploadAsync(id, stream, file.Length, ct);
        return CreatedAtAction(nameof(GetImage), new { id, imageId = image.Id }, image);
    }

    /// <summary>Replaces one photo with a new file, keeping its position and "main" status.</summary>
    [HttpPut("{id:guid}/images/{imageId:guid}")]
    [HasPermission(Products.Update)]
    [RequestSizeLimit(ProductImageService.MaxBytes + 64 * 1024)]
    public async Task<ProductImageDto> ReplaceImage(Guid id, Guid imageId, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return await images.ReplaceAsync(id, imageId, stream, file.Length, ct);
    }

    [HttpGet("{id:guid}/images/{imageId:guid}")]
    [HasPermission(Products.View)]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> GetImage(Guid id, Guid imageId, CancellationToken ct)
    {
        var image = await images.OpenAsync(id, imageId, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(image.Content, image.ContentType);
    }

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    [HasPermission(Products.Update)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteImage(Guid id, Guid imageId, CancellationToken ct)
    {
        await images.DeleteAsync(id, imageId, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/images/{imageId:guid}/primary")]
    [HasPermission(Products.Update)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> SetPrimaryImage(Guid id, Guid imageId, CancellationToken ct)
    {
        await images.SetPrimaryAsync(id, imageId, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/images/order")]
    [HasPermission(Products.Update)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ReorderImages(Guid id, ReorderImagesRequest request, CancellationToken ct)
    {
        await images.ReorderAsync(id, request, ct);
        return NoContent();
    }
}
