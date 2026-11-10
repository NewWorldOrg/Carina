using Carina.Api.Requests;

namespace Carina.Api.Tests.Unit;

public sealed class NotificationRequestsTests
{
    private const string Address = "https://hooks.slack.com/services/T0000/B0000/made-up-token-wxyz";

    [Fact(DisplayName = "BR-NA-003: A request to register a destination does not print the address it carries")]
    public void ARequestToRegisterDoesNotPrintItsAddress()
    {
        CreateNotificationDestinationRequest request = new() { Name = "居間", Url = Address };

        Assert.DoesNotContain("made-up-token", request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("hooks.slack.com", request.ToString(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-NA-003: A request to change a destination does not print the address it carries")]
    public void ARequestToChangeDoesNotPrintItsAddress()
    {
        ReviseNotificationDestinationRequest request = new() { Name = "居間", Url = Address, Enabled = true };

        Assert.DoesNotContain("made-up-token", request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("hooks.slack.com", request.ToString(), StringComparison.Ordinal);
    }
}
