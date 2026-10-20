using System.Net;
using Game.Server.Net;
using Microsoft.AspNetCore.Http;

namespace Game.Server.Tests;

/// <summary>De onde vem o IP do limite de contas: o do Caddy só vale vindo da rede interna.</summary>
public class ClientIpTests
{
    private static HttpRequest Request(string remote, string? forwarded = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (forwarded != null) context.Request.Headers["X-Forwarded-For"] = forwarded;
        return context.Request;
    }

    [Fact]
    public void Atras_do_proxy_interno_vale_o_ip_do_cabecalho() =>
        Assert.Equal("200.1.2.3", LoginApi.ClientIp(Request("172.18.0.5", "200.1.2.3")));

    [Fact]
    public void Com_varios_vale_o_ultimo_que_o_proxy_acrescentou() =>
        Assert.Equal("200.1.2.3", LoginApi.ClientIp(Request("172.18.0.5", "1.1.1.1, 200.1.2.3")));

    [Fact]
    public void De_endereco_publico_o_cabecalho_e_ignorado() =>
        Assert.Equal("200.9.9.9", LoginApi.ClientIp(Request("200.9.9.9", "1.1.1.1")));

    [Fact]
    public void Sem_cabecalho_vale_quem_conectou() =>
        Assert.Equal("127.0.0.1", LoginApi.ClientIp(Request("127.0.0.1")));
}
