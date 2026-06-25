namespace Kuvox.Api.Modules.Media.Models;

public sealed class Video : Media
{
    public required double DurationSeconds { get; set; }

    public required int Width { get; set; }

    public required int Height { get; set; }

    public required double FrameRate { get; set; }
}