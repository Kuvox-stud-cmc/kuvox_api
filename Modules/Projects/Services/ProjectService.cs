using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Projects.Dtos;
using Kuvox.Api.Modules.Projects.Repositories;
using Kuvox.Api.Modules.Shared.Dtos;
using MediatR;

namespace Kuvox.Api.Modules.Projects.Services;

/// <summary>
/// Real Projects business logic — SCAFFOLDED, NOT YET IMPLEMENTED (throws 501).
/// Dependencies show the intended wiring:
///   - <see cref="IProjectRepository"/> for persistence,
///   - <see cref="IAuthApi"/> to validate the owner exists — a cross-module call through
///     the Auth module's public contract only (Rule 1 / Rule 2),
///   - <see cref="IMediator"/> to publish <c>ProjectDeletedEvent</c> on delete (Rule 4).
/// </summary>
internal sealed class ProjectService(IProjectRepository projects, IAuthApi auth, IMediator mediator)
    : IProjectService
{
    private readonly IProjectRepository _projects = projects;
    private readonly IAuthApi _auth = auth;
    private readonly IMediator _mediator = mediator;

    public Task<PagedResult<ProjectDto>> ListAsync(Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ProjectDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
