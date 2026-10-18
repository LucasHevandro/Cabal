using System.Net;
using System.Text.Json;
using Game.Server.Accounts;
using Game.Server.Net;
using Game.Server.Persistence;
using Game.Shared.Net;

namespace Game.Server.Tests;

/// <summary>Monitoramento: /api/health e /metrics (Fase 8: infraestrutura).</summary>
public class HealthTests
{
    [Fact]
    public async Task Saude_responde_503_ate_o_servidor_ligar_e_200_com_ele_rodando()
    {
        var auth = new AuthService(new InMemoryGameStore(), new PasswordHasher(1000), new SessionTokens());
        await using var api = new LoginApi(auth, port: 0);
        await api.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(api.Address) };

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.GetAsync("/api/health")).StatusCode);

        using var host = new ServerHost(new ServerOptions { Port = 0, AllowGuests = true });
        api.Health = () => host.Health;
        host.Start();
        await Task.Delay(1500); // o loop atualiza a saúde uma vez por segundo

        var response = await http.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("monsters").GetInt32() > 0);
        Assert.InRange(json.RootElement.GetProperty("ticksPerSecond").GetDouble(), 15, 25);
        Assert.Equal(Protocol.Version.ToString(), json.RootElement.GetProperty("protocol").GetString());

        var metrics = await http.GetStringAsync("/metrics");
        Assert.Contains("game_up 1", metrics);
        Assert.Contains("game_players 0", metrics);
        Assert.Contains("game_errors_total 0", metrics);
    }

    [Fact]
    public void Loop_parado_ha_mais_de_5_s_nao_esta_saudavel()
    {
        var running = new HealthSnapshot(true, 3, 10, 3, 60, 20, 0.5, 2, 0, 50, 0.2, "11");
        Assert.True(LoginApi.IsHealthy(running));
        Assert.False(LoginApi.IsHealthy(running with { SecondsSinceTick = 8 }));
        Assert.False(LoginApi.IsHealthy(running with { Running = false }));
    }
}
