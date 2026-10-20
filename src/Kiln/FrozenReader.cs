using System.Buffers.Binary;

namespace Kiln;

static class FrozenReader
{
    public static object? Read(ReadOnlyMemory<byte> memory, ref int index, Schema schema, int cap, int depth, bool trusted)
    {
        if (depth > Limits.MaxDepth)
            throw new KilnException("LIMIT");
        var buf = memory.Span;
        switch (schema.Kind)
        {
            case SchemaKind.Str:
                return Scalars.ReadString(buf, ref index, cap, trusted);
            case SchemaKind.Bytes:
                return Scalars.ReadBytesRaw(buf, ref index, cap);
            case SchemaKind.Tensors:
                return TensorIo.Read(memory, ref index, cap, trusted);
            case SchemaKind.Opt:
                return ReadOpt(memory, ref index, schema.Item!, cap, depth, trusted);
            case SchemaKind.List:
                return ReadList(memory, ref index, schema.Item!, cap, depth, trusted);
            case SchemaKind.Dict:
                return ReadDict(memory, ref index, schema.Item!, cap, depth, trusted);
            case SchemaKind.Struct:
                return ReadStruct(memory, ref index, schema, cap, depth, trusted);
            default:
                return Scalars.ReadScalar(buf, ref index, schema.Kind);
        }
    }

    static object? ReadOpt(ReadOnlyMemory<byte> memory, ref int index, Schema inner, int cap, int depth, bool trusted)
    {
        var buf = memory.Span;
        if ((uint)index >= (uint)buf.Length)
            throw new KilnException("EOF");
        var tag = buf[index++];
        if (tag == 0)
            return null;
        if (tag != 1)
            throw new KilnException("TYPE");
        return Read(memory, ref index, inner, cap, depth + 1, trusted);
    }

    static Dictionary<string, object?> ReadStruct(ReadOnlyMemory<byte> memory, ref int index, Schema schema, int cap, int depth, bool trusted)
    {
        var result = new Dictionary<string, object?>(schema.Fields.Length);
        foreach (var field in schema.Fields)
        {
            var kind = field.Type.Kind;
            if (Scalars.IsPacked(kind) || kind == SchemaKind.Bool)
                result[field.Name] = Scalars.ReadScalar(memory.Span, ref index, kind);
            else if (kind is SchemaKind.Str or SchemaKind.Bytes)
                result[field.Name] = Read(memory, ref index, field.Type, cap, depth + 1, trusted);
            else
                result[field.Name] = Read(memory, ref index, field.Type, cap, depth + 1, trusted);
        }
        return result;
    }

    static Dictionary<string, object?> ReadDict(ReadOnlyMemory<byte> memory, ref int index, Schema inner, int cap, int depth, bool trusted)
    {
        var buf = memory.Span;
        var count = Scalars.ReadU32(buf, ref index);
        var unread = (uint)index > (uint)buf.Length ? 0UL : (ulong)(buf.Length - index);
        Limits.Fit(count, 4, unread, cap);
        var result = new Dictionary<string, object?>((int)count);
        byte[]? previous = null;
        for (var n = 0u; n < count; n++)
        {
            var keyStart = index;
            var raw = Scalars.ReadBytesRaw(buf, ref index, cap);
            if (previous is not null && raw.AsSpan().SequenceCompareTo(previous) < 0)
                throw new KilnException("TYPE");
            previous = raw;
            var key = Scalars.DecodeUtf8(raw, trusted);
            result[key] = Read(memory, ref index, inner, cap, depth + 1, trusted);
            _ = keyStart;
        }
        return result;
    }

    static object ReadList(ReadOnlyMemory<byte> memory, ref int index, Schema elem, int cap, int depth, bool trusted)
    {
        var buf = memory.Span;
        var count = Scalars.ReadU32(buf, ref index);
        var unread = (uint)index > (uint)buf.Length ? 0UL : (ulong)(buf.Length - index);
        Limits.Fit(count, Scalars.ElemWidth(elem), unread, cap);
        if (TryReadPacked(buf, ref index, elem.Kind, (int)count, out var packed))
            return packed;
        var list = new List<object?>((int)count);
        for (var n = 0u; n < count; n++)
            list.Add(Read(memory, ref index, elem, cap, depth + 1, trusted));
        return list;
    }

