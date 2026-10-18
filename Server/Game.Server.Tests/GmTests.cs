using Game.Server.Accounts;
using Game.Server.Persistence;
using Game.Server.World;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Net;
using Game.Shared.Social;
using Game.Shared.World;

namespace Game.Server.Tests;

/// <summary>Ferramentas de GM, silêncio, banimento e detecção de bot (Fase 8: segurança).</summary>
public class GmTests
{
    private sealed class FakeOutbox : IOutbox
    {
        public List<(int PlayerId, INetMessage Message)> Sent { get; } = new();
        public void Send(int playerId, INetMessage message) => Sent.Add((playerId, message));

        public List<string> System(int playerId) =>
            Sent.Where(s => s.PlayerId == playerId).Select(s => s.Message).OfType<ChatMessage>()
                .Where(m => m.Channel == ChatChannel.System).Select(m => m.SystemText.Key).ToList();
    }

    private static (GameWorld World, FakeOutbox Outbox, ServerPlayer Gm, ServerPlayer Ana) Create()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, Array.Empty<SpawnDefinition>());
        var gm = world.AddPlayer("Mestre", 0, character: AuthService.InitialCharacter() with { Id = 1, AccountId = 10, Name = "Mestre", IsGm = true });
        var ana = world.AddPlayer("Ana", 0, character: AuthService.InitialCharacter() with { Id = 2, AccountId = 20, Name = "Ana" });
        return (world, outbox, gm, ana);
    }

    [Fact]
    public void Quem_nao_e_gm_nem_ve_os_comandos()
    {
        var (world, outbox, _, ana) = Create();
        var actions = new List<GmActionRecord>();
        world.GmActionDone += actions.Add;

        world.Handle(ana.Id, new GmCommand { Text = "dar Ana nucleo_alto 99" }, 1);

        Assert.Contains("chat.unknown", outbox.System(ana.Id));
        Assert.Equal(0, ana.Bag.Count("nucleo_alto"));
        Assert.Empty(actions);
    }

    [Fact]
    public void Gm_da_e_tira_itens_e_alz_com_registro()
    {
        var (world, _, gm, ana) = Create();
        var actions = new List<GmActionRecord>();
        world.GmActionDone += actions.Add;

        world.Handle(gm.Id, new GmCommand { Text = "dar ana nucleo_alto 3" }, 1);
        Assert.Equal(3, ana.Bag.Count("nucleo_alto"));
        world.Handle(gm.Id, new GmCommand { Text = "tirar Ana nucleo_alto 2" }, 1);
        Assert.Equal(1, ana.Bag.Count("nucleo_alto"));

        world.Handle(gm.Id, new GmCommand { Text = "alz Ana 1000" }, 1);
        Assert.Equal(1000, ana.Alz);
        world.Handle(gm.Id, new GmCommand { Text = "alz Ana -5000" }, 1);
        Assert.Equal(0, ana.Alz); // nunca fica negativo

        Assert.Equal(new[] { "dar", "tirar", "alz", "alz" }, actions.Select(a => a.Action));
        Assert.All(actions, a => Assert.Equal(("Mestre", "Ana"), (a.GmName, a.Target)));
        // As mudanças também ficam no registro de itens da Ana (auditoria e suporte).
        Assert.Contains(ana.PendingLogs, l => l.Kind == "gm_dar" && l.ItemId == "nucleo_alto" && l.Quantity == 3);
        Assert.Contains(ana.PendingLogs, l => l.Kind == "gm_tirar" && l.Quantity == -2);
        Assert.Contains(ana.PendingLogs, l => l.Kind == "gm_alz" && l.AlzDelta == -1000);
    }

    [Fact]
    public void Gm_teleporta_para_o_jogador_e_traz_o_jogador()
    {
        var (world, outbox, gm, ana) = Create();
        ana.Movement.Teleport(30, 40, 0);

        world.Handle(gm.Id, new GmCommand { Text = "ir Ana" }, 1);
        Assert.InRange(MathF.Abs(gm.X - 31.5f) + MathF.Abs(gm.Z - 40), 0, 0.01f);
        Assert.Contains(outbox.Sent, s => s.PlayerId == gm.Id && s.Message is PositionCorrection);

        gm.Movement.Teleport(0, 0, 2);
        world.Handle(gm.Id, new GmCommand { Text = "trazer Ana" }, 2);
        Assert.InRange(MathF.Abs(ana.X - 1.5f) + MathF.Abs(ana.Z), 0, 0.01f);
        Assert.Contains("gm.brought", outbox.System(ana.Id));
    }

    [Fact]
    public void Silenciado_nao_fala_no_chat_ate_o_gm_liberar()
    {
        var (world, outbox, gm, ana) = Create();
        var requests = new List<ModerationRequest>();
        world.ModerationRequested += requests.Add;

        world.Handle(gm.Id, new GmCommand { Text = "mute Ana 10 xingou" }, 1);
        world.Handle(ana.Id, new ChatSend { Channel = ChatChannel.Map, Text = "oi" }, 2);
        Assert.Contains("chat.muted", outbox.System(ana.Id));
        Assert.DoesNotContain(outbox.Sent, s => s.Message is ChatMessage { Channel: ChatChannel.Map, Text: "oi" });
        Assert.Contains(requests, r => r.Kind == ModerationKind.Mute && r.AccountId == 20);

        world.Handle(gm.Id, new GmCommand { Text = "unmute Ana" }, 3);
        world.Handle(ana.Id, new ChatSend { Channel = ChatChannel.Map, Text = "voltei" }, 4);
        Assert.Contains(outbox.Sent, s => s.Message is ChatMessage { Channel: ChatChannel.Map, Text: "voltei" });
    }

    [Fact]
    public void Ban_derruba_quem_esta_online_e_pede_para_gravar()
    {
        var (world, _, gm, ana) = Create();
        var kicks = new List<int>();
        var requests = new List<ModerationRequest>();
        world.KickRequested += (id, _) => kicks.Add(id);
        world.ModerationRequested += requests.Add;

        world.Handle(gm.Id, new GmCommand { Text = "ban Ana 24 usou bot" }, 1);

        Assert.Equal(new[] { ana.Id }, kicks);
        var ban = Assert.Single(requests);
        Assert.Equal((ModerationKind.Ban, 20, "usou bot"), (ban.Kind, ban.AccountId, ban.Reason));
        Assert.InRange((ban.Until!.Value - DateTimeOffset.UtcNow).TotalHours, 23.9, 24.1);

        // Offline também: vai para o banco pelo nome (o Program procura a conta).
        world.Handle(gm.Id, new GmCommand { Text = "ban Fulano 0 golpe" }, 2);
        Assert.Contains(requests, r => r.CharacterName == "Fulano" && r.AccountId == 0 && r.Until > DateTimeOffset.UtcNow.AddYears(50));
    }

    [Fact]
    public async Task Conta_banida_nao_entra_ate_o_banimento_acabar()
    {
        var store = new InMemoryGameStore();
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        var auth = new AuthService(store, new PasswordHasher(1000), new SessionTokens(TimeSpan.FromHours(12), () => now), () => now,
            gmAccounts: new[] { "mestre" });
        await auth.RegisterAsync(new Credentials { Account = "anabela", Password = "senha123" });
        var login = await auth.LoginAsync(new Credentials { Account = "anabela", Password = "senha123" });
        var created = await auth.CreateCharacterAsync(login.Value!.Token, new CreateCharacterRequest { Name = "Ana", ClassId = 0 });
        var account = (await store.FindAccountAsync("anabela"))!;

        await store.SetBanAsync(account.Id, now.AddHours(2), "teste");
        var refused = await auth.LoginAsync(new Credentials { Account = "anabela", Password = "senha123" });
        Assert.Equal((false, 403, "auth.banned_until"), (refused.Ok, refused.StatusCode, refused.Error.Key));
        // Quem já tinha token também não entra no jogo.
        var resolve = await auth.ResolveCharacterAsync(login.Value.Token, created.Value!.Id);
        Assert.Equal((false, "auth.banned_until"), (resolve.Ok, resolve.Error.Key));

        now = now.AddHours(3);
        var again = await auth.LoginAsync(new Credentials { Account = "anabela", Password = "senha123" });
        Assert.True(again.Ok);
        var character = await auth.ResolveCharacterAsync(again.Value!.Token, created.Value.Id);
        Assert.True(character.Ok);
        Assert.False(character.Value!.IsGm);

        // GM pela opção --gm (sem a marca no banco).
        await auth.RegisterAsync(new Credentials { Account = "mestre", Password = "senha123" });
        var gmLogin = await auth.LoginAsync(new Credentials { Account = "mestre", Password = "senha123" });
        var gmCharacter = await auth.CreateCharacterAsync(gmLogin.Value!.Token, new CreateCharacterRequest { Name = "Mestre", ClassId = 0 });
        Assert.True((await auth.ResolveCharacterAsync(gmLogin.Value.Token, gmCharacter.Value!.Id)).Value!.IsGm);
    }

    [Fact]
    public void Combo_perfeito_demais_vira_suspeita_sem_punir()
    {
        var (world, _, _, ana) = Create();
        var raised = new List<string>();
        world.SuspicionRaised += (_, reason) => raised.Add(reason);

        for (var i = 0; i < GameWorld.SuspiciousExcellentStreak - 1; i++) world.TrackComboGrade(ana, ComboGrade.Excellent, i);
        world.TrackComboGrade(ana, ComboGrade.Good, 200); // um Good zera a sequência
        for (var i = 0; i < GameWorld.SuspiciousExcellentStreak - 1; i++) world.TrackComboGrade(ana, ComboGrade.Excellent, 300 + i);
        Assert.Empty(raised);

        world.TrackComboGrade(ana, ComboGrade.Excellent, 500);
        Assert.Equal(new[] { "suspect.perfect_combo" }, raised);
        world.TrackComboGrade(ana, ComboGrade.Excellent, 501);
        Assert.Single(raised); // marca uma vez só
        Assert.Contains(world.Players, p => p == ana); // continua jogando: só o GM decide
    }

    [Fact]
    public void Rota_repetida_e_horas_seguidas_viram_suspeita()
    {
        var (world, _, _, ana) = Create();
        var raised = new List<string>();
        world.SuspicionRaised += (_, reason) => raised.Add(reason);

        // Um caminho de 30 pontos, sempre igual, repetido por 5 janelas de 5 minutos.
        var now = ana.NextRouteSampleAt;
        for (var lap = 0; lap < 5; lap++)
        {
            for (var step = 0; step < GameWorld.RouteWindowSamples; step++)
            {
                ana.Movement.Teleport(step * 4.5f, 30, now);
                world.TrackSuspicion(ana, now);
                now += GameWorld.RouteSampleSeconds;
            }
        }
        Assert.Contains("suspect.repeated_route", raised);

        ana.JoinedAt = now - GameWorld.SuspiciousHoursOnline * 3600 - 1;
        world.TrackSuspicion(ana, now);
        Assert.Contains("suspect.hours_online", raised);
    }

    [Fact]
    public void Caminho_variado_nao_e_suspeito()
    {
        var (world, _, _, ana) = Create();
        var raised = new List<string>();
        world.SuspicionRaised += (_, reason) => raised.Add(reason);
        var random = new Random(4);
        var now = ana.NextRouteSampleAt;
        for (var i = 0; i < GameWorld.RouteWindowSamples * 8; i++)
        {
            ana.Movement.Teleport(random.Next(-40, 40), random.Next(0, 80), now);
            world.TrackSuspicion(ana, now);
            now += GameWorld.RouteSampleSeconds;
        }
        Assert.Empty(raised);
    }
}
