using System.Collections.Generic;
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

    /// <summary>Partes visuais de um monstro montado por <see cref="MonsterVisuals.Build"/>.</summary>
    public readonly struct MonsterParts
    {
        public GameObject Body { get; }
        public GameObject Ring { get; }
        public Transform Telegraph { get; }
        public float Height { get; }
        public float Radius { get; }

        public MonsterParts(GameObject body, GameObject ring, Transform telegraph, float height, float radius)
        {
            Body = body;
            Ring = ring;
            Telegraph = telegraph;
            Height = height;
            Radius = radius;
        }
    }

    /// <summary>
    /// Aparência dos monstros (os dados de jogo ficam no Shared) e montagem das primitivas.
    /// Usado pelo spawner offline e pela visão online das entidades do servidor.
    /// </summary>
    public static class MonsterVisuals
    {
        public static readonly Dictionary<string, (MonsterShape Shape, Vector3 Scale, Color Color)> Looks =
            new Dictionary<string, (MonsterShape, Vector3, Color)>
            {
                ["coelho_saltador"] = (MonsterShape.Sphere, new Vector3(0.7f, 0.5f, 0.7f), new Color(0.95f, 0.95f, 0.9f)),
                ["lobo_cinzento"] = (MonsterShape.Capsule, new Vector3(0.8f, 0.7f, 1.3f), new Color(0.5f, 0.5f, 0.55f)),
                ["javali_furioso"] = (MonsterShape.Cube, new Vector3(1.1f, 0.6f, 1.6f), new Color(0.45f, 0.3f, 0.18f)),
                ["aranha_da_mata"] = (MonsterShape.Sphere, new Vector3(1.6f, 0.6f, 1.6f), new Color(0.2f, 0.3f, 0.12f)),
                ["golem_de_pedra"] = (MonsterShape.Cube, new Vector3(1.6f, 1.3f, 1.6f), new Color(0.55f, 0.55f, 0.58f)),
                ["espectro"] = (MonsterShape.Capsule, new Vector3(0.9f, 1.1f, 0.9f), new Color(0.45f, 0.3f, 0.7f)),
                ["cavaleiro_caido"] = (MonsterShape.Capsule, new Vector3(1.1f, 1.25f, 1.1f), new Color(0.3f, 0.32f, 0.4f)),
                ["guardiao_das_ruinas"] = (MonsterShape.Capsule, new Vector3(2.2f, 2.2f, 2.2f), new Color(0.6f, 0.15f, 0.12f)),
                // Cripta Esquecida
                ["esqueleto_guerreiro"] = (MonsterShape.Capsule, new Vector3(0.8f, 1.05f, 0.8f), new Color(0.85f, 0.82f, 0.72f)),
                ["esqueleto_arqueiro"] = (MonsterShape.Capsule, new Vector3(0.7f, 1f, 0.7f), new Color(0.72f, 0.75f, 0.62f)),
                ["cavaleiro_ossudo"] = (MonsterShape.Cube, new Vector3(1.2f, 1.6f, 1f), new Color(0.55f, 0.52f, 0.48f)),
                ["servo_da_cripta"] = (MonsterShape.Sphere, new Vector3(0.8f, 0.8f, 0.8f), new Color(0.4f, 0.75f, 0.95f)),
                ["senhor_da_cripta"] = (MonsterShape.Capsule, new Vector3(2.3f, 2.4f, 2.3f), new Color(0.35f, 0.2f, 0.45f)),
            };

        /// <summary>Aparência do tipo, ou uma cápsula cinza se o tipo for desconhecido (servidor mais novo).</summary>
        public static (MonsterShape Shape, Vector3 Scale, Color Color) LookFor(string typeId) =>
            Looks.TryGetValue(typeId ?? "", out var look) ? look : (MonsterShape.Capsule, Vector3.one, Color.gray);

        /// <summary>Monta corpo, "olho" (frente), anel de seleção e aviso de golpe como filhos de <paramref name="root"/>.</summary>
        public static MonsterParts Build(Transform root, MonsterShape shape, Vector3 scale, Material body, Material telegraph, Material ring)
        {
            var height = 2f * scale.y;
            var radius = 0.5f * Mathf.Max(scale.x, scale.z);

            var bodyRoot = new GameObject("Corpo");
            bodyRoot.transform.SetParent(root, false);
            var mesh = Primitive(TypeFor(shape), "Malha", bodyRoot.transform, body);
            mesh.transform.localPosition = new Vector3(0, height / 2f, 0);
            mesh.transform.localScale = shape == MonsterShape.Capsule ? scale : new Vector3(scale.x, scale.y * 2f, scale.z);
            var eye = Primitive(PrimitiveType.Cube, "Frente", bodyRoot.transform, telegraph);
            eye.transform.localPosition = new Vector3(0, height * 0.75f, radius);
            eye.transform.localScale = new Vector3(0.3f, 0.12f, 0.12f) * Mathf.Max(1f, scale.x);

            var selection = Primitive(PrimitiveType.Cylinder, "Anel de seleção", root, ring);
            selection.transform.localPosition = new Vector3(0, 0.02f, 0);
            selection.transform.localScale = new Vector3(radius * 3.2f, 0.01f, radius * 3.2f);

            var warning = Primitive(PrimitiveType.Cylinder, "Aviso de golpe", root, telegraph);
            warning.transform.localPosition = new Vector3(0, 0.03f, 0);
            warning.SetActive(false);

            return new MonsterParts(bodyRoot, selection, warning.transform, height, radius);
        }

        /// <summary>Material novo copiado de <paramref name="template"/> com outra cor (sem precisar de Shader.Find no build).</summary>
        public static Material Tinted(Material template, Color color)
        {
            var material = new Material(template);
            material.SetColor("_BaseColor", color);
            return material;
        }

        private static PrimitiveType TypeFor(MonsterShape shape) => shape switch
        {
            MonsterShape.Cube => PrimitiveType.Cube,
            MonsterShape.Sphere => PrimitiveType.Sphere,
            _ => PrimitiveType.Capsule,
        };

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            Object.Destroy(primitive.GetComponent<Collider>());
            primitive.transform.SetParent(parent, false);
            if (material != null) primitive.GetComponent<Renderer>().sharedMaterial = material;
            return primitive;
        }
    }
}
