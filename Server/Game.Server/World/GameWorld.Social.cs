using Game.Shared.Accounts;
using Game.Shared.Net;
using Game.Shared.Social;

namespace Game.Server.World;

/// <summary>Grupo (convite, líder, saída) e chat (mapa, grupo, sussurro), com filtro e limite de mensagens.</summary>
public sealed partial class GameWorld
{
    private readonly Dictionary<int, Party> _parties = new();
    private int _nextPartyId = 1;

    public Party? FindParty(int partyId) => _parties.TryGetValue(partyId, out var party) ? party : null;

    /// <summary>Jogador online pelo nome (sem diferença de acento e maiúsculas).</summary>
    public ServerPlayer? FindPlayerByName(string name)
    {
        var key = AccountRules.NameKey(name);
        if (key.Length == 0) return null;
        return _players.Values.FirstOrDefault(p => AccountRules.NameKey(p.Name) == key);
    }

    /// <summary>Membros do grupo que estão no mundo (o próprio jogador sozinho se não tiver grupo).</summary>
    private IEnumerable<ServerPlayer> PartyMembers(ServerPlayer player)
    {
        var party = FindParty(player.PartyId);
        if (party == null)
        {
            yield return player;
            yield break;
        }
        foreach (var id in party.Members)
        {
            if (_players.TryGetValue(id, out var member)) yield return member;
        }
    }

    // ===== Grupo =====

    private void HandleParty(ServerPlayer player, PartyCommand command, double now)
    {
        switch (command.Action)
        {
            case PartyActionKind.Invite:
                Invite(player, command.TargetName, now);
                break;
            case PartyActionKind.Accept:
                AcceptInvite(player, command.TargetId, now);
                break;
            case PartyActionKind.Decline:
                DeclineInvite(player);
                break;
            case PartyActionKind.Leave:
                if (player.PartyId == 0) SystemChat(player, "Você não está em um grupo");
                else LeaveParty(player, $"{player.Name} saiu do grupo");
                break;
            case PartyActionKind.Kick:
                Kick(player, command.TargetName);
                break;
            case PartyActionKind.Promote:
                Promote(player, command.TargetName);
                break;
        }
    }

    private void Invite(ServerPlayer player, string name, double now)
    {
        var target = FindPlayerByName(name);
        var party = FindParty(player.PartyId);
        string? error =
            target == null ? $"{name} não está online" :
            target == player ? "Você não pode convidar a si mesmo" :
            party != null && party.LeaderId != player.Id ? "Só o líder pode convidar" :
            party != null && party.IsFull ? $"O grupo está cheio ({PartyRules.MaxMembers})" :
            target.PartyId != 0 ? $"{target.Name} já está em um grupo" :
            target.PendingInvite is { } pending && pending.ExpiresAt > now && pending.InviterId != player.Id
                ? $"{target.Name} já tem um convite esperando resposta" :
            null;
        if (error != null)
        {
            SystemChat(player, error);
            return;
        }

        target!.PendingInvite = (player.Id, now + PartyRules.InviteSeconds);
        _outbox.Send(target.Id, new PartyInvite { InviterId = player.Id, InviterName = player.Name, Seconds = (float)PartyRules.InviteSeconds });
        SystemChat(player, $"Convite enviado para {target.Name}");
    }

    private void AcceptInvite(ServerPlayer player, int inviterId, double now)
    {
        var pending = player.PendingInvite;
        player.PendingInvite = null;
        if (pending is not { } invite || invite.InviterId != inviterId || invite.ExpiresAt < now)
        {
            SystemChat(player, "O convite expirou");
            return;
        }
        if (player.PartyId != 0)
        {
            SystemChat(player, "Você já está em um grupo");
            return;
        }
        if (!_players.TryGetValue(inviterId, out var inviter))
        {
            SystemChat(player, "Quem convidou não está mais online");
            return;
        }

        var party = FindParty(inviter.PartyId);
        if (party == null)
        {
            party = new Party(_nextPartyId++, inviter.Id);
            _parties.Add(party.Id, party);
            inviter.PartyId = party.Id;
            inviter.PartyDirty = true;
        }
        else if (party.LeaderId != inviter.Id)
        {
            SystemChat(player, $"{inviter.Name} não é mais o líder do grupo");
            return;
        }
        if (!party.TryAdd(player.Id))
        {
            SystemChat(player, "O grupo está cheio");
            return;
        }
        player.PartyId = party.Id;
        PartyChat(party, $"{player.Name} entrou no grupo");
    }

    private void DeclineInvite(ServerPlayer player)
    {
        if (player.PendingInvite is not { } invite) return;
        player.PendingInvite = null;
        if (_players.TryGetValue(invite.InviterId, out var inviter)) SystemChat(inviter, $"{player.Name} recusou o convite");
    }

    private void Kick(ServerPlayer player, string name)
    {
        var party = FindParty(player.PartyId);
        var target = FindPlayerByName(name);
        if (party == null || party.LeaderId != player.Id)
        {
            SystemChat(player, "Só o líder pode expulsar");
            return;
        }
        if (target == null || !party.Contains(target.Id) || target == player)
        {
            SystemChat(player, $"{name} não está no seu grupo");
            return;
        }
        SystemChat(target, "Você foi expulso do grupo");
        LeaveParty(target, $"{target.Name} foi expulso do grupo");
    }

