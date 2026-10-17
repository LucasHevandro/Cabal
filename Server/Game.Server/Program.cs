using System.Globalization;
using Game.Server.Accounts;
using Game.Server.Net;
using Game.Server.Persistence;
using Game.Shared.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

// Servidor do jogo e API de login.
// Uso: Game.Server [--port 7777] [--api-port 7778] [--db postgres|memory] [--guests] [--stats 10]
//                  [--latency 75] [--jitter 20] [--save-interval 10]
//   --db postgres  (padrão) banco PostgreSQL; conexão na variável GAME_DB ou a do docker-compose
//   --db memory    banco em memória (tudo se perde ao desligar) — desenvolvimento sem Docker
//   --guests       aceita entrar sem conta (bots e testes; convidados não são salvos)
//   --stats N      métricas de desempenho a cada N segundos
//   --latency N    (testes) atrasa cada mensagem N ms em cada sentido; --jitter N soma até N ms aleatórios
var options = new ServerOptions();
var apiPort = -1;
var database = "postgres";
for (var i = 0; i < args.Length; i++)
{
    var value = i + 1 < args.Length ? args[i + 1] : "";
    switch (args[i])
    {
        case "--port" when int.TryParse(value, out var port):
            options = options with { Port = port };
            break;
        case "--api-port" when int.TryParse(value, out var apiValue):
            apiPort = apiValue;
            break;
        case "--db":
            database = value;
            break;
        case "--guests":
            options = options with { AllowGuests = true };
            break;
        case "--stats" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var stats):
            options = options with { StatsIntervalSeconds = stats };
            break;
        case "--save-interval" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var save):
            options = options with { SaveIntervalSeconds = save };
            break;
        case "--latency" when int.TryParse(value, out var latency):
            options = options with { SimulatedLatencyMs = Math.Max(0, latency) };
            break;
        case "--jitter" when int.TryParse(value, out var jitter):
            options = options with { SimulatedJitterMs = Math.Max(0, jitter) };
            break;
    }
}
if (apiPort < 0) apiPort = options.Port + 1;

Console.OutputEncoding = System.Text.Encoding.UTF8;
void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

IGameStore store;
if (database == "memory")
{
    store = new InMemoryGameStore();
    Log("ATENÇÃO: banco em memória — contas e personagens somem ao desligar.");
}
else
{
    var connection = Environment.GetEnvironmentVariable("GAME_DB") ?? DatabaseDefaults.DevelopmentConnection;
    var factory = new PooledDbContextFactory<GameDbContext>(new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(connection).Options);
    var ef = new EfGameStore(factory);
    try
    {
        await ef.MigrateAsync();
    }
    catch (Exception ex)
    {
        Log($"ERRO: não foi possível conectar ao PostgreSQL ({ex.GetBaseException().Message}).");
        Log("Abra o Docker Desktop e rode 'docker compose up -d' na raiz do projeto, ou use '--db memory'.");
        return 1;
    }
    store = ef;
    Log("Banco PostgreSQL pronto (migrações aplicadas).");
}

var auth = new AuthService(store, new PasswordHasher(), new SessionTokens(), uids: new ItemUidAllocator(await store.MaxItemUidAsync()));
await using var saver = new CharacterSaver(store, Log);
await using var api = new LoginApi(auth, apiPort, Log);
await api.StartAsync();
Log($"API de login em http://localhost:{apiPort}/api (em produção, use HTTPS na frente)");

using var host = new ServerHost(options, Log, auth, saver);
using var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};

host.Start();
if (options.AllowGuests) Log("ATENÇÃO: aceitando convidados sem conta (--guests).");
Log("Ctrl+C para desligar (os personagens são salvos antes).");
stop.Wait();
Log("Desligando...");
host.Stop();
Log($"Salvamentos concluídos: {saver.Saved}, falhas: {saver.Failures}.");
return 0;
