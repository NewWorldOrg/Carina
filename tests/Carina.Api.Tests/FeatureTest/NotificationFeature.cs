using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

using Carina.Api.Authentication;
using Carina.Domain.Base;
using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

internal sealed record HeardWebhookCall(string Method, string Target, string Body);

internal sealed class WebhookEnd : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);

    private readonly CancellationTokenSource stopping = new();

    private readonly Task accepting;

    public WebhookEnd()
    {
        Certificate = SelfSigned("hooks.slack.com", "discord.com", "discordapp.com");
        listener.Start();
        accepting = AcceptAsync();
    }

    public X509Certificate2 Certificate { get; }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public ConcurrentQueue<HeardWebhookCall> Heard { get; } = new();

    public int Status { get; set; } = StatusCodes.Status204NoContent;

    public string AnswerHeaders { get; set; } = string.Empty;

    public string AnswerBody { get; set; } = string.Empty;

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();

        try
        {
            await accepting;
        }
        catch (Exception refused) when (refused is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        stopping.Dispose();
        Certificate.Dispose();
    }

    private static X509Certificate2 SelfSigned(string host, params string[] alsoNamed)
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new($"CN={host}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        SubjectAlternativeNameBuilder names = new();
        names.AddDnsName(host);

        foreach (string other in alsoNamed)
        {
            names.AddDnsName(other);
        }

        request.CertificateExtensions.Add(names.Build());

        using X509Certificate2 made = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        return X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pfx), null);
    }

    private async Task AcceptAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient caller = await listener.AcceptTcpClientAsync(stopping.Token);

            _ = Task.Run(() => ServeAsync(caller));
        }
    }

    private async Task ServeAsync(TcpClient caller)
    {
        try
        {
            using TcpClient held = caller;
            await using SslStream secured = new(held.GetStream(), leaveInnerStreamOpen: false);
            await secured.AuthenticateAsServerAsync(Certificate, false, false);

            string head = await HeadAsync(secured);
            string[] lines = head.Split("\r\n");
            string[] first = lines[0].Split(' ');
            int length = lines
                .Select(line => line.Split(": ", 2))
                .Where(pair => pair.Length == 2 && string.Equals(pair[0], HeaderNames.ContentLength, StringComparison.OrdinalIgnoreCase))
                .Select(pair => int.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture))
                .FirstOrDefault();
            byte[] body = new byte[length];
            await secured.ReadExactlyAsync(body, stopping.Token);
            Heard.Enqueue(new HeardWebhookCall(first[0], first[1], Encoding.UTF8.GetString(body)));

            byte[] answered = Encoding.UTF8.GetBytes(AnswerBody);
            string opening =
                $"HTTP/1.1 {Status} Answered\r\nConnection: close\r\nContent-Length: {answered.Length}\r\n{AnswerHeaders}\r\n";
            await secured.WriteAsync(Encoding.ASCII.GetBytes(opening), stopping.Token);
            await secured.WriteAsync(answered, stopping.Token);
            await secured.FlushAsync(stopping.Token);
        }
        catch (Exception broken) when (broken is IOException or OperationCanceledException or AuthenticationException)
        {
        }
    }

    private async Task<string> HeadAsync(SslStream secured)
    {
        StringBuilder head = new();
        byte[] one = new byte[1];

        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            await secured.ReadExactlyAsync(one, stopping.Token);
            head.Append((char)one[0]);
        }

        return head.ToString();
    }
}

internal sealed class PointedNoticeNetwork(WebhookEnd end) : INoticeNetwork
{
    public static readonly IPAddress Public = IPAddress.Parse("203.0.113.40");

    public ConcurrentQueue<IPEndPoint> Connected { get; } = new();

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<IPAddress>>([Public]);

    public async Task<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        Connected.Enqueue(endpoint);

        Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, end.Port), cancellationToken);

        return new NetworkStream(socket, ownsSocket: true);
    }

    public bool Trusts(X509Certificate? certificate, SslPolicyErrors errors)
        => certificate is not null && certificate.GetCertHashString() == end.Certificate.GetCertHashString();
}

internal sealed class NotificationFeature : IAsyncDisposable
{
    public const string SlackUrl = "https://hooks.slack.com/services/T0000/B0000/made-up-token-wxyz";

    public const string DiscordUrl = "https://discord.com/api/webhooks/1234/made-up-token-abcd";

    public const string Secret = "made-up-token";

    public const string Destinations = "/api/notifications/destinations";

    public const string Rules = "/api/notifications/rules";

    public const string Deliveries = "/api/notifications/deliveries";

    public const string Settings = "/api/notifications/settings";

