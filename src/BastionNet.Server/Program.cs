// See https://aka.ms/new-console-template for more information
using BastionNet.Server;
using BastionNet.Shared;
using System;
using System.Threading;

var server = new RelayServer(5000, s => Console.WriteLine($"[LOG] {s}"));
Console.WriteLine("Starting BastionNet Server on port 5000...");

await server.StartAsync();

// Keep running
Console.WriteLine("Server running. Press Ctrl+C to stop.");
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) => cts.Cancel();
try { await Task.Delay(Timeout.Infinite, cts.Token); }
catch (TaskCanceledException) { }

await server.StopAsync();
Console.WriteLine("Server stopped.");