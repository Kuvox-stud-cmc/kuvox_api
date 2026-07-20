using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Kuvox.Api.Modules.Projects.Controllers;
using Kuvox.Api.Modules.Projects.Dtos;
using Kuvox.Api.Modules.Projects.Services;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Kuvox.Api.Modules.Shared.Infrastructure.Http;
using Kuvox.Api.Modules.Timelines.Controllers;
using Kuvox.Api.Modules.Timelines.Dtos;
using Kuvox.Api.Modules.Timelines.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Tests;

public sealed class HttpValidatorTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid TimelineId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid RevisionId = Guid.Parse("44444444-4444-4444-8444-444444444444");

    [Fact]
    public void Timeline_etags_are_stable_and_advance_with_revision_or_schema()
    {
        var current = Timeline(revisionNumber: 7, schemaVersion: 2);
        var same = Timeline(revisionNumber: 7, schemaVersion: 2);

        Assert.Equal(RevisionHttpValidators.TimelineETag(current), RevisionHttpValidators.TimelineETag(same));
        Assert.NotEqual(
            RevisionHttpValidators.TimelineETag(current),
            RevisionHttpValidators.TimelineETag(Timeline(revisionNumber: 8, schemaVersion: 2)));
        Assert.NotEqual(
            RevisionHttpValidators.TimelineETag(current),
            RevisionHttpValidators.TimelineETag(Timeline(revisionNumber: 7, schemaVersion: 3)));
        Assert.Matches("^\"[a-f0-9]{64}\"$", RevisionHttpValidators.TimelineETag(current));
    }

    [Fact]
    public void Image_etags_are_stable_and_advance_with_authoritative_revision()
    {
        var current = Image(revisionNumber: 4);
        Assert.Equal(
            RevisionHttpValidators.ImageCompositionETag(current),
            RevisionHttpValidators.ImageCompositionETag(Image(revisionNumber: 4)));
        Assert.NotEqual(
            RevisionHttpValidators.ImageCompositionETag(current),
            RevisionHttpValidators.ImageCompositionETag(Image(revisionNumber: 5)));
    }

    [Theory]
    [InlineData("{etag}")]
    [InlineData("W/{etag}")]
    [InlineData("\"other\", W/{etag}, \"third\"")]
    [InlineData("*")]
    public void If_none_match_supports_strong_weak_lists_and_wildcards(string template)
    {
        var etag = RevisionHttpValidators.TimelineETag(Timeline());
        Assert.True(RevisionHttpValidators.IfNoneMatchMatches(template.Replace("{etag}", etag), etag));
    }

    [Fact]
    public async Task Timeline_validator_is_disabled_by_default()
    {
        var controller = TimelineController(Timeline(), enabled: false);
        controller.Request.Headers.IfNoneMatch = "*";

        var result = await controller.GetCurrentDocument(ProjectId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.False(controller.Response.Headers.ContainsKey("ETag"));
        Assert.False(controller.Response.Headers.ContainsKey("Cache-Control"));
    }

    [Fact]
    public async Task Timeline_conditional_read_is_bodyless_304_after_authorized_load()
    {
        var calls = 0;
        var document = Timeline();
        var controller = TimelineController(document, enabled: true, onGet: () => calls++);
        controller.Request.Headers.IfNoneMatch = $"\"other\", W/{RevisionHttpValidators.TimelineETag(document)}";

        var result = await controller.GetCurrentDocument(ProjectId, CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status304NotModified, status.StatusCode);
        Assert.Equal(1, calls);
        Assert.Equal("private, no-cache", controller.Response.Headers.CacheControl);
        Assert.Equal(RevisionHttpValidators.TimelineETag(document), controller.Response.Headers.ETag);
    }

    [Fact]
    public async Task Image_conditional_read_is_bodyless_304()
    {
        var document = Image();
        var controller = ProjectController(document, enabled: true);
        controller.Request.Headers.IfNoneMatch = "*";

        var result = await controller.GetImageComposition(ProjectId, CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status304NotModified, status.StatusCode);
        Assert.Equal("private, no-cache", controller.Response.Headers.CacheControl);
        Assert.Equal(RevisionHttpValidators.ImageCompositionETag(document), controller.Response.Headers.ETag);
    }

    [Fact]
    public async Task Revoked_membership_is_rejected_before_if_none_match_is_evaluated()
    {
        var document = Image();
        var service = Proxy<IProjectService>((method, _) =>
            method.Name == nameof(IProjectService.GetImageCompositionAsync)
                ? throw DomainException.Forbidden("Membership revoked.")
                : throw new NotSupportedException(method.Name));
        var controller = WithHttpContext(new ProjectsController(service, new CachingOptions { HttpValidatorsEnabled = true }));
        controller.Request.Headers.IfNoneMatch = RevisionHttpValidators.ImageCompositionETag(document);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            controller.GetImageComposition(ProjectId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, error.StatusCode);
        Assert.False(controller.Response.Headers.ContainsKey("ETag"));
    }

    [Fact]
    public async Task Default_cache_policy_is_no_store_but_explicit_policies_win()
    {
        var defaultContext = new DefaultHttpContext();
        await new DefaultCacheControlMiddleware(_ => Task.CompletedTask).InvokeAsync(defaultContext);
        Assert.Equal("no-store", defaultContext.Response.Headers.CacheControl);

        var explicitContext = new DefaultHttpContext();
        await new DefaultCacheControlMiddleware(context =>
        {
            context.Response.Headers.CacheControl = "private, max-age=300";
            return Task.CompletedTask;
        }).InvokeAsync(explicitContext);
        Assert.Equal("private, max-age=300", explicitContext.Response.Headers.CacheControl);
    }

    private static TimelinesController TimelineController(
        TimelineDocumentDto document,
        bool enabled,
        Action? onGet = null)
    {
        var service = Proxy<ITimelineService>((method, _) =>
        {
            if (method.Name != nameof(ITimelineService.GetCurrentDocumentAsync))
            {
                throw new NotSupportedException(method.Name);
            }
            onGet?.Invoke();
            return Task.FromResult(document);
        });
        return WithHttpContext(new TimelinesController(
            service,
            new CachingOptions { HttpValidatorsEnabled = enabled }));
    }

    private static ProjectsController ProjectController(ImageCompositionDto document, bool enabled)
    {
        var service = Proxy<IProjectService>((method, _) =>
            method.Name == nameof(IProjectService.GetImageCompositionAsync)
                ? Task.FromResult(document)
                : throw new NotSupportedException(method.Name));
        return WithHttpContext(new ProjectsController(
            service,
            new CachingOptions { HttpValidatorsEnabled = enabled }));
    }

    private static TController WithHttpContext<TController>(TController controller)
        where TController : ControllerBase
    {
        var identity = new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, UserId.ToString("D"))],
            "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        return controller;
    }

    private static TimelineDocumentDto Timeline(int revisionNumber = 7, int schemaVersion = 2) => new(
        ProjectId,
        TimelineId,
        revisionNumber == 7 ? RevisionId : Guid.Parse("55555555-5555-4555-8555-555555555555"),
        JsonDocument.Parse("{\"schemaVersion\":2}").RootElement.Clone(),
        revisionNumber,
        schemaVersion,
        "editor",
        null,
        DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
        UserId);

    private static ImageCompositionDto Image(int revisionNumber = 4) => new(
        ProjectId,
        JsonDocument.Parse("{\"layers\":[]}").RootElement.Clone(),
        revisionNumber,
        DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
        UserId);

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var service = DispatchProxy.Create<T, TestDispatchProxy<T>>();
        ((TestDispatchProxy<T>)(object)service).Handler = handler;
        return service;
    }

    public class TestDispatchProxy<T> : DispatchProxy where T : class
    {
        public required Func<MethodInfo, object?[]?, object?> Handler { private get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod ?? throw new InvalidOperationException("Missing target method."), args);
    }
}
