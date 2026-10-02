using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

using Carina.BroadcastTestSupport;
using Carina.Domain.Base;
using Carina.Domain.Channels;
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

    private static readonly ServiceId Service = new(SyntheticBroadcast.SomeProgramNumber);

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
                     .. FfmpegLiveInvocation.Arguments(Service, profile, Interlaced, LiveEncoder.Software, CaptionOutlet.None),
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
        double SecondsApart)
    {
        public int Counted => Both + PictureAlone + SoundAlone + Neither;

        public bool TheOnlyOneApartIsTheLast => Both == Counted || !TheLastCarriedBoth;

        public static Fragments Of(ReadOnlySpan<byte> delivered)
        {
            Dictionary<uint, (string Kind, uint Rate)> tracks = [];
            List<double> pictures = [];
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
                pictures.Count > 1 ? (pictures[^1] - pictures[0]) / (pictures.Count - 1) : double.PositiveInfinity);
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
                        foreach ((string within, Range said) in Boxes(trak[held]))
                        {
                            ReadOnlySpan<byte> box = trak[held][said];

                            if (within is "hdlr")
                            {
                                kind = Encoding.ASCII.GetString(box.Slice(8, 4));
                            }

                            if (within is "mdhd")
                            {
                                rate = BinaryPrimitives.ReadUInt32BigEndian(box[(box[0] is 1 ? 20 : 12)..]);
                            }
                        }
                    }
                }

                if (id is { } track && rate is { } ticks && ticks > 0 && kind is not null)
                {
                    tracks[track] = (kind, ticks);
                }
            }
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
