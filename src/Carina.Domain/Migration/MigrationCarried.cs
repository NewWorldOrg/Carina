using Carina.Domain.Encodings;

namespace Carina.Domain.Migration;

public sealed record MigrationCarried(
    MigrationRoll Roll,
    MigrationAftermath Aftermath,
    MigrationRootStanding NewRoot,
    EncodeUnaskedStanding WhereEncodesGo,
    MigrationCarryStanding Carrying);
