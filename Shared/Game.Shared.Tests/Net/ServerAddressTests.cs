using Game.Shared.Net;

namespace Game.Shared.Tests.Net;

public class ServerAddressTests
{
    [Theory]
    [InlineData("127.0.0.1", 7777, "http://127.0.0.1:7778/api")]
    [InlineData("localhost", 9000, "http://localhost:9001/api")]
    [InlineData("192.168.0.20", 7777, "http://192.168.0.20:7778/api")]
    [InlineData("jogo.exemplo.com.br", 7777, "https://jogo.exemplo.com.br/api")]
    public void Ip_usa_http_na_porta_seguinte_e_dominio_usa_https(string host, int port, string expected)
    {
        Assert.Equal(expected, ServerAddress.ApiUrl(host, port));
    }
}
