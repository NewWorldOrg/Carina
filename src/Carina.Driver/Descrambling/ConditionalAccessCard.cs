using System.Buffers.Binary;

namespace Carina.Driver.Descrambling;

/// <summary>
/// What the card said to one ECM: its return code, and the keys when the code grants them.
/// </summary>
public sealed class EcmAnswer(ushort returnCode, ScrambleKeys? keys)
{
    public ushort ReturnCode { get; } = returnCode;

    public ScrambleKeys? Keys { get; } = keys;

    public bool Granted => Keys is not null;
}

/// <summary>
/// Which card answered the initial setting: the CA system it belongs to and its card ID, the six bytes that follow
/// the CA system in the answer. The ID names one card, so <see cref="ToString"/> leaves it out.
/// </summary>
public sealed class CardIdentity
{
    public const int CardIdLength = 6;

    public CardIdentity(int caSystemId, ReadOnlySpan<byte> cardId)
    {
        if (cardId.Length is not CardIdLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cardId),
                $"A card ID is {CardIdLength} bytes, not {cardId.Length}."
            );
        }

        CaSystemId = caSystemId;
        CardId = Convert.ToHexString(cardId);
    }

    public int CaSystemId { get; }

    /// <summary>
    /// The card ID as twelve upper-case hexadecimal digits, one pair for each byte in the order the card sent them.
    /// </summary>
    public string CardId { get; }

    public override string ToString() => $"a card of CA system 0x{CaSystemId:X4}";
}

/// <summary>
/// The conditional-access card of ARIB STD-B25, spoken to over PC/SC with the two commands descrambling needs.
/// </summary>
public sealed class ConditionalAccessCard : IDisposable
{
    public const int LongestEcmBody = 255;

    public const ushort InitialSettingAnswered = 0x2100;

    private const byte CommandClass = 0x90;

    private const byte InitialSettingInstruction = 0x30;

    private const byte EcmInstruction = 0x34;

    private const ushort StatusCompleted = 0x9000;

    private const int StatusLength = 2;

    private const int ReturnCodeOffset = 4;

    private const int CaSystemOffset = 6;

    private const int CardIdOffset = 8;

    private const int SystemKeyOffset = 16;

    private const int InitialValueOffset = 48;

    private const int InitialSettingLength = 56;

    private const int KeyPairOffset = 6;

    private const int EcmAnswerLength = KeyPairOffset + ScrambleKeys.PairLength;

    private static readonly ushort[] GrantingCodes = [0x0200, 0x0400, 0x0800];

    private readonly ISmartCardService service;

    private readonly ISmartCardConnection connection;

    private readonly Lock gate = new();

    private readonly Action<CardIdentity>? settled;

    private byte[] systemKey = [];

    private byte[] initialValue = [];

    private bool disposed;

    private ConditionalAccessCard(
        ISmartCardService service,
        ISmartCardConnection connection,
        Action<CardIdentity>? settled
    )
    {
        this.service = service;
        this.connection = connection;
        this.settled = settled;
    }

    public int CaSystemId => Identity?.CaSystemId ?? 0;

    /// <summary>
    /// The card that answered the initial setting last, which after the card was reset may be another card.
    /// </summary>
    public CardIdentity? Identity { get; private set; }

    public static ReadOnlySpan<byte> InitialSettingCommand =>
        [CommandClass, InitialSettingInstruction, 0x00, 0x00, 0x00];

    /// <summary>
    /// Takes the service over and connects to the first reader, in the order the service lists them, whose card answers the initial setting.
    /// <paramref name="settled"/> hears which card answered, each time one does.
    /// </summary>
    public static ConditionalAccessCard Open(ISmartCardService service, Action<CardIdentity>? settled = null)
    {
        ArgumentNullException.ThrowIfNull(service);

        try
        {
            return Connect(service, settled);
        }
        catch
        {
            service.Dispose();

            throw;
        }
    }

    private static ConditionalAccessCard Connect(ISmartCardService service, Action<CardIdentity>? settled)
    {
        IReadOnlyList<string> readers = Ask(service.Readers, "listing the readers");
        if (readers.Count is 0)
        {
            throw new DescramblingException("No smart-card reader is attached, so there is no card to ask.");
        }

        List<string> refusals = [];
        foreach (string reader in readers)
        {
            ConditionalAccessCard? card = TryReader(service, reader, refusals, settled);
            if (card is not null)
            {
                return card;
            }
        }

        throw new DescramblingException(
            $"None of the {readers.Count} reader(s) held a card that answered the initial setting: {string.Join("; ", refusals)}."
        );
    }

