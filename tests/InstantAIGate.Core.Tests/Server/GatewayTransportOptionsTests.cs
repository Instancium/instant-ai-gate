namespace InstantAIGate.Core.Tests.Server;

using InstantAIGate.Server.Configuration;
using Xunit;

public sealed class GatewayTransportOptionsTests
{
    [Fact]
    public void GetEffectiveReceiveMessageSize_DefaultOptions_ReturnsDefault()
    {
        var options = new GatewayTransportOptions();
        Assert.Equal(
            GatewayTransportOptions.DefaultReceiveMessageBytes,
            options.GetEffectiveReceiveMessageSize());
    }

    [Fact]
    public void GetEffectiveReceiveMessageSize_ZeroValue_ReturnsDefault()
    {
        var options = new GatewayTransportOptions { MaximumReceiveMessageBytes = 0 };
        Assert.Equal(
            GatewayTransportOptions.DefaultReceiveMessageBytes,
            options.GetEffectiveReceiveMessageSize());
    }

    [Fact]
    public void GetEffectiveReceiveMessageSize_NegativeValue_ReturnsDefault()
    {
        var options = new GatewayTransportOptions { MaximumReceiveMessageBytes = -1 };
        Assert.Equal(
            GatewayTransportOptions.DefaultReceiveMessageBytes,
            options.GetEffectiveReceiveMessageSize());
    }

    [Fact]
    public void GetEffectiveReceiveMessageSize_BelowDefault_ClampsToDefault()
    {
        var options = new GatewayTransportOptions { MaximumReceiveMessageBytes = 1024 };
        Assert.Equal(
            GatewayTransportOptions.DefaultReceiveMessageBytes,
            options.GetEffectiveReceiveMessageSize());
    }

    [Fact]
    public void GetEffectiveReceiveMessageSize_ValidValue_ReturnsValue()
    {
        var options = new GatewayTransportOptions { MaximumReceiveMessageBytes = 1_048_576 };
        Assert.Equal(1_048_576, options.GetEffectiveReceiveMessageSize());
    }

    [Fact]
    public void GetEffectiveReceiveMessageSize_AtDefault_ReturnsDefault()
    {
        var options = new GatewayTransportOptions
        {
            MaximumReceiveMessageBytes = GatewayTransportOptions.DefaultReceiveMessageBytes
        };
        Assert.Equal(
            GatewayTransportOptions.DefaultReceiveMessageBytes,
            options.GetEffectiveReceiveMessageSize());
    }

    [Fact]
    public void GetEffectiveReceiveMessageSize_AtCeiling_ReturnsCeiling()
    {
        var options = new GatewayTransportOptions
        {
            MaximumReceiveMessageBytes = GatewayTransportOptions.HardReceiveMessageCeilingBytes
        };
        Assert.Equal(
            GatewayTransportOptions.HardReceiveMessageCeilingBytes,
            options.GetEffectiveReceiveMessageSize());
    }

    [Fact]
    public void GetEffectiveReceiveMessageSize_AboveCeiling_ClampsToCeiling()
    {
        var options = new GatewayTransportOptions { MaximumReceiveMessageBytes = 100_000_000 };
        Assert.Equal(
            GatewayTransportOptions.HardReceiveMessageCeilingBytes,
            options.GetEffectiveReceiveMessageSize());
    }

    [Theory]
    [InlineData(-1000, GatewayTransportOptions.DefaultReceiveMessageBytes)]
    [InlineData(0, GatewayTransportOptions.DefaultReceiveMessageBytes)]
    [InlineData(1, GatewayTransportOptions.DefaultReceiveMessageBytes)]
    [InlineData(16_384, GatewayTransportOptions.DefaultReceiveMessageBytes)]
    [InlineData(32_768, 32_768)]
    [InlineData(65_536, 65_536)]
    [InlineData(1_048_576, 1_048_576)]
    [InlineData(16_777_216, 16_777_216)]
    [InlineData(17_000_000, 16_777_216)]
    [InlineData(int.MaxValue, 16_777_216)]
    public void GetEffectiveReceiveMessageSize_BoundaryValues_ClampedCorrectly(int input, int expected)
    {
        var options = new GatewayTransportOptions { MaximumReceiveMessageBytes = input };
        Assert.Equal(expected, options.GetEffectiveReceiveMessageSize());
    }

    [Fact]
    public void Constants_DefaultIsBelowCeiling()
    {
        Assert.True(
            GatewayTransportOptions.DefaultReceiveMessageBytes < GatewayTransportOptions.HardReceiveMessageCeilingBytes,
            "Default must be strictly below the hard ceiling to allow operator headroom.");
    }

    [Fact]
    public void Constants_DefaultMatchesSignalRFrameworkDefault()
    {
        Assert.Equal(32_768, GatewayTransportOptions.DefaultReceiveMessageBytes);
    }

    [Fact]
    public void Constants_CeilingIsSixteenMebibytes()
    {
        Assert.Equal(16 * 1024 * 1024, GatewayTransportOptions.HardReceiveMessageCeilingBytes);
    }
}
