using System.Collections.Generic;
using Game.Client.Net;
using Game.Shared.Net;
using Game.Shared.Pvp;
using UnityEngine;

namespace Game.Client.World
{
    /// <summary>
    /// Modo online: monta o campo da arena (chão, muros, bases e os três pontos) na origem da partida quando o servidor
    /// manda o <see cref="PvpState"/>, pinta cada ponto com a cor do dono e marca quem pode ser alvo: inimigos na arena
    /// e o adversário do duelo depois da contagem.
    /// </summary>
    [RequireComponent(typeof(NetworkSession))]
    public sealed class ArenaView : MonoBehaviour
    {
        public static readonly Color Blue = new Color(0.25f, 0.5f, 1f);
        public static readonly Color Red = new Color(1f, 0.3f, 0.25f);
        private static readonly Color Neutral = new Color(0.75f, 0.75f, 0.7f);

        private NetworkSession _session;
        private GameObject _root;
        private string _builtFor = "";
        private readonly List<Renderer> _points = new List<Renderer>();
        private readonly List<Transform> _fills = new List<Transform>();
        private readonly Dictionary<string, Material> _tints = new Dictionary<string, Material>();

        private void Start()
        {
            if (!GameMode.IsOnline)
            {
                enabled = false;
                return;
            }
            _session = GetComponent<NetworkSession>();
            _session.MessageReceived += OnMessage;
        }

        private void OnDestroy()
        {
            if (_session != null) _session.MessageReceived -= OnMessage;
        }

        private void OnMessage(INetMessage message)
        {
            if (message is not PvpState state) return;
            CharacterState.Apply(state);
            if (state.Mode != PvpMode.Arena)
            {
                Clear();
                return;
            }
            var key = $"{state.OriginX},{state.OriginZ}";
            if (key != _builtFor) Build(state);
            for (var i = 0; i < _points.Count && i < state.Points.Count; i++)
            {
                var point = state.Points[i];
                _points[i].sharedMaterial = Tint(point.Owner == (sbyte)ArenaTeam.Blue ? Blue : point.Owner == (sbyte)ArenaTeam.Red ? Red : Neutral);
                // O disco de dentro cresce com o progresso da captura, na cor de quem está tomando.
                var amount = Mathf.Abs(point.Progress);
                _fills[i].localScale = new Vector3(amount * ArenaRules.CaptureRadius * 2, 0.02f, amount * ArenaRules.CaptureRadius * 2);
                _fills[i].GetComponent<Renderer>().sharedMaterial = Tint(point.Progress < 0 ? Blue : Red);
            }
        }

        private void Update()
        {
            // Quem pode ser alvo muda com o estado: inimigo na arena ou adversário do duelo (depois da contagem).
            var pvp = CharacterState.Pvp;
            foreach (var view in _session.Views.Values)
            {
                if (view.Kind != EntityKind.Player || view.Target == null) continue;
                view.Target.IsSelectable = IsHostile(pvp, view.Id);
            }
        }

        public bool IsHostile(PvpState pvp, int playerId)
        {
            if (pvp == null) return false;
            var started = _session.ServerNow >= pvp.StartsAt;
            if (pvp.Mode == PvpMode.Duel) return started && pvp.OpponentId == playerId;
            if (pvp.Mode != PvpMode.Arena || pvp.Finished || !started) return false;
            foreach (var p in pvp.Players)
                if (p.Id == playerId) return p.Team != pvp.Team;
            return false;
        }

        private void Clear()
        {
            if (_root != null) Destroy(_root);
            _root = null;
            _builtFor = "";
            _points.Clear();
            _fills.Clear();
        }

        private void Build(PvpState state)
        {
            Clear();
            _builtFor = $"{state.OriginX},{state.OriginZ}";
            _root = new GameObject("Arena");
            _root.transform.position = Ground.Point(state.OriginX, state.OriginZ);
            var width = ArenaRules.HalfWidth * 2;
            var length = ArenaRules.HalfLength * 2;
            var wall = _session.Material("arena_muro", new Color(0.42f, 0.38f, 0.34f));

            Block("Chão", new Vector3(0, -0.5f, 0), new Vector3(width, 1, length), _session.Material("arena_chao", new Color(0.5f, 0.46f, 0.38f)));
            Block("Muro oeste", new Vector3(-ArenaRules.HalfWidth - 0.5f, 1.5f, 0), new Vector3(1, 3, length + 2), wall);
            Block("Muro leste", new Vector3(ArenaRules.HalfWidth + 0.5f, 1.5f, 0), new Vector3(1, 3, length + 2), wall);
            Block("Muro sul", new Vector3(0, 1.5f, -ArenaRules.HalfLength - 0.5f), new Vector3(width + 2, 3, 1), wall);
            Block("Muro norte", new Vector3(0, 1.5f, ArenaRules.HalfLength + 0.5f), new Vector3(width + 2, 3, 1), wall);

            foreach (var team in new[] { ArenaTeam.Blue, ArenaTeam.Red })
            {
                var (x, z) = ArenaRules.BaseOf(team);
                Block($"Base {ArenaRules.TeamName(team)}", new Vector3(x, 0.02f, z), new Vector3(14, 0.04f, 6),
                    Tint(team == ArenaTeam.Blue ? Blue : Red), collider: false);
            }
            // Obstáculos para quebrar a linha reta entre as bases.
            foreach (var (x, z) in new[] { (-10f, -14f), (10f, -14f), (-10f, 14f), (10f, 14f) })
                Block("Pilar", new Vector3(x, 1.5f, z), new Vector3(3, 3, 3), wall);

            foreach (var point in ArenaRules.Points)
            {
                var disc = Disc($"Ponto {point.Name}", new Vector3(point.X, 0.03f, point.Z), ArenaRules.CaptureRadius * 2, Tint(Neutral));
                _points.Add(disc.GetComponent<Renderer>());
                var fill = Disc($"Captura {point.Name}", new Vector3(point.X, 0.05f, point.Z), 0.01f, Tint(Blue));
                _fills.Add(fill.transform);
                Block($"Bandeira {point.Name}", new Vector3(point.X, 2f, point.Z), new Vector3(0.25f, 4f, 0.25f), wall, collider: false);
            }
        }

        private Material Tint(Color color)
        {
            var key = ColorUtility.ToHtmlStringRGB(color);
            if (!_tints.TryGetValue(key, out var material)) _tints[key] = material = _session.Material("arena_" + key, color);
            return material;
        }

        private GameObject Disc(string name, Vector3 position, float diameter, Material material)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = name;
            Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(_root.transform, false);
            disc.transform.localPosition = position;
            disc.transform.localScale = new Vector3(diameter, 0.02f, diameter);
            if (material != null) disc.GetComponent<Renderer>().sharedMaterial = material;
            return disc;
        }

        private GameObject Block(string name, Vector3 position, Vector3 size, Material material, bool collider = true)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(_root.transform, false);
            block.transform.localPosition = position;
            block.transform.localScale = size;
            if (material != null) block.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Destroy(block.GetComponent<Collider>());
            return block;
        }
    }
}
