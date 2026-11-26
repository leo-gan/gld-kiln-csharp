using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Kiln;

/// <summary>Reusable encode buffer. <see cref="Encode"/> resets the length and keeps the storage.</summary>
public sealed class Writer
{
    byte[] _buf;
    int _end;

    public Writer(int capacity = 64) => _buf = new byte[Math.Max(capacity, 0)];

    public int End => _end;
    public ReadOnlySpan<byte> Span => _buf.AsSpan(0, _end);
    public ReadOnlyMemory<byte> Written => new(_buf, 0, _end);

    public void Reset() => _end = 0;

    public void EnsureCapacity(int totalBytes)
    {
        if (totalBytes > _buf.Length)
            GrowTo(totalBytes);
    }

    public byte[] ToArray() => _buf.AsSpan(0, _end).ToArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBool(bool value)
    {
        if (_end >= _buf.Length)
            Grow(1);
        _buf[_end++] = value ? (byte)1 : (byte)0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteU32(uint value)
    {
        var pos = _end;
        if (pos + 4 > _buf.Length)
            Grow(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buf.AsSpan(pos, 4), value);
        _end = pos + 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteI32(int value)
    {
        var pos = _end;
        if (pos + 4 > _buf.Length)
            Grow(4);
        BinaryPrimitives.WriteInt32LittleEndian(_buf.AsSpan(pos, 4), value);
        _end = pos + 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteU64(ulong value)
    {
        var pos = _end;
        if (pos + 8 > _buf.Length)
            Grow(8);
        BinaryPrimitives.WriteUInt64LittleEndian(_buf.AsSpan(pos, 8), value);
        _end = pos + 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteI64(long value)
    {
        var pos = _end;
        if (pos + 8 > _buf.Length)
            Grow(8);
        BinaryPrimitives.WriteInt64LittleEndian(_buf.AsSpan(pos, 8), value);
        _end = pos + 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteF32(float value)
    {
        var pos = _end;
        if (pos + 4 > _buf.Length)
            Grow(4);
        BinaryPrimitives.WriteSingleLittleEndian(_buf.AsSpan(pos, 4), value);
        _end = pos + 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteF64(double value)
    {
        var pos = _end;
        if (pos + 8 > _buf.Length)
            Grow(8);
        BinaryPrimitives.WriteDoubleLittleEndian(_buf.AsSpan(pos, 8), value);
        _end = pos + 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteCount(int count, int cap = Limits.DefaultCap)
    {
        if ((uint)count > (uint)cap)
            throw new KilnException("LIMIT");
        WriteU32((uint)count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteString(string value, int cap = Limits.DefaultCap)
    {
        var charLen = value.Length;
        if (charLen == 0)
        {
            WriteU32(0);
            return;
        }
        if ((uint)charLen > (uint)cap)
            throw new KilnException("LIMIT");
        var pos = _end;
        if (pos + 4 + charLen > _buf.Length)
            Grow(4 + charLen);
        var status = Ascii.FromUtf16(value.AsSpan(), _buf.AsSpan(pos + 4, charLen), out var written);
        if (status == OperationStatus.Done)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_buf.AsSpan(pos, 4), (uint)written);
            _end = pos + 4 + written;
            return;
        }
        WriteStringUtf8Slow(value, cap);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void WriteStringUtf8Slow(string value, int cap)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        if ((uint)byteCount > (uint)cap)
            throw new KilnException("LIMIT");
        var pos = _end;
        if (pos + 4 + byteCount > _buf.Length)
            Grow(4 + byteCount);
        BinaryPrimitives.WriteUInt32LittleEndian(_buf.AsSpan(pos, 4), (uint)byteCount);
        Encoding.UTF8.GetBytes(value.AsSpan(), _buf.AsSpan(pos + 4, byteCount));
        _end = pos + 4 + byteCount;
    }

    public void WriteF64s(ReadOnlySpan<double> values, int cap = Limits.DefaultCap)
    {
        WriteCount(values.Length, cap);
        if (values.Length == 0)
            return;
        var nbytes = checked(values.Length * 8);
        var dest = Reserve(nbytes);
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(values).CopyTo(dest);
            return;
        }
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i * 8, 8), values[i]);
    }

    public void WriteF32s(ReadOnlySpan<float> values, int cap = Limits.DefaultCap)
    {
        WriteCount(values.Length, cap);
        if (values.Length == 0)
            return;
        var nbytes = checked(values.Length * 4);
        var dest = Reserve(nbytes);
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(values).CopyTo(dest);
            return;
        }
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i * 4, 4), values[i]);
    }

    public void WriteI64s(ReadOnlySpan<long> values, int cap = Limits.DefaultCap)
    {
        WriteCount(values.Length, cap);
        if (values.Length == 0)
            return;
        var nbytes = checked(values.Length * 8);
        var dest = Reserve(nbytes);
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(values).CopyTo(dest);
            return;
        }
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(i * 8, 8), values[i]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void Grow(int count)
    {
        var need = _end + count;
        if (count < 0 || need < 0)
            throw new KilnException("LIMIT");
        GrowTo(need);
    }

    void GrowTo(int need)
    {
        var size = _buf.Length == 0 || need > (long)_buf.Length * 2 ? need : _buf.Length;
        while (size < need)
        {
            if (size > int.MaxValue / 2)
            {
                size = need;
                break;
            }
            size *= 2;
        }
        var grown = size >= 4096
            ? GC.AllocateUninitializedArray<byte>(size)
            : new byte[size];
        if (_end > 0)
            _buf.AsSpan(0, _end).CopyTo(grown);
        _buf = grown;
    }

    public ReadOnlyMemory<byte> Encode(Schema schema, object? value, int cap = Limits.DefaultCap)
    {
        _end = 0;
        FrozenWriter.Write(this, schema, value, cap, 0);
        return Written;
    }

    public ReadOnlyMemory<byte> EncodeFramed(Schema schema, object? value, int cap = Limits.DefaultCap)
    {
        _end = 0;
        var head = Reserve(16);
        "KLN0"u8.CopyTo(head);
        head[4] = head[5] = head[6] = head[7] = 0;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(head.Slice(8), TypeHash.Compute(schema));
        FrozenWriter.Write(this, schema, value, cap, 0);
        return Written;
    }

    internal Span<byte> Reserve(int count)
    {
        if (count < 0)
            throw new KilnException("LIMIT");
        var need = _end + count;
        if (need < 0)
            throw new KilnException("LIMIT");
        if (need > _buf.Length)
        {
            var size = _buf.Length == 0 || need > (long)_buf.Length * 2 ? need : _buf.Length;
            while (size < need)
            {
                if (size > int.MaxValue / 2)
                {
                    size = need;
                    break;
                }
                size *= 2;
            }
            var grown = size >= 4096
                ? GC.AllocateUninitializedArray<byte>(size)
                : new byte[size];
            if (_end > 0)
                _buf.AsSpan(0, _end).CopyTo(grown);
            _buf = grown;
        }
        var span = _buf.AsSpan(_end, count);
        _end += count;
        return span;
    }

    internal byte[] Detach()
    {
        var result = _end == _buf.Length ? _buf : _buf.AsSpan(0, _end).ToArray();
        _buf = [];
        _end = 0;
        return result;
    }
}
