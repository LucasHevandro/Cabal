using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Game.Shared.Text
{
    /// <summary>
    /// Textos do jogo por idioma. Cada idioma é um arquivo <c>Resources/Locales/&lt;idioma&gt;.txt</c> com linhas
    /// <c>chave = texto</c>; o texto usa <c>{0}</c>, <c>{1}</c>... para os argumentos (com formato opcional, como
    /// <c>{0:N0}</c>). O cliente (Unity) carrega o arquivo pelo <c>Resources</c> e chama <see cref="Load"/>; no .NET
    /// (servidor, testes, bots) o português é lido sozinho da pasta <c>Locales</c> ao lado do programa.
    /// Chave que falta aparece como a própria chave, para ficar visível na tela.
    /// </summary>
    public static class Loc
    {
        public const string DefaultLanguage = "pt-BR";

        private static Dictionary<string, string>? _texts;
        private static CultureInfo _culture = CultureInfo.GetCultureInfo(DefaultLanguage);

        /// <summary>Muda a cada idioma carregado: quem guarda textos prontos sabe quando montar de novo.</summary>
        public static int Version { get; private set; }

        /// <summary>Idioma carregado (ex.: "pt-BR").</summary>
        public static string Language { get; private set; } = DefaultLanguage;

        /// <summary>Cultura do idioma (separador de milhar e decimal nos números).</summary>
        public static CultureInfo Culture => _culture;

        private static Dictionary<string, string> Texts => _texts ??= LoadDefault();

        /// <summary>Todas as chaves do idioma carregado.</summary>
        public static IEnumerable<string> Keys => Texts.Keys;

        /// <summary>
        /// Troca o idioma pelo conteúdo de um arquivo de tradução. Com <paramref name="fallback"/> (o português), o que
        /// faltar na tradução aparece no idioma padrão em vez da chave.
        /// </summary>
        public static void Load(string language, string contents, string? fallback = null)
        {
            var texts = fallback != null ? Parse(fallback, out _) : new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in Parse(contents, out _)) texts[pair.Key] = pair.Value;
            _texts = texts;
            Version++;
            Language = language;
            try
            {
                _culture = CultureInfo.GetCultureInfo(language);
            }
            catch (CultureNotFoundException)
            {
                _culture = CultureInfo.InvariantCulture;
            }
        }

        public static bool Has(string key) => Texts.ContainsKey(key);

        /// <summary>Texto da chave (ou a própria chave, se faltar).</summary>
        public static string T(string key) => Texts.TryGetValue(key, out var text) ? text : key;

        /// <summary>Texto da chave com os argumentos. Argumento texto começando com '@' é outra chave (traduzida também).</summary>
        public static string T(string key, params object?[] args)
        {
            var template = T(key);
            if (args.Length == 0) return template;
            var values = new object?[args.Length];
            for (var i = 0; i < args.Length; i++) values[i] = Resolve(args[i]);
            try
            {
                return string.Format(_culture, template, values);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        private static readonly Dictionary<(string, string, string), string> KeyCache = new Dictionary<(string, string, string), string>();
        private static readonly Dictionary<string, (int Version, long A, long B, int Count, string Text)> NumberCache =
            new Dictionary<string, (int, long, long, int, string)>(StringComparer.Ordinal);

        /// <summary>
        /// Chave montada de partes (ex.: "item." + id), guardada para não criar um texto novo a cada leitura: os nomes
        /// dos catálogos são lidos todo quadro pela interface.
        /// </summary>
        public static string Key(string prefix, string id, string suffix = "")
        {
            var parts = (prefix, id ?? "", suffix);
            lock (KeyCache) // o servidor e a API de contas podem ler nomes de threads diferentes
            {
                if (!KeyCache.TryGetValue(parts, out var key))
                {
                    key = prefix + parts.Item2 + suffix;
                    KeyCache[parts] = key;
                }
                return key;
            }
        }

        /// <summary>Texto com um número. Se o número não mudou desde a última vez, devolve o mesmo texto (sem montar de novo).</summary>
        public static string T(string key, long a) => Number(key, a, 0, 1);

        /// <summary>Texto com dois números (ex.: "HP 120 / 300"), reaproveitado enquanto os números não mudam.</summary>
        public static string T(string key, long a, long b) => Number(key, a, b, 2);

        private static string Number(string key, long a, long b, int count)
        {
            lock (NumberCache)
            {
                if (NumberCache.TryGetValue(key, out var last) && last.Version == Version && last.A == a && last.B == b && last.Count == count)
                    return last.Text;
            }
            var text = count == 1 ? T(key, new object?[] { a }) : T(key, new object?[] { a, b });
            lock (NumberCache) NumberCache[key] = (Version, a, b, count, text);
            return text;
        }

        /// <summary>Texto da chave, ou vazio se ela não existir (para textos opcionais, como descrições).</summary>
        public static string Optional(string key) => Texts.TryGetValue(key, out var text) ? text : "";

        /// <summary>Monta um texto vindo do servidor.</summary>
        public static string Format(LocalText? text)
        {
            if (text == null || text.Key.Length == 0) return "";
            var args = new object?[text.Args.Length];
            for (var i = 0; i < args.Length; i++) args[i] = text.Args[i];
            return T(text.Key, args);
        }

        /// <summary>Referência a uma chave para usar como argumento (traduzida no destino).</summary>
        public static string Ref(string key) => "@" + key;

        private static object? Resolve(object? arg)
        {
            switch (arg)
            {
                case LocalText local:
                    return Format(local);
                case string s when s.Length > 1 && s[0] == '@':
                    return T(s.Substring(1));
                case string s when LocalText.IsNumber(s):
                    // Números chegam do servidor como texto invariante; voltam a ser números para o formato do idioma.
                    return s.IndexOf('.') >= 0
                        ? (object)double.Parse(s, CultureInfo.InvariantCulture)
                        : long.Parse(s, CultureInfo.InvariantCulture);
                default:
                    return arg;
            }
        }

        /// <summary>Lê um arquivo de tradução. <paramref name="problems"/> lista linhas inválidas e chaves repetidas.</summary>
        public static Dictionary<string, string> Parse(string contents, out List<string> problems)
        {
            problems = new List<string>();
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            var lineNumber = 0;
            foreach (var raw in contents.Split('\n'))
            {
                lineNumber++;
                var line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    problems.Add($"linha {lineNumber}: falta '='");
                    continue;
                }
                var key = line.Substring(0, equals).Trim();
                var value = Unescape(line.Substring(equals + 1).Trim());
                if (texts.ContainsKey(key)) problems.Add($"linha {lineNumber}: chave repetida {key}");
                texts[key] = value;
            }
            return texts;
        }

        private static string Unescape(string value)
        {
            if (value.IndexOf('\\') < 0) return value;
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    var next = value[++i];
                    builder.Append(next == 'n' ? '\n' : next);
                    continue;
                }
                builder.Append(value[i]);
            }
            return builder.ToString();
        }

        private static Dictionary<string, string> LoadDefault()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory ?? "", "Locales", DefaultLanguage + ".txt");
                if (File.Exists(path)) return Parse(File.ReadAllText(path, Encoding.UTF8), out _);
            }
            catch (Exception)
            {
                // Sem acesso ao disco (ex.: plataforma restrita): fica sem textos até alguém chamar Load.
            }
            return new Dictionary<string, string>();
        }
    }
}
