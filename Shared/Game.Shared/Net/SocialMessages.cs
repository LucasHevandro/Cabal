using System.Collections.Generic;
using Game.Shared.Social;

namespace Game.Shared.Net
{
    public enum PartyActionKind : byte
    {
        /// <summary>Convida pelo nome (<see cref="PartyCommand.TargetName"/>).</summary>
        Invite = 0,

        /// <summary>Aceita o convite de <see cref="PartyCommand.TargetId"/>.</summary>
        Accept = 1,

        Decline = 2,
        Leave = 3,

        /// <summary>Só o líder: tira um membro pelo nome.</summary>
        Kick = 4,

        /// <summary>Só o líder: passa a liderança pelo nome.</summary>
        Promote = 5,
    }

    public enum RelationActionKind : byte
    {
        AddFriend = 0,
        RemoveFriend = 1,
        Block = 2,
        Unblock = 3,

        /// <summary>Denuncia <see cref="SocialCommand.TargetName"/>; <see cref="SocialCommand.Text"/> é o motivo.</summary>
        Report = 4,
    }

    // ===== Cliente → servidor =====

    /// <summary>Amigos, bloqueio e denúncia, sempre pelo nome do personagem.</summary>
    public sealed class SocialCommand : INetMessage
    {
        public MessageType Type => MessageType.SocialCommand;
        public RelationActionKind Action;
        public string TargetName = "";
        public string Text = "";

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Action);
            w.WriteString(TargetName);
            w.WriteString(Text);
        }

        public static SocialCommand Read(PacketReader r) =>
            new SocialCommand { Action = (RelationActionKind)r.ReadByte(), TargetName = r.ReadString(), Text = r.ReadString() };
    }

    public sealed class PartyCommand : INetMessage
    {
        public MessageType Type => MessageType.PartyCommand;
        public PartyActionKind Action;
        public int TargetId;
        public string TargetName = "";

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Action);
            w.WriteInt(TargetId);
            w.WriteString(TargetName);
        }

        public static PartyCommand Read(PacketReader r) =>
            new PartyCommand { Action = (PartyActionKind)r.ReadByte(), TargetId = r.ReadInt(), TargetName = r.ReadString() };
    }

    /// <summary>Mensagem de chat. <see cref="Target"/> é o nome de quem recebe o sussurro.</summary>
    public sealed class ChatSend : INetMessage
    {
        public MessageType Type => MessageType.ChatSend;
        public ChatChannel Channel;
        public string Target = "";
        public string Text = "";

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Channel);
            w.WriteString(Target);
            w.WriteString(Text);
        }

        public static ChatSend Read(PacketReader r) =>
            new ChatSend { Channel = (ChatChannel)r.ReadByte(), Target = r.ReadString(), Text = r.ReadString() };
    }

    // ===== Servidor → cliente =====

    public struct PartyMemberInfo
    {
        public int Id;
        public string Name;
        public int Level;
        public byte Class;
        public int Hp;
        public int MaxHp;
        public int Mp;
        public int MaxMp;
        public bool Dead;

        /// <summary>Perto o bastante para dividir XP e drop.</summary>
        public bool Near;
    }

    /// <summary>Grupo do jogador (lista vazia = sem grupo). Chega quando muda e quando a vida dos membros muda.</summary>
    public sealed class PartyState : INetMessage
    {
        public MessageType Type => MessageType.PartyState;
        public int LeaderId;
        public List<PartyMemberInfo> Members = new List<PartyMemberInfo>();

        public void Write(PacketWriter w)
        {
            w.WriteInt(LeaderId);
            w.WriteByte((byte)Members.Count);
            foreach (var m in Members)
            {
                w.WriteInt(m.Id);
                w.WriteString(m.Name);
                w.WriteInt(m.Level);
                w.WriteByte(m.Class);
                w.WriteInt(m.Hp);
                w.WriteInt(m.MaxHp);
                w.WriteInt(m.Mp);
                w.WriteInt(m.MaxMp);
                w.WriteBool(m.Dead);
                w.WriteBool(m.Near);
            }
        }

        public static PartyState Read(PacketReader r)
        {
            var state = new PartyState { LeaderId = r.ReadInt() };
            var count = r.ReadByte();
            for (var i = 0; i < count; i++)
            {
                state.Members.Add(new PartyMemberInfo
                {
                    Id = r.ReadInt(), Name = r.ReadString(), Level = r.ReadInt(), Class = r.ReadByte(), Hp = r.ReadInt(),
                    MaxHp = r.ReadInt(), Mp = r.ReadInt(), MaxMp = r.ReadInt(), Dead = r.ReadBool(), Near = r.ReadBool(),
                });
            }
            return state;
        }
    }

    public struct FriendInfo
    {
        public string Name;
        public bool Online;

        /// <summary>Nível e classe só quando online (0 = desconhecido).</summary>
        public int Level;

        public byte Class;
    }

    /// <summary>Lista de amigos (com quem está online) e de bloqueados. Chega ao entrar e quando muda.</summary>
    public sealed class SocialLists : INetMessage
    {
        public MessageType Type => MessageType.SocialLists;
        public List<FriendInfo> Friends = new List<FriendInfo>();
        public List<string> Blocked = new List<string>();

        public void Write(PacketWriter w)
        {
            w.WriteUShort((ushort)Friends.Count);
            foreach (var f in Friends)
            {
                w.WriteString(f.Name);
                w.WriteBool(f.Online);
                w.WriteInt(f.Level);
                w.WriteByte(f.Class);
            }
            w.WriteUShort((ushort)Blocked.Count);
            foreach (var name in Blocked) w.WriteString(name);
        }

        public static SocialLists Read(PacketReader r)
        {
            var lists = new SocialLists();
            var friends = r.ReadUShort();
            for (var i = 0; i < friends; i++)
                lists.Friends.Add(new FriendInfo { Name = r.ReadString(), Online = r.ReadBool(), Level = r.ReadInt(), Class = r.ReadByte() });
            var blocked = r.ReadUShort();
            for (var i = 0; i < blocked; i++) lists.Blocked.Add(r.ReadString());
            return lists;
        }
    }

    /// <summary>Alguém convidou este jogador para um grupo.</summary>
    public sealed class PartyInvite : INetMessage
    {
        public MessageType Type => MessageType.PartyInvite;
        public int InviterId;
        public string InviterName = "";
        public float Seconds;

        public void Write(PacketWriter w)
        {
            w.WriteInt(InviterId);
            w.WriteString(InviterName);
            w.WriteFloat(Seconds);
        }

        public static PartyInvite Read(PacketReader r) =>
            new PartyInvite { InviterId = r.ReadInt(), InviterName = r.ReadString(), Seconds = r.ReadFloat() };
    }

    /// <summary>
    /// Uma linha do chat. No sussurro, quem enviou recebe uma cópia com <see cref="ToName"/> preenchido
    /// ("Para Fulano: ...").
    /// </summary>
    public sealed class ChatMessage : INetMessage
    {
        public MessageType Type => MessageType.ChatMessage;
        public ChatChannel Channel;
        public int FromId;
        public string FromName = "";
        public string ToName = "";
        public string Text = "";

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Channel);
            w.WriteInt(FromId);
            w.WriteString(FromName);
            w.WriteString(ToName);
            w.WriteString(Text);
        }

        public static ChatMessage Read(PacketReader r) => new ChatMessage
        {
            Channel = (ChatChannel)r.ReadByte(), FromId = r.ReadInt(), FromName = r.ReadString(), ToName = r.ReadString(),
            Text = r.ReadString(),
        };
    }
}
