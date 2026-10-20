using System.Collections;
using System.Text.Json;
using Xunit;

namespace Kiln.Tests;

public class WireTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(V2CasesTests.TestdataPath("kiln-v2-cases.json")));
        foreach (var item in doc.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void V2Case(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement;
        var raw = Convert.FromHexString(item.GetProperty("input").GetString()!);
        var schema = SchemaJson.FromCase(item.GetProperty("schema"));
        var framed = item.GetProperty("schema").ValueKind == JsonValueKind.String && item.GetProperty("schema").GetString() == "frame";
        if (item.GetProperty("expect").GetString() == "reject")
        {
            var error = Assert.Throws<KilnException>(() => _ = framed ? KilnCodec.DecodeFramed(schema, raw) : KilnCodec.Decode(schema, raw));
            Assert.Equal(item.GetProperty("error").GetString(), error.Code);
            return;
        }
        var value = framed ? KilnCodec.DecodeFramed(schema, raw) : KilnCodec.Decode(schema, raw);
        if (item.TryGetProperty("decoded", out var decoded))
        {
            if (framed)
            {
                Assert.Equal(decoded.GetProperty("hash").GetUInt64(), TypeHash.Compute(schema));
                var map = (Dictionary<string, object?>)Project(value)!;
                Assert.True(Same(map["id"], decoded.GetProperty("id")));
                Assert.True(Same(map["batch"], decoded.GetProperty("batch")));
            }
            else
            {
                Assert.True(Same(Project(value), decoded), item.GetProperty("id").GetString());
            }
        }
        var again = framed ? KilnCodec.EncodeFramed(schema, value) : KilnCodec.Encode(schema, value);
        Assert.Equal(raw, again);
    }

    static object? Project(object? value)
    {
        if (value is TensorDirectory directory)
        {
            var tensors = new Dictionary<string, object?>[directory.Tensors.Length];
            for (var i = 0; i < directory.Tensors.Length; i++)
            {
                var tensor = directory.Tensors[i];
                tensors[i] = new Dictionary<string, object?>
                {
                    ["name"] = tensor.Name,
                    ["dtype"] = tensor.Dtype,
                    ["shape"] = tensor.Shape,
                    ["quant_scheme"] = tensor.QuantScheme,
                    ["data_hex"] = Convert.ToHexString(tensor.Data.Span).ToLowerInvariant(),
                };
            }
            return new Dictionary<string, object?>
            {
                ["version"] = directory.Version,
                ["flags"] = directory.Flags,
                ["tensors"] = tensors,
                ["block_length"] = directory.BlockLength,
            };
        }
        if (value is Dictionary<string, object?> map)
        {
            var copy = new Dictionary<string, object?>(map.Count);
            foreach (var pair in map)
                copy[pair.Key] = Project(pair.Value);
            return copy;
        }
        if (value is IList list && value is not byte[])
        {
            var copy = new object?[list.Count];
            for (var i = 0; i < list.Count; i++)
                copy[i] = Project(list[i]);
            return copy;
        }
        return value;
    }

    static bool Same(object? got, JsonElement want) => want.ValueKind switch
    {
        JsonValueKind.Null => got is null,
        JsonValueKind.True or JsonValueKind.False => got is bool bit && bit == want.GetBoolean(),
        JsonValueKind.String => got is string text && text == want.GetString(),
        JsonValueKind.Number => NumberSame(got, want),
        JsonValueKind.Array => ArraySame(got, want),
        JsonValueKind.Object => ObjectSame(got, want),
        _ => false,
    };

    static bool ArraySame(object? got, JsonElement want)
    {
        if (got is not IList list || list.Count != want.GetArrayLength())
            return false;
        var index = 0;
        foreach (var item in want.EnumerateArray())
        {
            if (!Same(list[index++], item))
                return false;
        }
        return true;
    }

    static bool ObjectSame(object? got, JsonElement want)
    {
        if (got is not IReadOnlyDictionary<string, object?> map)
            return false;
        foreach (var prop in want.EnumerateObject())
        {
            if (!map.TryGetValue(prop.Name, out var child) || !Same(child, prop.Value))
                return false;
        }
        return true;
    }

    static bool NumberSame(object? got, JsonElement want)
    {
        switch (got)
        {
            case double number:
                var wide = want.GetDouble();
                return double.IsNaN(number) && double.IsNaN(wide) || number == wide;
            case float number:
                var single = want.GetDouble();
                return float.IsNaN(number) && double.IsNaN(single) || number == single;
            case ulong number:
                return want.TryGetUInt64(out var unsigned) && unsigned == number;
            case long number:
                return want.TryGetInt64(out var signed) && signed == number;
            case int number:
                return want.TryGetInt64(out var integer) && integer == number;
            case uint number:
                return want.TryGetUInt64(out var uns) && uns == number;
            default:
                return false;
        }
    }
}
