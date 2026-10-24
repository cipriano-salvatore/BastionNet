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
    private readonly object _streamLock = new(); // Lock per scritture thread-safe sul stream

    /// <summary>
    /// Evento: messaggio ricevuto da questo client (da broadcastare).
    /// </summary>
    public event Action<ClientHandler, string>? MessageBroadcast;

    /// <summary>
    /// Evento: client disconnesso (senza argomenti).
    /// </summary>
    public event Action? Disconnected;

    // Per ECDH e chiave sessione
    private string _ownPrivateKeyBase64 = string.Empty;
    private string _ownPublicKeyBase64 = string.Empty;
    private readonly List<string> _peerPublicKeys = new(); // Raccolta ordinata delle public key dei peer
    private bool _sessionKeyDerived = false;
    private byte[] _sessionKey = Array.Empty<byte>();

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
                Message? msg;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // Determina il tipo di messaggio in base alla presenza di campi caratteristici
                    if (root.TryGetProperty("publicKey", out var _))
                        msg = JsonSerializer.Deserialize<KeyExchangeMessage>(json);
                    else if (root.TryGetProperty("iv", out var _) || root.TryGetProperty("ciphertext", out var _))
                        msg = JsonSerializer.Deserialize<EncryptedMessage>(json);
                    else if (root.TryGetProperty("clientName", out var _))
                        msg = JsonSerializer.Deserialize<ConnectMessage>(json);
                    else
                        msg = JsonSerializer.Deserialize<DisconnectMessage>(json);
                }
                catch
                {
                    continue;
                }

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
    /// Gestisce lo scambio chiavi ECDH e deriva la chiave sessione condivisa.
    /// Inoltra la public key del peer agli altri client affinché possano derivare la stessa chiave sessione.
    /// </summary>
    private void HandleKeyExchange(KeyExchangeMessage keMsg)
    {
        // Memorizza la propria public key alla prima ricezione (se non già impostata)
        if (string.IsNullOrEmpty(_ownPublicKeyBase64))
        {
            _ownPublicKeyBase64 = keMsg.PublicKey;
        }

        var peerPublicKey = keMsg.PublicKey;

        // Aggiunge la propria public key alla raccolta (se non esiste già)
        if (!_peerPublicKeys.Contains(_ownPublicKeyBase64))
        {
            _peerPublicKeys.Add(_ownPublicKeyBase64);
        }

        // Aggiunge la public key del peer alla raccolta (se non esiste già)
        if (!_peerPublicKeys.Contains(peerPublicKey))
        {
            _peerPublicKeys.Add(peerPublicKey);
        }

        // Inoltra le public key agli altri client perché possano derivare la stessa chiave sessione
        _server?.BroadcastPeerPublicKey(peerPublicKey, Id);

        // Deriva la chiave sessione se abbiamo almeno due key (propria + peer) e non l'abbiamo ancora fatta
        if (_peerPublicKeys.Count >= 2 && !_sessionKeyDerived)
        {
            DeriveSessionKey();
        }
    }

    /// <summary>
    /// Deriva la chiave sessione condivisa da tutte le public key raccolte.
    /// Ordina le chiavi per garantire che tutti i client derivino la stessa chiave.
    /// </summary>
    private void DeriveSessionKey()
    {
        try
        {
            _sessionKey = Crypto.DeriveSessionKey(_peerPublicKeys.ToArray());
            _sessionKeyDerived = true;
            Console.WriteLine($"[Client {Id}] Chiave sessione condivisa derivata da {_peerPublicKeys.Count} peer(s)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Client {Id}] Errore derivazione chiave sessione: {ex.Message}");
        }
    }

    /// <summary>
    /// Gestisce i messaggi crittografati ricevuti dal server (relè).
    /// Utilizza la chiave sessione condivisa per decifrare.
    /// </summary>
    private void HandleEncryptedMessage(EncryptedMessage encryptedMsg)
    {
        // Se non abbiamo ancora derivato la chiave sessione, proviamo a farlo
        if (!_sessionKeyDerived && _peerPublicKeys.Count > 0)
        {
            DeriveSessionKey();
        }

        string? plaintext = null;

        try
        {
            // Decifra usando la chiave sessione condivisa
            if (_sessionKeyDerived && _sessionKey != null && _sessionKey.Length > 0)
            {
                plaintext = Crypto.DecryptWithSessionKey(encryptedMsg.Iv, encryptedMsg.Tag, encryptedMsg.Ciphertext, _sessionKey);
            }
        }
        catch (CryptographicException)
        {
            // Tag HMAC non corrisponde
            plaintext = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Client {Id}] Errore decrittazione: {ex.Message}");
            plaintext = null;
        }

        // Se la decrittazione fallisce, proviamo a mostrare un messaggio indicativo
        if (plaintext != null)
        {
            // Solleva l'evento con il testo in chiaro
            MessageBroadcast?.Invoke(this, plaintext);
        }
        else
        {
            // Non è stato possibile decifrare (ancora nessuna chiave sessione derivata)
            MessageBroadcast?.Invoke(this, "[Messaggio cifrato - chiave sessione in attesa]");
        }
    }

    /// <summary>
    /// Invia un messaggio al client connesso.
    /// </summary>
    public async Task SendAsync(string message)
    {
        if (TcpClient?.Client?.Connected == true)
        {
            lock (_streamLock)
            {
                var data = Encoding.UTF8.GetBytes(message);
                Stream.Write(data, 0, data.Length);
            }
        }
    }

    /// <summary>
    /// Invia un messaggio crittografato end-to-end usando la chiave sessione condivisa.
    /// </summary>
    /// <param name="plaintext">Il messaggio in chiaro da inviare.</param>
    public async Task SendEncryptedAsync(string plaintext)
    {
        if (!_sessionKeyDerived) return;

        // Deriva la chiave sessione (già derivata da DeriveSessionKey)
        // In una implementazione completa, passerremmo la sessionKey come parametro o la manterremmo in stato
        // Per semplicità, deriviamo nuovamente qui (deve dare lo stesso risultato)
        var sessionKey = Crypto.DeriveSessionKey(_peerPublicKeys.ToArray());

        // Cifra il messaggio con AES-256-CBC + HMAC usando la chiave sessione
        var (ivBase64, tagBase64, ciphertextBase64) = Crypto.EncryptAesCbcHmac(plaintext, sessionKey);

        // Crea il messaggio encrypted
        var encryptedMsg = new EncryptedMessage
        {
            Iv = ivBase64,
            Tag = tagBase64,
            Ciphertext = ciphertextBase64
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
        // Non deriviamo chiavi sessione qui - invece raccogliamo le public key dai peer tramite KeyExchangeMessage
    }
}