using StonesHotspot.Core;
using StonesHotspot.Core.Rules;

namespace StonesHotspot.Tests;

public class CodeAndMacTests
{
    [Fact]
    public void Voucher_codes_have_8_unambiguous_characters()
    {
        foreach (var bad in "O0I1L")
            Assert.DoesNotContain(bad, VoucherCodeGenerator.Alphabet);

        for (var i = 0; i < 500; i++)
        {
            var code = VoucherCodeGenerator.Next();
            Assert.Equal(8, code.Length);
            Assert.All(code, c => Assert.Contains(c, VoucherCodeGenerator.Alphabet));
        }
    }

    [Theory]
    [InlineData("abcd-efgh", "ABCDEFGH")]
    [InlineData(" ab cd ef gh ", "ABCDEFGH")]
    [InlineData(null, "")]
    public void Voucher_code_input_is_normalised(string? input, string expected) =>
        Assert.Equal(expected, VoucherCodeGenerator.Normalize(input));

    [Fact]
    public void Voucher_code_is_printed_in_two_groups() => Assert.Equal("ABCD-EFGH", VoucherCodeGenerator.Format("ABCDEFGH"));

    [Theory]
    [InlineData("aa-bb-cc-dd-ee-ff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("AABB.CCDD.EEFF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aabbccddeeff", "AA:BB:CC:DD:EE:FF")]
    public void Mac_addresses_are_normalised(string input, string expected) => Assert.Equal(expected, MacAddress.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("AA:BB:CC:DD:EE")]
    [InlineData("GG:BB:CC:DD:EE:FF")]
    public void Invalid_mac_addresses_are_rejected(string input)
    {
        Assert.False(MacAddress.TryNormalize(input, out _));
        Assert.Throws<BusinessRuleException>(() => MacAddress.Normalize(input));
    }

    [Theory]
    [InlineData("DA:A1:19:00:00:01", true)]  // locally administered = random/private MAC
    [InlineData("00:1A:2B:3C:4D:5E", false)]
    public void Detects_random_private_macs(string mac, bool expected) => Assert.Equal(expected, MacAddress.IsRandomized(mac));
}
