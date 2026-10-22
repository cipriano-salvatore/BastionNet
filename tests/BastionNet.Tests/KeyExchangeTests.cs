using BastionNet.Shared;
using Xunit;

namespace BastionNet.Tests;

public class ProtocolTests
{
    [Fact]
    public void KeyExchangeMessage_HasCorrectType()
    {
        // Arrange
        var msg = new KeyExchangeMessage();

        // Act & Assert
        Assert.Equal(MessageType.KeyExchange, msg.Type);
        Assert.NotNull(msg.SenderId);
    }

    [Fact]
    public void EncryptedMessage_HasCorrectType()
    {
        // Arrange
        var msg = new EncryptedMessage();

        // Act & Assert
        Assert.Equal(MessageType.EncryptedMessage, msg.Type);
    }

    [Fact]
    public void ConnectMessage_HasCorrectTypeAndDefaultName()
    {
        // Arrange
        var msg = new ConnectMessage();

        // Act & Assert
        Assert.Equal(MessageType.Connect, msg.Type);
        Assert.Equal("Client", msg.ClientName);
    }

    [Fact]
    public void DisconnectMessage_HasCorrectType()
    {
        // Arrange
        var msg = new DisconnectMessage();

        // Act & Assert
        Assert.Equal(MessageType.Disconnect, msg.Type);
    }
}