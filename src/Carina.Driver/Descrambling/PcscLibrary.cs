using System.Runtime.InteropServices;
using System.Text;

namespace Carina.Driver.Descrambling;

[StructLayout(LayoutKind.Sequential)]
internal struct PcscIoRequest
{
    public nuint Protocol;

    public nuint Length;
}

/// <summary>
/// pcsc-lite's SCARD_READERSTATE. The answer to reset the service copies in is never read.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PcscReaderState
{
    public const int AnswerToResetCapacity = 33;

    public byte* Reader;

    public void* UserData;

    public nuint CurrentState;

    public nuint EventState;

    public nuint AnswerToResetLength;

    public fixed byte AnswerToReset[AnswerToResetCapacity];
}

/// <summary>
/// The PC/SC functions of pcsc-lite, where a DWORD and a LONG are as wide as a pointer.
/// </summary>
public sealed unsafe class PcscLibrary
{
    public const string SharedObject = "libpcsclite.so.1";

    private const nuint UserScope = 0;

    private const nuint SharedAccess = 2;

    private const nuint ProtocolT1 = 2;

    private const nuint LeaveCard = 0;

    private const int AnswerCapacity = 258;

    private const nuint StateUnaware = 0x0000;

    private const nuint StateEmpty = 0x0010;

    private const nuint StatePresent = 0x0020;

    private const nuint StateMute = 0x0200;

    private readonly delegate* unmanaged<nuint, void*, void*, nint*, nint> establishContext;

    private readonly delegate* unmanaged<nint, nint> releaseContext;

    private readonly delegate* unmanaged<nint, byte*, byte*, nuint*, nint> listReaders;

    private readonly delegate* unmanaged<nint, byte*, nuint, nuint, nint*, nuint*, nint> connect;

    private readonly delegate* unmanaged<nint, nuint, nuint, nuint, nuint*, nint> reconnect;

    private readonly delegate* unmanaged<nint, nuint, nint> disconnect;

    private readonly delegate* unmanaged<nint, PcscIoRequest*, byte*, nuint, PcscIoRequest*, byte*, nuint*, nint> transmit;

    private readonly delegate* unmanaged<nint, nuint, PcscReaderState*, nuint, nint> getStatusChange;

    private PcscLibrary(IReadOnlyDictionary<string, nint> exports)
    {
        establishContext = (delegate* unmanaged<nuint, void*, void*, nint*, nint>)exports["SCardEstablishContext"];
        releaseContext = (delegate* unmanaged<nint, nint>)exports["SCardReleaseContext"];
        listReaders = (delegate* unmanaged<nint, byte*, byte*, nuint*, nint>)exports["SCardListReaders"];
        connect = (delegate* unmanaged<nint, byte*, nuint, nuint, nint*, nuint*, nint>)exports["SCardConnect"];
        reconnect = (delegate* unmanaged<nint, nuint, nuint, nuint, nuint*, nint>)exports["SCardReconnect"];
        disconnect = (delegate* unmanaged<nint, nuint, nint>)exports["SCardDisconnect"];
        transmit = (delegate* unmanaged<nint, PcscIoRequest*, byte*, nuint, PcscIoRequest*, byte*, nuint*, nint>)exports["SCardTransmit"];
        getStatusChange = (delegate* unmanaged<nint, nuint, PcscReaderState*, nuint, nint>)exports["SCardGetStatusChange"];
    }

    private static readonly string[] EntryPoints =
    [
        "SCardEstablishContext",
        "SCardReleaseContext",
        "SCardListReaders",
        "SCardConnect",
        "SCardReconnect",
        "SCardDisconnect",
        "SCardTransmit",
        "SCardGetStatusChange",
    ];

    public static PcscLibrary? Load(out string whyNot)
    {
        if (!NativeLibrary.TryLoad(SharedObject, out nint handle))
        {
            whyNot = $"'{SharedObject}' is not installed on this machine.";

            return null;
        }

        Dictionary<string, nint> exports = [];
        foreach (string entryPoint in EntryPoints)
        {
            if (!NativeLibrary.TryGetExport(handle, entryPoint, out nint export))
            {
                whyNot = $"'{SharedObject}' carries no '{entryPoint}'.";

                return null;
            }

            exports[entryPoint] = export;
        }

        whyNot = string.Empty;

        return new PcscLibrary(exports);
    }

    public ISmartCardService Open()
    {
        nint context;
        Insist(establishContext(UserScope, null, null, &context), "opening a context with the smart-card service");

        return new Service(this, context);
    }

    /// <summary>
    /// Splits the reader list the service answers with: names ended by a zero byte, the list ended by one more.
    /// </summary>
    public static IReadOnlyList<string> ReaderNames(ReadOnlySpan<byte> list)
    {
        List<string> names = [];
        ReadOnlySpan<byte> rest = list;

        while (!rest.IsEmpty && rest[0] is not 0)
        {
            int end = rest.IndexOf((byte)0);
            if (end < 0)
            {
                end = rest.Length;
            }

            names.Add(Encoding.UTF8.GetString(rest[..end]));
            rest = end < rest.Length ? rest[(end + 1)..] : [];
        }

        return names;
    }

