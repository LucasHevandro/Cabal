using System.Collections.Generic;
using Game.Shared.Progression;
using Game.Shared.World;
using UnityEngine;

namespace Game.Client.World
{
    /// <summary>
    /// Altura do chão do mundo aberto. A mesma fórmula gera o relevo do terreno (no Editor, WorldBuilder) e põe no chão
    /// o que o servidor manda só em X e Z (outros jogadores, monstros, NPCs, itens e avisos de golpe).
    /// Onde se luta e anda muito (vila, estradas, grupos de monstros, NPCs, arena do chefe) o chão é plano; entre eles há
    /// morros suaves, e fora da área jogável do servidor sobem as montanhas que fecham o mapa.
    /// Fora do terreno (dungeon e arena, longe daqui) a altura é 0.
    /// </summary>
    public static class Ground
    {
        // Retângulo do terreno (maior que a área jogável, para as montanhas da borda).
        public const float MinX = -210f;
        public const float MinZ = -150f;
        public const float SizeX = 420f;
        public const float SizeZ = 460f;
        public const float MaxHeight = 45f;

        /// <summary>O terreno começa este tanto abaixo do zero (o leito do rio fica abaixo do chão normal).</summary>
        public const float Base = 3f;

        /// <summary>Altura da água do rio (o leito chega a -1,4 m; a margem fica no zero).</summary>
        public const float WaterLevel = -0.45f;

        private const float RiverHalfWidth = 2.6f;

        // Ponte de madeira em arco onde a estrada cruza o rio (VillageBuilder monta as tábuas com estes números).
        public const int BridgePlanks = 9; // de cada lado do meio
        public const float BridgePlankStep = 0.55f;
        public const float BridgeRise = 0.25f;
        public const float BridgeHalfWidth = 2.3f;
        public const float BridgePlankThickness = 0.12f;
        private const float BridgeBase = -0.05f;

        // Área jogável (igual ao limite do servidor, GameWorld.IsInsideWorld).
        private const float PlayMinX = -130f, PlayMaxX = 130f, PlayMinZ = -70f, PlayMaxZ = 230f;

        /// <summary>Estradas ligando as zonas (vila → campos → mata → ruínas → arena).</summary>
        public static readonly Vector2[] Road =
        {
            new Vector2(0, 0), new Vector2(0, 40), new Vector2(45, 85), new Vector2(-45, 125), new Vector2(0, 170),
        };

        private struct Flat
        {
            public Vector2 Center;
            public float Radius;
            public float Blend;
        }

        private static List<Flat> _flats;
        private static List<ZoneDefinition> _zones;

        /// <summary>Altura do chão em (x, z): do terreno da cena, se houver, ou da fórmula.</summary>
        public static float HeightAt(float x, float z)
        {
            if (!Inside(x, z)) return 0f;
            var terrain = Terrain.activeTerrain;
            var ground = terrain != null ? terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y : Shape(x, z);
            return Mathf.Max(ground, BridgeDeck(x, z));
        }

        /// <summary>Altura do meio da tábua da ponte em arco no ponto (sem o terreno), antes da espessura.</summary>
        public static float BridgeArch(float t) => BridgeRise * (1f - t * t) + BridgeBase;

        /// <summary>
        /// Topo das tábuas da ponte em (x, z), ou -infinito fora dela. A ponte não tem colisão: quem anda nela (jogador,
        /// monstros, os outros jogadores) fica em cima por esta altura, que o <see cref="HeightAt"/> já inclui.
        /// </summary>
        public static float BridgeDeck(float x, float z)
        {
            var half = BridgePlanks * BridgePlankStep + BridgePlankStep * 0.5f;
            var along = z - RiverZ(0f);
            if (Mathf.Abs(x) > BridgeHalfWidth || Mathf.Abs(along) > half) return float.NegativeInfinity;
            var t = Mathf.Clamp(along / (BridgePlanks * BridgePlankStep), -1f, 1f);
            return BridgeArch(t) + BridgePlankThickness * 0.5f;
        }

