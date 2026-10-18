using Game.Shared.Combat;

namespace Game.Server.World;

/// <summary>
/// Detecção básica de bot: só marca suspeitos (log do servidor e /gm suspeitos), nunca pune sozinha.
/// - Combo perfeito demais: uma sequência longa de Excellent seguidos (gente erra de vez em quando).
/// - Horas seguidas: muito tempo online sem sair.
/// - Rota repetida: o mesmo caminho, quadrado por quadrado, várias vezes seguidas.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>Excellent seguidos sem nenhum Good ou Miss no meio.</summary>
    public const int SuspiciousExcellentStreak = 150;

    /// <summary>Horas online sem sair.</summary>
    public const double SuspiciousHoursOnline = 6;

    /// <summary>A cada quantos segundos a posição é anotada (quadrados de 4 m) e quantas amostras formam uma janela.</summary>
    public const double RouteSampleSeconds = 10;

    public const int RouteWindowSamples = 30; // 5 minutos

    /// <summary>Janelas seguidas com exatamente o mesmo caminho.</summary>
    public const int SuspiciousRepeatedWindows = 4;

    internal void TrackComboGrade(ServerPlayer player, ComboGrade grade, double now)
    {
        player.ExcellentStreak = grade == ComboGrade.Excellent ? player.ExcellentStreak + 1 : 0;
        if (player.ExcellentStreak >= SuspiciousExcellentStreak) Suspect(player, "suspect.perfect_combo", now);
    }

    internal void TrackSuspicion(ServerPlayer player, double now)
    {
        if (now - player.JoinedAt > SuspiciousHoursOnline * 3600) Suspect(player, "suspect.hours_online", now);

        if (now < player.NextRouteSampleAt) return;
        player.NextRouteSampleAt = now + RouteSampleSeconds;
        // Parado não conta (ficar parado na vila conversando não é rota).
        var cell = ((int)MathF.Floor(player.X / 4f), (int)MathF.Floor(player.Z / 4f));
        player.RouteSamples.Add(cell);
        if (player.RouteSamples.Count < RouteWindowSamples) return;

        var distinct = player.RouteSamples.Distinct().Count();
        var signature = distinct < 4 ? 0 : string.Join(";", player.RouteSamples).GetHashCode();
        player.RouteSamples.Clear();
        if (signature == 0)
        {
            player.RepeatedRouteWindows = 0;
            player.LastRouteSignature = 0;
            return;
        }
        player.RepeatedRouteWindows = signature == player.LastRouteSignature ? player.RepeatedRouteWindows + 1 : 1;
        player.LastRouteSignature = signature;
        if (player.RepeatedRouteWindows >= SuspiciousRepeatedWindows) Suspect(player, "suspect.repeated_route", now);
    }

    private void Suspect(ServerPlayer player, string reasonKey, double now)
    {
        if (!player.Suspicions.TryAdd(reasonKey, now)) return;
        SuspicionRaised?.Invoke(player, reasonKey);
    }
}
