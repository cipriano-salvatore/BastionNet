using BastionNet.Shared;
using System.Security.Cryptography;
using Xunit;

namespace BastionNet.Tests;

public class CryptoTests
{
    [Fact]
    public void EncryptDecryptRoundTrip_PreservesMessage()
    {
        // Arrange
        var originalMessage = "Ciao BastionNet!";
        var rng = RandomNumberGenerator.Create();
        var key = new byte[32];
        rng.GetBytes(key);

        // Act
        var (iv, ciphertext) = Crypto.EncryptAes(originalMessage, key);
        var decrypted = Crypto.DecryptAes(iv, ciphertext, key);

        // Assert
        Assert.Equal(originalMessage, decrypted);
    }

    [Fact]
    public void DifferentMessages_ProduceDifferentCiphertext()
    {
        // Arrange
        var message1 = "Messaggio 1";
        var message2 = "Messaggio 2";
        var key = new byte[32];
        var rng = RandomNumberGenerator.Create();
        rng.GetBytes(key);

        // Act
        var (iv1, cipher1) = Crypto.EncryptAes(message1, key);
        var (iv2, cipher2) = Crypto.EncryptAes(message2, key);

        // Assert
        Assert.NotEqual(cipher1, cipher2);
    }

    [Fact]
    public void SendReceiveMessage_Workflow()
    {
        // Arrange
        var ownKeySource = "clientA";
        var peerKeySource = "clientB";
        var plaintext = "Hello end-to-end!";

        // Act
        var (iv, ciphertext) = Crypto.SendEncryptedMessage(ownKeySource, peerKeySource, plaintext);
        var decrypted = Crypto.ReceiveDecryptedMessage(ownKeySource, peerKeySource, iv, ciphertext);

        // Assert
        Assert.Equal(plaintext, decrypted);
    }
}