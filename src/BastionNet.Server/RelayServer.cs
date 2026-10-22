using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BastionNet.Shared;

namespace BastionNet.Server;

/// <summary>
/// Server relay centrale che inoltra i messaggi tra i client.
/// </summary>
public class RelayServer
{
    private readonly TcpListener _listener;
    private readonly ConcurrentBag<ClientHandler> _clients = new();
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Evento sollevato quando un client si connette.
    /// </summary>
    public event Action<ClientHandler>? ClientConnected;

    /// <summary>
    /// Evento sollevato quando un client si disconnette.
    /// </summary>
    public event Action<ClientHandler>? ClientDisconnected;

    /// <summary>
    /// Evento sollevato quando ricevo un messaggio da un client.
    /// </summary>
    public event Action<ClientHandler, string>? MessageReceived;

    /// <summary>
    /// Crea una nuova istanza del relay server.
    /// </summary>
    /// <param name="port">Porta su cui ascoltare.</param>
    /// <param name="logAction">Action per il logging.</param>
    public RelayServer(int port, Action<string> logAction)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        // Enable SO_REUSEADDR per riutilizzare la porta immediatamente dopo il chiusura
        // Evita l'errore "Address already in use" riavviando il server
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    }

    /// <summary>
    /// Avvia l'ascolto delle connessioni.
    /// </summary>
    public async Task StartAsync()
    {
        _listener.Start();
        Console.WriteLine($"[Server] Avviato su porta {_listener.LocalEndpoint}");

        while (!_cts.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync();
            var handler = new ClientHandler(this, client);
            _clients.Add(handler);

            _ = Task.Run(handler.HandleAsync);
            handler.MessageBroadcast += OnMessageBroadcast;
            ClientConnected?.Invoke(handler);
            Console.WriteLine($"[Server] Nuovo client connesso. Totale: {_clients.Count}");
        }
    }

    /// <summary>
    /// Ferma il server.
    /// </summary>
    public async Task StopAsync()
    {
        _cts.Cancel();
        foreach (var client in _clients)
        {
            try { client.TcpClient.Close(); } catch { }
        }
        _clients.Clear();
        _listener.Stop();
        Console.WriteLine("[Server] Fermato.");
    }

    /// <summary>
    /// Gestione disconnessione client segnalata dal ClientHandler.
    /// </summary>
    internal void HandleClientDisconnected(ClientHandler client)
    {
        _clients.TryTake(out _);
        ClientDisconnected?.Invoke(client);
        Console.WriteLine($"[Server] Client disconnesso. Rimanenti: {_clients.Count}");
    }

    /// <summary>
    /// Broadcast di un messaggio a tutti i client eccetto il mittente.
    /// </summary>
    /// <param name="message">Messaggio da broadcast.</param>
    /// <param name="senderId">ID del mittente da escludere.</param>
    internal void Broadcast(string message, Guid senderId)
    {
        foreach (var client in _clients)
        {
            if (client.Id == senderId) continue;
            _ = client.SendAsync(message);
        }
    }

    private void OnMessageBroadcast(ClientHandler sender, string message)
    {
        MessageReceived?.Invoke(sender, message);
        Broadcast(message, sender.Id);
    }
}