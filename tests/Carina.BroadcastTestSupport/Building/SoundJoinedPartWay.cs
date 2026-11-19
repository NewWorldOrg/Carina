using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;

namespace Carina.BroadcastTestSupport;

public static class SoundJoinedPartWay
{
    public const int PesBytes = 512;

    public const int FramesBeforeTheJoin = 5;

    public const int BytesIntoTheFrame = 100;

    public const int HeaderIntoTheLeftover = 20;

    public const int LengthTheHeaderClaims = 900;

    public const int AdtsSound = 0x0F;

    public const long TicksPerFrame =
        TransportStreamWriter.ProgrammeClockTicksPerSecond * DualMonoAdts.SamplesPerFrame / DualMonoAdts.SampleRate;

    public static byte[] Into(byte[] stream, int mapPid)
    {
        ArgumentNullException.ThrowIfNull(stream);

        int soundPid = SoundPid(stream, mapPid);
        (byte[] sound, List<SoundFrame> frames) = Sound(stream, soundPid);
        int join = frames[FramesBeforeTheJoin].At + BytesIntoTheFrame;

        DualMonoAdts.Header(LengthTheHeaderClaims, DualMonoAdts.Stereo).CopyTo(sound, join + HeaderIntoTheLeftover);

        Queue<byte[]> carried = new(Carried(sound, frames, join, soundPid));
        List<byte> spliced = [];

        for (int at = 0; at + TransportStreamWriter.PacketSize <= stream.Length; at += TransportStreamWriter.PacketSize)
        {
            byte[] packet = stream[at..(at + TransportStreamWriter.PacketSize)];

            if (!IsOn(packet, soundPid))
            {
                spliced.AddRange(packet);
            }
            else if (carried.TryDequeue(out byte[]? next))
            {
                spliced.AddRange(next);
            }
        }

        foreach (byte[] rest in carried)
        {
            spliced.AddRange(rest);
        }

        return [.. spliced];
    }

    public static int SoundPid(byte[] stream, int mapPid)
        => CarouselSplice.FirstMap(stream, mapPid).Streams.First(carried => carried.StreamType is AdtsSound).Pid;

    public static IReadOnlyList<byte[]> Pes(byte[] stream, int pid)
    {
        ArgumentNullException.ThrowIfNull(stream);

        List<byte[]> found = [];
        List<byte>? building = null;

        for (int at = 0; at + TransportStreamWriter.PacketSize <= stream.Length; at += TransportStreamWriter.PacketSize)
        {
            if (!TransportPacket.TryRead(stream.AsSpan(at, TransportStreamWriter.PacketSize), out TransportPacket read)
                || read.Pid != pid)
            {
                continue;
            }

            if (read.PayloadUnitStart)
            {
                Finish(building, found);
                building = [];
            }

            building?.AddRange(read.Payload);
        }

        Finish(building, found);

        return found;
    }

    private static bool IsOn(byte[] packet, int pid)
        => TransportPacket.TryRead(packet, out TransportPacket read) && read.Pid == pid;

    private static void Finish(List<byte>? building, List<byte[]> found)
    {
        if (building is not null)
        {
            found.Add([.. building]);
        }
    }

    private static (byte[] Sound, List<SoundFrame> Frames) Sound(byte[] stream, int soundPid)
    {
        List<byte> sound = [];
        List<SoundFrame> frames = [];

        foreach (byte[] pes in Pes(stream, soundPid))
        {
            int body = PesWriter.HeaderLength + pes[PesWriter.HeaderLength - 1];
            long pts = PesWriter.TimestampOf(pes);

            for (int at = body; at + DualMonoAdts.HeaderLength <= pes.Length; at += DualMonoAdts.FrameLengthOf(pes.AsSpan(at)))
            {
                frames.Add(new SoundFrame(sound.Count + at - body, pts));
                pts += TicksPerFrame;
            }

            sound.AddRange(pes.AsSpan(body));
        }

        return ([.. sound], frames);
    }

    private static IReadOnlyList<byte[]> Carried(byte[] sound, List<SoundFrame> frames, int join, int soundPid)
    {
        TransportStreamWriter writer = new(soundPid);

        for (int at = join; at < sound.Length; at += PesBytes)
        {
            int from = at;
            int until = Math.Min(at + PesBytes, sound.Length);
            long? pts = frames
                .Where(frame => frame.At >= from && frame.At < until)
                .Select(frame => (long?)frame.Pts)
                .FirstOrDefault();

            PesWriter.Packets(writer, PesWriter.Sound(pts, sound.AsSpan(from, until - from)));
        }

        return writer.Packets;
    }

    private sealed record SoundFrame(int At, long Pts);
}
