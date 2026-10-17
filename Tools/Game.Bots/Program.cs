using System.Diagnostics;
using Game.Bots;
using Game.Shared.Net;
using Game.Shared.Social;

// Teste de carga: N jogadores falsos conectam, caçam nos Campos do Vento e fazem combos pelo protocolo real.
// Uso: Game.Bots [--host 127.0.0.1] [--port 7777] [--count 20] [--seconds 60] [--party] [--invite Nome]
//   --party: os bots formam grupos de 4 (o primeiro de cada grupo convida, os outros aceitam) e conversam no chat.
//   --invite Nome: o primeiro bot convida esse personagem (um cliente de verdade) e fala no grupo quando ele entra.
var host = "127.0.0.1";
var port = Protocol.DefaultPort;
var count = 20;
var seconds = 60.0;
var party = args.Contains("--party");
var inviteName = "";
for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--invite": inviteName = args[i + 1]; break;
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
    // No modo grupo, cada grupo de 4 caça no mesmo lugar (para dividir XP e drop).
    if (party) forest = i / 4 % 3 == 2;
    if (party) prey = forest ? new[] { "javali_furioso", "aranha_da_mata" } : new[] { "coelho_saltador", "lobo_cinzento" };
    var bot = forest ? new Bot($"Bot{i + 1:00}", 1000 + i, prey, 45, 85) : new Bot($"Bot{i + 1:00}", 1000 + i, prey, 0, 40);
    bot.AutoAcceptInvites = party && i % 4 != 0;
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
var invitesSent = false;
var nextChatAt = last + 6;
var clientJoined = false;
var nextClientInviteAt = 0.0;
while (clock.Elapsed.TotalSeconds < seconds)
{
    var now = clock.Elapsed.TotalSeconds;
    var dt = (float)(now - last);
    last = now;
    foreach (var bot in bots) bot.Update(now, dt);

    // Convida depois que todos entraram no mundo (o convite vai pelo nome).
    if (!invitesSent && now > 2 && bots.All(b => b.PlayerId != 0))
    {
        invitesSent = true;
        if (party)
            for (var i = 0; i < bots.Count; i += 4)
                for (var j = i + 1; j < Math.Min(i + 4, bots.Count); j++)
                    bots[i].Invite(bots[j].Name);
    }
    // O cliente pode entrar depois dos bots: convida de novo a cada 5 s até ele aceitar.
    if (invitesSent && inviteName.Length > 0 && !clientJoined && bots.Count > 0 && now >= nextClientInviteAt)
    {
        nextClientInviteAt = now + 5;
        bots[0].Invite(inviteName);
    }
    if (party && now >= nextChatAt)
    {
        nextChatAt = now + 10;
        for (var i = 0; i < bots.Count; i += 4) bots[i].Say(ChatChannel.Party, $"grupo {i / 4 + 1}: tudo certo? que merda de lobo");
        if (bots.Count > 0) bots[^1].Say(ChatChannel.Map, "alguém vende poção?");
    }
    if (inviteName.Length > 0 && !clientJoined && bots.Count > 0 &&
        bots[0].Party.Members.Any(m => string.Equals(m.Name, inviteName, StringComparison.OrdinalIgnoreCase)))
    {
        clientJoined = true;
        bots[0].Say(ChatChannel.Party, "bem-vindo ao grupo!");
        bots[0].Say(ChatChannel.Whisper, "oi, sussurro do bot", inviteName);
        Console.WriteLine($"[{now:0}s] {inviteName} entrou no grupo do {bots[0].Name}");
    }

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
if (party)
{
    var grouped = bots.Count(b => b.Party.Members.Count == Math.Min(4, bots.Count - bots.IndexOf(b) / 4 * 4));
    Console.WriteLine($"Grupos: {grouped}/{bots.Count} bots no grupo certo | estados de grupo recebidos {bots.Sum(b => b.Stats.PartyStates)} | " +
                      $"chat recebido {bots.Sum(b => b.Stats.ChatReceived)} (grupo {bots.Sum(b => b.Stats.PartyChatReceived)}, sistema {bots.Sum(b => b.Stats.SystemChat)})");
    ok &= grouped == bots.Count && bots.Sum(b => b.Stats.PartyChatReceived) > 0;
}
if (inviteName.Length > 0)
{
    Console.WriteLine(clientJoined ? $"{inviteName} entrou no grupo" : $"{inviteName} NÃO entrou no grupo");
    ok &= clientJoined;
}
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
