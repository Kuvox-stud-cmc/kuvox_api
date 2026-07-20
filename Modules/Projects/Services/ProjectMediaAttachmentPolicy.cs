using System.Text.Json;
using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Shared.Infrastructure;

namespace Kuvox.Api.Modules.Projects.Services;

internal static class ProjectMediaAttachmentPolicy
{
    public static MediaSummary RequireAttachable(Guid mediaId, MediaResolution resolution)
    {
        if (resolution.Availability == MediaResolutionAvailability.Missing)
        {
            throw DomainException.NotFound($"Media {mediaId} was not found.");
        }

        if (resolution.Availability == MediaResolutionAvailability.Deleted)
        {
            throw DomainException.BadRequest($"Media {mediaId} is in Trash and cannot be attached to this project.");
        }

        if (resolution.Availability == MediaResolutionAvailability.Inaccessible)
        {
            throw DomainException.Forbidden($"You do not have access to media {mediaId}.");
        }

        return resolution.Media
            ?? throw DomainException.BadRequest($"Media {mediaId} cannot be attached to this project.");
    }

    public static IReadOnlySet<Guid> ExtractTimelineMediaIds(JsonElement document)
    {
        var ids = new HashSet<Guid>();
        if (document.ValueKind != JsonValueKind.Object)
        {
            return ids;
        }

        if (document.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in media.EnumerateObject())
            {
                if (Guid.TryParse(property.Name, out var mediaId))
                {
                    ids.Add(mediaId);
                }
            }
        }

        if (!document.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
        {
            return ids;
        }

        foreach (var track in tracks.EnumerateArray())
        {
            if (track.ValueKind != JsonValueKind.Object
                || !track.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("mediaId", out var mediaIdElement)
                    && mediaIdElement.ValueKind == JsonValueKind.String
                    && Guid.TryParse(mediaIdElement.GetString(), out var mediaId))
                {
                    ids.Add(mediaId);
                }
            }
        }

        return ids;
    }
}
