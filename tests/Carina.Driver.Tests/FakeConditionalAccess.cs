using System.Buffers.Binary;

using Carina.Driver.Descrambling;

namespace Carina.Driver.Tests;

internal static class SyntheticCardKeys
{
    public static readonly byte[] SystemKey =
    [
        0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
        0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xF0, 0x0F,
        0x1E, 0x2D, 0x3C, 0x4B, 0x5A, 0x69, 0x78, 0x87,
        0x96, 0xA5, 0xB4, 0xC3, 0xD2, 0xE1, 0xF0, 0x01,
    ];

    public static readonly byte[] InitialValue = [0xC0, 0xFF, 0xEE, 0x00, 0x12, 0x34, 0x56, 0x78];

    public static readonly byte[] CardId = [0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F];

    public const ushort CaSystemId = 0x0ABC;

    public static byte[] PairFor(ReadOnlySpan<byte> ecmBody)
    {
        byte[] pair = new byte[ScrambleKeys.PairLength];
        for (int index = 0; index < pair.Length; index++)
        {
            pair[index] = (byte)(ecmBody[index % ecmBody.Length] ^ (0x5A + index));
        }

        return pair;
    }

    public static Multi2 OddKeyFor(ReadOnlySpan<byte> ecmBody) =>
        Multi2.Keyed(SystemKey, PairFor(ecmBody).AsSpan(0, Multi2.DataKeyLength));

    public static Multi2 EvenKeyFor(ReadOnlySpan<byte> ecmBody) =>
        Multi2.Keyed(SystemKey, PairFor(ecmBody).AsSpan(Multi2.DataKeyLength));

    public static ulong InitialValueAsBlock => BinaryPrimitives.ReadUInt64BigEndian(InitialValue);
}

internal sealed class FakeCardConnection : ISmartCardConnection
{
    public List<byte[]> Commands { get; } = [];

    public Queue<uint> Failures { get; } = new();

    public ushort InitialReturnCode { get; set; } = ConditionalAccessCard.InitialSettingAnswered;

    public Func<byte[], ushort> EcmReturnCode { get; set; } = _ => 0x0800;

    public Func<byte[], byte[]?>? Override { get; set; }

    public int Reconnects { get; private set; }

    public bool Disposed { get; private set; }

    public IEnumerable<byte[]> EcmBodies =>
        Commands.Where(command => command[1] is 0x34).Select(command => command[5..^1]);

    public byte[] Transmit(ReadOnlySpan<byte> command)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);

        byte[] sent = command.ToArray();
        Commands.Add(sent);

        if (Failures.TryDequeue(out uint failure) && failure is not SmartCardCodes.Success)
        {
            throw new SmartCardException("synthetic failure", failure);
        }

        byte[]? overridden = Override?.Invoke(sent);
        if (overridden is not null)
        {
            return overridden;
        }

        return sent[1] is 0x30 ? InitialSetting() : Ecm(sent[5..^1]);
    }

    private byte[] InitialSetting()
    {
        byte[] answer = new byte[58];
        answer[1] = 54;
        answer[2] = 0x00;
        answer[3] = 0x01;
        BinaryPrimitives.WriteUInt16BigEndian(answer.AsSpan(4), InitialReturnCode);
        BinaryPrimitives.WriteUInt16BigEndian(answer.AsSpan(6), SyntheticCardKeys.CaSystemId);
        SyntheticCardKeys.CardId.CopyTo(answer, 8);
        answer[14] = 0x01;
        answer[15] = 0x50;
        SyntheticCardKeys.SystemKey.CopyTo(answer, 16);
        SyntheticCardKeys.InitialValue.CopyTo(answer, 48);
        answer[56] = 0x90;
        answer[57] = 0x00;

        return answer;
    }

    private byte[] Ecm(byte[] body)
    {
        byte[] answer = new byte[27];
        answer[1] = 23;
        answer[3] = 0x34;
        BinaryPrimitives.WriteUInt16BigEndian(answer.AsSpan(4), EcmReturnCode(body));
        SyntheticCardKeys.PairFor(body).CopyTo(answer, 6);
        answer[22] = 0x01;
        answer[25] = 0x90;
        answer[26] = 0x00;

        return answer;
    }

    public void Reconnect()
    {
        Reconnects++;

        if (Failures.TryDequeue(out uint failure) && failure is not SmartCardCodes.Success)
        {
            throw new SmartCardException("synthetic failure", failure);
        }
    }

    public void Dispose() => Disposed = true;
}

internal sealed class FakeSmartCardService : ISmartCardService
{
    private readonly Dictionary<string, Func<ISmartCardConnection>> readers = [];

    private readonly List<string> order = [];

    public List<FakeCardConnection> Connections { get; } = [];

    public bool Disposed { get; private set; }

    public uint? ListingFailure { get; set; }

    public static FakeSmartCardService WithOneCard(out FakeCardConnection card)
    {
        FakeSmartCardService service = new();
        card = service.Card("Synthetic Reader 0");

        return service;
    }

    public FakeCardConnection Card(string reader)
    {
        FakeCardConnection card = new();
        order.Add(reader);
        readers[reader] = () =>
        {
            Connections.Add(card);

            return card;
        };

        return card;
    }

    public void Refusing(string reader, uint code)
    {
        order.Add(reader);
        readers[reader] = () => throw new SmartCardException("synthetic refusal", code);
    }

    public IReadOnlyList<string> Readers() =>
        ListingFailure is uint failure ? throw new SmartCardException("synthetic failure", failure) : order;

    public ISmartCardConnection Connect(string reader) => readers[reader]();

    public void Dispose() => Disposed = true;
}
