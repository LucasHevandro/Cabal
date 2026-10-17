using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Números de dano que sobem e somem acima do alvo. Desenhados com IMGUI (suficiente para o protótipo).
    /// </summary>
    public sealed class DamageNumbers : MonoBehaviour
    {
        private const float Lifetime = 0.9f;
        private const float RiseMeters = 1.2f;

        private struct Entry
        {
            public Vector3 Position;
            public Vector2 Jitter;
            public string Text;
            public Color Color;
            public float Size;
            public float StartedAt;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private GUIStyle _style;

        /// <summary>Mostra um número no ponto do mundo indicado.</summary>
        public static void Spawn(Vector3 worldPosition, string text, Color color, float size = 26f)
        {
            Entries.Add(new Entry
            {
                Position = worldPosition,
                Jitter = Random.insideUnitCircle * 18f,
                Text = text,
                Color = color,
                Size = size,
                StartedAt = Time.unscaledTime,
            });
        }

        // Só GUI (sem GUILayout): sem a passada de layout, o OnGUI roda menos vezes por quadro.
        private void OnEnable() => useGUILayout = false;

        private void OnGUI()
        {
            var cam = Camera.main;
            if (cam == null) return;
            _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

            for (var i = Entries.Count - 1; i >= 0; i--)
            {
                var entry = Entries[i];
                var age = (Time.unscaledTime - entry.StartedAt) / Lifetime;
                if (age >= 1f)
                {
                    Entries.RemoveAt(i);
                    continue;
                }

                var screen = cam.WorldToScreenPoint(entry.Position + Vector3.up * (RiseMeters * age));
                if (screen.z < 0) continue;

                // Começa um pouco maior e diminui: dá "pancada" ao número.
                var pop = 1f + Mathf.Max(0, 0.25f - age) * 2f;
                _style.fontSize = Mathf.RoundToInt(entry.Size * pop);
                var rect = new Rect(screen.x - 120 + entry.Jitter.x, Screen.height - screen.y - 20 + entry.Jitter.y, 240, 40);

                var alpha = 1f - Mathf.Clamp01((age - 0.6f) / 0.4f);
                GUI.color = new Color(0, 0, 0, alpha * 0.8f);
                GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), entry.Text, _style);
                GUI.color = new Color(entry.Color.r, entry.Color.g, entry.Color.b, alpha);
                GUI.Label(rect, entry.Text, _style);
            }
            GUI.color = Color.white;
        }
    }
}
