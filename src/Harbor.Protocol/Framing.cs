using System.Buffers.Binary;
using MessagePack;

namespace Harbor.Protocol;

/// <summary>
/// 프레임: [uint32 length][uint16 opcode][MessagePack body]
/// length = opcode(2) + body 바이트 수. 리틀엔디언.
/// </summary>
public static class Framing
{
    public const int HeaderSize = 6;
    public const int MaxFrame = 64 * 1024;

    public static byte[] Encode<T>(Opcode op, T body)
    {
        byte[] payload = MessagePackSerializer.Serialize(body);
        var buf = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), (uint)(2 + payload.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(4, 2), (ushort)op);
        payload.CopyTo(buf, HeaderSize);
        return buf;
    }

    /// <summary>버퍼에서 완전한 프레임 1개를 잘라냄. 부족하면 false.</summary>
    public static bool TryDecode(ref ReadOnlyMemory<byte> buffer, out Opcode op, out ReadOnlyMemory<byte> body)
    {
        op = default; body = default;
        if (buffer.Length < 4) return false;
        uint len = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Span[..4]);
        if (len < 2 || len > MaxFrame) throw new InvalidDataException($"bad frame length {len}");
        if (buffer.Length < 4 + len) return false;
        op = (Opcode)BinaryPrimitives.ReadUInt16LittleEndian(buffer.Span.Slice(4, 2));
        body = buffer.Slice(6, (int)len - 2);
        buffer = buffer[(4 + (int)len)..];
        return true;
    }

    public static T Deserialize<T>(ReadOnlyMemory<byte> body) => MessagePackSerializer.Deserialize<T>(body);
}
