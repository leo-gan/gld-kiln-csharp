using System.Buffers.Binary;

namespace Kiln;

static partial class TensorIo
{
    sealed class Prep
    {
        public ushort Dtype;
        public byte Rank;
        public ulong[] Shape = new ulong[8];
        public byte[] Name = [];
        public byte[] Layout = [];
        public ushort NameOff = 0xFFFF;
        public ushort NameLen;
        public ushort LayoutOff = 0xFFFF;
        public ushort LayoutLen;
        public uint BlockSize;
        public ushort QuantScheme;
        public ushort QuantAxis = 0xFFFF;
        public uint GroupSize;
        public uint ScaleIndex = uint.MaxValue;
        public uint OuterIndex = uint.MaxValue;
        public ulong ByteOffset;
        public ReadOnlyMemory<byte> Data;
    }

    public static void Write(Writer writer, object? value)
    {
        if (TryWriteDirect(writer, value))
            return;
        var prepared = Prepare(value);
        Stamp(prepared);
        ulong payload = 0;
        foreach (var tensor in prepared)
        {
            tensor.ByteOffset = Align64(payload);
            payload = Limits.Add(tensor.ByteOffset, (ulong)tensor.Data.Length);
        }
        var heapLength = 0;
        foreach (var tensor in prepared)
            heapLength += tensor.Name.Length + tensor.Layout.Length;
        var names = false;
        foreach (var tensor in prepared)
            names |= tensor.Name.Length > 0;
        var directoryBytes = Limits.Add(Limits.Mul((ulong)prepared.Length, DescriptorSize), (ulong)heapLength);
        var payloadBase = Align64(Limits.Add(HeaderSize, directoryBytes));
        var blockEnd = Limits.Add(payloadBase, payload);
        if (blockEnd > int.MaxValue)
            throw new KilnException("LIMIT");
        var dest = writer.Reserve((int)blockEnd);
        var payloadAt = (int)payloadBase;
        dest[..payloadAt].Clear();
        WriteHeader(dest, prepared.Length, heapLength, payload, directoryBytes, (byte)(FlagLe | (names ? FlagName : 0)));
        var cursor = HeaderSize;
        for (var i = 0; i < prepared.Length; i++)
        {
            WriteDescriptor(dest[cursor..], prepared[i]);
            cursor += DescriptorSize;
        }
        foreach (var tensor in prepared)
        {
            if (tensor.Name.Length == 0)
                continue;
            tensor.Name.CopyTo(dest[cursor..]);
            cursor += tensor.Name.Length;
        }
        foreach (var tensor in prepared)
        {
            if (tensor.Layout.Length == 0)
                continue;
            tensor.Layout.CopyTo(dest[cursor..]);
            cursor += tensor.Layout.Length;
        }
        var placed = 0;
        foreach (var tensor in prepared)
        {
            var offset = (int)tensor.ByteOffset;
            if (offset > placed)
                dest.Slice(payloadAt + placed, offset - placed).Clear();
            tensor.Data.Span.CopyTo(dest[(payloadAt + offset)..]);
            placed = offset + tensor.Data.Length;
        }
        if (placed < (int)payload)
            dest.Slice(payloadAt + placed, (int)payload - placed).Clear();
    }

    static Prep[] Prepare(object? value)
    {
        if (value is TensorDirectory directory)
            return FromList(directory.Tensors);
        if (value is IReadOnlyDictionary<string, object?> map && map.TryGetValue("tensors", out var nested))
            value = nested;
        if (value is System.Collections.IList list)
            return FromList(list);
        throw new KilnException("TYPE");
    }

    static Prep[] FromList(System.Collections.IList list)
    {
        var prepared = new Prep[list.Count];
        for (var i = 0; i < list.Count; i++)
            prepared[i] = FromTensor(list[i] ?? throw new KilnException("TYPE"));
        return prepared;
    }

    static Prep FromTensor(object value)
    {
        if (value is TensorValue tensor)
            return FromValue(tensor);
        if (value is IReadOnlyDictionary<string, object?> map)
            return FromValue(FromMap(map));
        throw new KilnException("TYPE");
    }

