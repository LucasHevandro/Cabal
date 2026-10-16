using Game.Client.Monsters;
using Game.Client.World;
using Game.Shared.Loot;
using Game.Shared.Monsters;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Monta o mapa da Fase 2: vila inicial (onde ficam os bonecos de treino), três zonas de caça
    /// com dificuldade crescente para o norte e a arena do chefe no fim da estrada.
    /// Números de monstros e drops são provisórios até a planilha de design.
    /// </summary>
    internal static class WorldBuilder
    {
        // Centros das áreas (o jogador nasce na vila, em 0,0,0).
        private static readonly Vector3 Village = Vector3.zero;
        private static readonly Vector3 Fields = new Vector3(0, 0, 40);
        private static readonly Vector3 Forest = new Vector3(45, 0, 85);
        private static readonly Vector3 Ruins = new Vector3(-45, 0, 125);
        private static readonly Vector3 BossArena = new Vector3(0, 0, 170);

        public static void Build(Material ring, Material telegraph)
        {
            var world = new GameObject("Mundo").transform;

            CreateGround(world);
            Zone(world, "Vila de Partida", 1, 30, Village, 16, new Color(0.55f, 0.5f, 0.42f));
            Zone(world, "Campos do Vento", 1, 10, Fields, 24, new Color(0.45f, 0.62f, 0.35f));
            Zone(world, "Mata Sombria", 10, 20, Forest, 26, new Color(0.25f, 0.42f, 0.25f));
            Zone(world, "Ruínas Esquecidas", 20, 30, Ruins, 26, new Color(0.45f, 0.43f, 0.48f));
            Zone(world, "Arena do Guardião", 30, 30, BossArena, 18, new Color(0.4f, 0.25f, 0.22f));

            Road(world, Village, Fields);
            Road(world, Fields, Forest);
            Road(world, Forest, Ruins);
            Road(world, Ruins, BossArena);

            Scatter(world, "Árvore", Fields, 22, 10, PrimitiveType.Cylinder, new Vector3(0.6f, 2.5f, 0.6f), new Color(0.35f, 0.25f, 0.15f), 11);
            Scatter(world, "Árvore", Forest, 24, 26, PrimitiveType.Cylinder, new Vector3(0.9f, 4f, 0.9f), new Color(0.25f, 0.18f, 0.1f), 22);
            Scatter(world, "Copa", Forest, 24, 18, PrimitiveType.Sphere, new Vector3(3.5f, 3f, 3.5f), new Color(0.15f, 0.35f, 0.15f), 23, 6f);
            Scatter(world, "Pedra", Ruins, 24, 16, PrimitiveType.Cube, new Vector3(2f, 3f, 2f), new Color(0.5f, 0.48f, 0.5f), 33);
            Scatter(world, "Coluna", BossArena, 16, 8, PrimitiveType.Cylinder, new Vector3(1.4f, 5f, 1.4f), new Color(0.5f, 0.4f, 0.35f), 44);

            // Campos do Vento (Nv 1–10)
            Spawner(world, Fields + new Vector3(-10, 0, -4), 5, 7, MonsterShape.Sphere, new Vector3(0.7f, 0.5f, 0.7f),
                new Color(0.95f, 0.95f, 0.9f), ring, telegraph, new MonsterData
                {
                    Id = "coelho_saltador", Name = "Coelho Saltador", Level = 2, MaxHp = 350, AttackDamage = 25,
                    AttackPower = 5, Defense = 20, AttackRange = 1.2f, MoveSpeed = 3.5f, Aggressive = false,
                    XpReward = 30, RespawnSeconds = 6,
                    Drops = new DropTable(3, 8,
                        new DropEntry("pelo_macio", "Pelo macio", ItemRarity.Common, 0.6, 1, 2),
                        new DropEntry("pocao_hp_p", "Poção de vida (P)", ItemRarity.Common, 0.15)),
                });
            Spawner(world, Fields + new Vector3(10, 0, 6), 4, 7, MonsterShape.Capsule, new Vector3(0.8f, 0.7f, 1.3f),
                new Color(0.5f, 0.5f, 0.55f), ring, telegraph, new MonsterData
                {
                    Id = "lobo_cinzento", Name = "Lobo Cinzento", Level = 6, MaxHp = 900, AttackDamage = 60,
                    AttackPower = 15, Defense = 40, AttackRange = 1.5f, MoveSpeed = 4.5f, AggroRadius = 8,
                    XpReward = 70, RespawnSeconds = 8,
                    Drops = new DropTable(10, 25,
                        new DropEntry("pele_lobo", "Pele de lobo", ItemRarity.Common, 0.5, 1, 2),
                        new DropEntry("presa_lobo", "Presa de lobo", ItemRarity.Uncommon, 0.15),
                        new DropEntry("nucleo_baixo", "Núcleo de melhoria (baixo)", ItemRarity.Rare, 0.03)),
                });

            // Mata Sombria (Nv 10–20)
            Spawner(world, Forest + new Vector3(-9, 0, -6), 4, 8, MonsterShape.Cube, new Vector3(1.1f, 0.6f, 1.6f),
                new Color(0.45f, 0.3f, 0.18f), ring, telegraph, new MonsterData
                {
                    Id = "javali_furioso", Name = "Javali Furioso", Level = 12, MaxHp = 2200, AttackDamage = 120,
                    AttackPower = 40, Defense = 90, AttackRange = 1.6f, AttackWindupSeconds = 0.7f, MoveSpeed = 5f,
                    AggroRadius = 9, XpReward = 200, RespawnSeconds = 10,
                    Drops = new DropTable(30, 70,
                        new DropEntry("couro_javali", "Couro de javali", ItemRarity.Common, 0.5, 1, 3),
                        new DropEntry("nucleo_baixo", "Núcleo de melhoria (baixo)", ItemRarity.Rare, 0.06)),
                });
            Spawner(world, Forest + new Vector3(9, 0, 7), 4, 8, MonsterShape.Sphere, new Vector3(1.6f, 0.6f, 1.6f),
                new Color(0.2f, 0.3f, 0.12f), ring, telegraph, new MonsterData
                {
                    Id = "aranha_da_mata", Name = "Aranha da Mata", Level = 16, MaxHp = 2600, AttackDamage = 150,
                    AttackPower = 55, Defense = 80, AttackRange = 2f, AttackWindupSeconds = 0.5f, MoveSpeed = 4f,
                    AggroRadius = 10, XpReward = 300, RespawnSeconds = 10,
                    Drops = new DropTable(40, 90,
                        new DropEntry("seda", "Seda de aranha", ItemRarity.Common, 0.5, 1, 2),
                        new DropEntry("veneno", "Glândula de veneno", ItemRarity.Uncommon, 0.2),
                        new DropEntry("nucleo_medio", "Núcleo de melhoria (médio)", ItemRarity.Rare, 0.04)),
                });

            // Ruínas Esquecidas (Nv 20–30)
            Spawner(world, Ruins + new Vector3(8, 0, -7), 3, 8, MonsterShape.Cube, new Vector3(1.6f, 1.3f, 1.6f),
                new Color(0.55f, 0.55f, 0.58f), ring, telegraph, new MonsterData
                {
                    Id = "golem_de_pedra", Name = "Golem de Pedra", Level = 22, MaxHp = 6000, AttackDamage = 240,
                    AttackPower = 80, Defense = 260, AttackRange = 2.2f, AttackCooldownSeconds = 3f,
                    AttackWindupSeconds = 1.0f, MoveSpeed = 2.2f, AggroRadius = 7, XpReward = 700, RespawnSeconds = 15,
                    Drops = new DropTable(80, 160,
                        new DropEntry("fragmento_pedra", "Fragmento de pedra viva", ItemRarity.Common, 0.6, 1, 3),
                        new DropEntry("nucleo_medio", "Núcleo de melhoria (médio)", ItemRarity.Rare, 0.08)),
                });
            Spawner(world, Ruins + new Vector3(-8, 0, 8), 4, 8, MonsterShape.Capsule, new Vector3(0.9f, 1.1f, 0.9f),
                new Color(0.45f, 0.3f, 0.7f), ring, telegraph, new MonsterData
                {
                    Id = "espectro", Name = "Espectro", Level = 26, MaxHp = 4500, AttackDamage = 280,
                    AttackPower = 100, Defense = 120, AttackRange = 2.5f, AttackCooldownSeconds = 1.6f,
                    AttackWindupSeconds = 0.45f, MoveSpeed = 5.5f, AggroRadius = 11, XpReward = 900, RespawnSeconds = 12,
                    Drops = new DropTable(100, 200,
                        new DropEntry("essencia", "Essência espectral", ItemRarity.Uncommon, 0.4),
                        new DropEntry("nucleo_alto", "Núcleo de melhoria (alto)", ItemRarity.Rare, 0.05)),
                });

            // Chefe de campo
            Spawner(world, BossArena, 1, 0, MonsterShape.Capsule, new Vector3(2.2f, 2.2f, 2.2f),
                new Color(0.6f, 0.15f, 0.12f), ring, telegraph, new MonsterData
                {
                    Id = "guardiao_das_ruinas", Name = "Guardião das Ruínas", Level = 30, MaxHp = 60000,
                    AttackDamage = 380, AttackPower = 140, Defense = 280, AttackRange = 3.2f,
                    AttackCooldownSeconds = 2.4f, AttackWindupSeconds = 0.9f, MoveSpeed = 3.5f, AggroRadius = 14,
                    LeashRadius = 30, WanderRadius = 0, XpReward = 15000, RespawnSeconds = 120,
                    Drops = new DropTable(2000, 4000,
                        new DropEntry("nucleo_alto", "Núcleo de melhoria (alto)", ItemRarity.Rare, 0.6, 1, 3),
                        new DropEntry("fragmento_guardiao", "Fragmento do Guardião", ItemRarity.Epic, 0.25)),
                }, boss: true);
        }

        private static void CreateGround(Transform parent)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Chão";
            plane.transform.SetParent(parent, false);
            plane.transform.position = new Vector3(0, 0, 80);
            plane.transform.localScale = new Vector3(26, 1, 30);
            plane.GetComponent<Renderer>().sharedMaterial = TrainingSceneBuilder.MaterialFor("Chao", new Color(0.42f, 0.45f, 0.42f));
        }

        private static void Zone(Transform parent, string name, int minLevel, int maxLevel, Vector3 center, float radius, Color tint)
        {
            var zone = new GameObject($"Zona: {name}");
            zone.transform.SetParent(parent, false);
            zone.transform.position = center;
            zone.AddComponent<ZoneArea>().Configure(name, minLevel, maxLevel, radius);

            // Disco colorido no chão para a zona ser reconhecível de longe.
            var disc = Visual(PrimitiveType.Cylinder, "Chão da zona", zone.transform,
                TrainingSceneBuilder.MaterialFor($"Zona_{Slug(name)}", tint));
            disc.transform.localPosition = new Vector3(0, 0.005f, 0);
            disc.transform.localScale = new Vector3(radius * 2, 0.001f, radius * 2);
        }

        private static void Road(Transform parent, Vector3 from, Vector3 to)
        {
            var road = Visual(PrimitiveType.Cube, "Estrada", parent,
                TrainingSceneBuilder.MaterialFor("Estrada", new Color(0.6f, 0.55f, 0.45f)));
            var middle = (from + to) / 2f;
            road.transform.position = new Vector3(middle.x, 0.01f, middle.z);
            road.transform.rotation = Quaternion.LookRotation(to - from);
            road.transform.localScale = new Vector3(4f, 0.01f, Vector3.Distance(from, to));
        }

        /// <summary>Espalha obstáculos (com colisão) pela zona, deixando o centro livre para os monstros.</summary>
        private static void Scatter(Transform parent, string name, Vector3 center, float radius, int count,
            PrimitiveType type, Vector3 scale, Color color, int seed, float heightOffset = 0f)
        {
            var material = TrainingSceneBuilder.MaterialFor($"{Slug(name)}_{seed}", color);
            var random = new System.Random(seed);
            for (var i = 0; i < count; i++)
            {
                var angle = random.NextDouble() * Mathf.PI * 2;
                var distance = radius * (0.55 + 0.45 * random.NextDouble());
                var position = center + new Vector3((float)(System.Math.Cos(angle) * distance), 0, (float)(System.Math.Sin(angle) * distance));
                var size = (float)(0.8 + 0.5 * random.NextDouble());

                var obj = GameObject.CreatePrimitive(type);
                obj.name = name;
                obj.transform.SetParent(parent, false);
                obj.transform.localScale = Vector3.Scale(scale, new Vector3(size, size, size));
                var height = type == PrimitiveType.Cube ? obj.transform.localScale.y / 2f
                    : type == PrimitiveType.Sphere ? obj.transform.localScale.y / 2f : obj.transform.localScale.y;
                obj.transform.position = position + Vector3.up * (height + heightOffset);
                obj.transform.rotation = Quaternion.Euler(0, (float)(random.NextDouble() * 360), 0);
                obj.GetComponent<Renderer>().sharedMaterial = material;
            }
        }

        private static void Spawner(Transform parent, Vector3 position, int count, float radius, MonsterShape shape,
            Vector3 scale, Color color, Material ring, Material telegraph, MonsterData data, bool boss = false)
        {
            data.Validate();
            var spawner = new GameObject($"Spawner: {data.Name}");
            spawner.transform.SetParent(parent, false);
            spawner.transform.position = position;
            spawner.AddComponent<MonsterSpawner>().Configure(data, count, radius, shape, scale,
                TrainingSceneBuilder.MaterialFor($"Monstro_{data.Id}", color), telegraph, ring, boss);
        }

        private static GameObject Visual(PrimitiveType type, string name, Transform parent, Material material)
        {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            Object.DestroyImmediate(obj.GetComponent<Collider>());
            obj.transform.SetParent(parent, false);
            obj.GetComponent<Renderer>().sharedMaterial = material;
            obj.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return obj;
        }

        private static string Slug(string text)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var c in text.Normalize(System.Text.NormalizationForm.FormD))
            {
                if (char.IsLetterOrDigit(c) && c < 128) builder.Append(c);
                else if (c == ' ') builder.Append('_');
            }
            return builder.ToString();
        }
    }
}
