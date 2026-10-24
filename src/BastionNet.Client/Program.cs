// See https://aka.ms/new-console-template for more information
using System;
using System.Diagnostics; // Aggiunto per Process
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BastionNet.Shared;

namespace BastionNet.Client;

/// <summary>
/// Client BastionNet standalone con crittografia end-to-end AES-256-CBC + HMAC-SHA256
/// e integrazione assistente AI Python.
/// </summary>
internal class Program
{
    // ECDH keys
    private static string _ownPrivateKeyBase64 = string.Empty;
    private static string _ownPublicKeyBase64 = string.Empty;
    private static readonly Dictionary<string, string> _peerPublicKeys = new();
    private static readonly object _keysLock = new();

    // Client connection state
    private static TcpClient? _client;
    private static NetworkStream? _stream;
    private static CancellationTokenSource? _cts;
    private static Task? _readTask;

    // Python AI Assistant process
    private static Process? _aiProcess;
    private static readonly object _aiLock = new();
    private static Task? _aiReadTask;
    // Message to send via CLI argument (for end-to-end testing)

    // Message to send via CLI argument (for end-to-end testing)
    private static string? _messageToSend;

    static async Task Main(string[] args)
    {
        // 1. Genera la propria chiave ECDH
        var (privateKeyB64, publicKeyB64) = Crypto.GenerateEcdhKeyPair();
        lock (_keysLock)
        {
            _ownPrivateKeyBase64 = privateKeyB64;
            _ownPublicKeyBase64 = publicKeyB64;
        }

        Console.WriteLine("\n--- BastionNet Client con Crittografia End-to-End ---");
        Console.WriteLine($"Public key ECDH: {publicKeyB64.Substring(0, 40)}...");
        Console.WriteLine("Avvio Assistente AI...\n");

// Support message argument for end-to-end testing
_messageToSend = args.Length > 0 ? args[0] : null;

        // 2. Non avviare automaticamente l'assistente AI all'avvio.
        // L'AI process verrà avviato solo quando l'utente digita il comando "ai".
        _aiProcess = null;

        // 3. Connessione al server sulla stessa rete locale
        _client = new TcpClient();
        await _client.ConnectAsync(IPAddress.Parse("127.0.0.1"), 5000);
        _stream = _client.GetStream();
        _cts = new CancellationTokenSource();

        Console.WriteLine($"Connesso al server BastionNet su {_client.Client.LocalEndPoint} -> {_client.Client.RemoteEndPoint}");
        Console.WriteLine("Digita messaggi e premi Invio per inviare. 'exit' per uscire. 'ai' per richiesta suggerimento AI.\n");

        // If a message was provided via CLI, send it and exit
        if (!string.IsNullOrEmpty(_messageToSend))
        {
            // Start key exchange in background task (500ms delay)
            _readTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500);
                    var keMsg = new KeyExchangeMessage { PublicKey = _ownPublicKeyBase64 };
                    var keJson = JsonSerializer.Serialize(keMsg);
                    await _stream!.WriteAsync(Encoding.UTF8.GetBytes(keJson), 0, Encoding.UTF8.GetByteCount(keJson));
                    Console.WriteLine("[KeyExchange] Invia la propria public key al server.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("\nErrore key exchange: {ex.Message}");
                }
            });

            // Wait for key exchange, then send the message
            await Task.Delay(800);

            // Get peer public key and encrypt the message
            string? peerPublicKey = null;
            lock (_keysLock)
            {
                // Raccogli tutte le public key disponibili (stesso metodo del server)
                var allPeerKeys = _peerPublicKeys.Values.ToList();
                peerPublicKey = allPeerKeys.FirstOrDefault();
            }

            string ciphertextJson;
            if (!string.IsNullOrEmpty(peerPublicKey))
            {
                var allPeerKeys = _peerPublicKeys.Values.ToList();
                var sessionKey = Crypto.DeriveSessionKey(new[] { _ownPublicKeyBase64 }.Concat(allPeerKeys).ToArray());
                var (ivB64, tagB64, ctB64) = Crypto.EncryptAesCbcHmac(_messageToSend, sessionKey);
                var encryptedMsg = new EncryptedMessage
                {
                    Iv = ivB64,
                    Tag = tagB64,
                    Ciphertext = ctB64
                };
                ciphertextJson = JsonSerializer.Serialize(encryptedMsg);
            }
            else
            {
                ciphertextJson = JsonSerializer.Serialize(new EncryptedMessage { Iv = "", Tag = "", Ciphertext = Convert.ToBase64String(Encoding.UTF8.GetBytes(_messageToSend)) });
            }

            // Send the encrypted message to the server
            await _stream!.WriteAsync(Encoding.UTF8.GetBytes(ciphertextJson), 0, Encoding.UTF8.GetByteCount(ciphertextJson));
