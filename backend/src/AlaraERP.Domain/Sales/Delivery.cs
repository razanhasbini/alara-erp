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

/// <summary>A delivery expense recorded independently from customer orders.</summary>
public sealed class DeliveryExpense : IAuditable
{
    private DeliveryExpense() { }

    public DeliveryExpense(Guid deliveryCompanyId, DateOnly incurredOn, decimal amount, int deliveryCount,
        string? notes, Guid? recordedByUserId)
    {
        Id = Guid.CreateVersion7();
        DeliveryCompanyId = deliveryCompanyId;
        RecordedByUserId = recordedByUserId;
        Update(incurredOn, amount, deliveryCount, notes);
    }

    public Guid Id { get; private set; }
    public Guid DeliveryCompanyId { get; private set; }
    public DateOnly IncurredOn { get; private set; }
    public decimal Amount { get; private set; }
    public int DeliveryCount { get; private set; }
    public string? Notes { get; private set; }
    public Guid? RecordedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Update(DateOnly incurredOn, decimal amount, int deliveryCount, string? notes)
    {
        if (amount <= 0)
            throw new DomainException("delivery_expenses.amount_required", "Delivery cost must be greater than zero.");
        IncurredOn = incurredOn;
        Amount = Guard.Money(amount);
        DeliveryCount = Guard.Positive(deliveryCount, "Number of deliveries");
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void ChangeCompany(Guid deliveryCompanyId) => DeliveryCompanyId = deliveryCompanyId;
}
