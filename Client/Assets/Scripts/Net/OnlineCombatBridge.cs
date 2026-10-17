using Game.Client.Combat;
using Game.Client.Fx;
using Game.Client.Loot;
using Game.Shared.Net;
using UnityEngine;

namespace Game.Client.Net
{
    /// <summary>
    /// Modo online: entrega as mensagens de combate do servidor a quem mostra cada coisa —
    /// status e combo ao <see cref="PlayerCombat"/>, itens ao <see cref="PlayerLoot"/>, avisos de área no chão.
    /// </summary>
    [RequireComponent(typeof(NetworkSession))]
    public sealed class OnlineCombatBridge : MonoBehaviour
    {
        [SerializeField] private Material warningMaterial;

        private NetworkSession _session;
        private PlayerCombat _combat;
        private PlayerLoot _loot;

        public void SetWarningMaterial(Material material) => warningMaterial = material;

        private void Start()
        {
            if (!GameMode.IsOnline)
            {
                enabled = false;
                return;
            }
            _session = GetComponent<NetworkSession>();
            _combat = PlayerCombat.Local;
            if (_combat != null) _loot = _combat.GetComponent<PlayerLoot>();
            _session.MessageReceived += OnMessage;
            // Janelas do jogo online (inventário, NPCs, ferreira, missões, atributos).
            if (GetComponent<UI.GameWindows>() == null) gameObject.AddComponent<UI.GameWindows>();
            // Chat e grupo.
            if (GetComponent<UI.SocialWindows>() == null) gameObject.AddComponent<UI.SocialWindows>();
            // Mapa da dungeon (montado quando o servidor manda entrar).
            if (GetComponent<World.DungeonView>() == null) gameObject.AddComponent<World.DungeonView>();
        }

        private void OnDestroy()
        {
            if (_session != null) _session.MessageReceived -= OnMessage;
        }

        private void OnMessage(INetMessage message)
        {
            if (_combat == null) return;
            switch (message)
            {
                case PlayerStats stats:
                    _combat.ApplyServerStats(stats);
                    if (_loot != null) _loot.SetServerAlz(stats.Alz);
                    break;
                case ComboState combo:
                    _combat.ReconcileCombo(combo);
                    break;
                case CombatEvent hit:
                    OnCombatEvent(hit);
                    break;
                case LootSpawned spawned:
                    if (_loot != null) _loot.OnServerLootSpawned(spawned);
                    break;
                case LootRemoved removed:
                    if (_loot != null) _loot.OnServerLootRemoved(removed, _session.PlayerId);
                    break;
                case Inventory inventory:
                    if (_loot != null) _loot.SetServerInventory(inventory);
                    CharacterState.Apply(inventory);
                    break;
                case CharacterSheet sheet:
                    CharacterState.Apply(sheet);
                    _combat.ApplyCharacterSheet(sheet);
                    break;
                case QuestState quests:
                    CharacterState.Apply(quests);
                    break;
                case NpcDialog dialog:
                    CharacterState.Apply(dialog);
                    break;
                case UpgradeResult upgrade:
                    CharacterState.Apply(upgrade);
                    SoundPlayer.Play(upgrade.Outcome == 1 ? SoundId.Excellent : upgrade.Outcome == 0 ? SoundId.Error : SoundId.Miss, 0.8f, 0f);
                    break;
                case DungeonInfo dungeonInfo:
                    CharacterState.Apply(dungeonInfo);
                    break;
                case TradeState trade:
                    CharacterState.Apply(trade);
                    break;
                case TradeInvite tradeInvite:
                    CharacterState.Apply(tradeInvite);
                    break;
                case AreaWarning area:
                    ShowAreaWarning(area);
                    break;
            }
        }

        private void OnCombatEvent(CombatEvent hit)
        {
            if (hit.TargetId == _session.PlayerId)
            {
                _combat.ShowIncomingHit(hit);
                return;
            }
            var view = _session.FindView(hit.TargetId);
            if (view != null && view.Kind == EntityKind.Monster)
                _combat.ShowServerHit(hit, view.Target, hit.AttackerId == _session.PlayerId);
        }

        private void ShowAreaWarning(AreaWarning area)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Aviso de área";
            Destroy(disc.GetComponent<Collider>());
            disc.transform.position = new Vector3(area.X, 0.04f, area.Z);
            if (warningMaterial != null) disc.GetComponent<Renderer>().sharedMaterial = warningMaterial;
            disc.AddComponent<AreaWarningView>().Initialize(area.Radius, area.Seconds);
        }

        /// <summary>Círculo que enche até o tamanho total quando o golpe do servidor vai cair.</summary>
        private sealed class AreaWarningView : MonoBehaviour
        {
            private float _radius;
            private float _seconds;
            private float _startedAt;

            public void Initialize(float radius, float seconds)
            {
                _radius = radius;
                _seconds = Mathf.Max(0.1f, seconds);
                _startedAt = Time.time;
                Update();
            }

            private void Update()
            {
                var progress = Mathf.Clamp01((Time.time - _startedAt) / _seconds);
                var size = _radius * 2f * Mathf.Lerp(0.15f, 1f, progress);
                transform.localScale = new Vector3(size, 0.01f, size);
                if (progress < 1f) return;
                if (CombatFx.Instance != null) CombatFx.Instance.Sparks(transform.position + Vector3.up * 0.3f, new Color(1f, 0.3f, 0.2f), 50);
                SoundPlayer.Play(SoundId.HitHeavy, 1f, 0f);
                Destroy(gameObject);
            }
        }
    }
}
