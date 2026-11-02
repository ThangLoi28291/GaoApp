using GaoApp.Application.DTOs.Security;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.Security;

public sealed record SecurityAdministrator(int UserId, bool IsHostAdmin, bool IsStoreAdmin)
{
    public bool CanAdminister => IsHostAdmin || IsStoreAdmin;
}

public interface ISecurityAdministrationGuard
{
    Task<SecurityAdministrator> GetActorAsync(int storeId, int? actorUserId, CancellationToken ct = default);
    Task<ISecurityAdministrationScope> BeginAsync(int storeId, int? actorUserId, string permission, CancellationToken ct = default);
}

public interface ISecurityAdministrationScope : IAsyncDisposable
{
    SecurityAdministrator Actor { get; }
    void RequireAdministrator();
    void RequireTarget(UserInStore target, bool sensitive, Role? assignedRole = null);
    Task ConfirmAsync(SecurityActionConfirmation? confirmation, bool verifyPassword, CancellationToken ct = default);
    Task CompleteAsync(string action, string entity, int entityId, object? before, object? after,
        string? reason = null, IEnumerable<int>? affectedUsers = null, CancellationToken ct = default);
}

public interface IAccountSessionRevoker
{
    void RevokeUser(int userId);
}
