using System;
using System.Collections.Generic;
using System.Text;
using Game.Shared.Accounts;

namespace Game.Shared.Social
{
    public enum ChatChannel : byte
    {
        /// <summary>Todos no mapa.</summary>
        Map = 0,

        Party = 1,

        /// <summary>Sussurro: só para um jogador, pelo nome.</summary>
        Whisper = 2,

        /// <summary>Avisos do jogo (grupo, erros de chat). O cliente nunca envia neste canal.</summary>
        System = 3,
    }

    public static class ChatRules
    {
        public const int MaxLength = 120;

        /// <summary>Quantas mensagens seguidas antes do limite (balde que enche uma a cada <see cref="RefillSeconds"/>).</summary>
        public const int Burst = 5;

        public const double RefillSeconds = 1.5;

        /// <summary>Tira caracteres de controle, junta espaços e corta no tamanho máximo. Vazio = não enviar.</summary>
        public static string Sanitize(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var builder = new StringBuilder(Math.Min(text!.Length, MaxLength));
            var lastWasSpace = true;
            foreach (var c in text)
            {
                var space = char.IsWhiteSpace(c);
                if (char.IsControl(c) && !space) continue;
                if (space)
                {
                    if (lastWasSpace) continue;
                    builder.Append(' ');
                }
                else
                {
                    builder.Append(c);
                }
                lastWasSpace = space;
                if (builder.Length >= MaxLength) break;
            }
            return builder.ToString().TrimEnd();
        }
    }

    /// <summary>Limite de mensagens por jogador (anti-flood), decidido no servidor.</summary>
    public sealed class ChatRateLimiter
    {
        private double _tokens = ChatRules.Burst;
        private double _lastAt = double.NaN;

        public bool TryConsume(double now)
        {
            if (!double.IsNaN(_lastAt))
                _tokens = Math.Min(ChatRules.Burst, _tokens + Math.Max(0, now - _lastAt) / ChatRules.RefillSeconds);
            _lastAt = now;
            if (_tokens < 1) return false;
            _tokens -= 1;
            return true;
        }
    }

    /// <summary>
    /// Filtro de palavrões: troca cada palavra proibida por asteriscos do mesmo tamanho. Compara sem acento, sem
    /// maiúsculas, com números no lugar de letras (m3rd4) e letras repetidas (merrrda) — mas palavra inteira, para
    /// não estragar palavras normais que contêm um pedaço (cubo, computador).
    /// </summary>
    public static class ProfanityFilter
    {
        private static readonly string[] Words =
        {
            "cu", "fdp", "vsf", "pqp", "tnc", "krl", "porra", "merda", "bosta", "puta", "puto", "caralho", "cacete",
            "buceta", "foda", "fodase", "foder", "fudido", "fodido", "arrombado", "arrombada", "otario", "otaria",
            "desgracado", "desgracada", "viado", "cuzao", "piranha", "vagabunda", "vagabundo", "babaca",
        };

        /// <summary>Começos que pegam as variações (caralhudo, arrombados, filhadaputa).</summary>
        private static readonly string[] Stems =
        {
            "caralh", "arromb", "desgrac", "vagabund", "filhadaput", "filhodaput", "fodend", "putaria",
        };

        private static readonly HashSet<string> WordSet = new HashSet<string>();
        private static readonly List<string> StemList = new List<string>();

        static ProfanityFilter()
        {
            foreach (var word in Words) WordSet.Add(Normalize(word));
            foreach (var stem in Stems) StemList.Add(Normalize(stem));
        }