        /// <summary>O ponto (x, z) já no chão.</summary>
        public static Vector3 Point(float x, float z) => new Vector3(x, HeightAt(x, z), z);

        /// <summary>A mesma posição com a altura do chão.</summary>
        public static Vector3 Snap(Vector3 position) => Point(position.x, position.z);

        public static bool Inside(float x, float z) => x > MinX && x < MinX + SizeX && z > MinZ && z < MinZ + SizeZ;

        /// <summary>Verdadeiro onde o chão foi aplainado (lugares de luta e caminhos): sem obstáculos ali.</summary>
        public static bool IsKeepOut(float x, float z, float extra = 0f) => Flatness(x, z, extra) < 0.5f;

        /// <summary>A fórmula do relevo (metros).</summary>
        public static float Shape(float x, float z)
        {
            // O ruído fica perto de 0,5 quase sempre: estica para ter várzeas (0) e morros (1).
            var hills = Mathf.Clamp01((Fbm(x * 0.018f + 11.3f, z * 0.018f + 4.7f, 3) - 0.3f) / 0.45f);
            var height = hills * Amplitude(x, z) * Flatness(x, z, 0f);
            // Perto do rio a várzea fica no zero; senão, num morro, o leito cavado ficaria acima da água (trecho seco).
            if (OutsideDistance(x, z) < -6f)
            {
                var bank = Mathf.Clamp01((RiverHalfWidth + 10f - Mathf.Abs(z - RiverZ(x))) / 6f);
                height *= 1f - bank * bank * (3f - 2f * bank);
            }

            // Montanhas: começam a subir um pouco antes do limite do servidor e fecham o horizonte.
            var outside = OutsideDistance(x, z);
            if (outside > -10f)
            {
                var t = Mathf.Clamp01((outside + 10f) / 55f);
                var peaks = 22f + 20f * Fbm(x * 0.012f + 3.1f, z * 0.012f + 8.9f, 4);
                height += t * t * (3f - 2f * t) * peaks;
            }
            // Rio entre a vila e os Campos do Vento: cava o leito (a estrada passa por cima, na ponte).
            height -= RiverDepth(x, z);
            return Mathf.Clamp(height, -Base + 0.5f, MaxHeight - Base - 1f);
        }

        /// <summary>Centro do rio (em Z) na posição X: uma curva suave de leste a oeste.</summary>
        public static float RiverZ(float x) => 25f + 2.2f * Mathf.Sin(x * 0.045f + 1.2f) + 1.2f * Mathf.Sin(x * 0.11f);

        /// <summary>
        /// Quanto o leito do rio está abaixo do chão no ponto (0 fora do rio). O rio passa inteiro, também por baixo da
        /// ponte: quem atravessa anda nas tábuas (<see cref="BridgeDeck"/>), não num aterro.
        /// </summary>
        public static float RiverDepth(float x, float z)
        {
            if (OutsideDistance(x, z) > -6f) return 0f;
            var d = Mathf.Abs(z - RiverZ(x));
            var t = Mathf.Clamp01((RiverHalfWidth + 2.2f - d) / 2.6f);
            return t * t * (3f - 2f * t) * 1.4f;
        }

        /// <summary>Dentro da água (para não pôr árvore, pedra nem casa no rio).</summary>
        public static bool InRiver(float x, float z, float margin = 1.5f) =>
            OutsideDistance(x, z) < -6f && Mathf.Abs(z - RiverZ(x)) < RiverHalfWidth + margin;

        /// <summary>Quanto o ponto está fora da área jogável (negativo = dentro, em metros até a borda).</summary>
        public static float OutsideDistance(float x, float z)
        {
            var dx = Mathf.Max(PlayMinX - x, x - PlayMaxX);
            var dz = Mathf.Max(PlayMinZ - z, z - PlayMaxZ);
            if (dx > 0 && dz > 0) return Mathf.Sqrt(dx * dx + dz * dz);
            return Mathf.Max(dx, dz);
        }

