using Game.Server.Persistence;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Core;
using Game.Shared.Items;
using Game.Shared.Loot;
using Game.Shared.Monsters;
using Game.Shared.Progression;
using Game.Shared.Social;
using Game.Shared.World;

namespace Game.Server.World;

/// <summary>Jogador conectado, do ponto de vista do servidor (que decide tudo de combate, itens e missões).</summary>
public sealed class ServerPlayer
{
    /// <summary>Folga de timing do combo no servidor, além da compensação de latência.</summary>
    public const double ComboGraceSeconds = 0.08;

    /// <summary>Intervalo mínimo entre poções.</summary>
    public const double PotionCooldownSeconds = 1.0;

    public int Id { get; init; }
    public string Name { get; init; } = "";

    /// <summary>Id do personagem no banco; 0 = convidado (testes e bots, não é salvo).</summary>
    public int CharacterId { get; init; }

    /// <summary>
    /// Convidado é para testes e bots: tem tudo liberado (skills e combo) sem precisar das missões, e não é salvo.
    /// </summary>
    public bool IsGuest => CharacterId == 0;

    public CharacterClass Class { get; }

    public required MovementValidator Movement { get; init; }
    public float Yaw { get; set; }
    public uint LastMoveSequence { get; set; }

    public CharacterProgress Progress { get; set; } = new();
    public Attributes Attributes { get; set; }

    /// <summary>Valores finais (nível + atributos + equipamento). Recalcular com <see cref="RecalculateStats"/>.</summary>
    public StatBlock Stats { get; private set; }

    public ResourcePool Hp { get; }
    public ResourcePool Mp { get; }
    public ResourcePool Sp { get; } = new(PlayerRules.MaxSp, PlayerRules.StartingSp);

    /// <summary>Skills da classe, na ordem da barra (o <c>Slot</c> do <c>UseSkill</c> é o índice aqui).</summary>
    public IReadOnlyList<ClassSkill> ClassSkills { get; }
    public IReadOnlyList<SkillData> Skills { get; }
    public SkillCooldowns Cooldowns { get; }
    public ComboSession Combo { get; }
    public AwakeningState Awakening { get; } = new();
    public long Alz { get; set; }

    public ItemContainer Bag { get; } = new();
    public ItemContainer Warehouse { get; } = new();
    public Equipment Equipment { get; } = new();
    public QuestLog Quests { get; } = new();

    public bool IsDead { get; set; }
    public double RespawnAt { get; set; }
    public double DodgeUntil { get; set; }
    public double NextDodgeAt { get; set; }
    public double NextPotionAt { get; set; }
    public float MpRegenCarry { get; set; }

    /// <summary>Movimentos de item e Alz desde o último salvamento (vão junto no próximo).</summary>
    public List<ItemLogEntry> PendingLogs { get; } = new();

    /// <summary>Entidades e itens que este jogador já conhece (recebeu EntitySpawned/LootSpawned).</summary>
    public HashSet<int> KnownEntities { get; } = new();
    public HashSet<int> KnownLoot { get; } = new();

    /// <summary>Precisa reenviar a mensagem correspondente no próximo tick.</summary>
    public bool StatsDirty { get; set; } = true;
    public bool InventoryDirty { get; set; } = true;
    public bool ComboDirty { get; set; } = true;
    public bool SheetDirty { get; set; } = true;
    public bool QuestsDirty { get; set; } = true;

    /// <summary>Grupo atual (0 = sem grupo).</summary>
    public int PartyId { get; set; }

    /// <summary>Convite de grupo esperando resposta: quem convidou e até quando vale.</summary>
    public (int InviterId, double ExpiresAt)? PendingInvite { get; set; }

    /// <summary>Assinatura do último PartyState enviado (só reenvia quando muda).</summary>
    public int LastPartySignature { get; set; }

    /// <summary>Precisa mandar o PartyState mesmo sem mudança de assinatura (entrou ou saiu do grupo).</summary>
    public bool PartyDirty { get; set; }

