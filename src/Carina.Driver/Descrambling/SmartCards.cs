namespace Carina.Driver.Descrambling;

/// <summary>
/// A failure the smart-card service reported, carrying its result code and nothing the card said.
/// </summary>
public sealed class SmartCardException(string message, uint code) : IOException(message)
{
    public uint Code { get; } = code;

    public bool CardWasReset => Code is SmartCardCodes.ResetCard or SmartCardCodes.RemovedCard;
}

/// <summary>
/// The PC/SC result codes this driver tells apart.
/// </summary>
public static class SmartCardCodes
{
    public const uint Success = 0x00000000;

    public const uint InsufficientBuffer = 0x80100008;

    public const uint SharingViolation = 0x8010000B;

    public const uint NoSmartCard = 0x8010000C;

    public const uint NoService = 0x8010001D;

    public const uint NoReadersAvailable = 0x8010002E;

    public const uint ResetCard = 0x80100068;

    public const uint RemovedCard = 0x80100069;

    public static string Describe(uint code) =>
        code switch
        {
            Success => "success",
            InsufficientBuffer => "the answer did not fit",
            SharingViolation => "another program holds the card exclusively",
            NoSmartCard => "no card is in the reader",
            NoService => "the smart-card service is not running",
            NoReadersAvailable => "no reader is attached",
            ResetCard => "the card was reset",
            RemovedCard => "the card was removed",
            _ => "an error the service did not name",
        } + $" (0x{code:X8})";
}

/// <summary>
/// One connection to the card in one reader, spoken to in whole commands.
/// </summary>
public interface ISmartCardConnection : IDisposable
{
    byte[] Transmit(ReadOnlySpan<byte> command);

    void Reconnect();
}

/// <summary>
/// A context with the smart-card service: the readers it sees and a way to connect to one.
/// </summary>
public interface ISmartCardService : IDisposable
{
    IReadOnlyList<string> Readers();

    ISmartCardConnection Connect(string reader);
}
