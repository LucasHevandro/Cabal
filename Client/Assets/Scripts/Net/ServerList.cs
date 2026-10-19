using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Game.Shared.Net;
using UnityEngine;

namespace Game.Client.Net
{
    public enum ServerStatus
    {
        Checking,
        Online,

        /// <summary>Respondeu, mas diz que está iniciando ou travado.</summary>
        Unstable,

        /// <summary>No ar, mas com outra versão do protocolo (precisa atualizar o jogo).</summary>
        OtherVersion,
        Offline,
    }

    /// <summary>Um servidor da lista da tela de entrada, com o último status lido do <c>/api/health</c> dele.</summary>
    public sealed class ServerEntry
    {
        public string Name = "";
        public string Host = "127.0.0.1";
        public int Port = Protocol.DefaultPort;

        /// <summary>URL da API de login quando não segue a regra padrão (vazio = regra).</summary>
        public string Api = "";

        public ServerStatus Status = ServerStatus.Checking;
        public int Players;
        public int PingMs;
        public float CheckedAt = float.NegativeInfinity;
        public bool Checking;

        /// <summary>Consultas já feitas (a primeira mede também o aquecimento da conexão, então sai mais lenta).</summary>
        public int Checks;

        public string Address => $"{Host}:{Port}";
        public string ApiUrl => (Api.Length > 0 ? Api : ServerAddress.ApiUrl(Host, Port)).TrimEnd('/');
    }

    /// <summary>
    /// Servidores que o jogador pode escolher na tela de entrada, em vez de digitar o endereço. Vêm do
    /// <c>servidor.txt</c> (linhas <c>servidor = Nome | host:porta | api opcional</c>) e, se ele tiver
    /// <c>lista = https://...</c>, também de uma lista no mesmo formato baixada ao abrir o jogo (dá para abrir um
    /// servidor novo sem mandar outro cliente). O status de cada um é o <c>/api/health</c> do próprio servidor.
    /// </summary>
    public static class ServerList
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };

        public static readonly List<ServerEntry> Entries = new List<ServerEntry>();

        /// <summary>Lista na internet (vazio = só a do arquivo).</summary>
        public static string RemoteListUrl { get; set; } = "";

        [Serializable]
        private sealed class HealthDto
        {
            public string status = "";
            public int players;
            public string protocol = "";
        }

        /// <summary>Lê "Nome | host:porta | api" (o nome e a api são opcionais).</summary>
        public static bool TryParse(string value, out ServerEntry entry)
        {
            entry = null;
            var parts = value.Split('|');
            var address = parts.Length == 1 ? parts[0] : parts[1];
            if (!GameMode.TryParseAddress(address, out var host, out var port)) return false;
            entry = new ServerEntry
            {
                Name = parts.Length > 1 && parts[0].Trim().Length > 0 ? parts[0].Trim() : host,
                Host = host.Trim(),
                Port = port,
                Api = parts.Length > 2 ? parts[2].Trim() : "",
            };
            return true;
        }

        /// <summary>Acrescenta (o mesmo endereço não entra duas vezes: a primeira vez vale).</summary>
        public static ServerEntry Add(ServerEntry entry)
        {
            foreach (var existing in Entries)
                if (existing.Address == entry.Address) return existing;
            Entries.Add(entry);
            return entry;
        }

        /// <summary>Linhas no formato do servidor.txt: só as "servidor = ..." contam.</summary>
        public static void AddFromText(string text)
        {
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                var equals = line.IndexOf('=');
                if (line.StartsWith("#") || equals <= 0) continue;
                if (line.Substring(0, equals).Trim().ToLowerInvariant() != "servidor") continue;
                if (TryParse(line.Substring(equals + 1).Trim(), out var entry)) Add(entry);
            }
        }

        /// <summary>Baixa a lista da internet (se houver) e junta à do arquivo.</summary>
        public static async Task LoadRemoteAsync()
        {
            if (string.IsNullOrEmpty(RemoteListUrl)) return;
            try
            {
                AddFromText(await Http.GetStringAsync(RemoteListUrl));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Servidores] Não consegui baixar a lista {RemoteListUrl}: {ex.Message}");
            }
        }

        /// <summary>Pergunta ao servidor como ele está (status, jogadores, versão) e mede o tempo de resposta.</summary>
        public static async Task RefreshAsync(ServerEntry entry)
        {
            if (entry.Checking) return;
            entry.Checking = true;
            var clock = Stopwatch.StartNew();
            try
            {
                using var response = await Http.GetAsync(entry.ApiUrl + "/health");
                var body = await response.Content.ReadAsStringAsync();
                entry.PingMs = (int)clock.ElapsedMilliseconds;
                var health = JsonUtility.FromJson<HealthDto>(body);
                entry.Players = health?.players ?? 0;
                entry.Status = health == null || !response.IsSuccessStatusCode ? ServerStatus.Unstable
                    : !string.IsNullOrEmpty(health.protocol) && health.protocol != Protocol.Version.ToString() ? ServerStatus.OtherVersion
                    : ServerStatus.Online;
            }
            catch (Exception)
            {
                entry.Status = ServerStatus.Offline;
            }
            finally
            {
                entry.Checking = false;
                entry.Checks++;
                entry.CheckedAt = Time.unscaledTime;
            }
        }
    }
}
