using System.Collections.Generic;
using Game.Client.Combat;
using Game.Client.Net;
using Game.Shared.Monsters;
using Game.Shared.Text;
using UnityEngine;

namespace Game.Client.Monsters
{
    /// <summary>
    /// Modo offline: cria um grupo de monstros iguais numa área ao iniciar o jogo. Cada monstro renasce sozinho
    /// no próprio ponto depois de morrer (<see cref="MonsterData.RespawnSeconds"/>).
    /// No modo online não faz nada: os monstros vêm do servidor.
    /// </summary>
    public sealed class MonsterSpawner : MonoBehaviour
    {
        [SerializeField] private MonsterData data = new MonsterData();
        [SerializeField] private int count = 4;
        [SerializeField] private float radius = 6f;

        [Header("Visual")]
        [SerializeField] private MonsterShape shape = MonsterShape.Capsule;
        [SerializeField] private Vector3 bodyScale = Vector3.one;
        [SerializeField] private Material bodyMaterial;
        [SerializeField] private Material telegraphMaterial;
        [SerializeField] private Material ringMaterial;

        [Header("Chefe")]
        [Tooltip("Adiciona as habilidades de chefe (fase 2 e ataque em área).")]
        [SerializeField] private bool isBoss;

        private readonly List<MonsterController> _monsters = new List<MonsterController>();

        public IReadOnlyList<MonsterController> Monsters => _monsters;
        public MonsterData Data => data;

        public void Configure(MonsterData monsterData, int monsterCount, float areaRadius, MonsterShape bodyShape,
            Vector3 scale, Material body, Material telegraph, Material ring, bool boss = false)
        {
            isBoss = boss;
            data = monsterData;
            count = monsterCount;
            radius = areaRadius;
            shape = bodyShape;
            bodyScale = scale;
            bodyMaterial = body;
            telegraphMaterial = telegraph;
            ringMaterial = ring;
        }

        private void Start()
        {
            if (GameMode.IsOnline) return;

            data.Validate();
            for (var i = 0; i < count; i++)
            {
                // Distribui em volta do centro, com um pouco de variação, para não nascerem empilhados.
                var angle = (i / (float)Mathf.Max(1, count)) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                var distance = count == 1 ? 0f : radius * Random.Range(0.4f, 1f);
                var position = transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
                _monsters.Add(Spawn(position));
            }
        }

        private MonsterController Spawn(Vector3 position)
        {
            var root = new GameObject(Loc.T("ui.name_level", data.Name, data.Level));
            root.transform.SetParent(transform, true);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

            var parts = MonsterVisuals.Build(root.transform, shape, bodyScale, bodyMaterial, telegraphMaterial, ringMaterial,
                Fx.VisualCatalog.Monster, data.Id);

            var controller = root.AddComponent<CharacterController>();
            controller.height = parts.Height;
            controller.radius = parts.Radius;
            controller.center = new Vector3(0, parts.Height / 2f, 0);

            var targetable = root.AddComponent<Targetable>();
            targetable.Configure(data.Name, data.MaxHp, parts.Height + 0.3f, parts.Radius, parts.Ring, data.Defense);
            targetable.Level = data.Level;

            var monster = root.AddComponent<MonsterController>();
            monster.Initialize(data, parts.Body, parts.Telegraph);
            if (isBoss) root.AddComponent<BossAbilities>().SetWarningMaterial(telegraphMaterial);
            return monster;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
