using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Monta malhas low-poly de "faces chapadas" (cada triângulo com a própria normal), no estilo dos pacotes KayKit:
    /// cilindros afunilados, cones, esferas amassadas e caixas. Cada parte vai para um submesh (um material por parte).
    /// </summary>
    internal sealed class MeshKit
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<List<int>> _triangles = new List<List<int>>();

        // Centro da peça que está sendo montada: cada face é virada para fora dele (frente = para fora).
        private Vector3 _pivot;

        public MeshKit(int submeshes)
        {
            for (var i = 0; i < submeshes; i++) _triangles.Add(new List<int>());
        }

        public void Triangle(int sub, Vector3 a, Vector3 b, Vector3 c)
        {
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-10f) return;
            normal.Normalize();
            if (Vector3.Dot(normal, (a + b + c) / 3f - _pivot) < 0f)
            {
                (b, c) = (c, b);
                normal = -normal;
            }
            var start = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _normals.Add(normal);
            _normals.Add(normal);
            _normals.Add(normal);
            _triangles[sub].Add(start);
            _triangles[sub].Add(start + 1);
            _triangles[sub].Add(start + 2);
        }

        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Triangle(sub, a, b, c);
            Triangle(sub, a, c, d);
        }

        /// <summary>Cilindro afunilado (ou cone, com topo 0). <paramref name="jitter"/> mexe os vértices do topo.</summary>
        public void Frustum(int sub, Vector3 baseCenter, Quaternion rotation, float bottomRadius, float topRadius, float height,
            int sides, System.Random random = null, float jitter = 0f, bool cap = true, float topJitterY = 0f, bool bottomCap = false)
        {
            var bottom = new Vector3[sides];
            var top = new Vector3[sides];
            var offset = random == null ? 0f : (float)random.NextDouble() * Mathf.PI;
            for (var i = 0; i < sides; i++)
            {
                var angle = offset + i * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var j = random == null ? 0f : ((float)random.NextDouble() - 0.5f) * jitter;
                var jy = random == null ? 0f : (float)random.NextDouble() * topJitterY;
                bottom[i] = baseCenter + rotation * (dir * bottomRadius);
                top[i] = baseCenter + rotation * (dir * Mathf.Max(0f, topRadius + j) + Vector3.up * (height - jy));
            }
            var topCenter = baseCenter + rotation * (Vector3.up * height);
            _pivot = baseCenter + rotation * (Vector3.up * height * 0.5f);
            for (var i = 0; i < sides; i++)
            {
                var n = (i + 1) % sides;
                if (topRadius <= 0.001f) Triangle(sub, bottom[i], topCenter, bottom[n]);
                else Quad(sub, bottom[i], top[i], top[n], bottom[n]);
                if (cap && topRadius > 0.001f) Triangle(sub, topCenter, top[n], top[i]);
                if (bottomCap) Triangle(sub, baseCenter, bottom[i], bottom[n]);
            }
        }

        /// <summary>Esfera de 80 faces, amassada por ruído (copas, pedras, arbustos).</summary>
        public void Blob(int sub, Vector3 center, Vector3 scale, System.Random random, float roughness)
        {
            var t = (1f + Mathf.Sqrt(5f)) / 2f;
            var points = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            var faces = new List<int[]>
            {
                new[] { 0, 11, 5 }, new[] { 0, 5, 1 }, new[] { 0, 1, 7 }, new[] { 0, 7, 10 }, new[] { 0, 10, 11 },
                new[] { 1, 5, 9 }, new[] { 5, 11, 4 }, new[] { 11, 10, 2 }, new[] { 10, 7, 6 }, new[] { 7, 1, 8 },
                new[] { 3, 9, 4 }, new[] { 3, 4, 2 }, new[] { 3, 2, 6 }, new[] { 3, 6, 8 }, new[] { 3, 8, 9 },
                new[] { 4, 9, 5 }, new[] { 2, 4, 11 }, new[] { 6, 2, 10 }, new[] { 8, 6, 7 }, new[] { 9, 8, 1 },
            };
            for (var i = 0; i < points.Count; i++) points[i] = points[i].normalized;
            // Uma subdivisão: 80 faces.
            var midCache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (midCache.TryGetValue(key, out var index)) return index;
                points.Add(((points[a] + points[b]) / 2f).normalized);
                return midCache[key] = points.Count - 1;
            }
            var subdivided = new List<int[]>();
            foreach (var f in faces)
            {
                var ab = Mid(f[0], f[1]);
                var bc = Mid(f[1], f[2]);
                var ca = Mid(f[2], f[0]);
                subdivided.Add(new[] { f[0], ab, ca });
                subdivided.Add(new[] { f[1], bc, ab });
                subdivided.Add(new[] { f[2], ca, bc });
                subdivided.Add(new[] { ab, bc, ca });
            }
            var displaced = new Vector3[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                var r = 1f + ((float)random.NextDouble() - 0.5f) * 2f * roughness;
                displaced[i] = center + Vector3.Scale(points[i] * r, scale);
            }
            _pivot = center;
            foreach (var f in subdivided) Triangle(sub, displaced[f[0]], displaced[f[2]], displaced[f[1]]);
        }

        /// <summary>Caixa com a base no chão; <paramref name="topJitter"/> deixa o topo irregular (muro quebrado).</summary>
        public void Box(int sub, Vector3 baseCenter, Quaternion rotation, Vector3 size, System.Random random = null, float topJitter = 0f)
        {
            var hx = size.x / 2f;
            var hz = size.z / 2f;
            float Top() => size.y - (random == null ? 0f : (float)random.NextDouble() * topJitter);
            var b0 = new Vector3(-hx, 0, -hz);
            var b1 = new Vector3(hx, 0, -hz);
            var b2 = new Vector3(hx, 0, hz);
            var b3 = new Vector3(-hx, 0, hz);
            var t0 = new Vector3(-hx, Top(), -hz);
            var t1 = new Vector3(hx, Top(), -hz);
            var t2 = new Vector3(hx, Top(), hz);
            var t3 = new Vector3(-hx, Top(), hz);
            Vector3 W(Vector3 v) => baseCenter + rotation * v;
            _pivot = baseCenter + rotation * (Vector3.up * size.y * 0.5f);
            Quad(sub, W(b0), W(t0), W(t1), W(b1));
            Quad(sub, W(b1), W(t1), W(t2), W(b2));
            Quad(sub, W(b2), W(t2), W(t3), W(b3));
            Quad(sub, W(b3), W(t3), W(t0), W(b0));
            Quad(sub, W(t0), W(t3), W(t2), W(t1));
        }

        /// <summary>Telhado de duas águas (prisma) sobre uma base de largura x profundidade.</summary>
        public void Roof(int sub, Vector3 baseCenter, Quaternion rotation, float width, float depth, float height, float overhang)
        {
            var hx = width / 2f + overhang;
            var hz = depth / 2f + overhang;
            Vector3 W(Vector3 v) => baseCenter + rotation * v;
            var a = W(new Vector3(-hx, 0, -hz));
            var b = W(new Vector3(hx, 0, -hz));
            var c = W(new Vector3(hx, 0, hz));
            var d = W(new Vector3(-hx, 0, hz));
            var ridge0 = W(new Vector3(0, height, -hz));
            var ridge1 = W(new Vector3(0, height, hz));
            _pivot = baseCenter + rotation * (Vector3.up * height * 0.35f);
            Quad(sub, a, ridge0, ridge1, d);
            Quad(sub, c, ridge1, ridge0, b);
            Triangle(sub, a, b, ridge0);
            Triangle(sub, c, d, ridge1);
            Quad(sub, a, d, c, b);
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (_vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.subMeshCount = _triangles.Count;
            for (var i = 0; i < _triangles.Count; i++) mesh.SetTriangles(_triangles[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
