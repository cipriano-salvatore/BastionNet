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

    static async Task Main(string[] args)
    {
        // 1. Genera la propria chiave ECDH
        var (privateKeyB64, publicKeyB64) = Crypto.GenerateEcdhKeyPair();
        lock (_keysLock)
        {
            _ownPrivateKeyBase64 = privateKeyB64;
        }

        Console.WriteLine("\n--- BastionNet Client con Crittografia End-to-End ---");
        Console.WriteLine($"Public key ECDH: {publicKeyB64.Substring(0, 40)}...");
        Console.WriteLine("Avvio Assistente AI...\n");

        // 2. Avvia l'assistente Python come processo figlio con pipe
        var pythonExe = "python3";
        var aiScript = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "src", "BastionNet.AI-Assistant", "main.py");
        // Fallback: cerca nella directory corrente del progetto
        if (!File.Exists(aiScript))
        {
            aiScript = "src/BastionNet.AI-Assistant/main.py";
        }

        try
        {
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
        catch (Exception ex)
        {
            Console.WriteLine($"[AI] Errore avvio assistente: {ex.Message}. Continuando senza suggerimenti AI.\n");
            _aiProcess = null;
        }

        // 3. Connessione al server sulla stessa rete locale
        _client = new TcpClient();
        await _client.ConnectAsync(IPAddress.Parse("127.0.0.1"), 5000);
        _stream = _client.GetStream();
        _cts = new CancellationTokenSource();

        Console.WriteLine($"Connesso al server BastionNet su {_client.Client.LocalEndPoint} -> {_client.Client.RemoteEndPoint}");
        Console.WriteLine("Digita messaggi e premi Invio per inviare. 'exit' per uscire. 'ai' per richiesta suggerimento AI.\n");

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
                    // Deserializza il messaggio
                    Message? msg;
                    try { msg = JsonSerializer.Deserialize<Message>(json); }
                    catch { continue; }

                    if (msg is KeyExchangeMessage)
                    {
                        lock (_keysLock)
                        {
                            // Estrai l'ID peer dall'endpoint o usa un identificativo semplificato
                            var peerEndPoint = msg.ToString(); // simplified
                            var peerId = peerEndPoint.GetHashCode().ToString();
                            _peerPublicKeys[peerId] = (msg as KeyExchangeMessage)?.PublicKey ?? string.Empty;
                        }
                        Console.WriteLine($"[KeyExchange] Ricevuta public key da un peer.");
                        continue;
                    }

                    if (msg is EncryptedMessage encryptedMsg)
                    {
                        // Decifra il messaggio usando ECDH + AES-256-CBC + HMAC
                        string? plaintext = null;
                        lock (_keysLock)
                        {
                            var peerPublicKey = _peerPublicKeys.Values.FirstOrDefault() ?? string.Empty;
                            if (!string.IsNullOrEmpty(peerPublicKey))
                            {
                                var aesKey = Crypto.DeriveAesKeyFromEcdh(_ownPrivateKeyBase64, peerPublicKey);
                                try
                                {
                                    plaintext = Crypto.DecryptAesCbcHmac(encryptedMsg.Iv, encryptedMsg.Tag, encryptedMsg.Ciphertext, aesKey);
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

                    // Messaggio di testo semplice
                    if (!string.IsNullOrEmpty(json))
                        Console.WriteLine($"[Ricevuto dal server]: {json}");
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
                    // Richiedi un suggerimento AI basato sull'input precedente
                    try
                    {
                        // Invia un prompt all'AI assistant
                        if (_aiProcess != null)
                        {
                            _aiProcess.StandardInput.Write("ai suggestion\n");
                            var suggestion = await _aiProcess.StandardOutput.ReadLineAsync();
                            if (!string.IsNullOrEmpty(suggestion))
                                Console.WriteLine($"[AI Suggerimento]: {suggestion}");
                            else
                                Console.WriteLine("[AI] Nessun suggerimento disponibile.");
                        }
                        else
                        {
                            Console.WriteLine("[AI] Assistente non disponibile.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AI Errore] {ex.Message}");
                    }
                    continue;
                }

                // Crittografa il messaggio usando ECDH + AES-256-CBC + HMAC
                // Per semplicità in questo prototype broadcastiamo a tutti usando la prima key peer disponibile
                string? peerPublicKey = null;
                lock (_keysLock)
                {
                    peerPublicKey = _peerPublicKeys.Values.FirstOrDefault();
                }

                string ciphertextJson;
                if (!string.IsNullOrEmpty(peerPublicKey))
                {
                    var aesKey = Crypto.DeriveAesKeyFromEcdh(_ownPrivateKeyBase64, peerPublicKey);
                    var (ivB64, tagB64, ctB64) = Crypto.EncryptAesCbcHmac(line, aesKey);
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