using System.Numerics;
using Game.Server.Persistence;
using Game.Shared.Combat;
using Game.Shared.Monsters;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.World;
using ComboPhase = Game.Shared.Combat.ComboState;

namespace Game.Server.World;

/// <summary>Combate, monstros, drops e chefe — tudo decidido aqui, com as regras do Shared.</summary>
public sealed partial class GameWorld
{
    /// <summary>
    /// Maior compensação de latência aceita para o timing do combo. O cliente informa quando apertou;
    /// o servidor aceita esse horário se não for no futuro nem mais antigo que isto.
    /// </summary>
    public const double MaxLagCompensationSeconds = 0.35;

    /// <summary>Horário do aperto que o servidor usa para julgar o combo.</summary>
    public static double PressTime(double clientTime, double now) =>
        double.IsNaN(clientTime) ? now : Math.Clamp(clientTime, now - MaxLagCompensationSeconds, now);

    // ===== Jogador =====

    private void TickPlayer(ServerPlayer player, double now, float dt)
    {
        if (player.IsDead)
        {
            if (now >= player.RespawnAt) Respawn(player, now);
            return;
        }

        if (player.Combo.Tick(now))
        {
            player.ComboDirty = true;
            Notify(player, $"Combo encerrado: tempo esgotado ({player.Combo.HitCount} acertos)");
        }

        player.MpRegenCarry += PlayerRules.MpRegenPerSecond * dt;
        var whole = (int)player.MpRegenCarry;
        if (whole > 0)
        {
            player.MpRegenCarry -= whole;
            if (player.Mp.Add(whole) != 0) player.StatsDirty = true;
        }
    }

    private void HandleStartCombo(ServerPlayer player, StartCombo message, double now)
    {
        if (player.IsDead || player.Combo.State == ComboPhase.Active) return;
        var target = FindMonster(message.TargetId);
        if (target == null || target.IsDead)
        {
            Notify(player, "Selecione um alvo para o combo");
            return;
        }
        var cost = player.Combo.Config.ActivationSpCost;
        if (!player.Sp.TrySpend(cost))
        {
            Notify(player, $"SP insuficiente ({player.Sp.Current}/{cost})");
            return;
        }
        player.Combo.TryStart(PressTime(message.ClientTime, now));
        player.ComboDirty = true;
        player.StatsDirty = true;
    }

    private void HandleUseSkill(ServerPlayer player, UseSkill message, double now)
    {
        if (player.IsDead || message.Slot >= player.Skills.Count) return;
        var skill = player.Skills[message.Slot];
        var target = FindMonster(message.TargetId);
        double? distance = target != null && !target.IsDead ? EdgeDistance(player, target) : (double?)null;

        switch (SkillRules.Check(skill, message.Slot, player.Cooldowns, player.Mp, distance, now))
        {
            case SkillUseCheck.NoTarget:
                Notify(player, "Selecione um alvo");
                return;
            case SkillUseCheck.OnCooldown:
                if (player.Combo.State == ComboPhase.Active)
                {
                    // Como no CABAL: skill em recarga durante o combo quebra a corrente.
                    player.Combo.Cancel();
                    player.ComboDirty = true;
                    Notify(player, $"Combo encerrado: {skill.Name} em recarga");
                }
                else
                {
                    Notify(player, $"{skill.Name} em recarga");
                }
                return;
            case SkillUseCheck.OutOfRange:
                Notify(player, "Fora de alcance");
                return;
            case SkillUseCheck.NotEnoughMp:
                Notify(player, $"MP insuficiente ({player.Mp.Current}/{skill.MpCost})");
                return;
        }

        var multiplier = 1.0;
        byte grade = 255;
        var routeName = "";
        var inCombo = player.Combo.State == ComboPhase.Active;
        if (inCombo)
        {
            var result = player.Combo.Press(PressTime(message.ClientTime, now), skill.Kind);
            player.ComboDirty = true;
            if (result.ComboEnded)
            {
                Notify(player, $"Combo encerrado: fora da janela ({result.HitCount} acertos)");
            }
            else
            {
                multiplier = result.DamageMultiplier;
                grade = (byte)result.Grade;
                routeName = result.Route?.Name ?? "";
            }
        }

        SkillRules.TryUse(skill, message.Slot, player.Cooldowns, player.Mp, distance, now);
        player.StatsDirty = true;

        var hits = 0;
        foreach (var victim in SkillTargets(skill, target!))
        {
            var hit = DamageFormula.Calculate(
                new DamageInput(skill.BaseDamage, player.Stats.AttackPower, multiplier, PlayerRules.CritChance,
                    PlayerRules.CritMultiplier, victim.Data.Defense), _random);
            hits++;
            DamagePlayerToMonster(player, victim, hit, message.Slot, grade, routeName, now);
        }
        if (!inCombo && hits > 0) player.Sp.Add(PlayerRules.SpPerHit);
    }

