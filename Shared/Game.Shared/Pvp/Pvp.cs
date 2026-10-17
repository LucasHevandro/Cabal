using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Shared.Pvp
{
    /// <summary>
    /// Ajuste de dano entre jogadores, separado do PvE: os jogadores têm muito menos vida que os monstros, então o mesmo
    /// golpe mataria em segundos. Números para discutir no balanceamento.
    /// </summary>
    public static class PvpRules
    {
        /// <summary>Fração do dano normal que um golpe causa em outro jogador.</summary>
        public const double DamageScale = 0.35;

        /// <summary>O bônus do Despertar vale pela metade no PvP (30% vira 15%).</summary>
        public const double AwakeningShare = 0.5;

        public static int Scale(int amount) => Math.Max(1, (int)Math.Round(amount * DamageScale));

        public static double AwakeningMultiplier(double pveMultiplier) => 1 + (pveMultiplier - 1) * AwakeningShare;
    }

    /// <summary>Duelo combinado entre dois jogadores: ninguém morre, perde quem chegar a 1 de vida.</summary>
    public static class DuelRules
    {
        public const double InviteSeconds = 20;

        /// <summary>Distância para pedir e aceitar.</summary>
        public const float Range = 10f;

        public const double CountdownSeconds = 3;
        public const double MaxSeconds = 180;

        /// <summary>Sair deste raio (a partir do ponto do início) é desistir.</summary>
        public const float BoundaryRadius = 30f;
    }

    public enum ArenaTeam : sbyte
    {
        None = -1,
        Blue = 0,
        Red = 1,
    }

    /// <summary>Ponto de captura da arena (posição a partir da origem da partida).</summary>
    public sealed class CapturePoint
    {
        public string Name = "";
        public float X;
        public float Z;
    }

    /// <summary>Arena 5 contra 5 de captura de pontos.</summary>
    public static class ArenaRules
    {
        public const int TeamSize = 5;
        public const double MatchSeconds = 10 * 60;

        /// <summary>Tempo entre formar os times e começar (todos vão para a base).</summary>
        public const double CountdownSeconds = 10;

        /// <summary>Ganha quem chegar primeiro a esta pontuação (ou quem tiver mais quando o tempo acabar).</summary>
        public const int WinScore = 500;

        /// <summary>Cada ponto dominado dá 1 por segundo; cada abate, 3.</summary>
        public const int ScorePerPointSecond = 1;

        public const int ScorePerKill = 3;

        public const float CaptureRadius = 5f;

        /// <summary>Segundos para tomar um ponto neutro sozinho (de um dono para o outro, o dobro).</summary>
        public const double CaptureSeconds = 6;

        public const double RespawnSeconds = 6;

        /// <summary>Depois do fim, o placar fica na tela por este tempo antes de todos voltarem à vila.</summary>
        public const double ResultSeconds = 15;

        public const long WinAlz = 1500;
        public const long LossAlz = 500;

        /// <summary>Tamanho do campo: X de -HalfWidth a +HalfWidth, Z de -HalfLength a +HalfLength.</summary>
        public const float HalfWidth = 32f;

        public const float HalfLength = 38f;

        /// <summary>Bases: azul ao sul, vermelho ao norte.</summary>
        public const float BaseZ = 33f;

        /// <summary>As partidas ficam ao sul do mapa aberto, cada uma na sua faixa (longe das dungeons).</summary>
        public const float OriginZ = -1000f;

        public const float Spacing = 400f;

        public static readonly IReadOnlyList<CapturePoint> Points = new List<CapturePoint>
        {
            new CapturePoint { Name = "A", X = -20, Z = 0 },
            new CapturePoint { Name = "B", X = 0, Z = 0 },
            new CapturePoint { Name = "C", X = 20, Z = 0 },
        };

        public static (float X, float Z) BaseOf(ArenaTeam team) => (0, team == ArenaTeam.Blue ? -BaseZ : BaseZ);

        public static string TeamName(ArenaTeam team) => team == ArenaTeam.Blue ? "Azul" : team == ArenaTeam.Red ? "Vermelho" : "-";

        /// <summary>
        /// Divide os jogadores (id, nível) em dois times parecidos: do maior nível para o menor, em "cobra"
        /// (A B B A A B ...), assim a soma dos níveis fica próxima.
        /// </summary>
        public static (List<int> Blue, List<int> Red) Balance(IEnumerable<(int Id, int Level)> players)
        {
            var blue = new List<int>();
            var red = new List<int>();
            var index = 0;
            foreach (var player in players.OrderByDescending(p => p.Level).ThenBy(p => p.Id))
            {
                var toBlue = index % 4 == 0 || index % 4 == 3;
                (toBlue ? blue : red).Add(player.Id);
                index++;
            }
            return (blue, red);
        }

        /// <summary>
        /// Avança a captura de um ponto. <paramref name="progress"/> vai de -1 (azul) a +1 (vermelho); 0 é neutro.
        /// Só anda quando há jogadores de um time só no círculo; com os dois, fica parado (disputado).
        /// Retorna o novo dono (ou o mesmo).
        /// </summary>
        public static ArenaTeam Advance(ref double progress, ArenaTeam owner, int bluePresent, int redPresent, double dt)
        {
            if ((bluePresent > 0) == (redPresent > 0)) return owner; // vazio ou disputado
            var direction = bluePresent > 0 ? -1 : 1;
            progress = Math.Max(-1, Math.Min(1, progress + direction * dt / CaptureSeconds));
            // Somar frações de segundo deixa sobras (0,9999…): encosta nos marcos.
            if (Math.Abs(progress) < 1e-9) progress = 0;
            else if (progress < -1 + 1e-9) progress = -1;
            else if (progress > 1 - 1e-9) progress = 1;
            if (progress <= -1) return ArenaTeam.Blue;
            if (progress >= 1) return ArenaTeam.Red;
            // Voltou a passar do meio: o antigo dono perde o ponto.
            if (owner == ArenaTeam.Blue && progress > 0) return ArenaTeam.None;
            if (owner == ArenaTeam.Red && progress < 0) return ArenaTeam.None;
            return owner;
        }
    }
}
