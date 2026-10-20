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

    /// <summary>Contas novas que um mesmo IP pode criar em <see cref="AccountWindow"/> (contra quem cria contas sem fim).</summary>
    public const int MaxAccountsPerIp = 3;

    public static readonly TimeSpan AccountWindow = TimeSpan.FromHours(24);

    private static readonly LocalText WrongCredentials = new("auth.wrong_credentials");

    private readonly IGameStore _store;
    private readonly PasswordHasher _hasher;
    private readonly SessionTokens _tokens;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset LockedUntil)> _throttle = new();
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _accountsByIp = new();
    private readonly string _dummyHash;

    /// <summary>Ids únicos de item do servidor (o mundo usa o mesmo contador).</summary>
    public ItemUidAllocator Uids { get; }

    private readonly HashSet<string> _gmAccounts;
    private readonly string? _termsVersion;

    /// <param name="gmAccounts">Contas tratadas como GM mesmo sem a marca no banco (opção --gm, para testes locais).</param>
    /// <param name="termsVersion">
    /// Versão dos termos que o cadastro exige e que o login pede de novo quando muda (o servidor passa
    /// <see cref="Legal.Version"/>); nula não confere (testes das outras regras).
    /// </param>
    public AuthService(IGameStore store, PasswordHasher hasher, SessionTokens tokens, Func<DateTimeOffset>? now = null,
        ItemUidAllocator? uids = null, IEnumerable<string>? gmAccounts = null, string? termsVersion = null)
    {
        _termsVersion = termsVersion;
        _gmAccounts = new HashSet<string>((gmAccounts ?? Array.Empty<string>()).Select(AccountRules.NormalizeAccount));
        Uids = uids ?? new ItemUidAllocator();
        _store = store;
        _hasher = hasher;
        _tokens = tokens;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        // Usado quando a conta não existe, para o tempo de resposta ser igual ao de senha errada.
        _dummyHash = hasher.Hash("conta-inexistente-123");
    }

    public SessionTokens Tokens => _tokens;

    /// <param name="clientIp">IP de quem pede (nulo = sem limite por IP, como nos testes e nas ferramentas locais).</param>
    public async Task<AuthResult<string>> RegisterAsync(Credentials credentials, string? clientIp = null)
    {
        var account = AccountRules.NormalizeAccount(credentials.Account);
        var error = AccountRules.ValidateAccount(account) ?? AccountRules.ValidatePassword(credentials.Password);
        if (error != null) return AuthResult<string>.Fail(error);
        if (_termsVersion != null && credentials.AcceptTerms != _termsVersion) return AuthResult<string>.Fail(new LocalText("auth.terms_required"));
        var now = _now();
        if (clientIp != null && RecentAccounts(clientIp, now) >= MaxAccountsPerIp)
            return AuthResult<string>.Fail(new LocalText("auth.too_many_accounts", MaxAccountsPerIp), 429);

        try
        {
            await _store.CreateAccountAsync(account, _hasher.Hash(credentials.Password), _termsVersion ?? credentials.AcceptTerms ?? "");
            if (clientIp != null)
            {
                var created = _accountsByIp.GetOrAdd(clientIp, _ => new List<DateTimeOffset>());
                lock (created) created.Add(now);
            }
            return AuthResult<string>.Success(account);
        }
        catch (StoreConflictException ex)
        {
            return AuthResult<string>.Fail(ex.Text, 409);
        }
    }

    /// <summary>Quantas contas o IP criou na janela (e esquece as mais velhas).</summary>
    private int RecentAccounts(string ip, DateTimeOffset now)
    {
        if (!_accountsByIp.TryGetValue(ip, out var created)) return 0;
        lock (created)
        {
            created.RemoveAll(at => now - at >= AccountWindow);
            return created.Count;
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
        if (account!.IsBanned(now)) return AuthResult<LoginResponse>.Fail(Banned(account), 403);

        _throttle.TryRemove(name, out _);
        // Termos novos desde o último aceite: o cliente mostra e manda de novo com o aceite.
        if (_termsVersion != null && account.TermsVersion != _termsVersion)
        {
            if (credentials.AcceptTerms != _termsVersion) return AuthResult<LoginResponse>.Fail(new LocalText("auth.terms_updated", _termsVersion), 403);
            await _store.AcceptTermsAsync(account.Id, _termsVersion);
        }
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
            list.Characters.Add(new CharacterSummary
            {
                Id = c.Id, Name = c.Name, ClassId = (int)c.Class, Level = c.Level, Hair = c.Hair, Cloth = c.Cloth,
                ArmorGrade = Game.Shared.Items.Appearance.GradeOf(c.ArmorId, Game.Shared.Items.EquipSlot.Armor),
                GlovesGrade = Game.Shared.Items.Appearance.GradeOf(c.GlovesId, Game.Shared.Items.EquipSlot.Gloves),
                BootsGrade = Game.Shared.Items.Appearance.GradeOf(c.BootsId, Game.Shared.Items.EquipSlot.Boots),
            });
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
                InitialCharacter((CharacterClass)request.ClassId, Uids.Next) with
                {
                    Hair = Game.Shared.Items.Appearance.Hair(request.Hair), Cloth = Game.Shared.Items.Appearance.Cloth(request.Cloth),
                });
            return AuthResult<CharacterSummary>.Success(new CharacterSummary
            {
                Id = created.Id, Name = created.Name, ClassId = (int)created.Class, Level = created.Level,
                Hair = created.Hair, Cloth = created.Cloth,
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
        // O token pode ser de antes do banimento: confere a conta de novo na hora de entrar no jogo.
        var account = await _store.FindAccountByIdAsync(accountId);
        if (account == null) return AuthResult<CharacterRecord>.Fail(new LocalText("auth.character_not_found"), 404);
        if (account.IsBanned(_now())) return AuthResult<CharacterRecord>.Fail(Banned(account), 403);
        return AuthResult<CharacterRecord>.Success(character with
        {
            IsGm = account.IsGm || _gmAccounts.Contains(account.Name), MutedUntil = account.MutedUntil,
        });
    }

    /// <summary>Motivo da recusa de uma conta banida (até quando e por quê; permanente se passar de 50 anos).</summary>
    private LocalText Banned(AccountRecord account)
    {
        var until = account.BannedUntil!.Value;
        return until - _now() > TimeSpan.FromDays(365 * 50)
            ? new LocalText("auth.banned_forever", account.BanReason)
            : new LocalText("auth.banned_until", until.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), account.BanReason);
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