    /// <summary>
    /// Reads a slot from the event state the service answers with: a mute card is there but would not answer reset.
    /// </summary>
    public static CardSlot SlotOf(nuint eventState)
    {
        if ((eventState & StateMute) is not 0)
        {
            return CardSlot.Mute;
        }

        if ((eventState & StatePresent) is not 0)
        {
            return CardSlot.Present;
        }

        return (eventState & StateEmpty) is not 0 ? CardSlot.Empty : CardSlot.Unknown;
    }

    private static void Insist(nint result, string what)
    {
        uint code = (uint)result;
        if (code is not SmartCardCodes.Success)
        {
            throw new SmartCardException(
                $"The smart-card service refused {what}: {SmartCardCodes.Describe(code)}.",
                code
            );
        }
    }

    private sealed class Service(PcscLibrary library, nint context) : ISmartCardService
    {
        private nint context = context;

        public IReadOnlyList<string> Readers()
        {
            nuint length = 0;
            uint first = (uint)library.listReaders(Alive(), null, null, &length);
            if (first is SmartCardCodes.NoReadersAvailable)
            {
                return [];
            }

            Insist((nint)first, "listing the readers");

            byte[] list = new byte[(int)length];
            fixed (byte* names = list)
            {
                uint second = (uint)library.listReaders(Alive(), null, names, &length);
                if (second is SmartCardCodes.NoReadersAvailable)
                {
                    return [];
                }

                Insist((nint)second, "listing the readers");
            }

            return ReaderNames(list.AsSpan(0, Math.Min(list.Length, (int)length)));
        }

        public IReadOnlyList<CardSlot> Slots(IReadOnlyList<string> readers)
        {
            ArgumentNullException.ThrowIfNull(readers);

            if (readers.Count is 0)
            {
                return [];
            }

            byte[][] names = [.. readers.Select(reader => (byte[])[.. Encoding.UTF8.GetBytes(reader), 0])];
            PcscReaderState[] states = new PcscReaderState[readers.Count];
            GCHandle[] pinned = [.. names.Select(name => GCHandle.Alloc(name, GCHandleType.Pinned))];

            try
            {
                for (int index = 0; index < states.Length; index++)
                {
                    states[index].Reader = (byte*)pinned[index].AddrOfPinnedObject();
                    states[index].CurrentState = StateUnaware;
                }

                fixed (PcscReaderState* asked = states)
                {
                    uint code = (uint)library.getStatusChange(Alive(), 0, asked, (nuint)states.Length);
                    if (code is not SmartCardCodes.Timeout)
                    {
                        Insist((nint)code, "reading what the readers hold");
                    }
                }
            }
            finally
            {
                foreach (GCHandle handle in pinned)
                {
                    handle.Free();
                }
            }

            return [.. states.Select(state => SlotOf(state.EventState))];
        }

        public ISmartCardConnection Connect(string reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            byte[] name = [.. Encoding.UTF8.GetBytes(reader), 0];
            nint card;
            nuint active;

            fixed (byte* named = name)
            {
                Insist(
                    library.connect(Alive(), named, SharedAccess, ProtocolT1, &card, &active),
                    "connecting to the card"
                );
            }

            return new Connection(library, card);
        }

        private nint Alive() =>
            context is 0 ? throw new ObjectDisposedException(nameof(PcscLibrary)) : context;

        public void Dispose()
        {
            if (context is 0)
            {
                return;
            }

            library.releaseContext(context);
            context = 0;
        }
    }

    private sealed class Connection(PcscLibrary library, nint card) : ISmartCardConnection
    {
        private nint card = card;

        public byte[] Transmit(ReadOnlySpan<byte> command)
        {
            PcscIoRequest request = new() { Protocol = ProtocolT1, Length = (nuint)sizeof(PcscIoRequest) };
            byte[] answer = new byte[AnswerCapacity];
            nuint length = (nuint)answer.Length;

            fixed (byte* sending = command)
            fixed (byte* receiving = answer)
            {
                Insist(
                    library.transmit(Alive(), &request, sending, (nuint)command.Length, null, receiving, &length),
                    "a command to the card"
                );
            }

            return answer[..(int)length];
        }

        public void Reconnect()
        {
            nuint active;
            Insist(
                library.reconnect(Alive(), SharedAccess, ProtocolT1, LeaveCard, &active),
                "reconnecting to the card"
            );
        }

        private nint Alive() =>
            card is 0 ? throw new ObjectDisposedException(nameof(PcscLibrary)) : card;

        public void Dispose()
        {
            if (card is 0)
            {
                return;
            }

            library.disconnect(card, LeaveCard);
            card = 0;
        }
    }
}
