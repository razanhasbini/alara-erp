using AlaraERP.Domain.Common;

namespace AlaraERP.Domain.Sales;

/// <summary>A durable customer identity. Orders keep separate recipient snapshots for delivery and audit history.</summary>
public sealed class Customer : IAuditable
{
    private Customer() { }

    public Customer(string name, string phone, string? email, string? city, string? address, string? notes)
    {
        Id = Guid.CreateVersion7();
        IsActive = true;
        Update(name, phone, email, city, address, notes);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public string NormalizedPhone { get; private set; } = "";
    public string? Email { get; private set; }
    public string? City { get; private set; }
    public string? Address { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Update(string name, string phone, string? email, string? city, string? address, string? notes)
    {
        Name = Required(name, "Customer name");
        Phone = Order.NormalizePhone(Required(phone, "Customer phone"));
        NormalizedPhone = NormalizePhoneKey(Phone);
        Email = Clean(email)?.ToLowerInvariant();
        City = Clean(city);
        Address = Clean(address);
        Notes = Clean(notes);
    }

    public void SetActive(bool active) => IsActive = active;

    /// <summary>Canonical Lebanese number used for identity matching (+961…, 03… and 3… become the same key).</summary>
    public static string NormalizePhoneKey(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00961", StringComparison.Ordinal)) return digits[2..];
        // "+961 03 123 456": the local trunk 0 typed after the country code.
        if (digits.StartsWith("9610", StringComparison.Ordinal) && digits.Length is 11 or 12) return "961" + digits[4..];
        if (digits.StartsWith("961", StringComparison.Ordinal)) return digits;
        if (digits.StartsWith('0') && digits.Length is 8 or 9) return "961" + digits[1..];
        if (digits.Length is 7 or 8) return "961" + digits;
        return digits;
    }

    private static string Required(string value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainException("validation.required", $"{what} is required.") : value.Trim();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
