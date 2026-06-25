namespace Kuvox.Api.Modules.Media.Models;

public sealed class Audio : Media
{
    public override Kuvox.Api.Modules.Media.Enums.MediaKind Kind => Kuvox.Api.Modules.Media.Enums.MediaKind.Audio;

    public required double DurationSeconds { get; set; }
}