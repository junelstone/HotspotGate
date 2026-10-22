using StonesHotspot.Core.Money;

namespace StonesHotspot.Tests;

public class XafTests
{
    [Theory]
    [InlineData(0, "0 FCFA")]
    [InlineData(500, "500 FCFA")]
    [InlineData(1000, "1 000 FCFA")]
    [InlineData(25000, "25 000 FCFA")]
    [InlineData(1234567, "1 234 567 FCFA")]
    [InlineData(-1500, "-1 500 FCFA")]
    public void Formats_with_space_groups_and_FCFA(long amount, string expected)
    {
        Assert.Equal(expected, new Xaf(amount).ToString(XafStyle.Ascii));
        Assert.Equal(expected.Replace(' ', ' '), new Xaf(amount).ToString());
    }

    [Fact]
    public void Number_only_has_no_symbol() => Assert.Equal("10 000", new Xaf(10000).ToNumberString(XafStyle.Ascii));

    [Theory]
    [InlineData("1000", 1000)]
    [InlineData("1 000", 1000)]
    [InlineData("1 000 FCFA", 1000)]
    [InlineData("1 000", 1000)]
    [InlineData("1.000", 1000)]
    [InlineData("1,000,000", 1000000)]
    [InlineData(" 2500 XAF ", 2500)]
    [InlineData("300 F CFA", 300)]
    [InlineData("-500", -500)]
    public void Parses_common_inputs(string text, long expected)
    {
        Assert.True(Xaf.TryParse(text, out var value));
        Assert.Equal(expected, value.Amount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("FCFA")]
    [InlineData("abc")]
    [InlineData("1000,50")]
    [InlineData("1.5")]
    [InlineData("10 00")]
    [InlineData("1000 USD")]
    public void Rejects_invalid_or_decimal_inputs(string? text) => Assert.False(Xaf.TryParse(text, out _));

    [Fact]
    public void Arithmetic_and_sum()
    {
        Assert.Equal(new Xaf(1500), new Xaf(1000) + new Xaf(500));
        Assert.Equal(new Xaf(600), new Xaf(200) * 3);
        Assert.Equal(new Xaf(1750), Xaf.Sum([new Xaf(1000), new Xaf(500), new Xaf(250)]));
        Assert.True(new Xaf(100) < new Xaf(200));
    }
}
