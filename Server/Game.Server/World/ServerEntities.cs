using Game.Shared.Combat;
using Game.Shared.Core;
using Game.Shared.Loot;
using Game.Shared.Monsters;
using Game.Shared.Progression;
using Game.Shared.World;

namespace Game.Server.World;

/// <summary>Jogador conectado, do ponto de vista do servidor (que decide tudo de combate).</summary>
public sealed class ServerPlayer
{
    /// <summary>Folga de timing do combo no servidor, além da compensação de latência.</summary>
    public const double ComboGraceSeconds = 0.08;

    public int Id { get; init; }
    public string Name { get; init; } = "";
    public required MovementValidator Movement { get; init; }
    public float Yaw { get; set; }
    public uint LastMoveSequence { get; set; }

    public CharacterProgress Progress { get; } = new();
    public CharacterStats Stats => CharacterStats.ForLevel(Progress.Level);
    public ResourcePool Hp { get; }
    public ResourcePool Mp { get; }
    public ResourcePool Sp { get; } = new(PlayerRules.MaxSp, PlayerRules.StartingSp);
    public List<SkillData> Skills { get; } = PlayerRules.StarterSkills();
    public SkillCooldowns Cooldowns { get; }
    public ComboSession Combo { get; } = new(new ComboConfig { GraceSeconds = ComboGraceSeconds });
    public long Alz { get; set; }
    public Dictionary<string, (string Name, ItemRarity Rarity, int Quantity)> Inventory { get; } = new();

    public bool IsDead { get; set; }
    public double RespawnAt { get; set; }
    public double DodgeUntil { get; set; }
    public double NextDodgeAt { get; set; }
    public float MpRegenCarry { get; set; }

    /// <summary>Entidades e itens que este jogador já conhece (recebeu EntitySpawned/LootSpawned).</summary>
    public HashSet<int> KnownEntities { get; } = new();
    public HashSet<int> KnownLoot { get; } = new();

    /// <summary>Precisa reenviar <c>PlayerStats</c> / <c>ComboState</c> no próximo tick.</summary>
    public bool StatsDirty { get; set; } = true;
    public bool ComboDirty { get; set; } = true;

    public ServerPlayer()
    {
        var stats = CharacterStats.ForLevel(1);
        Hp = new ResourcePool(stats.MaxHp);
        Mp = new ResourcePool(stats.MaxMp);
        Cooldowns = new SkillCooldowns(Skills.Count);
    }

    public float X => Movement.X;
    public float Z => Movement.Z;
    public bool IsDodging(double now) => now < DodgeUntil;
}

/// <summary>Monstro simulado no servidor com a IA do Shared.</summary>
public sealed class ServerMonster
{
    public int Id { get; init; }
    public required SpawnDefinition Spawn { get; init; }
    public required MonsterBrain Brain { get; init; }
    public required ResourcePool Hp { get; init; }
    public float HomeX { get; init; }
    public float HomeZ { get; init; }

    public MonsterData Data => Spawn.Data;
    public float X { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public double RespawnAt { get; set; }

    /// <summary>Jogador que o monstro persegue (quem bateu por último), ou 0.</summary>
    public int AggroPlayerId { get; set; }

    /// <summary>Dano recebido por jogador (quem causou mais dano fica com o XP e o drop).</summary>
    public Dictionary<int, int> DamageBy { get; } = new();

    public bool IsDead => Brain.State == MonsterState.Dead;

    // Chefe
    public bool PhaseTwo { get; set; }
    public double BaseAttackCooldown { get; init; }
    public double NextAreaAt { get; set; }
    public (float X, float Z, double LandsAt)? PendingArea { get; set; }
}

/// <summary>Item no chão. Durante alguns segundos só o dono pode pegar.</summary>
public sealed class ServerLoot
{
    public const double OwnerSeconds = 15;
    public const double LifetimeSeconds = 90;

    public int Id { get; init; }
    public ItemDrop Drop { get; init; }
    public float X { get; init; }
    public float Z { get; init; }
    public int OwnerId { get; init; }
    public double OwnerUntil { get; init; }
    public double ExpiresAt { get; init; }

    public bool CanBePickedBy(int playerId, double now) => playerId == OwnerId || now >= OwnerUntil;
}
