namespace Kuvox.Api.Modules.Media.Models;

public sealed class Audio : Media
{
    public required double DurationSeconds { get; set; }
}