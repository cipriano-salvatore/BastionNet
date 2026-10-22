using System.Text.Json.Serialization;

namespace BastionNet.Shared;

/// <summary>
/// Tipi di messaggio per il protocollo di comunicazione.
/// </summary>
public enum MessageType : byte
{
    /// <summary>
    /// Scambio chiavi ECDH (chiave pubblica scambiata)
    /// </summary>
    KeyExchange = 1,

    /// <summary>
    /// Messaggio di chat cifrato
    /// </summary>
    EncryptedMessage = 2,

    /// <summary>
    /// Richiesta di connessione
    /// </summary>
    Connect = 3,

    /// <summary>
    /// Disconnessione
    /// </summary>
    Disconnect = 4
}

/// <summary>
/// Base per tutti i messaggi del protocollo.
/// </summary>
public abstract partial class Message
{
    [JsonIgnore] public MessageType Type { get; protected set; }
    [JsonIgnore] public Guid SenderId { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Messaggio di scambio chiavi ECDH.
/// Contiene la chiave pubblica del mittente.
/// </summary>
public class KeyExchangeMessage : Message
{
    public KeyExchangeMessage() => Type = MessageType.KeyExchange;

    /// <summary>
    /// Chiave pubblica del mittente (formato XML o base64).
    /// </summary>
    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;
}

/// <summary>
/// Messaggio di chat cifrato end-to-end.
/// Contiene l'IV, il tag di autenticazione e il ciphertext (base64).
/// </summary>
public class EncryptedMessage : Message
{
    public EncryptedMessage() => Type = MessageType.EncryptedMessage;

    /// <summary>
    /// Initialization Vector (12 bytes per AES-GCM).
    /// </summary>
    [JsonPropertyName("iv")]
    public string Iv { get; set; } = string.Empty;

    /// <summary>
    /// Tag di autenticazione (16 bytes per AES-GCM).
    /// </summary>
    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// Cifrato payload (base64).
    /// </summary>
    [JsonPropertyName("ciphertext")]
    public string Ciphertext { get; set; } = string.Empty;

    /// <summary>
    /// ID del destinatario (per messaggi privati o broadcast).
    /// </summary>
    [JsonPropertyName("targetId")]
    public Guid? TargetId { get; set; }
}

/// <summary>
/// Messaggio semplice di connessione/disconnessione.
/// </summary>
public class ConnectMessage : Message
{
    public ConnectMessage() => Type = MessageType.Connect;

    /// <summary>
    /// Nome del client per identificazione visiva.
    /// </summary>
    [JsonPropertyName("clientName")]
    public string ClientName { get; set; } = "Client";
}

/// <summary>
/// Messaggio di disconnessione.
/// </summary>
public class DisconnectMessage : Message
{
    public DisconnectMessage() => Type = MessageType.Disconnect;
}