using Game.Shared.Accounts;
using Game.Shared.Loot;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Game.Server.Persistence;

/// <summary>Banco PostgreSQL via Entity Framework Core. Cada operação usa um contexto curto (seguro entre threads).</summary>
public sealed class EfGameStore : IGameStore
{
    private readonly IDbContextFactory<GameDbContext> _factory;

    public EfGameStore(IDbContextFactory<GameDbContext> factory) => _factory = factory;

    /// <summary>Cria o banco/tabelas que faltam aplicando as migrações pendentes.</summary>
    public async Task MigrateAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async Task<AccountRecord?> FindAccountAsync(string normalizedAccount)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Name == normalizedAccount);
        return account == null ? null : new AccountRecord(account.Id, account.Name, account.PasswordHash);
    }

    public async Task<AccountRecord> CreateAccountAsync(string normalizedAccount, string passwordHash)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var account = new AccountEntity { Name = normalizedAccount, PasswordHash = passwordHash, CreatedAt = DateTimeOffset.UtcNow };
        db.Accounts.Add(account);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new StoreConflictException("Essa conta já existe.");
        }
        return new AccountRecord(account.Id, account.Name, account.PasswordHash);
    }

    public async Task UpdateLoginAsync(int accountId, string? newPasswordHash)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var account = await db.Accounts.FindAsync(accountId);
        if (account == null) return;
        account.LastLoginAt = DateTimeOffset.UtcNow;
        if (newPasswordHash != null) account.PasswordHash = newPasswordHash;
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<CharacterSummaryRecord>> ListCharactersAsync(int accountId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Characters.AsNoTracking()
            .Where(c => c.AccountId == accountId)
            .OrderBy(c => c.Id)
            .Select(c => new CharacterSummaryRecord(c.Id, c.Name, (CharacterClass)c.ClassId, c.Level))
            .ToListAsync();
    }

    public async Task<CharacterRecord> CreateCharacterAsync(int accountId, string name, CharacterClass characterClass, CharacterRecord initial)
    {
        await using var db = await _factory.CreateDbContextAsync();
        // Transação serializável: dois pedidos ao mesmo tempo não passam do limite de personagens.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var count = await db.Characters.CountAsync(c => c.AccountId == accountId);
        if (count >= AccountRules.MaxCharactersPerAccount)
            throw new StoreConflictException($"A conta já tem {AccountRules.MaxCharactersPerAccount} personagens.");

        var now = DateTimeOffset.UtcNow;
        var character = new CharacterEntity
        {
            AccountId = accountId, Name = name.Trim(), NameKey = AccountRules.NameKey(name), ClassId = (int)characterClass,
            Level = initial.Level, Xp = initial.Xp, Alz = initial.Alz, PositionX = initial.X, PositionZ = initial.Z,
            Hp = initial.Hp, Mp = initial.Mp, Sp = initial.Sp, CreatedAt = now, SavedAt = now,
        };
        db.Characters.Add(character);
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new StoreConflictException("Esse nome já está em uso.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
        {
            throw new StoreConflictException("Tente de novo em instantes.");
        }
        return ToRecord(character);
    }

    public async Task<CharacterRecord?> LoadCharacterAsync(int characterId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var character = await db.Characters.AsNoTracking().Include(c => c.Inventory).FirstOrDefaultAsync(c => c.Id == characterId);
        return character == null ? null : ToRecord(character);
    }

    public async Task SaveCharacterAsync(CharacterSnapshot snapshot, IReadOnlyList<ItemLogEntry> logs)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var character = await db.Characters.Include(c => c.Inventory).FirstOrDefaultAsync(c => c.Id == snapshot.CharacterId);
        if (character == null) return;

        character.Level = snapshot.Level;
        character.Xp = snapshot.Xp;
        character.Alz = snapshot.Alz;
        character.PositionX = snapshot.X;
        character.PositionZ = snapshot.Z;
        character.Hp = snapshot.Hp;
        character.Mp = snapshot.Mp;
        character.Sp = snapshot.Sp;
        character.SavedAt = DateTimeOffset.UtcNow;

        // Inventário: o servidor é a fonte da verdade; o banco fica igual ao que está na memória.
        var wanted = snapshot.Inventory.ToDictionary(i => i.ItemId);
        character.Inventory.RemoveAll(i => !wanted.ContainsKey(i.ItemId));
        foreach (var entry in snapshot.Inventory)
        {
            var row = character.Inventory.FirstOrDefault(i => i.ItemId == entry.ItemId);
            if (row == null)
            {
                character.Inventory.Add(new InventoryItemEntity
                {
                    CharacterId = character.Id, ItemId = entry.ItemId, Name = entry.Name, Rarity = (int)entry.Rarity, Quantity = entry.Quantity,
                });
            }
            else
            {
                row.Quantity = entry.Quantity;
                row.Name = entry.Name;
                row.Rarity = (int)entry.Rarity;
            }
        }

        foreach (var log in logs)
        {
            db.ItemLog.Add(new ItemLogEntity
            {
                CharacterId = log.CharacterId, At = log.At, Kind = log.Kind, ItemId = log.ItemId,
                Quantity = log.Quantity, AlzDelta = log.AlzDelta, Details = log.Details,
            });
        }

        // SaveChanges é uma transação: estado, inventário e registros entram juntos ou nenhum entra.
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ItemLogEntry>> ItemLogAsync(int characterId, int max = 100)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.ItemLog.AsNoTracking()
            .Where(l => l.CharacterId == characterId)
            .OrderByDescending(l => l.Id)
            .Take(max)
            .Select(l => new ItemLogEntry(l.CharacterId, l.At, l.Kind, l.ItemId, l.Quantity, l.AlzDelta, l.Details))
            .ToListAsync();
    }

    private static CharacterRecord ToRecord(CharacterEntity c) => new(
        c.Id, c.AccountId, c.Name, (CharacterClass)c.ClassId, c.Level, c.Xp, c.Alz, c.PositionX, c.PositionZ,
        c.Hp, c.Mp, c.Sp,
        c.Inventory.Select(i => new InventoryEntry(i.ItemId, i.Name, (ItemRarity)i.Rarity, i.Quantity)).ToList());

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

