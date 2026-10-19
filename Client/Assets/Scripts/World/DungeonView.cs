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

            // Chão do corredor inteiro (um pouco antes da entrada): lajotas de pedra.
            var ground = Block("Chão", new Vector3(0, -0.5f, length / 2 - 2), new Vector3(width, 1, length + 4), floor);
            Kit.Floor(ground.GetComponent<Renderer>(), "piso_cripta", new Vector2(width, length + 4), 4f);
            var bossRoom = definition.Rooms[definition.Rooms.Count - 1];
            var bossMiddle = (bossRoom.StartZ + bossRoom.EndZ) / 2;
            var carpet = Block("Piso do trono", new Vector3(0, 0.01f, bossMiddle), new Vector3(3.2f, 0.02f, bossRoom.EndZ - bossRoom.StartZ - 4f), boss, collider: false);
            Kit.Floor(carpet.GetComponent<Renderer>(), "tapete", new Vector2(1f, bossRoom.EndZ - bossRoom.StartZ - 4f), 4f);

            // Paredes: o bloco fica só como colisão; o visual são muros de tijolo de 4 m em sequência.
            var walls = new[]
            {
                Block("Parede esquerda", new Vector3(-definition.HalfWidth - 0.5f, WallHeight / 2, length / 2 - 2), new Vector3(1, WallHeight, length + 4), wall),
                Block("Parede direita", new Vector3(definition.HalfWidth + 0.5f, WallHeight / 2, length / 2 - 2), new Vector3(1, WallHeight, length + 4), wall),
                Block("Parede do fundo", new Vector3(0, WallHeight / 2, length + 0.5f), new Vector3(width + 2, WallHeight, 1), wall),
                Block("Parede da entrada", new Vector3(0, WallHeight / 2, -4.5f), new Vector3(width + 2, WallHeight, 1), wall),
            };
            var hasKit = Kit.Place(_root.transform, "parede", new Vector3(-definition.HalfWidth - 0.3f, 0, -2f), Quaternion.LookRotation(Vector3.left)) != null;
            if (hasKit)
            {
                foreach (var block in walls) Kit.HideMesh(block);
                for (var z = 2f; z < length + 2f; z += 4f)
                {
                    Kit.Place(_root.transform, "parede", new Vector3(-definition.HalfWidth - 0.3f, 0, z), Quaternion.LookRotation(Vector3.left));
                    Kit.Place(_root.transform, "parede", new Vector3(definition.HalfWidth + 0.3f, 0, z - 4f), Quaternion.LookRotation(Vector3.right));
                }
                Kit.Place(_root.transform, "parede", new Vector3(definition.HalfWidth + 0.3f, 0, length - 2f + 4f), Quaternion.LookRotation(Vector3.right));
                for (var x = -definition.HalfWidth + 2f; x < definition.HalfWidth; x += 4f)
                {
                    Kit.Place(_root.transform, "parede", new Vector3(x, 0, length + 0.3f), Quaternion.identity);
                    Kit.Place(_root.transform, "parede", new Vector3(x, 0, -4.3f), Quaternion.LookRotation(Vector3.back));
                }
            }

            // Pilares entre as salas e uma grade de ferro em cada passagem.
            foreach (var room in definition.Rooms)
            {
                if (room.IsBossRoom) continue;
                foreach (var side in new[] { -1f, 1f })
                {
                    var pillar = Block("Pilar", new Vector3(side * (definition.HalfWidth - 2), WallHeight / 2, room.GateZ), new Vector3(4, WallHeight, 1.2f), wall);
                    if (!hasKit) continue;
                    Kit.HideMesh(pillar);
                    Kit.Place(_root.transform, "pilar", new Vector3(side * (definition.HalfWidth - 0.9f), 0, room.GateZ), Quaternion.identity);
                    Kit.Place(_root.transform, "pilar", new Vector3(side * (definition.HalfWidth - 3.1f), 0, room.GateZ), Quaternion.identity);
                }
                var gateWidth = width - 8;
                var gateObject = Block($"Portão: {room.Name}", new Vector3(0, 2, room.GateZ), new Vector3(gateWidth, 4, 0.6f), gate);
                if (hasKit)
                {
                    Kit.HideMesh(gateObject);
                    // As barras ficam como filhas do bloco (que é esticado): desfaz a escala para não deformar.
                    for (var x = -gateWidth / 2f + 0.5f; x < gateWidth / 2f; x += 1f)
                    {
                        var bars = Kit.Place(gateObject.transform, "portao", Vector3.zero, Quaternion.identity);
                        if (bars == null) continue;
                        bars.transform.localPosition = new Vector3(x / gateWidth, -0.5f, 0);
                        bars.transform.localScale = new Vector3(1f / gateWidth, 1f / 4f, 1f / 0.6f);
                    }
                }
                _gates.Add(gateObject);
            }

            if (hasKit)
            {
                // Teto de pedra: o céu não aparece mais por cima. Não faz sombra (as tochas e o ambiente iluminam).
                var ceiling = Block("Teto", new Vector3(0, WallHeight + 0.5f, length / 2 - 2), new Vector3(width + 2, 1, length + 6), floor, collider: false);
                var ceilingRenderer = ceiling.GetComponent<Renderer>();
                Kit.Floor(ceilingRenderer, "piso_cripta", new Vector2(width + 2, length + 6), 6f);
                ceilingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Decorate(definition, bossRoom, length);
            }
            else
            {
                // Sem as peças: as luzes de tocha de antes.
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
            }

            // Baú (aparece quando o chefe cai).
            _chest = Block("Baú", new Vector3(0, 0.5f, definition.ChestZ), new Vector3(1.6f, 1f, 1f),
                _session.Material("dungeon_bau", new Color(0.75f, 0.55f, 0.15f)), collider: false);
            if (hasKit)
            {
                Kit.HideMesh(_chest);
                var chest = Kit.Place(_chest.transform, "bau", Vector3.zero, Quaternion.Euler(0, 180, 0));
                if (chest != null)
                {
                    chest.transform.localPosition = new Vector3(0, -0.5f, 0);
                    chest.transform.localScale = new Vector3(1f / 1.6f, 1f, 1f);
                }
            }
            _chest.SetActive(state.Completed);
        }

        /// <summary>Tochas, caixões, ossos, velas e estandartes junto às paredes; trono e braseiros na sala do chefe.</summary>
        private void Decorate(DungeonDefinition definition, DungeonRoom bossRoom, float length)
        {
            var edge = definition.HalfWidth;
            var random = new System.Random(definition.Id.GetHashCode());
            float Range(float min, float max) => (float)(min + random.NextDouble() * (max - min));
            for (var z = 6f; z < length; z += 18f)
            {
                Kit.Place(_root.transform, "tocha", new Vector3(-edge + 0.05f, 3.2f, z), Quaternion.LookRotation(Vector3.right));
                Kit.Place(_root.transform, "tocha", new Vector3(edge - 0.05f, 3.2f, z + 9f), Quaternion.LookRotation(Vector3.left));
            }
            for (var z = 12f; z < length - 6f; z += 24f)
            {
                Kit.Place(_root.transform, "estandarte", new Vector3(-edge + 0.08f, 0, z), Quaternion.LookRotation(Vector3.right));
                Kit.Place(_root.transform, "estandarte", new Vector3(edge - 0.08f, 0, z), Quaternion.LookRotation(Vector3.left));
            }
            foreach (var room in definition.Rooms)
            {
                if (room.IsBossRoom) continue;
                for (var i = 0; i < 3; i++)
                {
                    var side = i % 2 == 0 ? -1f : 1f;
                    Kit.Place(_root.transform, "caixao", new Vector3(side * (edge - 1.1f), 0, Range(room.StartZ + 4, room.EndZ - 4)), Quaternion.Euler(0, Range(-8, 8), 0));
                }
                for (var i = 0; i < 5; i++)
                {
                    var side = random.Next(2) == 0 ? -1f : 1f;
                    Kit.Place(_root.transform, "ossos", new Vector3(side * Range(edge - 3f, edge - 0.6f), 0, Range(room.StartZ + 2, room.EndZ - 2)), Quaternion.Euler(0, Range(0, 360), 0));
                }
                for (var i = 0; i < 3; i++)
                {
                    var side = random.Next(2) == 0 ? -1f : 1f;
                    Kit.Place(_root.transform, "velas", new Vector3(side * (edge - 0.5f), 0, Range(room.StartZ + 2, room.EndZ - 2)), Quaternion.identity);
                }
            }
            // Sala do chefe: trono no fundo e braseiros nos cantos.
            Kit.Place(_root.transform, "trono", new Vector3(0, 0, bossRoom.EndZ - 1.8f), Quaternion.identity);
            foreach (var x in new[] { -1f, 1f })
            foreach (var z in new[] { bossRoom.StartZ + 5f, bossRoom.EndZ - 6f })
                Kit.Place(_root.transform, "braseiro_arena", new Vector3(x * (edge - 2f), 0, z), Quaternion.identity);
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
