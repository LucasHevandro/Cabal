using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.World
{
    /// <summary>
    /// Estrelas e lua da noite (<c>Shaders/CeuNoturno.shader</c>): uma cúpula de estrelas que gira bem devagar e a lua na
    /// direção da luz da noite (o <see cref="Atmosphere"/> usa o mesmo sol como lua). Acompanham a câmera, acendem com
    /// <see cref="Atmosphere.Night"/> e somem de dia. Na dungeon (fechada) não aparecem.
    /// </summary>
    public sealed class NightSky : MonoBehaviour
    {
        private const float Radius = 450f;
        private const int StarCount = 1800;
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        private Transform _stars;
        private Transform _moon;
        private Material _starMaterial;
        private Material _moonMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<NightSky>() != null) return;
            new GameObject("Céu noturno").AddComponent<NightSky>();
        }

        private void Start()
        {
            var template = Resources.Load<Material>("Visuals/fx/ceu_noturno");
            if (template == null)
            {
                enabled = false;
                return;
            }
            _starMaterial = new Material(template);
            _moonMaterial = new Material(template);
            _stars = Part("Estrelas", StarMesh(), _starMaterial);
            _moon = Part("Lua", MoonMesh(), _moonMaterial);
        }

        private Transform Part(string name, Mesh mesh, Material material)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return obj.transform;
        }

        private void LateUpdate()
        {
            var camera = Camera.main;
            if (camera == null) return;
            var eye = camera.transform.position;
            var night = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.85f, Atmosphere.Night));
            var inside = eye.z > 500f; // dungeon: teto fechado
            var alpha = inside ? 0f : night;
            _stars.gameObject.SetActive(alpha > 0.01f);
            _moon.gameObject.SetActive(alpha > 0.01f);
            if (alpha <= 0.01f) return;

            _starMaterial.SetFloat(AlphaId, alpha);
            _moonMaterial.SetFloat(AlphaId, alpha);
            _stars.SetPositionAndRotation(eye, Quaternion.Euler(0f, Time.time * 0.25f, 0f));

            // A luz da noite vem da lua: ela fica na direção de onde a luz chega.
            var sun = RenderSettings.sun;
            var toMoon = sun != null ? -sun.transform.forward : new Vector3(0.3f, 0.6f, 0.75f);
            if (toMoon.y < 0.15f) toMoon = new Vector3(toMoon.x, 0.15f, toMoon.z);
            toMoon.Normalize();
            _moon.SetPositionAndRotation(eye + toMoon * Radius * 0.92f, Quaternion.LookRotation(-toMoon));
        }

        /// <summary>Quadradinhos virados para o centro, espalhados por igual na metade de cima da esfera.</summary>
        private static Mesh StarMesh()
        {
            var random = new System.Random(7);
            var vertices = new List<Vector3>(StarCount * 4);
            var colors = new List<Color>(StarCount * 4);
            var uvs = new List<Vector2>(StarCount * 4);
            var twinkles = new List<Vector2>(StarCount * 4);
            var triangles = new List<int>(StarCount * 6);
            for (var i = 0; i < StarCount; i++)
            {
                var angle = (float)(random.NextDouble() * Mathf.PI * 2);
                var height = Mathf.Lerp(0.03f, 1f, (float)random.NextDouble()); // altura uniforme = área uniforme na esfera
                var ring = Mathf.Sqrt(1f - height * height);
                var direction = new Vector3(Mathf.Cos(angle) * ring, height, Mathf.Sin(angle) * ring);
                var right = Vector3.Cross(Vector3.up, direction);
                right = right.sqrMagnitude < 0.0001f ? Vector3.right : right.normalized;
                var up = Vector3.Cross(direction, right);

                // Poucas estrelas grandes e muitas pequenas; algumas puxam para o azul ou o amarelo.
                var big = random.NextDouble() < 0.08;
                var size = (big ? 2.8f : 1.3f) + (float)random.NextDouble() * 0.9f;
                var brightness = (big ? 1.6f : 0.75f) + (float)random.NextDouble() * 0.6f;
                var tint = random.NextDouble();
                var color = tint < 0.2 ? new Color(0.75f, 0.85f, 1f) : tint > 0.88 ? new Color(1f, 0.9f, 0.7f) : Color.white;
                color *= brightness;
                var twinkle = new Vector2((float)(random.NextDouble() * 6.28), 1.2f + (float)random.NextDouble() * 2.5f);

                var center = direction * Radius;
                var start = vertices.Count;
                vertices.Add(center + (-right - up) * size);
                vertices.Add(center + (-right + up) * size);
                vertices.Add(center + (right + up) * size);
                vertices.Add(center + (right - up) * size);
                uvs.Add(new Vector2(-1, -1));
                uvs.Add(new Vector2(-1, 1));
                uvs.Add(new Vector2(1, 1));
                uvs.Add(new Vector2(1, -1));
                for (var k = 0; k < 4; k++)
                {
                    colors.Add(color);
                    twinkles.Add(twinkle);
                }
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            var mesh = new Mesh { name = "Estrelas" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, twinkles);
            mesh.SetTriangles(triangles, 0);
            // A cúpula inteira sempre visível (ela vai junto com a câmera).
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * Radius * 2.2f);
            return mesh;
        }

        /// <summary>Um quadrado só, com o halo em volta (a fase negativa no UV1 marca a lua para o shader).</summary>
        private static Mesh MoonMesh()
        {
            const float size = 46f;
            var mesh = new Mesh { name = "Lua" };
            mesh.SetVertices(new[] { new Vector3(-size, -size, 0), new Vector3(-size, size, 0), new Vector3(size, size, 0), new Vector3(size, -size, 0) });
            var color = new Color(1.25f, 1.2f, 1.05f);
            mesh.SetColors(new[] { color, color, color, color });
            mesh.SetUVs(0, new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1) });
            var moon = new Vector2(-1f, 0f);
            mesh.SetUVs(1, new[] { moon, moon, moon, moon });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * size * 2f);
            return mesh;
        }
    }
}
