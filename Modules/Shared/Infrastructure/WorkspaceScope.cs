namespace Kuvox.Api.Modules.Shared.Infrastructure;

/// <summary>
/// Module-agnostic description of the workspace a request targets: either the signed-in
/// user's Personal workspace (<see cref="IsStudio"/> = false, <see cref="OwnerId"/> = user id)
/// or a Team studio (<see cref="IsStudio"/> = true, <see cref="OwnerId"/> = studio id).
///
/// Each module maps this to its own per-module <c>OwnerKind</c> enum — there is deliberately
/// no shared <c>OwnerKind</c> type across modules (Rule 1). The active workspace is expressed
/// in the request (a <c>studioId</c> query/route value), never persisted server-side.
/// </summary>
public sealed record WorkspaceScope(bool IsStudio, Guid OwnerId);
