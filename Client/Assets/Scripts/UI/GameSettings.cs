using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Opções do jogador (qualidade, limite de FPS, sincronia vertical e volumes), salvas no PlayerPrefs e aplicadas
    /// antes da primeira cena. A resolução e a tela cheia o próprio Unity guarda; o idioma fica no <see cref="GameLocale"/>.
    /// </summary>
    public static class GameSettings
    {
        /// <summary>Limites de FPS oferecidos (0 = sem limite).</summary>
        public static readonly IReadOnlyList<int> FpsChoices = new[] { 30, 60, 120, 144, 0 };

        private const string Prefix = "opcoes.";

        public static int QualityLevel { get; set; }
        public static int FpsLimit { get; set; } = 60;
        public static bool VSync { get; set; }
        public static float MasterVolume { get; set; } = 0.8f;
        public static float EffectsVolume { get; set; } = 0.9f;

        /// <summary>Guardado já agora; a música entra na parte de áudio da Fase 7.</summary>
        public static float MusicVolume { get; set; } = 0.6f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadAndApply()
        {
            Load();
            Apply();
        }

        public static void Load()
        {
            ResetDefaults();
            try
            {
                QualityLevel = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "qualidade", QualityLevel), 0, QualitySettings.names.Length - 1);
                var fps = PlayerPrefs.GetInt(Prefix + "fps", FpsLimit);
                FpsLimit = ((ICollection<int>)FpsChoices).Contains(fps) ? fps : 60;
                VSync = PlayerPrefs.GetInt(Prefix + "vsync", VSync ? 1 : 0) == 1;
                MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "volume", MasterVolume));
                EffectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "efeitos", EffectsVolume));
                MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "musica", MusicVolume));
            }
            catch (Exception)
            {
                // Sem PlayerPrefs: fica com os valores padrão.
            }
        }

        /// <summary>Valores padrão (a qualidade é a do projeto).</summary>
        public static void ResetDefaults()
        {
            QualityLevel = QualitySettings.GetQualityLevel();
            FpsLimit = 60;
            VSync = false;
            MasterVolume = 0.8f;
            EffectsVolume = 0.9f;
            MusicVolume = 0.6f;
        }

        public static void Apply()
        {
            if (QualitySettings.GetQualityLevel() != QualityLevel) QualitySettings.SetQualityLevel(QualityLevel, true);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            // Com a sincronia vertical, quem limita é o monitor.
            Application.targetFrameRate = VSync || FpsLimit == 0 ? -1 : FpsLimit;
            AudioListener.volume = MasterVolume;
        }

        public static void Save()
        {
            try
            {
                PlayerPrefs.SetInt(Prefix + "qualidade", QualityLevel);
                PlayerPrefs.SetInt(Prefix + "fps", FpsLimit);
                PlayerPrefs.SetInt(Prefix + "vsync", VSync ? 1 : 0);
                PlayerPrefs.SetFloat(Prefix + "volume", MasterVolume);
                PlayerPrefs.SetFloat(Prefix + "efeitos", EffectsVolume);
                PlayerPrefs.SetFloat(Prefix + "musica", MusicVolume);
                PlayerPrefs.Save();
            }
            catch (Exception)
            {
                // Não salvou: as opções valem só nesta sessão.
            }
        }

        /// <summary>Resoluções do monitor, sem repetir largura x altura, da menor para a maior.</summary>
        public static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (!list.Contains(size)) list.Add(size);
            }
            var current = new Vector2Int(Screen.width, Screen.height);
            if (!list.Contains(current)) list.Add(current);
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }
    }
}
