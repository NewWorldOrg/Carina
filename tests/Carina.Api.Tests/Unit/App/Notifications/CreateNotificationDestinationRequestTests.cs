using Carina.Api.Requests;

namespace Carina.Api.Tests.Unit.App.Notifications;

[Trait("Category", "Unit")]
public sealed class CreateNotificationDestinationRequestTests
{
    private const string Address = "https://hooks.slack.com/services/T0000/B0000/made-up-token-wxyz";

    [Fact(DisplayName = "BR-NA-003: A request to register a destination does not print the address it carries")]
    public void ARequestToRegisterDoesNotPrintItsAddress()
    {
        CreateNotificationDestinationRequest request = new() { Name = "居間", Url = Address };

        Assert.DoesNotContain("made-up-token", request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("hooks.slack.com", request.ToString(), StringComparison.Ordinal);
    }
}
