using Game.Server.Accounts;
using Game.Server.Persistence;
using Game.Shared.Accounts;
using Game.Shared.Items;
using Game.Shared.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Game.Server.Tests;

/// <summary>
/// Testes do banco PostgreSQL de verdade. Só rodam com a variável GAME_DB_TEST apontando para um banco de teste
/// (ex.: <c>Host=localhost;Port=5432;Database=game_tests;Username=game;Password=game_dev_local</c>);
/// sem ela passam sem fazer nada, para o <c>dotnet test</c> funcionar sem Docker.
/// O banco de teste é apagado e recriado a cada teste.
/// </summary>
public class PostgresStoreTests
{
    private static readonly string? Connection = Environment.GetEnvironmentVariable("GAME_DB_TEST");

    private static async Task<EfGameStore?> FreshStoreAsync()
    {
        if (string.IsNullOrEmpty(Connection)) return null;
        if (!Connection.Contains("test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("GAME_DB_TEST precisa ser um banco de teste (o nome deve conter 'test').");

        var options = new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(Connection).Options;
        await using (var db = new GameDbContext(options)) await db.Database.EnsureDeletedAsync();
        var store = new EfGameStore(new PooledDbContextFactory<GameDbContext>(options));
        await store.MigrateAsync();
        return store;
    }

    [Fact]
    public async Task Contas_e_nomes_unicos_no_banco_real()
    {
        var store = await FreshStoreAsync();
        if (store == null) return;

        var account = await store.CreateAccountAsync("lucas", "hash");
        await Assert.ThrowsAsync<StoreConflictException>(() => store.CreateAccountAsync("lucas", "outro"));

        await store.CreateCharacterAsync(account.Id, "Ação", CharacterClass.Blade, AuthService.InitialCharacter());
        await Assert.ThrowsAsync<StoreConflictException>(() =>
            store.CreateCharacterAsync(account.Id, "acao", CharacterClass.Arcanist, AuthService.InitialCharacter()));
    }

    [Fact]
    public async Task Limite_de_tres_personagens_no_banco_real()
    {
        var store = await FreshStoreAsync();
        if (store == null) return;
        var account = await store.CreateAccountAsync("lucas", "hash");

        foreach (var name in new[] { "Ana", "Bia", "Caio" })
            await store.CreateCharacterAsync(account.Id, name, CharacterClass.Blade, AuthService.InitialCharacter());

        await Assert.ThrowsAsync<StoreConflictException>(() =>
            store.CreateCharacterAsync(account.Id, "Duda", CharacterClass.Blade, AuthService.InitialCharacter()));
        Assert.Equal(3, (await store.ListCharactersAsync(account.Id)).Count);
    }

    [Fact]
    public async Task Salvar_e_carregar_estado_inventario_e_registros()
    {
        var store = await FreshStoreAsync();
        if (store == null) return;
        var account = await store.CreateAccountAsync("lucas", "hash");
        var character = await store.CreateCharacterAsync(account.Id, "Ana", CharacterClass.Blade, AuthService.InitialCharacter());
        var uid = await store.MaxItemUidAsync() + 1;

        await store.SaveCharacterAsync(new CharacterSnapshot(character.Id, 7, 120, 5000, 12.5f, 40f, 900, 300, 1500,
                new[]
                {
                    new ItemRecord(uid, ItemContainerKind.Bag, 0, "pelo_macio", 3),
                    new ItemRecord(uid + 1, ItemContainerKind.Bag, 1, "nucleo_baixo", 1),
                    new ItemRecord(uid + 2, ItemContainerKind.Equipment, 0, "espada_ferro", 1, Upgrade: 6, PityFails: 2, PityLevel: 6),
                },
                Attributes: new Attributes { Strength = 15, Dexterity = 6, Intelligence = 2 }, AwakeningGauge: 40,
                Quests: new[] { new QuestRecord("primeiros_passos", true, Array.Empty<int>()), new QuestRecord("arte_do_combo", false, new[] { 4 }) }),
            new[] { new ItemLogEntry(character.Id, DateTimeOffset.UtcNow, "coleta", "pelo_macio", 3, 0, "teste") });
        // Segundo salvamento: um item some, outro muda de quantidade.
        await store.SaveCharacterAsync(new CharacterSnapshot(character.Id, 8, 10, 5100, 13f, 41f, 950, 320, 1400,
                new[]
                {
                    new ItemRecord(uid, ItemContainerKind.Bag, 5, "pelo_macio", 5),
                    new ItemRecord(uid + 2, ItemContainerKind.Equipment, 0, "espada_ferro", 1, Upgrade: 7),
                },
                Quests: new[] { new QuestRecord("primeiros_passos", true, Array.Empty<int>()), new QuestRecord("arte_do_combo", false, new[] { 9 }) }),
            new[] { new ItemLogEntry(character.Id, DateTimeOffset.UtcNow, "alz_monstro", null, 0, 100, "teste") });

        var loaded = await store.LoadCharacterAsync(character.Id);
        Assert.Equal((8, 10, 5100L, 13f, 950), (loaded!.Level, loaded.Xp, loaded.Alz, loaded.X, loaded.Hp));
        Assert.Equal(2, loaded.Items.Count);
        var pelt = loaded.Items.Single(i => i.Uid == uid);
        Assert.Equal(("pelo_macio", 5, 5), (pelt.ItemId, pelt.Quantity, pelt.Slot)); // mudou de posição e quantidade
        Assert.Equal(7, loaded.Items.Single(i => i.Uid == uid + 2).Upgrade);
        Assert.Equal(9, loaded.Quests.Single(q => q.QuestId == "arte_do_combo").Counts[0]);
        Assert.True(loaded.Quests.Single(q => q.QuestId == "primeiros_passos").Completed);
        Assert.True(await store.MaxItemUidAsync() >= uid + 2);
        Assert.Equal(2, (await store.ItemLogAsync(character.Id)).Count);
    }

    [Fact]
    public async Task Fluxo_completo_de_conta_no_banco_real()
    {
        var store = await FreshStoreAsync();
        if (store == null) return;
        var auth = new AuthService(store, new PasswordHasher(1000), new SessionTokens());

        Assert.True((await auth.RegisterAsync(new Credentials { Account = "lucas", Password = "senha123" })).Ok);
        var token = (await auth.LoginAsync(new Credentials { Account = "lucas", Password = "senha123" })).Value!.Token;
        var created = await auth.CreateCharacterAsync(token, new CreateCharacterRequest { Name = "Guerreira", ClassId = 0 });

        Assert.True(created.Ok);
        Assert.True((await auth.ResolveCharacterAsync(token, created.Value!.Id)).Ok);
    }
}
