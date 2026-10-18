using System.Collections.Generic;

namespace Game.Shared.Net
{
    public enum DuelActionKind : byte
    {
        /// <summary>Desafia <see cref="DuelCommand.TargetName"/>.</summary>
        Request = 0,

        /// <summary>Aceita o desafio de <see cref="DuelCommand.TargetId"/>.</summary>
        Accept = 1,

        Decline = 2,

        /// <summary>Desiste do duelo em andamento.</summary>
        Forfeit = 3,
    }

    public enum ArenaActionKind : byte
    {
        Join = 0,
        LeaveQueue = 1,

        /// <summary>Sai da partida (sem recompensa).</summary>
        Leave = 2,
    }

    public enum PvpMode : byte
    {
        None = 0,
        Duel = 1,
        Arena = 2,
    }

    // ===== Cliente → servidor =====

    public sealed class DuelCommand : INetMessage
    {
        public MessageType Type => MessageType.DuelCommand;
        public DuelActionKind Action;
        public int TargetId;
        public string TargetName = "";

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Action);
            w.WriteInt(TargetId);
            w.WriteString(TargetName);
        }

        public static DuelCommand Read(PacketReader r) =>
            new DuelCommand { Action = (DuelActionKind)r.ReadByte(), TargetId = r.ReadInt(), TargetName = r.ReadString() };
    }

    /// <summary>Comando de GM digitado no chat (/gm ...): o texto vai inteiro e o servidor interpreta e confere.</summary>
    public sealed class GmCommand : INetMessage
    {
        public MessageType Type => MessageType.GmCommand;
        public string Text = "";

        public void Write(PacketWriter w) => w.WriteString(Text.Length > 300 ? Text.Substring(0, 300) : Text);

        public static GmCommand Read(PacketReader r) => new GmCommand { Text = r.ReadString() };
    }

    public sealed class ArenaCommand : INetMessage
    {
        public MessageType Type => MessageType.ArenaCommand;
        public ArenaActionKind Action;

        public void Write(PacketWriter w) => w.WriteByte((byte)Action);

        public static ArenaCommand Read(PacketReader r) => new ArenaCommand { Action = (ArenaActionKind)r.ReadByte() };
    }

    // ===== Servidor → cliente =====

    /// <summary>Alguém desafiou este jogador para um duelo.</summary>
    public sealed class DuelInvite : INetMessage
    {
        public MessageType Type => MessageType.DuelInvite;
        public int FromId;
        public string FromName = "";
        public float Seconds;

        public void Write(PacketWriter w)
        {
            w.WriteInt(FromId);
            w.WriteString(FromName);
            w.WriteFloat(Seconds);
        }

        public static DuelInvite Read(PacketReader r) => new DuelInvite { FromId = r.ReadInt(), FromName = r.ReadString(), Seconds = r.ReadFloat() };
    }

    public struct ArenaPointInfo
    {
        /// <summary><see cref="Pvp.ArenaTeam"/> do dono (-1 = ninguém).</summary>
        public sbyte Owner;

        /// <summary>-1 (azul) a +1 (vermelho).</summary>
        public float Progress;

        public bool Contested;
    }

    public struct ArenaPlayerInfo
    {
        public int Id;
        public string Name;
        public sbyte Team;
        public int Level;
        public ushort Kills;
        public ushort Deaths;
        public ushort Captures;
    }

    /// <summary>
    /// PvP do jogador: fila da arena, duelo ou partida da arena (Mode None e fora da fila = nada). Chega quando muda e,
    /// durante a partida, duas vezes por segundo (pontos e placar).
    /// </summary>
    public sealed class PvpState : INetMessage
    {
        public MessageType Type => MessageType.PvpState;
        public PvpMode Mode;

        // Fila
        public bool InQueue;
        public byte QueueSize;
        public byte QueueNeeded;

        // Duelo e arena (relógio do servidor)
        public double StartsAt;
        public double EndsAt;

        // Duelo
        public int OpponentId;
        public string OpponentName = "";

        // Arena
        public float OriginX;
        public float OriginZ;
        public sbyte Team = -1;
        public ushort ScoreBlue;
        public ushort ScoreRed;
        public List<ArenaPointInfo> Points = new List<ArenaPointInfo>();
        public List<ArenaPlayerInfo> Players = new List<ArenaPlayerInfo>();
        public bool Finished;

        /// <summary>Time vencedor (-1 = empate).</summary>
        public sbyte Winner = -1;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Mode);
            w.WriteBool(InQueue);
            w.WriteByte(QueueSize);
            w.WriteByte(QueueNeeded);
            w.WriteDouble(StartsAt);
            w.WriteDouble(EndsAt);
            w.WriteInt(OpponentId);
            w.WriteString(OpponentName);
            w.WriteFloat(OriginX);
            w.WriteFloat(OriginZ);
            w.WriteByte((byte)Team);
            w.WriteUShort(ScoreBlue);
            w.WriteUShort(ScoreRed);
            w.WriteByte((byte)Points.Count);
            foreach (var p in Points)
            {
                w.WriteByte((byte)p.Owner);
                w.WriteFloat(p.Progress);
                w.WriteBool(p.Contested);
            }
            w.WriteByte((byte)Players.Count);
            foreach (var p in Players)
            {
                w.WriteInt(p.Id);
                w.WriteString(p.Name);
                w.WriteByte((byte)p.Team);
                w.WriteInt(p.Level);
                w.WriteUShort(p.Kills);
                w.WriteUShort(p.Deaths);
                w.WriteUShort(p.Captures);
            }
            w.WriteBool(Finished);
            w.WriteByte((byte)Winner);
        }

        public static PvpState Read(PacketReader r)
        {
            var state = new PvpState
            {
                Mode = (PvpMode)r.ReadByte(), InQueue = r.ReadBool(), QueueSize = r.ReadByte(), QueueNeeded = r.ReadByte(),
                StartsAt = r.ReadDouble(), EndsAt = r.ReadDouble(), OpponentId = r.ReadInt(), OpponentName = r.ReadString(),
                OriginX = r.ReadFloat(), OriginZ = r.ReadFloat(), Team = (sbyte)r.ReadByte(), ScoreBlue = r.ReadUShort(),
                ScoreRed = r.ReadUShort(),
            };
            var points = r.ReadByte();
            for (var i = 0; i < points; i++)
                state.Points.Add(new ArenaPointInfo { Owner = (sbyte)r.ReadByte(), Progress = r.ReadFloat(), Contested = r.ReadBool() });
            var players = r.ReadByte();
            for (var i = 0; i < players; i++)
                state.Players.Add(new ArenaPlayerInfo
                {
                    Id = r.ReadInt(), Name = r.ReadString(), Team = (sbyte)r.ReadByte(), Level = r.ReadInt(), Kills = r.ReadUShort(),
                    Deaths = r.ReadUShort(), Captures = r.ReadUShort(),
                });
            state.Finished = r.ReadBool();
            state.Winner = (sbyte)r.ReadByte();
            return state;
        }
    }
}
