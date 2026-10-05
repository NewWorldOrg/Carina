using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Channels;

using Carina.BroadcastTestSupport;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Machines;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class LiveFragmentMaterialTests : IDisposable
{
    private const string Picture = "vide";

    private const string Sound = "soun";

    private const double LongestPieceInSeconds = 0.1;

    private static readonly TimeSpan PastTheProbe = TimeSpan.FromSeconds(12);

    private static readonly TimeSpan HoursIntoTheDay = TimeSpan.FromHours(13);

    private static readonly TimeSpan WhenTheClockComesAround =
        TimeSpan.FromTicks((long)(LivePts.ComesAroundAt * TimeSpan.TicksPerSecond / LivePts.Hertz));

    private static readonly ulong OneFrame = (ulong)(LivePts.Hertz / FrameRate.BroadcastFrames.PerSecond);

    private static readonly ServiceId Service = new(SyntheticBroadcast.SomeProgramNumber);

    private static readonly MachineSettings Machine = new();

    private static readonly StreamAttributes Interlaced = new(
        new VideoSize(1440, 1080),
        ScanType.Interlaced,
        FrameRate.BroadcastFrames,
        AudioMode.Stereo);

    private readonly string room = Directory.CreateTempSubdirectory("carina-fragments").FullName;

    public void Dispose() => Directory.Delete(room, recursive: true);

    public static TheoryData<string> EveryProfile => [.. LiveProfile.All.Select(profile => profile.Name)];

    [Theory(DisplayName = "BR-PD-006: every piece the live transcoder writes while the broadcast goes on carries the picture and the sound together, and the pieces come no further than a tenth of a second apart")]
    [MemberData(nameof(EveryProfile))]
    public async Task BrPd006EveryPieceTheLiveTranscoderWritesCarriesThePictureAndTheSoundTogether(string profile)
    {
        string written = await (SyntheticBroadcast.AsMeasured() with { Length = PastTheProbe })
            .WriteAsync(Path.Combine(room, "live.m2ts"));

        Fragments measured = Fragments.Of(await LiveAsync(written, LiveProfile.Find(profile)!));

        Assert.True(measured.Counted > 2, $"the transcoder wrote {measured.Counted} piece(s), so nothing was measured");
        Assert.True(
            measured.Both >= measured.Counted - 1 && measured.TheOnlyOneApartIsTheLast,
            $"of {measured.Counted} pieces {measured.Both} carried both, {measured.PictureAlone} the picture alone, "
            + $"{measured.SoundAlone} the sound alone and {measured.Neither} neither; the end of the input may flush "
            + "one track out alone as the last piece, and nothing else may");
        Assert.True(
            measured.SecondsApart <= LongestPieceInSeconds,
            $"the pieces came {measured.SecondsApart:F4} s apart on average");
    }

    [Fact(DisplayName = "BR-PD-007: a caption reaches a live viewer on the clock its pictures are carried on, as far after the first picture when the broadcast's clock comes around inside it as when it begins hours into the day")]
    public async Task BrPd007ACaptionIsOnThePicturesClockWhenTheBroadcastsClockComesAroundInsideIt()
    {
        (ulong Picture, IReadOnlyList<ulong> Captions) daytime =
            await CaptionedLiveAsync(HoursIntoTheDay, PastTheProbe, SyntheticCaptions.ShownThenCleared);
        (ulong Picture, IReadOnlyList<ulong> Captions) around =
            await CaptionedLiveAsync(WhenTheClockComesAround - TimeSpan.FromSeconds(5), PastTheProbe, SyntheticCaptions.ShownThenCleared);

        Assert.Equal(2, daytime.Captions.Count);
        Assert.Equal(daytime.Captions.Count, around.Captions.Count);

        foreach ((ulong inTheDay, ulong comingAround) in daytime.Captions.Zip(around.Captions))
        {
            long apart = (long)(comingAround - around.Picture) - (long)(inTheDay - daytime.Picture);

            Assert.True(
                Math.Abs(apart) <= (long)OneFrame,
                string.Create(CultureInfo.InvariantCulture, $"the caption came {apart} ticks away from where it came on a clock hours into the day"));
        }
    }

    [Fact(DisplayName = "BR-PD-007: captions keep reaching a live viewer after the broadcast's clock comes around part way through what is being watched")]
    public async Task BrPd007CaptionsKeepComingAfterTheClockComesAroundPartWayThrough()
    {
        TimeSpan whole = TimeSpan.FromSeconds(80);
        TimeSpan aroundAfter = TimeSpan.FromSeconds(70);

        (ulong picture, IReadOnlyList<ulong> carried) =
            await CaptionedLiveAsync(WhenTheClockComesAround - aroundAfter, whole, SyntheticCaptions.EverySecond);

        Assert.NotEmpty(carried);
        Assert.All(carried, pts => Assert.True(pts >= picture, $"a caption at {pts} came before the first picture at {picture}"));
        Assert.True(
            carried[^1] - carried[0] > (ulong)(aroundAfter.TotalSeconds + 5) * (ulong)LivePts.Hertz,
            string.Create(CultureInfo.InvariantCulture, $"the captions carried ran from {carried[0]} to {carried[^1]}, and stopped where the clock came around"));
    }

    private async Task<(ulong Picture, IReadOnlyList<ulong> Captions)> CaptionedLiveAsync(
        TimeSpan startsAt,
        TimeSpan length,
        SyntheticCaptions shown)
    {
        string written = await (SyntheticBroadcast.AsMeasured() with
        {
            Length = length,
            Captions = shown,
            StartsAt = startsAt,
        }).WriteAsync(Path.Combine(room, string.Create(CultureInfo.InvariantCulture, $"captioned-{startsAt.Ticks}.m2ts")));

        using AnonymousPipeServerStream captions = new(PipeDirection.In, HandleInheritability.Inheritable);

        var start = new ProcessStartInfo(FfmpegProgramme.Default)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in (string[])
                 [
                     .. FfmpegLiveInvocation.Arguments(Service, LiveProfile.Hd30, Interlaced, LiveEncoder.Software, Machine, CaptionOutlet.Drawn),
                     .. FfmpegLiveInvocation.Delivery(),
                     .. FfmpegLiveInvocation.CaptionDelivery(Service, int.Parse(captions.GetClientHandleAsString(), CultureInfo.InvariantCulture)),
                 ])
        {
            start.ArgumentList.Add(argument);
        }

        using Process running = Process.Start(start)!;

        captions.DisposeLocalCopyOfClientHandle();

        Task feeding = Task.Run(async () =>
        {
            await using (FileStream source = File.OpenRead(written))
            {
                await source.CopyToAsync(running.StandardInput.BaseStream);
            }

            running.StandardInput.Close();
        });
        Task<string> complaint = running.StandardError.ReadToEndAsync();

        using MemoryStream delivered = new();
        Task picture = running.StandardOutput.BaseStream.CopyToAsync(delivered);
        Channel<LiveFrame> drawn = Channel.CreateUnbounded<LiveFrame>();

        CaptionFlowFault? fault = await CaptionFrames.CarryAsync(captions, new CaptionCanvas(Interlaced.Size), drawn.Writer, CancellationToken.None);

        await feeding;
        await picture;
        await running.WaitForExitAsync();

        Assert.True(running.ExitCode is 0, await complaint);
        Assert.Null(fault);

        List<ulong> carried = [];

        while (drawn.Reader.TryRead(out LiveFrame? frame))
        {
            if (frame.Channel is LiveChannel.Caption)
            {
                carried.Add(frame.Pts.Value);
            }
        }

        Fragments measured = Fragments.Of(delivered.ToArray());

        Assert.True(measured.FirstPicture is not null, "the transcoder wrote no picture");

        return (measured.FirstPicture.Value, carried);
    }

    private static async Task<byte[]> LiveAsync(string written, LiveProfile profile)
    {
        var start = new ProcessStartInfo(FfmpegProgramme.Default)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in (string[])
                 [
                     .. FfmpegLiveInvocation.Arguments(Service, profile, Interlaced, LiveEncoder.Software, Machine, CaptionOutlet.None),
                     .. FfmpegLiveInvocation.Delivery(),
                 ])
        {
            start.ArgumentList.Add(argument);
        }

        using Process running = Process.Start(start)!;

        Task feeding = Task.Run(async () =>
        {
            await using (FileStream source = File.OpenRead(written))
            {
                await source.CopyToAsync(running.StandardInput.BaseStream);
            }

            running.StandardInput.Close();
        });
        Task<string> complaint = running.StandardError.ReadToEndAsync();

        using MemoryStream delivered = new();

        await running.StandardOutput.BaseStream.CopyToAsync(delivered);
        await feeding;
        await running.WaitForExitAsync();

        Assert.True(running.ExitCode is 0, await complaint);

        return delivered.ToArray();
    }

    private sealed record Fragments(
        int Both,
        int PictureAlone,
        int SoundAlone,
        int Neither,
        bool TheLastCarriedBoth,
        double SecondsApart,
        ulong? FirstPicture)
    {
        public int Counted => Both + PictureAlone + SoundAlone + Neither;

        public bool TheOnlyOneApartIsTheLast => Both == Counted || !TheLastCarriedBoth;

        public static Fragments Of(ReadOnlySpan<byte> delivered)
        {
            Dictionary<uint, (string Kind, uint Rate)> tracks = [];
            List<double> pictures = [];
            ulong? firstPicture = null;
            int both = 0;
            int pictureAlone = 0;
            int soundAlone = 0;
            int neither = 0;
            bool lastCarriedBoth = false;

            foreach ((string name, Range body) in Boxes(delivered))
            {
                if (name is "moov")
                {
                    Learn(delivered[body], tracks);

                    continue;
                }

                if (name is not "moof")
                {
                    continue;
                }

                HashSet<string> carried = [];

                foreach ((string inside, Range traf) in Boxes(delivered[body]))
                {
                    if (inside is not "traf"
                        || Stamp(delivered[body][traf]) is not { } stamped
                        || !tracks.TryGetValue(stamped.Track, out (string Kind, uint Rate) track))
                    {
                        continue;
                    }

                    carried.Add(track.Kind);

                    if (track.Kind is Picture)
                    {
                        pictures.Add((double)stamped.DecodedAt / track.Rate);
                        firstPicture ??= LivePts.Rescaled(stamped.DecodedAt, track.Rate).Value;
                    }
                }

                bool picture = carried.Contains(Picture);
                bool sound = carried.Contains(Sound);

                both += picture && sound ? 1 : 0;
                pictureAlone += picture && !sound ? 1 : 0;
                soundAlone += sound && !picture ? 1 : 0;
                neither += !picture && !sound ? 1 : 0;
                lastCarriedBoth = picture && sound;
            }

            return new Fragments(
                both,
                pictureAlone,
                soundAlone,
                neither,
                lastCarriedBoth,
                pictures.Count > 1 ? (pictures[^1] - pictures[0]) / (pictures.Count - 1) : double.PositiveInfinity,
                firstPicture);
        }

        private static void Learn(ReadOnlySpan<byte> moov, Dictionary<uint, (string Kind, uint Rate)> tracks)
        {
            foreach ((string name, Range body) in Boxes(moov))
            {
                if (name is not "trak")
                {
                    continue;
                }

                ReadOnlySpan<byte> trak = moov[body];
                uint? id = null;
                uint? rate = null;
                string? kind = null;

                foreach ((string inside, Range held) in Boxes(trak))
                {
                    if (inside is "tkhd")
                    {
                        ReadOnlySpan<byte> tkhd = trak[held];

                        id = BinaryPrimitives.ReadUInt32BigEndian(tkhd[(tkhd[0] is 1 ? 20 : 12)..]);
                    }

                    if (inside is "mdia")
                    {
                        (string? saidKind, uint? saidRate) = Media(trak[held]);
                        kind = saidKind ?? kind;
                        rate = saidRate ?? rate;
                    }
                }

                if (id is { } track && rate is { } ticks && ticks > 0 && kind is not null)
                {
                    tracks[track] = (kind, ticks);
                }
            }
        }

        private static (string? Kind, uint? Rate) Media(ReadOnlySpan<byte> mdia)
        {
            string? kind = null;
            uint? rate = null;

            foreach ((string within, Range said) in Boxes(mdia))
            {
                ReadOnlySpan<byte> box = mdia[said];

                if (within is "hdlr")
                {
                    kind = Encoding.ASCII.GetString(box.Slice(8, 4));
                }

                if (within is "mdhd")
                {
                    rate = BinaryPrimitives.ReadUInt32BigEndian(box[(box[0] is 1 ? 20 : 12)..]);
                }
            }

            return (kind, rate);
        }

        private static (uint Track, ulong DecodedAt)? Stamp(ReadOnlySpan<byte> traf)
        {
            uint? track = null;
            ulong? decodedAt = null;

            foreach ((string name, Range body) in Boxes(traf))
            {
                ReadOnlySpan<byte> box = traf[body];

                if (name is "tfhd")
                {
                    track = BinaryPrimitives.ReadUInt32BigEndian(box[4..]);
                }

                if (name is "tfdt")
                {
                    decodedAt = box[0] is 1
                        ? BinaryPrimitives.ReadUInt64BigEndian(box[4..])
                        : BinaryPrimitives.ReadUInt32BigEndian(box[4..]);
                }
            }

            return track is { } id && decodedAt is { } at ? (id, at) : null;
        }

        private static List<(string Name, Range Body)> Boxes(ReadOnlySpan<byte> held)
        {
            List<(string, Range)> found = [];
            int at = 0;

            while (at + 8 <= held.Length)
            {
                long length = BinaryPrimitives.ReadUInt32BigEndian(held[at..]);
                int header = 8;

                if (length is 1)
                {
                    length = (long)BinaryPrimitives.ReadUInt64BigEndian(held[(at + 8)..]);
                    header = 16;
                }

                if (length < header || at + length > held.Length)
                {
                    break;
                }

                found.Add((Encoding.ASCII.GetString(held.Slice(at + 4, 4)), (at + header)..(at + (int)length)));
                at += (int)length;
            }

            return found;
        }
    }
}
