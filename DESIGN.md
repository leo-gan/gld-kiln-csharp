# Kiln for C#

Kiln is a positional little-endian codec. The caller passes the schema. `Writer` owns a buffer and reuses it. `Decode` is checked. A tensor payload is a `ReadOnlyMemory<byte>` into the input.

The normative text is [Kiln wire v2](https://leo-gan.github.io/gld-kiln/spec/). This repo does not copy that text into a Spec page.

| Topic | Choice |
| --- | --- |
| Wire | Frozen layout and the version-2 tensor directory |
| API | `KilnCodec.Encode` / `Decode`, `Writer`, `EncodeTensors` / `DecodeTensors`, `SchemaJson.FromJson`, `gld-kilngen-csharp` |
| Lengths | `u32` byte counts. Dict keys are sorted. Default cap 16777216 |
| Errors | `KilnException.Code` is FRAME, LIMIT, EOF, TYPE, UTF8, TRAILING, or SCHEMA |
| Package | `Gld.Kiln`, import `Kiln`, .NET 10, MIT |
