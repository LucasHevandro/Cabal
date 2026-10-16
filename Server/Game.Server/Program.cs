using System.Globalization;
using Game.Server.Net;
using Game.Shared.Net;

// Servidor do jogo.
// Uso: Game.Server [--port 7777] [--stats 10] [--latency 75] [--jitter 20]
//   --stats N     registra métricas de desempenho a cada N segundos
//   --latency N   (testes) atrasa cada mensagem N ms em cada sentido — ida e volta = 2N
//   --jitter N    (testes) soma de 0 a N ms aleatórios ao atraso
var options = new ServerOptions();
for (var i = 0; i < args.Length - 1; i++)
{
    var value = args[i + 1];
    switch (args[i])
    {
        case "--port" when int.TryParse(value, out var port):
            options = options with { Port = port };
            break;
        case "--stats" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var stats):
            options = options with { StatsIntervalSeconds = stats };
            break;
        case "--latency" when int.TryParse(value, out var latency):
            options = options with { SimulatedLatencyMs = Math.Max(0, latency) };
            break;
        case "--jitter" when int.TryParse(value, out var jitter):
            options = options with { SimulatedJitterMs = Math.Max(0, jitter) };
            break;
    }
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

using var host = new ServerHost(options, Log);
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
