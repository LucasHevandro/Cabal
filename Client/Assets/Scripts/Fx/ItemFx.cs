using System.Collections;
using Game.Shared.Loot;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>
    /// Efeitos de progresso: brilho de raridade nos itens no chão, subida de nível e resultado da melhoria na ferreira.
    /// Tudo com partículas geradas em código e o material de partículas da cena (o mesmo das faíscas do combate).
    /// </summary>
    public static class ItemFx
    {
        private static readonly Color Gold = new Color(1f, 0.85f, 0.3f);

        /// <summary>
        /// Brilho contínuo no item no chão, mais forte quanto mais raro: comum não tem; incomum solta poucas luzes; raro
        /// ganha um feixe de luz subindo; épico, feixe mais alto e um anel no chão.
        /// </summary>
        public static void AttachRarityGlow(Transform item, ItemRarity rarity, Color color)
        {
            if (rarity == ItemRarity.Common || CombatFx.Instance == null) return;
            var material = CombatFx.Instance.ParticleMaterial;

            // Luzes subindo em volta do item.
            var motes = NewSystem("Brilho", item, material, local: false);
            var main = motes.main;
            main.startLifetime = 1.2f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
            main.startColor = color;
            main.gravityModifier = -0.15f;
            var emission = motes.emission;
            emission.rateOverTime = rarity == ItemRarity.Uncommon ? 6f : rarity == ItemRarity.Rare ? 12f : 20f;
            var shape = motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 15f;
            shape.radius = 0.35f;
            shape.rotation = new Vector3(-90, 0, 0);
            FadeOut(motes);
            motes.Play();

            if (rarity < ItemRarity.Rare) return;

            // Feixe: partículas esticadas subindo do chão, como uma coluna de luz.
            var beam = NewSystem("Feixe", item, material, local: false);
            var beamMain = beam.main;
            var height = rarity >= ItemRarity.Epic ? 4.5f : 2.8f;
            beamMain.startLifetime = 0.9f;
            beamMain.startSpeed = height / 0.9f;
            beamMain.startSize = rarity >= ItemRarity.Epic ? 0.6f : 0.45f;
            beamMain.startColor = new Color(color.r, color.g, color.b, 0.85f);
            var beamEmission = beam.emission;
            beamEmission.rateOverTime = 40f;
            var beamShape = beam.shape;
            beamShape.enabled = true;
            beamShape.shapeType = ParticleSystemShapeType.Cone;
            beamShape.angle = 0f;
            beamShape.radius = 0.06f;
            beamShape.rotation = new Vector3(-90, 0, 0);
            beamShape.position = new Vector3(0, -0.4f, 0);
            var renderer = beam.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 4f;
            FadeOut(beam);
            beam.Play();

            if (rarity < ItemRarity.Epic) return;

            // Anel no chão pulsando para fora.
            var ring = NewSystem("Anel", item, material, local: false);
            var ringMain = ring.main;
            ringMain.startLifetime = 0.8f;
            ringMain.startSpeed = 1.2f;
            ringMain.startSize = 0.1f;
            ringMain.startColor = color;
            var ringEmission = ring.emission;
            ringEmission.rateOverTime = 0f;
            ringEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24, 24, 0, 1.2f) });
            var ringShape = ring.shape;
            ringShape.enabled = true;
            ringShape.shapeType = ParticleSystemShapeType.Circle;
            ringShape.radius = 0.2f;
            ringShape.radiusThickness = 0f;
            ringShape.rotation = new Vector3(90, 0, 0);
            ringShape.position = new Vector3(0, -0.3f, 0);
            FadeOut(ring);
            ring.Play();
        }

        /// <summary>Item raro ou melhor caiu: som e um estouro de luz para chamar a atenção.</summary>
        public static void RareDrop(Vector3 position, ItemRarity rarity, Color color)
        {
            if (rarity < ItemRarity.Rare) return;
            SoundPlayer.Play(SoundId.RareDrop, rarity >= ItemRarity.Epic ? 1f : 0.8f, 0f);
            if (CombatFx.Instance != null) CombatFx.Instance.Sparks(position + Vector3.up * 0.4f, color, rarity >= ItemRarity.Epic ? 50 : 30);
        }

        /// <summary>Poeirinha no chão (passos correndo, esquiva): sobe e espalha.</summary>
        public static void Dust(Vector3 position, int count, float spread)
        {
            var fx = CombatFx.Instance;
            if (fx == null) return;
            var ground = new Vector3(position.x, World.Ground.HeightAt(position.x, position.z) + 0.08f, position.z);
            var color = new Color(0.62f, 0.56f, 0.46f, 0.5f);
            for (var i = 0; i < count; i++)
            {
                var dir = Random.insideUnitCircle;
                fx.Glow(ground + new Vector3(dir.x, 0, dir.y) * 0.15f, new Vector3(dir.x * spread, Random.Range(0.3f, 0.7f), dir.y * spread),
                    color, Random.Range(0.25f, 0.45f), Random.Range(0.4f, 0.7f));
            }
        }

        /// <summary>Renascer: luz clara subindo em espiral pelo personagem.</summary>
        public static void Respawn(Vector3 position)
        {
            var fx = CombatFx.Instance;
            if (fx == null) return;
            var color = new Color(0.75f, 0.95f, 1f);
            for (var i = 0; i < 40; i++)
            {
                var a = i * 0.45f;
                var offset = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.7f;
                fx.Glow(position + offset + Vector3.up * (i * 0.05f), Vector3.up * 1.6f - offset * 0.5f, color, 0.18f, 0.8f);
            }
            fx.Flash(position + Vector3.up, color, 3f, 4f, 0.3f);
        }

        /// <summary>Monstro voltando: fumaça escura saindo do chão e um brilho roxo.</summary>
        public static void MonsterSpawn(Vector3 position, float size)
        {
            var fx = CombatFx.Instance;
            if (fx == null) return;
            for (var i = 0; i < 18; i++)
            {
                var dir = Random.insideUnitCircle * size;
                fx.Glow(position + new Vector3(dir.x, 0.1f, dir.y), Vector3.up * Random.Range(0.6f, 1.4f),
                    new Color(0.18f, 0.12f, 0.22f, 0.7f), Random.Range(0.5f, 0.9f) * size, Random.Range(0.6f, 1f));
            }
            for (var i = 0; i < 10; i++)
                fx.Glow(position + Vector3.up * 0.3f, Random.onUnitSphere * 1.5f + Vector3.up, new Color(0.6f, 0.35f, 1f), 0.15f, 0.5f);
        }

        /// <summary>Subida de nível: anel dourado se abrindo no chão e uma coluna de luz subindo pelo personagem.</summary>
        public static void LevelUp(Transform player)
        {
            if (CombatFx.Instance == null) return;
            CombatFx.Instance.StartCoroutine(LevelUpRoutine(player));
        }

        private static IEnumerator LevelUpRoutine(Transform player)
        {
            var material = CombatFx.Instance.ParticleMaterial;
            var host = new GameObject("Efeito: nível");
            host.transform.position = player.position;

            // Anel que se abre no chão.
            var ring = NewSystem("Anel", host.transform, material, local: false);
            var ringMain = ring.main;
            ringMain.loop = false;
            ringMain.startLifetime = 0.9f;
            ringMain.startSpeed = 5f;
            ringMain.startSize = 0.18f;
            ringMain.startColor = Gold;
            var ringShape = ring.shape;
            ringShape.enabled = true;
            ringShape.shapeType = ParticleSystemShapeType.Circle;
            ringShape.radius = 0.4f;
            ringShape.radiusThickness = 0f;
            ringShape.rotation = new Vector3(90, 0, 0);
            ringShape.position = new Vector3(0, 0.1f, 0);
            var ringEmission = ring.emission;
            ringEmission.rateOverTime = 0f;
            ringEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });
            FadeOut(ring);
            ring.Play();

            // Coluna subindo, que segue o jogador por 1,5 s.
            var column = NewSystem("Coluna", host.transform, material, local: false);
            var columnMain = column.main;
            columnMain.loop = true;
            columnMain.startLifetime = 1f;
            columnMain.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            columnMain.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            columnMain.startColor = new ParticleSystem.MinMaxGradient(Gold, Color.white);
            var columnShape = column.shape;
            columnShape.enabled = true;
            columnShape.shapeType = ParticleSystemShapeType.Cone;
            columnShape.angle = 0f;
            columnShape.radius = 0.6f;
            columnShape.rotation = new Vector3(-90, 0, 0);
            var columnEmission = column.emission;
            columnEmission.rateOverTime = 90f;
            FadeOut(column);
            column.Play();

            var until = Time.time + 1.5f;
            while (Time.time < until && player != null)
            {
                host.transform.position = player.position;
                yield return null;
            }
            column.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Object.Destroy(host, 2f);
        }

        /// <summary>
        /// Resultado da ferreira em volta do personagem: sucesso é um estouro dourado (maior a partir do +8); falha que
        /// mantém o nível é uma fumaça cinza; falha que cai de nível é um estouro vermelho e um tremor curto.
        /// </summary>
        public static void UpgradeResult(Transform player, bool success, bool downgraded, int newLevel)
        {
            if (CombatFx.Instance == null || player == null) return;
            var center = player.position + Vector3.up * 1.2f;
            if (success)
            {
                var big = newLevel >= 8;
                CombatFx.Instance.Sparks(center, Gold, big ? 90 : 45);
                CombatFx.Instance.Sparks(center, Color.white, big ? 40 : 15);
                SoundPlayer.Play(SoundId.UpgradeSuccess, 1f, 0f);
                if (big) LevelUp(player);
            }
            else if (downgraded)
            {
                CombatFx.Instance.Sparks(center, new Color(1f, 0.25f, 0.2f), 55);
                SoundPlayer.Play(SoundId.UpgradeFail, 1f, 0f);
                var camera = Camera.main != null ? Camera.main.GetComponent<Player.OrbitCamera>() : null;
                if (camera != null) camera.Shake(0.15f, 0.25f);
            }
            else
            {
                CombatFx.Instance.Sparks(center, new Color(0.55f, 0.55f, 0.6f), 30);
                SoundPlayer.Play(SoundId.UpgradeFail, 0.6f, 0f);
            }
        }

        private static ParticleSystem NewSystem(string name, Transform parent, Material material, bool local)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            var system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false;
            main.simulationSpace = local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var shape = system.shape;
            shape.enabled = false;
            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (material != null) renderer.sharedMaterial = material;
            return system;
        }

        /// <summary>Some aos poucos no fim da vida (tamanho e transparência).</summary>
        private static void FadeOut(ParticleSystem system)
        {
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
        }
    }
}
