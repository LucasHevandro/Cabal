using System;
using System.Globalization;
using Game.Shared.Net;

namespace Game.Shared.Text
{
    /// <summary>
    /// Texto a traduzir no destino: uma chave do arquivo de idioma e os argumentos já como texto. É o que o servidor
    /// manda para o cliente (avisos, chat do sistema, motivos de recusa), para cada jogador ver no próprio idioma.
    /// Argumentos: números viram texto invariante; '@chave' é traduzido também (ex.: nome de item).
    /// </summary>
    public sealed class LocalText
    {
        public static readonly LocalText Empty = new LocalText("");

        public string Key { get; }
        public string[] Args { get; }

        public LocalText(string key, params object?[] args)
        {
            Key = key ?? "";
            Args = new string[args.Length];
            for (var i = 0; i < args.Length; i++) Args[i] = ToArg(args[i]);
        }

        private LocalText(string key, string[] args)
        {
            Key = key;
            Args = args;
        }

        public bool IsEmpty => Key.Length == 0;

        /// <summary>Remonta um texto a partir da chave e dos argumentos já convertidos (ex.: vindos de JSON).</summary>
        public static LocalText FromParts(string key, string[]? args) => new LocalText(key ?? "", args ?? Array.Empty<string>());

        /// <summary>O texto no idioma carregado (<see cref="Loc"/>).</summary>
        public override string ToString() => Loc.Format(this);

        public void Write(PacketWriter w)
        {
            w.WriteString(Key);
            w.WriteByte((byte)Args.Length);
            foreach (var arg in Args) w.WriteString(arg);
        }

        public static LocalText Read(PacketReader r)
        {
            var key = r.ReadString();
            var count = r.ReadByte();
            var args = new string[count];
            for (var i = 0; i < count; i++) args[i] = r.ReadString();
            return new LocalText(key, args);
        }

        internal static bool IsNumber(string s)
        {
            if (s.Length == 0 || s.Length > 24) return false;
            var start = s[0] == '-' ? 1 : 0;
            if (start == s.Length) return false;
            var dots = 0;
            for (var i = start; i < s.Length; i++)
            {
                if (s[i] == '.' && ++dots == 1 && i > start && i < s.Length - 1) continue;
                if (s[i] < '0' || s[i] > '9') return false;
            }
            return true;
        }

        private static string ToArg(object? arg) => arg switch
        {
            null => "",
            string s => s,
            LocalText nested when nested.Args.Length == 0 => Loc.Ref(nested.Key),
            LocalText nested => nested.ToString(),
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            float f => Math.Round(f, 3).ToString("0.###", CultureInfo.InvariantCulture),
            double d => Math.Round(d, 3).ToString("0.###", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => arg.ToString() ?? "",
        };
    }
}
