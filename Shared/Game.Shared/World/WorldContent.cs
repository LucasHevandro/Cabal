using System;
using System.Collections.Generic;
using Game.Shared.Combat;
using Game.Shared.Items;
using Game.Shared.Loot;
using Game.Shared.Monsters;

namespace Game.Shared.World
{
    /// <summary>Região do mapa com nome e faixa de nível.</summary>
    public sealed class ZoneDefinition
    {
        public string Name = "";
        public int MinLevel;
        public int MaxLevel;
        public float CenterX;
        public float CenterZ;
        public float Radius;
    }

    /// <summary>Grupo de monstros iguais em volta de um ponto.</summary>
    public sealed class SpawnDefinition
    {
        public MonsterData Data = new MonsterData();
        public float CenterX;
        public float CenterZ;
        public int Count = 1;
        public float Radius;
        public bool IsBoss;

        /// <summary>
        /// Pontos de nascimento, sempre os mesmos para a mesma semente (o servidor e as ferramentas concordam).
        /// </summary>
        public List<(float X, float Z)> SpawnPoints(int seed)
        {
            var random = new Random(seed);
            var points = new List<(float, float)>(Count);
            for (var i = 0; i < Count; i++)
            {
                var angle = i / (double)Math.Max(1, Count) * Math.PI * 2 + (random.NextDouble() - 0.5) * 0.6;
                var distance = Count == 1 ? 0 : Radius * (0.4 + 0.6 * random.NextDouble());
                points.Add(((float)(CenterX + Math.Cos(angle) * distance), (float)(CenterZ + Math.Sin(angle) * distance)));
            }
            return points;
        }
    }

    /// <summary>
    /// Conteúdo do mapa da Fase 2: zonas, monstros e drops. Fonte única para o servidor e para o construtor
    /// da cena do cliente. Números provisórios até a planilha de design.
    /// Cada chamada cria objetos novos (o chefe altera os próprios dados durante a luta).
    /// </summary>
    public static class WorldContent
    {
        public const float PlayerSpawnX = 0f;
        public const float PlayerSpawnZ = 0f;

        public static List<ZoneDefinition> Zones() => new List<ZoneDefinition>
        {
            new ZoneDefinition { Name = "Vila de Partida", MinLevel = 1, MaxLevel = 30, CenterX = 0, CenterZ = 0, Radius = 16 },
            new ZoneDefinition { Name = "Campos do Vento", MinLevel = 1, MaxLevel = 10, CenterX = 0, CenterZ = 40, Radius = 24 },
            new ZoneDefinition { Name = "Mata Sombria", MinLevel = 10, MaxLevel = 20, CenterX = 45, CenterZ = 85, Radius = 26 },
            new ZoneDefinition { Name = "Ruínas Esquecidas", MinLevel = 20, MaxLevel = 30, CenterX = -45, CenterZ = 125, Radius = 26 },
            new ZoneDefinition { Name = "Arena do Guardião", MinLevel = 30, MaxLevel = 30, CenterX = 0, CenterZ = 170, Radius = 18 },
        };

