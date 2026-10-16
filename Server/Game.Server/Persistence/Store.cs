using Game.Shared.Accounts;
using Game.Shared.Loot;

namespace Game.Server.Persistence;

public sealed record AccountRecord(int Id, string Name, string PasswordHash);

public sealed record InventoryEntry(string ItemId, string Name, ItemRarity Rarity, int Quantity);

public sealed record CharacterSummaryRecord(int Id, string Name, CharacterClass Class, int Level);

/// <summary>Personagem como está no banco.</summary>
public sealed record CharacterRecord(
    int Id, int AccountId, string Name, CharacterClass Class, int Level, int Xp, long Alz,
    float X, float Z, int Hp, int Mp, int Sp, IReadOnlyList<InventoryEntry> Inventory);

/// <summary>Estado do personagem para salvar (o que muda durante o jogo).</summary>
public sealed record CharacterSnapshot(
    int CharacterId, int Level, int Xp, long Alz, float X, float Z, int Hp, int Mp, int Sp,
    IReadOnlyList<InventoryEntry> Inventory);

/// <summary>Movimento de item ou Alz, para auditoria e suporte (quem ganhou o quê, quando).</summary>
public sealed record ItemLogEntry(int CharacterId, DateTimeOffset At, string Kind, string? ItemId, int Quantity, long AlzDelta, string Details);

/// <summary>Conta ou nome de personagem já existente.</summary>
public sealed class StoreConflictException : Exception
{
    public StoreConflictException(string message) : base(message) { }
}

/// <summary>
/// Acesso ao banco do jogo. Implementações: <see cref="EfGameStore"/> (PostgreSQL) e
/// <see cref="InMemoryGameStore"/> (testes e desenvolvimento sem banco). Seguras para chamar de várias threads.
/// </summary>
public interface IGameStore
{
    Task<AccountRecord?> FindAccountAsync(string normalizedAccount);

    /// <summary>Cria a conta. <see cref="StoreConflictException"/> se o nome já existe.</summary>
    Task<AccountRecord> CreateAccountAsync(string normalizedAccount, string passwordHash);

    Task UpdateLoginAsync(int accountId, string? newPasswordHash);

    Task<IReadOnlyList<CharacterSummaryRecord>> ListCharactersAsync(int accountId);

    /// <summary>
    /// Cria o personagem com os valores iniciais. <see cref="StoreConflictException"/> se o nome (sem acento/caixa)
    /// já existe ou a conta já tem o máximo de personagens.
    /// </summary>
    Task<CharacterRecord> CreateCharacterAsync(int accountId, string name, CharacterClass characterClass, CharacterRecord initial);

    Task<CharacterRecord?> LoadCharacterAsync(int characterId);

    /// <summary>Salva o estado e os registros de itens numa única transação (tudo ou nada).</summary>
    Task SaveCharacterAsync(CharacterSnapshot snapshot, IReadOnlyList<ItemLogEntry> logs);

    /// <summary>Registros de itens de um personagem, mais recentes primeiro (suporte e testes).</summary>
    Task<IReadOnlyList<ItemLogEntry>> ItemLogAsync(int characterId, int max = 100);
}
