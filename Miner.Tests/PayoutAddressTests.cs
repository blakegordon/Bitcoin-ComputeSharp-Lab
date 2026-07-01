using NBitcoin;

namespace Miner.Tests;

public class PayoutAddressTests
{
    [Fact(DisplayName = nameof(PayoutScriptHex_ShouldMatchAddress))]
    public void PayoutScriptHex_ShouldMatchAddress()
    {
        // Arrange
        var options = new MiningOptions(); // use default payout address

        // Act
        string publicKey = options.PayoutScriptHex;

        // Assert
        var address = BitcoinAddress.Create("bc1qk80v7pux0z3vw9kjw7mx6pmkethnngs5a5q5nq", Network.Main);
        Assert.Equal(publicKey, address.ScriptPubKey.ToHex());
    }
}