        public static bool IsBad(string word)
        {
            var key = Normalize(word);
            if (key.Length == 0) return false;
            if (WordSet.Contains(key)) return true;
            foreach (var stem in StemList)
            {
                if (key.StartsWith(stem, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var result = new StringBuilder(text);
            var start = -1;
            for (var i = 0; i <= text.Length; i++)
            {
                var inWord = i < text.Length && IsWordChar(text[i]);
                if (inWord && start < 0) start = i;
                if (inWord || start < 0) continue;
                if (IsBad(text.Substring(start, i - start)))
                {
                    for (var j = start; j < i; j++) result[j] = '*';
                }
                start = -1;
            }
            return result.ToString();
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '@' || c == '$';

        /// <summary>Sem acento, minúsculas, números e símbolos viram letras, letras repetidas viram uma só.</summary>
        private static string Normalize(string word)
        {
            var plain = AccountRules.NameKey(word);
            var builder = new StringBuilder(plain.Length);
            foreach (var raw in plain)
            {
                var c = raw switch
                {
                    '0' => 'o',
                    '1' => 'i',
                    '3' => 'e',
                    '4' => 'a',
                    '@' => 'a',
                    '5' => 's',
                    '$' => 's',
                    '7' => 't',
                    _ => raw,
                };
                if (!char.IsLetter(c)) continue;
                if (builder.Length > 0 && builder[builder.Length - 1] == c) continue;
                builder.Append(c);
            }
            return builder.ToString();
        }
    }

    public enum ChatInputKind
    {
        /// <summary>Nada para fazer (linha vazia).</summary>
        None,

        Message,
        PartyInvite,
        PartyLeave,
        PartyKick,
        PartyPromote,
        FriendAdd,
        FriendRemove,
        Block,
        Unblock,

        /// <summary>Denúncia: <see cref="ChatInput.Target"/> é o nome e <see cref="ChatInput.Text"/> o motivo.</summary>
        Report,

        /// <summary>Abre ou fecha a janela de amigos.</summary>
        FriendsWindow,

        Help,

        /// <summary>Comando errado; <see cref="ChatInput.Error"/> diz o porquê.</summary>
        Invalid,
    }

    public struct ChatInput
    {
        public ChatInputKind Kind;
        public ChatChannel Channel;

        /// <summary>Nome do jogador (sussurro e comandos de grupo).</summary>
        public string Target;

        public string Text;
        public string Error;
    }

    /// <summary>
    /// Lê o que o jogador digitou na caixa de chat. Texto solto vai para o canal atual; comandos começam com "/":
    /// /m (mapa), /g (grupo), /s nome (sussurro), /r (responder o último sussurro), /convidar, /sair, /expulsar,
    /// /lider e /ajuda.
    /// </summary>
    public static class ChatParser
    {
        public const string HelpText =
            "/m texto: mapa · /g texto: grupo · /s nome texto: sussurro · /r texto: responder · " +
            "/convidar nome · /sair · /expulsar nome · /lider nome · /amigo nome · /remover nome · " +
            "/bloquear nome · /desbloquear nome · /denunciar nome motivo · /amigos";

        public static ChatInput Parse(string line, ChatChannel current, string lastWhisperFrom = "")
        {
            var text = ChatRules.Sanitize(line);
            if (text.Length == 0) return new ChatInput { Kind = ChatInputKind.None };
            if (text[0] != '/') return Message(current == ChatChannel.System ? ChatChannel.Map : current, "", text);

            var space = text.IndexOf(' ');
            var command = (space < 0 ? text.Substring(1) : text.Substring(1, space - 1)).ToLowerInvariant();
            var rest = space < 0 ? "" : text.Substring(space + 1).Trim();
            var (first, afterFirst) = SplitFirst(rest);

            switch (command)
            {
                case "m":
                case "mapa":
                    return Message(ChatChannel.Map, "", rest);
                case "g":
                case "grupo":
                    return Message(ChatChannel.Party, "", rest);
                case "s":
                case "w":
                case "sussurro":
                    if (first.Length == 0) return Invalid("Use /s nome mensagem");
                    return Message(ChatChannel.Whisper, first, afterFirst);
                case "r":
                case "responder":
                    if (string.IsNullOrEmpty(lastWhisperFrom)) return Invalid("Ninguém sussurrou para você ainda");
                    return Message(ChatChannel.Whisper, lastWhisperFrom, rest);
                case "convidar":
                case "c":
                    return first.Length == 0 ? Invalid("Use /convidar nome") : Command(ChatInputKind.PartyInvite, first);
                case "sair":
                    return Command(ChatInputKind.PartyLeave, "");
                case "expulsar":
                    return first.Length == 0 ? Invalid("Use /expulsar nome") : Command(ChatInputKind.PartyKick, first);
                case "lider":
                case "líder":
                    return first.Length == 0 ? Invalid("Use /lider nome") : Command(ChatInputKind.PartyPromote, first);
                case "amigo":
                    return first.Length == 0 ? Invalid("Use /amigo nome") : Command(ChatInputKind.FriendAdd, first);
                case "remover":
                    return first.Length == 0 ? Invalid("Use /remover nome") : Command(ChatInputKind.FriendRemove, first);
                case "bloquear":
                    return first.Length == 0 ? Invalid("Use /bloquear nome") : Command(ChatInputKind.Block, first);
                case "desbloquear":
                    return first.Length == 0 ? Invalid("Use /desbloquear nome") : Command(ChatInputKind.Unblock, first);
                case "denunciar":
                    if (first.Length == 0 || afterFirst.Length == 0) return Invalid("Use /denunciar nome motivo");
                    return new ChatInput { Kind = ChatInputKind.Report, Target = first, Text = afterFirst };
                case "amigos":
                    return Command(ChatInputKind.FriendsWindow, "");
                case "ajuda":
                case "?":
                    return Command(ChatInputKind.Help, "");
                default:
                    return Invalid($"Comando desconhecido: /{command} (digite /ajuda)");
            }
        }

        private static ChatInput Message(ChatChannel channel, string target, string text) => text.Length == 0
            ? new ChatInput { Kind = ChatInputKind.None, Channel = channel, Target = target, Text = "" }
            : new ChatInput { Kind = ChatInputKind.Message, Channel = channel, Target = target, Text = text };

        private static ChatInput Command(ChatInputKind kind, string target) =>
            new ChatInput { Kind = kind, Target = target, Text = "" };

        private static ChatInput Invalid(string error) => new ChatInput { Kind = ChatInputKind.Invalid, Error = error, Text = "" };

        private static (string First, string Remainder) SplitFirst(string text)
        {
            var space = text.IndexOf(' ');
            return space < 0 ? (text, "") : (text.Substring(0, space), text.Substring(space + 1).Trim());
        }
    }
}