Console.WriteLine($"[Messaggio inviato (crittato)]: {_messageToSend}");

            // Cleanup and exit
            _cts?.Cancel();
            _client?.Close();
            if (_aiProcess != null)
            {
                try
                {
                    _aiProcess.StandardInput?.WriteLine("quit");
                    _aiProcess?.Kill();
                }
                catch { }
            }
            Console.WriteLine("\nDisconnesso dal server.\n");
            return;
        }

        // 4. Avvia un task che legge continuamente dallo stream (riceve broadcast dagli altri client, anche crittati)
        _readTask = Task.Run(async () =>
        {
            try
            {
                var buffer = new byte[8192];
                while (!_cts.IsCancellationRequested)
                {
                    if (_stream == null) break;
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    var json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    // Gestione messaggi: proviamo a deserializzare nei tipi specifici prima
                    MessageType? msgType = null;
                    string? msgContent = null;

                    if (json.Trim().StartsWith("{"))
                    {
                        // Prova a deserializzare come KeyExchangeMessage
                        try
                        {
                            var keMsg = JsonSerializer.Deserialize<KeyExchangeMessage>(json);
                            if (keMsg != null)
                            {
                                msgType = MessageType.KeyExchange;
                                msgContent = json;
                            }
                        }
                        catch { }

                        // Prova a deserializzare come EncryptedMessage
                        if (msgType == null)
                        {
                            try
                            {
                                var encMsg = JsonSerializer.Deserialize<EncryptedMessage>(json);
                                if (encMsg != null)
                                {
                                    msgType = MessageType.EncryptedMessage;
                                    msgContent = json;
                                }
                            }
                            catch { }
                        }

                        // Deserializza come Message base (per tipi Connect, Disconnect, o testo semplice)
                        if (msgType == null)
                        {
                            try { msgType = (MessageType)0; JsonSerializer.Deserialize<Message>(json); msgContent = json; }
                            catch { msgContent = json; }
                        }
                    }
                    else
                    {
                        // Messaggio di testo semplice (non JSON)
                        msgType = (MessageType)0;
                        msgContent = json;
                    }

                    // Gestione basata sul tipo di messaggio
                    if (msgType == MessageType.KeyExchange)
                    {
                        lock (_keysLock)
                        {
                            // Estrai la public key dal content JSON
                            try
                            {
                                var keMsg = JsonSerializer.Deserialize<KeyExchangeMessage>(msgContent!);
                                _peerPublicKeys[keMsg?.PublicKey ?? string.Empty] = keMsg?.PublicKey ?? string.Empty;
                            }
                            catch { }
                        }
                        Console.WriteLine($"[KeyExchange] Ricevuta public key da un peer.");
                        
                        // Deriva la chiave sessione se abbiamo abbastanza key (propria + peer)
                        var allPeerKeys = _peerPublicKeys.Values.ToList();
                        if (allPeerKeys.Count >= 1 && !_sessionKeyDerivedClient)
                        {
                            // Già abbiamo la chiave privata nostra e almeno una chiave peer
                            // Deriva la session key usando il metodo coerente con il server
                            var sessionKey = Crypto.DeriveSessionKey(new[] { _ownPublicKeyBase64 }.Concat(allPeerKeys).ToArray());
                            // Marca come derivata (useremo un flag per tracking)
                            _sessionKeyDerivedClient = true;
                            Console.WriteLine($"[Client {Id} if available] Chiave sessione derivata da {allPeerKeys.Count + 1} key(s)");
                        }
                        
                        var firstKey = _peerPublicKeys.Values.FirstOrDefault();
                        if (!string.IsNullOrEmpty(firstKey))
                        {
                            Console.WriteLine($"[Debug] Peer public key stored: {firstKey.Substring(0, Math.Min(30, firstKey.Length))}...");
                        }
                        else
                        {
                            Console.WriteLine("[Debug] Peer public key NOT stored (empty!)");
                        }
                        continue;
                    }

                    if (msgType == MessageType.EncryptedMessage)
                    {
                        // Decifra il messaggio usando ECDH + AES-256-CBC + HMAC
                        string? plaintext = null;
                        lock (_keysLock)
                        {
                            // Raccogli tutte le public key dei peer conosciuti
                            var allPeerKeys = _peerPublicKeys.Values.ToList();
                            if (allPeerKeys.Count > 0)
                            {
                                var sessionKey = Crypto.DeriveSessionKey(new[] { _ownPublicKeyBase64 }.Concat(allPeerKeys).ToArray());
                                try
                                {
                                    // Parsa il messaggio encrypt per estrarre iv, tag, ciphertext
                                    var encMsg = JsonSerializer.Deserialize<EncryptedMessage>(msgContent!);
                                    if (encMsg != null)
                                    {
                                        plaintext = Crypto.DecryptAesCbcHmac(encMsg.Iv, encMsg.Tag, encMsg.Ciphertext, sessionKey);
                                    }
                                }
                                catch (CryptographicException) { plaintext = null; }
                            }
                        }

                        if (plaintext != null)
                        {
                            Console.WriteLine($"[Ricevuto (crittato) da altri client]: {plaintext}");
                        }
                        else
                        {
                            Console.WriteLine("[Ricevuto (messaggio non decifrabile - chiave mancante)]");
                        }
                        continue;
                    }

                    // Messaggio di testo semplice (server broadcast di messaggi non crittati o in attesa di chiave)
                    // Mostra il contenuto come ricevuto dal server
                    if (!string.IsNullOrEmpty(msgContent))
                        Console.WriteLine($"[Ricevuto dal server]: {msgContent}");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"\nErrore lettura: {ex.Message}");
            }
        });

        // 5. Loop principale: legge dalla console, gestisce AI, crittografa e invia al server
        try
        {
            using var consoleReader = new StreamReader(Console.OpenStandardInput());
            while (!_cts.IsCancellationRequested)
            {
                Console.Write("> ");
                var line = await consoleReader.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;
                if (line.Equals("ai", StringComparison.OrdinalIgnoreCase))
                {
                    // Avvia l'assistente AI se non è già in esecuzione
                    try
                    {
                        if (_aiProcess == null)
                        {
                            var pythonExe = "python3";
                            var aiScript = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "src", "BastionNet.AI-Assistant", "main.py");
                            if (!File.Exists(aiScript))
                            {
                                aiScript = "src/BastionNet.AI-Assistant/main.py";
                            }

                            _aiProcess = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = pythonExe,
                                    Arguments = aiScript,
                                    RedirectStandardInput = true,
                                    RedirectStandardOutput = true,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                }
                            };
                            _aiProcess.Start();

                            // Avvia un task per leggere gli output dall'assistente AI
                            _aiReadTask = Task.Run(async () =>
                            {
                                try
                                {
                                    while (!_aiProcess.StandardOutput.EndOfStream)
                                    {
                                        var line = await _aiProcess.StandardOutput.ReadLineAsync();
                                        if (!string.IsNullOrEmpty(line))
                                        {
                                            Console.WriteLine($"[AI]: {line}");
                                        }
                                    }
                                }
                                catch { }
                            });
                        }

                        // Invia un prompt all'AI assistant
                        _aiProcess!.StandardInput.Write("ai suggestion\n");
                        var suggestion = await _aiProcess!.StandardOutput.ReadLineAsync();
                        if (!string.IsNullOrEmpty(suggestion))
                            Console.WriteLine($"[AI Suggerimento]: {suggestion}");
                        else
                            Console.WriteLine("[AI] Nessun suggerimento disponibile.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AI Errore] {ex.Message}");
                    }
                    continue;
                }

                // Crittografa il messaggio usando ECDH + AES-256-CBC + HMAC
                // Deriva la session key usando tutte le public key dei peer raccolti (stesso metodo del server)
                var allPeerKeys = _peerPublicKeys.Values.ToList();
                string ciphertextJson;
                if (allPeerKeys.Count > 0)
                {
                    var sessionKey = Crypto.DeriveSessionKey(new[] { _ownPublicKeyBase64 }.Concat(allPeerKeys).ToArray());
                    var (ivB64, tagB64, ctB64) = Crypto.EncryptAesCbcHmac(line, sessionKey);
                    var encryptedMsg = new EncryptedMessage
                    {
                        Iv = ivB64,
                        Tag = tagB64,
                        Ciphertext = ctB64
                    };
                    ciphertextJson = JsonSerializer.Serialize(encryptedMsg);
                }
                else
                {
                    // Nessuna key peer disponibile: invia in chiaro (il server lo vede ma non può decifrare)
                    ciphertextJson = JsonSerializer.Serialize(new EncryptedMessage { Iv = "", Tag = "", Ciphertext = Convert.ToBase64String(Encoding.UTF8.GetBytes(line)) });
                }

                // Invia al server (che broadcasterà a tutti gli altri client)
                await _stream!.WriteAsync(Encoding.UTF8.GetBytes(ciphertextJson), 0, Encoding.UTF8.GetByteCount(ciphertextJson));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"\nErrore invio: {ex.Message}");
        }
        finally
        {
            _cts?.Cancel();
            _client?.Close();
            // Chiudi anche il processo AI
            if (_aiProcess != null)
            {
                try
                {
                    _aiProcess.StandardInput?.WriteLine("quit");
                    _aiProcess?.Kill();
                }
                catch { }
            }
            Console.WriteLine("\nDisconnesso dal server.\n");
        }
    }
}