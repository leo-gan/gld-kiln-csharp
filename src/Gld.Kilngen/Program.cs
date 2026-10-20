namespace Gld.Kilngen;

static class Program
{
    static int Main(string[] args)
    {
        string? schema = null;
        string? output = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "-o" or "--output")
            {
                if (i + 1 >= args.Length)
                    return Fail("missing output path");
                output = args[++i];
            }
            else if (args[i] is "-h" or "--help")
            {
                Console.WriteLine("gld-kilngen-csharp <schema.json> -o <output.cs>");
                return 0;
            }
            else if (schema is null)
            {
                schema = args[i];
            }
            else
            {
                return Fail("unexpected argument " + args[i]);
            }
        }
        if (schema is null || output is null)
            return Fail("usage: gld-kilngen-csharp <schema.json> -o <output.cs>");
        try
        {
            var text = Emitter.Emit(File.ReadAllText(schema));
            var directory = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(output, text);
            return 0;
        }
        catch (Exception ex) when (ex is Kiln.KilnException or IOException)
        {
            return Fail(ex.Message);
        }
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
