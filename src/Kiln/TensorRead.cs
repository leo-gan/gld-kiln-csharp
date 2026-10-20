using System.Buffers.Binary;
using System.Text;

namespace Kiln;

static partial class TensorIo
{
    public static object Read(ReadOnlyMemory<byte> memory, ref int index, int cap, bool trusted)
    {
        var (directory, block) = Decode(memory[index..], cap, trusted, standalone: false);
        if (index > int.MaxValue - block)
            throw new KilnException("LIMIT");
        index += block;
        return directory;
    }

    public static (TensorDirectory Directory, int Block) Decode(ReadOnlyMemory<byte> view, int cap, bool trusted, bool standalone)
    {
        if (TryFastDecode(view, cap, standalone, out var fastDirectory, out var fastBlock))
            return (fastDirectory, fastBlock);
        var span = view.Span;
        if (span.Length < 4)
            throw new KilnException("EOF");
        if (span[0] != (byte)'K' || span[1] != (byte)'L' || span[2] != (byte)'N' || span[3] != (byte)'T')
            throw new KilnException("FRAME");
        if (span.Length < HeaderSize)
            throw new KilnException("EOF");
        var version = span[4];
        var flags = span[5];
        var reserved16 = BinaryPrimitives.ReadUInt16LittleEndian(span[6..]);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        var nameBytes = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
        var payloadBytes = BinaryPrimitives.ReadUInt64LittleEndian(span[16..]);
        var directoryBytes = BinaryPrimitives.ReadUInt64LittleEndian(span[24..]);
        if (span[0..4] is not [0x4B, 0x4C, 0x4E, 0x54] || version != 2 || (flags & ~0x03) != 0 || (flags & FlagLe) == 0)
            throw new KilnException("FRAME");
        if (reserved16 != 0 || Above(span.Slice(32, 32), 0))
            throw new KilnException("FRAME");
        if (cap < 0 || count > (ulong)cap || nameBytes > (ulong)cap || count > int.MaxValue)
            throw new KilnException("LIMIT");
        var tensors = (int)count;
        var expect = Limits.Add(Limits.Mul(count, DescriptorSize), nameBytes);
        if (directoryBytes != expect)
            throw new KilnException("TYPE");
        var directoryEnd = Limits.Add(HeaderSize, directoryBytes);
        if (directoryEnd > (ulong)span.Length)
            throw new KilnException("EOF");
        var payloadBase = Align64(directoryEnd);
        var blockEnd = Limits.Add(payloadBase, payloadBytes);
        if (blockEnd > (ulong)span.Length)
            throw new KilnException("EOF");
        if (Above(span.Slice((int)directoryEnd, (int)(payloadBase - directoryEnd)), 0))
            throw new KilnException("TYPE");
        var raw = new Raw[tensors];
        try
        {
            for (var i = 0; i < tensors; i++)
                raw[i] = ReadDescriptor(span.Slice((int)(HeaderSize + (long)i * DescriptorSize), DescriptorSize));
            var heap = view.Slice((int)(HeaderSize + (long)tensors * DescriptorSize), (int)nameBytes);
            ReadNames(raw, heap.Span, nameBytes, flags, trusted);
            var ranges = new (ulong Start, ulong End)[raw.Length];
            for (var i = 0; i < raw.Length; i++)
            {
                var end = Limits.Add(raw[i].ByteOffset, raw[i].ByteLength);
                if (end > payloadBytes)
                    throw new KilnException("TYPE");
                ranges[i] = (raw[i].ByteOffset, end);
                CheckTensor(raw[i]);
            }
            for (var left = 0; left < ranges.Length; left++)
            {
                for (var right = left + 1; right < ranges.Length; right++)
                {
                    var (a0, a1) = ranges[left];
                    var (b0, b1) = ranges[right];
                    if (a0 == a1 || b0 == b1)
                        continue;
                    if (a0 < b1 && b0 < a1)
                        throw new KilnException("TYPE");
                }
            }
            ulong cursor = 0;
            foreach (var item in raw)
            {
                if (item.ByteOffset != Align64(cursor))
                    throw new KilnException("TYPE");
                cursor = item.ByteOffset + item.ByteLength;
            }
            if (cursor != payloadBytes)
                throw new KilnException("TYPE");
            CheckPayload(raw, view.Slice((int)payloadBase, (int)payloadBytes));
            if (!trusted)
                CheckQuant(raw);
            if (standalone && blockEnd != (ulong)span.Length)
                throw new KilnException("TRAILING");
            return (Publish(raw, flags, (int)blockEnd, trusted), (int)blockEnd);
        }
        finally
        {
            ReleaseShapes(raw);
        }
    }

