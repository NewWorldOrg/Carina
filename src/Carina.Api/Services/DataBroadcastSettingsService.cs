using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.DataBroadcast;

namespace Carina.Api.Services;

/// <summary>
/// Reads and changes the data broadcast settings changed from a screen. A change names the postal code and the
/// prefecture together, and is made against the settings as they stand once no other change is being made.
/// </summary>
public sealed class DataBroadcastSettingsService(
    IDataBroadcastSettingsRepository rows,
    IAtomicWrite writes,
    TimeProvider clock)
{
    private const string PostalCodeExpected =
        "postalCode: expected the postal code as seven digits, or an empty string or null for none.";

    private const string PrefectureExpected =
        "prefecture: expected the prefecture as its place in the prefecture designation of ARIB STD-B10, from 0 to 49, or null for none.";

    public async Task<ServiceResult<DataBroadcastSettingsStanding>> ReadAsync(CancellationToken cancellationToken)
        => ServiceResult<DataBroadcastSettingsStanding>.Success(await StandingAsync(cancellationToken));

    public async Task<ServiceResult<DataBroadcastSettingsStanding>> ChangeAsync(
        string? postalCode,
        int? prefecture,
        CancellationToken cancellationToken)
    {
        if (!DataBroadcastSettingsStanding.TakesPostalCode(postalCode))
        {
            return ServiceResult<DataBroadcastSettingsStanding>.Failure(PostalCodeExpected);
        }

        if (!DataBroadcastSettingsStanding.TakesPrefecture(prefecture))
        {
            return ServiceResult<DataBroadcastSettingsStanding>.Failure(PrefectureExpected);
        }

        DataBroadcastSettingsStanding changed = await writes.AllOrNothingAsync(
            async token =>
            {
                await rows.TakeTurnAsync(token);

                DataBroadcastSettingsStanding standing = await StandingAsync(token);

                if (standing.Set(postalCode, prefecture, clock.GetUtcNow().UtcDateTime) is not { } set)
                {
                    return standing;
                }

                await rows.SaveAsync(set, token);

                return DataBroadcastSettingsStanding.Over(set);
            },
            cancellationToken);

        return ServiceResult<DataBroadcastSettingsStanding>.Success(changed);
    }

    private async Task<DataBroadcastSettingsStanding> StandingAsync(CancellationToken cancellationToken)
        => DataBroadcastSettingsStanding.Over(await rows.ReadAsync(cancellationToken));
}
