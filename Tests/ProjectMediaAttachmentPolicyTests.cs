using System.Text.Json;
using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Projects.Services;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Tests;

public sealed class ProjectMediaAttachmentPolicyTests
{
    [Fact]
    public void Accessible_cross_workspace_revision_zero_media_is_attachable()
    {
        var mediaId = Guid.NewGuid();
        var summary = new MediaSummary(
            mediaId,
            Guid.NewGuid(),
            OwnerKind.User,
            MediaKind.Video,
            "shared.mp4",
            "Ready",
            SearchRevision: 0);
        var resolution = new MediaResolution(
            mediaId,
            MediaKind.Video,
            MediaResolutionAvailability.Available,
            summary);

        var accepted = ProjectMediaAttachmentPolicy.RequireAttachable(mediaId, resolution);

        Assert.Same(summary, accepted);
        Assert.Equal(0, accepted.SearchRevision);
    }

    [Fact]
    public void Inaccessible_media_is_rejected_without_workspace_specific_logic()
    {
        var mediaId = Guid.NewGuid();
        var error = Assert.Throws<DomainException>(() =>
            ProjectMediaAttachmentPolicy.RequireAttachable(
                mediaId,
                new MediaResolution(mediaId, MediaKind.Video, MediaResolutionAvailability.Inaccessible, null)));

        Assert.Equal(StatusCodes.Status403Forbidden, error.StatusCode);
        Assert.Contains(mediaId.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_reconciliation_extracts_media_map_and_track_references()
    {
        var mappedId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        using var document = JsonDocument.Parse($$"""
            {
              "media": { "{{mappedId}}": { "name": "mapped" } },
              "tracks": [
                { "items": [
                  { "mediaId": "{{trackId}}" },
                  { "mediaId": "not-a-guid" }
                ] }
              ]
            }
            """);

        var ids = ProjectMediaAttachmentPolicy.ExtractTimelineMediaIds(document.RootElement);

        Assert.Equal(2, ids.Count);
        Assert.Contains(mappedId, ids);
        Assert.Contains(trackId, ids);
    }
}