    private void HandleFinisher(ServerPlayer player, Finisher message, double now)
    {
        if (player.IsDead || !player.Combo.CanFinish) return;
        var target = FindMonster(message.TargetId);
        if (target == null || target.IsDead || EdgeDistance(player, target) > PlayerRules.FinisherRange)
        {
            Notify(player, "Chegue perto do alvo para finalizar");
            return;
        }

        var chain = player.Combo.HitCount;
        player.Combo.TryFinish(out var multiplier);
        player.ComboDirty = true;
        var hit = DamageFormula.Calculate(
            new DamageInput(PlayerRules.FinisherBaseDamage, player.Stats.AttackPower, multiplier, PlayerRules.CritChance,
                PlayerRules.CritMultiplier, target.Data.Defense), _random);
        DamagePlayerToMonster(player, target, hit, CombatSource.Finisher, 255, "", now);
        Notify(player, $"Finalizador x{multiplier:0.00} depois de {chain} acertos");
    }

    private IEnumerable<ServerMonster> SkillTargets(SkillData skill, ServerMonster main)
    {
        if (skill.AreaRadius <= 0)
        {
            yield return main;
            yield break;
        }
        foreach (var monster in _monsters.Values.ToList())
        {
            if (!monster.IsDead && InRange(monster.X, monster.Z, main.X, main.Z, skill.AreaRadius)) yield return monster;
        }
    }

    private void DamagePlayerToMonster(ServerPlayer player, ServerMonster monster, DamageResult hit, byte source,
        byte grade, string routeName, double now)
    {
        var dealt = -monster.Hp.Add(-hit.Amount);
        monster.DamageBy[player.Id] = monster.DamageBy.GetValueOrDefault(player.Id) + dealt;
        monster.AggroPlayerId = player.Id;
        monster.Brain.Provoke();

        SendNearby(monster.X, monster.Z, new CombatEvent
        {
            AttackerId = player.Id, TargetId = monster.Id, Source = source, Damage = dealt, Critical = hit.Critical,
            Grade = grade, RouteName = routeName,
        });

        if (monster.Hp.Current == 0) KillMonster(monster, now);
    }

    private void Respawn(ServerPlayer player, double now)
    {
        player.IsDead = false;
        player.Hp.Add(player.Hp.Max);
        player.Mp.Add(player.Mp.Max);
        var (spawnX, spawnZ) = SpawnPointFor(player.Id);
        player.Movement.Teleport(spawnX, spawnZ, now);
        player.StatsDirty = true;
        _outbox.Send(player.Id, new PositionCorrection { X = player.X, Z = player.Z });
        Notify(player, "Você renasceu na vila");
    }

    private void DamageMonsterToPlayer(ServerMonster monster, ServerPlayer player, int baseDamage, byte source, double now)
    {
        if (player.IsDodging(now))
        {
            SendNearby(player.X, player.Z, new CombatEvent { AttackerId = monster.Id, TargetId = player.Id, Source = source, Dodged = true });
            return;
        }

        var hit = DamageFormula.Calculate(
            new DamageInput(baseDamage, monster.Data.AttackPower, 1.0, 0.05, 1.5, player.Stats.Defense), _random);
        var dealt = -player.Hp.Add(-hit.Amount);
        player.StatsDirty = true;
        SendNearby(player.X, player.Z, new CombatEvent
        {
            AttackerId = monster.Id, TargetId = player.Id, Source = source, Damage = dealt, Critical = hit.Critical,
        });

        if (player.Hp.Current > 0) return;
        player.IsDead = true;
        player.RespawnAt = now + PlayerRules.RespawnSeconds;
        if (player.Combo.State == ComboPhase.Active)
        {
            player.Combo.Cancel();
            player.ComboDirty = true;
        }
        foreach (var m in _monsters.Values)
        {
            if (m.AggroPlayerId == player.Id) m.AggroPlayerId = 0;
        }
        Notify(player, "Você morreu", announcement: true);
    }

    // ===== Monstros =====

