using System.Collections.Frozen;
using System.Text;

using Carina.Broadcast.DsmCc;
using Carina.Domain.DataBroadcast;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// Talks to a broadcaster's server for a document shown on a live channel, over the <see cref="RelayLine"/> that
/// connects to public addresses only. Redirects are followed on the same host only. Each exchange is written to the
/// log by its channel, host, path, method, result, status, sizes and time, never by its query, its bodies or a
/// cookie, and nothing is retried.
/// </summary>
public sealed class BroadcasterRelay : IBroadcasterRelay, IDisposable
{
    public const string Exchanged =
        "Data broadcast relay on channel {Channel}: {Method} {Host}{Path} ended {Result} with status {Status}, "
        + "{Sent} bytes sent and {Received} received in {Elapsed} ms.";

    private static readonly FrozenSet<string> EucJpNames =
        new[] { "euc-jp", "x-euc-jp" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> Utf8Names =
        new[] { "utf-8", "utf8" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly BroadcasterRelaySettings settings;

    private readonly RelayLimits limits;

    private readonly TimeProvider clock;

    private readonly ILogger<BroadcasterRelay> logger;

    private readonly RelayLine line;

    public BroadcasterRelay(
        IRelayNetwork network,
        BroadcasterRelaySettings settings,
        TimeProvider clock,
        ILogger<BroadcasterRelay> logger)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.settings = settings;
        this.clock = clock;
        this.logger = logger;
        limits = new RelayLimits(settings.AtOnce, settings.Time, settings.LargestReceived, settings.Redirects);
        line = new RelayLine(network, settings.UserAgent, clock);
    }

    public async Task<RelayedText> TransmitAsync(
        string channel,
        string uri,
        string text,
        string charset,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);
        ArgumentNullException.ThrowIfNull(text);

        long started = clock.GetTimestamp();
        TextCharset? asked = CharsetNamed(charset);
        byte[]? body = asked is not { } named || text.Length == 0 ? null : Encoded(text, named);
        HttpMethod method = body is null ? HttpMethod.Get : HttpMethod.Post;
        Uri? destination = asked is null || body?.Length > settings.LargestSent ? null : RelayDestination.Admit(uri);

        RelayExchange exchange = await line.RelayAsync(
            new RelayAsk(channel, limits, destination, method, body, charset, OnTheSameHost),
            cancellationToken);
        Write(channel, uri, method, exchange, body?.Length ?? 0, started);

        return exchange.Body is { } received && asked is { } decodedAs
            ? new RelayedText(exchange.Result, exchange.Status, Decoded(received, exchange.Type?.CharSet, decodedAs))
            : new RelayedText(exchange.Result, exchange.Status, null);
    }

    public async Task<RelayedResource> FetchAsync(string channel, string uri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);

        long started = clock.GetTimestamp();
        RelayExchange exchange = await line.RelayAsync(
            new RelayAsk(channel, limits, RelayDestination.Admit(uri), HttpMethod.Get, null, null, OnTheSameHost),
            cancellationToken);
        Write(channel, uri, HttpMethod.Get, exchange, 0, started);

        return exchange is { Body: { } received, Status: >= 200 and < 300 }
            ? new RelayedResource(exchange.Result, exchange.Status, FetchedResources.Of(exchange.At!, exchange.Type, received))
            : new RelayedResource(exchange.Result, exchange.Status, null);
    }

    public void Dispose() => line.Dispose();

    private static bool OnTheSameHost(Uri at, Uri next) => string.Equals(next.IdnHost, at.IdnHost, StringComparison.OrdinalIgnoreCase);

    private static TextCharset? CharsetNamed(string? name)
    {
        if (name is null)
        {
            return null;
        }

        if (EucJpNames.Contains(name))
        {
            return TextCharset.EucJp;
        }

        return Utf8Names.Contains(name) ? TextCharset.Utf8 : null;
    }

    private static byte[] Encoded(string text, TextCharset charset)
        => charset is TextCharset.EucJp ? EucJpText.Encode(text) : Encoding.UTF8.GetBytes(text);

    private static string Decoded(byte[] received, string? said, TextCharset asked)
    {
        TextCharset charset = CharsetNamed(said?.Trim('"')) ?? asked;

        return charset is TextCharset.EucJp ? EucJpText.Decode(received) : Encoding.UTF8.GetString(received);
    }

    private void Write(string channel, string uri, HttpMethod method, RelayExchange exchange, int sent, long started)
    {
        Uri? asked = Uri.TryCreate(uri, UriKind.Absolute, out Uri? read) ? read : null;

        logger.LogInformation(
            Exchanged,
            channel,
            method.Method,
            asked?.IdnHost ?? string.Empty,
            asked?.AbsolutePath ?? string.Empty,
            (int)exchange.Result,
            exchange.Status,
            sent,
            exchange.Body?.Length ?? 0,
            (long)clock.GetElapsedTime(started).TotalMilliseconds);
    }

    private enum TextCharset
    {
        EucJp,

        Utf8,
    }
}
