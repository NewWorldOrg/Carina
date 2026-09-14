using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class RootsHeldApartValidationTests
{
    [Fact]
    public void TheRootsThisMachineIsSetUpWithAreTwoSetsThatShareNoName()
    {
        Assert.Equal(
            ValidateOptionsResult.Success,
            Validated("primary=/srv/recordings", "encodes=/srv/encodes"));
    }

    [Fact]
    public void HoldingNoRootToEncodeIntoLeavesNothingToShare()
    {
        Assert.Equal(ValidateOptionsResult.Success, Validated("primary=/srv/recordings", null));
    }

    [Fact]
    public void WalkingNoRootAtAllLeavesNothingToShare()
    {
        Assert.Equal(ValidateOptionsResult.Success, Validated(null, "encodes=/srv/encodes"));
    }

    [Fact]
    public void ANameInBothSetsIsRefusedAndBothSettingsAreSaidByName()
    {
        ValidateOptionsResult refusal = Validated("primary=/srv/recordings", "primary=/srv/encodes");

        Assert.True(refusal.Failed);
        Assert.Contains("Encodings:OutputRoots", refusal.FailureMessage!, StringComparison.Ordinal);
        Assert.Contains("Integrity:OutputRoots", refusal.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("primary", refusal.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ARootMountedAtTheSamePathUnderAnotherNameIsNotSharing()
    {
        Assert.Equal(
            ValidateOptionsResult.Success,
            Validated("primary=/srv/recordings", "encodes=/srv/recordings"));
    }

    [Fact]
    public void EveryNameInBothSetsIsSaidAtOnceRatherThanOneRunAtATime()
    {
        ValidateOptionsResult refusal = Validated(
            "primary=/srv/one;bulk=/srv/two;spare=/srv/three",
            "spare=/srv/four;primary=/srv/five");

        Assert.True(refusal.Failed);
        Assert.Contains("primary, spare", refusal.FailureMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void ASettingNeitherSideCanReadIsHandedBackAsTheRefusalItAlreadyIs()
    {
        ValidateOptionsResult refusal = Validated("primary=/srv/recordings", "encodes");

        Assert.True(refusal.Failed);
        Assert.Contains("Encodings:OutputRoots", refusal.FailureMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationWithNoSettingsAtAllIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => Validation(null).Validate(null, null!));
    }

    private static ValidateOptionsResult Validated(string? sweeping, string? held)
        => Validation(sweeping).Validate(null, Held(held));

    private static RootsHeldApartValidation Validation(string? sweeping) => new(Options.Create(Sweeping(sweeping)));

    private static IntegrityOptions Sweeping(string? written)
    {
        var options = new IntegrityOptions();
        options.ReadFrom(Configured("Integrity:OutputRoots", written));

        return options;
    }

    private static EncodingOptions Held(string? written)
    {
        var options = new EncodingOptions();
        options.ReadFrom(Configured("Encodings:OutputRoots", written));

        return options;
    }

    private static IConfiguration Configured(string key, string? written)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = written })
            .Build();
}
