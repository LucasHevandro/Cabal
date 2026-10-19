using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>
    /// O modelo pisca claro por um instante quando apanha (todo jogo de ação faz isso: deixa claro que o golpe pegou).
    /// Clareia a cor base dos materiais Lit por 0,12 s e volta ao normal. Só mexe em renderers sem MaterialPropertyBlock
    /// próprio (os do brilho da arma e dos pisos ficam de fora).
    /// </summary>
    public static class HitFlash
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly HashSet<GameObject> Flashing = new HashSet<GameObject>();

        public static void Play(GameObject model)
        {
            if (model == null || CombatFx.Instance == null || Flashing.Contains(model)) return;
            CombatFx.Instance.StartCoroutine(Run(model));
        }

        private static IEnumerator Run(GameObject model)
        {
            Flashing.Add(model);
            var renderers = new List<(Renderer Renderer, Color Color)>();
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var material = renderer.sharedMaterial;
                if (material == null || renderer.HasPropertyBlock() || !material.HasProperty(BaseColor)) continue;
                if (!material.shader.name.Contains("Lit") || material.shader.name.Contains("Particles")) continue;
                renderers.Add((renderer, material.GetColor(BaseColor)));
            }
            var block = new MaterialPropertyBlock();
            const float seconds = 0.12f;
            for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                var k = 1f + 1.8f * (1f - t / seconds);
                foreach (var (renderer, color) in renderers)
                {
                    if (renderer == null) continue;
                    block.SetColor(BaseColor, new Color(color.r * k + 0.25f, color.g * k + 0.25f, color.b * k + 0.25f, color.a));
                    renderer.SetPropertyBlock(block);
                }
                yield return null;
            }
            foreach (var (renderer, _) in renderers)
                if (renderer != null) renderer.SetPropertyBlock(null);
            Flashing.Remove(model);
        }
    }
}
