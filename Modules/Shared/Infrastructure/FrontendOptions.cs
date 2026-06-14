namespace Kuvox.Api.Modules.Shared.Infrastructure;

/// <summary>
/// Frontend settings, bound from the <c>Frontend</c> configuration section. Used to build
/// links (email verification, password reset) that point back into the SPA.
/// </summary>
public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    public string BaseUrl { get; set; } = "http://localhost:5173";
}
