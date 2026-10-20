namespace Kiln;

public static class Limits
{
    public const int DefaultCap = 16_777_216;
    public const int MaxDepth = 64;

    public static void Fit(ulong count, ulong each, ulong unread, int cap)
    {
        if (cap < 0 || count > (ulong)cap)
            throw new KilnException("LIMIT");
        if (each > 0 && count > ulong.MaxValue / each)
            throw new KilnException("LIMIT");
        if (each > 0 && count * each > unread)
            throw new KilnException("EOF");
    }

    public static ulong Add(ulong left, ulong right)
    {
        if (left > ulong.MaxValue - right)
            throw new KilnException("LIMIT");
        return left + right;
    }

    public static ulong Mul(ulong left, ulong right)
    {
        if (left != 0 && right > ulong.MaxValue / left)
            throw new KilnException("LIMIT");
        return left * right;
    }
}
