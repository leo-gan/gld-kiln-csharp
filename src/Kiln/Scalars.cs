using System.Buffers.Binary;
using System.Text;

namespace Kiln;

static class Scalars
{
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static int MinSize(Schema schema)
    {
        switch (schema.Kind)
        {
            case SchemaKind.Bool:
            case SchemaKind.I8:
            case SchemaKind.U8:
                return 1;
            case SchemaKind.I16:
            case SchemaKind.U16:
                return 2;
            case SchemaKind.I32:
            case SchemaKind.U32:
            case SchemaKind.F32:
                return 4;
            case SchemaKind.I64:
            case SchemaKind.Int:
            case SchemaKind.U64:
            case SchemaKind.Uint:
            case SchemaKind.F64:
                return 8;
            case SchemaKind.Str:
            case SchemaKind.Bytes:
            case SchemaKind.List:
            case SchemaKind.Dict:
                return 4;
            case SchemaKind.Tensors:
                return 64;
            case SchemaKind.Opt:
                return 1;
            case SchemaKind.Struct:
                long sum = 0;
                foreach (var field in schema.Fields)
                {
                    sum += MinSize(field.Type);
                    if (sum > int.MaxValue)
                        throw new KilnException("LIMIT");
                }
                return (int)sum;
            default:
                throw new KilnException("TYPE");
        }
    }

    public static ulong ElemWidth(Schema schema) =>
        schema.Kind is SchemaKind.U8 or SchemaKind.I8 ? 1UL : (ulong)MinSize(schema);

    public static bool IsPacked(SchemaKind kind) => kind is
        SchemaKind.I8 or SchemaKind.I16 or SchemaKind.I32 or SchemaKind.I64 or SchemaKind.Int or
        SchemaKind.U8 or SchemaKind.U16 or SchemaKind.U32 or SchemaKind.U64 or SchemaKind.Uint or
        SchemaKind.F32 or SchemaKind.F64;

    public static int Width(SchemaKind kind) => kind switch
    {
        SchemaKind.Bool or SchemaKind.I8 or SchemaKind.U8 => 1,
        SchemaKind.I16 or SchemaKind.U16 => 2,
        SchemaKind.I32 or SchemaKind.U32 or SchemaKind.F32 => 4,
        SchemaKind.I64 or SchemaKind.Int or SchemaKind.U64 or SchemaKind.Uint or SchemaKind.F64 => 8,
        _ => 0,
    };

