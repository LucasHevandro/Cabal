using System.Collections.Generic;
using System.Linq;
using Game.Shared.Text;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Carrega o arquivo de idioma (Resources/Locales/&lt;idioma&gt;.txt, no pacote Shared) antes da primeira cena e
    /// também no editor, para os construtores de cena verem os nomes. O idioma escolhido fica salvo no PlayerPrefs.
    /// </summary>
    public static class GameLocale
    {
        private const string PrefKey = "idioma";
        private const string Folder = "Locales/";

        /// <summary>Idiomas com arquivo disponível (ex.: pt-BR).</summary>
        public static IReadOnlyList<string> Available =>
            Resources.LoadAll<TextAsset>(Folder.TrimEnd('/')).Select(a => a.name).OrderBy(n => n).ToList();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadSaved()
        {
            var language = Loc.DefaultLanguage;
            try
            {
                language = PlayerPrefs.GetString(PrefKey, Loc.DefaultLanguage);
            }
            catch (System.Exception)
            {
                // Sem PlayerPrefs (ex.: plataforma restrita): fica no padrão.
            }
            Load(language);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void LoadInEditor() => Load(Loc.DefaultLanguage);
#endif

        /// <summary>Nome do idioma escrito nele mesmo (chave <c>language.name</c> do arquivo), ou o código.</summary>
        public static string DisplayName(string language)
        {
            var asset = Resources.Load<TextAsset>(Folder + language);
            if (asset == null) return language;
            return Loc.Parse(asset.text, out _).TryGetValue("language.name", out var name) ? name : language;
        }

        /// <summary>Carrega o idioma; o que faltar nele aparece em português. Falso se o arquivo não existir.</summary>
        public static bool Load(string language)
        {
            var fallback = Resources.Load<TextAsset>(Folder + Loc.DefaultLanguage);
            var asset = language == Loc.DefaultLanguage ? fallback : Resources.Load<TextAsset>(Folder + language);
            if (asset == null)
            {
                if (fallback != null) Loc.Load(Loc.DefaultLanguage, fallback.text);
                return false;
            }
            Loc.Load(language, asset.text, asset == fallback || fallback == null ? null : fallback.text);
            return true;
        }

        /// <summary>Troca o idioma e guarda a escolha para a próxima vez.</summary>
        public static bool Select(string language)
        {
            if (!Load(language)) return false;
            PlayerPrefs.SetString(PrefKey, language);
            PlayerPrefs.Save();
            return true;
        }
    }
}
