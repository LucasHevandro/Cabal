using System;
using System.Collections.Generic;
using System.Linq;
using Game.Shared.Items;
using Game.Shared.Loot;
using Game.Shared.Monsters;
using Game.Shared.World;
using Game.Shared.Text;

namespace Game.Shared.Dungeon
{
    public enum DungeonMedal : byte
    {
        None = 0,
        B = 1,
        A = 2,
        S = 3,
    }

    /// <summary>Regras gerais das dungeons. Números para discutir no balanceamento.</summary>
    public static class DungeonRules
    {
        public const int MaxPlayers = 4;
        public const double TimeLimitSeconds = 15 * 60;

        /// <summary>Medalha S: até 8 minutos e combo máximo do grupo de pelo menos 12 acertos.</summary>
        public const double MedalSSeconds = 8 * 60;
        public const int MedalSCombo = 12;

        /// <summary>Medalha A: até 11 minutos (ou tempo de S sem o combo).</summary>
        public const double MedalASeconds = 11 * 60;

        /// <summary>Distância do portal em que todo o grupo precisa estar para entrar.</summary>
        public const float PortalRange = 12f;

        /// <summary>Depois de vencer, a instância fecha sozinha neste tempo (dá para pegar o baú e sair).</summary>
        public const double CloseAfterCompletionSeconds = 90;

        /// <summary>Cada cópia fica numa faixa própria do mapa, longe da outra e do mundo aberto.</summary>
        public const float InstanceOriginZ = 1000f;
        public const float InstanceSpacing = 400f;

        public static DungeonMedal Medal(double seconds, int maxCombo)
        {
            if (seconds > TimeLimitSeconds) return DungeonMedal.None;
            if (seconds <= MedalSSeconds && maxCombo >= MedalSCombo) return DungeonMedal.S;
            if (seconds <= MedalASeconds) return DungeonMedal.A;
            return DungeonMedal.B;
        }

        /// <summary>Sorteios do baú por jogador: S = 3, A = 2, B = 1.</summary>
        public static int ChestRolls(DungeonMedal medal) => medal switch
        {
            DungeonMedal.S => 3,
            DungeonMedal.A => 2,
            DungeonMedal.B => 1,
            _ => 0,
        };

        public static string MedalName(DungeonMedal medal) => medal == DungeonMedal.None ? "-" : Loc.T("medal." + medal);

        public static string FormatTime(double seconds)
        {
            var total = (int)Math.Max(0, Math.Floor(seconds));
            return $"{total / 60}:{total % 60:00}";
        }
    }

    /// <summary>Mecânica própria do chefe da cripta: escudo com servos e golpe no chão avisado.</summary>
    public static class CryptBossRules
    {
        /// <summary>Ao chegar nestas frações de vida, fica imune e chama servos; o escudo cai quando os servos morrem.</summary>
        public static readonly float[] ShieldAtHpFractions = { 0.66f, 0.33f };

        public const int ServantsPerShield = 3;
        public const float ServantRadius = 6f;

        public const double SlamIntervalSeconds = 9;
        public const float SlamWarningSeconds = 2f;
        public const float SlamRadius = 5f;
        public const int SlamDamage = 650;
    }

    /// <summary>Uma sala: faixa no eixo Z (a partir da origem da instância) e os monstros dela.</summary>
    public sealed class DungeonRoom
    {
        public string Id = "";

        /// <summary>Nome no idioma do jogador (chave <c>room.&lt;id&gt;</c>).</summary>
        public string Name => Loc.T("room." + Id);

        public float StartZ;
        public float EndZ;

        /// <summary>Posição (Z relativo) do portão no fim da sala; só abre quando a sala está limpa.</summary>
        public float GateZ => EndZ + 2f;

        public List<SpawnDefinition> Spawns = new List<SpawnDefinition>();
        public bool IsBossRoom;
    }

    public sealed class DungeonDefinition
    {
        public string Id = "";

        /// <summary>Nome no idioma do jogador (chave <c>dungeon.&lt;id&gt;</c>).</summary>
        public string Name => Loc.T("dungeon." + Id);

        public int MinLevel;
        public string KeyItemId = "";

        /// <summary>Meia largura do corredor (as paredes ficam em X = ±HalfWidth).</summary>
        public float HalfWidth = 10f;

        public float EntranceZ = 3f;
        public List<DungeonRoom> Rooms = new List<DungeonRoom>();
        public string BossId = "";

        /// <summary>Onde o baú aparece (Z relativo) e o que ele dá.</summary>
        public float ChestZ;

        public DropTable Chest = new DropTable();

        public float Length => Rooms.Count == 0 ? 0 : Rooms[Rooms.Count - 1].EndZ + 4f;

        public void Validate()
        {
            if (Rooms.Count == 0 || !Rooms[Rooms.Count - 1].IsBossRoom) throw new ArgumentException($"{Id}: a última sala é a do chefe.");
            foreach (var spawn in Rooms.SelectMany(r => r.Spawns)) spawn.Data.Validate();
            Chest.Validate();
        }
    }

    /// <summary>Dungeons do jogo. Cada chamada cria objetos novos (o servidor pode ajustar os números nos testes).</summary>
    public static class DungeonCatalog
    {
        public const string CryptId = "cripta_esquecida";
        public const string CryptKey = "chave_cripta";

