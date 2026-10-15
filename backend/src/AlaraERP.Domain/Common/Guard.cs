namespace AlaraERP.Domain.Common;

public static class Guard
{
    public static int Positive(int value, string what) =>
        value > 0 ? value : throw new DomainException("validation.not_positive", $"{what} must be greater than zero.");

    public static decimal NotNegative(decimal value, string what) =>
        value >= 0 ? value : throw new DomainException("validation.negative_amount", $"{what} cannot be negative.");

    /// <summary>Rounds a monetary amount to cents (half away from zero).</summary>
    public static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
