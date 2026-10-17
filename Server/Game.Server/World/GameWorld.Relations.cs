using Game.Server.Persistence;
using Game.Shared.Accounts;
using Game.Shared.Net;
using Game.Shared.Social;

namespace Game.Server.World;

/// <summary>
/// Amigos (lista simples, de um lado só), bloqueio (some do chat e dos convites de quem bloqueou) e denúncia
/// (guarda o motivo e as últimas mensagens do denunciado para a moderação). Só personagens de conta: convidados não
/// são salvos.
/// </summary>
public sealed partial class GameWorld
{
    private void HandleRelation(ServerPlayer player, SocialCommand command, double now)
    {
        if (player.IsGuest)
        {
            SystemChat(player, "Convidados não têm amigos, bloqueios nem denúncias (entre com uma conta)");
            return;
        }
        var name = (command.TargetName ?? "").Trim();
        switch (command.Action)
        {
            case RelationActionKind.AddFriend:
                AddFriend(player, name);
                break;
            case RelationActionKind.RemoveFriend:
                RemoveRelation(player, RelationKind.Friend, name, "não é mais seu amigo", "não está na sua lista de amigos");
                break;
            case RelationActionKind.Block:
                Block(player, name);
                break;
            case RelationActionKind.Unblock:
                RemoveRelation(player, RelationKind.Blocked, name, "foi desbloqueado", "não está bloqueado");
                break;
            case RelationActionKind.Report:
                Report(player, name, command.Text, now);
                break;
        }
    }

    private void AddFriend(ServerPlayer player, string name)
    {
        var target = FindPlayerByName(name);
        string? error =
            target == null ? $"{name} precisa estar online para ser adicionado" :
            target == player ? "Você não pode adicionar a si mesmo" :
            target.IsGuest ? $"{target.Name} é um convidado e não pode ser adicionado" :
            player.HasRelation(RelationKind.Friend, target.CharacterId) ? $"{target.Name} já é seu amigo" :
            player.HasRelation(RelationKind.Blocked, target.CharacterId) ? $"Desbloqueie {target.Name} antes de adicionar" :
            player.Relations.Count(r => r.Kind == RelationKind.Friend) >= RelationRules.MaxFriends
                ? $"Sua lista de amigos está cheia ({RelationRules.MaxFriends})" :
            null;
        if (error != null)
        {
            SystemChat(player, error);
            return;
        }
        player.Relations.Add(new RelationRecord(RelationKind.Friend, target!.CharacterId, target.Name));
        player.SocialDirty = true;
        SystemChat(player, $"{target.Name} agora é seu amigo");
        if (!target.HasBlocked(player)) SystemChat(target, $"{player.Name} adicionou você como amigo");
    }

    private void Block(ServerPlayer player, string name)
    {
        // Online pelo nome; offline só se já for amigo (o id está na lista).
        var target = FindPlayerByName(name);
        var key = AccountRules.NameKey(name);
        var known = player.Relations.FirstOrDefault(r => r.Kind == RelationKind.Friend && AccountRules.NameKey(r.OtherName) == key);
        var (id, display) = target != null ? (target.CharacterId, target.Name) : known != null ? (known.OtherId, known.OtherName) : (0, name);
        string? error =
            target == player ? "Você não pode bloquear a si mesmo" :
            target == null && known == null ? $"{name} precisa estar online para ser bloqueado" :
            id == 0 ? $"{display} é um convidado e não pode ser bloqueado" :
            player.HasRelation(RelationKind.Blocked, id) ? $"{display} já está bloqueado" :
            player.Relations.Count(r => r.Kind == RelationKind.Blocked) >= RelationRules.MaxBlocked
                ? $"Sua lista de bloqueados está cheia ({RelationRules.MaxBlocked})" :
            null;
        if (error != null)
        {
            SystemChat(player, error);
            return;
        }
        player.Relations.RemoveAll(r => r.Kind == RelationKind.Friend && r.OtherId == id);
        player.Relations.Add(new RelationRecord(RelationKind.Blocked, id, display));
        player.SocialDirty = true;
        SystemChat(player, $"{display} foi bloqueado: você não vê mais as mensagens nem recebe convites dele");
    }

