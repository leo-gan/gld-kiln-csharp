namespace Kiln;

static partial class TensorIo
{
    sealed class Raw
    {
        public int Dtype;
        public int Rank;
        public byte Reserved;
        public ushort NameOff;
        public ushort NameLen;
        public ushort LayoutOff;
        public ushort LayoutLen;
        public uint BlockSize;
        public int QuantScheme;
        public int QuantAxis;
        public uint GroupSize;
        public uint ScaleIndex;
        public uint OuterIndex;
        public ulong ByteOffset;
        public ulong ByteLength;
        public ulong[] Shape = null!;
        public byte[] Name = [];
        public byte[] Layout = [];
        public ReadOnlyMemory<byte> Data;
    }

    static void CheckTensor(Raw item)
    {
        if (item.Rank > 8)
            throw new KilnException("TYPE");
        for (var dim = item.Rank; dim < 8; dim++)
        {
            if (item.Shape[dim] != 0)
                throw new KilnException("TYPE");
        }
        if (item.Dtype == 0 || (item.Dtype >= 20 && item.Dtype < OpaqueMin) || item.Dtype > OpaqueMax)
            throw new KilnException("TYPE");
        var count = ElementCount(item.Rank, item.Shape);
        if (item.Dtype >= OpaqueMin)
        {
            if (item.Layout.Length == 0 || item.BlockSize == 0 || item.ByteLength % item.BlockSize != 0)
                throw new KilnException("TYPE");
            return;
        }
        if (item.Layout.Length != 0 || item.BlockSize != 0 || item.ByteLength != ExpectedBytes(item.Dtype, count))
            throw new KilnException("TYPE");
    }

    static void CheckQuant(Raw[] tensors)
    {
        for (var index = 0; index < tensors.Length; index++)
        {
            var item = tensors[index];
            if (item.QuantScheme == 0)
            {
                if (item.GroupSize != 0 || item.QuantAxis != 0xFFFF || item.ScaleIndex != uint.MaxValue || item.OuterIndex != uint.MaxValue)
                    throw new KilnException("TYPE");
                continue;
            }
            if (item.QuantScheme is not (1 or 2) || item.Rank == 0 || item.QuantAxis >= item.Rank || item.GroupSize == 0)
                throw new KilnException("TYPE");
            if (item.ScaleIndex == (uint)index || item.ScaleIndex >= (uint)tensors.Length)
                throw new KilnException("TYPE");
            var scale = tensors[item.ScaleIndex];
            if (scale.QuantScheme != 0)
                throw new KilnException("TYPE");
            var groups = item.Shape[item.QuantAxis] / item.GroupSize;
            if (item.Shape[item.QuantAxis] % item.GroupSize != 0)
                groups++;
            ulong others = 1;
            for (var dim = 0; dim < item.Rank; dim++)
            {
                if (dim != item.QuantAxis)
                    others = Limits.Mul(others, item.Shape[dim]);
            }
            if (ElementCount(scale.Rank, scale.Shape) != Limits.Mul(groups, others))
                throw new KilnException("TYPE");
            if (item.QuantScheme == 1)
            {
                if (item.OuterIndex != uint.MaxValue)
                    throw new KilnException("TYPE");
                continue;
            }
            if (item.OuterIndex == (uint)index || item.OuterIndex == item.ScaleIndex || item.OuterIndex >= (uint)tensors.Length)
                throw new KilnException("TYPE");
            var outer = tensors[item.OuterIndex];
            if (outer.QuantScheme != 0 || outer.Dtype != Dtypes["f32"] || ElementCount(outer.Rank, outer.Shape) != 1)
                throw new KilnException("TYPE");
        }
    }

    static void ReleaseShapes(Raw[] raw)
    {
        var pool = System.Buffers.ArrayPool<ulong>.Shared;
        foreach (var item in raw)
        {
            if (item?.Shape is not null)
                pool.Return(item.Shape);
        }
    }

    static void CheckPayload(Raw[] raw, ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;
        foreach (var item in raw)
        {
            var region = span.Slice((int)item.ByteOffset, (int)item.ByteLength);
            if (item.Dtype is 16 or 17 or 18)
            {
                var count = ElementCount(item.Rank, item.Shape);
                if ((count & 1) == 1 && region.Length > 0 && (region[^1] >> 4) != 0)
                    throw new KilnException("TYPE");
            }
            if (item.Dtype == Dtypes["bool"] && Above(region, 1))
                throw new KilnException("TYPE");
            item.Data = payload.Slice((int)item.ByteOffset, (int)item.ByteLength);
        }
        ulong cursor = 0;
        foreach (var item in raw)
        {
            if (Above(span.Slice((int)cursor, (int)(item.ByteOffset - cursor)), 0))
                throw new KilnException("TYPE");
            cursor = item.ByteOffset + item.ByteLength;
        }
        if (Above(span[(int)cursor..], 0))
            throw new KilnException("TYPE");
    }
}
