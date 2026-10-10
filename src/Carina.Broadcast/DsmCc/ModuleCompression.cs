namespace Carina.Broadcast.DsmCc;

public sealed record ModuleCompression(int CompressionType, long OriginalSize)
{
    public const int Zlib = 0x00;

    public bool IsZlib => CompressionType == Zlib;
}