/// <summary>Banco em memória: testes e desenvolvimento sem PostgreSQL (tudo se perde ao desligar).</summary>
public sealed class InMemoryGameStore : IGameStore
{
    private readonly object _lock = new();
    private readonly Dictionary<int, AccountRecord> _accounts = new();
    private readonly Dictionary<int, CharacterRecord> _characters = new();
    private readonly List<ItemLogEntry> _log = new();
    private int _nextAccountId = 1;
    private int _nextCharacterId = 1;

    /// <summary>Quantas vezes algo foi salvo (testes).</summary>
    public int SaveCount { get; private set; }

    public Task<AccountRecord?> FindAccountAsync(string normalizedAccount)
    {
        lock (_lock) return Task.FromResult(_accounts.Values.FirstOrDefault(a => a.Name == normalizedAccount));
    }

    public Task<AccountRecord> CreateAccountAsync(string normalizedAccount, string passwordHash)
    {
        lock (_lock)
        {
            if (_accounts.Values.Any(a => a.Name == normalizedAccount)) throw new StoreConflictException("Essa conta já existe.");
            var account = new AccountRecord(_nextAccountId++, normalizedAccount, passwordHash);
            _accounts.Add(account.Id, account);
            return Task.FromResult(account);
        }
    }

    public Task UpdateLoginAsync(int accountId, string? newPasswordHash)
    {
        lock (_lock)
        {
            if (newPasswordHash != null && _accounts.TryGetValue(accountId, out var account))
                _accounts[accountId] = account with { PasswordHash = newPasswordHash };
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CharacterSummaryRecord>> ListCharactersAsync(int accountId)
    {
        lock (_lock)
        {
            IReadOnlyList<CharacterSummaryRecord> list = _characters.Values.Where(c => c.AccountId == accountId).OrderBy(c => c.Id)
                .Select(c => new CharacterSummaryRecord(c.Id, c.Name, c.Class, c.Level)).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<CharacterRecord> CreateCharacterAsync(int accountId, string name, CharacterClass characterClass, CharacterRecord initial)
    {
        lock (_lock)
        {
            if (_characters.Values.Count(c => c.AccountId == accountId) >= AccountRules.MaxCharactersPerAccount)
                throw new StoreConflictException($"A conta já tem {AccountRules.MaxCharactersPerAccount} personagens.");
            var key = AccountRules.NameKey(name);
            if (_characters.Values.Any(c => AccountRules.NameKey(c.Name) == key)) throw new StoreConflictException("Esse nome já está em uso.");
            var character = initial with { Id = _nextCharacterId++, AccountId = accountId, Name = name.Trim(), Class = characterClass };
            _characters.Add(character.Id, character);
            return Task.FromResult(character);
        }
    }

    public Task<CharacterRecord?> LoadCharacterAsync(int characterId)
    {
        lock (_lock) return Task.FromResult(_characters.TryGetValue(characterId, out var c) ? c : null);
    }

    public Task SaveCharacterAsync(CharacterSnapshot s, IReadOnlyList<ItemLogEntry> logs)
    {
        lock (_lock)
        {
            if (!_characters.TryGetValue(s.CharacterId, out var c)) return Task.CompletedTask;
            _characters[s.CharacterId] = c with
            {
                Level = s.Level, Xp = s.Xp, Alz = s.Alz, X = s.X, Z = s.Z, Hp = s.Hp, Mp = s.Mp, Sp = s.Sp,
                Inventory = s.Inventory.ToList(),
            };
            _log.AddRange(logs);
            SaveCount++;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ItemLogEntry>> ItemLogAsync(int characterId, int max = 100)
    {
        lock (_lock)
        {
            IReadOnlyList<ItemLogEntry> list = _log.Where(l => l.CharacterId == characterId).Reverse().Take(max).ToList();
            return Task.FromResult(list);
        }
    }
}
