using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Kiln;

/// <summary>Checked positional reader over a byte span.</summary>
public ref struct Reader
{
    readonly ReadOnlySpan<byte> _buf;
    int _pos;
    readonly int _cap;
    readonly bool _trusted;

    public Reader(ReadOnlySpan<byte> buf, int cap = Limits.DefaultCap, bool trusted = false)
    {
        _buf = buf;
        _pos = 0;
        _cap = cap;
        _trusted = trusted;
    }

    public int Position => _pos;
    public int Remaining => _buf.Length - _pos;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBool()
    {
        if ((uint)_pos >= (uint)_buf.Length)
            throw new KilnException("EOF");
        var v = _buf[_pos++];
        if (v > 1)
            throw new KilnException("TYPE");
        return v == 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadU32()
    {
        var pos = _pos;
        if (pos + 4 > _buf.Length)
            throw new KilnException("EOF");
        var v = BinaryPrimitives.ReadUInt32LittleEndian(_buf.Slice(pos, 4));
        _pos = pos + 4;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadI32()
    {
        var pos = _pos;
        if (pos + 4 > _buf.Length)
            throw new KilnException("EOF");
        var v = BinaryPrimitives.ReadInt32LittleEndian(_buf.Slice(pos, 4));
        _pos = pos + 4;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadU64()
    {
        var pos = _pos;
        if (pos + 8 > _buf.Length)
            throw new KilnException("EOF");
        var v = BinaryPrimitives.ReadUInt64LittleEndian(_buf.Slice(pos, 8));
        _pos = pos + 8;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadI64()
    {
        var pos = _pos;
        if (pos + 8 > _buf.Length)
            throw new KilnException("EOF");
        var v = BinaryPrimitives.ReadInt64LittleEndian(_buf.Slice(pos, 8));
        _pos = pos + 8;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadF32()
    {
        var pos = _pos;
        if (pos + 4 > _buf.Length)
            throw new KilnException("EOF");
        var v = BinaryPrimitives.ReadSingleLittleEndian(_buf.Slice(pos, 4));
        _pos = pos + 4;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadF64()
    {
        var pos = _pos;
        if (pos + 8 > _buf.Length)
            throw new KilnException("EOF");
        var v = BinaryPrimitives.ReadDoubleLittleEndian(_buf.Slice(pos, 8));
        _pos = pos + 8;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadCount(int eachMin)
    {
        var count = ReadU32();
        if (count > (uint)_cap)
            throw new KilnException("LIMIT");
        if (eachMin > 0 && (ulong)count * (ulong)eachMin > (ulong)(_buf.Length - _pos))
            throw new KilnException("EOF");
        return (int)count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadString()
    {
        var n = ReadCount(1);
        if (n == 0)
            return "";
        var raw = _buf.Slice(_pos, n);
        _pos += n;
        if (Ascii.IsValid(raw))
            return Encoding.ASCII.GetString(raw);
        return Scalars.DecodeUtf8(raw, _trusted);
    }

    public List<double> ReadF64List()
    {
        var n = ReadCount(8);
        var list = new List<double>(n);
        if (n == 0)
            return list;
        CollectionsMarshal.SetCount(list, n);
        var dst = CollectionsMarshal.AsSpan(list);
        var nbytes = n * 8;
        var src = _buf.Slice(_pos, nbytes);
        _pos += nbytes;
        if (BitConverter.IsLittleEndian)
        {
            src.CopyTo(MemoryMarshal.AsBytes(dst));
            return list;
        }
        for (var i = 0; i < n; i++)
            dst[i] = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i * 8, 8));
        return list;
    }

    public double[] ReadF64s()
    {
        var n = ReadCount(8);
        if (n == 0)
            return [];
        var arr = GC.AllocateUninitializedArray<double>(n);
        var nbytes = n * 8;
        var src = _buf.Slice(_pos, nbytes);
        _pos += nbytes;
        if (BitConverter.IsLittleEndian)
        {
            src.CopyTo(MemoryMarshal.AsBytes(arr.AsSpan()));
            return arr;
        }
        for (var i = 0; i < n; i++)
            arr[i] = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i * 8, 8));
        return arr;
    }

    public float[] ReadF32s()
    {
        var n = ReadCount(4);
        if (n == 0)
            return [];
        var arr = GC.AllocateUninitializedArray<float>(n);
        var nbytes = n * 4;
        var src = _buf.Slice(_pos, nbytes);
        _pos += nbytes;
        if (BitConverter.IsLittleEndian)
        {
            src.CopyTo(MemoryMarshal.AsBytes(arr.AsSpan()));
            return arr;
        }
        for (var i = 0; i < n; i++)
            arr[i] = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i * 4, 4));
        return arr;
    }

    public long[] ReadI64s()
    {
        var n = ReadCount(8);
        if (n == 0)
            return [];
        var arr = GC.AllocateUninitializedArray<long>(n);
        var nbytes = n * 8;
        var src = _buf.Slice(_pos, nbytes);
        _pos += nbytes;
        if (BitConverter.IsLittleEndian)
        {
            src.CopyTo(MemoryMarshal.AsBytes(arr.AsSpan()));
            return arr;
        }
        for (var i = 0; i < n; i++)
            arr[i] = BinaryPrimitives.ReadInt64LittleEndian(src.Slice(i * 8, 8));
        return arr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Finish()
    {
        if (_pos != _buf.Length)
            throw new KilnException("TRAILING");
    }
}
