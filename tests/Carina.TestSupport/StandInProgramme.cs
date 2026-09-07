using System.Diagnostics;

namespace Carina.TestSupport;

public static class StandInProgramme
{
    public static string Written(string path, string body)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(body);

        ThroughAProcessOfItsOwn(path, $"#!/bin/sh\n{body}\n");

        return path;
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
