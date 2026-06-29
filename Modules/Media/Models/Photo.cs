namespace Kuvox.Api.Modules.Media.Models;

public sealed class Photo : Media
{
    public override Kuvox.Api.Modules.Media.Enums.MediaKind Kind => Kuvox.Api.Modules.Media.Enums.MediaKind.Image;

    public required int Width { get; set; }

    public required int Height { get; set; }
}