    private static ConditionalAccessCard? TryReader(
        ISmartCardService service,
        string reader,
        List<string> refusals,
        Action<CardIdentity>? settled
    )
    {
        ISmartCardConnection connection;

        try
        {
            connection = service.Connect(reader);
        }
        catch (SmartCardException refused)
        {
            refusals.Add(SmartCardCodes.Describe(refused.Code));

            return null;
        }

        ConditionalAccessCard card = new(service, connection, settled);

        try
        {
            card.Settle();

            return card;
        }
        catch (DescramblingException refused)
        {
            connection.Dispose();
            refusals.Add(refused.Message);

            return null;
        }
    }

    /// <summary>
    /// Hands the card the body of one ECM section — the bytes after its eight-byte header and before its CRC.
    /// </summary>
    public EcmAnswer Answer(ReadOnlySpan<byte> ecmBody)
    {
        if (ecmBody.Length is 0 or > LongestEcmBody)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ecmBody),
                $"An ECM body handed to the card is 1 to {LongestEcmBody} bytes, not {ecmBody.Length}."
            );
        }

        byte[] command = [CommandClass, EcmInstruction, 0x00, 0x00, (byte)ecmBody.Length, .. ecmBody, 0x00];

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            byte[] answer = TransmitOnce(command, "an ECM");
            ReadOnlySpan<byte> payload = Payload(answer, ReturnCodeOffset + 2, "an ECM");
            ushort returnCode = BinaryPrimitives.ReadUInt16BigEndian(payload[ReturnCodeOffset..]);

            if (!GrantingCodes.Contains(returnCode))
            {
                return new EcmAnswer(returnCode, null);
            }

            payload = Payload(answer, EcmAnswerLength, "an ECM that unlocked its keys");

            return new EcmAnswer(
                returnCode,
                ScrambleKeys.Of(systemKey, initialValue, payload.Slice(KeyPairOffset, ScrambleKeys.PairLength))
            );
        }
    }

    private void Settle()
    {
        byte[] answer = Transmit(InitialSettingCommand, "the initial setting");
        ReadOnlySpan<byte> payload = Payload(answer, InitialSettingLength, "the initial setting");

        ushort returnCode = BinaryPrimitives.ReadUInt16BigEndian(payload[ReturnCodeOffset..]);
        if (returnCode is not InitialSettingAnswered)
        {
            throw new DescramblingException(
                $"The card answered the initial setting with return code 0x{returnCode:X4} rather than 0x{InitialSettingAnswered:X4}."
            );
        }

        CardIdentity identity = new(
            BinaryPrimitives.ReadUInt16BigEndian(payload[CaSystemOffset..]),
            payload.Slice(CardIdOffset, CardIdentity.CardIdLength)
        );
        systemKey = payload.Slice(SystemKeyOffset, Multi2.SystemKeyLength).ToArray();
        initialValue = payload.Slice(InitialValueOffset, ScrambleKeys.InitialValueLength).ToArray();
        Identity = identity;
        settled?.Invoke(identity);
    }

    private byte[] TransmitOnce(byte[] command, string what)
    {
        try
        {
            return connection.Transmit(command);
        }
        catch (SmartCardException reset) when (reset.CardWasReset)
        {
            Ask(connection.Reconnect, "reconnecting after the card was reset");
            Settle();

            return Transmit(command, what);
        }
        catch (SmartCardException failed)
        {
            throw Stopped(failed, what);
        }
    }

    private byte[] Transmit(ReadOnlySpan<byte> command, string what)
    {
        try
        {
            return connection.Transmit(command);
        }
        catch (SmartCardException failed)
        {
            throw Stopped(failed, what);
        }
    }

    private static ReadOnlySpan<byte> Payload(byte[] answer, int least, string what)
    {
        if (answer.Length < least + StatusLength)
        {
            throw new DescramblingException(
                $"The card's answer to {what} was {answer.Length} bytes, shorter than the {least + StatusLength} it must be."
            );
        }

        ushort status = BinaryPrimitives.ReadUInt16BigEndian(answer.AsSpan(answer.Length - StatusLength));
        if (status is not StatusCompleted)
        {
            throw new DescramblingException(
                $"The card ended its answer to {what} with status 0x{status:X4} rather than 0x{StatusCompleted:X4}."
            );
        }

        return answer.AsSpan(0, answer.Length - StatusLength);
    }

    private static T Ask<T>(Func<T> ask, string what)
    {
        try
        {
            return ask();
        }
        catch (SmartCardException failed)
        {
            throw Stopped(failed, what);
        }
    }

    private static void Ask(Action ask, string what)
    {
        try
        {
            ask();
        }
        catch (SmartCardException failed)
        {
            throw Stopped(failed, what);
        }
    }

    private static DescramblingException Stopped(SmartCardException failed, string what) =>
        new($"The card could not be reached for {what}: {SmartCardCodes.Describe(failed.Code)}.");

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        try
        {
            connection.Dispose();
        }
        finally
        {
            service.Dispose();
        }
    }
}
