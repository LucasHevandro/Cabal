using System.Net;

namespace Game.Shared.Net
{
    /// <summary>
    /// Onde fica a API de login para um endereço do jogo (<c>host:porta</c>):
    /// - IP ou localhost (desenvolvimento, rede local): HTTP direto na porta do jogo + 1;
    /// - domínio (produção): HTTPS no próprio domínio, pelo Caddy (Docs/deploy.md).
    /// O cliente pode trocar com <c>api = ...</c> no servidor.txt; os bots, com <c>--api</c>.
    /// </summary>
    public static class ServerAddress
    {
        public static string ApiUrl(string host, int gamePort) =>
            IsLocalOrIp(host) ? $"http://{host}:{gamePort + 1}/api" : $"https://{host}/api";

        public static bool IsLocalOrIp(string host) =>
            host == "localhost" || IPAddress.TryParse(host, out _);
    }
}
