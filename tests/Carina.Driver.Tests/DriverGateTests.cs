using System.Net;
using System.Net.Http.Headers;

using Carina.Contracts;
using Carina.Driver.Ipc;

namespace Carina.Driver.Tests;

public sealed class DriverGateTests
{
    private const int TsPacketLength = 188;

    private static CancellationToken Soon() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    [Fact]
    public async Task AStreamIsServedToARequestCarryingNoCredentialsBecauseTheDriverAuthenticatesNobody()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("unauthenticated")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            DriverEndpoints.SessionStream(SessionId.Parse("unauthenticated"))
        );

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            Soon()
        );

        Assert.Null(request.Headers.Authorization);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);

        await using var body = await response.Content.ReadAsStreamAsync(Soon());

        var buffer = new byte[TsPacketLength];
        await body.ReadExactlyAsync(buffer, Soon());

        Assert.Equal(0x47, buffer[0]);
    }

    [Fact]
    public async Task ACredentialTheDriverNeverAskedForChangesNothingBecauseNothingHereInspectsOne()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var anonymous = new HttpRequestMessage(HttpMethod.Get, DriverEndpoints.Health);
        using var presented = new HttpRequestMessage(HttpMethod.Get, DriverEndpoints.Health)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token") },
        };

        using var withoutCredentials = await client.SendAsync(anonymous, Soon());
        using var withCredentials = await client.SendAsync(presented, Soon());

        Assert.Equal(HttpStatusCode.OK, withoutCredentials.StatusCode);
        Assert.Equal(withoutCredentials.StatusCode, withCredentials.StatusCode);
        Assert.Equal(
            await withoutCredentials.Content.ReadAsStringAsync(Soon()),
            await withCredentials.Content.ReadAsStringAsync(Soon())
        );
    }

    [Fact]
    public async Task TheSocketGrantsNothingToAnyoneOutsideItsGroupBecauseThatIsTheWholeGate()
    {
        await using var driver = await DriverUnderTest.Start();

        var entry = UnixFile.Inspect(driver.SocketPath);

        var forOthers =
            entry.Permissions
            & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);

        Assert.Equal(UnixPathKind.Socket, entry.Kind);
        Assert.Equal(UnixFileMode.None, forOthers);
        Assert.Equal(DriverSocket.RequiredPermissions, entry.Permissions);
        Assert.Equal((uint)driver.Configuration.SocketGroupId, entry.GroupId);
    }
}