    private void RemoveRelation(ServerPlayer player, RelationKind kind, string name, string done, string missing)
    {
        var key = AccountRules.NameKey(name);
        var relation = player.Relations.FirstOrDefault(r => r.Kind == kind && AccountRules.NameKey(r.OtherName) == key);
        if (relation == null)
        {
            SystemChat(player, $"{name} {missing}");
            return;
        }
        player.Relations.Remove(relation);
        player.SocialDirty = true;
        SystemChat(player, $"{relation.OtherName} {done}");
    }

    private void Report(ServerPlayer player, string name, string reasonText, double now)
    {
        var target = FindPlayerByName(name);
        var reason = ChatRules.Sanitize(reasonText);
        if (reason.Length > RelationRules.MaxReportReason) reason = reason[..RelationRules.MaxReportReason];
        player.ReportTimes.RemoveAll(t => now - t > 3600);
        var targetKey = target != null ? AccountRules.NameKey(target.Name) : "";
        string? error =
            target == null ? $"{name} precisa estar online para ser denunciado" :
            target == player ? "Você não pode denunciar a si mesmo" :
            reason.Length == 0 ? "Diga o motivo da denúncia" :
            player.ReportedAt.TryGetValue(targetKey, out var last) && now - last < RelationRules.SameTargetCooldownSeconds
                ? $"Você já denunciou {target.Name} há pouco tempo" :
            player.ReportTimes.Count >= RelationRules.MaxReportsPerHour ? "Você fez muitas denúncias na última hora" :
            null;
        if (error != null)
        {
            SystemChat(player, error);
            return;
        }

        player.ReportTimes.Add(now);
        player.ReportedAt[targetKey] = now;
        var excerpt = string.Join("\n", target!.RecentChat.Select(l => $"[{l.At:0} s · {ChannelName(l.Channel)}] {l.Text}"));
        player.PendingReports.Add(new ReportRecord(
            player.CharacterId, player.Name, target.CharacterId, target.Name, reason, excerpt, DateTimeOffset.UtcNow));
        SystemChat(player, $"Denúncia de {target.Name} enviada para a moderação. Obrigado!");
    }

    /// <summary>Guarda a mensagem original (sem filtro) nas recentes do jogador, para uma eventual denúncia.</summary>
    private static void RememberChat(ServerPlayer player, ChatChannel channel, string text, double now)
    {
        player.RecentChat.Enqueue((now, channel, text));
        while (player.RecentChat.Count > RelationRules.ReportChatLines) player.RecentChat.Dequeue();
    }

    /// <summary>Avisa os amigos (quem tem este personagem na lista) que ele entrou ou saiu, e atualiza a lista deles.</summary>
    private void NotifyWatchers(ServerPlayer player, string? message)
    {
        if (player.IsGuest) return;
        foreach (var other in _players.Values)
        {
            if (other == player || !other.HasRelation(RelationKind.Friend, player.CharacterId)) continue;
            other.SocialDirty = true;
            if (message != null) SystemChat(other, message);
        }
    }

    private void SendSocialLists(ServerPlayer player)
    {
        if (!player.SocialDirty || player.IsGuest) return;
        player.SocialDirty = false;
        var lists = new SocialLists();
        foreach (var relation in player.Relations.OrderBy(r => r.OtherName, StringComparer.OrdinalIgnoreCase))
        {
            if (relation.Kind == RelationKind.Blocked)
            {
                lists.Blocked.Add(relation.OtherName);
                continue;
            }
            var online = _players.Values.FirstOrDefault(p => p.CharacterId == relation.OtherId);
            lists.Friends.Add(new FriendInfo
            {
                Name = online?.Name ?? relation.OtherName, Online = online != null,
                Level = online?.Progress.Level ?? 0, Class = (byte)(online?.Class ?? 0),
            });
        }
        _outbox.Send(player.Id, lists);
    }

    private static string ChannelName(ChatChannel channel) => channel switch
    {
        ChatChannel.Party => "grupo",
        ChatChannel.Whisper => "sussurro",
        _ => "mapa",
    };
}