    static TensorValue FromMap(IReadOnlyDictionary<string, object?> map)
    {
        if (!map.TryGetValue("dtype", out var dtype) || !map.TryGetValue("shape", out var shape) || !map.TryGetValue("data", out var data))
            throw new KilnException("TYPE");
        var tensor = new TensorValue { Dtype = dtype, Shape = ShapeOf(shape), Data = DataOf(data) };
        if (map.TryGetValue("name", out var name) && name is string text)
            tensor.Name = text;
        if (map.TryGetValue("layout", out var layout) && layout is string layoutText)
            tensor.Layout = layoutText;
        if (map.TryGetValue("quant_scheme", out var scheme) && scheme is not null)
            tensor.QuantScheme = (int)Values.AsI64(scheme);
        if (map.TryGetValue("quant_axis", out var axis) && axis is not null)
            tensor.QuantAxis = (int)Values.AsI64(axis);
        if (map.TryGetValue("group_size", out var group) && group is not null)
            tensor.GroupSize = Values.AsU32(group);
        if (map.TryGetValue("scale_index", out var scale) && scale is not null)
            tensor.ScaleIndex = Values.AsU32(scale);
        if (map.TryGetValue("outer_index", out var outer) && outer is not null)
            tensor.OuterIndex = Values.AsU32(outer);
        if (map.TryGetValue("block_size", out var block) && block is not null)
            tensor.BlockSize = Values.AsU32(block);
        return tensor;
    }

    static Prep FromValue(TensorValue tensor)
    {
        var shape = tensor.Shape ?? [];
        if (shape.Length > 8)
            throw new KilnException("TYPE");
        var prep = new Prep
        {
            Dtype = CheckedU16(DtypeCode(tensor.Dtype)),
            Rank = (byte)shape.Length,
            Name = Utf8(tensor.Name),
            Layout = Utf8(tensor.Layout),
            Data = tensor.Data,
            BlockSize = tensor.BlockSize,
            QuantScheme = CheckedU16(tensor.QuantScheme),
            QuantAxis = CheckedU16(tensor.QuantAxis),
            GroupSize = tensor.GroupSize,
            ScaleIndex = tensor.ScaleIndex,
            OuterIndex = tensor.OuterIndex,
        };
        shape.AsSpan().CopyTo(prep.Shape);
        return prep;
    }

    static void Stamp(Prep[] prepared)
    {
        var heap = 0;
        foreach (var tensor in prepared)
            Place(tensor.Name, ref heap, out tensor.NameOff, out tensor.NameLen);
        foreach (var tensor in prepared)
            Place(tensor.Layout, ref heap, out tensor.LayoutOff, out tensor.LayoutLen);
    }

    static void Place(byte[] bytes, ref int heap, out ushort offset, out ushort length)
    {
        if (bytes.Length == 0)
        {
            offset = 0xFFFF;
            length = 0;
            return;
        }
        if (heap > ushort.MaxValue || bytes.Length > ushort.MaxValue)
            throw new KilnException("LIMIT");
        offset = (ushort)heap;
        length = (ushort)bytes.Length;
        heap += bytes.Length;
    }

    static ulong[] ShapeOf(object? shape)
    {
        if (shape is ulong[] ready)
            return ready;
        if (shape is not System.Collections.IList list || list.Count > 8)
            throw new KilnException("TYPE");
        var values = new ulong[list.Count];
        for (var i = 0; i < list.Count; i++)
            values[i] = Values.AsU64(list[i]);
        return values;
    }

    static ReadOnlyMemory<byte> DataOf(object? data)
    {
        if (Values.TryBytes(data, out var bytes))
            return bytes;
        throw new KilnException("TYPE");
    }

    static ushort CheckedU16(int value)
    {
        if ((uint)value > ushort.MaxValue)
            throw new KilnException("TYPE");
        return (ushort)value;
    }

    static void WriteHeader(Span<byte> dest, int count, int heap, ulong payload, ulong directory, byte flags)
    {
        "KLNT"u8.CopyTo(dest);
        dest[4] = 2;
        dest[5] = flags;
        BinaryPrimitives.WriteUInt32LittleEndian(dest[8..], (uint)count);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[12..], (uint)heap);
        BinaryPrimitives.WriteUInt64LittleEndian(dest[16..], payload);
        BinaryPrimitives.WriteUInt64LittleEndian(dest[24..], directory);
    }

    static void WriteDescriptor(Span<byte> dest, Prep tensor)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(dest, tensor.Dtype);
        dest[2] = tensor.Rank;
        BinaryPrimitives.WriteUInt16LittleEndian(dest[4..], tensor.NameOff);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[6..], tensor.NameLen);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[8..], tensor.LayoutOff);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[10..], tensor.LayoutLen);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[12..], tensor.BlockSize);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[16..], tensor.QuantScheme);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[18..], tensor.QuantAxis);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[20..], tensor.GroupSize);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[24..], tensor.ScaleIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[28..], tensor.OuterIndex);
        BinaryPrimitives.WriteUInt64LittleEndian(dest[32..], tensor.ByteOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(dest[40..], (ulong)tensor.Data.Length);
        for (var dim = 0; dim < 8; dim++)
            BinaryPrimitives.WriteUInt64LittleEndian(dest[(48 + dim * 8)..], tensor.Shape[dim]);
    }
}
