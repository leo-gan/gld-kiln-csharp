# gld-kiln-csharp

A from-scratch Kiln codec for C#. The runtime and the code generator are C#. They do not wrap, link, or vendor another language's Kiln library.

`testdata/reference/kiln_ref.py` is a test oracle for Frozen values. It is not used at run time.

This repository is a standalone library.

Documentation: [Why Kiln](https://leo-gan.github.io/gld-kiln-csharp/why-kiln/),
[Instructions](https://leo-gan.github.io/gld-kiln-csharp/instructions/),
[Examples](https://leo-gan.github.io/gld-kiln-csharp/examples/),
[Techniques](https://leo-gan.github.io/gld-kiln-csharp/techniques/),
[Test data](https://leo-gan.github.io/gld-kiln-csharp/test-data/).

## Install

```bash
dotnet add package Gld.Kiln
```

The import name is `Kiln`. The generator command is `gld-kilngen-csharp`.

```csharp
using Kiln;

var point = Schema.Struct([new SchemaField("x", Schema.I32), new SchemaField("y", Schema.I32)]);
var writer = new Writer();
var raw = writer.Encode(point, new Dictionary<string, object?> { ["x"] = 9, ["y"] = -4 });
Console.WriteLine(KilnCodec.Decode(point, raw));
```

## License

MIT. Copyright (c) 2026 Leonid Ganeline.
