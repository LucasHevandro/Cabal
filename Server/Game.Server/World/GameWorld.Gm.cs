using System.Globalization;
using Game.Server.Persistence;
using Game.Shared.Items;
using Game.Shared.Net;
using Game.Shared.Text;

namespace Game.Server.World;

/// <summary>O que um GM pediu que precisa do banco (o mundo não espera o banco: o ServerHost/Program cuida).</summary>
public enum ModerationKind
{
    Ban,
    Unban,
    Mute,
    Unmute,
}

/// <summary>Banir, desbanir, silenciar ou tirar o silêncio de uma conta, pelo nome do personagem.</summary>
public sealed record ModerationRequest(ModerationKind Kind, string CharacterName, int AccountId, DateTimeOffset? Until, string Reason);

/// <summary>
/// Ferramentas de GM pelo chat (/gm ...): expulsar, banir, silenciar, teleportar, dar e tirar itens e Alz, ver
/// informações e suspeitos de bot. Só contas marcadas como GM; para os outros o comando nem existe. Toda ação vira um
/// <see cref="GmActionRecord"/> (auditoria) e as que mexem em itens também entram no registro de itens do alvo.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>Um GM pediu para derrubar a conexão de um jogador (o ServerHost fecha e mostra o motivo).</summary>
    public event Action<int, LocalText>? KickRequested;

    /// <summary>Banimento ou silêncio a gravar no banco.</summary>
    public event Action<ModerationRequest>? ModerationRequested;

    /// <summary>Ação de GM feita (para gravar na auditoria).</summary>
    public event Action<GmActionRecord>? GmActionDone;

    /// <summary>Um jogador ficou suspeito de usar bot (para o log do servidor).</summary>
    public event Action<ServerPlayer, string>? SuspicionRaised;

    private const double PermanentBanYears = 100;

    private void HandleGm(ServerPlayer gm, GmCommand command, double now)
    {
        if (!gm.IsGm)
        {
            SystemChat(gm, LT("chat.unknown", "gm"));
            return;
        }
        var parts = (command.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "ajuda";
        string Arg(int i) => i < parts.Length ? parts[i] : "";
        string Rest(int from) => string.Join(' ', parts.Skip(from));

        if (verb == "ajuda")
        {
            SystemChat(gm, LT("gm.help"));
            return;
        }
        if (verb == "chefe")
        {
            var boss = _monsters.Values.FirstOrDefault(m => m.Spawn.Scheduled);
            if (SummonWorldBoss(now) && boss != null) SystemChat(gm, LT("gm.boss_spawned", Loc.Ref("monster." + boss.Data.Id)));
            else SystemChat(gm, LT("gm.help"));
            return;
        }
        if (verb == "suspeitos")
        {
            var suspects = _players.Values.Where(p => p.Suspicions.Count > 0).ToList();
            if (suspects.Count == 0) SystemChat(gm, LT("gm.no_suspects"));
            foreach (var p in suspects) SystemChat(gm, LT("gm.suspect", p.Name, string.Join(", ", p.Suspicions.Keys.Select(k => Loc.Ref(k)))));
            return;
        }

        var name = Arg(1);
        if (name.Length == 0)
        {
            SystemChat(gm, LT("gm.help"));
            return;
        }
        var target = FindPlayerByName(name);

        switch (verb)
        {
            case "ban":
            {
                if (!double.TryParse(Arg(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) || hours < 0)
                {
                    SystemChat(gm, LT("gm.usage_ban"));
                    return;
                }
                var reason = Rest(3);
                if (reason.Length == 0) reason = "-";
                var until = hours == 0 ? DateTimeOffset.UtcNow.AddYears((int)PermanentBanYears) : DateTimeOffset.UtcNow.AddHours(hours);
                if (target != null && target.IsGuest)
                {
                    SystemChat(gm, LT("gm.guest", target.Name));
                    return;
                }
                ModerationRequested?.Invoke(new ModerationRequest(ModerationKind.Ban, target?.Name ?? name, target?.AccountId ?? 0, until, reason));
                if (target != null) KickRequested?.Invoke(target.Id, new LocalText(hours == 0 ? "auth.banned_forever" : "gm.kicked_banned", reason));
                Audit(gm, "ban", target?.Name ?? name, hours == 0 ? $"permanente: {reason}" : $"{hours} h: {reason}");
                SystemChat(gm, LT("gm.done_ban", target?.Name ?? name));
                return;
            }
            case "unban":
                ModerationRequested?.Invoke(new ModerationRequest(ModerationKind.Unban, name, 0, null, ""));
                Audit(gm, "unban", name, "");
                SystemChat(gm, LT("gm.done_unban", name));
                return;
        }

        // Daqui para baixo o alvo precisa estar online.
        if (target == null)
        {
            SystemChat(gm, LT("player.offline", name));
            return;
        }

        switch (verb)
        {
            case "info":
                SystemChat(gm, LT("gm.info", target.Name, target.Progress.Level, Loc.Ref(target.Class == Game.Shared.Accounts.CharacterClass.Arcanist ? "class.arcanist" : "class.blade"),
                    target.Alz, $"{target.X:0}, {target.Z:0}", target.IsGuest ? Loc.Ref("gm.guest_tag") : target.AccountId.ToString(CultureInfo.InvariantCulture),
                    $"{(now - target.JoinedAt) / 60:0}"));
                break;
            case "kick":
            {
                var reason = Rest(2);
                KickRequested?.Invoke(target.Id, new LocalText("gm.kicked", reason.Length == 0 ? "-" : reason));
                Audit(gm, "kick", target.Name, reason);
                SystemChat(gm, LT("gm.done_kick", target.Name));
                break;
            }
            case "mute":
            {
                if (!double.TryParse(Arg(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) || minutes <= 0)
                {
                    SystemChat(gm, LT("gm.usage_mute"));
                    return;
                }
                var until = DateTimeOffset.UtcNow.AddMinutes(minutes);
                target.MutedUntil = until;
                if (!target.IsGuest) ModerationRequested?.Invoke(new ModerationRequest(ModerationKind.Mute, target.Name, target.AccountId, until, Rest(3)));
                SystemChat(target, LT("chat.muted_now", minutes));
                Audit(gm, "mute", target.Name, $"{minutes} min: {Rest(3)}");
                SystemChat(gm, LT("gm.done_mute", target.Name, minutes));
                break;
            }
            case "unmute":
                target.MutedUntil = null;
                if (!target.IsGuest) ModerationRequested?.Invoke(new ModerationRequest(ModerationKind.Unmute, target.Name, target.AccountId, null, ""));
                SystemChat(target, LT("chat.unmuted"));
                Audit(gm, "unmute", target.Name, "");
                SystemChat(gm, LT("gm.done_unmute", target.Name));
                break;
            case "ir":
                Teleport(gm, target.X + 1.5f, target.Z, now);
                Audit(gm, "ir", target.Name, $"{target.X:0}, {target.Z:0}");
                break;
            case "trazer":
                Teleport(target, gm.X + 1.5f, gm.Z, now);
                SystemChat(target, LT("gm.brought", gm.Name));
                Audit(gm, "trazer", target.Name, $"{gm.X:0}, {gm.Z:0}");
                break;
            case "dar":
            case "tirar":
            {
                var itemId = Arg(2);
                var definition = ItemCatalog.Find(itemId);
                var quantity = int.TryParse(Arg(3), out var q) ? Math.Clamp(q, 1, 999) : 1;
                if (definition == null)
                {
                    SystemChat(gm, LT("gm.unknown_item", itemId));
                    return;
                }
                if (verb == "dar")
                {
                    if (!target.Bag.TryAdd(itemId, quantity, _uids.Next))
                    {
                        SystemChat(gm, LT("gm.bag_full", target.Name));
                        return;
                    }
                    Log(target, "gm_dar", itemId, quantity, 0, $"por {gm.Name}");
                }
                else
                {
                    quantity = Math.Min(quantity, target.Bag.Count(itemId));
                    if (quantity == 0 || !target.Bag.TryRemove(itemId, quantity))
                    {
                        SystemChat(gm, LT("gm.no_item", target.Name, Loc.Ref("item." + itemId)));
                        return;
                    }
                    Log(target, "gm_tirar", itemId, -quantity, 0, $"por {gm.Name}");
                }
                target.InventoryDirty = true;
                Audit(gm, verb, target.Name, $"{itemId} x{quantity}");
                SystemChat(gm, LT(verb == "dar" ? "gm.done_give" : "gm.done_take", target.Name, Loc.Ref("item." + itemId), quantity));
                break;
            }
            case "alz":
            {
                if (!long.TryParse(Arg(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var delta) || delta == 0)
                {
                    SystemChat(gm, LT("gm.usage_alz"));
                    return;
                }
                delta = Math.Max(delta, -target.Alz); // nunca negativo
                target.Alz += delta;
                target.StatsDirty = true;
                Log(target, "gm_alz", null, 0, delta, $"por {gm.Name}");
                Audit(gm, "alz", target.Name, delta.ToString(CultureInfo.InvariantCulture));
                SystemChat(gm, LT("gm.done_alz", target.Name, delta, target.Alz));
                break;
            }
            default:
                SystemChat(gm, LT("gm.help"));
                break;
        }
    }

    /// <summary>Move o jogador na hora (o validador de movimento aceita) e avisa o cliente dele.</summary>
    private void Teleport(ServerPlayer player, float x, float z, double now)
    {
        player.Movement.Teleport(x, z, now);
        _outbox.Send(player.Id, new PositionCorrection { X = player.X, Z = player.Z });
    }

    private void Audit(ServerPlayer gm, string action, string target, string details) =>
        GmActionDone?.Invoke(new GmActionRecord(gm.Name, action, target, details, DateTimeOffset.UtcNow));
}
