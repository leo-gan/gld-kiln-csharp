using System.Text;

namespace Kiln;

static partial class TensorIo
{
    public const int HeaderSize = 64;
    public const int DescriptorSize = 128;
    const byte FlagName = 0x01;
    const byte FlagLe = 0x02;
    const int OpaqueMin = 4096;
    const int OpaqueMax = 32767;

    static readonly Dictionary<string, int> Dtypes = new()
    {
        ["f64"] = 1, ["f32"] = 2, ["f16"] = 3, ["bf16"] = 4, ["f8e4m3"] = 5, ["f8e5m2"] = 6,
        ["bool"] = 7, ["i8"] = 8, ["u8"] = 9, ["i16"] = 10, ["u16"] = 11, ["i32"] = 12, ["u32"] = 13,
        ["i64"] = 14, ["u64"] = 15, ["i4"] = 16, ["u4"] = 17, ["e2m1"] = 18, ["e8m0"] = 19,
    };

    static readonly string?[] DtypeNames = CreateNames();

    static string?[] CreateNames()
    {
        var names = new string?[20];
        foreach (var pair in Dtypes)
            names[pair.Value] = pair.Key;
        return names;
    }

    public static ulong Align64(ulong value) => Limits.Add(value, 63) & ~63UL;

    public static ulong ElementCount(int rank, ReadOnlySpan<ulong> shape)
    {
        if (rank == 0)
            return 1;
        ulong count = 1;
        for (var dim = 0; dim < rank; dim++)
            count = Limits.Mul(count, shape[dim]);
        return count;
    }

    public static ulong ExpectedBytes(int dtype, ulong count)
    {
        var width = dtype switch
        {
            1 or 14 or 15 => 8,
            2 or 12 or 13 => 4,
            3 or 4 or 10 or 11 => 2,
            5 or 6 or 7 or 8 or 9 or 19 => 1,
            16 or 17 or 18 => -1,
            _ => 0,
        };
        if (width > 0)
            return Limits.Mul(count, (ulong)width);
        if (width < 0)
            return (count + 1) / 2;
        throw new KilnException("TYPE");
    }

    static int DtypeCode(object? dtype)
    {
        if (dtype is string name && Dtypes.TryGetValue(name, out var code))
            return code;
        if (dtype is string)
            throw new KilnException("TYPE");
        var number = Values.AsU64(dtype);
        if (number > ushort.MaxValue)
            throw new KilnException("TYPE");
        return (int)number;
    }

    static object DtypeValue(int code) =>
        (uint)code < (uint)DtypeNames.Length && DtypeNames[code] is { } name ? name : code;

    static byte[] Utf8(string? text) => string.IsNullOrEmpty(text) ? [] : Encoding.UTF8.GetBytes(text);

    static bool Above(ReadOnlySpan<byte> data, byte max)
    {
        foreach (var value in data)
        {
            if (value > max)
                return true;
        }
        return false;
    }
}
