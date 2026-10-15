using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Common;
using AlaraERP.Domain.Inventory;

namespace AlaraERP.UnitTests.Domain;

public sealed class StockBalanceTests
{
    [Fact]
    public void Reservations_never_exceed_available_stock()
    {
        var balance = new StockBalance(Guid.NewGuid(), Guid.NewGuid());
        balance.Add(10);
        balance.Reserve(7, "Mask");

        Assert.Equal("inventory.insufficient_stock", Assert.Throws<DomainException>(() => balance.Reserve(4, "Mask")).Code);
        Assert.Equal("inventory.insufficient_available", Assert.Throws<DomainException>(() => balance.RemoveUnreserved(4, "Mask")).Code);
        Assert.Equal((10, 7, 3), (balance.QuantityOnHand, balance.QuantityReserved, balance.Available));
    }

    [Fact]
    public void Fulfilling_converts_a_reservation_into_a_deduction()
    {
        var balance = new StockBalance(Guid.NewGuid(), Guid.NewGuid());
        balance.Add(10);
        balance.Reserve(4, "Mask");

        balance.FulfilReserved(4);

        Assert.Equal((6, 0), (balance.QuantityOnHand, balance.QuantityReserved));
        Assert.Throws<InvalidOperationException>(() => balance.FulfilReserved(1)); // nothing reserved any more
    }

    [Fact]
    public void Movement_sign_must_match_its_type()
    {
        Assert.Throws<InvalidOperationException>(() => new StockMovement(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            MovementType.Shipment, 5, MovementReference.Order, Guid.NewGuid(), "AL-1", null, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => new StockMovement(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            MovementType.Receipt, 0, MovementReference.Receipt, Guid.NewGuid(), "RC-1", null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Batch_expiry_is_compared_against_the_business_date()
    {
        var batch = new Batch(Guid.NewGuid(), "L1", new DateOnly(2026, 10, 15), DateTimeOffset.UtcNow);
        Assert.False(batch.IsExpiredOn(new DateOnly(2026, 10, 15))); // usable through its expiry date
        Assert.True(batch.IsExpiredOn(new DateOnly(2026, 10, 16)));
    }
}

public sealed class ProductTests
{
    [Fact]
    public void Prices_are_rounded_to_cents_and_compare_at_must_not_undercut()
    {
        var product = new Product(ProductType.Individual, "Mask", "mask", "ALR-MASK", 24.005m, null);
        Assert.Equal(24.01m, product.SellingPrice);
        Assert.Equal("products.invalid_compare_at_price",
            Assert.Throws<DomainException>(() => product.SetPricing(30m, 25m)).Code);
    }

    [Fact]
    public void Only_bundles_have_components()
    {
        var shampoo = new Product(ProductType.Individual, "Shampoo", "s", "S", 15m, null);
        var other = new Product(ProductType.Individual, "Mask", "m", "M", 24m, null);
        Assert.Equal("bundles.not_a_bundle", Assert.Throws<DomainException>(() => shampoo.ReplaceBundleItems([(other, 1)])).Code);
    }

    [Fact]
    public void Removing_the_primary_image_promotes_the_next_one()
    {
        var product = new Product(ProductType.Individual, "Mask", "m", "M", 24m, null);
        var first = product.AddImage("products/a/1.png", "image/png", 10);
        var second = product.AddImage("products/a/2.png", "image/png", 10);

        product.RemoveImage(first.Id);

        Assert.True(second.IsPrimary);
    }
}
