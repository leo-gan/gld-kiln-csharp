namespace Kiln;

public sealed class TensorValue
{
    public string? Name { get; set; }
    public object? Dtype { get; set; }
    public ulong[] Shape { get; set; } = [];
    public ReadOnlyMemory<byte> Data { get; set; }
    public int QuantScheme { get; set; }
    public int QuantAxis { get; set; } = 0xFFFF;
    public uint GroupSize { get; set; }
    public uint ScaleIndex { get; set; } = uint.MaxValue;
    public uint OuterIndex { get; set; } = uint.MaxValue;
    public string? Layout { get; set; }
    public uint BlockSize { get; set; }
}

public sealed class TensorDirectory
{
    public int Version { get; set; } = 2;
    public string[] Flags { get; set; } = ["le"];
    public TensorValue[] Tensors { get; set; } = [];
    public int BlockLength { get; set; }
}
