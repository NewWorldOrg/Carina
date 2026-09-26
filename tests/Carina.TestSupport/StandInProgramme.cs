using System.Diagnostics;

namespace Carina.TestSupport;

public static class StandInProgramme
{
    /// <summary>
    /// More than a pipe holds, written on the error stream, so a stand-in that begins with it cannot
    /// reach its last line before it has been identified and handed over.
    /// </summary>
    private const string MoreThanAPipeHolds = """
        head -c 70000 /dev/zero | tr '\0' '.' >&2
        printf '\n' >&2
        """;

    public static string Written(string path, string body)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(body);

        ThroughAProcessOfItsOwn(path, $"#!/bin/sh\n{body}\n");

        return path;
    }

    /// <summary>
    /// The same stand-in, made to outlive the look at it: it is held at its first line until someone
    /// reads what it said, which is after it has been handed over. Only the error stream is filled.
    /// </summary>
    public static string WrittenToOutliveTheLook(string path, string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Written(path, $"{MoreThanAPipeHolds}\n{body}");
    }

    private static void ThroughAProcessOfItsOwn(string path, string text)
    {
        var writing = new ProcessStartInfo("/bin/sh")
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        writing.ArgumentList.Add("-c");
        writing.ArgumentList.Add("cat > \"$0\" && chmod 700 \"$0\"");
        writing.ArgumentList.Add(path);

        using Process writer = Process.Start(writing)
            ?? throw new InvalidOperationException($"nothing started to write the stand-in at '{path}'.");

        Task<string> complained = writer.StandardError.ReadToEndAsync();

        writer.StandardInput.Write(text);
        writer.StandardInput.Close();
        writer.WaitForExit();

        if (writer.ExitCode is not 0)
        {
            throw new InvalidOperationException(
                $"the stand-in at '{path}' was not written: {complained.GetAwaiter().GetResult()}");
        }
    }
}
