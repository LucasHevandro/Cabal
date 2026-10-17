using Game.Shared.Accounts;
using Game.Shared.Items;
using Game.Shared.Net;

namespace Game.Server.Persistence;

public sealed record AccountRecord(int Id, string Name, string PasswordHash);

/// <summary>Um item guardado: onde está (bolsa, vestido ou armazém), quantos e o estado da melhoria.</summary>
public sealed record ItemRecord(
    long Uid, ItemContainerKind Container, int Slot, string ItemId, int Quantity,
    int Upgrade = 0, int PityFails = 0, int PityLevel = 0, bool RegainGuaranteed = false, string OptionId = "");

/// <summary>Missão aceita (com contadores) ou concluída.</summary>
public sealed record QuestRecord(string QuestId, bool Completed, IReadOnlyList<int> Counts);

public enum RelationKind
{
    Friend = 0,
    Blocked = 1,
}

/// <summary>Amigo ou bloqueado: o outro personagem pelo id e o nome (para mostrar quando ele está offline).</summary>
public sealed record RelationRecord(RelationKind Kind, int OtherId, string OtherName);

/// <summary>Denúncia de um jogador, com as últimas mensagens do denunciado (texto original, sem filtro).</summary>
public sealed record ReportRecord(
    int ReporterId, string ReporterName, int TargetId, string TargetName, string Reason, string ChatExcerpt, DateTimeOffset At);

public sealed record CharacterSummaryRecord(int Id, string Name, CharacterClass Class, int Level);

/// <summary>Personagem como está no banco.</summary>
public sealed record CharacterRecord(
    int Id, int AccountId, string Name, CharacterClass Class, int Level, int Xp, long Alz,
    float X, float Z, int Hp, int Mp, int Sp, IReadOnlyList<ItemRecord> Items,
    Attributes Attributes = default, int AwakeningGauge = 0, IReadOnlyList<QuestRecord>? Quests = null)
{
    public IReadOnlyList<QuestRecord> Quests { get; init; } = Quests ?? Array.Empty<QuestRecord>();
    public IReadOnlyList<RelationRecord> Relations { get; init; } = Array.Empty<RelationRecord>();
}

/// <summary>Estado do personagem para salvar (o que muda durante o jogo).</summary>
public sealed record CharacterSnapshot(
    int CharacterId, int Level, int Xp, long Alz, float X, float Z, int Hp, int Mp, int Sp,
    IReadOnlyList<ItemRecord> Items, Attributes Attributes = default, int AwakeningGauge = 0,
    IReadOnlyList<QuestRecord>? Quests = null)
{
    public IReadOnlyList<QuestRecord> Quests { get; init; } = Quests ?? Array.Empty<QuestRecord>();

    /// <summary>Amigos e bloqueados (o banco fica igual a esta lista).</summary>
    public IReadOnlyList<RelationRecord> Relations { get; init; } = Array.Empty<RelationRecord>();

    /// <summary>Denúncias feitas desde o último salvamento (só acrescenta).</summary>
    public IReadOnlyList<ReportRecord> Reports { get; init; } = Array.Empty<ReportRecord>();
}

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

    /// <summary>Maior id único de item já guardado (o servidor continua a contar daí).</summary>
    Task<long> MaxItemUidAsync();

    /// <summary>Denúncias, mais recentes primeiro (moderação e testes).</summary>
    Task<IReadOnlyList<ReportRecord>> ReportsAsync(int max = 100);
}

/// <summary>
/// Dá ids únicos de item para o servidor inteiro (mundo e criação de personagem). Começa depois do maior id do banco.
/// Seguro entre threads.
/// </summary>
public sealed class ItemUidAllocator
{
    private long _last;

    public ItemUidAllocator(long lastUsed = 0) => _last = lastUsed;

    public long Next() => Interlocked.Increment(ref _last);
}