    public static void WriteU32(Writer writer, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(writer.Reserve(4), value);

    public static uint ReadU32(ReadOnlySpan<byte> buf, ref int index)
    {
        if ((uint)index > (uint)buf.Length || buf.Length - index < 4)
            throw new KilnException("EOF");
        var value = BinaryPrimitives.ReadUInt32LittleEndian(buf.Slice(index));
        index += 4;
        return value;
    }

    public static void WriteScalar(Writer writer, SchemaKind kind, object? value)
    {
        switch (kind)
        {
            case SchemaKind.Bool:
                writer.Reserve(1)[0] = (byte)(Values.AsBool(value) ? 1 : 0);
                return;
            case SchemaKind.I8:
                writer.Reserve(1)[0] = Values.AsI8(value);
                return;
            case SchemaKind.U8:
                writer.Reserve(1)[0] = Values.AsU8(value);
                return;
            case SchemaKind.I16:
                BinaryPrimitives.WriteInt16LittleEndian(writer.Reserve(2), Values.AsI16(value));
                return;
            case SchemaKind.U16:
                BinaryPrimitives.WriteUInt16LittleEndian(writer.Reserve(2), Values.AsU16(value));
                return;
            case SchemaKind.I32:
                BinaryPrimitives.WriteInt32LittleEndian(writer.Reserve(4), Values.AsI32(value));
                return;
            case SchemaKind.U32:
                BinaryPrimitives.WriteUInt32LittleEndian(writer.Reserve(4), Values.AsU32(value));
                return;
            case SchemaKind.F32:
                BinaryPrimitives.WriteSingleLittleEndian(writer.Reserve(4), Values.AsF32(value));
                return;
            case SchemaKind.I64:
            case SchemaKind.Int:
                BinaryPrimitives.WriteInt64LittleEndian(writer.Reserve(8), Values.AsI64(value));
                return;
            case SchemaKind.U64:
            case SchemaKind.Uint:
                BinaryPrimitives.WriteUInt64LittleEndian(writer.Reserve(8), Values.AsU64(value));
                return;
            case SchemaKind.F64:
                BinaryPrimitives.WriteDoubleLittleEndian(writer.Reserve(8), Values.AsF64(value));
                return;
            default:
                throw new KilnException("TYPE");
        }
    }

    public static object ReadScalar(ReadOnlySpan<byte> buf, ref int index, SchemaKind kind)
    {
        var width = Width(kind);
        if (width == 0 || (uint)index > (uint)buf.Length || buf.Length - index < width)
            throw new KilnException("EOF");
        var slice = buf.Slice(index, width);
        index += width;
        return kind switch
        {
            SchemaKind.Bool => ReadBool(slice),
            SchemaKind.I8 => (int)(sbyte)slice[0],
            SchemaKind.U8 => (int)slice[0],
            SchemaKind.I16 => (int)BinaryPrimitives.ReadInt16LittleEndian(slice),
            SchemaKind.U16 => (int)BinaryPrimitives.ReadUInt16LittleEndian(slice),
            SchemaKind.I32 => BinaryPrimitives.ReadInt32LittleEndian(slice),
            SchemaKind.U32 => (long)BinaryPrimitives.ReadUInt32LittleEndian(slice),
            SchemaKind.F32 => (double)BinaryPrimitives.ReadSingleLittleEndian(slice),
            SchemaKind.I64 or SchemaKind.Int => BinaryPrimitives.ReadInt64LittleEndian(slice),
            SchemaKind.U64 or SchemaKind.Uint => BinaryPrimitives.ReadUInt64LittleEndian(slice),
            SchemaKind.F64 => BinaryPrimitives.ReadDoubleLittleEndian(slice),
            _ => throw new KilnException("TYPE"),
        };
    }

    public static void WriteBytes(Writer writer, ReadOnlySpan<byte> raw, int cap)
    {
        if (raw.Length > cap)
            throw new KilnException("LIMIT");
        WriteU32(writer, (uint)raw.Length);
        if (raw.Length > 0)
            raw.CopyTo(writer.Reserve(raw.Length));
    }

    public static byte[] ReadBytesRaw(ReadOnlySpan<byte> buf, ref int index, int cap)
    {
        var length = ReadU32(buf, ref index);
        if (length > (uint)cap)
            throw new KilnException("LIMIT");
        if ((uint)index > (uint)buf.Length || (ulong)(buf.Length - index) < length)
            throw new KilnException("EOF");
        var copy = buf.Slice(index, (int)length).ToArray();
        index += (int)length;
        return copy;
    }

    public static void WriteUtf8(Writer writer, string text, int cap)
    {
        var length = Encoding.UTF8.GetByteCount(text);
        if (length > cap)
            throw new KilnException("LIMIT");
        WriteU32(writer, (uint)length);
        if (length > 0)
            Encoding.UTF8.GetBytes(text, writer.Reserve(length));
    }

    public static string ReadString(ReadOnlySpan<byte> buf, ref int index, int cap, bool trusted)
    {
        var length = ReadU32(buf, ref index);
        if (length > (uint)cap)
            throw new KilnException("LIMIT");
        if ((uint)index > (uint)buf.Length || (ulong)(buf.Length - index) < length)
            throw new KilnException("EOF");
        var text = DecodeUtf8(buf.Slice(index, (int)length), trusted);
        index += (int)length;
        return text;
    }

    public static string DecodeUtf8(ReadOnlySpan<byte> raw, bool trusted)
    {
        if (trusted)
            return Encoding.UTF8.GetString(raw);
        try
        {
            return StrictUtf8.GetString(raw);
        }
        catch (ArgumentException)
        {
            throw new KilnException("UTF8");
        }
    }

    static bool ReadBool(ReadOnlySpan<byte> slice)
    {
        if (slice[0] > 1)
            throw new KilnException("TYPE");
        return slice[0] == 1;
    }
}
