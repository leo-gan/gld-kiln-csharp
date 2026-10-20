# Examples

`Point` has two `i32` fields. Kiln writes them in that order. Eight little-endian bytes, and no names.

```csharp
using Kiln;

var point = Schema.Struct([new SchemaField("x", Schema.I32), new SchemaField("y", Schema.I32)]);
var writer = new Writer(64);
var raw = writer.Encode(point, new Dictionary<string, object?> { ["x"] = 9, ["y"] = -4 });
Console.WriteLine($"{KilnCodec.Decode(point, raw)} {raw.Length}");
```

The program prints a map of `x = 9`, `y = -4`, and the length `8`.

A tensor directory keeps the numeric payload as the array. The view's `Data` field is a slice of the input buffer.

```csharp
using Kiln;

var block = KilnCodec.EncodeTensors(new[]
{
    new TensorValue
    {
        Dtype = "f32",
        Shape = [2],
        Data = new byte[] { 0x00, 0x00, 0x80, 0x3f, 0x00, 0x00, 0x00, 0xc0 },
    },
});
var directory = KilnCodec.DecodeTensors(block);
var payload = directory.Tensors[0].Data;
Console.WriteLine($"{directory.BlockLength} {Convert.ToHexString(payload.Span).ToLowerInvariant()}");
```
