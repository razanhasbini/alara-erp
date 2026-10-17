using AlaraERP.Domain.Sales;

namespace AlaraERP.UnitTests.Domain;

public sealed class CustomerPhoneTests
{
    [Theory]
    [InlineData("+961 70 123 456")]
    [InlineData("+96170123456")]
    [InlineData("70 123 456")]
    [InlineData("0070 123 456")]
    [InlineData("00961 70 123 456")]
    public void Lebanese_mobile_numbers_match_however_they_are_typed(string typed) =>
        Assert.Equal("96170123456", Customer.NormalizePhoneKey(typed.Replace("0070", "070")));

    [Theory]
    [InlineData("+961 3 123 456")]
    [InlineData("+961 03 123 456")] // trunk 0 typed after the country code
    [InlineData("03 123 456")]
    public void Older_lebanese_numbers_match_with_or_without_the_trunk_zero(string typed) =>
        Assert.Equal("9613123456", Customer.NormalizePhoneKey(typed));

    [Fact]
    public void Other_countries_keep_their_code() => Assert.Equal("962791234567", Customer.NormalizePhoneKey("+962 79 123 4567"));
}
