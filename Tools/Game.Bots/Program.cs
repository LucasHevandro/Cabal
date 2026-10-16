using System.Diagnostics;
using Game.Bots;
using Game.Shared.Net;

// Teste de carga: N jogadores falsos conectam, caçam nos Campos do Vento e fazem combos pelo protocolo real.
// Uso: Game.Bots [--host 127.0.0.1] [--port 7777] [--count 20] [--seconds 60]
var host = "127.0.0.1";
var port = Protocol.DefaultPort;
var count = 20;
var seconds = 60.0;
for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--host": host = args[i + 1]; break;
        case "--port": int.TryParse(args[i + 1], out port); break;
        case "--count": int.TryParse(args[i + 1], out count); break;
        case "--seconds": double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out seconds); break;
    }
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"Iniciando {count} bots em {host}:{port} por {seconds:0} s");

var clock = Stopwatch.StartNew();
var bots = new List<Bot>();
for (var i = 0; i < count; i++)
{
    // Um terço caça na Mata Sombria (mais monstros e com mais vida), o resto nos Campos do Vento.
    var forest = i % 3 == 2;
    var prey = forest ? new[] { "javali_furioso", "aranha_da_mata" } : new[] { "coelho_saltador", "lobo_cinzento" };
    var bot = forest ? new Bot($"Bot{i + 1:00}", 1000 + i, prey, 45, 85) : new Bot($"Bot{i + 1:00}", 1000 + i, prey, 0, 40);
    try
    {
        await bot.StartAsync(host, port);
        bots.Add(bot);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{bot.Name}: falhou ao conectar ({ex.Message})");
    }
    await Task.Delay(100); // não conecta todos no mesmo instante
}

var last = clock.Elapsed.TotalSeconds;
var nextReport = last + 10;
while (clock.Elapsed.TotalSeconds < seconds)
{
    var now = clock.Elapsed.TotalSeconds;
    var dt = (float)(now - last);
    last = now;
    foreach (var bot in bots) bot.Update(now, dt);

    if (now >= nextReport)
    {
        nextReport = now + 10;
        Console.WriteLine($"[{now:0}s] {Summary(bots, now)}");
    }
    Thread.Sleep(20);
}

var elapsed = clock.Elapsed.TotalSeconds;
Console.WriteLine();
Console.WriteLine("===== Resultado =====");
Console.WriteLine(Summary(bots, elapsed));

var rtts = bots.SelectMany(b => b.Stats.RttMs).OrderBy(r => r).ToList();
if (rtts.Count > 0)
    Console.WriteLine($"Latência (ida e volta): mediana {rtts[rtts.Count / 2]:0} ms, p95 {rtts[(int)(rtts.Count * 0.95)]:0} ms, máx {rtts[^1]:0} ms");

var grades = new int[3];
foreach (var bot in bots) for (var g = 0; g < 3; g++) grades[g] += bot.Stats.Grades[g];
var graded = grades.Sum();
if (graded > 0)
    Console.WriteLine($"Combo (julgado pelo servidor): {grades[2]} Excellent ({grades[2] * 100.0 / graded:0}%), " +
                      $"{grades[1]} Good ({grades[1] * 100.0 / graded:0}%), " +
                      $"{bots.Sum(b => b.Stats.CombosBrokenByServer)} combos quebrados por Miss em {bots.Sum(b => b.Stats.CombosStarted)} iniciados");

Console.WriteLine($"Combos iniciados: {bots.Sum(b => b.Stats.CombosStarted)}");
var misses = bots.SelectMany(b => b.Stats.MissDetails.Select(d => $"{b.Name} {d}")).Take(10).ToList();
if (misses.Count > 0)
{
    Console.WriteLine("Apertos julgados como Miss pelo servidor (visão do bot no momento do aperto):");
    foreach (var miss in misses) Console.WriteLine("  " + miss);
}
var notices = new Dictionary<string, int>();
foreach (var bot in bots)
    foreach (var (text, n) in bot.Stats.Notices) notices[text] = notices.GetValueOrDefault(text) + n;
Console.WriteLine("Avisos do servidor (mais comuns):");
foreach (var (text, n) in notices.OrderByDescending(kv => kv.Value).Take(8)) Console.WriteLine($"  {n,5}x  {text}");

var disconnected = bots.Where(b => !b.Connection.Connected).ToList();
foreach (var bot in disconnected) Console.WriteLine($"{bot.Name} desconectou: {bot.Connection.Error}");

var ok = bots.Count == count && disconnected.Count == 0 && bots.Sum(b => b.Stats.HitsConfirmed) > 0;
Console.WriteLine(ok ? "SUCESSO" : "FALHA");
foreach (var bot in bots) bot.Connection.Dispose();
return ok ? 0 : 1;

static string Summary(List<Bot> bots, double seconds)
{
    var connected = bots.Count(b => b.Connection.Connected && b.PlayerId != 0);
    var kb = bots.Sum(b => b.Connection.BytesReceived) / 1024.0;
    return $"{connected}/{bots.Count} conectados | golpes confirmados {bots.Sum(b => b.Stats.HitsConfirmed)}, " +
           $"dano {bots.Sum(b => b.Stats.DamageDealt)} | mortes de monstros ~{bots.Sum(b => b.Stats.Kills)} | " +
           $"bots mortos {bots.Sum(b => b.Stats.Deaths)} | correções de posição {bots.Sum(b => b.Stats.Corrections)} | " +
           $"recebido {kb / Math.Max(1, seconds) / Math.Max(1, bots.Count):0.0} KB/s por bot";
}
