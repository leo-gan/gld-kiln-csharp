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
