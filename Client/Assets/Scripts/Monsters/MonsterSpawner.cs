using System.Collections.Generic;
using Game.Client.Combat;
using Game.Shared.Monsters;
using UnityEngine;

namespace Game.Client.Monsters
{
    /// <summary>Formato do corpo do monstro no protótipo (primitivas até existirem modelos).</summary>
    public enum MonsterShape
    {
        Capsule,
        Cube,
        Sphere,
    }

    /// <summary>
    /// Cria um grupo de monstros iguais numa área ao iniciar o jogo. Cada monstro renasce sozinho no
    /// próprio ponto depois de morrer (<see cref="MonsterData.RespawnSeconds"/>).
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
            var height = 2f * bodyScale.y;
            var bodyRadius = 0.5f * Mathf.Max(bodyScale.x, bodyScale.z);

            var root = new GameObject($"{data.Name} (Nv {data.Level})");
            root.transform.SetParent(transform, true);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

            var controller = root.AddComponent<CharacterController>();
            controller.height = height;
            controller.radius = bodyRadius;
            controller.center = new Vector3(0, height / 2f, 0);

            var body = new GameObject("Corpo");
            body.transform.SetParent(root.transform, false);
            var mesh = Primitive(PrimitiveTypeFor(shape), "Malha", body.transform, bodyMaterial);
            mesh.transform.localPosition = new Vector3(0, height / 2f, 0);
            mesh.transform.localScale = shape == MonsterShape.Capsule ? bodyScale : new Vector3(bodyScale.x, bodyScale.y * 2f, bodyScale.z);
            var eye = Primitive(PrimitiveType.Cube, "Frente", body.transform, telegraphMaterial);
            eye.transform.localPosition = new Vector3(0, height * 0.75f, bodyRadius);
            eye.transform.localScale = new Vector3(0.3f, 0.12f, 0.12f) * Mathf.Max(1f, bodyScale.x);

            var ring = Primitive(PrimitiveType.Cylinder, "Anel de seleção", root.transform, ringMaterial);
            ring.transform.localPosition = new Vector3(0, 0.02f, 0);
            ring.transform.localScale = new Vector3(bodyRadius * 3.2f, 0.01f, bodyRadius * 3.2f);

            var telegraph = Primitive(PrimitiveType.Cylinder, "Aviso de golpe", root.transform, telegraphMaterial);
            telegraph.transform.localPosition = new Vector3(0, 0.03f, 0);
            telegraph.SetActive(false);

            var targetable = root.AddComponent<Targetable>();
            targetable.Configure(data.Name, data.MaxHp, height + 0.3f, bodyRadius, ring, data.Defense);
            targetable.Level = data.Level;

            var monster = root.AddComponent<MonsterController>();
            monster.Initialize(data, body, telegraph.transform);
            if (isBoss) root.AddComponent<BossAbilities>().SetWarningMaterial(telegraphMaterial);
            return monster;
        }

        private static PrimitiveType PrimitiveTypeFor(MonsterShape monsterShape) => monsterShape switch
        {
            MonsterShape.Cube => PrimitiveType.Cube,
            MonsterShape.Sphere => PrimitiveType.Sphere,
            _ => PrimitiveType.Capsule,
        };

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            Destroy(primitive.GetComponent<Collider>());
            primitive.transform.SetParent(parent, false);
            if (material != null) primitive.GetComponent<Renderer>().sharedMaterial = material;
            return primitive;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
