using System;
using System.Collections.Generic;
using System.Linq;
using Game.Shared.Accounts;
using Game.Shared.Monsters;
using Game.Shared.Progression;

namespace Game.Shared.Dungeon
{
    /// <summary>Tempo de uma sala na estimativa.</summary>
    public sealed class DungeonRoomEstimate
    {
        public string Name = "";
        public int TotalHp;
        public double FightSeconds;
        public double OverheadSeconds;
        public double Seconds => FightSeconds + OverheadSeconds;
    }

    /// <summary>
    /// Estima quanto um grupo leva para fazer a dungeon: soma a vida dos monstros de cada sala e divide pelo dano por
    /// segundo do grupo (o mesmo cálculo da simulação de nível), mais o tempo de andar entre os grupos de monstros,
    /// e no chefe as perdas da mecânica (sair do golpe no chão e matar os servos do escudo).
    /// </summary>
    public static class DungeonEstimate
    {
        /// <summary>Andar até cada grupo de monstros, juntar e reposicionar.</summary>
        public const double SecondsPerPack = 12;

        /// <summary>Atravessar a sala e o portão.</summary>
        public const double SecondsPerRoom = 15;

        /// <summary>Parte do tempo do chefe perdida fugindo do golpe no chão.</summary>
        public const double SlamDpsLoss = 0.15;

        public static List<DungeonRoomEstimate> Run(DungeonDefinition dungeon, IReadOnlyList<(CharacterClass Class, int Level)> group,
            LevelingAssumptions? assumptions = null)
        {
            assumptions ??= new LevelingAssumptions();
            var result = new List<DungeonRoomEstimate>();
            foreach (var room in dungeon.Rooms)
            {
                var estimate = new DungeonRoomEstimate { Name = room.Name, OverheadSeconds = SecondsPerRoom };
                foreach (var spawn in room.Spawns)
                {
                    var data = spawn.Data;
                    estimate.TotalHp += data.MaxHp * spawn.Count;
                    var groupDps = GroupDps(group, data, assumptions);
                    estimate.FightSeconds += data.MaxHp * spawn.Count / groupDps;
                    estimate.OverheadSeconds += SecondsPerPack;
                }
                if (room.IsBossRoom)
                {
                    // Servos dos escudos e a perda do golpe no chão.
                    var rules = dungeon.Boss;
                    var servant = rules.Servant();
                    var servants = rules.ShieldAtHpFractions.Length * rules.ServantsPerShield;
                    estimate.TotalHp += servant.MaxHp * servants;
                    if (servants > 0)
                        estimate.FightSeconds = estimate.FightSeconds / (1 - SlamDpsLoss) + servant.MaxHp * servants / GroupDps(group, servant, assumptions);
                    estimate.OverheadSeconds += rules.ShieldAtHpFractions.Length * SecondsPerPack;
                }
                result.Add(estimate);
            }
            return result;
        }

        public static double TotalSeconds(IEnumerable<DungeonRoomEstimate> rooms) => rooms.Sum(r => r.Seconds);

        private static double GroupDps(IReadOnlyList<(CharacterClass Class, int Level)> group, MonsterData monster, LevelingAssumptions assumptions) =>
            group.Sum(member => LevelingSimulation.DpsAgainst(member.Class, member.Level, monster, assumptions));
    }
}
