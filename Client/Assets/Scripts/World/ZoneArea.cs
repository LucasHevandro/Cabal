using System.Collections.Generic;
using Game.Shared.Text;
using UnityEngine;

namespace Game.Client.World
{
    /// <summary>Região do mapa com nome e faixa de nível, mostrada no HUD quando o jogador entra.</summary>
    public sealed class ZoneArea : MonoBehaviour
    {
        private static readonly List<ZoneArea> Active = new List<ZoneArea>();

        [SerializeField] private string displayName = "Zona";
        [SerializeField] private int minLevel = 1;
        [SerializeField] private int maxLevel = 10;
        [SerializeField] private float radius = 25f;

        /// <summary>Nome da zona no idioma do jogador (a cena guarda a chave, ex.: zone.vila).</summary>
        public string DisplayName => Loc.Has(displayName) ? Loc.T(displayName) : displayName;
        public int MinLevel => minLevel;
        public int MaxLevel => maxLevel;
        public float Radius => radius;

        public void Configure(string name, int min, int max, float areaRadius)
        {
            displayName = name;
            minLevel = min;
            maxLevel = max;
            radius = areaRadius;
        }

        /// <summary>Zona em que o ponto está (a de menor raio, se houver sobreposição), ou nula.</summary>
        public static ZoneArea At(Vector3 position)
        {
            ZoneArea best = null;
            foreach (var zone in Active)
            {
                var offset = position - zone.transform.position;
                offset.y = 0;
                if (offset.magnitude > zone.radius) continue;
                if (best == null || zone.radius < best.radius) best = zone;
            }
            return best;
        }

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
