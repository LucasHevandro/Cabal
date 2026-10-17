using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Game.Server.Persistence;

public sealed class AccountEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public List<CharacterEntity> Characters { get; set; } = new();
}

public sealed class CharacterEntity
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public AccountEntity? Account { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Nome sem acento e em minúsculas: índice único impede nomes "iguais".</summary>
    public string NameKey { get; set; } = "";

    public int ClassId { get; set; }
    public int Level { get; set; }
    public int Xp { get; set; }
    public long Alz { get; set; }
    public float PositionX { get; set; }
    public float PositionZ { get; set; }
    public int Hp { get; set; }
    public int Mp { get; set; }
    public int Sp { get; set; }
    public int Strength { get; set; }
    public int Dexterity { get; set; }
    public int Intelligence { get; set; }
    public int AwakeningGauge { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    public List<ItemInstanceEntity> Items { get; set; } = new();
    public List<CharacterQuestEntity> Quests { get; set; } = new();
}

/// <summary>Um item de verdade, com id único: bolsa (0), vestido (1, posição = encaixe) ou armazém (2).</summary>
public sealed class ItemInstanceEntity
{
    public long Uid { get; set; }
    public int CharacterId { get; set; }
    public int Container { get; set; }
    public int Slot { get; set; }
    public string ItemId { get; set; } = "";
    public int Quantity { get; set; }
    public int Upgrade { get; set; }
    public int PityFails { get; set; }
    public int PityLevel { get; set; }
    public bool RegainGuaranteed { get; set; }
    public string OptionId { get; set; } = "";
}

/// <summary>Missão aceita ou concluída. Contadores separados por vírgula ("3,10").</summary>
public sealed class CharacterQuestEntity
{
    public int CharacterId { get; set; }
    public string QuestId { get; set; } = "";
    public bool Completed { get; set; }
    public string Counts { get; set; } = "";
}

public sealed class ItemLogEntity
{
    public long Id { get; set; }
    public int CharacterId { get; set; }
    public DateTimeOffset At { get; set; }
    public string Kind { get; set; } = "";
    public string? ItemId { get; set; }
    public int Quantity { get; set; }
    public long AlzDelta { get; set; }
    public string Details { get; set; } = "";
}

/// <summary>Banco do jogo (PostgreSQL). Tabelas em snake_case, nomes em português nos comentários.</summary>
public sealed class GameDbContext : DbContext
{
    public GameDbContext(DbContextOptions<GameDbContext> options) : base(options) { }

    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();
    public DbSet<CharacterEntity> Characters => Set<CharacterEntity>();
    public DbSet<ItemInstanceEntity> Items => Set<ItemInstanceEntity>();
    public DbSet<CharacterQuestEntity> CharacterQuests => Set<CharacterQuestEntity>();
    public DbSet<ItemLogEntity> ItemLog => Set<ItemLogEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<AccountEntity>(e =>
        {
            e.ToTable("accounts");
            e.Property(a => a.Name).HasMaxLength(32).IsRequired();
            e.HasIndex(a => a.Name).IsUnique();
            e.Property(a => a.PasswordHash).HasMaxLength(200).IsRequired();
        });

        model.Entity<CharacterEntity>(e =>
        {
            e.ToTable("characters");
            e.Property(c => c.Name).HasMaxLength(32).IsRequired();
            e.Property(c => c.NameKey).HasMaxLength(32).IsRequired();
            e.HasIndex(c => c.NameKey).IsUnique();
            e.HasIndex(c => c.AccountId);
            e.HasOne(c => c.Account).WithMany(a => a.Characters).HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.CharacterId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(c => c.Quests).WithOne().HasForeignKey(q => q.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ItemInstanceEntity>(e =>
        {
            e.ToTable("item_instances");
            e.HasKey(i => i.Uid);
            e.Property(i => i.Uid).ValueGeneratedNever(); // o servidor dá o id (ItemUidAllocator)
            e.HasIndex(i => i.CharacterId);
            e.Property(i => i.ItemId).HasMaxLength(64);
            e.Property(i => i.OptionId).HasMaxLength(64);
        });

        model.Entity<CharacterQuestEntity>(e =>
        {
            e.ToTable("character_quests");
            e.HasKey(q => new { q.CharacterId, q.QuestId });
            e.Property(q => q.QuestId).HasMaxLength(64);
            e.Property(q => q.Counts).HasMaxLength(200);
        });

        model.Entity<ItemLogEntity>(e =>
        {
            e.ToTable("item_log");
            e.HasIndex(l => new { l.CharacterId, l.At });
            e.Property(l => l.Kind).HasMaxLength(32);
            e.Property(l => l.ItemId).HasMaxLength(64);
            e.Property(l => l.Details).HasMaxLength(500);
        });
    }
}

/// <summary>
/// Usado pela ferramenta de migrações (<c>dotnet ef</c>): lê a conexão de GAME_DB ou usa a padrão do docker-compose.
/// </summary>
public sealed class GameDbContextDesignFactory : IDesignTimeDbContextFactory<GameDbContext>
{
    public GameDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("GAME_DB") ?? DatabaseDefaults.DevelopmentConnection;
        return new GameDbContext(new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(connection).Options);
    }
}

public static class DatabaseDefaults
{
    /// <summary>Conexão do PostgreSQL do docker-compose.yml (só desenvolvimento local).</summary>
    public const string DevelopmentConnection = "Host=localhost;Port=5432;Database=game;Username=game;Password=game_dev_local";
}