        public static List<SpawnDefinition> Spawns() => new List<SpawnDefinition>
        {
            // Campos do Vento (Nv 1–10)
            Spawn(-10, 36, 5, 7, new MonsterData
            {
                Id = "coelho_saltador", Name = "Coelho Saltador", Level = 2, MaxHp = 350, AttackDamage = 25,
                AttackPower = 5, Defense = 20, AttackRange = 1.2f, MoveSpeed = 3.5f, BodyRadius = 0.35f, Aggressive = false,
                XpReward = 30, RespawnSeconds = 6,
                Drops = new DropTable(3, 8,
                    new DropEntry("pelo_macio", "Pelo macio", ItemRarity.Common, 0.6, 1, 2),
                    new DropEntry("pocao_hp_p", "Poção de vida (P)", ItemRarity.Common, 0.15),
                    Equip("armadura_ferro", 0.02), Equip("anel_ferro", 0.01)),
            }),
            Spawn(10, 46, 4, 7, new MonsterData
            {
                Id = "lobo_cinzento", Name = "Lobo Cinzento", Level = 6, MaxHp = 900, AttackDamage = 60,
                AttackPower = 15, Defense = 40, AttackRange = 1.5f, MoveSpeed = 4.5f, AggroRadius = 8,
                XpReward = 70, RespawnSeconds = 8,
                Drops = new DropTable(10, 25,
                    new DropEntry("pele_lobo", "Pele de lobo", ItemRarity.Common, 0.5, 1, 2),
                    new DropEntry("presa_lobo", "Presa de lobo", ItemRarity.Uncommon, 0.15),
                    new DropEntry("nucleo_baixo", "Núcleo de melhoria (baixo)", ItemRarity.Rare, 0.03),
                    Equip("luvas_ferro", 0.03), Equip("botas_ferro", 0.03), Equip("espada_ferro", 0.02),
                    Equip("cajado_ferro", 0.02), Equip("pedra_vigor", 0.01)),
            }),

            // Mata Sombria (Nv 10–20)
            Spawn(36, 79, 4, 8, new MonsterData
            {
                Id = "javali_furioso", Name = "Javali Furioso", Level = 12, MaxHp = 2200, AttackDamage = 120,
                AttackPower = 40, Defense = 90, AttackRange = 1.6f, AttackWindupSeconds = 0.7f, MoveSpeed = 5f,
                AggroRadius = 9, XpReward = 200, RespawnSeconds = 10,
                Drops = new DropTable(30, 70,
                    new DropEntry("couro_javali", "Couro de javali", ItemRarity.Common, 0.5, 1, 3),
                    new DropEntry("nucleo_baixo", "Núcleo de melhoria (baixo)", ItemRarity.Rare, 0.06),
                    Equip("armadura_aco_negro", 0.02), Equip("botas_aco_negro", 0.03), Equip("pedra_furia", 0.01)),
            }),
            Spawn(54, 92, 4, 8, new MonsterData
            {
                Id = "aranha_da_mata", Name = "Aranha da Mata", Level = 16, MaxHp = 2600, AttackDamage = 150,
                AttackPower = 55, Defense = 80, AttackRange = 2f, AttackWindupSeconds = 0.5f, MoveSpeed = 4f, BodyRadius = 0.8f,
                AggroRadius = 10, XpReward = 300, RespawnSeconds = 10,
                Drops = new DropTable(40, 90,
                    new DropEntry("seda", "Seda de aranha", ItemRarity.Common, 0.5, 1, 2),
                    new DropEntry("veneno", "Glândula de veneno", ItemRarity.Uncommon, 0.2),
                    new DropEntry("nucleo_medio", "Núcleo de melhoria (médio)", ItemRarity.Rare, 0.04),
                    Equip("luvas_aco_negro", 0.03), Equip("anel_aco_negro", 0.02), Equip("espada_aco_negro", 0.015),
                    Equip("cajado_aco_negro", 0.015)),
            }),

            // Ruínas Esquecidas (Nv 20–30)
            Spawn(-37, 118, 3, 8, new MonsterData
            {
                Id = "golem_de_pedra", Name = "Golem de Pedra", Level = 22, MaxHp = 6000, AttackDamage = 240,
                AttackPower = 80, Defense = 260, AttackRange = 2.2f, AttackCooldownSeconds = 3f,
                AttackWindupSeconds = 1.0f, MoveSpeed = 2.2f, BodyRadius = 0.8f, AggroRadius = 7, XpReward = 700, RespawnSeconds = 15,
                Drops = new DropTable(80, 160,
                    new DropEntry("fragmento_pedra", "Fragmento de pedra viva", ItemRarity.Common, 0.6, 1, 3),
                    new DropEntry("nucleo_medio", "Núcleo de melhoria (médio)", ItemRarity.Rare, 0.08),
                    Equip("armadura_astralita", 0.015), Equip("botas_astralita", 0.02), Equip("pedra_precisao", 0.01)),
            }),
            Spawn(-53, 133, 4, 8, new MonsterData
            {
                Id = "espectro", Name = "Espectro", Level = 26, MaxHp = 4500, AttackDamage = 280,
                AttackPower = 100, Defense = 120, AttackRange = 2.5f, AttackCooldownSeconds = 1.6f,
                AttackWindupSeconds = 0.45f, MoveSpeed = 5.5f, AggroRadius = 11, XpReward = 900, RespawnSeconds = 12,
                Drops = new DropTable(100, 200,
                    new DropEntry("essencia", "Essência espectral", ItemRarity.Uncommon, 0.4),
                    new DropEntry("nucleo_alto", "Núcleo de melhoria (alto)", ItemRarity.Rare, 0.05),
                    Equip("luvas_astralita", 0.02), Equip("anel_astralita", 0.015), Equip("espada_astralita", 0.01),
                    Equip("cajado_astralita", 0.01)),
            }),

            // Chefe de campo
            new SpawnDefinition
            {
                CenterX = 0, CenterZ = 170, Count = 1, Radius = 0, IsBoss = true,
                Data = new MonsterData
                {
                    Id = "guardiao_das_ruinas", Name = "Guardião das Ruínas", Level = 30, MaxHp = 60000,
                    AttackDamage = 380, AttackPower = 140, Defense = 280, AttackRange = 3.2f,
                    AttackCooldownSeconds = 2.4f, AttackWindupSeconds = 0.9f, MoveSpeed = 3.5f, BodyRadius = 1.1f, AggroRadius = 14,
                    LeashRadius = 30, WanderRadius = 0, XpReward = 15000, RespawnSeconds = 120,
                    Drops = new DropTable(2000, 4000,
                        new DropEntry("nucleo_alto", "Núcleo de melhoria (alto)", ItemRarity.Rare, 0.6, 1, 3),
                        new DropEntry("fragmento_guardiao", "Fragmento do Guardião", ItemRarity.Epic, 0.25),
                        Equip("espada_astralita", 0.25), Equip("cajado_astralita", 0.25), Equip("armadura_astralita", 0.3)),
                },
            },
        };

