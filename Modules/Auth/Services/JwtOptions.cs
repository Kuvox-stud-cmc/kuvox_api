namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// JWT signing + lifetime settings, bound from the <c>Jwt</c> configuration section.
/// Internal to the Auth module.
/// </summary>
internal sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = "kuvox-api";

    public string Audience { get; set; } = "kuvox-client";

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 30;
}
