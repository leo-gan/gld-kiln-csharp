namespace Kiln;

/// <summary>Decode or encode failure. <see cref="Code"/> is FRAME, LIMIT, EOF, TYPE, UTF8, TRAILING, or SCHEMA.</summary>
public sealed class KilnException : Exception
{
    public string Code { get; }
    public int Offset { get; }

    public KilnException(string code, int offset = 0) : base(code)
    {
        Code = code;
        Offset = offset;
    }
}
