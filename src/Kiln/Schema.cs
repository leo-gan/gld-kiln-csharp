namespace Kiln;

public enum SchemaKind : byte
{
    Bool, I8, I16, I32, I64, Int, U8, U16, U32, U64, Uint, F32, F64,
    Str, Bytes, Tensors, List, Opt, Dict, Struct,
}

public readonly record struct SchemaField(string Name, Schema Type);

public sealed class Schema
{
    public SchemaKind Kind { get; }
    public Schema? Item { get; }
    public string? StructName { get; }
    public SchemaField[] Fields { get; }

    Schema(SchemaKind kind, Schema? item, SchemaField[] fields, string? structName)
    {
        Kind = kind;
        Item = item;
        Fields = fields;
        StructName = structName;
    }

    public static readonly Schema Bool = new(SchemaKind.Bool, null, [], null);
    public static readonly Schema I8 = new(SchemaKind.I8, null, [], null);
    public static readonly Schema I16 = new(SchemaKind.I16, null, [], null);
    public static readonly Schema I32 = new(SchemaKind.I32, null, [], null);
    public static readonly Schema I64 = new(SchemaKind.I64, null, [], null);
    public static readonly Schema Int = new(SchemaKind.Int, null, [], null);
    public static readonly Schema U8 = new(SchemaKind.U8, null, [], null);
    public static readonly Schema U16 = new(SchemaKind.U16, null, [], null);
    public static readonly Schema U32 = new(SchemaKind.U32, null, [], null);
    public static readonly Schema U64 = new(SchemaKind.U64, null, [], null);
    public static readonly Schema Uint = new(SchemaKind.Uint, null, [], null);
    public static readonly Schema F32 = new(SchemaKind.F32, null, [], null);
    public static readonly Schema F64 = new(SchemaKind.F64, null, [], null);
    public static readonly Schema Str = new(SchemaKind.Str, null, [], null);
    public static readonly Schema Bytes = new(SchemaKind.Bytes, null, [], null);
    public static readonly Schema Tensors = new(SchemaKind.Tensors, null, [], null);

    public static Schema Scalar(string name) => name switch
    {
        "bool" => Bool,
        "i8" => I8,
        "i16" => I16,
        "i32" => I32,
        "i64" => I64,
        "int" => Int,
        "u8" => U8,
        "u16" => U16,
        "u32" => U32,
        "u64" => U64,
        "uint" => Uint,
        "f32" => F32,
        "f64" => F64,
        "str" => Str,
        "bytes" => Bytes,
        "tensors" => Tensors,
        _ => throw new KilnException("SCHEMA"),
    };

    public static Schema List(Schema item) => new(SchemaKind.List, item, [], null);
    public static Schema Opt(Schema item) => new(SchemaKind.Opt, item, [], null);
    public static Schema Dict(Schema item) => new(SchemaKind.Dict, item, [], null);
    public static Schema Struct(SchemaField[] fields, string? name = null) => new(SchemaKind.Struct, null, fields, name);

    public static Schema Parse(string text)
    {
        var parser = new TypeParser(text.Trim());
        var schema = parser.ParseType();
        if (!parser.Done)
            throw new KilnException("SCHEMA");
        return schema;
    }

    sealed class TypeParser
    {
        readonly string _text;
        int _index;
        public TypeParser(string text) => _text = text;
        public bool Done => _index >= _text.Length;

        public Schema ParseType()
        {
            if (Match("struct{"))
            {
                var fields = new List<SchemaField>();
                if (!Consume('}'))
                {
                    while (true)
                    {
                        var name = TakeUntil(':');
                        Expect(':');
                        fields.Add(new SchemaField(name, ParseType()));
                        if (Consume('}'))
                            break;
                        Expect(',');
                    }
                }
                return Struct(fields.ToArray());
            }
            if (Match("list<"))
            {
                var item = ParseType();
                Expect('>');
                return List(item);
            }
            if (Match("opt<"))
            {
                var item = ParseType();
                Expect('>');
                return Opt(item);
            }
            if (Match("dict<str,"))
            {
                var item = ParseType();
                Expect('>');
                return Dict(item);
            }
            var start = _index;
            while (!Done && (char.IsAsciiLetterOrDigit(_text[_index]) || _text[_index] == '_'))
                _index++;
            if (_index == start)
                throw new KilnException("SCHEMA");
            return Scalar(_text[start.._index]);
        }

        bool Match(string token)
        {
            if (!_text.AsSpan(_index).StartsWith(token))
                return false;
            _index += token.Length;
            return true;
        }

        string TakeUntil(char stop)
        {
            var start = _index;
            while (!Done && _text[_index] != stop)
                _index++;
            if (_index == start)
                throw new KilnException("SCHEMA");
            return _text[start.._index];
        }

        void Expect(char value)
        {
            if (!Consume(value))
                throw new KilnException("SCHEMA");
        }

        bool Consume(char value)
        {
            if (Done || _text[_index] != value)
                return false;
            _index++;
            return true;
        }
    }
}
