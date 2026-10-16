using System.Numerics;
using Game.Shared.Combat;
using Game.Shared.Core;
using Game.Shared.Monsters;
using Game.Shared.Net;
using Game.Shared.World;
using ComboPhase = Game.Shared.Combat.ComboState;
using ComboStateMessage = Game.Shared.Net.ComboState;

namespace Game.Server.World;

/// <summary>Para onde o mundo manda mensagens. A rede implementa; os testes usam uma lista.</summary>
public interface IOutbox
{
    void Send(int playerId, INetMessage message);
}

/// <summary>
/// O mundo do jogo no servidor, sem rede: recebe mensagens já decodificadas, avança no tempo
/// e responde pelo <see cref="IOutbox"/>. Tudo roda numa única thread (o loop de ticks).
/// Este arquivo cuida de entrada/saída, movimento e do que cada jogador enxerga;
/// combate, monstros e drops ficam em GameWorld.Combat.cs.
/// </summary>
public sealed partial class GameWorld
{
    public const byte TickRate = 20;

    /// <summary>Distância até onde um jogador recebe entidades e itens.</summary>
    public const float InterestRadius = 60f;

    private readonly IOutbox _outbox;
    private readonly IRandomSource _random;
    private readonly Dictionary<int, ServerPlayer> _players = new();
    private readonly Dictionary<int, ServerMonster> _monsters = new();
    private readonly Dictionary<int, ServerLoot> _loot = new();
    private int _nextEntityId = 1;
    private int _nextLootId = 1;
    private uint _tick;
    private double _lastTickAt = double.NaN;

    /// <summary>
    /// Cria o mundo. <paramref name="spawns"/> nulo usa o conteúdo padrão (<see cref="WorldContent"/>);
    /// os testes passam grupos próprios e uma fonte aleatória fixa.
    /// </summary>
    public GameWorld(IOutbox outbox, IEnumerable<SpawnDefinition>? spawns = null, IRandomSource? random = null)
    {
        _outbox = outbox;
        _random = random ?? new SystemRandomSource();

        var seed = 1;
        foreach (var spawn in spawns ?? WorldContent.Spawns())
        {
            spawn.Data.Validate();
            foreach (var (x, z) in spawn.SpawnPoints(seed++)) CreateMonster(spawn, x, z);
        }
    }

    public IReadOnlyCollection<ServerPlayer> Players => _players.Values;
    public IReadOnlyCollection<ServerMonster> Monsters => _monsters.Values;
    public IReadOnlyCollection<ServerLoot> Loot => _loot.Values;
    public uint CurrentTick => _tick;

    public ServerPlayer? FindPlayer(int id) => _players.TryGetValue(id, out var p) ? p : null;
    public ServerMonster? FindMonster(int id) => _monsters.TryGetValue(id, out var m) ? m : null;

    /// <summary>
    /// Entra um jogador. <paramref name="onIdAssigned"/> roda antes de qualquer mensagem sair, para a rede saber para
    /// onde mandar. As entidades por perto chegam no primeiro tick.
    /// </summary>
    public ServerPlayer AddPlayer(string name, double now, Action<int>? onIdAssigned = null)
    {
        var cleanName = string.IsNullOrWhiteSpace(name) ? "Aventureiro" : name.Trim();
        var player = new ServerPlayer
        {
            Id = _nextEntityId++,
            Name = cleanName.Length > 20 ? cleanName[..20] : cleanName,
            Movement = new MovementValidator(WorldContent.PlayerSpawnX, WorldContent.PlayerSpawnZ, now),
        };
        onIdAssigned?.Invoke(player.Id);

        _outbox.Send(player.Id, new Welcome
        {
            PlayerId = player.Id, ServerTime = now, TickRate = TickRate,
            SpawnX = WorldContent.PlayerSpawnX, SpawnZ = WorldContent.PlayerSpawnZ,
        });
        _players.Add(player.Id, player);
        return player;
    }

    public void RemovePlayer(int playerId)
    {
        if (!_players.Remove(playerId)) return;
        foreach (var monster in _monsters.Values)
        {
            if (monster.AggroPlayerId == playerId) monster.AggroPlayerId = 0;
            monster.DamageBy.Remove(playerId);
        }
        foreach (var other in _players.Values)
        {
            if (other.KnownEntities.Remove(playerId)) _outbox.Send(other.Id, new EntityDespawned { Id = playerId });
        }
    }

