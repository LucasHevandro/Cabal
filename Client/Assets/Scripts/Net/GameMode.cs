using System;
using System.IO;
using Game.Shared.Net;
using UnityEngine;

namespace Game.Client.Net
{
    /// <summary>
    /// Offline (tudo simulado no cliente, como nas Fases 1 e 2) ou online (o servidor decide).
    /// Definido antes da cena carregar: pela linha de comando (<c>-connect host:porta -name Nome</c>)
    /// ou pelo menu de conexão, que recarrega a cena no modo escolhido.
    /// O servidor padrão do menu vem do <c>servidor.txt</c> ao lado do executável (<c>jogo = host:porta</c> e, se
    /// precisar, <c>api = https://...</c>); o <c>-server</c> e o <c>-api</c> da linha de comando passam na frente.
    /// </summary>
    public static class GameMode
    {
        public static bool IsOnline { get; private set; }
        public static bool ChoiceMade { get; private set; }
        public static string Host { get; private set; } = "127.0.0.1";
        public static int Port { get; private set; } = Protocol.DefaultPort;
        public static string PlayerName { get; private set; } = "Aventureiro";

        /// <summary>URL da API de login no lugar da regra padrão (<see cref="ServerAddress.ApiUrl"/>); vazio = regra.</summary>
        public static string ApiOverride { get; private set; } = "";

        /// <summary>Nome do arquivo com o servidor padrão, ao lado do executável (na pasta Client/ no Editor).</summary>
        public const string ServerFileName = "servidor.txt";

        /// <summary>Token da API de login e personagem escolhido (vazio/0 = entra como convidado).</summary>
        public static string Token { get; private set; } = "";
        public static int CharacterId { get; private set; }

        /// <summary>Login automático da linha de comando (<c>-autologin conta senha</c>), para testes.</summary>
        public static string AutoLoginAccount { get; private set; } = "";
        public static string AutoLoginPassword { get; private set; } = "";

        public static void SetOffline()
        {
            IsOnline = false;
            ChoiceMade = true;
        }

        /// <summary>Entra como convidado (o servidor precisa estar com --guests).</summary>
        public static void SetOnline(string host, int port, string name)
        {
            IsOnline = true;
            ChoiceMade = true;
            Host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            Port = port;
            PlayerName = string.IsNullOrWhiteSpace(name) ? "Aventureiro" : name.Trim();
            Token = "";
            CharacterId = 0;
        }

        /// <summary>Entra com conta, no personagem escolhido.</summary>
        public static void SetOnlineCharacter(string host, int port, string token, int characterId, string characterName)
        {
            SetOnline(host, port, characterName);
            Token = token;
            CharacterId = characterId;
        }

        /// <summary>Endereço para o menu: só define onde conectar, sem escolher o modo ainda.</summary>
        public static void SetServer(string host, int port)
        {
            Host = host;
            Port = port;
        }

        /// <summary>Tenta ler "host:porta" (porta opcional).</summary>
        public static bool TryParseAddress(string address, out string host, out int port)
        {
            host = "127.0.0.1";
            port = Protocol.DefaultPort;
            if (string.IsNullOrWhiteSpace(address)) return false;
            var parts = address.Trim().Split(':');
            host = parts[0];
            return parts.Length == 1 || (parts.Length == 2 && int.TryParse(parts[1], out port) && port > 0 && port < 65536);
        }

        /// <summary>Lê o servidor.txt (linhas <c>chave = valor</c>; <c>#</c> comenta). Sem o arquivo, fica o 127.0.0.1.</summary>
        private static void ReadServerFile()
        {
            try
            {
                var path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", ServerFileName);
                if (!File.Exists(path)) return;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    var equals = line.IndexOf('=');
                    if (line.StartsWith("#") || equals <= 0) continue;
                    var key = line.Substring(0, equals).Trim().ToLowerInvariant();
                    var value = line.Substring(equals + 1).Trim();
                    if (key == "jogo" && TryParseAddress(value, out var host, out var port)) SetServer(host, port);
                    if (key == "api") ApiOverride = value;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Servidor] Não consegui ler o {ServerFileName}: {ex.Message}");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ReadCommandLine()
        {
            ReadServerFile();
            var args = Environment.GetCommandLineArgs();
            string name = null;
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-name") name = args[i + 1];
            }
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "-autologin" && i + 2 < args.Length)
                {
                    AutoLoginAccount = args[i + 1];
                    AutoLoginPassword = args[i + 2];
                }
                if (args[i] == "-server" && i + 1 < args.Length && TryParseAddress(args[i + 1], out var serverHost, out var serverPort))
                    SetServer(serverHost, serverPort);
                if (args[i] == "-api" && i + 1 < args.Length) ApiOverride = args[i + 1];
                if (args[i] == "-offline") SetOffline();
                if (args[i] == "-connect" && i + 1 < args.Length && TryParseAddress(args[i + 1], out var host, out var port))
                    SetOnline(host, port, name);
            }
            // A demo automática offline não deve parar no menu.
            if (!ChoiceMade && Array.IndexOf(args, "-autodemo") >= 0) SetOffline();
        }
    }
}