        /// <summary>Drop de um item do catálogo (nome e raridade vêm dele).</summary>
        private static DropEntry Equip(string itemId, double chance)
        {
            var item = ItemCatalog.Get(itemId);
            return new DropEntry(itemId, item.Name, item.Rarity, chance);
        }

        private static SpawnDefinition Spawn(float x, float z, int count, float radius, MonsterData data) =>
            new SpawnDefinition { CenterX = x, CenterZ = z, Count = count, Radius = radius, Data = data };
    }

    /// <summary>Habilidades do chefe (fase 2 e ataque em área), iguais no cliente offline e no servidor.</summary>
    public static class BossRules
    {
        public const float PhaseTwoAtHpFraction = 0.5f;
        public const float PhaseTwoCooldownFactor = 0.6f;
        public const float AreaIntervalSeconds = 6f;
        public const float AreaWarningSeconds = 1.6f;
        public const float AreaRadius = 4f;
        public const int AreaDamage = 350;
    }

    /// <summary>Regras do personagem que o cliente offline e o servidor precisam concordar.</summary>
    public static class PlayerRules
    {
        public const int MaxSp = 2000;
        public const int StartingSp = 1000;
        public const int SpPerHit = 60;
        public const float MpRegenPerSecond = 20f;
        public const float RespawnSeconds = 4f;
        public const int FinisherBaseDamage = 400;
        public const float FinisherRange = 3f;
        public const double CritChance = 0.1;
        public const double CritMultiplier = 1.5;
        public const float BodyRadius = 0.4f;

        /// <summary>Folga do alcance do golpe do monstro: o jogador precisa sair de fato ou esquivar.</summary>
        public const float MonsterReachTolerance = 1.3f;

        /// <summary>Duração da invulnerabilidade da esquiva.</summary>
        public const float DodgeSeconds = 0.25f;

        public const float DodgeCooldownSeconds = 0.8f;

        /// <summary>Distância máxima para coletar um item com a tecla.</summary>
        public const float PickupRange = 3.5f;

        /// <summary>Skills iniciais (teclas 1 a 4).</summary>
        public static List<SkillData> StarterSkills() => new List<SkillData>
        {
            new SkillData("corte", "Corte", SkillKind.Physical, 120, 2.5f, 0.5f),
            new SkillData("giro_de_lamina", "Giro de Lâmina", SkillKind.Physical, 90, 2.5f, 2.0f, areaRadius: 3.5f),
            new SkillData("lanca_de_forca", "Lança de Força", SkillKind.Magical, 160, 12f, 1.5f, mpCost: 40),
            new SkillData("explosao_arcana", "Explosão Arcana", SkillKind.Magical, 220, 10f, 5.0f, areaRadius: 4f, mpCost: 90),
        };
    }
}
