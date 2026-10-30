namespace Carina.Driver.Descrambling;

/// <summary>
/// Something that answers ECMs with scramble keys: the card in this machine's reader, or one borrowed from another driver.
/// </summary>
public interface IConditionalAccess : IDisposable
{
    int CaSystemId { get; }

    /// <summary>
    /// Hands over the body of one ECM section, the bytes after its eight-byte header and before its CRC.
    /// </summary>
    EcmAnswer Answer(ReadOnlySpan<byte> ecmBody);
}