    /// <summary>Trata uma mensagem do cliente.</summary>
    public void Handle(int playerId, INetMessage message, double now)
    {
        if (!_players.TryGetValue(playerId, out var player)) return;

        switch (message)
        {
            case MoveInput move:
                HandleMove(player, move, now);
                break;
            case Ping ping:
                _outbox.Send(playerId, new Pong { ClientTime = ping.ClientTime, ServerTime = now });
                break;
            case UseSkill use:
                HandleUseSkill(player, use, now);
                break;
            case StartCombo start:
                HandleStartCombo(player, start, now);
                break;
            case Finisher finisher:
                HandleFinisher(player, finisher, now);
                break;
            case PickupLoot pickup:
                HandlePickup(player, pickup, now);
                break;
        }
    }

    /// <summary>Um passo da simulação.</summary>
    public void Tick(double now)
    {
        var dt = double.IsNaN(_lastTickAt) ? 1.0 / TickRate : Math.Clamp(now - _lastTickAt, 0, 0.25);
        _lastTickAt = now;
        _tick++;

        foreach (var player in _players.Values) TickPlayer(player, now, (float)dt);
        foreach (var monster in _monsters.Values) TickMonster(monster, now, (float)dt);
        ExpireLoot(now);

        foreach (var player in _players.Values) SendView(player, now);
    }

    private void HandleMove(ServerPlayer player, MoveInput move, double now)
    {
        if (player.IsDead) return;
        if (move.Sequence <= player.LastMoveSequence && player.LastMoveSequence != 0) return;
        player.LastMoveSequence = move.Sequence;

        if (move.Dodging && now >= player.NextDodgeAt)
        {
            player.DodgeUntil = now + PlayerRules.DodgeSeconds;
            player.NextDodgeAt = now + PlayerRules.DodgeCooldownSeconds;
        }

        if (player.Movement.TryMove(move.X, move.Z, now))
        {
            player.Yaw = move.Yaw;
            return;
        }
        _outbox.Send(player.Id, new PositionCorrection { X = player.X, Z = player.Z });
    }

    /// <summary>Envia o que o jogador enxerga: entidades que apareceram/sumiram, itens e o snapshot do tick.</summary>
    private void SendView(ServerPlayer player, double now)
    {
        var snapshot = new Snapshot { Tick = _tick, ServerTime = now };
        var visible = new HashSet<int>();

        foreach (var other in _players.Values)
        {
            if (!InRange(player.X, player.Z, other.X, other.Z, InterestRadius)) continue;
            visible.Add(other.Id);
            if (player.KnownEntities.Add(other.Id)) _outbox.Send(player.Id, SpawnMessage(other));
            snapshot.Entities.Add(new EntityState
            {
                Id = other.Id, X = other.X, Z = other.Z, Yaw = other.Yaw, Hp = other.Hp.Current,
                Flags = other.IsDead ? EntityState.FlagDead : (byte)0,
            });
        }

        foreach (var monster in _monsters.Values)
        {
            if (!InRange(player.X, player.Z, monster.X, monster.Z, InterestRadius)) continue;
            visible.Add(monster.Id);
            if (player.KnownEntities.Add(monster.Id)) _outbox.Send(player.Id, SpawnMessage(monster));
            byte flags = 0;
            if (monster.IsDead) flags |= EntityState.FlagDead;
            if (monster.Brain.State == MonsterState.WindUp) flags |= EntityState.FlagWindUp;
            if (monster.PhaseTwo) flags |= EntityState.FlagEnraged;
            snapshot.Entities.Add(new EntityState
            {
                Id = monster.Id, X = monster.X, Z = monster.Z, Yaw = monster.Yaw, Hp = monster.Hp.Current, Flags = flags,
            });
        }

        foreach (var id in player.KnownEntities.Where(id => !visible.Contains(id)).ToList())
        {
            player.KnownEntities.Remove(id);
            _outbox.Send(player.Id, new EntityDespawned { Id = id });
        }

        foreach (var loot in _loot.Values)
        {
            var near = InRange(player.X, player.Z, loot.X, loot.Z, InterestRadius);
            if (near && player.KnownLoot.Add(loot.Id)) _outbox.Send(player.Id, LootMessage(loot));
            else if (!near && player.KnownLoot.Remove(loot.Id)) _outbox.Send(player.Id, new LootRemoved { LootId = loot.Id });
        }

        _outbox.Send(player.Id, snapshot);

        if (player.StatsDirty)
        {
            player.StatsDirty = false;
            _outbox.Send(player.Id, StatsMessage(player));
        }
        if (player.ComboDirty)
        {
            player.ComboDirty = false;
            _outbox.Send(player.Id, ComboMessage(player));
        }
    }

