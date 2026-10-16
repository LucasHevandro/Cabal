using System.Collections.Generic;
using Game.Shared.Loot;
using UnityEngine;

namespace Game.Client.Loot
{
    /// <summary>Item caído no chão: flutua, brilha na cor da raridade e espera ser coletado.</summary>
    public sealed class LootPickup : MonoBehaviour
    {
        private static readonly List<LootPickup> Active = new List<LootPickup>();

        private const float LifetimeSeconds = 90f;

        private Vector3 _restPosition;
        private float _spawnedAt;
        private float _phase;

        public ItemDrop Drop { get; private set; }

        /// <summary>Itens no chão agora.</summary>
        public static IReadOnlyList<LootPickup> All => Active;

        public static Color ColorFor(ItemRarity rarity) => rarity switch
        {
            ItemRarity.Uncommon => new Color(0.35f, 0.9f, 0.35f),
            ItemRarity.Rare => new Color(0.3f, 0.55f, 1f),
            ItemRarity.Epic => new Color(0.75f, 0.35f, 1f),
            _ => new Color(0.9f, 0.9f, 0.9f),
        };

        public void Initialize(ItemDrop drop)
        {
            Drop = drop;
            _restPosition = transform.position;
            _spawnedAt = Time.time;
            _phase = Random.Range(0f, Mathf.PI * 2f);
        }

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        private void Update()
        {
            if (Time.time - _spawnedAt > LifetimeSeconds)
            {
                Destroy(gameObject);
                return;
            }
            var t = Time.time * 2f + _phase;
            transform.position = _restPosition + Vector3.up * (0.35f + Mathf.Sin(t) * 0.12f);
            transform.Rotate(0, 90f * Time.deltaTime, 0, Space.World);
        }
    }
}
