using System.Text.Json;

namespace Kiln;

public static class SchemaJson
{
    static readonly Dictionary<string, SchemaKind> Integers = new()
    {
        ["int8"] = SchemaKind.I8,
        ["int16"] = SchemaKind.I16,
        ["int32"] = SchemaKind.I32,
        ["int64"] = SchemaKind.I64,
        ["uint8"] = SchemaKind.U8,
        ["uint16"] = SchemaKind.U16,
        ["uint32"] = SchemaKind.U32,
        ["uint64"] = SchemaKind.U64,
    };

    public static (string Title, Schema Root) FromJson(string text)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw new KilnException("SCHEMA");
        }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new KilnException("SCHEMA");
            var root = doc.RootElement;
            var defs = root.TryGetProperty("$defs", out var defsEl) && defsEl.ValueKind == JsonValueKind.Object
                ? defsEl
                : default;
            var cache = new Dictionary<string, Schema>();
            var building = new HashSet<string>();
            var title = root.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
                ? titleEl.GetString() ?? "Root"
                : "Root";
            return (title, Convert(root, title));

            Schema Convert(JsonElement node, string label)
            {
                if (node.ValueKind != JsonValueKind.Object)
                    throw new KilnException("SCHEMA");
                if (node.TryGetProperty("$ref", out var refEl))
                {
                    var reference = refEl.GetString() ?? "";
                    const string prefix = "#/$defs/";
                    if (!reference.StartsWith(prefix, StringComparison.Ordinal))
                        throw new KilnException("SCHEMA");
                    return Resolve(reference[prefix.Length..]);
                }
                var kind = node.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                if (kind == "boolean")
                    return Schema.Bool;
                if (kind == "string")
                    return node.TryGetProperty("format", out var format) && format.GetString() == "binary"
                        ? Schema.Bytes
                        : Schema.Str;
                if (kind == "integer")
                {
                    var format = node.TryGetProperty("format", out var fmt) ? fmt.GetString() ?? "" : "";
                    var scalar = Integers.TryGetValue(format, out var mapped) ? mapped : SchemaKind.I64;
                    return Schema.Scalar(NameOf(scalar));
                }
                if (kind == "number")
                    return node.TryGetProperty("format", out var fmt) && fmt.GetString() == "float" ? Schema.F32 : Schema.F64;
                if (kind == "array")
                {
                    if (!node.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object)
                        throw new KilnException("SCHEMA");
                    return Schema.List(Convert(items, label + "Item"));
                }
                if (kind == "object")
                {
                    var props = node.TryGetProperty("properties", out var propEl) ? propEl : default;
                    var hasProps = props.ValueKind == JsonValueKind.Object && HasAny(props);
                    if (node.TryGetProperty("additionalProperties", out var extra) && extra.ValueKind == JsonValueKind.Object && !hasProps)
                        return Schema.Dict(Convert(extra, label + "Value"));
                    return ObjectSchema(node, label);
                }
                throw new KilnException("SCHEMA");
            }

            Schema Resolve(string name)
            {
                if (cache.TryGetValue(name, out var ready))
                    return ready;
                if (!building.Add(name) || defs.ValueKind != JsonValueKind.Object || !defs.TryGetProperty(name, out var body))
                    throw new KilnException("SCHEMA");
                var built = Convert(body, name);
                building.Remove(name);
                cache[name] = built;
                return built;
            }

            Schema ObjectSchema(JsonElement node, string label)
            {
                var props = node.TryGetProperty("properties", out var propEl) && propEl.ValueKind == JsonValueKind.Object
                    ? propEl
                    : default;
                HashSet<string>? required = null;
                if (node.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
                {
                    required = [];
                    foreach (var item in req.EnumerateArray())
                    {
                        if (item.GetString() is { } text)
                            required.Add(text);
                    }
                }
                var fields = new List<SchemaField>();
                if (props.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in props.EnumerateObject())
                    {
                        var field = Convert(prop.Value, label + "_" + prop.Name);
                        if (required is not null && !required.Contains(prop.Name))
                            field = Schema.Opt(field);
                        fields.Add(new SchemaField(prop.Name, field));
                    }
                }
                return Schema.Struct(fields.ToArray(), label);
            }
        }
    }

    public static Schema FromCase(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.String)
        {
            return schema.GetString() switch
            {
                "tensor-v2" => Schema.Tensors,
                "frame" => Schema.Struct([new SchemaField("id", Schema.I64), new SchemaField("batch", Schema.Tensors)]),
                _ => throw new KilnException("SCHEMA"),
            };
        }
        if (schema.ValueKind == JsonValueKind.Object)
        {
            var kind = schema.GetProperty("type").GetString();
            if (kind == "list")
                return Schema.List(Schema.Parse(schema.GetProperty("element").GetString()!));
            if (kind == "struct")
            {
                var fields = new List<SchemaField>();
                foreach (var item in schema.GetProperty("fields").EnumerateArray())
                {
                    var text = item.GetString() ?? throw new KilnException("SCHEMA");
                    var colon = text.IndexOf(':');
                    if (colon <= 0)
                        throw new KilnException("SCHEMA");
                    fields.Add(new SchemaField(text[..colon], Schema.Parse(text[(colon + 1)..])));
                }
                return Schema.Struct(fields.ToArray());
            }
        }
        throw new KilnException("SCHEMA");
    }

    static bool HasAny(JsonElement obj)
    {
        foreach (var _ in obj.EnumerateObject())
            return true;
        return false;
    }

    static string NameOf(SchemaKind kind) => kind switch
    {
        SchemaKind.I8 => "i8",
        SchemaKind.I16 => "i16",
        SchemaKind.I32 => "i32",
        SchemaKind.I64 => "i64",
        SchemaKind.U8 => "u8",
        SchemaKind.U16 => "u16",
        SchemaKind.U32 => "u32",
        SchemaKind.U64 => "u64",
        _ => throw new KilnException("SCHEMA"),
    };
}
