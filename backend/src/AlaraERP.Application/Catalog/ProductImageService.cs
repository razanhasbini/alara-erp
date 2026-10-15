using AlaraERP.Application.Common;
using AlaraERP.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Catalog;

public sealed class ProductImageService(IAlaraDbContext db, IFileStorage storage)
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxImagesPerProduct = 12;

    /// <summary>Validates the real file type from its bytes (not the client's claim) and stores it under a generated key.</summary>
    public async Task<ProductImageDto> UploadAsync(Guid productId, Stream content, long length, CancellationToken ct)
    {
        if (length <= 0 || length > MaxBytes)
            throw new DomainException("images.invalid_size", "Images must be between 1 byte and 5 MB.");

        var product = await db.Products.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == productId, ct)
                      ?? throw new NotFoundException("Product", productId);
        if (product.Images.Count >= MaxImagesPerProduct)
            throw new DomainException("images.too_many", $"A product can have at most {MaxImagesPerProduct} images.");

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxBytes)
            throw new DomainException("images.invalid_size", "Images must be at most 5 MB.");

        var (contentType, extension) = Sniff(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))
            ?? throw new DomainException("images.invalid_type", "Only JPEG, PNG and WebP images are accepted.");

        var key = $"products/{productId:N}/{Guid.CreateVersion7():N}.{extension}";
        buffer.Position = 0;
        await storage.SaveAsync(key, buffer, ct);

        var image = product.AddImage(key, contentType, buffer.Length);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteAsync(key, CancellationToken.None);
            throw;
        }
        return new ProductImageDto(image.Id, image.ContentType, image.SizeBytes, image.DisplayOrder, image.IsPrimary);
    }

    public async Task<ImageContent> OpenAsync(Guid productId, Guid imageId, CancellationToken ct)
    {
        var image = await db.ProductImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == productId, ct)
                    ?? throw new NotFoundException("Image", imageId);
        var stream = await storage.OpenReadAsync(image.StorageKey, ct)
                     ?? throw new NotFoundException("Image file", imageId);
        return new ImageContent(stream, image.ContentType);
    }

    public async Task DeleteAsync(Guid productId, Guid imageId, CancellationToken ct)
    {
        var product = await LoadAsync(productId, ct);
        var removed = product.RemoveImage(imageId);
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(removed.StorageKey, ct);
    }

    public async Task SetPrimaryAsync(Guid productId, Guid imageId, CancellationToken ct)
    {
        var product = await LoadAsync(productId, ct);
        product.SetPrimaryImage(imageId);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderAsync(Guid productId, ReorderImagesRequest request, CancellationToken ct)
    {
        var product = await LoadAsync(productId, ct);
        product.ReorderImages(request.ImageIds);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Domain.Catalog.Product> LoadAsync(Guid productId, CancellationToken ct) =>
        await db.Products.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == productId, ct)
        ?? throw new NotFoundException("Product", productId);

    private static (string ContentType, string Extension)? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ("image/jpeg", "jpg");
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ("image/png", "png");
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return ("image/webp", "webp");
        return null;
    }
}
