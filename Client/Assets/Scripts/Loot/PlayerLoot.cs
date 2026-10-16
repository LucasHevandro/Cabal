using System.Collections.Generic;
using Game.Client.Combat;
using Game.Client.Fx;
using Game.Client.Monsters;
using Game.Client.Player;
using Game.Client.UI;
using Game.Shared.Combat;
using Game.Shared.Loot;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Client.Loot
{
    /// <summary>
    /// Recompensas do jogador no modo offline: ao matar um monstro, ganha XP, Alz e sorteia o drop
    /// (no jogo online isso será decidido pelo servidor). Itens caem no chão e são coletados
    /// passando por cima ou com a tecla E.
    /// </summary>
    [RequireComponent(typeof(PlayerProgression))]
    public sealed class PlayerLoot : MonoBehaviour
    {
        [SerializeField] private float autoPickupRadius = 0.9f;
        [SerializeField] private float keyPickupRadius = 3.5f;
        [SerializeField] private Material lootMaterial;

        /// <summary>Uma pilha de itens iguais no inventário provisório.</summary>
        public sealed class Stack
        {
            public string ItemId;
            public string Name;
            public ItemRarity Rarity;
            public int Quantity;
        }

        private readonly List<Stack> _inventory = new List<Stack>();
        private readonly IRandomSource _random = new SystemRandomSource();
        private PlayerProgression _progression;

        public long Alz { get; private set; }
        public IReadOnlyList<Stack> Inventory => _inventory;
        public int MonstersKilled { get; private set; }

        public void SetLootMaterial(Material material) => lootMaterial = material;

        private void Awake() => _progression = GetComponent<PlayerProgression>();

        private void OnEnable() => MonsterController.AnyKilled += OnMonsterKilled;
        private void OnDisable() => MonsterController.AnyKilled -= OnMonsterKilled;

        private void Update()
        {
            var player = PlayerCombat.Local;
            if (player != null && player.IsDead) return;

            var keyboard = Keyboard.current;
            var pressedE = keyboard != null && keyboard.eKey.wasPressedThisFrame;

            for (var i = LootPickup.All.Count - 1; i >= 0; i--)
            {
                var pickup = LootPickup.All[i];
                var offset = pickup.transform.position - transform.position;
                offset.y = 0;
                var distance = offset.magnitude;
                if (distance <= autoPickupRadius || (pressedE && distance <= keyPickupRadius)) Collect(pickup);
            }
        }

        private void OnMonsterKilled(MonsterController monster)
        {
            MonstersKilled++;
            _progression.GainMonsterXp(monster.Data.XpReward, monster.Data.Level);

            var loot = monster.Data.Drops.Roll(_random);
            if (loot.Alz > 0)
            {
                Alz += loot.Alz;
                DamageNumbers.Spawn(monster.transform.position + Vector3.up * 1.6f, $"+{loot.Alz} Alz", new Color(1f, 0.85f, 0.3f), 22f);
            }

            for (var i = 0; i < loot.Items.Count; i++)
            {
                var angle = i * 2.4f;
                var position = monster.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (0.6f + 0.25f * i);
                SpawnPickup(loot.Items[i], position);
            }
        }

        private void SpawnPickup(ItemDrop drop, Vector3 position)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Drop: {drop.Name}";
            Destroy(cube.GetComponent<Collider>());
            cube.transform.position = new Vector3(position.x, 0, position.z);
            cube.transform.localScale = Vector3.one * (drop.Rarity >= ItemRarity.Rare ? 0.4f : 0.3f);

            var renderer = cube.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (lootMaterial != null)
            {
                renderer.sharedMaterial = lootMaterial;
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", LootPickup.ColorFor(drop.Rarity));
                renderer.SetPropertyBlock(block);
            }

            cube.AddComponent<LootPickup>().Initialize(drop);
            if (drop.Rarity >= ItemRarity.Rare)
                SoundPlayer.Play(SoundId.Excellent, 0.8f, 0f);
        }

        private void Collect(LootPickup pickup)
        {
            var drop = pickup.Drop;
            var stack = _inventory.Find(s => s.ItemId == drop.ItemId);
            if (stack == null)
            {
                stack = new Stack { ItemId = drop.ItemId, Name = drop.Name, Rarity = drop.Rarity };
                _inventory.Add(stack);
            }
            stack.Quantity += drop.Quantity;

            DamageNumbers.Spawn(transform.position + Vector3.up * 2.4f, $"{drop.Name} x{drop.Quantity}", LootPickup.ColorFor(drop.Rarity), 20f);
            SoundPlayer.Play(SoundId.Good, 0.5f);
            Destroy(pickup.gameObject);
        }
    }
}
