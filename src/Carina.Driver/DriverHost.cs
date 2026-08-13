using System.Diagnostics.CodeAnalysis;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Events;
using Carina.Driver.Ipc;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Driver;

public enum DriverHostRefusal
{
    None,

    Configuration,

    Socket,
}

public sealed record DriverHostResult
{
    private DriverHostResult(
        IHost? host,
        DriverHostRefusal refusal,
        IReadOnlyList<string> problems
    )
    {
        Host = host;
        Refusal = refusal;
        Problems = problems;
    }

    public IHost? Host { get; }

    public DriverHostRefusal Refusal { get; }

    public IReadOnlyList<string> Problems { get; }

    public static DriverHostResult Serving(IHost host) =>
        new(host, DriverHostRefusal.None, []);

    public static DriverHostResult Refused(
        DriverHostRefusal refusal,
        IReadOnlyList<string> problems
    ) => new(null, refusal, problems);

    public bool TryGetHost([NotNullWhen(true)] out IHost? host)
    {
        host = Host;

        return host is not null;
    }
}

public static class DriverHost
{
    public static DriverHostResult Create(string[] args, DriverConfiguration configuration)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        var wouldBindTcp = TcpBindingGate.Inspect(builder.Configuration, args);
        if (wouldBindTcp.Count > 0)
        {
            return DriverHostResult.Refused(DriverHostRefusal.Configuration, wouldBindTcp);
        }

        var socketPath = configuration.SocketPath!;

        try
        {
            DriverSocket.ClearStale(socketPath);
        }
        catch (DriverSocketException error)
        {
            return DriverHostResult.Refused(DriverHostRefusal.Socket, [error.Message]);
        }

        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.ListenUnixSocket(socketPath);
        });

        builder.Services.Configure<HostOptions>(options =>
            options.ShutdownTimeout =
                TimeSpan.FromHours(configuration.ShutdownGraceHours) + TimeSpan.FromMinutes(1)
        );
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, DriverJson.Context)
        );

        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(DriverGreeting.ForThisProcess());
        builder.Services.AddSingleton<ITunerDeviceFactory, TunerDeviceFactory>();
        builder.Services.AddSingleton<DriverEventHub>();
        builder.Services.AddSingleton(provider => new TunerSessionManager(
            provider.GetRequiredService<DriverConfiguration>(),
            provider.GetRequiredService<ITunerDeviceFactory>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<TunerSessionManager>>(),
            events: provider.GetRequiredService<DriverEventHub>()
        ));

        builder.Services.AddHostedService<DriverEventHubService>();
        builder.Services.AddHostedService(provider =>
            provider.GetRequiredService<TunerSessionManager>()
        );
        builder.Services.AddHostedService<SocketPermissionGuard>();

        var app = builder.Build();

        DriverApi.Map(app);

        return DriverHostResult.Serving(app);
    }
}
