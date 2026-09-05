using System.Globalization;

namespace Carina.BroadcastTestSupport;

public enum SyntheticPicture
{
    None = 0,

    BroadcastHd = 1,

    StandardDefinition = 2,

    FullHd = 3,
}

public enum SyntheticSound
{
    Stereo = 1,

    Mono = 2,

    DualMono = 3,

    Surround = 4,

    TwoLanguages = 5,
}

public enum SyntheticCaptions
{
    EverySecond = 1,

    ShownThenCleared = 2,
}

public sealed record SyntheticBroadcast
{
    public const int SomeProgramNumber = 1040;

    public const int SideTransportStreamId = 1;

    public const int PmtPid = 0x1FC8;

    public const int FirstElementaryPid = 0x100;

    public const int CaptionPid = 0x130;

    public const int SuperimposePid = 0x138;

    public const string BroadcastRate = "30000/1001";

    public const string TransportStream = ".m2ts";

    public const string Encoded = ".mp4";

    public const int CaptionShownAtSecond = 8;

    public const int CaptionClearedAtSecond = 9;

    public static readonly TimeSpan DefaultLength = TimeSpan.FromSeconds(3);

    private const int TicksPerSecond = 90_000;

    private const int CaptionRow = 7;

    private const int CaptionColumn = 2;

    public SyntheticPicture Picture { get; init; } = SyntheticPicture.BroadcastHd;

    public SyntheticSound Sound { get; init; } = SyntheticSound.Stereo;

    public bool WithCaptions { get; init; } = true;

    public SyntheticCaptions Captions { get; init; } = SyntheticCaptions.EverySecond;

    public bool WithSuperimpose { get; init; } = true;

    public int ProgramNumber { get; init; } = SomeProgramNumber;

    public TimeSpan Length { get; init; } = DefaultLength;

    public string Programme { get; init; } = FfmpegProgramme.Default;

    private bool CarriesSideInformation => WithCaptions || WithSuperimpose;

    public static SyntheticBroadcast AsMeasured() => new();

    public static SyntheticBroadcast Of(SyntheticPicture picture) => new() { Picture = picture };

    public static SyntheticBroadcast Sounding(SyntheticSound sound) => new() { Sound = sound };