        /// <summary>Peso de cada zona no ponto (0 a 1, somando no máximo 1); o resto é "entre zonas".</summary>
        public static float ZoneWeight(string zoneId, float x, float z)
        {
            var total = 0f;
            var mine = 0f;
            foreach (var zone in Zones())
            {
                var w = ZoneInfluence(zone, x, z);
                total += w;
                if (zone.Id == zoneId) mine = w;
            }
            return total <= 0f ? 0f : mine / Mathf.Max(1f, total);
        }

        private static float ZoneInfluence(ZoneDefinition zone, float x, float z)
        {
            var d = Vector2.Distance(new Vector2(x, z), new Vector2(zone.CenterX, zone.CenterZ)) / (zone.Radius * 1.6f);
            return Mathf.Exp(-d * d * 2f);
        }

        /// <summary>Morros mais altos na mata e nas ruínas, quase nada na vila e na arena.</summary>
        private static float Amplitude(float x, float z)
        {
            var amplitude = 2.2f; // entre zonas
            var weightSum = 0f;
            var weighted = 0f;
            foreach (var zone in Zones())
            {
                var w = ZoneInfluence(zone, x, z);
                var a = zone.Id switch
                {
                    "vila" => 0.3f,
                    "campos_do_vento" => 1.6f,
                    "mata_sombria" => 3.2f,
                    "ruinas_esquecidas" => 2.6f,
                    _ => 0.6f,
                };
                weightSum += w;
                weighted += w * a;
            }
            var blend = Mathf.Clamp01(weightSum);
            return Mathf.Lerp(amplitude, weightSum > 0 ? weighted / weightSum : amplitude, blend);
        }

        /// <summary>0 = plano (lugar de luta, estrada, NPC), 1 = livre para ter relevo.</summary>
        private static float Flatness(float x, float z, float extra)
        {
            var point = new Vector2(x, z);
            var flat = 1f;
            foreach (var f in Flats())
            {
                var d = Vector2.Distance(point, f.Center);
                flat = Mathf.Min(flat, Smooth(f.Radius + extra, f.Radius + extra + f.Blend, d));
            }
            for (var i = 0; i < Road.Length - 1; i++)
            {
                var d = DistanceToSegment(point, Road[i], Road[i + 1]);
                flat = Mathf.Min(flat, Smooth(3.5f + extra, 9f + extra, d));
            }
            return flat;
        }

        public static float DistanceToRoad(float x, float z)
        {
            var point = new Vector2(x, z);
            var best = float.MaxValue;
            for (var i = 0; i < Road.Length - 1; i++) best = Mathf.Min(best, DistanceToSegment(point, Road[i], Road[i + 1]));
            return best;
        }

        private static List<ZoneDefinition> Zones() => _zones ??= WorldContent.Zones();

        private static List<Flat> Flats()
        {
            if (_flats != null) return _flats;
            var list = new List<Flat>
            {
                new Flat { Center = Vector2.zero, Radius = 15f, Blend = 12f }, // praça da vila
            };
            foreach (var zone in Zones())
            {
                if (zone.Id == "arena_do_guardiao") list.Add(new Flat { Center = new Vector2(zone.CenterX, zone.CenterZ), Radius = zone.Radius, Blend = 10f });
            }
            foreach (var spawn in WorldContent.Spawns())
                list.Add(new Flat { Center = new Vector2(spawn.CenterX, spawn.CenterZ), Radius = spawn.Radius + 3f, Blend = 10f });
            foreach (var npc in NpcCatalog.All)
                list.Add(new Flat { Center = new Vector2(npc.X, npc.Z), Radius = 3f, Blend = 5f });
            return _flats = list;
        }

        private static float Smooth(float edge0, float edge1, float x)
        {
            var t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Ruído fractal de 0 a 1.</summary>
        public static float Fbm(float x, float z, int octaves)
        {
            var sum = 0f;
            var amplitude = 1f;
            var norm = 0f;
            for (var i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x, z) * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                x = x * 2.03f + 17.1f;
                z = z * 2.03f + 5.3f;
            }
            return Mathf.Clamp01(sum / norm);
        }
    }
}
