using Game.Client.Monsters;
using Game.Client.World;
using System.Collections.Generic;
using Game.Shared.Monsters;
using Game.Shared.World;
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
        // Centros das áreas para estradas e enfeites (as zonas em si vêm do Shared).
        private static readonly Vector3 Village = Vector3.zero;
        private static readonly Vector3 Fields = new Vector3(0, 0, 40);
        private static readonly Vector3 Forest = new Vector3(45, 0, 85);
        private static readonly Vector3 Ruins = new Vector3(-45, 0, 125);
        private static readonly Vector3 BossArena = new Vector3(0, 0, 170);

        /// <summary>Aparência de cada monstro no protótipo (os dados de jogo ficam no Shared).</summary>
        private static readonly Dictionary<string, (MonsterShape Shape, Vector3 Scale, Color Color)> Looks =
            new Dictionary<string, (MonsterShape, Vector3, Color)>
            {
                ["coelho_saltador"] = (MonsterShape.Sphere, new Vector3(0.7f, 0.5f, 0.7f), new Color(0.95f, 0.95f, 0.9f)),
                ["lobo_cinzento"] = (MonsterShape.Capsule, new Vector3(0.8f, 0.7f, 1.3f), new Color(0.5f, 0.5f, 0.55f)),
                ["javali_furioso"] = (MonsterShape.Cube, new Vector3(1.1f, 0.6f, 1.6f), new Color(0.45f, 0.3f, 0.18f)),
                ["aranha_da_mata"] = (MonsterShape.Sphere, new Vector3(1.6f, 0.6f, 1.6f), new Color(0.2f, 0.3f, 0.12f)),
                ["golem_de_pedra"] = (MonsterShape.Cube, new Vector3(1.6f, 1.3f, 1.6f), new Color(0.55f, 0.55f, 0.58f)),
                ["espectro"] = (MonsterShape.Capsule, new Vector3(0.9f, 1.1f, 0.9f), new Color(0.45f, 0.3f, 0.7f)),
                ["guardiao_das_ruinas"] = (MonsterShape.Capsule, new Vector3(2.2f, 2.2f, 2.2f), new Color(0.6f, 0.15f, 0.12f)),
            };

        private static readonly Dictionary<string, Color> ZoneTints = new Dictionary<string, Color>
        {
            ["Vila de Partida"] = new Color(0.55f, 0.5f, 0.42f),
            ["Campos do Vento"] = new Color(0.45f, 0.62f, 0.35f),
            ["Mata Sombria"] = new Color(0.25f, 0.42f, 0.25f),
            ["Ruínas Esquecidas"] = new Color(0.45f, 0.43f, 0.48f),
            ["Arena do Guardião"] = new Color(0.4f, 0.25f, 0.22f),
        };

        public static void Build(Material ring, Material telegraph)
        {
            var world = new GameObject("Mundo").transform;

            CreateGround(world);
            foreach (var zone in WorldContent.Zones())
            {
                var tint = ZoneTints.TryGetValue(zone.Name, out var color) ? color : new Color(0.5f, 0.5f, 0.5f);
                Zone(world, zone.Name, zone.MinLevel, zone.MaxLevel, new Vector3(zone.CenterX, 0, zone.CenterZ), zone.Radius, tint);
            }

            Road(world, Village, Fields);
            Road(world, Fields, Forest);
            Road(world, Forest, Ruins);
            Road(world, Ruins, BossArena);

            Scatter(world, "Árvore", Fields, 22, 10, PrimitiveType.Cylinder, new Vector3(0.6f, 2.5f, 0.6f), new Color(0.35f, 0.25f, 0.15f), 11);
            Scatter(world, "Árvore", Forest, 24, 26, PrimitiveType.Cylinder, new Vector3(0.9f, 4f, 0.9f), new Color(0.25f, 0.18f, 0.1f), 22);
            Scatter(world, "Copa", Forest, 24, 18, PrimitiveType.Sphere, new Vector3(3.5f, 3f, 3.5f), new Color(0.15f, 0.35f, 0.15f), 23, 6f);
            Scatter(world, "Pedra", Ruins, 24, 16, PrimitiveType.Cube, new Vector3(2f, 3f, 2f), new Color(0.5f, 0.48f, 0.5f), 33);
            Scatter(world, "Coluna", BossArena, 16, 8, PrimitiveType.Cylinder, new Vector3(1.4f, 5f, 1.4f), new Color(0.5f, 0.4f, 0.35f), 44);

            foreach (var spawn in WorldContent.Spawns())
            {
                if (!Looks.TryGetValue(spawn.Data.Id, out var look))
                    throw new System.InvalidOperationException($"Sem aparência definida para o monstro {spawn.Data.Id}.");
                Spawner(world, new Vector3(spawn.CenterX, 0, spawn.CenterZ), spawn.Count, spawn.Radius, look.Shape,
                    look.Scale, look.Color, ring, telegraph, spawn.Data, spawn.IsBoss);
            }
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
