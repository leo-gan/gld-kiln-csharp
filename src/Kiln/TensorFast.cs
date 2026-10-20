using System.Buffers.Binary;

namespace Kiln;

static partial class TensorIo
{
    static bool TryWriteDirect(Writer writer, object? value)
    {
        if (value is not TensorValue[] tensors || tensors.Length != 1)
            return false;
        var tensor = tensors[0];
        if (!string.IsNullOrEmpty(tensor.Name) || !string.IsNullOrEmpty(tensor.Layout))
            return false;
        if (tensor.QuantScheme != 0 || tensor.BlockSize != 0 || tensor.GroupSize != 0 || tensor.QuantAxis != 0xFFFF)
            return false;
        if (tensor.ScaleIndex != uint.MaxValue || tensor.OuterIndex != uint.MaxValue)
            return false;
        var shape = tensor.Shape ?? [];
        if (shape.Length > 8)
            return false;
        var data = tensor.Data;
        var payloadAt = (int)Align64(HeaderSize + DescriptorSize);
        var block = payloadAt + data.Length;
        if (block < 0)
            throw new KilnException("LIMIT");
        var dest = writer.Reserve(block);
        dest[..payloadAt].Clear();
        WriteHeader(dest, 1, 0, (ulong)data.Length, DescriptorSize, FlagLe);
        var desc = dest[HeaderSize..];
        BinaryPrimitives.WriteUInt16LittleEndian(desc, CheckedU16(DtypeCode(tensor.Dtype)));
        desc[2] = (byte)shape.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(desc[4..], 0xFFFF);
        BinaryPrimitives.WriteUInt16LittleEndian(desc[8..], 0xFFFF);
        BinaryPrimitives.WriteUInt16LittleEndian(desc[18..], 0xFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(desc[24..], uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(desc[28..], uint.MaxValue);
        BinaryPrimitives.WriteUInt64LittleEndian(desc[40..], (ulong)data.Length);
        for (var dim = 0; dim < shape.Length; dim++)
            BinaryPrimitives.WriteUInt64LittleEndian(desc[(48 + dim * 8)..], shape[dim]);
        data.Span.CopyTo(dest[payloadAt..]);
        return true;
    }

    static readonly string[] LeFlags = ["le"];

    internal static bool TryFastDecode(ReadOnlyMemory<byte> view, int cap, bool standalone, out TensorDirectory directory, out int block)
    {
        directory = null!;
        block = 0;
        var span = view.Span;
        if (cap < 1 || span.Length < HeaderSize || span[0] != (byte)'K' || span[1] != (byte)'L' || span[2] != (byte)'N' || span[3] != (byte)'T' || span[4] != 2 || span[5] != FlagLe)
            return false;
        if (BinaryPrimitives.ReadUInt16LittleEndian(span[6..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(span[8..]) != 1 || BinaryPrimitives.ReadUInt32LittleEndian(span[12..]) != 0)
            return false;
        var payload = BinaryPrimitives.ReadUInt64LittleEndian(span[16..]);
        if (BinaryPrimitives.ReadUInt64LittleEndian(span[24..]) != DescriptorSize || Above(span.Slice(32, 32), 0))
            return false;
        const int payloadAt = 192;
        if (payload > (ulong)(int.MaxValue - payloadAt))
            return false;
        block = payloadAt + (int)payload;
        if (block > span.Length || (standalone && block != span.Length))
            return false;
        var desc = span.Slice(HeaderSize, DescriptorSize);
        if (desc[3] != 0 || Above(desc.Slice(112, 16), 0))
            return false;
        var dtype = BinaryPrimitives.ReadUInt16LittleEndian(desc);
        var rank = desc[2];
        if (rank > 8 || dtype == 0 || dtype == 7 || dtype >= 16)
            return false;
        if (BinaryPrimitives.ReadUInt16LittleEndian(desc[4..]) != 0xFFFF || BinaryPrimitives.ReadUInt16LittleEndian(desc[6..]) != 0)
            return false;
        if (BinaryPrimitives.ReadUInt16LittleEndian(desc[8..]) != 0xFFFF || BinaryPrimitives.ReadUInt16LittleEndian(desc[10..]) != 0)
            return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(desc[12..]) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(desc[16..]) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(desc[18..]) != 0xFFFF)
            return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(desc[20..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(desc[24..]) != uint.MaxValue || BinaryPrimitives.ReadUInt32LittleEndian(desc[28..]) != uint.MaxValue)
            return false;
        if (BinaryPrimitives.ReadUInt64LittleEndian(desc[32..]) != 0 || BinaryPrimitives.ReadUInt64LittleEndian(desc[40..]) != payload)
            return false;
        ulong count = 1;
        var shape = new ulong[rank];
        for (var dim = 0; dim < 8; dim++)
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(desc[(48 + dim * 8)..]);
            if (dim < rank)
            {
                shape[dim] = value;
                count = Limits.Mul(count, value);
            }
            else if (value != 0)
            {
                return false;
            }
        }
        if (rank == 0)
            count = 1;
        if (payload != ExpectedBytes(dtype, count))
            return false;
        directory = new TensorDirectory
        {
            Version = 2,
            Flags = LeFlags,
            Tensors =
            [
                new TensorValue
                {
                    Dtype = DtypeValue(dtype),
                    Shape = shape,
                    Data = view.Slice(payloadAt, (int)payload),
                    QuantAxis = 0xFFFF,
                    ScaleIndex = uint.MaxValue,
                    OuterIndex = uint.MaxValue,
                },
            ],
            BlockLength = block,
        };
        return true;
    }
}
