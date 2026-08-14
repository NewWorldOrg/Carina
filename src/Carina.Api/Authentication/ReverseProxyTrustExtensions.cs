using Microsoft.Extensions.Options;

namespace Carina.Api.Authentication;

public static class ReverseProxyTrustExtensions
{
    public static IServiceCollection AddReverseProxyTrust(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ReverseProxyTrustOptions>()
            .Configure(options =>
                options.TrustedNetworks = configuration[TrustedProxyNetworks.SettingKey])
            .ValidateDataAnnotations()
            .Validate(
                options => TrustedProxyNetworks.TryParse(options.TrustedNetworks, out _),
                TrustedProxyNetworks.SettingRequirement)
            .ValidateOnStart();

        services.AddSingleton(provider =>
            TrustedProxyNetworks.TryParse(
                provider.GetRequiredService<IOptions<ReverseProxyTrustOptions>>().Value.TrustedNetworks,
                out var trusted)
                ? trusted
                : throw new OptionsValidationException(
                    Options.DefaultName,
                    typeof(ReverseProxyTrustOptions),
                    [TrustedProxyNetworks.SettingRequirement]));

        return services;
    }
}