    public static IReadOnlyList<string> EncodingArguments(string source, string destination, int programNumber)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        return
        [
            .. Preamble(),
            "-i",
            source,
            "-map",
            Invariant($"p:{programNumber}:v:0"),
            "-map",
            Invariant($"p:{programNumber}:a"),
            "-vf",
            "bwdif=mode=send_frame",
            "-c:v",
            "libx264",
            "-preset",
            "veryfast",
            "-crf",
            "23",
            "-c:a",
            "copy",
            "-f",
            "mp4",
            "-movflags",
            "+faststart",
            destination,
        ];
    }

    public static Task EncodeAsH264Async(
        string source,
        string destination,
        int programNumber = SomeProgramNumber,
        string programme = FfmpegProgramme.Default,
        CancellationToken cancellationToken = default)
        => FfmpegProgramme.RunAsync(programme, EncodingArguments(source, destination, programNumber), cancellationToken);

    public byte[] SideInformation()
    {
        List<byte[]> streams = [];

        if (WithCaptions)
        {
            streams.Add(PmtWriter.Stream(
                PmtWriter.PrivateData,
                CaptionPid,
                DescriptorWriter.Loop(
                    PsiDescriptorWriter.StreamIdentifier(PsiDescriptorWriter.FirstCaptionComponentTag),
                    PsiDescriptorWriter.DataComponent(PsiDescriptorWriter.CaptionDataComponentId, 0x3D))));
        }

        if (WithSuperimpose)
        {
            streams.Add(PmtWriter.Stream(
                PmtWriter.PrivateData,
                SuperimposePid,
                DescriptorWriter.Loop(
                    PsiDescriptorWriter.StreamIdentifier(PsiDescriptorWriter.FirstSuperimposeComponentTag),
                    PsiDescriptorWriter.DataComponent(PsiDescriptorWriter.CaptionDataComponentId, 0x3C))));
        }

        byte[] association = new TransportStreamWriter(PatWriter.Pid)
            .Sections(PatWriter.Section(SideTransportStreamId, (ProgramNumber, PmtPid)))
            .Bytes;
        byte[] map = new TransportStreamWriter(PmtPid)
            .Sections(new PmtWriter { ProgramNumber = ProgramNumber, Streams = [.. streams] }.ToBytes())
            .Bytes;
        byte[] management = CaptionWriter.Management();

        List<byte[]> packets = [];

        for (int second = 0; second < Seconds(); second++)
        {
            long pts = (long)second * TicksPerSecond;

            packets.Add(association);
            packets.Add(map);

            if (WithCaptions)
            {
                packets.Add(PesWriter.Packets(
                    CaptionPid,
                    PesWriter.PrivateStream(pts, CaptionWriter.Carried(CaptionWriter.CaptionDataIdentifier, management))));

                if (Caption(second) is { } statement)
                {
                    packets.Add(PesWriter.Packets(
                        CaptionPid,
                        PesWriter.PrivateStream(
                            pts + (TicksPerSecond / 2),
                            CaptionWriter.Carried(CaptionWriter.CaptionDataIdentifier, CaptionWriter.Statement(statement)))));
                }
            }

            if (WithSuperimpose)
            {
                packets.Add(PesWriter.Packets(
                    SuperimposePid,
                    PesWriter.PrivateStream(pts, CaptionWriter.Carried(CaptionWriter.SuperimposeDataIdentifier, management))));
            }
        }

        return [.. packets.SelectMany(packet => packet)];
    }

    public IReadOnlyList<string> Arguments(string? sideInformation, string? sound, string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (CarriesSideInformation != sideInformation is not null)
        {
            throw new ArgumentException(
                "The side information file is handed over exactly when captions or superimposition are asked for.",
                nameof(sideInformation));
        }

        if (Sound is SyntheticSound.DualMono != sound is not null)
        {
            throw new ArgumentException(
                "A sound file is handed over exactly when the sound cannot be encoded on the way.",
                nameof(sound));
        }

        List<string> arguments = [.. Preamble()];
        int inputs = 0;
        int? picture = null;
        List<int> sounds = [];
        int? side = null;

        if (Picture is not SyntheticPicture.None)
        {
            arguments.AddRange(["-f", "lavfi", "-i", Invariant($"testsrc2=size={Size()}:rate={BroadcastRate}")]);
            picture = inputs++;
        }

        if (sound is not null)
        {
            arguments.AddRange(["-f", "aac", "-i", sound]);
            sounds.Add(inputs++);
        }
        else
        {
            arguments.AddRange(["-f", "lavfi", "-i", Tone(440)]);
            sounds.Add(inputs++);

            if (Sound is SyntheticSound.TwoLanguages)
            {
                arguments.AddRange(["-f", "lavfi", "-i", Tone(880)]);
                sounds.Add(inputs++);
            }
        }

        if (sideInformation is not null)
        {
            arguments.AddRange(["-i", sideInformation]);
            side = inputs++;
        }

        arguments.AddRange(["-t", Length.TotalSeconds.ToString(CultureInfo.InvariantCulture)]);

        if (picture is { } video)
        {
            arguments.AddRange(["-map", Invariant($"{video}:v")]);
        }

        foreach (int held in sounds)
        {
            arguments.AddRange(["-map", Invariant($"{held}:a")]);
        }

        if (side is { } carried)
        {
            if (WithCaptions)
            {
                arguments.AddRange(["-map", Invariant($"{carried}:s:0"), "-c:s", "copy"]);
            }

            if (WithSuperimpose)
            {
                arguments.AddRange(["-map", Invariant($"{carried}:d:0"), "-c:d", "copy"]);
            }
        }

        if (picture is not null)
        {
            arguments.AddRange(
            [
                "-vf",
                "setfield=tff",
                "-c:v",
                "mpeg2video",
                "-flags",
                "+ilme+ildct+bitexact",
                "-alternate_scan",
                "1",
                "-aspect",
                "16:9",
                "-b:v",
                Bitrate(),
            ]);
        }

        arguments.AddRange(SoundEncoding());
        arguments.AddRange(
        [
            "-f",
            "mpegts",
            "-mpegts_m2ts_mode",
            "0",
            "-mpegts_service_id",
            ProgramNumber.ToString(CultureInfo.InvariantCulture),
            "-mpegts_pmt_start_pid",
            Invariant($"0x{PmtPid:x}"),
            "-mpegts_start_pid",
            Invariant($"0x{FirstElementaryPid:x}"),
            output,
        ]);

        return arguments;
    }

    public async Task<string> WriteAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        DirectoryInfo held = Directory.CreateTempSubdirectory("carina-synthetic-");

        try
        {
            string? side = null;
            string? sound = null;

            if (CarriesSideInformation)
            {
                side = Path.Combine(held.FullName, "side.ts");
                await File.WriteAllBytesAsync(side, SideInformation(), cancellationToken);
            }

            if (Sound is SyntheticSound.DualMono)
            {
                sound = Path.Combine(held.FullName, "dual-mono.aac");
                await File.WriteAllBytesAsync(sound, DualMonoAdts.Silence(Length), cancellationToken);
            }

            await FfmpegProgramme.RunAsync(Programme, Arguments(side, sound, path), cancellationToken);

            return path;
        }
        finally
        {
            held.Delete(recursive: true);
        }
    }

    private static IReadOnlyList<string> Preamble()
        =>
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-fflags",
            "+bitexact",
            "-flags",
            "+bitexact",
        ];

    private static string Tone(int hertz) => Invariant($"sine=frequency={hertz}:sample_rate={DualMonoAdts.SampleRate}");

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    private byte[]? Caption(int second)
        => Captions switch
        {
            SyntheticCaptions.ShownThenCleared when second == CaptionShownAtSecond => Kanji(),
            SyntheticCaptions.ShownThenCleared when second == CaptionClearedAtSecond => [CaptionWriter.ClearScreen],
            SyntheticCaptions.ShownThenCleared => null,
            _ => second % 2 is 0 ? Kanji() : Positioned(new AribTextWriter().DesignateAlphanumericToG0().Ascii("CARINA")),
        };

    private static byte[] Kanji() => Positioned(new AribTextWriter().Kanji("合成字幕"));

    private static byte[] Positioned(AribTextWriter text) => CaptionWriter.Positioned(CaptionRow, CaptionColumn, text);

    private int Seconds() => Math.Max(1, (int)Math.Ceiling(Length.TotalSeconds));

    private string Size()
        => Picture switch
        {
            SyntheticPicture.BroadcastHd => "1440x1080",
            SyntheticPicture.StandardDefinition => "720x480",
            SyntheticPicture.FullHd => "1920x1080",
            _ => throw new InvalidOperationException("A broadcast without a picture has no size."),
        };

    private string Bitrate()
        => Picture switch
        {
            SyntheticPicture.StandardDefinition => "3M",
            SyntheticPicture.FullHd => "8M",
            _ => "6M",
        };

    private IReadOnlyList<string> SoundEncoding()
        => Sound switch
        {
            SyntheticSound.Stereo => ["-c:a", "aac", "-ac", "2"],
            SyntheticSound.Mono => ["-c:a", "aac", "-ac", "1"],
            SyntheticSound.Surround => ["-af", "aformat=channel_layouts=5.1", "-c:a", "aac"],
            SyntheticSound.TwoLanguages =>
            [
                "-c:a",
                "aac",
                "-ac",
                "2",
                "-metadata:s:a:0",
                "language=jpn",
                "-metadata:s:a:1",
                "language=eng",
            ],
            SyntheticSound.DualMono => ["-c:a", "copy"],
            _ => throw new InvalidOperationException("Sound arrives in one of the modes named here."),
        };
}
