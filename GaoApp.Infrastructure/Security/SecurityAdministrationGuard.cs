using System.Security.Claims;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Security;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Security;

// All store security mutations share a database transaction lock. This also
// serializes cross-store account linking with global password reset decisions.
public sealed class SecurityAdministrationGuard(AppDbContext db, ITenantContext tenant,
    ICurrentUser currentUser, IPasswordHasher hasher, IHttpContextAccessor http,
    IEnumerable<IAccountSessionRevoker> revokers) : ISecurityAdministrationGuard
{
    private readonly AppDbContext db = db;
    private readonly ICurrentUser currentUser = currentUser;
    private readonly IPasswordHasher hasher = hasher;
    private readonly IHttpContextAccessor http = http;
    private readonly IEnumerable<IAccountSessionRevoker> revokers = revokers;
    public async Task<SecurityAdministrator> GetActorAsync(int storeId, int? actorUserId, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || actorUserId is not > 0 || currentUser.UserId != actorUserId || tenant.StoreId != storeId)
            throw new ForbiddenAppException("Ngữ cảnh quản trị cửa hàng không hợp lệ.");
        var membership = await db.UserInStores.AsNoTracking().Include(x => x.User).Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.StoreId == storeId && x.UserId == actorUserId && !x.IsDeleted, ct);
        if (membership is not { IsActive: true, User.IsActive: true, User.IsDeleted: false } ||
            membership.Role.IsDeleted || membership.Role.StoreId != storeId ||
            !await db.Stores.AnyAsync(x => x.Id == storeId && x.IsActive && !x.IsDeleted, ct))
            throw new ForbiddenAppException("Tài khoản không còn quyền truy cập cửa hàng.");
        if (http.HttpContext is { } context &&
            !AuthSessionStamp.Matches(context.User.FindFirstValue(AuthSessionStamp.ClaimType), AuthSessionStamp.Create(membership.User, membership)))
            throw new ForbiddenAppException("Phiên đăng nhập đã thay đổi. Vui lòng đăng nhập lại.");
        return new(membership.UserId, membership.User.IsHostAdmin,
            membership.Role.IsSystemRole && membership.Role.Code == "ADMIN");
    }

    public async Task<ISecurityAdministrationScope> BeginAsync(int storeId, int? actorUserId, string permission, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction != null)
            throw new InvalidOperationException("Security administration requires its own transaction.");
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=N'GaoApp.SecurityAdministration', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @result < 0 THROW 51000, 'Security administration is busy. Retry the operation.', 1;", ct);
            var actor = await GetActorAsync(storeId, actorUserId, ct);
            if (!actor.IsHostAdmin)
            {
                var codes = PermissionAliasMap.GetAcceptedCodes(permission).ToArray();
                if (!await db.UserInStores.AsNoTracking().Where(x => x.StoreId == storeId && x.UserId == actor.UserId)
                    .SelectMany(x => x.Role.RolePermissions).AnyAsync(x => codes.Contains(x.Permission.Code), ct))
                    throw new ForbiddenAppException("Bạn không có quyền thực hiện thao tác này.");
            }
            var hadAdmin = await ActiveAdmins(storeId).AnyAsync(ct);
            return new Scope(this, transaction, storeId, actor, hadAdmin);
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    private IQueryable<UserInStore> ActiveAdmins(int storeId) => db.UserInStores.Where(x =>
        x.StoreId == storeId && !x.IsDeleted && x.IsActive && x.User.IsActive && !x.User.IsDeleted &&
        !x.Role.IsDeleted && x.Role.StoreId == storeId && x.Role.IsSystemRole && x.Role.Code == "ADMIN");

    private sealed class Scope(SecurityAdministrationGuard owner, IDbContextTransaction transaction,
        int storeId, SecurityAdministrator actor, bool hadAdmin) : ISecurityAdministrationScope
    {
        private bool committed;
        public SecurityAdministrator Actor => actor;
        public void RequireAdministrator()
        {
            if (!actor.CanAdminister) throw new ForbiddenAppException("Chỉ ADMIN cửa hàng hoặc Host Admin được thực hiện thao tác này.");
        }

        public void RequireTarget(UserInStore target, bool sensitive, Role? assignedRole = null)
        {
            if (target.User is null || target.Role is null || target.StoreId != storeId || target.Role.StoreId != storeId)
                throw new ForbiddenAppException("Nhân viên không thuộc cửa hàng hiện tại.");
            if (sensitive) RequireAdministrator();
            var protectedTarget = target.User.IsHostAdmin || target.Role.Code.Equals("ADMIN", StringComparison.OrdinalIgnoreCase);
            if (protectedTarget && !actor.IsHostAdmin)
                throw new ForbiddenAppException("Chỉ Host Admin được quản lý tài khoản ADMIN.");
            if (assignedRole != null && (assignedRole.StoreId != storeId || assignedRole.IsDeleted ||
                (assignedRole.Code.Equals("ADMIN", StringComparison.OrdinalIgnoreCase) && !actor.IsHostAdmin)))
                throw new ForbiddenAppException("Chỉ Host Admin được cấp vai trò ADMIN.");
            if (sensitive && target.UserId == actor.UserId)
                throw new ForbiddenAppException("Không được tự đổi vai trò, đặt lại mật khẩu, khóa hoặc xóa chính mình.");
        }

        public async Task ConfirmAsync(SecurityActionConfirmation? confirmation, bool verifyPassword, CancellationToken ct = default)
        {
            if (confirmation?.Reason?.Trim() is not { Length: >= 3 and <= 500 })
                throw new ValidationAppException("Nhập lý do thao tác, từ 3 đến 500 ký tự.");
            if (!verifyPassword) return;
            var user = await owner.db.Users.AsNoTracking().SingleAsync(x => x.Id == actor.UserId, ct);
            if (confirmation.AdministratorPassword is not { Length: >= 1 and <= 128 } password ||
                !owner.hasher.Verify(password, user.PasswordHash))
                throw new ValidationAppException("Mật khẩu xác minh của người đang thao tác không đúng.");
        }

        public async Task CompleteAsync(string action, string entity, int entityId, object? before, object? after,
            string? reason = null, IEnumerable<int>? affectedUsers = null, CancellationToken ct = default)
        {
            owner.db.ChangeTracker.DetectChanges();
            var grants = owner.db.ChangeTracker.Entries<RolePermission>()
                .Where(x => x.State is EntityState.Added or EntityState.Deleted or EntityState.Modified)
                .Select(x => new { x.Entity.RoleId, x.Entity.PermissionId, Change = x.State.ToString() }).ToArray();
            var roleIds = grants.Select(x => x.RoleId).Distinct().ToArray();
            var adminRoles = await owner.db.Roles.AsNoTracking().Where(x => x.StoreId == storeId &&
                roleIds.Contains(x.Id) && x.Code == "ADMIN").Select(x => x.Id).ToListAsync(ct);
            if (grants.Length > 0)
            {
                RequireAdministrator();
                if (adminRoles.Count > 0 && !actor.IsHostAdmin)
                    throw new ForbiddenAppException("Chỉ Host Admin được thay đổi quyền của vai trò ADMIN.");
                if (await owner.db.Roles.IgnoreQueryFilters().AnyAsync(x => roleIds.Contains(x.Id) && (x.StoreId != storeId || x.IsDeleted), ct))
                    throw new ForbiddenAppException("Vai trò không thuộc cửa hàng hiện tại.");
            }
            var previousAdminCodes = await owner.db.RolePermissions.AsNoTracking().Where(x => adminRoles.Contains(x.RoleId))
                .Select(x => new { x.RoleId, x.Permission.Code }).ToListAsync(ct);
            var users = (affectedUsers ?? []).ToHashSet();
            if (roleIds.Length > 0)
            {
                var memberships = await owner.db.UserInStores.Where(x => x.StoreId == storeId && roleIds.Contains(x.RoleId)).ToListAsync(ct);
                foreach (var membership in memberships)
                {
                    membership.UpdatedAtUtc = DateTime.UtcNow;
                    membership.UpdatedBy = actor.UserId;
                    users.Add(membership.UserId);
                }
            }
            var createdEntity = entityId == 0 ? owner.db.ChangeTracker.Entries()
                .SingleOrDefault(x => x.State == EntityState.Added && x.Entity.GetType().Name == entity) : null;
            var audit = new AuditLog
            {
                StoreId = storeId, ActorUserId = actor.UserId, ActorUserName = owner.currentUser.UserName,
                Module = AuditModuleType.Security, ActionType = AuditActionType.Update,
                EntityName = entity, EntityId = entityId.ToString(), Summary = action + (string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason.Trim()),
                OldValuesJson = before == null ? null : JsonSerializer.Serialize(before),
                NewValuesJson = JsonSerializer.Serialize(new { State = after, GrantChanges = grants }),
                TraceId = owner.http.HttpContext?.TraceIdentifier, CreatedAtUtc = DateTime.UtcNow, IsSuccess = true
            };
            owner.db.AuditLogs.Add(audit);
            await owner.db.SaveChangesAsync(ct);
            if (createdEntity != null)
            {
                audit.EntityId = createdEntity.Property("Id").CurrentValue?.ToString();
                await owner.db.SaveChangesAsync(ct);
            }
            if (hadAdmin && !await owner.ActiveAdmins(storeId).AnyAsync(ct))
                throw new ValidationAppException("Phải giữ ít nhất một ADMIN cửa hàng đang hoạt động.");
            string[] essentials = [PermissionCodes.Security.UserInStore.View, PermissionCodes.Security.UserInStore.Create,
                PermissionCodes.Security.UserInStore.Update, PermissionCodes.Security.Role.View, PermissionCodes.Security.Role.Permissions];
            var nextAdminCodes = await owner.db.RolePermissions.AsNoTracking().Where(x => adminRoles.Contains(x.RoleId))
                .Select(x => new { x.RoleId, x.Permission.Code }).ToListAsync(ct);
            foreach (var roleId in adminRoles)
                foreach (var essential in essentials)
                {
                    var accepted = PermissionAliasMap.GetAcceptedCodes(essential);
                    if (previousAdminCodes.Any(x => x.RoleId == roleId && accepted.Contains(x.Code)) &&
                        !nextAdminCodes.Any(x => x.RoleId == roleId && accepted.Contains(x.Code)))
                        throw new ValidationAppException("Không được gỡ quyền quản trị thiết yếu của ADMIN cửa hàng.");
                }
            await transaction.CommitAsync(ct);
            committed = true;
            foreach (var userId in users)
                foreach (var revoker in owner.revokers) revoker.RevokeUser(userId);
        }

        public async ValueTask DisposeAsync()
        {
            if (!committed)
            {
                await transaction.RollbackAsync();
                // Discard rolled-back entities so a caller cannot save them later.
                owner.db.ChangeTracker.Clear();
            }
            await transaction.DisposeAsync();
        }
    }
}
