using Carina.Infrastructure.Migration;

namespace Carina.Infrastructure.Tests.Migration;

public sealed class MigrationRootSettingsTests
{
    [Fact]
    public void TheRootIsTheNameTheInstallationDeclaredForThatDirectory()
    {
        Assert.True(MigrationRootSettings.TryRead(
            Declaring("primary=/srv/recordings;spare=/srv/spare"),
            "/srv/recordings",
            out MigrationRootSettings? read,
            out _));

        Assert.NotNull(read);
        Assert.Equal("primary", read.Root.Value);
    }

    [Fact]
    public void ADirectoryIsTheSameOneWhetherOrNotItIsWrittenWithASeparatorOnTheEnd()
    {
        Assert.True(MigrationRootSettings.TryRead(
            Declaring("primary=/srv/recordings/"),
            "/srv/recordings",
            out MigrationRootSettings? read,
            out _));

        Assert.NotNull(read);
        Assert.Equal("primary", read.Root.Value);
    }

    [Fact]
    public void ADirectoryNothingDeclaresIsRefusedRatherThanNamedAfterItself()
    {
        Assert.False(MigrationRootSettings.TryRead(
            Declaring("primary=/srv/recordings"),
            "/srv/elsewhere",
            out MigrationRootSettings? read,
            out string problem));

        Assert.Null(read);
        Assert.Contains("/srv/elsewhere", problem, StringComparison.Ordinal);
        Assert.Contains(MigrationRootSettings.DeclarationVariable, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ADirectoryWhoseOwnNameLooksLikeARootIsStillRefusedWhenNothingDeclaresIt()
    {
        Assert.False(MigrationRootSettings.TryRead(
            Declaring("primary=/srv/recordings"),
            "/srv/primary",
            out MigrationRootSettings? read,
            out string problem));

        Assert.Null(read);
        Assert.NotEmpty(problem);
    }

    [Fact]
    public void AnInstallationThatDeclaresNoRootAtAllIsRefused()
    {
        Assert.False(MigrationRootSettings.TryRead(
            Declaring(null),
            "/srv/recordings",
            out MigrationRootSettings? read,
            out string problem));

        Assert.Null(read);
        Assert.Contains(MigrationRootSettings.DeclarationVariable, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclarationNobodyCanReadIsRefusedWithTheReasonItCouldNotBeRead()
    {
        Assert.False(MigrationRootSettings.TryRead(
            Declaring("primary"),
            "/srv/recordings",
            out MigrationRootSettings? read,
            out string problem));

        Assert.Null(read);
        Assert.Contains(MigrationRootSettings.DeclarationVariable, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingButTheDeclarationIsAsked()
    {
        List<string> asked = [];

        MigrationRootSettings.TryRead(
            name =>
            {
                asked.Add(name);

                return name is MigrationRootSettings.DeclarationVariable ? "primary=/srv/recordings" : null;
            },
            "/srv/recordings",
            out _,
            out _);

        Assert.Equal([MigrationRootSettings.DeclarationVariable], asked);
    }

    private static Func<string, string?> Declaring(string? declaration)
        => name => name is MigrationRootSettings.DeclarationVariable ? declaration : null;
}
