namespace Kuvox.Api.Modules.Timelines.Dtos;

public sealed record TimelineDto(Guid Id, Guid ProjectId, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CreateTimelineRequest(Guid ProjectId, string Name);

public sealed record TimelineRevisionDto(Guid Id, Guid TimelineId, int RevisionNumber, string Operations, DateTimeOffset CreatedAt);

/// <summary>Appends a new revision (the JSONB operations payload) to a timeline.</summary>
public sealed record CreateRevisionRequest(string Operations);

public sealed record RenderJobDto(Guid Id, Guid TimelineId, Guid? RevisionId, string Status, string? OutputStorageKey, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CommandHistoryDto(Guid Id, Guid ProjectId, Guid UserId, string CommandText, string? Intent, DateTimeOffset CreatedAt);
