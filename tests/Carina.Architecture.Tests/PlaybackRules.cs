using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static class PlaybackRules
{
    public const string FeatureFolder = "/Playback/";

    public const string DeliveryPath = "/api/videos";

    public const string DeliveryEndpoint = "VideoDelivery";

    public static readonly IReadOnlyList<string> WaysToTranscodeWhilePlaying =
    [
        "ILiveTranscoder",
        "ILiveTranscoderFactory",
        "LiveTranscoder",
        "LiveTranscoderFactory",
        "LiveTranscoderStart",
        "LiveTranscodeSettings",
        "FfmpegLiveInvocation",
        "FfmpegPlaybackInvocation",
        "IOnTheFlyPlayer",
        "IOnTheFlyViewing",
        "OnTheFlyPlayer",
        "OnTheFlyStart",
        "OnTheFlyViewing",
        "OnTheFlySettings",
        "TranscoderProcess",
    ];

    private static readonly Regex Transcodes = new(
        string.Join('|', WaysToTranscodeWhilePlaying.Select(name => @"\b" + name + @"\b")),
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    public static IReadOnlyList<string> FilesSpellingTheDeliveryPath(string directory)
        => Reported(directory, file => file.Source.Contains(DeliveryPath, StringComparison.Ordinal));

    public static IReadOnlyList<string> FilesNamingTheDelivery(string directory)
        => Reported(directory, file => file.Source.Contains(DeliveryEndpoint, StringComparison.Ordinal));

    public static IReadOnlyList<string> WhatTheDeliveryTranscodes(string directory)
        => Reported(
            directory,
            file => file.Relative.Contains(FeatureFolder, StringComparison.Ordinal)
                    && Transcodes.IsMatch(file.Source));

    private static IReadOnlyList<string> Reported(string directory, Func<SourceFile, bool> looking)
        => Scanned(directory)
            .Where(looking)
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

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

    private readonly record struct SourceFile(string Relative, string Source);
}