    static Raw ReadDescriptor(ReadOnlySpan<byte> desc)
    {
        if (Above(desc.Slice(112, 16), 0))
            throw new KilnException("FRAME");
        var raw = new Raw
        {
            Dtype = BinaryPrimitives.ReadUInt16LittleEndian(desc),
            Rank = desc[2],
            Reserved = desc[3],
            NameOff = BinaryPrimitives.ReadUInt16LittleEndian(desc[4..]),
            NameLen = BinaryPrimitives.ReadUInt16LittleEndian(desc[6..]),
            LayoutOff = BinaryPrimitives.ReadUInt16LittleEndian(desc[8..]),
            LayoutLen = BinaryPrimitives.ReadUInt16LittleEndian(desc[10..]),
            BlockSize = BinaryPrimitives.ReadUInt32LittleEndian(desc[12..]),
            QuantScheme = BinaryPrimitives.ReadUInt16LittleEndian(desc[16..]),
            QuantAxis = BinaryPrimitives.ReadUInt16LittleEndian(desc[18..]),
            GroupSize = BinaryPrimitives.ReadUInt32LittleEndian(desc[20..]),
            ScaleIndex = BinaryPrimitives.ReadUInt32LittleEndian(desc[24..]),
            OuterIndex = BinaryPrimitives.ReadUInt32LittleEndian(desc[28..]),
            ByteOffset = BinaryPrimitives.ReadUInt64LittleEndian(desc[32..]),
            ByteLength = BinaryPrimitives.ReadUInt64LittleEndian(desc[40..]),
        };
        raw.Shape = System.Buffers.ArrayPool<ulong>.Shared.Rent(8);
        raw.Shape.AsSpan(0, 8).Clear();
        for (var dim = 0; dim < 8; dim++)
            raw.Shape[dim] = BinaryPrimitives.ReadUInt64LittleEndian(desc[(48 + dim * 8)..]);
        return raw;
    }

    static void ReadNames(Raw[] raw, ReadOnlySpan<byte> heap, uint nameBytes, byte flags, bool trusted)
    {
        var namesFlag = (flags & FlagName) != 0;
        var hasName = false;
        foreach (var item in raw)
            hasName |= item.NameLen != 0;
        if (namesFlag != hasName || (namesFlag && Array.Exists(raw, item => item.NameLen == 0)))
            throw new KilnException("TYPE");
        List<byte[]>? seen = hasName ? new List<byte[]>(raw.Length) : null;
        var cursor = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i].Reserved != 0)
                throw new KilnException("FRAME");
            raw[i].Name = Take(heap, nameBytes, raw[i].NameOff, raw[i].NameLen, emptyOff: true);
            if (raw[i].Name.Length == 0)
                continue;
            if (!trusted)
                DemandUtf8(raw[i].Name);
            foreach (var previous in seen!)
            {
                if (previous.AsSpan().SequenceEqual(raw[i].Name))
                    throw new KilnException("TYPE");
            }
            seen.Add(raw[i].Name);
            cursor = Consume(heap, cursor, raw[i].Name);
        }
        for (var i = 0; i < raw.Length; i++)
        {
            raw[i].Layout = Take(heap, nameBytes, raw[i].LayoutOff, raw[i].LayoutLen, emptyOff: false);
            if (raw[i].Layout.Length == 0)
                continue;
            if (!trusted)
                DemandUtf8(raw[i].Layout);
            cursor = Consume(heap, cursor, raw[i].Layout);
        }
        if (cursor != heap.Length)
            throw new KilnException("TYPE");
    }

    static int Consume(ReadOnlySpan<byte> heap, int cursor, ReadOnlySpan<byte> chunk)
    {
        if ((uint)cursor > (uint)heap.Length || heap.Length - cursor < chunk.Length || !heap.Slice(cursor, chunk.Length).SequenceEqual(chunk))
            throw new KilnException("TYPE");
        return cursor + chunk.Length;
    }

    static byte[] Take(ReadOnlySpan<byte> heap, uint nameBytes, ushort offset, ushort length, bool emptyOff)
    {
        if (length == 0)
        {
            if (offset != 0xFFFF)
                throw new KilnException("TYPE");
            return [];
        }
        if (emptyOff && offset == 0xFFFF)
            throw new KilnException("TYPE");
        var end = Limits.Add(offset, length);
        if (end > nameBytes)
            throw new KilnException("TYPE");
        return heap.Slice(offset, length).ToArray();
    }

    static void DemandUtf8(ReadOnlySpan<byte> raw)
    {
        try
        {
            Strict.GetString(raw);
        }
        catch (ArgumentException)
        {
            throw new KilnException("UTF8");
        }
    }

    static readonly UTF8Encoding Strict = new(false, true);

    static TensorDirectory Publish(Raw[] raw, byte flags, int block, bool trusted)
    {
        var tensors = new TensorValue[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            var item = raw[i];
            var shape = new ulong[item.Rank];
            item.Shape.AsSpan(0, item.Rank).CopyTo(shape);
            tensors[i] = new TensorValue
            {
                Name = item.Name.Length == 0 ? null : Scalars.DecodeUtf8(item.Name, trusted),
                Dtype = DtypeValue(item.Dtype),
                Shape = shape,
                Data = item.Data,
                QuantScheme = item.QuantScheme,
                QuantAxis = item.QuantAxis,
                GroupSize = item.GroupSize,
                ScaleIndex = item.ScaleIndex,
                OuterIndex = item.OuterIndex,
                Layout = item.Layout.Length == 0 ? null : Scalars.DecodeUtf8(item.Layout, trusted),
                BlockSize = item.BlockSize,
            };
        }
        return new TensorDirectory
        {
            Version = 2,
            Flags = (flags & FlagName) != 0 ? ["names", "le"] : ["le"],
            Tensors = tensors,
            BlockLength = block,
        };
    }
}