    static bool TryReadPacked(ReadOnlySpan<byte> buf, ref int index, SchemaKind kind, int count, out object packed)
    {
        packed = null!;
        if (kind == SchemaKind.Bool || !Scalars.IsPacked(kind) && kind is not (SchemaKind.U8 or SchemaKind.I8))
        {
            if (kind != SchemaKind.U8 && kind != SchemaKind.I8 && !Scalars.IsPacked(kind))
                return false;
        }
        if (kind == SchemaKind.Bool)
            return false;
        var width = Scalars.Width(kind);
        var bytes = count * width;
        if ((uint)index > (uint)buf.Length || buf.Length - index < bytes)
            throw new KilnException("EOF");
        var slice = buf.Slice(index, bytes);
        index += bytes;
        packed = kind switch
        {
            SchemaKind.U8 => slice.ToArray(),
            SchemaKind.I8 => SignedBytes(slice),
            SchemaKind.I16 => Ints(slice, 2, static span => BinaryPrimitives.ReadInt16LittleEndian(span)),
            SchemaKind.U16 => Ints(slice, 2, static span => BinaryPrimitives.ReadUInt16LittleEndian(span)),
            SchemaKind.I32 => Int32s(slice),
            SchemaKind.U32 => Longs(slice, static span => BinaryPrimitives.ReadUInt32LittleEndian(span)),
            SchemaKind.I64 or SchemaKind.Int => Int64s(slice),
            SchemaKind.U64 or SchemaKind.Uint => UInt64s(slice),
            SchemaKind.F32 => F32s(slice),
            SchemaKind.F64 => F64s(slice),
            _ => throw new KilnException("TYPE"),
        };
        return true;
    }

    static int[] SignedBytes(ReadOnlySpan<byte> slice)
    {
        var values = new int[slice.Length];
        for (var i = 0; i < slice.Length; i++)
            values[i] = (sbyte)slice[i];
        return values;
    }

    static int[] Ints(ReadOnlySpan<byte> slice, int width, Func<ReadOnlySpan<byte>, int> read)
    {
        var values = new int[slice.Length / width];
        for (var i = 0; i < values.Length; i++)
            values[i] = read(slice[(i * width)..]);
        return values;
    }

    static int[] Int32s(ReadOnlySpan<byte> slice)
    {
        var values = new int[slice.Length / 4];
        for (var i = 0; i < values.Length; i++)
            values[i] = BinaryPrimitives.ReadInt32LittleEndian(slice[(i * 4)..]);
        return values;
    }

    static long[] Longs(ReadOnlySpan<byte> slice, Func<ReadOnlySpan<byte>, uint> read)
    {
        var values = new long[slice.Length / 4];
        for (var i = 0; i < values.Length; i++)
            values[i] = read(slice[(i * 4)..]);
        return values;
    }

    static long[] Int64s(ReadOnlySpan<byte> slice)
    {
        var values = new long[slice.Length / 8];
        for (var i = 0; i < values.Length; i++)
            values[i] = BinaryPrimitives.ReadInt64LittleEndian(slice[(i * 8)..]);
        return values;
    }

    static ulong[] UInt64s(ReadOnlySpan<byte> slice)
    {
        var values = new ulong[slice.Length / 8];
        for (var i = 0; i < values.Length; i++)
            values[i] = BinaryPrimitives.ReadUInt64LittleEndian(slice[(i * 8)..]);
        return values;
    }

    static double[] F32s(ReadOnlySpan<byte> slice)
    {
        var values = new double[slice.Length / 4];
        for (var i = 0; i < values.Length; i++)
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(slice[(i * 4)..]);
        return values;
    }

    static double[] F64s(ReadOnlySpan<byte> slice)
    {
        var values = new double[slice.Length / 8];
        if (!BitConverter.IsLittleEndian)
        {
            for (var i = 0; i < values.Length; i++)
                values[i] = BinaryPrimitives.ReadDoubleLittleEndian(slice[(i * 8)..]);
            return values;
        }
        MemoryMarshalCopy(slice, values);
        return values;
    }

    static void MemoryMarshalCopy(ReadOnlySpan<byte> slice, double[] values)
    {
        slice.CopyTo(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
    }
}
