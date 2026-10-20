using System.Text;

namespace Kiln;

public static class TypeHash
{
    const ulong Offset = 14695981039346656037UL;
    const ulong Prime = 1099511628211UL;

    public static ulong Compute(Schema schema) => Hash(Offset, schema);

    public static ulong Mix(ulong hash, ReadOnlySpan<byte> data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            hash ^= data[i];
            hash *= Prime;
        }
        return hash * Prime;
    }

    static ulong Hash(ulong hash, Schema schema)
    {
        switch (schema.Kind)
        {
            case SchemaKind.List:
                hash = Mix(hash, "list<"u8);
                hash = Hash(hash, schema.Item!);
                return Mix(hash, ">"u8);
            case SchemaKind.Opt:
                hash = Mix(hash, "opt<"u8);
                hash = Hash(hash, schema.Item!);
                return Mix(hash, ">"u8);
            case SchemaKind.Dict:
                hash = Mix(hash, "dict<str,"u8);
                hash = Hash(hash, schema.Item!);
                return Mix(hash, ">"u8);
            case SchemaKind.Struct:
                hash = Mix(hash, "struct{"u8);
                foreach (var field in schema.Fields)
                {
                    hash = Mix(hash, Encoding.UTF8.GetBytes(field.Name));
                    hash = Mix(hash, ":"u8);
                    hash = Hash(hash, field.Type);
                    hash = Mix(hash, ","u8);
                }
                return Mix(hash, "}"u8);
            default:
                return Mix(hash, Token(schema.Kind));
        }
    }

    static ReadOnlySpan<byte> Token(SchemaKind kind) => kind switch
    {
        SchemaKind.Bool => "bool"u8,
        SchemaKind.I8 => "i8"u8,
        SchemaKind.I16 => "i16"u8,
        SchemaKind.I32 => "i32"u8,
        SchemaKind.I64 => "i64"u8,
        SchemaKind.Int => "int"u8,
        SchemaKind.U8 => "u8"u8,
        SchemaKind.U16 => "u16"u8,
        SchemaKind.U32 => "u32"u8,
        SchemaKind.U64 => "u64"u8,
        SchemaKind.Uint => "uint"u8,
        SchemaKind.F32 => "f32"u8,
        SchemaKind.F64 => "f64"u8,
        SchemaKind.Str => "str"u8,
        SchemaKind.Bytes => "bytes"u8,
        SchemaKind.Tensors => "tensors"u8,
        _ => throw new KilnException("TYPE"),
    };
}
