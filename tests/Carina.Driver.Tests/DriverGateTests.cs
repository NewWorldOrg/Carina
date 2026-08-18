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
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("unauthenticated")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            DriverEndpoints.SessionStream(SessionId.Parse("unauthenticated"))
        );

        using HttpResponseMessage response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            Soon()
        );

        Assert.Null(request.Headers.Authorization);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);

        await using Stream body = await response.Content.ReadAsStreamAsync(Soon());

        byte[] buffer = new byte[TsPacketLength];
        await body.ReadExactlyAsync(buffer, Soon());

        Assert.Equal(0x47, buffer[0]);
    }

    [Fact]
    public async Task ACredentialTheDriverNeverAskedForChangesNothingBecauseNothingHereInspectsOne()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using var anonymous = new HttpRequestMessage(HttpMethod.Get, DriverEndpoints.Health);
        using var presented = new HttpRequestMessage(HttpMethod.Get, DriverEndpoints.Health)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token") },
        };

        using HttpResponseMessage withoutCredentials = await client.SendAsync(anonymous, Soon());
        using HttpResponseMessage withCredentials = await client.SendAsync(presented, Soon());

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
        await using DriverUnderTest driver = await DriverUnderTest.Start();

        UnixEntry entry = UnixFile.Inspect(driver.SocketPath);

        UnixFileMode forOthers =
            entry.Permissions
            & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);

        Assert.Equal(UnixPathKind.Socket, entry.Kind);
        Assert.Equal(UnixFileMode.None, forOthers);
        Assert.Equal(DriverSocket.RequiredPermissions, entry.Permissions);
        Assert.Equal((uint)driver.Configuration.SocketGroupId, entry.GroupId);
    }
}
