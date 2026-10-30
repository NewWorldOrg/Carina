using System.Net;

using Carina.Driver.CardLending;
using Carina.Driver.Configuration;

using Microsoft.Extensions.Hosting;

namespace Carina.Driver.Tests;

public sealed class CardLendingHostTests : IDisposable
{
    private readonly string root = DriverUnderTest.NewRoot();

    public CardLendingHostTests() => DriverUnderTest.ClearTheInheritedUrls();

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string KeyFile(UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite)
    {
        string path = Path.Combine(root, "lending.key");
        File.WriteAllText(path, "a household key long enough to be a key 0123\n");
        File.SetUnixFileMode(path, mode);

        return path;
    }

    private DriverConfiguration Lending(string keyFile) =>
        DriverUnderTest.ConfigurationIn(root) with
        {
            CardLending = new CardLendingSettings(
                Lend: new LendSettings("127.0.0.1", Port: 0, HostNames: ["lender.example"], KeyFile: keyFile)
            ),
        };

    [Fact]
    public void ADriverThatDoesNotLendOpensNoLendingPort()
    {
        DriverHostResult built = DriverHost.Create([], DriverUnderTest.ConfigurationIn(root));
        Assert.True(built.TryGetHost(out IHost? host));

        using (host)
        {
            Assert.Null(host.Services.GetService(typeof(CardLendingServer)));
        }
    }

    [Fact]
    public async Task ADriverThatLendsServesLendingAndNothingElseOnItsOwnPort()
    {
        DriverHostResult built = DriverHost.Create([], Lending(KeyFile()));
        Assert.True(built.TryGetHost(out IHost? host), string.Join(" ", built.Problems));

        using (host)
        {
            await host.StartAsync();

            try
            {
                CardLendingServer server = (CardLendingServer)host.Services.GetService(typeof(CardLendingServer))!;
                using HttpClient client = new(new SocketsHttpHandler { UseProxy = false });

                Assert.Equal(HttpStatusCode.OK, (await Post(client, server.Bound!, LendingProtocol.ChallengePath)).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await Post(client, server.Bound!, "/sessions")).StatusCode);
            }
            finally
            {
                await host.StopAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public void ADriverWhoseLendingKeyCannotBeReadDoesNotStart()
    {
        DriverHostResult built = DriverHost.Create([], Lending(Path.Combine(root, "missing.key")));

        Assert.False(built.TryGetHost(out _));
        Assert.Equal(DriverHostRefusal.Configuration, built.Refusal);
        Assert.Contains(built.Problems, problem => problem.StartsWith("cardLending.lend.keyFile:", StringComparison.Ordinal));
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, IPEndPoint lender, string path)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"http://{lender}{path}");
        request.Headers.Host = "lender.example";

        return await client.SendAsync(request);
    }
}
