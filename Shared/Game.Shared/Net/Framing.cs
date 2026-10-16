using System;
using System.Buffers.Binary;

namespace Game.Shared.Net
{
    /// <summary>
    /// Formato de cada mensagem no TCP: <c>[int32 tamanho do corpo][ushort tipo][conteúdo]</c>, little-endian.
    /// O tamanho conta o tipo e o conteúdo. O TCP entrega bytes em pedaços arbitrários, por isso
    /// quem recebe acumula no <see cref="FrameBuffer"/> até ter mensagens completas.
    /// </summary>
    public static class Framing
    {
        /// <summary>Bytes do cabeçalho de tamanho.</summary>
        public const int LengthPrefixSize = 4;

        /// <summary>Maior corpo aceito (protege o servidor de mensagens gigantes).</summary>
        public const int MaxBodySize = 256 * 1024;

        /// <summary>Monta a mensagem completa pronta para enviar.</summary>
        public static byte[] Encode(MessageType type, PacketWriter payload)
        {
            var bodyLength = 2 + payload.Length;
            if (bodyLength > MaxBodySize) throw new ProtocolException($"Mensagem {type} grande demais ({bodyLength} bytes).");

            var frame = new byte[LengthPrefixSize + bodyLength];
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0), bodyLength);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(LengthPrefixSize), (ushort)type);
            Buffer.BlockCopy(payload.ToArray(), 0, frame, LengthPrefixSize + 2, payload.Length);
            return frame;
        }
    }

    /// <summary>Acumula bytes recebidos e entrega mensagens completas, uma por vez.</summary>
    public sealed class FrameBuffer
    {
        private byte[] _data = new byte[4096];
        private int _count;

        /// <summary>Bytes acumulados ainda não entregues.</summary>
        public int Pending => _count;

        public void Append(byte[] source, int offset, int count)
        {
            if (count <= 0) return;
            if (_count + count > _data.Length)
            {
                var size = _data.Length;
                while (size < _count + count) size *= 2;
                Array.Resize(ref _data, size);
            }
            Buffer.BlockCopy(source, offset, _data, _count, count);
            _count += count;
        }

        /// <summary>
        /// Tenta tirar a próxima mensagem completa. Retorna falso se ainda faltam bytes.
        /// Lança <see cref="ProtocolException"/> se o tamanho declarado for inválido.
        /// </summary>
        public bool TryRead(out MessageType type, out byte[] payload)
        {
            type = default;
            payload = Array.Empty<byte>();
            if (_count < Framing.LengthPrefixSize) return false;

            var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(0));
            if (bodyLength < 2 || bodyLength > Framing.MaxBodySize)
                throw new ProtocolException($"Tamanho de mensagem inválido: {bodyLength}.");
            var frameLength = Framing.LengthPrefixSize + bodyLength;
            if (_count < frameLength) return false;

            type = (MessageType)BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(Framing.LengthPrefixSize));
            payload = new byte[bodyLength - 2];
            Buffer.BlockCopy(_data, Framing.LengthPrefixSize + 2, payload, 0, payload.Length);

            // Descarta a mensagem lida movendo o restante para o início.
            _count -= frameLength;
            if (_count > 0) Buffer.BlockCopy(_data, frameLength, _data, 0, _count);
            return true;
        }
    }
}
