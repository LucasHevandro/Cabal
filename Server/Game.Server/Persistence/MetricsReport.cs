using System.Globalization;
using System.Text;
using Game.Server.World;

namespace Game.Server.Persistence;

/// <summary>
/// Resumo das métricas do teste fechado (opção <c>--report</c>): contas, sessões, retenção, progresso, combo, tutorial,
/// FPS e erros do cliente. Só números agregados; nenhum nome de conta aparece. Explicação em Docs/metricas.md.
/// </summary>
public static class MetricsReport
{
    /// <summary>Dias contados no horário de Brasília.</summary>
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    public static string Build(MetricsData data, DateTimeOffset since, DateTimeOffset now)
    {
        var text = new StringBuilder();
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        string Pct(double part, double total) => total <= 0 ? "-" : (part / total).ToString("0%", pt);
        DateTime Day(DateTimeOffset at) => at.ToOffset(Brasilia).Date;

        var sessions = data.Events.Where(e => e.Kind == GameWorld.SessionMetric).ToList();
        var accountsInPeriod = data.Accounts.Count(a => a.CreatedAt >= since);
        text.AppendLine(since == DateTimeOffset.MinValue
            ? "Métricas do teste fechado (desde o início)"
            : $"Métricas do teste fechado desde {Day(since):dd/MM/yyyy}");
        text.AppendLine($"Contas: {data.Accounts.Count} no total, {accountsInPeriod} criadas no período");

        // Sessões
        var players = sessions.Select(s => s.AccountId).Distinct().Count();
        var minutes = sessions.Select(s => s.Value / 60).OrderBy(m => m).ToList();
        text.AppendLine($"Jogadores com pelo menos uma sessão: {players} ({Pct(players, data.Accounts.Count)} das contas)");
        if (minutes.Count > 0)
        {
            text.AppendLine($"Sessões: {minutes.Count} | mediana {Median(minutes):0} min | média {minutes.Average():0} min | " +
                            $"total {minutes.Sum() / 60:0.0} h | {minutes.Count / (double)players:0.0} por jogador");
        }

        // Ativos por dia (últimos 14 dias com movimento)
        var perDay = sessions.GroupBy(s => Day(s.At)).OrderBy(g => g.Key).TakeLast(14).ToList();
        if (perDay.Count > 0)
            text.AppendLine("Jogadores por dia: " + string.Join("  ", perDay.Select(g => $"{g.Key:dd/MM} {g.Select(s => s.AccountId).Distinct().Count()}")));

        // Retenção: o dia da primeira sessão de cada conta (D0); voltou no dia seguinte (D1) ou 7+ dias depois (D7).
        var today = Day(now);
        var byAccount = sessions.GroupBy(s => s.AccountId).Select(g => g.Select(s => Day(s.At)).Distinct().OrderBy(d => d).ToList()).ToList();
        var d1Base = byAccount.Where(days => days[0] <= today.AddDays(-1)).ToList();
        var d7Base = byAccount.Where(days => days[0] <= today.AddDays(-7)).ToList();
        var d1 = d1Base.Count(days => days.Contains(days[0].AddDays(1)));
        var d7 = d7Base.Count(days => days.Any(d => d >= days[0].AddDays(7)));
        text.AppendLine($"Retenção: D1 {Pct(d1, d1Base.Count)} ({d1}/{d1Base.Count}) | D7 {Pct(d7, d7Base.Count)} ({d7}/{d7Base.Count})");

        // Progresso e combate
        if (sessions.Count > 0)
        {
            var details = sessions.Select(s => GameWorld.ParseDetails(s.Details)).ToList();
            var hours = sessions.Sum(s => s.Value) / 3600;
            int Sum(string key) => details.Sum(d => GameWorld.DetailInt(d, key));
            var maxLevel = sessions.Zip(details).GroupBy(p => p.First.CharacterId)
                .Select(g => g.Max(p => EndLevel(p.Second))).ToList();
            text.AppendLine("Nível mais alto por personagem: " + string.Join("  ",
                maxLevel.GroupBy(LevelBand).OrderBy(g => g.Min()).Select(g => $"{g.Key} {g.Count()}")));
            if (hours > 0)
                text.AppendLine($"Por hora de jogo: {Sum("abates") / hours:0} monstros, {Sum("mortes") / hours:0.0} mortes, " +
                                $"{Sum("missoes") / hours:0.0} missões");
            text.AppendLine($"Missões concluídas: {Sum("missoes")} | dungeons concluídas: {Sum("dungeons")}");
            var grades = Sum("excellent") + Sum("good") + Sum("miss");
            text.AppendLine($"Combo: {grades} notas | Excellent {Pct(Sum("excellent"), grades)} | Good {Pct(Sum("good"), grades)} | " +
                            $"Miss {Pct(Sum("miss"), grades)}");
        }

        // Tutorial: até que passo cada personagem chegou (funil).
        var tutorial = data.Events.Where(e => e.Kind == "client.tutorial").ToList();
        if (tutorial.Count > 0)
        {
            var started = tutorial.Select(e => e.CharacterId).Distinct().Count();
            text.AppendLine($"Tutorial (personagens que mandaram dados: {started}):");
            foreach (var step in tutorial.GroupBy(e => (Index: (int)e.Value, Name: e.Details)).OrderBy(g => g.Key.Index))
            {
                var reached = step.Select(e => e.CharacterId).Distinct().Count();
                text.AppendLine($"  {step.Key.Index,2} {step.Key.Name,-14} {reached,4}  {Pct(reached, started)}");
            }
        }

        // FPS: média de cada relatório de 5 minutos.
        var fps = data.Events.Where(e => e.Kind == "client.fps" && e.Value > 0).Select(e => e.Value).OrderBy(v => v).ToList();
        if (fps.Count > 0)
            text.AppendLine($"FPS: mediana {Median(fps):0} | 10% piores abaixo de {fps[(int)(fps.Count * 0.1)]:0} | {fps.Count} relatórios");

        var errors = data.Events.Where(e => e.Kind == "client.error").ToList();
        if (errors.Count > 0)
        {
            text.AppendLine($"Erros do cliente: {errors.Count} (em {errors.Select(e => e.AccountId).Distinct().Count()} contas). Mais comuns:");
            foreach (var group in errors.GroupBy(e => e.Details).OrderByDescending(g => g.Count()).Take(5))
                text.AppendLine($"  {group.Count(),4}x {group.Key}");
        }
        return text.ToString();
    }

    private static int EndLevel(Dictionary<string, string> details) =>
        details.TryGetValue("nivel", out var v) && int.TryParse(v.Split('>').Last(), out var level) ? level : 0;

    private static string LevelBand(int level) => level <= 1 ? "Nv1" : level <= 5 ? "Nv2-5" : level <= 10 ? "Nv6-10" : level <= 20 ? "Nv11-20" : "Nv21+";

    private static double Median(IReadOnlyList<double> sorted) =>
        sorted.Count == 0 ? 0 : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
}