    public static readonly DateTime Noon = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public NotificationFeature(string? publicOrigin = null, RecordedStartup? heard = null)
    {
        Network = new PointedNoticeNetwork(End);
        Built = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(PublicOrigin.Key, publicOrigin ?? string.Empty);
            Listening(builder, heard);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<INotificationDestinationRepository>(HeldDestinations);
                services.AddSingleton<INotificationRuleRepository>(HeldRules);
                services.AddSingleton<INotificationDeliveryRepository>(HeldDeliveries);
                services.AddSingleton<INotificationSettingsRepository>(HeldSettings);
                services.AddSingleton<IAtomicWrite, UnguardedWrites>();
                services.AddSingleton<INoticeNetwork>(Network);

                if (heard is not null)
                {
                    services.AddSingleton<ILoggerProvider>(heard);
                }
            });
        });

        Client = Signed();
    }

    public WebApplicationFactory<Program> Built { get; }

    public HttpClient Client { get; }

    public MovingClock Clock { get; } = new(Noon);

    public WebhookEnd End { get; } = new();

    public PointedNoticeNetwork Network { get; }

    public HeldNotificationDestinations HeldDestinations { get; } = new();

    public HeldNotificationRules HeldRules { get; } = new();

    public HeldNotificationDeliveries HeldDeliveries { get; } = new();

    public HeldNotificationSettings HeldSettings { get; } = new();

    public static string OneOf(Guid id) => $"{Destinations}/{id}";

    public static string TrialOf(Guid id) => $"{Destinations}/{id}/test";

    public static string RuleOf(Guid id) => $"{Rules}/{id}";

    public HttpClient Anonymous() => Built.WithTestScheme().CreateClient();

    public HttpClient FromElsewhere()
    {
        HttpClient client = Signed();
        client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        client.DefaultRequestHeaders.Add(HeaderNames.Origin, "https://elsewhere.example");

        return client;
    }

    public HttpClient NamingNoOrigin()
    {
        HttpClient client = Signed();
        client.DefaultRequestHeaders.Remove(HeaderNames.Origin);

        return client;
    }

    public async Task<Guid> RegisteredAsync(string name = "居間", string url = SlackUrl)
    {
        (HttpStatusCode status, JsonElement body) = await PostAsync(Destinations, new { name, url });

        Assert.Equal(HttpStatusCode.Created, status);

        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

    public NoticeOnItsWay Waiting(Guid destination, NoticeKind kind = NoticeKind.RecordingFailed, DateTime? occurredAt = null)
    {
        DateTime at = occurredAt ?? Clock.Now;
        Notice notice = Notice.Take(
            NoticeId.New(),
            kind,
            Guid.NewGuid(),
            at,
            new NoticeWords("録画に失敗しました", "作り物の番組", null),
            at);
        NoticeOnItsWay row = new(
            NotificationDelivery.Queue(NotificationDeliveryId.New(), notice.Id, new NotificationDestinationId(destination), at),
            notice);
        HeldDeliveries.Rows.Add(row);

        return row;
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(path, UriKind.Relative));

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path, object? body = null, HttpClient? client = null)
    {
        using HttpResponseMessage response = await (client ?? Client).PostAsJsonAsync(new Uri(path, UriKind.Relative), body ?? new { });

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(string path, object body, HttpClient? client = null)
    {
        using HttpResponseMessage response = await (client ?? Client).PutAsJsonAsync(new Uri(path, UriKind.Relative), body);

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> PatchAsync(string path, object body, HttpClient? client = null)
    {
        using HttpResponseMessage response = await (client ?? Client).PatchAsJsonAsync(new Uri(path, UriKind.Relative), body);

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> DeleteAsync(string path, HttpClient? client = null)
    {
        using HttpRequestMessage asking = new(HttpMethod.Delete, new Uri(path, UriKind.Relative));
        using HttpResponseMessage response = await (client ?? Client).SendAsync(asking);

        return await ReadAsync(response);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
        await End.DisposeAsync();
    }

    private static void Listening(IWebHostBuilder builder, RecordedStartup? heard)
    {
        if (heard is null)
        {
            return;
        }

        string provider = typeof(RecordedStartup).FullName!;

        builder
            .UseSetting("Logging:LogLevel:Default", nameof(LogLevel.Trace))
            .UseSetting("Logging:LogLevel:Microsoft", nameof(LogLevel.Trace))
            .UseSetting("Logging:LogLevel:Microsoft.AspNetCore", nameof(LogLevel.Trace))
            .UseSetting("Logging:LogLevel:System", nameof(LogLevel.Trace))
            .UseSetting($"Logging:{provider}:LogLevel:Default", nameof(LogLevel.Trace))
            .UseSetting($"Logging:{provider}:LogLevel:Microsoft", nameof(LogLevel.Trace))
            .UseSetting($"Logging:{provider}:LogLevel:System", nameof(LogLevel.Trace));
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        if (!body.StartsWith('{') && !body.StartsWith('['))
        {
            return (response.StatusCode, default);
        }

        using JsonDocument document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.Clone());
    }

    private HttpClient Signed()
    {
        HttpClient client = Built.WithTestScheme().CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");

        return client;
    }
}
