using System.Numerics;
using Game.Server.Persistence;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Core;
using Game.Shared.Items;
using Game.Shared.Monsters;
using Game.Shared.Net;
using Game.Shared.Progression;
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
    private readonly ItemUidAllocator _uids;
    private readonly DungeonTuning _dungeonTuning;
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
    public GameWorld(IOutbox outbox, IEnumerable<SpawnDefinition>? spawns = null, IRandomSource? random = null,
        ItemUidAllocator? uids = null, DungeonTuning? dungeonTuning = null)
    {
        _outbox = outbox;
        _dungeonTuning = dungeonTuning ?? DungeonTuning.Normal;
        _random = random ?? new SystemRandomSource();
        _uids = uids ?? new ItemUidAllocator();

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
    /// Com <paramref name="character"/> (vindo do banco), volta exatamente como estava: nível, XP, recursos, Alz,
    /// itens, atributos, missões e posição. Sem ele, é um convidado novo (não é salvo) com a arma inicial.
    /// </summary>
    public ServerPlayer AddPlayer(string name, double now, Action<int>? onIdAssigned = null, CharacterRecord? character = null)
    {
        var cleanName = string.IsNullOrWhiteSpace(name) ? "Aventureiro" : name.Trim();
        if (character != null) cleanName = character.Name;
        var id = _nextEntityId++;
        var (spawnX, spawnZ) = SpawnPointFor(id);
        if (character != null && IsInsideWorld(character.X, character.Z)) (spawnX, spawnZ) = (character.X, character.Z);
        var player = new ServerPlayer(character?.Class ?? CharacterClass.Blade)
        {
            Id = id,
            Name = cleanName.Length > 20 ? cleanName[..20] : cleanName,
            CharacterId = character?.Id ?? 0,
            Movement = new MovementValidator(spawnX, spawnZ, now),
        };
        if (character != null) Restore(player, character);
        else GiveGuestKit(player);
        onIdAssigned?.Invoke(player.Id);

        _outbox.Send(player.Id, new Welcome
        {
            PlayerId = player.Id, ServerTime = now, TickRate = TickRate, SpawnX = spawnX, SpawnZ = spawnZ,
        });
        _players.Add(player.Id, player);
        NotifyWatchers(player, $"{player.Name} entrou no jogo");
        return player;
    }

    /// <summary>Verdadeiro se o personagem do banco já está no mundo (impede entrar duas vezes ao mesmo tempo).</summary>
    public bool IsCharacterOnline(int characterId) => characterId != 0 && _players.Values.Any(p => p.CharacterId == characterId);

    /// <summary>
    /// Foto do estado para salvar, e os registros de itens desde o último salvamento (que saem da fila do jogador).
    /// Morto salva com a vida cheia e na vila, para não voltar morto.
    /// </summary>
    public (CharacterSnapshot Snapshot, List<ItemLogEntry> Logs) CaptureForSave(ServerPlayer player)
    {
        // Na dungeon (ou morto) salva fora: na volta aparece no portal ou na vila.
        var (x, z) = player.DungeonId != 0 ? (NpcCatalog.Find(NpcCatalog.DungeonPortal)!.X, NpcCatalog.Find(NpcCatalog.DungeonPortal)!.Z + 2.5f)
            : player.IsDead ? SpawnPointFor(player.Id) : (player.X, player.Z);
        var items = new List<ItemRecord>();
        foreach (var (slot, item) in player.Bag.Items()) items.Add(ToRecord(item, ItemContainerKind.Bag, slot));
        foreach (var (slot, item) in player.Warehouse.Items()) items.Add(ToRecord(item, ItemContainerKind.Warehouse, slot));
        foreach (var (slot, item) in player.Equipment.Worn) items.Add(ToRecord(item, ItemContainerKind.Equipment, (int)slot));
        var quests = player.Quests.Completed.Select(id => new QuestRecord(id, true, Array.Empty<int>()))
            .Concat(player.Quests.Active.Select(q => new QuestRecord(q.QuestId, false, q.Counts.ToArray())))
            .ToList();
        var snapshot = new CharacterSnapshot(
            player.CharacterId, player.Progress.Level, player.Progress.Xp, player.Alz, x, z,
            player.IsDead ? player.Hp.Max : player.Hp.Current, player.IsDead ? player.Mp.Max : player.Mp.Current, player.Sp.Current,
            items, player.Attributes, player.Awakening.Gauge, quests)
        {
            Relations = player.Relations.ToList(),
            Reports = player.PendingReports.ToList(),
        };
        player.PendingReports.Clear();
        var logs = player.PendingLogs.ToList();
        player.PendingLogs.Clear();
        return (snapshot, logs);
    }

    public void RemovePlayer(int playerId)
    {
        if (!_players.TryGetValue(playerId, out var leaving)) return;
        _players.Remove(playerId);
        if (leaving.PartyId != 0) LeaveParty(leaving, $"{leaving.Name} saiu do jogo e deixou o grupo");
        NotifyWatchers(leaving, $"{leaving.Name} saiu do jogo");
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
            // Durante a troca a bolsa não se mexe (só usar poção): a oferta aponta para posições da bolsa.
            case ItemCommand command when player.TradeId != 0 && command.Action != ItemActionKind.Use:
            case UpgradeItem or ApplyOption when player.TradeId != 0:
                SystemChat(player, "Termine a troca antes de mexer na bolsa");
                break;
            case ItemCommand command:
                HandleItemCommand(player, command, now);
                break;
            case UpgradeItem upgrade:
                HandleUpgrade(player, upgrade);
                break;
            case ApplyOption option:
                HandleApplyOption(player, option);
                break;
            case ShopCommand shop:
                HandleShop(player, shop);
                break;
            case TradeCommand trade:
                HandleTrade(player, trade, now);
                break;
            case AllocateAttributes allocate:
                HandleAllocate(player, allocate);
                break;
            case TalkToNpc talk:
                HandleTalk(player, talk);
                break;
            case QuestCommand quest:
                HandleQuest(player, quest);
                break;
            case ActivateAwakening:
                HandleAwakening(player, now);
                break;
            case PartyCommand party:
                HandleParty(player, party, now);
                break;
            case ChatSend chat:
                HandleChat(player, chat, now);
                break;
            case SocialCommand social:
                HandleRelation(player, social, now);
                break;
            case DungeonCommand dungeon:
                HandleDungeon(player, dungeon, now);
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
        TickDungeons(now);
        ExpireLoot(now);
        TickTrades();

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
            if (player.DungeonId != 0 && ClampToDungeon(player, now))
                _outbox.Send(player.Id, new PositionCorrection { X = player.X, Z = player.Z });
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
        if (player.InventoryDirty)
        {
            player.InventoryDirty = false;
            _outbox.Send(player.Id, InventoryMessage(player));
        }
        if (player.SheetDirty)
        {
            player.SheetDirty = false;
            _outbox.Send(player.Id, SheetMessage(player));
        }
        if (player.QuestsDirty)
        {
            player.QuestsDirty = false;
            _outbox.Send(player.Id, QuestMessage(player));
        }
        if (player.ComboDirty)
        {
            player.ComboDirty = false;
            _outbox.Send(player.Id, ComboMessage(player));
        }
        SendPartyState(player);
        SendSocialLists(player);
        SendTradeState(player);
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

    private ServerMonster CreateMonster(SpawnDefinition spawn, float x, float z, int instanceId = 0)
    {
        var monster = new ServerMonster
        {
            Id = _nextEntityId++,
            InstanceId = instanceId,
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
        return monster;
    }

    private void Restore(ServerPlayer player, CharacterRecord character)
    {
        player.Progress = new CharacterProgress(Math.Clamp(character.Level, 1, LevelCurve.MaxLevel), Math.Max(0, character.Xp));
        // Atributos estragados (ou de antes dos atributos) voltam para os iniciais da classe.
        var attributes = character.Attributes;
        var starting = Attributes.StartingFor(player.Class);
        if (attributes.Strength < starting.Strength || attributes.Dexterity < starting.Dexterity ||
            attributes.Intelligence < starting.Intelligence || Attributes.FreePoints(player.Progress.Level, player.Class, attributes) < 0)
            attributes = starting;
        player.Attributes = attributes;
        player.Alz = Math.Max(0, character.Alz);
        player.Awakening.Restore(character.AwakeningGauge);

        // Itens: cada um volta para o lugar salvo; se o lugar não servir mais, vai para a primeira posição livre da bolsa.
        var misplaced = new List<ItemInstance>();
        var overflow = new List<(string ItemId, int Quantity)>();
        foreach (var record in character.Items)
        {
            var definition = ItemCatalog.Find(record.ItemId);
            if (definition == null || record.Quantity <= 0) continue;
            var item = new ItemInstance
            {
                Uid = record.Uid, ItemId = record.ItemId, Quantity = Math.Min(record.Quantity, definition.MaxStack),
                Upgrade = Math.Clamp(record.Upgrade, 0, UpgradeRules.MaxLevel), PityFails = Math.Max(0, record.PityFails),
                PityLevel = record.PityLevel, RegainGuaranteed = record.RegainGuaranteed, OptionId = record.OptionId ?? "",
            };
            var placed = record.Container switch
            {
                ItemContainerKind.Equipment when definition.Kind == ItemKind.Equipment && (int)definition.Slot == record.Slot
                    && player.Equipment[definition.Slot] == null => Wear(player, definition.Slot, item),
                ItemContainerKind.Warehouse => player.Warehouse.TryPut(record.Slot, item),
                ItemContainerKind.Bag => player.Bag.TryPut(record.Slot, item),
                _ => false,
            };
            if (!placed) misplaced.Add(item);
            if (record.Quantity > item.Quantity) overflow.Add((item.ItemId, record.Quantity - item.Quantity));
        }
        foreach (var item in misplaced)
        {
            if (!player.Bag.TryPlace(item, out _)) player.Warehouse.TryPlace(item, out _);
        }
        // Pilhas maiores que o limite (dados antigos) viram várias pilhas.
        foreach (var (itemId, quantity) in overflow)
        {
            if (!player.Bag.TryAdd(itemId, quantity, _uids.Next)) player.Warehouse.TryAdd(itemId, quantity, _uids.Next);
        }

        player.Quests.Restore(
            character.Quests.Where(q => q.Completed).Select(q => q.QuestId),
            character.Quests.Where(q => !q.Completed).Select(q => new QuestProgress(q.QuestId, q.Counts.ToArray())));

        // Relações repetidas ou consigo mesmo (dados estragados) ficam de fora.
        foreach (var relation in character.Relations)
        {
            if (relation.OtherId == 0 || relation.OtherId == character.Id || player.HasRelation(relation.Kind, relation.OtherId)) continue;
            player.Relations.Add(relation);
        }

        player.RecalculateStats();
        player.Hp.Add(Math.Clamp(character.Hp, 1, player.Hp.Max) - player.Hp.Current);
        player.Mp.Add(Math.Clamp(character.Mp, 0, player.Mp.Max) - player.Mp.Current);
        player.Sp.Add(Math.Clamp(character.Sp, 0, player.Sp.Max) - player.Sp.Current);
    }

    private static bool Wear(ServerPlayer player, EquipSlot slot, ItemInstance item)
    {
        player.Equipment.Put(slot, item);
        return true;
    }

    /// <summary>Convidado começa com a arma da classe vestida e algumas poções.</summary>
    private void GiveGuestKit(ServerPlayer player)
    {
        var weapon = player.Class == CharacterClass.Arcanist ? "cajado_ferro" : "espada_ferro";
        player.Equipment.Put(EquipSlot.Weapon, new ItemInstance { Uid = _uids.Next(), ItemId = weapon });
        player.Bag.TryAdd("pocao_hp_p", 5, _uids.Next);
        player.Bag.TryAdd(Game.Shared.Dungeon.DungeonCatalog.CryptKey, 3, _uids.Next); // bots e testes entram na cripta
        player.RecalculateStats();
        player.Hp.Add(player.Hp.Max);
        player.Mp.Add(player.Mp.Max);
    }

    private static ItemRecord ToRecord(ItemInstance item, ItemContainerKind container, int slot) => new(
        item.Uid, container, slot, item.ItemId, item.Quantity, item.Upgrade, item.PityFails, item.PityLevel,
        item.RegainGuaranteed, item.OptionId);

    /// <summary>Posição salva dentro do chão do mapa (protege contra dados estragados).</summary>
    private static bool IsInsideWorld(float x, float z) =>
        !float.IsNaN(x) && !float.IsNaN(z) && x >= -130 && x <= 130 && z >= -70 && z <= 230;

    /// <summary>Ponto de nascimento na vila, espalhado num círculo de 3 m para jogadores não nascerem um dentro do outro.</summary>
    public static (float X, float Z) SpawnPointFor(int playerId)
    {
        var angle = playerId * 2.39996f; // ângulo áureo: pontos bem distribuídos
        var radius = 1.5f + playerId % 3 * 0.75f;
        return (WorldContent.PlayerSpawnX + MathF.Cos(angle) * radius, WorldContent.PlayerSpawnZ + MathF.Sin(angle) * radius);
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

    private static Inventory InventoryMessage(ServerPlayer player)
    {
        var message = new Inventory();
        void Add(ItemInstance item, ItemContainerKind container, int slot)
        {
            var definition = ItemCatalog.Get(item.ItemId);
            message.Items.Add(new InventoryStack
            {
                ItemId = item.ItemId, Name = definition.Name, Rarity = (byte)definition.Rarity, Quantity = item.Quantity,
                Container = container, Slot = (ushort)slot, Uid = item.Uid, Upgrade = (byte)item.Upgrade,
                PityFails = (byte)Math.Min(item.PityFails, 255), PityLevel = (byte)item.PityLevel,
                RegainGuaranteed = item.RegainGuaranteed, OptionId = item.OptionId,
            });
        }
        foreach (var (slot, item) in player.Bag.Items()) Add(item, ItemContainerKind.Bag, slot);
        foreach (var (slot, item) in player.Equipment.Worn) Add(item, ItemContainerKind.Equipment, (int)slot);
        foreach (var (slot, item) in player.Warehouse.Items()) Add(item, ItemContainerKind.Warehouse, slot);
        return message;
    }

    private static CharacterSheet SheetMessage(ServerPlayer player)
    {
        var sheet = new CharacterSheet
        {
            Class = (byte)player.Class, Strength = (ushort)player.Attributes.Strength, Dexterity = (ushort)player.Attributes.Dexterity,
            Intelligence = (ushort)player.Attributes.Intelligence, FreePoints = (ushort)Math.Max(0, player.FreeAttributePoints),
            AttackPower = player.Stats.AttackPower, MagicPower = player.Stats.MagicPower, Defense = player.Stats.Defense,
            CritPercent = player.Stats.CritPercent, ComboUnlocked = player.ComboUnlocked,
            AwakeningGauge = (byte)player.Awakening.Gauge,
            AwakeningUntil = double.IsInfinity(player.Awakening.ActiveUntil) ? 0 : player.Awakening.ActiveUntil,
        };
        for (var i = 0; i < player.ClassSkills.Count; i++) sheet.SkillUnlocked.Add(player.SkillUnlocked(i));
        return sheet;
    }

    private static QuestState QuestMessage(ServerPlayer player)
    {
        var state = new QuestState();
        state.Completed.AddRange(player.Quests.Completed);
        foreach (var progress in player.Quests.Active)
        {
            // Coletar mostra o que está na bolsa agora (o diário não guarda contador para isso).
            var counts = new List<int>();
            for (var i = 0; i < progress.Counts.Length; i++) counts.Add(QuestLog.Done(progress, i, player.Bag));
            state.Active.Add(new QuestEntry { QuestId = progress.QuestId, Counts = counts });
        }
        return state;
    }

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
