using System.Text.Json;
using Xunit;

namespace Kiln.Tests;

public class V2CasesTests
{
    [Fact]
    public void LoadsKilnV2Cases()
    {
        var path = TestdataPath("kiln-v2-cases.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var cases = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.GetProperty("cases");
        Assert.Equal(46, cases.GetArrayLength());
    }

    internal static string TestdataPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "testdata", relative);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
