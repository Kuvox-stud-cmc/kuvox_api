using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Kuvox.Api.Modules.Projects.Dtos;
using Kuvox.Api.Modules.Timelines.Dtos;
using Microsoft.Extensions.Primitives;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Http;

public static class RevisionHttpValidators
{
    private const string TimelineRepresentationVersion = "timeline-current-v1";
    private const string ImageRepresentationVersion = "image-composition-v1";

    public static string TimelineETag(TimelineDocumentDto document) => StrongSha256(
        TimelineRepresentationVersion,
        document.ProjectId.ToString("D"),
        document.TimelineId.ToString("D"),
        document.RevisionId.ToString("D"),
        document.RevisionNumber.ToString(CultureInfo.InvariantCulture),
        document.DocumentSchemaVersion.ToString(CultureInfo.InvariantCulture));

    public static string ImageCompositionETag(ImageCompositionDto document) => StrongSha256(
        ImageRepresentationVersion,
        document.ProjectId.ToString("D"),
        document.RevisionNumber.ToString(CultureInfo.InvariantCulture));

    public static bool IfNoneMatchMatches(StringValues values, string currentETag)
    {
        foreach (var header in values)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            foreach (var rawCandidate in header.Split(','))
            {
                var candidate = rawCandidate.Trim();
                if (candidate == "*")
                {
                    return true;
                }

                if (candidate.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = candidate[2..].TrimStart();
                }

                if (string.Equals(candidate, currentETag, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string StrongSha256(params string[] fields)
    {
        var canonical = string.Join('\n', fields);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"\"{Convert.ToHexStringLower(digest)}\"";
    }
}
