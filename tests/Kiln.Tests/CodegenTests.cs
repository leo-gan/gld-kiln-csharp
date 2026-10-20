using Xunit;

namespace Kiln.Tests;

public class CodegenTests
{
    [Fact]
    public void GeneratorIsStableAndStructsRoundTrip()
    {
        var root = RepoRoot();
        var tool = Path.Combine(root, "src", "Gld.Kilngen", "Gld.Kilngen.csproj");
        var point = Path.Combine(root, "testdata", "schema", "point.json");
        var message = Path.Combine(root, "testdata", "schema", "message.json");
        var dir = Directory.CreateTempSubdirectory("kiln-csharp-");
        try
        {
            var pointCs = Path.Combine(dir.FullName, "Point.cs");
            var pointAgain = Path.Combine(dir.FullName, "Point2.cs");
            var messageCs = Path.Combine(dir.FullName, "Message.cs");
            var messageAgain = Path.Combine(dir.FullName, "Message2.cs");
            Run(tool, point, pointCs);
            Run(tool, point, pointAgain);
            Run(tool, message, messageCs);
            Run(tool, message, messageAgain);
            Assert.Equal(File.ReadAllText(pointCs), File.ReadAllText(pointAgain));
            Assert.Equal(File.ReadAllText(messageCs), File.ReadAllText(messageAgain));
            File.Delete(pointAgain);
            File.Delete(messageAgain);
            File.WriteAllText(Path.Combine(dir.FullName, "gen.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="KILN" />
                  </ItemGroup>
                </Project>
                """.Replace("KILN", Path.Combine(root, "src", "Kiln", "Kiln.csproj")));
            File.WriteAllText(Path.Combine(dir.FullName, "Program.cs"), """
                using Kiln.Generated;
                var point = new Point { x = 3, y = -1, label = "n" };
                var again = Point.Decode(point.Encode());
                if (again.x != 3 || again.y != -1 || again.label != "n") return 2;
                if (Point.EncodedSize(point.ToValue()) != point.Encode().Length) return 3;
                if (new Point { x = 1, y = 2 }.Encode() is var bare && Point.Decode(bare).label is not null) return 4;
                var message = new Message
                {
                    f_bool = true, f_int32 = -2, f_int64 = 99, f_float64 = 1.5, f_string = "hello",
                    f_bool_2 = false, f_int32_2 = 7, f_string_2 = "world", meta = new Meta { region = "eu", flag = true },
                };
                var back = Message.Decode(message.Encode());
                if (back.f_string != "hello" || back.meta.region != "eu" || !back.meta.flag || back.f_float64 != 1.5) return 5;
                if (Message.EncodedSize(message.ToValue()) != message.Encode().Length) return 6;
                return 0;
                """);
            var run = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { "run", "--project", Path.Combine(dir.FullName, "gen.csproj") },
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            })!;
            var output = run.StandardOutput.ReadToEnd() + run.StandardError.ReadToEnd();
            run.WaitForExit();
            Assert.True(run.ExitCode == 0, output);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    static void Run(string project, string schema, string output)
    {
        var run = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "run", "--project", project, "--", schema, "-o", output },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        })!;
        var text = run.StandardOutput.ReadToEnd() + run.StandardError.ReadToEnd();
        run.WaitForExit();
        Assert.True(run.ExitCode == 0, text);
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "testdata", "schema", "point.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }
}
