using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Projects.Dtos;
using Kuvox.Api.Modules.Projects.Enums;
using Kuvox.Api.Modules.Shared.Dtos;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Projects.Controllers;

/// <summary>
/// Mockup Projects endpoints returning canned data (no DB). The <c>Delete</c> action
/// publishes a real <see cref="ProjectDeletedEvent"/> via MediatR so the cross-module event
/// flow (Rule 4) can be exercised end-to-end — watch the logs for the Timelines and Media
/// cleanup handlers.
/// </summary>
[ApiController]
[Route("api/mock/projects")]
[Produces("application/json")]
public sealed class ProjectsMockupController(IMediator mediator) : ControllerBase
{
    private static readonly Guid SampleOwner = Guid.Parse("0192a3b4-c5d6-7e8f-9a0b-1c2d3e4f5061");

    private static readonly ProjectDto[] Sample =
    [
        new(Guid.Parse("0192a400-0000-7000-8000-000000000001"), SampleOwner, OwnerKind.User, ProjectKind.Video, "Summer Travel Vlog", "Beach + roadtrip cut", "draft", DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddHours(-2)),
        new(Guid.Parse("0192a400-0000-7000-8000-000000000002"), SampleOwner, OwnerKind.User, ProjectKind.Video, "Product Launch Teaser", "30s vertical promo", "rendering", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMinutes(-10)),
    ];

    [HttpGet]
    public ActionResult<PagedResult<ProjectDto>> List() =>
        Ok(new PagedResult<ProjectDto>(Sample, Page: 1, PageSize: 20, TotalCount: Sample.Length));

    [HttpGet("{id:guid}")]
    public ActionResult<ProjectDto> Get(Guid id) =>
        Ok(Sample[0] with { Id = id });

    [HttpPost]
    public ActionResult<ProjectDto> Create(CreateProjectRequest request) =>
        Ok(new ProjectDto(Guid.CreateVersion7(), SampleOwner, OwnerKind.User, request.Kind, request.Name, request.Description, "draft", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        // Demonstrates Rule 4: publish a cross-module event; Timelines/Media handlers react.
        await mediator.Publish(new ProjectDeletedEvent(id), ct);
        return NoContent();
    }
}
