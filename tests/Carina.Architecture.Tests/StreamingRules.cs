using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class StreamingRules
{
    public const string OpeningTheDriversStream = "OpenSessionStreamAsync";

    public const string TheViewersSeat = "ViewerSubscriber";

    public const int MarksThatMakeAParserHere = 1;

    public static readonly IReadOnlyList<string> FeatureFolders = ["/Streaming/", "/Live/", "/Playback/", "/Videos/"];

    public static readonly IReadOnlyList<string> FeatureNamespaces =
    [
        "Carina.Domain.Streaming",
        "Carina.Domain.Playback",
        "Carina.Api.Live",
        "Carina.Api.Playback",
        "Carina.Api.Controllers.Videos",
        "Carina.Api.Responder.Playback",
    ];

    public static readonly IReadOnlyList<string> WhereTheRefusalWireIsLaidOut =
    [
        "/Carina.Domain/Streaming/LiveRefusalReport.cs",
        "/Carina.Domain/Streaming/LiveRefusalDetail.cs",
    ];

    public static readonly IReadOnlyList<string> WhereTheWholeApplicationIsWired =
        ["/Program.cs", "ServiceCollectionExtensions.cs"];

    public static readonly IReadOnlyList<string> WritersOfWhatIsNotStreamings =
    [
        "IRecordingRepository",
        "RecordingRepository",
        "RecordingRound",
        "IRecordingFileEraser",
        "DriverRecordingFileEraser",
        "ProgrammeWriter",
        "TunerLedgerService",
        "ScanApplier",
        "CarinaDbContext",
    ];

    public static readonly IReadOnlyList<string> VerbsThatWrite =
    [
        "AddAsync",
        "AddRangeAsync",
        "SaveAsync",
        "SaveChangesAsync",
        "RemoveAsync",
        "RemoveRangeAsync",
        "ForgetAsync",
        "ForgetEverythingAsync",
        "HaltAsync",
        "DiscardAsync",
        "EraseRecordingAsync",
        "ReplaceTunerLedgerAsync",
        "ToggleTunerAsync",
    ];

    public static readonly IReadOnlyList<string> OtherSeatsAndThePathByHand =
    [
        "SurveySubscriber",
        "PiggybackSubscriber",
        "SubscriberQuery",
        "SessionStream(",
        "\"/stream\"",
        "/stream?",
    ];

    private static readonly Regex NamesAWriter = new(
        string.Join('|', WritersOfWhatIsNotStreamings.Select(name => @"\b" + name + @"\b")),
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex CallsAVerbThatWrites = new(
        @"\.\s*(" + string.Join('|', VerbsThatWrite) + @")\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    public static IReadOnlyList<string> FilesInTheFeature(string directory)
        => Feature(directory)
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatTakesTheStreamApartInsideTheFeature(string directory)
        => Feature(directory)
            .SelectMany(file => MeasurementRules.Marks
                .Where(mark => mark.IsMatch(file.Source))
                .Select(mark => mark.Match(file.Source).Value)
                .Concat(CountsTheStream().Matches(file.Source).Select(match => match.Value))
                .Select(found => $"{file.Relative} {found}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> FilesOpeningTheDriversStream(string directory)
        => Feature(directory)
            .Where(file => TimesTheDriversStreamIsOpenedIn(file.Source) > 0)
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static int TimesTheDriversStreamIsOpenedIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return OpensTheStream().Matches(source).Count;
    }

    public static IReadOnlyList<string> WhatLaysOutARefusalOutsideItsOwnFiles(string directory)
        => Feature(directory)
            .Where(file => !WhereTheRefusalWireIsLaidOut.Contains(file.Relative, StringComparer.Ordinal))
            .SelectMany(file => WhatLaysOutARefusalIn(file.Source).Select(way => $"{file.Relative} {way}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatLaysOutARefusalIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            .. LaysOutARefusal().Matches(source)
                .Select(match => Squeezed(match.Value))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    public static IReadOnlyList<string> WhatAsksForAnotherSeatInsideTheFeature(string directory)
        => Feature(directory)
            .SelectMany(file => OtherSeatsAndThePathByHand
                .Where(seat => file.Source.Contains(seat, StringComparison.Ordinal))
                .Select(seat => $"{file.Relative} {seat}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatWritesWhatIsNotItsOwnInsideTheFeature(string directory)
        => Feature(directory)
            .SelectMany(file => WhatWritesWhatIsNotItsOwnIn(file.Source).Select(way => $"{file.Relative} {way}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatWaitsWithoutADeadlineInsideTheFeature(string directory)
        => Feature(directory)
            .SelectMany(file => WhatWaitsWithoutADeadlineIn(file.Source).Select(way => $"{file.Relative} {way}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatWaitsWithoutADeadlineIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            .. WaitsOnAPromiseForever().Matches(source)
                .Concat(WaitsOnAPromiseWithNothingButAToken().Matches(source))
                .Select(match => Squeezed(match.Value))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    public static IReadOnlyList<string> WhatWritesWhatIsNotItsOwnIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            .. NamesAWriter.Matches(source)
                .Concat(CallsAVerbThatWrites.Matches(source))
                .Concat(ReachesTheStore().Matches(source))
                .Concat(WritesSql().Matches(source))
                .Concat(ChangesWhatIsOnDisk().Matches(source))
                .Select(match => Squeezed(match.Value))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    public static bool BelongsToTheFeature(string relative, string source)
    {
        ArgumentNullException.ThrowIfNull(relative);
        ArgumentNullException.ThrowIfNull(source);

        return FeatureFolders.Any(folder => relative.Contains(folder, StringComparison.Ordinal))
               || (FeatureNamespaces.Any(space => source.Contains(space, StringComparison.Ordinal))
                   && !WiresTheWholeApplication(relative));
    }

    private static bool WiresTheWholeApplication(string relative)
        => WhereTheWholeApplicationIsWired.Any(root => relative.EndsWith(root, StringComparison.Ordinal));

    private static IEnumerable<SourceFile> Feature(string directory)
        => Scanned(directory).Where(file => BelongsToTheFeature(file.Relative, file.Source));

    private static string Squeezed(string matched) => Spaces().Replace(matched, string.Empty);

    private static IEnumerable<SourceFile> Scanned(string directory)
        => Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Select(file => new SourceFile(
                "/" + Path.GetRelativePath(directory, file).Replace('\\', '/'),
                File.ReadAllText(file)));

    private static bool IsBuildOutput(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal) || segments.Contains("bin", StringComparer.Ordinal);
    }

    [GeneratedRegex(@"\bContinuityCounter\w*|\bScrambling\w*|\bcontinuity_counter\b|\btransport_scrambling_control\b")]
    private static partial Regex CountsTheStream();

    [GeneratedRegex(@"\bOpenSessionStreamAsync\s*\(")]
    private static partial Regex OpensTheStream();

    [GeneratedRegex(
        @"\bDbContext\b|\bDbSet\s*<|\bSaveChanges\w*\s*\(|\bExecuteDelete\w*|\bExecuteUpdate\w*"
        + @"|\bExecuteSql\w*|\bFromSql\w*|\bEntityEntry\b|\.\s*Entry\s*\(")]
    private static partial Regex ReachesTheStore();

    [GeneratedRegex(@"(?i:\bINSERT\s+INTO\b|\bUPDATE\s+\w+\s+SET\b|\bDELETE\s+FROM\b|\bTRUNCATE\s+TABLE\b|\bALTER\s+TABLE\b)")]
    private static partial Regex WritesSql();

    [GeneratedRegex(
        @"\bFile\s*\.\s*(Delete|Create|CreateText|CreateSymbolicLink|Move|Replace|Copy|OpenWrite|AppendText"
        + @"|WriteAll\w*|AppendAll\w*|SetAttributes|SetUnixFileMode|SetLastWrite\w*)\b"
        + @"|\bDirectory\s*\.\s*(Delete|Move|CreateDirectory|CreateSymbolicLink|CreateTempSubdirectory)\b"
        + @"|\bFileMode\s*\.\s*(Create|CreateNew|OpenOrCreate|Truncate|Append)\b"
        + @"|\bnew\s+(FileStream|StreamWriter|BinaryWriter)\b"
        + @"|\bunlink\b|\bftruncate\b")]
    private static partial Regex ChangesWhatIsOnDisk();

    [GeneratedRegex(@"await\s+[A-Za-z_][\w.]*\.Task\b(?!\s*\.\s*WaitAsync\s*\()")]
    private static partial Regex WaitsOnAPromiseForever();

    [GeneratedRegex(@"\.Task\s*\.\s*WaitAsync\s*\(\s*[^(),]*\s*\)")]
    private static partial Regex WaitsOnAPromiseWithNothingButAToken();

    [GeneratedRegex(
        @"\(\s*byte\s*\)\s*(LiveRefusal|TuneFailureKind|LiveTunerHolder)\s*\."
        + @"|\bLiveRefusalReport\s*\.\s*PayloadLength\b")]
    private static partial Regex LaysOutARefusal();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private readonly record struct SourceFile(string Relative, string Source);
}
