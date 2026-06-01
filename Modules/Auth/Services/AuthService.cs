using Kuvox.Api.Modules.Auth.Dtos;
using Kuvox.Api.Modules.Auth.Repositories;
using MediatR;

namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Real Auth business logic — SCAFFOLDED, NOT YET IMPLEMENTED. Every method throws
/// <see cref="NotImplementedException"/> (surfaced as HTTP 501). Dependencies are wired so
/// the implementation slot is obvious: persist via <see cref="IUserRepository"/>, hash
/// passwords, issue JWTs, and publish <c>UserRegisteredEvent</c> through <see cref="IMediator"/>.
/// </summary>
internal sealed class AuthService(IUserRepository users, IMediator mediator) : IAuthService
{
    // Suppress "unused" until the real implementation lands.
    private readonly IUserRepository _users = users;
    private readonly IMediator _mediator = mediator;

    public Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<AuthTokenDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<AuthTokenDto> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
