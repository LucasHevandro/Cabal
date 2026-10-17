using System.Collections.Generic;
using Game.Client.Net;
using Game.Shared.Dungeon;
using Game.Shared.Net;
using UnityEngine;

namespace Game.Client.World
{
    /// <summary>
    /// Modo online: monta o mapa da dungeon (chão, paredes, portões e baú) na origem da instância quando o servidor
    /// manda o <see cref="DungeonState"/>, e desmonta ao sair. O estado chega antes da posição, então o chão já existe
    /// quando o personagem é levado para lá. Portões e baú acompanham o andamento.
    /// </summary>
    [RequireComponent(typeof(NetworkSession))]
    public sealed class DungeonView : MonoBehaviour
    {
        private const float WallHeight = 7f;

        private NetworkSession _session;
        private GameObject _root;
        private string _builtFor = "";
        private readonly List<GameObject> _gates = new List<GameObject>();
        private GameObject _chest;

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
            if (message is not DungeonState state) return;
            CharacterState.Apply(state);
            if (!state.Active)
            {
                Clear();
                return;
            }
            var definition = DungeonCatalog.Find(state.DungeonId);
            if (definition == null) return;
            var key = $"{state.DungeonId}@{state.OriginX},{state.OriginZ}";
            if (key != _builtFor) Build(definition, state);
            for (var i = 0; i < _gates.Count; i++) _gates[i].SetActive(i >= state.Room);
            if (_chest != null) _chest.SetActive(state.Completed);
        }

        private void Clear()
        {
            if (_root != null) Destroy(_root);
            _root = null;
            _builtFor = "";
            _gates.Clear();
            _chest = null;
        }

        private void Build(DungeonDefinition definition, DungeonState state)
        {
            Clear();
            _builtFor = $"{state.DungeonId}@{state.OriginX},{state.OriginZ}";
            _root = new GameObject($"Dungeon: {definition.Name}");
            _root.transform.position = new Vector3(state.OriginX, 0, state.OriginZ);

            var floor = _session.Material("dungeon_chao", new Color(0.22f, 0.21f, 0.24f));
            var wall = _session.Material("dungeon_parede", new Color(0.3f, 0.28f, 0.32f));
            var gate = _session.Material("dungeon_portao", new Color(0.45f, 0.2f, 0.15f));
            var boss = _session.Material("dungeon_trono", new Color(0.28f, 0.12f, 0.14f));
            var length = definition.Length;
            var width = definition.HalfWidth * 2;

            // Chão do corredor inteiro (um pouco antes da entrada) e o piso do trono com outra cor.
            Block("Chão", new Vector3(0, -0.5f, length / 2 - 2), new Vector3(width, 1, length + 4), floor);
            var bossRoom = definition.Rooms[definition.Rooms.Count - 1];
            Block("Piso do trono", new Vector3(0, 0.01f, (bossRoom.StartZ + bossRoom.EndZ) / 2), new Vector3(width - 1, 0.02f, bossRoom.EndZ - bossRoom.StartZ), boss, collider: false);

            // Paredes laterais, do fundo e da entrada.
            Block("Parede esquerda", new Vector3(-definition.HalfWidth - 0.5f, WallHeight / 2, length / 2 - 2), new Vector3(1, WallHeight, length + 4), wall);
            Block("Parede direita", new Vector3(definition.HalfWidth + 0.5f, WallHeight / 2, length / 2 - 2), new Vector3(1, WallHeight, length + 4), wall);
            Block("Parede do fundo", new Vector3(0, WallHeight / 2, length + 0.5f), new Vector3(width + 2, WallHeight, 1), wall);
            Block("Parede da entrada", new Vector3(0, WallHeight / 2, -4.5f), new Vector3(width + 2, WallHeight, 1), wall);

            // Pilares entre as salas e um portão em cada passagem.
            foreach (var room in definition.Rooms)
            {
                if (room.IsBossRoom) continue;
                Block("Pilar", new Vector3(-definition.HalfWidth + 2, WallHeight / 2, room.GateZ), new Vector3(4, WallHeight, 1.2f), wall);
                Block("Pilar", new Vector3(definition.HalfWidth - 2, WallHeight / 2, room.GateZ), new Vector3(4, WallHeight, 1.2f), wall);
                _gates.Add(Block($"Portão: {room.Name}", new Vector3(0, 2, room.GateZ), new Vector3(width - 8, 4, 0.6f), gate));
            }

            // Tochas (luzes pontuais) ao longo do corredor, para a cripta não ficar chapada.
            for (var z = 6f; z < length; z += 18f)
            {
                foreach (var side in new[] { -1f, 1f })
                {
                    var torch = new GameObject("Tocha");
                    torch.transform.SetParent(_root.transform, false);
                    torch.transform.localPosition = new Vector3(side * (definition.HalfWidth - 0.8f), 3.2f, z);
                    var light = torch.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = new Color(1f, 0.6f, 0.3f);
                    light.range = 14;
                    light.intensity = 2.2f;
                }
            }

            // Baú (aparece quando o chefe cai).
            _chest = Block("Baú", new Vector3(0, 0.5f, definition.ChestZ), new Vector3(1.6f, 1f, 1f),
                _session.Material("dungeon_bau", new Color(0.75f, 0.55f, 0.15f)), collider: false);
            _chest.SetActive(state.Completed);
        }

        private GameObject Block(string name, Vector3 localPosition, Vector3 size, Material material, bool collider = true)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(_root.transform, false);
            block.transform.localPosition = localPosition;
            block.transform.localScale = size;
            if (material != null) block.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Destroy(block.GetComponent<Collider>());
            return block;
        }
    }
}