        public static DungeonDefinition Crypt()
        {
            var crypt = new DungeonDefinition
            {
                Id = CryptId, MinLevel = 18, KeyItemId = CryptKey, BossId = "senhor_da_cripta",
                ChestZ = 172,
                Chest = new DropTable(800, 1500,
                    new DropEntry(ItemCatalog.CoreMedium, ItemRarity.Rare, 0.6, 1, 2),
                    new DropEntry(ItemCatalog.CoreHigh, ItemRarity.Rare, 0.3),
                    Item("pedra_vigor", 0.15), Item("pedra_furia", 0.15), Item("pedra_precisao", 0.15),
                    Item("armadura_astralita", 0.1), Item("luvas_astralita", 0.1), Item("botas_astralita", 0.1),
                    Item("anel_astralita", 0.1), Item("espada_astralita", 0.07), Item("cajado_astralita", 0.07)),
            };
            crypt.Rooms.Add(new DungeonRoom
            {
                Id = "salao_dos_ossos", StartZ = 0, EndZ = 40,
                Spawns = { Group(0, 26, 4, 6, Warrior()) },
            });
            crypt.Rooms.Add(new DungeonRoom
            {
                Id = "galeria_das_flechas", StartZ = 44, EndZ = 84,
                Spawns = { Group(0, 72, 3, 7, Archer()), Group(0, 62, 2, 4, Warrior()) },
            });
            crypt.Rooms.Add(new DungeonRoom
            {
                Id = "camara_dos_cavaleiros", StartZ = 88, EndZ = 128,
                Spawns = { Group(0, 112, 2, 5, Knight()), Group(0, 104, 3, 6, Warrior()) },
            });
            crypt.Rooms.Add(new DungeonRoom
            {
                Id = "trono_da_cripta", StartZ = 132, EndZ = 180, IsBossRoom = true,
                // Sem IsBoss: essa marca é do chefe de campo; o da cripta tem regras próprias (CryptBossRules).
                Spawns = { new SpawnDefinition { CenterX = 0, CenterZ = 160, Count = 1, Data = Boss() } },
            });
            return crypt;
        }

        public static DungeonDefinition? Find(string id) => id == CryptId ? Crypt() : null;

        /// <summary>Servo chamado pelo chefe durante o escudo.</summary>
        public static MonsterData Servant() => new MonsterData
        {
            Id = "servo_da_cripta", Level = 22, MaxHp = 20000, AttackDamage = 150, AttackPower = 60,
            Defense = 100, AttackRange = 1.5f, MoveSpeed = 4.5f, AggroRadius = 30, LeashRadius = 40, WanderRadius = 0,
            XpReward = 600, RespawnSeconds = 9999, Drops = new DropTable(20, 40),
        };

        private static MonsterData Warrior() => new MonsterData
        {
            Id = "esqueleto_guerreiro", Level = 22, MaxHp = 45000, AttackDamage = 220,
            AttackPower = 80, Defense = 160, AttackRange = 1.6f, AttackWindupSeconds = 0.6f, MoveSpeed = 4.2f,
            AggroRadius = 10, LeashRadius = 26, WanderRadius = 2, XpReward = 1800, RespawnSeconds = 9999,
            Drops = new DropTable(60, 120, new DropEntry("po_de_osso", ItemRarity.Common, 0.5, 1, 2)),
        };

        private static MonsterData Archer() => new MonsterData
        {
            Id = "esqueleto_arqueiro", Level = 22, MaxHp = 30000, AttackDamage = 180,
            AttackPower = 80, Defense = 110, AttackRange = 9f, AttackCooldownSeconds = 2.4f, AttackWindupSeconds = 0.8f,
            MoveSpeed = 3.5f, AggroRadius = 13, LeashRadius = 26, WanderRadius = 0, XpReward = 1600, RespawnSeconds = 9999,
            Drops = new DropTable(60, 120, new DropEntry("po_de_osso", ItemRarity.Common, 0.5, 1, 2)),
        };

        private static MonsterData Knight() => new MonsterData
        {
            Id = "cavaleiro_ossudo", Level = 24, MaxHp = 90000, AttackDamage = 300,
            AttackPower = 110, Defense = 260, AttackRange = 2.2f, AttackCooldownSeconds = 2.6f, AttackWindupSeconds = 0.9f,
            MoveSpeed = 3.2f, BodyRadius = 0.8f, AggroRadius = 10, LeashRadius = 26, WanderRadius = 0, XpReward = 3600,
            RespawnSeconds = 9999,
            Drops = new DropTable(120, 240,
                new DropEntry("po_de_osso", ItemRarity.Common, 0.6, 1, 3),
                new DropEntry(ItemCatalog.CoreMedium, ItemRarity.Rare, 0.1)),
        };

        private static MonsterData Boss() => new MonsterData
        {
            Id = "senhor_da_cripta", Level = 25, MaxHp = 450000, AttackDamage = 360, AttackPower = 130,
            Defense = 240, AttackRange = 3f, AttackCooldownSeconds = 2.4f, AttackWindupSeconds = 0.9f, MoveSpeed = 3.5f,
            BodyRadius = 1.1f, AggroRadius = 16, LeashRadius = 40, WanderRadius = 0, XpReward = 30000, RespawnSeconds = 9999,
            Drops = new DropTable(1000, 2000),
        };

        private static SpawnDefinition Group(float x, float z, int count, float radius, MonsterData data) =>
            new SpawnDefinition { CenterX = x, CenterZ = z, Count = count, Radius = radius, Data = data };

        private static DropEntry Item(string itemId, double chance)
        {
            var item = ItemCatalog.Get(itemId);
            return new DropEntry(itemId, item.Rarity, chance);
        }
    }
}
