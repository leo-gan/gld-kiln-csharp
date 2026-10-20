using System.Buffers.Binary;
using Xunit;

namespace Kiln.Tests;

public class FrozenTests
{
    static readonly Schema Point = Schema.Struct([new SchemaField("x", Schema.I32), new SchemaField("y", Schema.I32)]);

    static readonly Schema Message = Schema.Struct(
    [
        new SchemaField("f_bool", Schema.Bool),
        new SchemaField("f_int32", Schema.I32),
        new SchemaField("f_int64", Schema.I64),
        new SchemaField("f_float64", Schema.F64),
        new SchemaField("f_string", Schema.Str),
        new SchemaField("f_bool_2", Schema.Bool),
        new SchemaField("f_int32_2", Schema.I32),
        new SchemaField("f_string_2", Schema.Str),
    ]);

    [Fact]
    public void ReferencePointAndMessageRoundTrip()
    {
        var point = Map(("x", 9), ("y", -4));
        var message = Map(
            ("f_bool", true), ("f_int32", -2), ("f_int64", 99L), ("f_float64", 1.5),
            ("f_string", "hello"), ("f_bool_2", false), ("f_int32_2", 7), ("f_string_2", "world"));
        AssertBytes("09000000fcffffff", KilnCodec.Encode(Point, point));
        Assert.Equal(9, ((Dictionary<string, object?>)KilnCodec.Decode(Point, Hex("09000000fcffffff"))!)["x"]);
        AssertBytes("09000000fcffffff", KilnCodec.Encode(Point, KilnCodec.Decode(Point, Hex("09000000fcffffff"))));
        AssertBytes("4b4c4e30000000002fc7036138c06ae609000000fcffffff", KilnCodec.EncodeFramed(Point, point));
        Assert.Equal(-4, ((Dictionary<string, object?>)KilnCodec.DecodeFramed(Point, Hex("4b4c4e30000000002fc7036138c06ae609000000fcffffff"))!)["y"]);
        var messageRaw = "01feffffff6300000000000000000000000000f83f0500000068656c6c6f000700000005000000776f726c64";
        var messageFramed = "4b4c4e300000000003e81cf4456464d601feffffff6300000000000000000000000000f83f0500000068656c6c6f000700000005000000776f726c64";
        AssertBytes(messageRaw, KilnCodec.Encode(Message, message));
        AssertBytes(messageRaw, KilnCodec.Encode(Message, KilnCodec.Decode(Message, Hex(messageRaw))));
        AssertBytes(messageFramed, KilnCodec.EncodeFramed(Message, message));
        Assert.Equal("world", ((Dictionary<string, object?>)KilnCodec.DecodeFramed(Message, Hex(messageFramed))!)["f_string_2"]);
    }

    [Fact]
    public void NegativeZeroAndNanPayload()
    {
        var schema = Schema.List(Schema.F64);
        var raw = Hex("020000000000000000000080010000000000f87f");
        var got = (double[])KilnCodec.Decode(schema, raw)!;
        Assert.True(double.IsNegative(got[0]));
        Assert.True(double.IsNaN(got[1]));
        Assert.Equal(0x7FF8000000000001UL, BitConverter.DoubleToUInt64Bits(got[1]));
        AssertBytes("020000000000000000000080010000000000f87f", KilnCodec.Encode(schema, got));
    }

    [Fact]
    public void DictKeyOrderAndOpt()
    {
        var schema = Schema.Dict(Schema.I32);
        var raw = KilnCodec.Encode(schema, Map(("b", 2), ("a", 1)));
        var got = (Dictionary<string, object?>)KilnCodec.Decode(schema, raw)!;
        Assert.Equal(new[] { "a", "b" }, got.Keys.ToArray());
        Assert.Equal(1, got["a"]);
        Assert.Equal(2, got["b"]);
        var opt = Schema.Opt(Schema.Str);
        Assert.Null(KilnCodec.Decode(opt, KilnCodec.Encode(opt, null)));
        Assert.Equal("z", KilnCodec.Decode(opt, KilnCodec.Encode(opt, "z")));
    }

    [Fact]
    public void DirectTensorEncodeMatchesDirectory()
    {
        var raw = KilnCodec.EncodeTensors(new[]
        {
            new TensorValue { Dtype = "f32", Shape = [2], Data = Hex("0000803f000000c0") },
        });
        var directory = KilnCodec.DecodeTensors(raw);
        Assert.Equal(raw, KilnCodec.EncodeTensors(directory));
        Assert.Equal("0000803f000000c0", Convert.ToHexString(directory.Tensors[0].Data.Span).ToLowerInvariant());
    }

    [Fact]
    public void TrustedSkipsUtf8ButNotALengthPastTheBuffer()
    {
        var bad = Hex("01000000ff");
        Assert.Equal("UTF8", Assert.Throws<KilnException>(() => KilnCodec.Decode(Schema.Str, bad)).Code);
        Assert.Equal("\uFFFD", KilnCodec.Decode(Schema.Str, bad, trusted: true));
        var shortBytes = Hex("32000000");
        Assert.Equal("EOF", Assert.Throws<KilnException>(() => KilnCodec.Decode(Schema.Str, shortBytes, trusted: true)).Code);
    }

    [Fact]
    public void HostileListDoesNotAllocate()
    {
        var schema = Schema.List(Schema.Struct([new SchemaField("a", Schema.I64)]));
        var error = Assert.Throws<KilnException>(() => KilnCodec.Decode(schema, Hex("00000001")));
        Assert.Equal("EOF", error.Code);
        var huge = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(huge, 1_000_000_000);
        error = Assert.Throws<KilnException>(() => KilnCodec.Decode(schema, huge, cap: int.MaxValue));
        Assert.Equal("EOF", error.Code);
    }

    static Dictionary<string, object?> Map(params (string Name, object? Value)[] fields)
    {
        var map = new Dictionary<string, object?>();
        foreach (var field in fields)
            map[field.Name] = field.Value;
        return map;
    }

    static byte[] Hex(string text) => Convert.FromHexString(text);

    static void AssertBytes(string hex, byte[] raw) => Assert.Equal(hex, Convert.ToHexString(raw).ToLowerInvariant());
}
