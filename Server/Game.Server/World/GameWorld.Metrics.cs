using System.Globalization;
using Game.Server.Persistence;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Net;

namespace Game.Server.World;

/// <summary>Contadores de uma sessão de jogo (de entrar até sair), para as métricas do teste fechado.</summary>
public sealed class SessionStats
{
    public int StartLevel { get; set; }
    public int Deaths { get; set; }
    public int Kills { get; set; }
    public int Excellent { get; set; }
    public int Good { get; set; }
    public int Miss { get; set; }
    public int Quests { get; set; }
    public int Dungeons { get; set; }
    public bool Ended { get; set; }
}

/// <summary>
/// Métricas do teste fechado: cada sessão vira um evento "session" ao sair, e o cliente manda relatórios curtos
/// (tutorial, FPS, erros). Os eventos ficam na fila do jogador e vão para o banco junto do salvamento.
/// Convidados (bots, testes) não contam. Detalhes no formato "chave=valor;chave=valor" (Docs/metricas.md).
/// </summary>
public sealed partial class GameWorld
{
    public const string SessionMetric = "session";

    /// <summary>Fecha a sessão do jogador (ao sair ou ao desligar o servidor). Só uma vez por sessão.</summary>
    public void EndSession(ServerPlayer player, double now)
    {
        if (player.IsGuest || player.Session.Ended) return;
        var s = player.Session;
        s.Ended = true;
        var details = string.Join(";",
            $"nivel={s.StartLevel}>{player.Progress.Level}", $"mortes={s.Deaths}", $"abates={s.Kills}",
            $"excellent={s.Excellent}", $"good={s.Good}", $"miss={s.Miss}", $"missoes={s.Quests}", $"dungeons={s.Dungeons}");
        player.PendingMetrics.Add(new MetricRecord(player.AccountId, player.CharacterId, SessionMetric,
            Math.Round(Math.Max(0, now - player.JoinedAt), 1), details, DateTimeOffset.UtcNow));
    }

    private static void CountGrade(ServerPlayer player, ComboGrade grade)
    {
        if (grade == ComboGrade.Excellent) player.Session.Excellent++;
        else if (grade == ComboGrade.Miss) player.Session.Miss++;
        else player.Session.Good++;
    }

    private void HandleClientReport(ServerPlayer player, ClientReport report)
    {
        if (player.IsGuest || !ClientReportRules.IsKnown(report.Kind)) return;
        if (++player.ClientReports > ClientReportRules.MaxPerSession) return;
        var value = float.IsFinite(report.Value) ? Math.Clamp(report.Value, -1_000_000f, 1_000_000f) : 0;
        var text = (report.Text ?? "").Replace('\n', ' ').Replace('\r', ' ');
        if (text.Length > ClientReportRules.MaxTextLength) text = text[..ClientReportRules.MaxTextLength];
        player.PendingMetrics.Add(new MetricRecord(player.AccountId, player.CharacterId, "client." + report.Kind,
            Math.Round(value, 2), text, DateTimeOffset.UtcNow));
    }

    /// <summary>Lê "chave=valor;chave=valor" (relatório de métricas).</summary>
    public static Dictionary<string, string> ParseDetails(string details)
    {
        var result = new Dictionary<string, string>();
        foreach (var part in (details ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            if (equals > 0) result[part[..equals]] = part[(equals + 1)..];
        }
        return result;
    }

    public static int DetailInt(Dictionary<string, string> details, string key) =>
        details.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
}
