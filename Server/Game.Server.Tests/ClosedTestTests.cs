using System.Net;
using Game.Server.Accounts;
using Game.Server.Net;
using Game.Server.Persistence;
using Game.Server.World;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Net;
using Game.Shared.World;

namespace Game.Server.Tests;

/// <summary>Teste fechado (Fase 8): aceite dos termos, exclusão de conta e métricas.</summary>
public class ClosedTestTests
{
    private sealed class NullOutbox : IOutbox
    {
        public void Send(int playerId, INetMessage message) { }
    }

    private static AuthService Auth(IGameStore store, string? terms) =>
        new(store, new PasswordHasher(1000), new SessionTokens(), termsVersion: terms);

    [Fact]
    public async Task Cadastro_exige_o_aceite_e_o_login_pede_de_novo_quando_a_versao_muda()
    {
        var store = new InMemoryGameStore();
        var auth = Auth(store, "v1");
        var refused = await auth.RegisterAsync(new Credentials { Account = "anabela", Password = "senha123" });
        Assert.Equal((false, "auth.terms_required"), (refused.Ok, refused.Error.Key));
        Assert.True((await auth.RegisterAsync(new Credentials { Account = "anabela", Password = "senha123", AcceptTerms = "v1" })).Ok);
        Assert.True((await auth.LoginAsync(new Credentials { Account = "anabela", Password = "senha123" })).Ok);

        // Termos novos: sem o aceite não entra; aceitando, entra e não pergunta mais.
        var updated = Auth(store, "v2");
        var blocked = await updated.LoginAsync(new Credentials { Account = "anabela", Password = "senha123" });
        Assert.Equal((false, 403, "auth.terms_updated"), (blocked.Ok, blocked.StatusCode, blocked.Error.Key));
        Assert.True((await updated.LoginAsync(new Credentials { Account = "anabela", Password = "senha123", AcceptTerms = "v2" })).Ok);
        Assert.True((await updated.LoginAsync(new Credentials { Account = "anabela", Password = "senha123" })).Ok);
        Assert.Equal("v2", (await store.FindAccountAsync("anabela"))!.TermsVersion);

        // Senha errada não revela nada sobre os termos.
        var wrong = await updated.LoginAsync(new Credentials { Account = "anabela", Password = "errada123" });
        Assert.Equal("auth.wrong_credentials", wrong.Error.Key);
    }

    [Fact]
    public async Task Termos_e_politica_saem_pela_api()
    {
        await using var api = new LoginApi(Auth(new InMemoryGameStore(), Legal.Version), port: 0);
        await api.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(api.Address) };

