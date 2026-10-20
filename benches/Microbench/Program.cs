using System.Diagnostics;
using Kiln;

var point = Schema.Struct(
[
    new SchemaField("x", Schema.I32),
    new SchemaField("y", Schema.I32),
    new SchemaField("label", Schema.Str),
]);
var value = new Dictionary<string, object?> { ["x"] = 9, ["y"] = -4, ["label"] = "item" };
var data = new byte[1024 * 4];
for (var i = 0; i < 1024; i++)
    System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * 4), 1f);
var tensor = new TensorValue { Dtype = "f32", Shape = [1024], Data = data };
TensorValue[] batch = [tensor];
var writer = new Writer(256);
writer.Encode(point, value);

long Bench(Action action, int iterations, int warm)
{
    for (var i = 0; i < warm; i++)
        action();
    var clock = Stopwatch.StartNew();
    for (var i = 0; i < iterations; i++)
        action();
    clock.Stop();
    return (long)(clock.Elapsed.TotalNanoseconds / iterations);
}

var block = KilnCodec.EncodeTensors(batch);
Console.WriteLine($"struct_encode_ns {Bench(() => writer.Encode(point, value), 2000, 100)}");
Console.WriteLine($"struct_decode_ns {Bench(() => KilnCodec.Decode(point, writer.Written), 2000, 100)}");
Console.WriteLine($"struct_bytes {writer.End}");
Console.WriteLine($"tensor_encode_ns {Bench(() => KilnCodec.EncodeTensors(batch), 200, 20)}");
Console.WriteLine($"tensor_decode_ns {Bench(() => KilnCodec.DecodeTensors(block), 200, 20)}");
Console.WriteLine($"tensor_bytes {block.Length}");
