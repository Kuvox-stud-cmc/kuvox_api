using Kuvox.Api.Modules.Auth.Models;

namespace Kuvox.Api.Modules.Auth.Repositories;

/// <summary>Persistence boundary for <see cref="User"/>. Internal to the Auth module.</summary>
internal interface IUserRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