    public ChatRateLimiter ChatLimiter { get; } = new();

    /// <summary>Troca aberta (0 = nenhuma), pedido de troca esperando resposta e se precisa reenviar o TradeState.</summary>
    public int TradeId { get; set; }
    public (int FromId, double ExpiresAt)? PendingTradeInvite { get; set; }
    public bool TradeDirty { get; set; }

    /// <summary>Amigos e bloqueados (salvos com o personagem).</summary>
    public List<RelationRecord> Relations { get; } = new();

    /// <summary>Precisa mandar a lista de amigos e bloqueados.</summary>
    public bool SocialDirty { get; set; } = true;

    /// <summary>Últimas mensagens enviadas (texto original), que vão junto se alguém denunciar.</summary>
    public Queue<(double At, ChatChannel Channel, string Text)> RecentChat { get; } = new();

    /// <summary>Denúncias feitas: horários (limite por hora) e quando denunciou cada nome.</summary>
    public List<double> ReportTimes { get; } = new();
    public Dictionary<string, double> ReportedAt { get; } = new();

    /// <summary>Denúncias desde o último salvamento (vão junto no próximo).</summary>
    public List<ReportRecord> PendingReports { get; } = new();

    public bool HasRelation(RelationKind kind, int characterId) =>
        characterId != 0 && Relations.Any(r => r.Kind == kind && r.OtherId == characterId);

    /// <summary>Este jogador bloqueou o outro (não recebe mensagens nem convites dele).</summary>
    public bool HasBlocked(ServerPlayer other) => HasRelation(RelationKind.Blocked, other.CharacterId);

    public ServerPlayer(CharacterClass characterClass = CharacterClass.Blade)
    {
        Class = characterClass;
        Attributes = Attributes.StartingFor(characterClass);
        ClassSkills = ClassCatalog.SkillsFor(characterClass);
        Skills = ClassSkills.Select(s => s.Skill).ToList();
        Cooldowns = new SkillCooldowns(Skills.Count);
        Combo = new ComboSession(ClassCatalog.ComboConfigFor(characterClass, ComboGraceSeconds));
        Stats = StatCalculator.Compute(1, Attributes, Equipment);
        Hp = new ResourcePool(Stats.MaxHp);
        Mp = new ResourcePool(Stats.MaxMp);
    }

    public float X => Movement.X;
    public float Z => Movement.Z;
    public bool IsDodging(double now) => now < DodgeUntil;

    /// <summary>Missão concluída (convidado conta tudo como concluído).</summary>
    public bool QuestCompleted(string questId) => IsGuest || Quests.IsCompleted(questId);

    public bool SkillUnlocked(int slot) =>
        IsGuest || (slot >= 0 && slot < ClassSkills.Count && ClassSkills[slot].IsUnlocked(Progress.Level, QuestCompleted));

    public bool ComboUnlocked => ClassCatalog.ComboUnlocked(QuestCompleted);

    public int FreeAttributePoints => Attributes.FreePoints(Progress.Level, Class, Attributes);

    /// <summary>Recalcula os valores finais e ajusta os máximos de vida e MP (avisa o cliente).</summary>
    public void RecalculateStats()
    {
        Stats = StatCalculator.Compute(Progress.Level, Attributes, Equipment);
        Hp.SetMax(Stats.MaxHp);
        Mp.SetMax(Stats.MaxMp);
        StatsDirty = true;
        SheetDirty = true;
    }
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

    /// <summary>Grupo do dono quando caiu (0 = sozinho): qualquer membro pode pegar.</summary>
    public int OwnerPartyId { get; init; }

    public double OwnerUntil { get; init; }
    public double ExpiresAt { get; init; }

    public bool CanBePickedBy(int playerId, int partyId, double now) =>
        playerId == OwnerId || (OwnerPartyId != 0 && partyId == OwnerPartyId) || now >= OwnerUntil;
}
