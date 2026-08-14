using System.Net.Http.Json;
using System.Net.Sockets;

using Carina.Contracts;
using Carina.Driver.Configuration;

namespace Carina.Driver.Ipc;

public sealed record ProbeVerdict(bool Healthy, string Reason);

public static class DriverProbe
{
    public const int HealthyExitCode = 0;

    public const int UnhealthyExitCode = 1;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);

    public static ProbeVerdict Judge(DriverHello? hello, IReadOnlyList<TunerSnapshot>? tuners)
    {
        if (hello is null)
        {
            return new ProbeVerdict(false, "the driver did not answer on its socket.");
        }

        if (hello.Draining)
        {
            return new ProbeVerdict(
                false,
                "draining: the driver is finishing the recordings it holds and refuses new sessions."
            );
        }

        if (tuners is null || tuners.Count is 0)
        {
            return new ProbeVerdict(false, "the driver declares no tuner.");
        }

        var enabled = tuners
            .Where(tuner => tuner.State is not (TunerState.Disabled or TunerState.Draining))
            .ToList();

        if (enabled.Count is 0)
        {
            return new ProbeVerdict(false, $"all {tuners.Count} tuners are disabled.");
        }

        var faulted = enabled.Where(tuner => tuner.State is TunerState.Faulted).ToList();

        if (faulted.Count == enabled.Count)
        {
            return new ProbeVerdict(
                false,
                $"every usable tuner is faulted: {Name(faulted)}."
            );
        }

        if (faulted.Count > 0)
        {
            return new ProbeVerdict(
                true,
                $"serving with {enabled.Count - faulted.Count} of {enabled.Count} tuners; faulted: {Name(faulted)}."
            );
        }

        return new ProbeVerdict(true, $"serving with {enabled.Count} tuners.");
    }

    public static async Task<int> RunAsync(
        DriverConfiguration configuration,
        TextWriter output,
        TimeSpan? timeout = null
    )
    {
        var verdict = await AskAsync(configuration, timeout ?? DefaultTimeout);

        await output.WriteLineAsync(verdict.Reason);

        return verdict.Healthy ? HealthyExitCode : UnhealthyExitCode;
    }

    public static async Task<ProbeVerdict> AskAsync(
        DriverConfiguration configuration,
        TimeSpan timeout
    )
    {
        var path = configuration.SocketPath;

        if (string.IsNullOrEmpty(path))
        {
            return new ProbeVerdict(false, "socketPath: the configuration names no socket.");
        }

        try
        {
            using var client = ClientFor(path, timeout);

            var hello = await client.GetFromJsonAsync(
                DriverEndpoints.Health,
                DriverJson.Context.DriverHello
            );

            var tuners = await client.GetFromJsonAsync(
                DriverEndpoints.Tuners,
                DriverJson.Context.IReadOnlyListTunerSnapshot
            );

            return Judge(hello, tuners);
        }
        catch (Exception failure)
        {
            return new ProbeVerdict(
                false,
                $"the driver at '{path}' did not answer: {failure.Message}"
            );
        }
    }

    private static HttpClient ClientFor(string path, TimeSpan timeout) =>
        new(
            new SocketsHttpHandler
            {
                ConnectCallback = async (_, cancellationToken) =>
                {
                    var socket = new Socket(
                        AddressFamily.Unix,
                        SocketType.Stream,
                        ProtocolType.Unspecified
                    );

                    await socket.ConnectAsync(
                        new UnixDomainSocketEndPoint(path),
                        cancellationToken
                    );

                    return new NetworkStream(socket, ownsSocket: true);
                },
            }
        )
        {
            BaseAddress = new Uri("http://localhost"),
            Timeout = timeout,
        };

    private static string Name(IReadOnlyList<TunerSnapshot> tuners) =>
        string.Join(", ", tuners.Select(tuner => tuner.DeviceId));
}
