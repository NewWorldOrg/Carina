using System.ComponentModel;
using System.Diagnostics;

namespace Carina.BroadcastTestSupport;

public static class FfmpegProgramme
{
    public const string Default = "ffmpeg";

    public static async Task RunAsync(string programme, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programme);
        ArgumentNullException.ThrowIfNull(arguments);

        var start = new ProcessStartInfo(programme)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process? started;

        try
        {
            started = Process.Start(start);
        }
        catch (Win32Exception failure)
        {
            throw new InvalidOperationException(
                $"'{programme}' is not on this machine, so no synthetic broadcast can be written here: {failure.Message}");
        }

        using Process running = started
            ?? throw new InvalidOperationException($"'{programme}' started no process of its own.");

        running.StandardInput.Close();

        Task<string> output = running.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> complaint = running.StandardError.ReadToEndAsync(cancellationToken);

        await running.WaitForExitAsync(cancellationToken);

        if (running.ExitCode is not 0)
        {
            throw new InvalidOperationException(
                $"'{programme}' exited {running.ExitCode} for [{string.Join(' ', arguments)}]: {(await complaint).Trim()}");
        }

        await output;
    }
}
