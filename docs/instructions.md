# Instructions

.NET 10 or newer.

```bash
dotnet add package Gld.Kiln
```

From a git checkout:

```bash
dotnet test
dotnet run --project src/Gld.Kilngen -- testdata/schema/point.json -o Point.cs
```

The built generator command is `gld-kilngen-csharp`. The import name is `Kiln`. `KilnCodec.Encode` and `KilnCodec.Decode` take a schema and a value. `Writer` reuses one buffer across calls. `SchemaJson.FromJson` reads the JSON Schema subset: booleans, integers with an optional `format`, numbers, strings, binary strings, arrays, objects, `$ref` of the form `#/$defs/Name`, and `additionalProperties` as a dict. A property omitted from `required` is optional. `gld-kilngen-csharp` writes C# classes with `Encode`, `Decode`, and `EncodedSize`. A `tensors` field is not produced from that JSON subset. Build a tensor directory with `KilnCodec.EncodeTensors` and read it with `KilnCodec.DecodeTensors`.
