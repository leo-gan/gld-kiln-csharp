using System.Buffers.Binary;

namespace Kiln;

public static class KilnCodec
{
    public const string Version = "0.2.0";

    public static int MinSize(Schema schema) => Scalars.MinSize(schema);

    public static byte[] Encode(Schema schema, object? value, int cap = Limits.DefaultCap)
    {
        var writer = new Writer();
        return writer.Encode(schema, value, cap).ToArray();
    }

    public static byte[] EncodeFramed(Schema schema, object? value, int cap = Limits.DefaultCap)
    {
        var writer = new Writer();
        return writer.EncodeFramed(schema, value, cap).ToArray();
    }

    public static object? Decode(Schema schema, ReadOnlyMemory<byte> buf, int cap = Limits.DefaultCap, bool trusted = false)
    {
        var index = 0;
        var value = FrozenReader.Read(buf, ref index, schema, cap, 0, trusted);
        if (index != buf.Length)
            throw new KilnException("TRAILING");
        return value;
    }

    public static object? DecodeFramed(Schema schema, ReadOnlyMemory<byte> buf, int cap = Limits.DefaultCap, bool trusted = false)
    {
        if (buf.Length < 16)
            throw new KilnException("EOF");
        var span = buf.Span;
        if (span[0] != (byte)'K' || span[1] != (byte)'L' || span[2] != (byte)'N' || span[3] != (byte)'0' || span[4] != 0 || span[5] != 0 || span[6] != 0 || span[7] != 0)
            throw new KilnException("FRAME");
        if (BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(8)) != TypeHash.Compute(schema))
            throw new KilnException("SCHEMA");
        var index = 16;
        var value = FrozenReader.Read(buf, ref index, schema, cap, 0, trusted);
        if (index != buf.Length)
            throw new KilnException("TRAILING");
        return value;
    }

    public static byte[] EncodeTensors(object? tensors)
    {
        var writer = new Writer(0);
        TensorIo.Write(writer, tensors);
        return writer.Detach();
    }

    public static TensorDirectory DecodeTensors(ReadOnlyMemory<byte> buf, bool standalone = true, int cap = Limits.DefaultCap, bool trusted = false) =>
        TensorIo.Decode(buf, cap, trusted, standalone).Directory;
}