    private void TickMonster(ServerMonster monster, double now, float dt)
    {
        if (monster.IsDead)
        {
            if (now >= monster.RespawnAt) RespawnMonster(monster, now);
            return;
        }

        var target = ChooseTarget(monster);
        var action = monster.Brain.Tick(now, new Vector2(monster.X, monster.Z),
            target != null ? new Vector2(target.X, target.Z) : (Vector2?)null);

        switch (action.Kind)
        {
            case MonsterActionKind.MoveTo:
                MoveMonster(monster, action.Point, dt);
                break;
            case MonsterActionKind.Face:
            case MonsterActionKind.AttackStarted:
                Face(monster, action.Point);
                break;
            case MonsterActionKind.AttackLanded:
                if (target != null && !target.IsDead &&
                    Distance(monster.X, monster.Z, target.X, target.Z) - PlayerRules.BodyRadius
                    <= monster.Data.AttackRange * PlayerRules.MonsterReachTolerance)
                {
                    DamageMonsterToPlayer(monster, target, monster.Data.AttackDamage, CombatSource.MonsterAttack, now);
                }
                break;
            case MonsterActionKind.ResetHealth:
                ResetMonster(monster);
                break;
        }

        if (monster.Spawn.IsBoss) TickBoss(monster, target, now);
    }

    /// <summary>Quem o monstro persegue: quem bateu nele por último; se ninguém, o jogador vivo mais perto.</summary>
    private ServerPlayer? ChooseTarget(ServerMonster monster)
    {
        if (monster.AggroPlayerId != 0 && _players.TryGetValue(monster.AggroPlayerId, out var aggro) && !aggro.IsDead)
            return aggro;

        ServerPlayer? nearest = null;
        var best = float.MaxValue;
        foreach (var player in _players.Values)
        {
            if (player.IsDead) continue;
            var distance = Distance(monster.X, monster.Z, player.X, player.Z);
            if (distance < best)
            {
                best = distance;
                nearest = player;
            }
        }
        return nearest;
    }