        Assert.Contains("TERMOS DE USO", await http.GetStringAsync("/api/legal/termos"));
        Assert.Contains("LGPD", await http.GetStringAsync("/api/legal/privacidade"));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/legal/..%2Fsegredo")).StatusCode);
        // A versão do texto é a que o servidor exige.
        Assert.Contains("Versão " + Legal.Version, await http.GetStringAsync("/api/legal/termos"));
    }

    [Fact]
    public async Task Excluir_conta_apaga_personagens_registros_e_metricas()
    {
        var store = new InMemoryGameStore();
        var account = await store.CreateAccountAsync("anabela", "hash", "v1");
        var other = await store.CreateAccountAsync("bruna", "hash", "v1");
        var ana = await store.CreateCharacterAsync(account.Id, "Ana", CharacterClass.Blade, AuthService.InitialCharacter());
        var bia = await store.CreateCharacterAsync(other.Id, "Bia", CharacterClass.Blade, AuthService.InitialCharacter());
        foreach (var (who, owner) in new[] { (ana, account), (bia, other) })
        {
            await store.SaveCharacterAsync(new CharacterSnapshot(who.Id, 2, 0, 10, 0, 0, 100, 100, 100, Array.Empty<ItemRecord>())
            {
                Metrics = new[] { new MetricRecord(owner.Id, who.Id, "session", 60, "", DateTimeOffset.UtcNow) },
            }, new[] { new ItemLogEntry(who.Id, DateTimeOffset.UtcNow, "alz_monstro", null, 0, 10, "") });
        }

        Assert.True(await store.DeleteAccountAsync("anabela"));

        Assert.Null(await store.FindAccountAsync("anabela"));
        Assert.Null(await store.LoadCharacterAsync(ana.Id));
        Assert.Empty(await store.ItemLogAsync(ana.Id));
        var metrics = await store.MetricsAsync(DateTimeOffset.MinValue);
        Assert.All(metrics.Events, e => Assert.Equal(other.Id, e.AccountId));
        Assert.Single(metrics.Accounts);
        Assert.NotNull(await store.LoadCharacterAsync(bia.Id)); // os outros ficam
        Assert.False(await store.DeleteAccountAsync("anabela"));
    }

    private static (GameWorld World, ServerPlayer Ana, ServerPlayer Guest) World()
    {
        var world = new GameWorld(new NullOutbox(), Array.Empty<SpawnDefinition>());
        var ana = world.AddPlayer("Ana", 10, character: AuthService.InitialCharacter() with { Id = 7, AccountId = 3, Name = "Ana" });
        var guest = world.AddPlayer("Bot01", 10);
        return (world, ana, guest);
    }

    [Fact]
    public void Sessao_vira_um_evento_ao_sair_e_convidado_nao_conta()
    {
        var (world, ana, guest) = World();
        world.TrackComboGrade(ana, ComboGrade.Excellent, 11);
        world.TrackComboGrade(ana, ComboGrade.Excellent, 12);
        world.TrackComboGrade(ana, ComboGrade.Miss, 13);
        ana.Session.Kills = 5;

        world.EndSession(ana, 130);
        world.EndSession(ana, 200); // uma vez só
        world.EndSession(guest, 130);

        var (snapshot, _) = world.CaptureForSave(ana);
        var session = Assert.Single(snapshot.Metrics);
        Assert.Equal((3, 7, "session", 120.0), (session.AccountId, session.CharacterId, session.Kind, session.Value));
        var details = GameWorld.ParseDetails(session.Details);
        Assert.Equal(("1>1", 5, 2, 1), (details["nivel"], GameWorld.DetailInt(details, "abates"),
            GameWorld.DetailInt(details, "excellent"), GameWorld.DetailInt(details, "miss")));
        Assert.Empty(world.CaptureForSave(ana).Snapshot.Metrics); // já foi para o banco
        Assert.Empty(guest.PendingMetrics);
    }

    [Fact]
    public void Relatorio_do_cliente_e_conferido_e_limitado()
    {
        var (world, ana, guest) = World();
        world.Handle(ana.Id, new ClientReport { Kind = "tutorial", Value = 3, Text = "Dodge" }, 11);
        world.Handle(ana.Id, new ClientReport { Kind = "hack", Value = 1 }, 11);
        world.Handle(ana.Id, new ClientReport { Kind = "fps", Value = float.NaN }, 11);
        world.Handle(ana.Id, new ClientReport { Kind = "error", Text = new string('x', 500) + "\nlinha" }, 11);
        world.Handle(guest.Id, new ClientReport { Kind = "fps", Value = 60 }, 11);

        Assert.Equal(new[] { "client.tutorial", "client.fps", "client.error" }, ana.PendingMetrics.Select(m => m.Kind));
        Assert.Equal(0, ana.PendingMetrics[1].Value);
        Assert.Equal(ClientReportRules.MaxTextLength, ana.PendingMetrics[2].Details.Length);
        Assert.Empty(guest.PendingMetrics);

        for (var i = 0; i < 500; i++) world.Handle(ana.Id, new ClientReport { Kind = "fps", Value = 60 }, 12);
        Assert.Equal(ClientReportRules.MaxPerSession, ana.PendingMetrics.Count);
    }

    [Fact]
    public void Relatorio_resume_retencao_sessoes_tutorial_e_fps()
    {
        var day0 = new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero);
        MetricRecord Session(int account, int days, double minutes, string details = "nivel=1>3;abates=30;excellent=8;good=2;miss=0") =>
            new(account, account * 10, "session", minutes * 60, details, day0.AddDays(days));
        var events = new List<MetricRecord>
        {
            Session(1, 0, 30), Session(1, 1, 60), Session(1, 8, 20), // volta no D1 e no D7
            Session(2, 0, 10),                                         // não volta
            new(1, 10, "client.tutorial", 0, "Move", day0), new(1, 10, "client.tutorial", 1, "Target", day0),
            new(2, 20, "client.tutorial", 0, "Move", day0),
            new(1, 10, "client.fps", 120, "", day0), new(2, 20, "client.fps", 40, "", day0),
        };
        var data = new MetricsData(new[] { (1, day0), (2, day0), (3, day0) }, events);

        var report = MetricsReport.Build(data, DateTimeOffset.MinValue, day0.AddDays(10));

        Assert.Contains("Contas: 3", report);
        Assert.Contains("Jogadores com pelo menos uma sessão: 2", report);
        Assert.Contains("D1 50% (1/2)", report);
        Assert.Contains("D7 50% (1/2)", report);
        Assert.Contains("Sessões: 4", report);
        Assert.Contains("Excellent 80%", report);
        Assert.Matches(@"0 Move\s+2\s+100%", report);
        Assert.Matches(@"1 Target\s+1\s+50%", report);
        Assert.Contains("FPS: mediana 80", report);
    }
}
