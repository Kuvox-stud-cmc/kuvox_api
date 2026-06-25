namespace Kuvox.Api.Modules.Media.Models;

public sealed class Photo : Media
{
    public required int Width { get; set; }

    public required int Height { get; set; }
}