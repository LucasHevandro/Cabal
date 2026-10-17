using System.Collections.Generic;

namespace Game.Shared.Net
{
    public enum DungeonActionKind : byte
    {
        /// <summary>Entrar com o grupo (pedido do líder, perto do portal).</summary>
        Enter = 0,

        /// <summary>Sair da dungeon e voltar ao portal.</summary>
        Leave = 1,
    }

    /// <summary>Entrar ou sair de uma dungeon.</summary>
    public sealed class DungeonCommand : INetMessage
    {
        public MessageType Type => MessageType.DungeonCommand;
        public DungeonActionKind Action;
        public string DungeonId = "";

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Action);
            w.WriteString(DungeonId);
        }

        public static DungeonCommand Read(PacketReader r) => new DungeonCommand { Action = (DungeonActionKind)r.ReadByte(), DungeonId = r.ReadString() };
    }

    /// <summary>
    /// Estado da dungeon em que o jogador está (Active falso = saiu). O cliente monta o mapa na origem informada e
    /// mostra o cronômetro a partir de <see cref="StartedAt"/> (relógio do servidor).
    /// </summary>
    public sealed class DungeonState : INetMessage
    {
        public MessageType Type => MessageType.DungeonState;
        public bool Active;
        public string DungeonId = "";
        public float OriginX;
        public float OriginZ;

        /// <summary>Sala atual (0 = primeira); portões abertos = salas já limpas.</summary>
        public byte Room;

        public byte Rooms;
        public ushort MonstersLeft;
        public double StartedAt;
        public float LimitSeconds;
        public bool BossShield;
        public bool Completed;
        public bool Failed;

        /// <summary>Tempo final (quando terminou), medalha e combo máximo do grupo.</summary>
        public float ElapsedSeconds;

        public byte Medal;
        public ushort MaxCombo;

        public void Write(PacketWriter w)
        {
            w.WriteBool(Active);
            w.WriteString(DungeonId);
            w.WriteFloat(OriginX);
            w.WriteFloat(OriginZ);
            w.WriteByte(Room);
            w.WriteByte(Rooms);
            w.WriteUShort(MonstersLeft);
            w.WriteDouble(StartedAt);
            w.WriteFloat(LimitSeconds);
            w.WriteBool(BossShield);
            w.WriteBool(Completed);
            w.WriteBool(Failed);
            w.WriteFloat(ElapsedSeconds);
            w.WriteByte(Medal);
            w.WriteUShort(MaxCombo);
        }

        public static DungeonState Read(PacketReader r) => new DungeonState
        {
            Active = r.ReadBool(), DungeonId = r.ReadString(), OriginX = r.ReadFloat(), OriginZ = r.ReadFloat(), Room = r.ReadByte(),
            Rooms = r.ReadByte(), MonstersLeft = r.ReadUShort(), StartedAt = r.ReadDouble(), LimitSeconds = r.ReadFloat(),
            BossShield = r.ReadBool(), Completed = r.ReadBool(), Failed = r.ReadBool(), ElapsedSeconds = r.ReadFloat(),
            Medal = r.ReadByte(), MaxCombo = r.ReadUShort(),
        };
    }

    public struct DungeonRankEntry
    {
        public float Seconds;
        public byte Medal;
        public ushort MaxCombo;

        /// <summary>Nomes do grupo separados por vírgula.</summary>
        public string Members;
    }

    /// <summary>Ranking dos melhores tempos (chega ao falar com o portal e ao terminar a dungeon).</summary>
    public sealed class DungeonInfo : INetMessage
    {
        public MessageType Type => MessageType.DungeonInfo;
        public string DungeonId = "";
        public List<DungeonRankEntry> Ranking = new List<DungeonRankEntry>();

        public void Write(PacketWriter w)
        {
            w.WriteString(DungeonId);
            w.WriteByte((byte)Ranking.Count);
            foreach (var entry in Ranking)
            {
                w.WriteFloat(entry.Seconds);
                w.WriteByte(entry.Medal);
                w.WriteUShort(entry.MaxCombo);
                w.WriteString(entry.Members);
            }
        }

        public static DungeonInfo Read(PacketReader r)
        {
            var info = new DungeonInfo { DungeonId = r.ReadString() };
            var count = r.ReadByte();
            for (var i = 0; i < count; i++)
                info.Ranking.Add(new DungeonRankEntry { Seconds = r.ReadFloat(), Medal = r.ReadByte(), MaxCombo = r.ReadUShort(), Members = r.ReadString() });
            return info;
        }
    }
}
