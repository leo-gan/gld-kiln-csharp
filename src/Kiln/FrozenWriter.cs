using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Kiln;

static class FrozenWriter
{
    public static void Write(Writer writer, Schema schema, object? value, int cap, int depth)
    {
        if (depth > Limits.MaxDepth)
            throw new KilnException("LIMIT");
        switch (schema.Kind)
        {
            case SchemaKind.Str:
                if (value is not string text)
                    throw new KilnException("TYPE");
                Scalars.WriteUtf8(writer, text, cap);
                return;
            case SchemaKind.Bytes:
                Scalars.WriteBytes(writer, BytesOf(value), cap);
                return;
            case SchemaKind.Tensors:
                TensorIo.Write(writer, value);
                return;
            case SchemaKind.Opt:
                WriteOpt(writer, schema.Item!, value, cap, depth);
                return;
            case SchemaKind.List:
                WriteList(writer, schema.Item!, value, cap, depth);
                return;
            case SchemaKind.Dict:
                WriteDict(writer, schema.Item!, value, cap, depth);
                return;
            case SchemaKind.Struct:
                WriteStruct(writer, schema, value, cap, depth);
                return;
            default:
                Scalars.WriteScalar(writer, schema.Kind, value);
                return;
        }
    }

    static void WriteOpt(Writer writer, Schema inner, object? value, int cap, int depth)
    {
        if (value is null)
        {
            writer.Reserve(1)[0] = 0;
            return;
        }
        writer.Reserve(1)[0] = 1;
        Write(writer, inner, value, cap, depth + 1);
    }

    static void WriteStruct(Writer writer, Schema schema, object? value, int cap, int depth)
    {
        var map = Values.AsMap(value);
        foreach (var field in schema.Fields)
        {
            if (!map.TryGetValue(field.Name, out var item))
                throw new KilnException("TYPE");
            var kind = field.Type.Kind;
            if (Scalars.IsPacked(kind) || kind == SchemaKind.Bool)
                Scalars.WriteScalar(writer, kind, item);
            else if (kind == SchemaKind.Str)
            {
                if (item is not string text)
                    throw new KilnException("TYPE");
                Scalars.WriteUtf8(writer, text, cap);
            }
            else if (kind == SchemaKind.Bytes)
                Scalars.WriteBytes(writer, BytesOf(item), cap);
            else
                Write(writer, field.Type, item, cap, depth + 1);
        }
    }

    static void WriteDict(Writer writer, Schema inner, object? value, int cap, int depth)
    {
        var map = Values.AsMap(value);
        var keys = new string[map.Count];
        var index = 0;
        foreach (var key in map.Keys)
            keys[index++] = key;
        Array.Sort(keys, CompareUtf8);
        Limits.Fit((ulong)keys.Length, 4, ulong.MaxValue, cap);
        Scalars.WriteU32(writer, (uint)keys.Length);
        foreach (var key in keys)
        {
            Write(writer, Schema.Str, key, cap, depth + 1);
            Write(writer, inner, map[key], cap, depth + 1);
        }
    }

    static void WriteList(Writer writer, Schema elem, object? value, int cap, int depth)
    {
        if (elem.Kind == SchemaKind.U8 && Values.TryBytes(value, out var raw))
        {
            Limits.Fit((ulong)raw.Length, 1, ulong.MaxValue, cap);
            if (raw.Length > cap)
                throw new KilnException("LIMIT");
            Scalars.WriteU32(writer, (uint)raw.Length);
            raw.Span.CopyTo(writer.Reserve(raw.Length));
            return;
        }
        if (elem.Kind == SchemaKind.I8 && value is byte[] signedBytes)
        {
            Limits.Fit((ulong)signedBytes.Length, 1, ulong.MaxValue, cap);
            if (signedBytes.Length > cap)
                throw new KilnException("LIMIT");
            Scalars.WriteU32(writer, (uint)signedBytes.Length);
            signedBytes.CopyTo(writer.Reserve(signedBytes.Length));
            return;
        }
        if (value is not System.Collections.IList list)
            throw new KilnException("TYPE");
        var count = list.Count;
        if (count < 0)
            throw new KilnException("LIMIT");
        Limits.Fit((ulong)count, Scalars.ElemWidth(elem), ulong.MaxValue, cap);
        Scalars.WriteU32(writer, (uint)count);
        if (TryWritePacked(writer, elem.Kind, list, count))
            return;
        for (var i = 0; i < count; i++)
            Write(writer, elem, list[i], cap, depth + 1);
    }

    static bool TryWritePacked(Writer writer, SchemaKind kind, System.Collections.IList list, int count)
    {
        if (!Scalars.IsPacked(kind) || count == 0)
            return count == 0 && Scalars.IsPacked(kind);
        var width = Scalars.Width(kind);
        if (kind == SchemaKind.F64 && list is double[] doubles)
        {
            CopyLe(MemoryMarshal.AsBytes(doubles.AsSpan()), writer.Reserve(count * width));
            return true;
        }
        if (kind == SchemaKind.F32 && list is float[] floats)
        {
            CopyLe(MemoryMarshal.AsBytes(floats.AsSpan()), writer.Reserve(count * width));
            return true;
        }
        if (kind == SchemaKind.F32 && list is double[] widened)
        {
            var dest = writer.Reserve(count * 4);
            for (var i = 0; i < count; i++)
                BinaryPrimitives.WriteSingleLittleEndian(dest[(i * 4)..], (float)widened[i]);
            return true;
        }
        if (kind == SchemaKind.I32 && list is int[] ints)
        {
            CopyLe(MemoryMarshal.AsBytes(ints.AsSpan()), writer.Reserve(count * width));
            return true;
        }
        if (kind == SchemaKind.I64 && list is long[] longs)
        {
            CopyLe(MemoryMarshal.AsBytes(longs.AsSpan()), writer.Reserve(count * width));
            return true;
        }
        if (kind == SchemaKind.U64 && list is ulong[] ulongs)
        {
            CopyLe(MemoryMarshal.AsBytes(ulongs.AsSpan()), writer.Reserve(count * width));
            return true;
        }
        if (!Scalars.IsPacked(kind))
            return false;
        for (var i = 0; i < count; i++)
            Scalars.WriteScalar(writer, kind, list[i]);
        return true;
    }

    static void CopyLe(ReadOnlySpan<byte> raw, Span<byte> dest)
    {
        if (!BitConverter.IsLittleEndian)
            throw new KilnException("TYPE");
        raw.CopyTo(dest);
    }

    static ReadOnlySpan<byte> BytesOf(object? value)
    {
        if (Values.TryBytes(value, out var memory))
            return memory.Span;
        if (value is System.Collections.IList list)
        {
            var raw = new byte[list.Count];
            for (var i = 0; i < list.Count; i++)
                raw[i] = Values.AsU8(list[i]);
            return raw;
        }
        throw new KilnException("TYPE");
    }

    static int CompareUtf8(string left, string right)
    {
        Span<byte> a = stackalloc byte[256];
        Span<byte> b = stackalloc byte[256];
        var leftBytes = KeyBytes(left, ref a);
        var rightBytes = KeyBytes(right, ref b);
        return leftBytes.SequenceCompareTo(rightBytes);
    }

    static ReadOnlySpan<byte> KeyBytes(string text, ref Span<byte> buffer)
    {
        var need = Encoding.UTF8.GetByteCount(text);
        if (need > buffer.Length)
            buffer = new byte[need];
        var written = Encoding.UTF8.GetBytes(text, buffer);
        return buffer[..written];
    }
}
