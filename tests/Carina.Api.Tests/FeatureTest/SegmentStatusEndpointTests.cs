using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Domain.Segments;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class SegmentStatusFeature : IAsyncDisposable
{
    public const string Path = "/api/segments/status";

    private readonly TestingWebApplicationFactory factory = new();

    public SegmentStatusFeature()
    {
        Built = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<ILearningDataAmountReader>(LearningData);
        }));

        Client = Built.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");
    }

    public WebApplicationFactory<Program> Built { get; }

    public HttpClient Client { get; }

    public HeldLearningDataAmount LearningData { get; } = new();

    public HttpClient Anonymous() => Built.WithTestScheme().CreateClient();

    public async Task<(HttpStatusCode Status, JsonElement LearningData)> GetAsync()
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(Path, UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.GetProperty("data").GetProperty("learningData").Clone());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }
}

public sealed class SegmentStatusEndpointTests
{
    [Fact(DisplayName = "with no learning data kept every figure of it is zero, and only the figures are answered")]
    public async Task WithNoLearningDataKeptEveryFigureIsZero()
    {
        await using var feature = new SegmentStatusFeature();

        (HttpStatusCode status, JsonElement learningData) = await feature.GetAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["bytes", "recordings", "seconds", "waiting"],
            learningData.EnumerateObject().Select(figure => figure.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(0, learningData.GetProperty("recordings").GetInt32());
        Assert.Equal(0, learningData.GetProperty("seconds").GetInt64());
        Assert.Equal(0, learningData.GetProperty("bytes").GetInt64());
        Assert.Equal(0, learningData.GetProperty("waiting").GetInt32());
        Assert.Equal(1, feature.LearningData.Reads);
    }

    [Fact(DisplayName = "the learning data kept is answered as recordings, whole seconds read and the bytes the tables take")]
    public async Task TheLearningDataKeptIsAnsweredAsRecordingsSecondsAndBytes()
    {
        await using var feature = new SegmentStatusFeature();
        feature.LearningData.Amount = new LearningDataAmount(3, TimeSpan.FromSeconds(5_400.75), 5_000_000_123, 0);

        (HttpStatusCode status, JsonElement learningData) = await feature.GetAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, learningData.GetProperty("recordings").GetInt32());
        Assert.Equal(5_400, learningData.GetProperty("seconds").GetInt64());
        Assert.Equal(5_000_000_123, learningData.GetProperty("bytes").GetInt64());
        Assert.Equal(0, learningData.GetProperty("waiting").GetInt32());
    }

    [Fact(DisplayName = "recordings still waiting to be read are answered beside the learning data kept")]
    public async Task RecordingsStillWaitingToBeReadAreAnswered()
    {
        await using var feature = new SegmentStatusFeature();
        feature.LearningData.Amount = new LearningDataAmount(1, TimeSpan.FromMinutes(30), 4_096, 12);

        (HttpStatusCode status, JsonElement learningData) = await feature.GetAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, learningData.GetProperty("recordings").GetInt32());
        Assert.Equal(1_800, learningData.GetProperty("seconds").GetInt64());
        Assert.Equal(12, learningData.GetProperty("waiting").GetInt32());
    }

    [Fact(DisplayName = "a caller who has not signed in cannot read the status, and nothing is counted for them")]
    public async Task ACallerWhoHasNotSignedInCannotReadTheStatus()
    {
        await using var feature = new SegmentStatusFeature();
        using HttpClient anonymous = feature.Anonymous();

        using HttpResponseMessage response = await anonymous.GetAsync(new Uri(SegmentStatusFeature.Path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(0, feature.LearningData.Reads);
    }
}
