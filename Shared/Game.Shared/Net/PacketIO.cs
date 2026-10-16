using System;
using System.Buffers.Binary;
using System.Text;

namespace Game.Shared.Net
{
    /// <summary>Erro de protocolo: mensagem malformada, grande demais ou desconhecida. A conexão deve ser fechada.</summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
    }

    /// <summary>Escreve tipos primitivos em little-endian num buffer que cresce sozinho.</summary>
    public sealed class PacketWriter
    {
        private byte[] _buffer;

        public int Length { get; private set; }

        public PacketWriter(int capacity = 256)
        {
            _buffer = new byte[Math.Max(16, capacity)];
        }

        public void Clear() => Length = 0;

        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[Length++] = value;
        }

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteUShort(ushort value)
        {
            Ensure(2);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(Length), value);
            Length += 2;
        }

        public void WriteInt(int value)
        {
            Ensure(4);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(Length), value);
            Length += 4;
        }

        public void WriteUInt(uint value)
        {
            Ensure(4);
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(Length), value);
            Length += 4;
        }

        public void WriteLong(long value)
        {
            Ensure(8);
            BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(Length), value);
            Length += 8;
        }

        public void WriteFloat(float value) => WriteInt(BitConverter.SingleToInt32Bits(value));

        public void WriteDouble(double value) => WriteLong(BitConverter.DoubleToInt64Bits(value));

        /// <summary>Texto em UTF-8 com prefixo de tamanho (no máximo 65.535 bytes).</summary>
        public void WriteString(string? value)
        {
            var bytes = string.IsNullOrEmpty(value) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(value);
            if (bytes.Length > ushort.MaxValue) throw new ArgumentException("Texto grande demais para o protocolo.", nameof(value));
            WriteUShort((ushort)bytes.Length);
            Ensure(bytes.Length);
            Buffer.BlockCopy(bytes, 0, _buffer, Length, bytes.Length);
            Length += bytes.Length;
        }

        /// <summary>Cópia exata do que foi escrito.</summary>
        public byte[] ToArray()
        {
            var result = new byte[Length];
            Buffer.BlockCopy(_buffer, 0, result, 0, Length);
            return result;
        }

        private void Ensure(int extra)
        {
            if (Length + extra <= _buffer.Length) return;
            var size = _buffer.Length;
            while (size < Length + extra) size *= 2;
            Array.Resize(ref _buffer, size);
        }
    }

    /// <summary>Lê o que o <see cref="PacketWriter"/> escreveu. Ler além do fim lança <see cref="ProtocolException"/>.</summary>
    public sealed class PacketReader
    {
        private readonly byte[] _buffer;
        private readonly int _end;
        private int _position;

        public PacketReader(byte[] buffer) : this(buffer, 0, buffer.Length) { }

        public PacketReader(byte[] buffer, int offset, int count)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));
            _position = offset;
            _end = offset + count;
        }

        /// <summary>Bytes que ainda não foram lidos.</summary>
        public int Remaining => _end - _position;

        public byte ReadByte()
        {
            Need(1);
            return _buffer[_position++];
        }

        public bool ReadBool() => ReadByte() != 0;

        public ushort ReadUShort()
        {
            Need(2);
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(_position));
            _position += 2;
            return value;
        }

        public int ReadInt()
        {
            Need(4);
            var value = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(_position));
            _position += 4;
            return value;
        }

        public uint ReadUInt()
        {
            Need(4);
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(_position));
            _position += 4;
            return value;
        }

        public long ReadLong()
        {
            Need(8);
            var value = BinaryPrimitives.ReadInt64LittleEndian(_buffer.AsSpan(_position));
            _position += 8;
            return value;
        }

        public float ReadFloat() => BitConverter.Int32BitsToSingle(ReadInt());

        public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadLong());

        public string ReadString()
        {
            var length = ReadUShort();
            Need(length);
            var value = Encoding.UTF8.GetString(_buffer, _position, length);
            _position += length;
            return value;
        }

        private void Need(int count)
        {
            if (_position + count > _end)
                throw new ProtocolException($"Mensagem incompleta: faltam {_position + count - _end} bytes.");
        }
    }
}
