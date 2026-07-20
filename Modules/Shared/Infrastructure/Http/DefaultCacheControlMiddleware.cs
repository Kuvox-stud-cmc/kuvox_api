namespace Kuvox.Api.Modules.Shared.Infrastructure.Http;

public sealed class DefaultCacheControlMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        return next(context);
    }
}
