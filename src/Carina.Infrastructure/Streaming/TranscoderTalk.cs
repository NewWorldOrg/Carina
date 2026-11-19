using System.Text.RegularExpressions;

namespace Carina.Infrastructure.Streaming;

public readonly record struct TranscoderLine(string? Kept, bool InputRead);

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