    /// <summary>Manda a mensagem a todos os jogadores perto do ponto.</summary>
    private void SendNearby(float x, float z, INetMessage message)
    {
        foreach (var player in _players.Values)
        {
            if (InRange(player.X, player.Z, x, z, InterestRadius)) _outbox.Send(player.Id, message);
        }
    }

    private void Notify(ServerPlayer player, string text, bool announcement = false) =>
        _outbox.Send(player.Id, new Notice { Text = text, Announcement = announcement });

    private void CreateMonster(SpawnDefinition spawn, float x, float z)
    {
        var monster = new ServerMonster
        {
            Id = _nextEntityId++,
            Spawn = spawn,
            Brain = new MonsterBrain(spawn.Data, new Vector2(x, z), _random),
            Hp = new ResourcePool(spawn.Data.MaxHp),
            HomeX = x,
            HomeZ = z,
            X = x,
            Z = z,
            BaseAttackCooldown = spawn.Data.AttackCooldownSeconds,
        };
        monster.Brain.Revive(0);
        _monsters.Add(monster.Id, monster);
    }

    private static bool InRange(float ax, float az, float bx, float bz, float radius)
    {
        var dx = ax - bx;
        var dz = az - bz;
        return dx * dx + dz * dz <= radius * radius;
    }

    private static float Distance(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    private static EntitySpawned SpawnMessage(ServerPlayer player) => new()
    {
        Id = player.Id, Kind = EntityKind.Player, Name = player.Name, Level = player.Progress.Level,
        MaxHp = player.Hp.Max, X = player.X, Z = player.Z,
    };

    private static EntitySpawned SpawnMessage(ServerMonster monster) => new()
    {
        Id = monster.Id, Kind = EntityKind.Monster, TypeId = monster.Data.Id, Name = monster.Data.Name,
        Level = monster.Data.Level, MaxHp = monster.Hp.Max, X = monster.X, Z = monster.Z,
    };

    private static LootSpawned LootMessage(ServerLoot loot) => new()
    {
        LootId = loot.Id, ItemId = loot.Drop.ItemId, Name = loot.Drop.Name, Rarity = (byte)loot.Drop.Rarity,
        Quantity = loot.Drop.Quantity, X = loot.X, Z = loot.Z,
    };

    private static PlayerStats StatsMessage(ServerPlayer player) => new()
    {
        Level = player.Progress.Level, Xp = player.Progress.Xp, XpToNext = player.Progress.XpToNext,
        Hp = player.Hp.Current, MaxHp = player.Hp.Max, Mp = player.Mp.Current, MaxMp = player.Mp.Max,
        Sp = player.Sp.Current, MaxSp = player.Sp.Max, Alz = player.Alz,
    };

    private static ComboStateMessage ComboMessage(ServerPlayer player)
    {
        var combo = player.Combo;
        return new ComboStateMessage
        {
            Active = combo.State == ComboPhase.Active,
            CycleStartServerTime = combo.CycleStart,
            BarDurationSeconds = (float)combo.BarDurationSeconds,
            WindowCenter = (float)combo.Config.WindowCenter,
            WindowWidth = (float)combo.WindowWidth,
            ExcellentZoneRatio = (float)combo.Config.ExcellentZoneRatio,
            HitCount = (ushort)Math.Min(combo.HitCount, ushort.MaxValue),
            ChainBonus = (float)combo.ChainBonus,
            CanFinish = combo.CanFinish,
            LastEndReason = (byte)combo.LastEndReason,
        };
    }
}
