namespace Kuvox.Api.Modules.Projects.Dtos;

public sealed record ProjectDto(
    Guid Id,
    Guid OwnerId,
    string Name,
    string? Description,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateProjectRequest(Guid OwnerId, string Name, string? Description);

public sealed record UpdateProjectRequest(string Name, string? Description, string Status);
