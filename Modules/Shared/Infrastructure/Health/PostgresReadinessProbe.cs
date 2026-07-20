using Kuvox.Api.Modules.Auth.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Health;

public interface IPostgresReadinessProbe
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

public sealed class PostgresReadinessProbe(AuthDbContext database) : IPostgresReadinessProbe
{
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await database.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
