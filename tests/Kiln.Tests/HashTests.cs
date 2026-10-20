using System.Text.Json;
using Xunit;

namespace Kiln.Tests;

public class HashTests
{
    [Fact]
    public void KnownStructHashesMatchTheReference()
    {
        var point = Schema.Struct([new SchemaField("x", Schema.I32), new SchemaField("y", Schema.I32)]);
        var message = Schema.Struct(
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
        var frame = SchemaJson.FromCase(JsonDocument.Parse("\"frame\"").RootElement);
        Assert.Equal(16603294324633093935UL, TypeHash.Compute(point));
        Assert.Equal(15448582873398700035UL, TypeHash.Compute(message));
        Assert.Equal(16540223653110812034UL, TypeHash.Compute(frame));
    }

    [Fact]
    public void JsonSchemaMarksMissingRequiredAsOptional()
    {
        var (title, root) = SchemaJson.FromJson(File.ReadAllText(V2CasesTests.TestdataPath("schema/point.json")));
        Assert.Equal("Point", title);
        Assert.Equal(SchemaKind.Struct, root.Kind);
        Assert.Equal("x", root.Fields[0].Name);
        Assert.Equal(SchemaKind.I32, root.Fields[0].Type.Kind);
        Assert.Equal(SchemaKind.Opt, root.Fields[2].Type.Kind);
        Assert.Equal(SchemaKind.Str, root.Fields[2].Type.Item!.Kind);
        var message = SchemaJson.FromJson(File.ReadAllText(V2CasesTests.TestdataPath("schema/message.json"))).Root;
        Assert.Equal("Meta", message.Fields[8].Type.StructName);
        Assert.Equal(2, message.Fields[8].Type.Fields.Length);
    }
}
