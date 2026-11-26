using System.Collections.Generic;
using Xunit;

namespace Kiln.Tests;

public class CursorTests
{
    [Fact]
    public void WriterAndReaderMatchSchemaWireFormat()
    {
        var schema = Schema.Struct(new[]
        {
            new SchemaField("b", Schema.Bool),
            new SchemaField("i32", Schema.I32),
            new SchemaField("i64", Schema.I64),
            new SchemaField("f64", Schema.F64),
            new SchemaField("s", Schema.Str),
            new SchemaField("u", Schema.Str),
            new SchemaField("vals", Schema.List(Schema.F64)),
        });

        var vals = new double[] { 1.5, -0.0, 42.25 };
        var ascii = "0123456789abcdef-ascii";
        var utf8 = "héllo-🚀-kiln";

        var w = new Writer(128);
        w.Reset();
        w.WriteBool(true);
        w.WriteI32(-7);
        w.WriteI64(9_000_000_000L);
        w.WriteF64(-12.5);
        w.WriteString(ascii);
        w.WriteString(utf8);
        w.WriteF64s(vals);
        var wire = w.ToArray();

        var expected = KilnCodec.Encode(schema, new Dictionary<string, object?>
        {
            ["b"] = true,
            ["i32"] = -7,
            ["i64"] = 9_000_000_000L,
            ["f64"] = -12.5,
            ["s"] = ascii,
            ["u"] = utf8,
            ["vals"] = new List<object?> { 1.5, -0.0, 42.25 },
        });
        Assert.Equal(expected, wire);

        var r = new Reader(wire);
        Assert.True(r.ReadBool());
        Assert.Equal(-7, r.ReadI32());
        Assert.Equal(9_000_000_000L, r.ReadI64());
        Assert.Equal(-12.5, r.ReadF64());
        Assert.Equal(ascii, r.ReadString());
        Assert.Equal(utf8, r.ReadString());
        var back = r.ReadF64List();
        r.Finish();
        Assert.Equal(3, back.Count);
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(back[1]));

        var badUtf8 = new byte[] { 2, 0, 0, 0, 0xFF, 0xFE };
        var ex = Assert.Throws<KilnException>(() =>
        {
            var rb = new Reader(badUtf8);
            rb.ReadString();
        });
        Assert.Equal("UTF8", ex.Code);

        var trailing = new byte[] { 1, 0 };
        var ex2 = Assert.Throws<KilnException>(() =>
        {
            var rt = new Reader(trailing);
            rt.ReadBool();
            rt.Finish();
        });
        Assert.Equal("TRAILING", ex2.Code);
    }
}
