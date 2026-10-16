using System.Text.RegularExpressions;

namespace Carina.Infrastructure.Streaming;

public readonly record struct TranscoderLine(string? Kept, bool InputRead);

/// <summary>
/// Reads what ffmpeg writes to standard error one line at a time. A line ffmpeg marked with how serious it is
/// is kept, without the mark, only when it is an error or worse; a line it did not mark goes with the line before
/// it, so a programme asked for its errors alone has every line kept.
/// </summary>
public sealed partial class TranscoderTalk
{
    private const string WhatTheInputHolds = "Input #0";

    private static readonly string[] Errors = ["panic", "fatal", "error"];

    private bool keeping = true;

    public TranscoderLine Hear(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Match marked = Marked().Match(line);

        if (!marked.Success)
        {
            return new TranscoderLine(keeping ? line : null, false);
        }

        string level = marked.Groups["level"].Value;
        string said = marked.Groups["said"].Value;

        keeping = Errors.Contains(level, StringComparer.Ordinal);

        return new TranscoderLine(
            keeping ? marked.Groups["context"].Value + said : null,
            string.Equals(level, "info", StringComparison.Ordinal) && said.StartsWith(WhatTheInputHolds, StringComparison.Ordinal));
    }

    [GeneratedRegex(
        @"^(?<context>\[[^\]]+ @ 0x[0-9a-f]+\] )?\[(?<level>panic|fatal|error|warning|info|verbose|debug|trace)\] (?<said>.*)$",
        RegexOptions.None,
        5000)]
    private static partial Regex Marked();
}
