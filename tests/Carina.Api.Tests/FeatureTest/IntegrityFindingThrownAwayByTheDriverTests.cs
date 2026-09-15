using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text.Json;

using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Integrity;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Api.Tests.FeatureTest;

[SupportedOSPlatform("linux")]
[Collection(FeatureTestCollection.Name)]
public sealed class IntegrityFindingThrownAwayByTheDriverTests
{
    [Fact]
    public async Task AFileNoRecordingOwnsIsReallyTakenOffTheDiskByTheDriverAndNothingBesideItIsTouched()
    {
        await using SyntheticDriverHost driver = await SyntheticDriverHost.StartAsync();
        string leftover = Holding(driver, "nested/leftover.tmp", 1_000);
        string kept = Holding(driver, "kept.bin", 10);
        await using var app = new AppAgainstTheDriver(driver);
        Guid finding = await app.FoundAsync("nested/leftover.tmp");

        (HttpStatusCode status, JsonElement body) = await app.PostAsync(
            $"/api/recordings/integrity/findings/{finding}/delete");
        (_, JsonElement page) = await app.GetAsync("/api/recordings/integrity");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("data").GetProperty("fileRemoved").GetBoolean());
        Assert.False(File.Exists(leftover));
        Assert.True(File.Exists(kept));
        Assert.DoesNotContain(
            "nested/leftover.tmp",
            page.GetProperty("data").GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("path").GetString()));
    }

    [Fact]
    public async Task AFileThatChangedAfterTheCheckIsRefusedWithAConflictAndKeepsItsNewBytes()
    {
        await using SyntheticDriverHost driver = await SyntheticDriverHost.StartAsync();
        string leftover = Holding(driver, "leftover.tmp", 1_000);
        await using var app = new AppAgainstTheDriver(driver);
        Guid finding = await app.FoundAsync("leftover.tmp");
        await File.AppendAllTextAsync(leftover, "written after the check");

        (HttpStatusCode status, JsonElement body) = await app.PostAsync(
            $"/api/recordings/integrity/findings/{finding}/delete");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("fileChanged", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.True(File.Exists(leftover));
        Assert.Equal(1_023, new FileInfo(leftover).Length);
    }

    [Fact]
    public async Task NoPathCanBeSlippedInBecauseTheOnlyThingTheRouteTakesIsTheFindingId()
    {
        await using SyntheticDriverHost driver = await SyntheticDriverHost.StartAsync();
        string leftover = Holding(driver, "leftover.tmp", 1_000);
        string precious = Holding(driver, "precious.bin", 10);
        string outside = Path.GetFullPath(Path.Combine(driver.RecordingsDirectory, "..", "outside.bin"));
        await File.WriteAllBytesAsync(outside, new byte[10]);
        await using var app = new AppAgainstTheDriver(driver);
        Guid finding = await app.FoundAsync("leftover.tmp");

        (HttpStatusCode traversal, _) = await app.PostAsync(
            "/api/recordings/integrity/findings/..%2F..%2Foutside.bin/delete");

        using HttpResponseMessage carrying = await app.Client.PostAsJsonAsync(
            new Uri($"/api/recordings/integrity/findings/{finding}/delete", UriKind.Relative),
            new { path = "../outside.bin", outputRoot = SyntheticDriverHost.RootName, sizeBytes = 10 });

        Assert.Equal(HttpStatusCode.BadRequest, traversal);
        Assert.Equal(HttpStatusCode.OK, carrying.StatusCode);
        Assert.False(File.Exists(leftover));
        Assert.True(File.Exists(precious));
        Assert.True(File.Exists(outside));
    }

    private static string Holding(SyntheticDriverHost driver, string path, int size)
    {
        string full = Path.Combine(driver.RecordingsDirectory, path);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[size]);

        return full;
    }

    private sealed class AppAgainstTheDriver : IAsyncDisposable
    {
        private readonly TestingWebApplicationFactory factory;

        private readonly WebApplicationFactory<Program> configured;

        public AppAgainstTheDriver(SyntheticDriverHost driver)
        {
            var settings = new IntegritySettings
            {
                OutputRoots =
                [
                    new StorageRootPath(new OutputRoot(SyntheticDriverHost.RootName), driver.RecordingsDirectory),
                ],
            };

            factory = new TestingWebApplicationFactory { DriverSocketPath = driver.SocketPath };
            configured = factory
                .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IHostedService>();
                    services.AddSingleton(settings);
                    services.AddSingleton<IRecordingFileSurvey>(
                        new LocalRecordingFileSurvey(settings, NullLogger<LocalRecordingFileSurvey>.Instance));
                    services.AddSingleton<IRecordingRepository>(new HeldInFlightRecordings());
                    services.AddScoped<IRecordingLedger>(_ => Ledger);
                    services.AddScoped<IEncodeWorkLedger>(_ => Working);
                    services.AddScoped<IIntegrityCheckRepository>(_ => Checks);
                }))
                .WithTestScheme();

            Client = configured.CreateClient();
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                TestAuthenticationHandler.SchemeName,
                "anything");
        }

        public HttpClient Client { get; }

        public HeldLedgerFiles Ledger { get; } = new();

        public HeldEncodeWorkFiles Working { get; } = new();

        public HeldIntegrityChecks Checks { get; } = new();

        public async Task<Guid> FoundAsync(string path)
        {
            (HttpStatusCode ran, _) = await PostAsync("/api/recordings/integrity/run");
            (_, JsonElement page) = await GetAsync("/api/recordings/integrity");

            Assert.Equal(HttpStatusCode.OK, ran);

            return page.GetProperty("data").GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("path").GetString() == path)
                .GetProperty("id")
                .GetGuid();
        }

        public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
        {
            using HttpResponseMessage response = await Client.GetAsync(new Uri(path, UriKind.Relative));

            return await ReadAsync(response);
        }

        public async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path)
        {
            using HttpResponseMessage response = await Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), new { });

            return await ReadAsync(response);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await configured.DisposeAsync();
            await factory.DisposeAsync();
        }

        private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
        {
            string body = await response.Content.ReadAsStringAsync();

            if (!body.StartsWith('{'))
            {
                return (response.StatusCode, default);
            }

            using var document = JsonDocument.Parse(body);

            return (response.StatusCode, document.RootElement.Clone());
        }
    }
}