    private static void MoveMonster(ServerMonster monster, Vector2 point, float dt)
    {
        var dx = point.X - monster.X;
        var dz = point.Y - monster.Z;
        var distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance < 0.01f) return;
        var step = MathF.Min(distance, monster.Data.MoveSpeed * dt);
        monster.X += dx / distance * step;
        monster.Z += dz / distance * step;
        monster.Yaw = MathF.Atan2(dx, dz) * 180f / MathF.PI;
    }

    private static void Face(ServerMonster monster, Vector2 point)
    {
        var dx = point.X - monster.X;
        var dz = point.Y - monster.Z;
        if (dx * dx + dz * dz > 0.0001f) monster.Yaw = MathF.Atan2(dx, dz) * 180f / MathF.PI;
    }

    private void ResetMonster(ServerMonster monster)
    {
        monster.Hp.Add(monster.Hp.Max);
        monster.DamageBy.Clear();
        monster.AggroPlayerId = 0;
        if (monster.Spawn.IsBoss) EndBossPhaseTwo(monster);
    }

    private void KillMonster(ServerMonster monster, double now)
    {
        monster.Brain.Kill();
        monster.RespawnAt = now + monster.Data.RespawnSeconds;
        monster.PendingArea = null;

        // Quem causou mais dano fica com o XP e é o dono do drop.
        var killerId = monster.DamageBy.Count == 0 ? 0 : monster.DamageBy.MaxBy(kv => kv.Value).Key;
        monster.DamageBy.Clear();
        monster.AggroPlayerId = 0;
        if (!_players.TryGetValue(killerId, out var killer)) return;

        var xp = XpRules.Adjusted(monster.Data.XpReward, killer.Progress.Level, monster.Data.Level);
        var levels = killer.Progress.AddXp(xp);
        if (levels > 0)
        {
            var stats = killer.Stats;
            killer.Hp.SetMax(stats.MaxHp);
            killer.Mp.SetMax(stats.MaxMp);
            killer.Hp.Add(killer.Hp.Max);
            killer.Mp.Add(killer.Mp.Max);
            Notify(killer, $"Nível {killer.Progress.Level}!", announcement: true);
        }

        var loot = monster.Data.Drops.Roll(_random);
        killer.Alz += loot.Alz;
        killer.StatsDirty = true;
        if (killer.CharacterId != 0)
        {
            var at = DateTimeOffset.UtcNow;
            if (loot.Alz > 0)
                killer.PendingLogs.Add(new ItemLogEntry(killer.CharacterId, at, "alz_monstro", null, 0, loot.Alz, $"{monster.Data.Id} (id {monster.Id})"));
            if (levels > 0)
                killer.PendingLogs.Add(new ItemLogEntry(killer.CharacterId, at, "nivel", null, 0, 0, $"nível {killer.Progress.Level}"));
        }
        Notify(killer, $"+{xp} XP, +{loot.Alz} Alz");

        for (var i = 0; i < loot.Items.Count; i++)
        {
            var angle = i * 2.4f;
            var id = _nextLootId++;
            _loot.Add(id, new ServerLoot
            {
                Id = id, Drop = loot.Items[i], OwnerId = killer.Id,
                X = monster.X + MathF.Cos(angle) * (0.6f + 0.25f * i), Z = monster.Z + MathF.Sin(angle) * (0.6f + 0.25f * i),
                OwnerUntil = now + ServerLoot.OwnerSeconds, ExpiresAt = now + ServerLoot.LifetimeSeconds,
            });
        }
    }

    private void RespawnMonster(ServerMonster monster, double now)
    {
        monster.X = monster.HomeX;
        monster.Z = monster.HomeZ;
        monster.Hp.Add(monster.Hp.Max);
        monster.Brain.Revive(now);
        if (monster.Spawn.IsBoss) EndBossPhaseTwo(monster);
    }

    // ===== Chefe =====

    private void TickBoss(ServerMonster boss, ServerPlayer? target, double now)
    {
        var fraction = boss.Hp.Current / (float)boss.Hp.Max;
        if (!boss.PhaseTwo && fraction <= BossRules.PhaseTwoAtHpFraction)
        {
            boss.PhaseTwo = true;
            boss.Data.AttackCooldownSeconds = (float)(boss.BaseAttackCooldown * BossRules.PhaseTwoCooldownFactor);
            boss.NextAreaAt = now + 1.5;
            foreach (var player in _players.Values)
            {
                if (InRange(player.X, player.Z, boss.X, boss.Z, InterestRadius))
                    Notify(player, $"{boss.Data.Name} ficou furioso!", announcement: true);
            }
        }
        if (!boss.PhaseTwo) return;

        if (boss.PendingArea is { } area)
        {
            if (now < area.LandsAt) return;
            boss.PendingArea = null;
            boss.NextAreaAt = now + BossRules.AreaIntervalSeconds;
            foreach (var player in _players.Values.ToList())
            {
                if (!player.IsDead && Distance(player.X, player.Z, area.X, area.Z) <= BossRules.AreaRadius + PlayerRules.BodyRadius)
                    DamageMonsterToPlayer(boss, player, BossRules.AreaDamage, CombatSource.BossArea, now);
            }
            return;
        }

        var fighting = boss.Brain.State is MonsterState.Chase or MonsterState.WindUp;
        if (!fighting || target == null || now < boss.NextAreaAt) return;
        boss.PendingArea = (target.X, target.Z, now + BossRules.AreaWarningSeconds);
        SendNearby(boss.X, boss.Z, new AreaWarning
        {
            SourceId = boss.Id, X = target.X, Z = target.Z, Radius = BossRules.AreaRadius, Seconds = BossRules.AreaWarningSeconds,
        });
    }

    private static void EndBossPhaseTwo(ServerMonster boss)
    {
        boss.PhaseTwo = false;
        boss.PendingArea = null;
        boss.Data.AttackCooldownSeconds = (float)boss.BaseAttackCooldown;
    }

    // ===== Itens no chão =====

    private void HandlePickup(ServerPlayer player, PickupLoot message, double now)
    {
        if (player.IsDead || !_loot.TryGetValue(message.LootId, out var loot)) return;
        if (Distance(player.X, player.Z, loot.X, loot.Z) > PlayerRules.PickupRange + 0.5f)
        {
            Notify(player, "Longe demais para pegar");
            return;
        }
        if (!loot.CanBePickedBy(player.Id, now))
        {
            Notify(player, "Este item ainda é de outro jogador");
            return;
        }

        var drop = loot.Drop;
        var current = player.Inventory.TryGetValue(drop.ItemId, out var stack) ? stack.Quantity : 0;
        player.Inventory[drop.ItemId] = (drop.Name, drop.Rarity, current + drop.Quantity);
        player.InventoryDirty = true;
        // O item sai do chão no mesmo passo em que entra no inventário: não existe momento com dois.
        RemoveLoot(loot, player.Id);
        if (player.CharacterId != 0)
            player.PendingLogs.Add(new ItemLogEntry(player.CharacterId, DateTimeOffset.UtcNow, "coleta", drop.ItemId, drop.Quantity, 0,
                $"item no chão {loot.Id}, dono {loot.OwnerId}"));
        Notify(player, $"{drop.Name} x{drop.Quantity}");
    }

    private void ExpireLoot(double now)
    {
        foreach (var loot in _loot.Values.Where(l => now >= l.ExpiresAt).ToList()) RemoveLoot(loot, 0);
    }

    private void RemoveLoot(ServerLoot loot, int pickedBy)
    {
        _loot.Remove(loot.Id);
        foreach (var player in _players.Values)
        {
            if (player.KnownLoot.Remove(loot.Id))
                _outbox.Send(player.Id, new LootRemoved { LootId = loot.Id, PickedById = pickedBy });
        }
    }

    private static double EdgeDistance(ServerPlayer player, ServerMonster monster) =>
        Distance(player.X, player.Z, monster.X, monster.Z) - monster.Data.BodyRadius;
}
