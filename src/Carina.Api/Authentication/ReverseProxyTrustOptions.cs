using System.ComponentModel.DataAnnotations;

namespace Carina.Api.Authentication;

public sealed class ReverseProxyTrustOptions
{
    [Required(ErrorMessage = TrustedProxyNetworks.SettingRequirement)]
    public string? TrustedNetworks { get; set; }
}
