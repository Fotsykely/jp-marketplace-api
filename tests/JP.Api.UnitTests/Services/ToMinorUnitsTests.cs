using JP.Api.Services;

namespace JP.Api.UnitTests.Services;

/// <summary>Stripe wants integers in the smallest unit of the currency.</summary>
public class ToMinorUnitsTests
{
    [Theory]
    [InlineData(45.00, "eur", 4500)]
    [InlineData(0.99, "eur", 99)]
    [InlineData(12.5, "usd", 1250)]
    public void RegularCurrency_MultipliesBy100(double amount, string currency, long expected)
    {
        // xUnit attributes cannot hold a decimal literal, hence the double -> decimal cast
        Assert.Equal(expected, PaymentService.ToMinorUnits((decimal)amount, currency));
    }

    [Theory]
    [InlineData(45000, "mga", 45000)]
    [InlineData(1500, "jpy", 1500)]
    [InlineData(10, "xof", 10)]
    public void ZeroDecimalCurrency_IsNotMultiplied(double amount, string currency, long expected)
    {
        Assert.Equal(expected, PaymentService.ToMinorUnits((decimal)amount, currency));
    }

    [Theory]
    [InlineData("MGA")]
    [InlineData("Mga")]
    public void CurrencyCode_IsCaseInsensitive(string currency)
    {
        Assert.Equal(45000, PaymentService.ToMinorUnits(45000m, currency));
    }

    [Fact]
    public void Midpoint_RoundsAwayFromZero()
    {
        // 0.125 EUR = 12.5 cents: AwayFromZero gives 13 (banker's rounding would give 12)
        Assert.Equal(13, PaymentService.ToMinorUnits(0.125m, "eur"));
    }

    [Fact]
    public void ZeroAmount_ReturnsZero()
    {
        Assert.Equal(0, PaymentService.ToMinorUnits(0m, "eur"));
    }
}
