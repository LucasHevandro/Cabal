using System.Collections.Generic;
using Game.Client.Fx;
using Game.Shared.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Client.UI
{
    /// <summary>
    /// Janela de opções (F10 no jogo, botão no menu de entrada): vídeo, áudio e idioma. Volume, qualidade, FPS e idioma
    /// valem na hora; resolução e tela cheia só com "Aplicar". Funciona online e offline (criada sozinha ao abrir o jogo).
    /// </summary>
    public sealed class OptionsWindow : MonoBehaviour
    {
        private const float VirtualWidth = 1920f;
        private const float VirtualHeight = 1080f;
        private static readonly Color Panel = new Color(0.03f, 0.03f, 0.05f, 0.94f);
        private static readonly Color Accent = new Color(1f, 0.82f, 0.35f);

        private static OptionsWindow _instance;

        private GUIStyle _title;
        private GUIStyle _label;
        private GUIStyle _small;
        private GUIStyle _button;
        private GUIStyle _toggle;

        private List<Vector2Int> _resolutions = new List<Vector2Int>();
        private int _resolutionIndex;
        private bool _fullscreen;
        private IReadOnlyList<string> _languages = new List<string>();
        private readonly Dictionary<string, string> _languageNames = new Dictionary<string, string>();
        private Rect _rect;
        private float _scale = 1f;
        private Vector2 _offset;

        public static bool IsOpen { get; private set; }

        /// <summary>O ponteiro está sobre a janela (o clique não vai para o mundo).</summary>
        public static bool Contains(Vector2 screenPoint)
        {
            if (!IsOpen || _instance == null) return false;
            var point = new Vector2((screenPoint.x - _instance._offset.x) / _instance._scale,
                (Screen.height - screenPoint.y - _instance._offset.y) / _instance._scale);
            return _instance._rect.Contains(point);
        }

        public static void Open()
        {
            if (_instance == null) return;
            _instance.Refresh();
            IsOpen = true;
        }

        public static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            GameSettings.Save();
        }

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (_instance != null) return;
            var go = new GameObject("Opções");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<OptionsWindow>();
        }

        private void Refresh()
        {
            _resolutions = GameSettings.Resolutions();
            _resolutionIndex = Mathf.Max(0, _resolutions.IndexOf(new Vector2Int(Screen.width, Screen.height)));
            _fullscreen = Screen.fullScreen;
            _languages = GameLocale.Available;
            foreach (var language in _languages)
                if (!_languageNames.ContainsKey(language)) _languageNames[language] = GameLocale.DisplayName(language);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || GameInput.Typing || GameInput.FieldFocused) return;
            if (keyboard.f10Key.wasPressedThisFrame) Toggle();
            else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame) Close();
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            GUI.depth = -30; // por cima de tudo, inclusive do menu de entrada
            EnsureStyles();
            _scale = Mathf.Min(Screen.width / VirtualWidth, Screen.height / VirtualHeight);
            _offset = new Vector2((Screen.width - VirtualWidth * _scale) / 2, (Screen.height - VirtualHeight * _scale) / 2);
            GUI.matrix = Matrix4x4.TRS(new Vector3(_offset.x, _offset.y, 0), Quaternion.identity, new Vector3(_scale, _scale, 1));

            _rect = new Rect(VirtualWidth / 2 - 330, VirtualHeight / 2 - 330, 660, 660);
            Box(new Rect(0, 0, VirtualWidth, VirtualHeight), new Color(0, 0, 0, 0.35f));
            Box(_rect, Panel);
            var x = _rect.x + 28;
            var y = _rect.y + 18;
            var width = _rect.width - 56;
            GUI.Label(new Rect(x, y, width, 34), Loc.T("options.title"), _title);
            y += 52;

            // ----- Vídeo -----
            y = Section(x, y, width, Loc.T("options.video"));
            var size = _resolutions.Count > 0 ? _resolutions[_resolutionIndex] : new Vector2Int(Screen.width, Screen.height);
            y = Chooser(x, y, Loc.T("options.resolution"), $"{size.x} x {size.y}", step =>
                _resolutionIndex = Mathf.Clamp(_resolutionIndex + step, 0, Mathf.Max(0, _resolutions.Count - 1)));
            _fullscreen = GUI.Toggle(new Rect(x + 230, y, 220, 28), _fullscreen, " " + Loc.T("options.fullscreen"), _toggle);
            var changed = size.x != Screen.width || size.y != Screen.height || _fullscreen != Screen.fullScreen;
            GUI.enabled = changed;
            if (GUI.Button(new Rect(_rect.xMax - 28 - 150, y - 4, 150, 34), Loc.T("options.apply"), _button))
                Screen.SetResolution(size.x, size.y, _fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            GUI.enabled = true;
            y += 40;

            var qualityNames = QualitySettings.names;
            var quality = Mathf.Clamp(GameSettings.QualityLevel, 0, qualityNames.Length - 1);
            y = Chooser(x, y, Loc.T("options.quality"), QualityName(qualityNames[quality]), step =>
            {
                GameSettings.QualityLevel = Mathf.Clamp(quality + step, 0, qualityNames.Length - 1);
                GameSettings.Apply();
            });

            var fpsIndex = Mathf.Max(0, IndexOf(GameSettings.FpsChoices, GameSettings.FpsLimit));
            GUI.enabled = !GameSettings.VSync;
            y = Chooser(x, y, Loc.T("options.fps"), FpsName(GameSettings.FpsLimit), step =>
            {
                GameSettings.FpsLimit = GameSettings.FpsChoices[Mathf.Clamp(fpsIndex + step, 0, GameSettings.FpsChoices.Count - 1)];
                GameSettings.Apply();
            });
            GUI.enabled = true;
            var vsync = GUI.Toggle(new Rect(x + 230, y, 400, 28), GameSettings.VSync, " " + Loc.T("options.vsync"), _toggle);
            if (vsync != GameSettings.VSync)
            {
                GameSettings.VSync = vsync;
                GameSettings.Apply();
            }
            y += 46;

            // ----- Áudio -----
            y = Section(x, y, width, Loc.T("options.audio"));
            var master = Slider(x, y, Loc.T("options.volume_master"), GameSettings.MasterVolume);
            y += 36;
            var effects = Slider(x, y, Loc.T("options.volume_effects"), GameSettings.EffectsVolume);
            y += 36;
            GameSettings.MusicVolume = Slider(x, y, Loc.T("options.volume_music"), GameSettings.MusicVolume);
            y += 46;
            if (!Mathf.Approximately(master, GameSettings.MasterVolume))
            {
                GameSettings.MasterVolume = master;
                GameSettings.Apply();
            }
            if (!Mathf.Approximately(effects, GameSettings.EffectsVolume))
            {
                GameSettings.EffectsVolume = effects;
                SoundPlayer.Play(SoundId.Hit, 1f, 0f); // amostra do volume novo
            }

            // ----- Idioma -----
            y = Section(x, y, width, Loc.T("options.language"));
            var languageIndex = Mathf.Max(0, IndexOf(_languages, Loc.Language));
            var languageName = _languageNames.TryGetValue(Loc.Language, out var display) ? display : Loc.Language;
            GUI.enabled = _languages.Count > 1;
            y = Chooser(x, y, Loc.T("options.language_label"), languageName, step =>
            {
                if (_languages.Count == 0) return;
                var next = (languageIndex + step + _languages.Count) % _languages.Count;
                GameLocale.Select(_languages[next]);
            });
            GUI.enabled = true;

            // ----- Botões -----
            var buttonsY = _rect.yMax - 60;
            if (GUI.Button(new Rect(x, buttonsY, 220, 40), Loc.T("options.defaults"), _button))
            {
                GameSettings.ResetDefaults();
                GameSettings.Apply();
            }
            if (GUI.Button(new Rect(_rect.xMax - 28 - 160, buttonsY, 160, 40), "<b>" + Loc.T("ui.close") + "</b>", _button)) Close();
            GUI.Label(new Rect(x, buttonsY - 34, width, 24), Loc.T("options.hint"), _small);

            GUI.matrix = Matrix4x4.identity;
        }

        private float Section(float x, float y, float width, string title)
        {
            GUI.color = Accent;
            GUI.Label(new Rect(x, y, width, 28), "<b>" + title + "</b>", _label);
            GUI.color = Color.white;
            Box(new Rect(x, y + 30, width, 1), new Color(1, 1, 1, 0.2f));
            return y + 40;
        }

        /// <summary>Linha "rótulo  ‹ valor ›" com os botões de anterior e próximo.</summary>
        private float Chooser(float x, float y, string label, string value, System.Action<int> step)
        {
            GUI.Label(new Rect(x, y, 220, 30), label, _label);
            if (GUI.Button(new Rect(x + 230, y, 40, 32), "‹", _button)) step(-1);
            GUI.Label(new Rect(x + 276, y, 200, 30), value, _label.WithCenter());
            if (GUI.Button(new Rect(x + 482, y, 40, 32), "›", _button)) step(1);
            return y + 40;
        }

        private float Slider(float x, float y, string label, float value)
        {
            GUI.Label(new Rect(x, y, 220, 30), label, _label);
            var result = GUI.HorizontalSlider(new Rect(x + 230, y + 10, 292, 20), value, 0f, 1f);
            GUI.Label(new Rect(x + 534, y, 80, 30), $"{Mathf.RoundToInt(result * 100)}%", _label);
            return result;
        }

        private static string QualityName(string unityName)
        {
            var key = "options.quality_" + unityName.ToLowerInvariant();
            return Loc.Has(key) ? Loc.T(key) : unityName;
        }

        private static string FpsName(int fps) => fps == 0 ? Loc.T("options.fps_unlimited") : fps.ToString();

        private static int IndexOf<T>(IReadOnlyList<T> list, T value)
        {
            for (var i = 0; i < list.Count; i++)
                if (EqualityComparer<T>.Default.Equals(list[i], value)) return i;
            return -1;
        }

        private static void Box(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true, wordWrap = true };
            _label.normal.textColor = Color.white;
            _title = new GUIStyle(_label) { fontSize = 28, fontStyle = FontStyle.Bold };
            _small = new GUIStyle(_label) { fontSize = 14 };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 18, richText = true };
            _toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 20, richText = true };
            _toggle.normal.textColor = _toggle.onNormal.textColor = _toggle.hover.textColor = _toggle.onHover.textColor = Color.white;
        }
    }
}