    private void Promote(ServerPlayer player, string name)
    {
        var party = FindParty(player.PartyId);
        var target = FindPlayerByName(name);
        if (party == null || party.LeaderId != player.Id)
        {
            SystemChat(player, "Só o líder pode passar a liderança");
            return;
        }
        if (target == null || !party.TryPromote(target.Id))
        {
            SystemChat(player, $"{name} não está no seu grupo");
            return;
        }
        PartyChat(party, $"{target.Name} agora é o líder");
    }

    /// <summary>Tira o jogador do grupo; avisa quem fica e desfaz o grupo se sobrar um só.</summary>
    private void LeaveParty(ServerPlayer player, string message)
    {
        var party = FindParty(player.PartyId);
        player.PartyId = 0;
        player.PartyDirty = true;
        if (party == null || !party.Remove(player.Id)) return;

        PartyChat(party, message);
        if (!party.ShouldDisband) return;
        _parties.Remove(party.Id);
        foreach (var id in party.Members)
        {
            if (!_players.TryGetValue(id, out var last)) continue;
            last.PartyId = 0;
            SystemChat(last, "O grupo foi desfeito");
        }
    }

    /// <summary>Manda o PartyState quando algo que aparece na tela mudou (entrou, saiu, vida, nível, distância).</summary>
    private void SendPartyState(ServerPlayer player)
    {
        var party = FindParty(player.PartyId);
        if (party == null)
        {
            if (!player.PartyDirty) return;
            player.PartyDirty = false;
            player.LastPartySignature = 0;
            _outbox.Send(player.Id, new PartyState());
            return;
        }

        var state = new PartyState { LeaderId = party.LeaderId };
        var signature = new HashCode();
        signature.Add(party.LeaderId);
        foreach (var id in party.Members)
        {
            if (!_players.TryGetValue(id, out var member)) continue;
            var info = new PartyMemberInfo
            {
                Id = member.Id, Name = member.Name, Level = member.Progress.Level, Class = (byte)member.Class,
                Hp = member.Hp.Current, MaxHp = member.Hp.Max, Mp = member.Mp.Current, MaxMp = member.Mp.Max,
                Dead = member.IsDead, Near = InRange(player.X, player.Z, member.X, member.Z, PartyRules.ShareRadius),
            };
            state.Members.Add(info);
            signature.Add(info.Id);
            signature.Add(info.Level);
            signature.Add(info.Hp);
            signature.Add(info.MaxHp);
            signature.Add(info.Mp);
            signature.Add(info.MaxMp);
            signature.Add(info.Dead);
            signature.Add(info.Near);
        }
        var hash = signature.ToHashCode();
        if (!player.PartyDirty && hash == player.LastPartySignature) return;
        player.PartyDirty = false;
        player.LastPartySignature = hash;
        _outbox.Send(player.Id, state);
    }

    // ===== Chat =====

    private void HandleChat(ServerPlayer player, ChatSend message, double now)
    {
        if (message.Channel == ChatChannel.System) return;
        var text = ChatRules.Sanitize(message.Text);
        if (text.Length == 0) return;
        if (!player.ChatLimiter.TryConsume(now))
        {
            SystemChat(player, "Calma! Espere um pouco para mandar outra mensagem");
            return;
        }
        text = ProfanityFilter.Clean(text);

        switch (message.Channel)
        {
            case ChatChannel.Map:
                var line = new ChatMessage { Channel = ChatChannel.Map, FromId = player.Id, FromName = player.Name, Text = text };
                foreach (var other in _players.Values) _outbox.Send(other.Id, line);
                break;
            case ChatChannel.Party:
                var party = FindParty(player.PartyId);
                if (party == null)
                {
                    SystemChat(player, "Você não está em um grupo");
                    return;
                }
                var partyLine = new ChatMessage { Channel = ChatChannel.Party, FromId = player.Id, FromName = player.Name, Text = text };
                foreach (var member in PartyMembers(player)) _outbox.Send(member.Id, partyLine);
                break;
            case ChatChannel.Whisper:
                var target = FindPlayerByName(message.Target);
                if (target == null)
                {
                    SystemChat(player, $"{message.Target} não está online");
                    return;
                }
                if (target == player)
                {
                    SystemChat(player, "Você não pode sussurrar para si mesmo");
                    return;
                }
                _outbox.Send(target.Id, new ChatMessage { Channel = ChatChannel.Whisper, FromId = player.Id, FromName = player.Name, Text = text });
                _outbox.Send(player.Id, new ChatMessage
                {
                    Channel = ChatChannel.Whisper, FromId = player.Id, FromName = player.Name, ToName = target.Name, Text = text,
                });
                break;
        }
    }

    private void SystemChat(ServerPlayer player, string text) =>
        _outbox.Send(player.Id, new ChatMessage { Channel = ChatChannel.System, Text = text });

    private void PartyChat(Party party, string text)
    {
        foreach (var id in party.Members)
        {
            if (!_players.TryGetValue(id, out var member)) continue;
            member.PartyDirty = true;
            SystemChat(member, text);
        }
    }
}
