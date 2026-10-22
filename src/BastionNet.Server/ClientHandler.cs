using System;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BastionNet.Shared;

namespace BastionNet.Server;

/// <summary>
/// Gestisce una singola connessione client.
/// </summary>
public class ClientHandler
{
    public Guid Id { get; } = Guid.NewGuid();
    public TcpClient? TcpClient { get; }
    public NetworkStream Stream { get; }
    private readonly RelayServer? _server;
    private readonly CancellationTokenSource _cts;
    private bool _disposed;

    /// <summary>
    /// Evento: messaggio ricevuto da questo client (da broadcastare).
    /// </summary>
    public event Action<ClientHandler, string>? MessageBroadcast;

    /// <summary>
    /// Evento: client disconnesso (senza argomenti).
    /// </summary>
    public event Action? Disconnected;

    // Per ECDH
    private string _ownPrivateKeyBase64 = string.Empty;
    private readonly Dictionary<Guid, string> _peerPublicKeys = new(); // PeerId -> PublicKey B64

    /// <summary>
    /// Costruttore principale: usato dal server.
    /// </summary>
    public ClientHandler(RelayServer server, TcpClient client)
    {
        _server = server;
        TcpClient = client;
        Stream = client.GetStream();
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// Costruttore secondario: usato dal client.
    /// Fornisce solo lo stream e il token di cancellazione.
    /// </summary>
    public ClientHandler(NetworkStream stream, CancellationToken cancellationToken)
    {
        TcpClient = null;
        Stream = stream;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _server = null;
    }

    /// <summary>
    /// Loop principale: legge i messaggi dal client e li invia al server.
    /// </summary>
    public async Task HandleAsync()
    {
        var buffer = new byte[8192];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var bytesRead = await Stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                var json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                // Deserializza il messaggio
                var msg = JsonSerializer.Deserialize<Message>(json);

                if (msg == null) continue;

                // Gestione messaggi basati sul tipo
                if (msg is KeyExchangeMessage keMsg)
                {
                    HandleKeyExchange(keMsg);
                }
                else if (msg is EncryptedMessage encryptedMsg)
                {
                    HandleEncryptedMessage(encryptedMsg);
                }
                else
                {
                    // Messaggi di connessione/disconnessione o sconosciuti
                    // Li relayiamo come testo semplice per compatibilità
                    MessageBroadcast?.Invoke(this, json);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
        }
        finally
        {
            if (!_disposed)
            {
                _disposed = true;
                try { Stream?.Close(); } catch { }
                try { TcpClient?.Close(); } catch { }
                Disconnected?.Invoke();
                _server?.HandleClientDisconnected(this);
                Console.WriteLine($"[Client {Id}] Disconnesso");
            }
        }
    }

    /// <summary>
    /// Gestisce lo scambio chiavi ECDH.
    /// </summary>
    private void HandleKeyExchange(KeyExchangeMessage keMsg)
    {
        var peerPublicKey = keMsg.PublicKey;
        var peerId = Guid.NewGuid(); // ID semplificato per il peer

        // Memorizza la public key del peer
        _peerPublicKeys[peerId] = peerPublicKey;

        Console.WriteLine($"[Client {Id}] Ricevuta public key dal peer. Totale peer: {_peerPublicKeys.Count}");

        // Se abbiamo già la nostra chiave privata e quella di altri peer, deriviamo chiavi AES
        if (!string.IsNullOrEmpty(_ownPrivateKeyBase64) && _peerPublicKeys.Count > 0)
        {
            DeriveAndSetupKeys();
        }
    }

    /// <summary>
    /// Deriva le chiavi AES per tutti i peer conosciuti.
    /// </summary>
    private void DeriveAndSetupKeys()
    {
        foreach (var (peerId, peerPublicKey) in _peerPublicKeys)
        {
            try
            {
                var aesKey = Crypto.DeriveAesKeyFromEcdh(_ownPrivateKeyBase64, peerPublicKey);
                Console.WriteLine($"[Client {Id}] Chiave AES derivata per peer {peerId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Client {Id}] Errore derivazione chiave per peer {peerId}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Gestisce i messaggi crittografati ricevuti dal server (relè).
    /// </summary>
    private void HandleEncryptedMessage(EncryptedMessage encryptedMsg)
    {
        // Deriva la chiave AES usando la nostra private key e quella di un peer
        // Prendiamo la prima public key disponibile
        var peerPublicKey = _peerPublicKeys.Values.FirstOrDefault() ?? string.Empty;
        var aesKey = Crypto.DeriveAesKeyFromEcdh(_ownPrivateKeyBase64, peerPublicKey);

        // Decifra il messaggio using CBC + HMAC
        string? plaintext = null;

        try
        {
            plaintext = Crypto.DecryptAesCbcHmac(encryptedMsg.Iv, encryptedMsg.Tag, encryptedMsg.Ciphertext, aesKey);
        }
        catch (CryptographicException)
        {
            // Tag HMAC non corrisponde, lascia testo vuoto
            plaintext = null;
        }

        if (plaintext != null)
        {
            // Solleva l'evento con il testo in chiaro
            MessageBroadcast?.Invoke(this, plaintext);
        }
        else
        {
            // Non è stato possibile decifrare
            MessageBroadcast?.Invoke(this, "[Messaggio non decifrabile - chiave non corrisponde]");
        }
    }

    /// <summary>
    /// Invia un messaggio al client connesso.
    /// </summary>
    public async Task SendAsync(string message)
    {
        if (TcpClient?.Client?.Connected == true)
        {
            var data = Encoding.UTF8.GetBytes(message);
            await Stream.WriteAsync(data, 0, data.Length);
        }
    }

    /// <summary>
    /// Invia un messaggio crittografato end-to-end.
    /// </summary>
    /// <param name="plaintext">Il messaggio in chiaro da inviare.</param>
    /// <param name="targetPeerPublicKey">La chiave pubblica del destinatario (per unicast).</param>
    /// <param name="ownPrivateKey">La propria chiave privata ECDH in base64.</param>
    public async Task SendEncryptedAsync(string plaintext, string targetPeerPublicKey, string ownPrivateKey)
    {
        // Deriva la chiave AES usando ECDH
        var aesKey = Crypto.DeriveAesKeyFromEcdh(ownPrivateKey, targetPeerPublicKey);

        // Cifra il messaggio con AES-256-CBC + HMAC
        var (ivBase64, tagBase64, ciphertextBase64) = Crypto.EncryptAesCbcHmac(plaintext, aesKey);

        // Crea il messaggio encrypted
        var encryptedMsg = new EncryptedMessage
        {
            Iv = ivBase64,
            Tag = tagBase64,
            Ciphertext = ciphertextBase64,
            TargetId = null // Broadcast a tutti
        };

        var json = JsonSerializer.Serialize(encryptedMsg);
        await SendAsync(json);
    }

    /// <summary>
    /// Imposta la propria chiave privata ECDH.
    /// </summary>
    /// <param name="privateKeyBase64">Chiave privata in base64.</param>
    public void SetOwnPrivateKey(string privateKeyBase64)
    {
        _ownPrivateKeyBase64 = privateKeyBase64;
        // Se abbiamo già public key dei peer, deriva immediatamente le chiavi
        if (_peerPublicKeys.Count > 0)
        {
            DeriveAndSetupKeys();
        }
    }
}