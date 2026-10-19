using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Números de dano que sobem e somem acima do alvo, com contorno escuro para ler em qualquer fundo. O crítico (texto
    /// com "!") entra maior, treme um instante e sobe mais. Desenhados com IMGUI (suficiente para o protótipo).
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
            if (cam == null || Event.current.type != EventType.Repaint) return;
            _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };

            for (var i = Entries.Count - 1; i >= 0; i--)
            {
                var entry = Entries[i];
                var age = (Time.unscaledTime - entry.StartedAt) / Lifetime;
                if (age >= 1f)
                {
                    Entries.RemoveAt(i);
                    continue;
                }

                var critical = entry.Text.EndsWith("!");
                // Sobe rápido no começo e devagar no fim; o crítico sobe mais.
                var rise = RiseMeters * (critical ? 1.35f : 1f) * (1f - (1f - age) * (1f - age));
                var screen = cam.WorldToScreenPoint(entry.Position + Vector3.up * rise);
                if (screen.z < 0) continue;

                // Começa maior e encolhe: dá "pancada" ao número (bem mais forte no crítico).
                var pop = 1f + Mathf.Max(0, 0.22f - age) * (critical ? 4.5f : 2f);
                _style.fontSize = Mathf.RoundToInt(entry.Size * pop);
                var shake = critical && age < 0.18f ? new Vector2(Mathf.Sin(age * 140f) * 3f, Mathf.Cos(age * 110f) * 2f) : Vector2.zero;
                var rect = new Rect(screen.x - 160 + entry.Jitter.x + shake.x, Screen.height - screen.y - 25 + entry.Jitter.y + shake.y, 320, 50);

                var alpha = 1f - Mathf.Clamp01((age - 0.6f) / 0.4f);
                // Contorno: o texto escuro em volta, nos oito lados.
                var stroke = Mathf.Max(1.5f, _style.fontSize / 14f);
                GUI.color = new Color(0.05f, 0.03f, 0.02f, alpha * 0.9f);
                for (var k = 0; k < 8; k++)
                {
                    var angle = k * Mathf.PI / 4f;
                    var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * stroke;
                    GUI.Label(new Rect(rect.x + offset.x, rect.y + offset.y + 1f, rect.width, rect.height), entry.Text, _style);
                }
                GUI.color = new Color(entry.Color.r, entry.Color.g, entry.Color.b, alpha);
                GUI.Label(rect, entry.Text, _style);
                if (critical && age < 0.35f)
                {
                    // Brilho claro por cima no crítico, que some logo.
                    GUI.color = new Color(1f, 1f, 1f, alpha * (0.35f - age) * 1.6f);
                    GUI.Label(rect, entry.Text, _style);
                }
            }
            GUI.color = Color.white;
        }
    }
}
