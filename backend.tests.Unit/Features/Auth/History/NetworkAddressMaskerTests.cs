using System.Net;

using backend.main.features.auth.history;

using FluentAssertions;

namespace backend.tests.Unit.Features.Auth.History;

public class NetworkAddressMaskerTests
{
    [Theory]
    [InlineData("203.0.113.50", "203.0.113.0/24")]
    [InlineData("198.51.100.255", "198.51.100.0/24")]
    [InlineData("::ffff:203.0.113.50", "203.0.113.0/24")]
    [InlineData("2001:db8:abcd:12:34:56:78:9a", "2001:db8:abcd::/48")]
    [InlineData("2001:db8:abcd::1", "2001:db8:abcd::/48")]
    public void Mask_ShouldKeepOnlyTheNetworkPrefix(string address, string expected)
    {
        NetworkAddressMasker.Mask(IPAddress.Parse(address)).Should().Be(expected);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void Mask_ShouldShowNothing_ForAddressesThatSayNothingToTheOwner(string address)
    {
        NetworkAddressMasker.Mask(IPAddress.Parse(address)).Should().BeNull();
    }

    [Fact]
    public void Mask_ShouldShowNothing_WhenThereIsNoAddress()
    {
        NetworkAddressMasker.Mask(null).Should().BeNull();
    }
}
