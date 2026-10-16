using Game.Server.Net;
using Game.Shared.Net;

// Servidor do jogo. Uso: Game.Server [--port 7777]
var port = Protocol.DefaultPort;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--port" && int.TryParse(args[i + 1], out var parsed)) port = parsed;
}

void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

using var host = new ServerHost(port, Log);
using var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};

host.Start();
Log("Ctrl+C para desligar.");
stop.Wait();
Log("Desligando...");
host.Stop();
