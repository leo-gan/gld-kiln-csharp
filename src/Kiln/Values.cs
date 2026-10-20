namespace Kiln;

static class Values
{
    public static bool AsBool(object? value) => value is bool bit ? bit : throw new KilnException("TYPE");

    public static long AsI64(object? value) => value switch
    {
        long number => number,
        int number => number,
        short number => number,
        sbyte number => number,
        ulong number when number <= long.MaxValue => (long)number,
        uint number => number,
        ushort number => number,
        byte number => number,
        _ => throw new KilnException("TYPE"),
    };

    public static ulong AsU64(object? value) => value switch
    {
        ulong number => number,
        long number when number >= 0 => (ulong)number,
        uint number => number,
        int number when number >= 0 => (ulong)number,
        ushort number => number,
        short number when number >= 0 => (ulong)number,
        byte number => number,
        sbyte number when number >= 0 => (byte)number,
        _ => throw new KilnException("TYPE"),
    };

    public static byte AsI8(object? value)
    {
        var number = AsI64(value);
        if (number is < -128 or > 255)
            throw new KilnException("TYPE");
        return unchecked((byte)number);
    }

    public static byte AsU8(object? value)
    {
        var number = AsU64(value);
        if (number > byte.MaxValue)
            throw new KilnException("TYPE");
        return (byte)number;
    }

    public static short AsI16(object? value)
    {
        var number = AsI64(value);
        if (number is < -32768 or > 65535)
            throw new KilnException("TYPE");
        return unchecked((short)number);
    }

    public static ushort AsU16(object? value)
    {
        var number = AsU64(value);
        if (number > ushort.MaxValue)
            throw new KilnException("TYPE");
        return (ushort)number;
    }

    public static int AsI32(object? value)
    {
        var number = AsI64(value);
        if (number is < int.MinValue or > uint.MaxValue)
            throw new KilnException("TYPE");
        return unchecked((int)number);
    }

    public static uint AsU32(object? value)
    {
        var number = AsU64(value);
        if (number > uint.MaxValue)
            throw new KilnException("TYPE");
        return (uint)number;
    }

    public static float AsF32(object? value) => value switch
    {
        float number => number,
        double number => (float)number,
        _ => throw new KilnException("TYPE"),
    };

    public static double AsF64(object? value) => value switch
    {
        double number => number,
        float number => number,
        _ => throw new KilnException("TYPE"),
    };

    public static IReadOnlyDictionary<string, object?> AsMap(object? value) =>
        value as IReadOnlyDictionary<string, object?> ?? throw new KilnException("TYPE");

    public static bool TryBytes(object? value, out ReadOnlyMemory<byte> bytes)
    {
        switch (value)
        {
            case byte[] array:
                bytes = array;
                return true;
            case ReadOnlyMemory<byte> memory:
                bytes = memory;
                return true;
            case Memory<byte> memory:
                bytes = memory;
                return true;
            case ArraySegment<byte> segment:
                bytes = segment;
                return true;
            default:
                bytes = default;
                return false;
        }
    }
}
