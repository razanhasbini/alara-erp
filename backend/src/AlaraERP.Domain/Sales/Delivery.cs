using AlaraERP.Domain.Common;

namespace AlaraERP.Domain.Sales;

public sealed class DeliveryCompany : IAuditable
{
    private DeliveryCompany() { }

    public DeliveryCompany(string name, string? phone)
    {
        Id = Guid.CreateVersion7();
        IsActive = true;
        Update(name, phone, true);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string? Phone { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Update(string name, string? phone, bool isActive)
    {
        Name = name.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        IsActive = isActive;
    }
}

/// <summary>A cash hand-over from a delivery company covering one or more delivered COD orders.</summary>
public sealed class CourierRemittance
{
    private CourierRemittance() { }

    public CourierRemittance(string remittanceNumber, Guid deliveryCompanyId, IReadOnlyList<Order> orders,
        DateTimeOffset receivedAt, string? notes, Guid? recordedByUserId, DateTimeOffset createdAt)
    {
        if (orders.Count == 0)
            throw new DomainException("remittances.no_orders", "Select at least one order.");
        if (orders.Any(o => o.DeliveryCompanyId != deliveryCompanyId))
            throw new DomainException("remittances.wrong_company", "All orders must have been delivered by this company.");

        Id = Guid.CreateVersion7();
        RemittanceNumber = remittanceNumber;
        DeliveryCompanyId = deliveryCompanyId;
        AmountCollected = Guard.Money(orders.Sum(o => o.GrandTotal));
        CourierFees = Guard.Money(orders.Sum(o => o.CourierFee ?? 0));
        NetAmount = AmountCollected - CourierFees;
        OrderCount = orders.Count;
        ReceivedAt = receivedAt;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        RecordedByUserId = recordedByUserId;
        CreatedAt = createdAt;

        foreach (var order in orders)
            order.MarkRemitted(Id, recordedByUserId, createdAt);
    }

    public Guid Id { get; private set; }
    public string RemittanceNumber { get; private set; } = "";
    public Guid DeliveryCompanyId { get; private set; }
    /// <summary>Cash the courier collected from customers (sum of order totals).</summary>
    public decimal AmountCollected { get; private set; }
    /// <summary>Fees the courier kept.</summary>
    public decimal CourierFees { get; private set; }
    /// <summary>Cash actually handed to ÀLara Line.</summary>
    public decimal NetAmount { get; private set; }
    public int OrderCount { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public string? Notes { get; private set; }
    public Guid? RecordedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
