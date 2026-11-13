using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using static Carina.Domain.Tests.Unit.Integrity.IntegrityFixtures;

namespace Carina.Domain.Tests.Unit.Integrity;

[Trait("Category", "Unit")]
public sealed class IntegrityClaimDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly HeldLedger ledger = new();

    private readonly HeldEncodeWork encodeWork = new();

    private readonly PicturesOfEveryRow written = new();

    private IntegrityClaimDomainService Claims => new(ledger, encodeWork, written);

    [Fact(DisplayName = "BR-KD-025: what the ledgers claim is the recording rows, the files encode work declares and what the places this process writes into add for every row")]
    public async Task TheClaimsAreTheRowsTheEncodeWorkAndWhatThePlacesAdd()
    {
        LedgerFile row = Complete(Primary, "one.m2ts", 100, seed: 1);
        DeclaredFile working = new(Encodes, "two.encoding");
        ledger.Rows.Add(row);
        encodeWork.Declared.Add(working);

        IntegrityClaims claims = await Claims.ReadAsync(Cancel);

        Assert.Equal([row], claims.Ledger);
        Assert.Equal([working, new DeclaredFile(Pictures, Id(1).Wire + ".jpg")], claims.Declared);
        Assert.Equal([row], written.AskedWith);
    }

    [Fact(DisplayName = "BR-LD-009: a file a recording row names is claimed, and so is one encode work in hand declares")]
    public async Task AFileARowOrEncodeWorkNamesIsClaimed()
    {
        ledger.Rows.Add(Complete(Primary, "one.m2ts", 100));
        encodeWork.Declared.Add(new DeclaredFile(Encodes, "two.encoding"));

        Assert.True(await Claims.ClaimsAsync(Stray(Primary, "one.m2ts"), Cancel));
        Assert.True(await Claims.ClaimsAsync(Stray(Encodes, "two.encoding"), Cancel));
    }

    [Fact(DisplayName = "BR-KD-025: the picture of a recording row is claimed in the place pictures are drawn into")]
    public async Task APictureOfARowIsClaimed()
    {
        ledger.Rows.Add(Complete(Primary, "one.m2ts", 100, seed: 4));

        Assert.True(await Claims.ClaimsAsync(Stray(Pictures, Id(4).Wire + ".jpg"), Cancel));
    }

    [Fact(DisplayName = "BR-LD-009: a file nothing names is not claimed")]
    public async Task AFileNothingNamesIsNotClaimed()
    {
        ledger.Rows.Add(Complete(Primary, "one.m2ts", 100));
        encodeWork.Declared.Add(new DeclaredFile(Encodes, "two.encoding"));

        Assert.False(await Claims.ClaimsAsync(Stray(Primary, "leftover.tmp"), Cancel));
        Assert.False(await Claims.ClaimsAsync(Stray(Encodes, "one.m2ts"), Cancel));
    }

    private static IntegrityFinding Stray(OutputRoot root, string path)
        => IntegrityFinding.NoLedgerRow(Check, root, path, 64, At, At.AddMinutes(-5));

    private sealed class HeldLedger : IRecordingLedger
    {
        public List<LedgerFile> Rows { get; } = [];

        public Task<IReadOnlyList<LedgerFile>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LedgerFile>>([.. Rows]);
    }

    private sealed class HeldEncodeWork : IEncodeWorkLedger
    {
        public List<DeclaredFile> Declared { get; } = [];

        public Task<IReadOnlyList<DeclaredFile>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DeclaredFile>>([.. Declared]);
    }

    private sealed class PicturesOfEveryRow : IWrittenFileSurvey
    {
        public IReadOnlyList<LedgerFile> AskedWith { get; private set; } = [];

        public IReadOnlyList<OutputRoot> Places => [Pictures];

        public Task<RootListing> ListAsync(OutputRoot place, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public IReadOnlyList<DeclaredFile> Claimed(IReadOnlyList<LedgerFile> ledger, IReadOnlyList<DeclaredFile> declared)
        {
            AskedWith = ledger;

            return [.. declared, .. ledger.Select(row => new DeclaredFile(Pictures, row.Id.Wire + ".jpg"))];
        }

        public IReadOnlyList<DrawnPicture> Drawn(IReadOnlyList<LedgerFile> ledger) => throw new NotSupportedException();
    }
}
