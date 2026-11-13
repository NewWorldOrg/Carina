using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests.Unit.Infra.Quality;

[Trait("Category", "Unit")]
public sealed class QualitySignalValidationTests
{
    [Fact(DisplayName = "a retention the settings could not hold is named at startup")]
    public void ARetentionTheSettingsCouldNotHoldIsNamedAtStartup()
    {
        ValidateOptionsResult refused = new QualitySignalValidation().Validate(
            null,
            new QualitySignalOptions { KeepMinuteWindowsFor = "1.00:00:00" });

        Assert.True(refused.Failed);
        Assert.Contains(nameof(QualitySignalOptions.KeepMinuteWindowsFor), refused.FailureMessage, StringComparison.Ordinal);
    }
}
