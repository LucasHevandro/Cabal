using System.Collections.Concurrent;
using Game.Server.Persistence;
using Game.Shared.Accounts;
using Game.Shared.Items;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.Text;
using Game.Shared.World;

namespace Game.Server.Accounts;

/// <summary>Resultado de uma operação de conta: o valor ou a mensagem de erro com o código HTTP correspondente.</summary>
public readonly record struct AuthResult<T>(bool Ok, T? Value, LocalText Error, int StatusCode)
{
    public static AuthResult<T> Success(T value) => new(true, value, LocalText.Empty, 200);
    public static AuthResult<T> Fail(LocalText error, int status = 400) => new(false, default, error, status);
}

/// <summary>
/// Contas, login e personagens. Usada pela API HTTP (cadastro, login, lista e criação de personagens) e pelo
/// servidor do jogo (conferir o token e o personagem do Hello).
/// </summary>
public sealed class AuthService
{
    /// <summary>Tentativas erradas seguidas antes de travar a conta.</summary>
    public const int MaxFailedLogins = 5;

    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private static readonly LocalText WrongCredentials = new("auth.wrong_credentials");

    private readonly IGameStore _store;
    private readonly PasswordHasher _hasher;
    private readonly SessionTokens _tokens;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset LockedUntil)> _throttle = new();
    private readonly string _dummyHash;

    /// <summary>Ids únicos de item do servidor (o mundo usa o mesmo contador).</summary>
    public ItemUidAllocator Uids { get; }

    public AuthService(IGameStore store, PasswordHasher hasher, SessionTokens tokens, Func<DateTimeOffset>? now = null,
        ItemUidAllocator? uids = null)
    {
        Uids = uids ?? new ItemUidAllocator();
        _store = store;
        _hasher = hasher;
        _tokens = tokens;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        // Usado quando a conta não existe, para o tempo de resposta ser igual ao de senha errada.
        _dummyHash = hasher.Hash("conta-inexistente-123");
    }

    public SessionTokens Tokens => _tokens;

    public async Task<AuthResult<string>> RegisterAsync(Credentials credentials)
    {
        var account = AccountRules.NormalizeAccount(credentials.Account);
        var error = AccountRules.ValidateAccount(account) ?? AccountRules.ValidatePassword(credentials.Password);
        if (error != null) return AuthResult<string>.Fail(error);

        try
        {
            await _store.CreateAccountAsync(account, _hasher.Hash(credentials.Password));
            return AuthResult<string>.Success(account);
        }
        catch (StoreConflictException ex)
        {
            return AuthResult<string>.Fail(ex.Text, 409);
        }
    }

    public async Task<AuthResult<LoginResponse>> LoginAsync(Credentials credentials)
    {
        var name = AccountRules.NormalizeAccount(credentials.Account);
        var now = _now();
        if (_throttle.TryGetValue(name, out var state) && now < state.LockedUntil)
        {
            var minutes = Math.Ceiling((state.LockedUntil - now).TotalMinutes);
            return AuthResult<LoginResponse>.Fail(new LocalText("auth.locked", minutes), 429);
        }

        var account = await _store.FindAccountAsync(name);
        var valid = _hasher.Verify(credentials.Password ?? "", account?.PasswordHash ?? _dummyHash) && account != null;
        if (!valid)
        {
            RegisterFailure(name, now);
            return AuthResult<LoginResponse>.Fail(WrongCredentials, 401);
        }

        _throttle.TryRemove(name, out _);
        var rehash = _hasher.NeedsRehash(account!.PasswordHash) ? _hasher.Hash(credentials.Password!) : null;
        await _store.UpdateLoginAsync(account.Id, rehash);
        return AuthResult<LoginResponse>.Success(new LoginResponse
        {
            Token = _tokens.Create(account.Id), ExpiresInSeconds = (int)_tokens.Lifetime.TotalSeconds,
        });
    }

    public async Task<AuthResult<CharacterList>> ListCharactersAsync(string? token)
    {
        if (_tokens.Validate(token) is not { } accountId) return AuthResult<CharacterList>.Fail(new LocalText("auth.session_expired"), 401);
        var list = new CharacterList();
        foreach (var c in await _store.ListCharactersAsync(accountId))
            list.Characters.Add(new CharacterSummary { Id = c.Id, Name = c.Name, ClassId = (int)c.Class, Level = c.Level });
        return AuthResult<CharacterList>.Success(list);
    }

    public async Task<AuthResult<CharacterSummary>> CreateCharacterAsync(string? token, CreateCharacterRequest request)
    {
        if (_tokens.Validate(token) is not { } accountId) return AuthResult<CharacterSummary>.Fail(new LocalText("auth.session_expired"), 401);
        var error = AccountRules.ValidateCharacterName(request.Name);
        if (error != null) return AuthResult<CharacterSummary>.Fail(error);
        if (!Enum.IsDefined(typeof(CharacterClass), request.ClassId)) return AuthResult<CharacterSummary>.Fail(new LocalText("auth.bad_class"));

        try
        {
            var created = await _store.CreateCharacterAsync(accountId, request.Name, (CharacterClass)request.ClassId,
                InitialCharacter((CharacterClass)request.ClassId, Uids.Next));
            return AuthResult<CharacterSummary>.Success(new CharacterSummary
            {
                Id = created.Id, Name = created.Name, ClassId = (int)created.Class, Level = created.Level,
            });
        }
        catch (StoreConflictException ex)
        {
            return AuthResult<CharacterSummary>.Fail(ex.Text, 409);
        }
    }

    /// <summary>Para o Hello do jogo: o personagem pedido, se o token for válido e o personagem for da conta.</summary>
    public async Task<AuthResult<CharacterRecord>> ResolveCharacterAsync(string? token, int characterId)
    {
        if (_tokens.Validate(token) is not { } accountId) return AuthResult<CharacterRecord>.Fail(new LocalText("auth.session_expired"), 401);
        var character = await _store.LoadCharacterAsync(characterId);
        if (character == null || character.AccountId != accountId) return AuthResult<CharacterRecord>.Fail(new LocalText("auth.character_not_found"), 404);
        return AuthResult<CharacterRecord>.Success(character);
    }

    /// <summary>Valores de um personagem novo: nível 1 com a vida e a mana cheias, na vila.</summary>
    /// <summary>
    /// Personagem novo: atributos iniciais da classe e, com <paramref name="newUid"/>, a arma inicial vestida e
    /// algumas poções na bolsa.
    /// </summary>
    public static CharacterRecord InitialCharacter(CharacterClass characterClass = CharacterClass.Blade, Func<long>? newUid = null)
    {
        var attributes = Attributes.StartingFor(characterClass);
        var stats = StatCalculator.Compute(1, attributes, new Equipment());
        var items = new List<ItemRecord>();
        if (newUid != null)
        {
            var weapon = characterClass == CharacterClass.Arcanist ? "cajado_ferro" : "espada_ferro";
            items.Add(new ItemRecord(newUid(), ItemContainerKind.Equipment, (int)EquipSlot.Weapon, weapon, 1));
            items.Add(new ItemRecord(newUid(), ItemContainerKind.Bag, 0, "pocao_hp_p", 5));
        }
        return new CharacterRecord(0, 0, "", characterClass, 1, 0, 0, WorldContent.PlayerSpawnX, WorldContent.PlayerSpawnZ,
            stats.MaxHp, stats.MaxMp, PlayerRules.StartingSp, items, attributes);
    }

    private void RegisterFailure(string name, DateTimeOffset now)
    {
        _throttle.AddOrUpdate(name,
            _ => (1, DateTimeOffset.MinValue),
            (_, s) =>
            {
                var failures = s.LockedUntil != DateTimeOffset.MinValue && now >= s.LockedUntil ? 1 : s.Failures + 1;
                return failures >= MaxFailedLogins ? (0, now + LockDuration) : (failures, DateTimeOffset.MinValue);
            });
    }
}
