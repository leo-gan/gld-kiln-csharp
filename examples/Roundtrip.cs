using Kiln;

var point = Schema.Struct([new SchemaField("x", Schema.I32), new SchemaField("y", Schema.I32)]);
var writer = new Writer(64);
var raw = writer.Encode(point, new Dictionary<string, object?> { ["x"] = 9, ["y"] = -4 });
Console.WriteLine($"{KilnCodec.Decode(point, raw)} {raw.Length}");
