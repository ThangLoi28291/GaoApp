using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Security.UserInStores;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Application.Interfaces.Repositories.Users;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Security;

public class UserInStoreAdminService : IUserInStoreAdminService
{
    private readonly IUserInStoreRepository _userInStoreRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAppUnitOfWork _uow;
    private readonly IPasswordHasher _passwordHasher;

    public UserInStoreAdminService(
        IUserInStoreRepository userInStoreRepository,
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        IAppUnitOfWork uow,
        IPasswordHasher passwordHasher)
    {
        _userInStoreRepository = userInStoreRepository;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _uow = uow;
        _passwordHasher = passwordHasher;
    }

    public async Task<UserInStoreIndexVm> GetPagedAsync(int storeId, UserInStoreIndexQueryDto query, CancellationToken ct = default)
    {
        var (items, total) = await _userInStoreRepository.GetPagedAsync(
            storeId,
            query.Keyword,
            query.RoleId,
            query.IsActive,
            query.Page,
            query.PageSize,
            ct);

        var roles = await _roleRepository.GetLookupAsync(storeId, false, ct);

        var dtoItems = items.Select(x => new UserInStoreListItemDto
        {
            Id = x.Id,
            UserId = x.UserId,
            Username = x.User?.UserName ?? string.Empty,
            FullName = x.User?.FullName ?? string.Empty,
            Email = x.User?.Email,
            RoleId = x.RoleId,
            RoleName = x.Role?.Name ?? string.Empty,
            RoleCode = x.Role?.Code ?? string.Empty,
            IsActive = x.IsActive,
            CreatedAtUtc = x.CreatedAtUtc,
            PhoneNumber = x.PhoneNumber,
            PositionName = x.PositionName,
            JoinedDate = x.JoinedDate,
            Note = x.Note,
            UpdatedAtUtc = x.UpdatedAtUtc
        }).ToList();

        return new UserInStoreIndexVm
        {
            Query = query,
            Items = new PagedResult<UserInStoreListItemDto>(query.Page, query.PageSize, total, dtoItems),
            Roles = roles.Select(x => new RoleLookupItemDto
            {
                RoleId = x.Id,
                RoleName = x.Name,
                RoleCode = x.Code,
                IsActive = !x.IsDeleted
            }).ToList(),
            TotalActiveUsers = items.Count(x => x.IsActive),
            TotalInactiveUsers = items.Count(x => !x.IsActive)
        };
    }

    public async Task<UserInStoreEditDto?> GetByIdAsync(int storeId, int id, CancellationToken ct = default)
    {
        var entity = await _userInStoreRepository.GetByIdAsync(storeId, id, ct);
        if (entity == null) return null;

        var roles = await _roleRepository.GetLookupAsync(storeId, true, ct);

        return new UserInStoreEditDto
        {
            Id = entity.Id,
            UserId = entity.UserId,
            Username = entity.User?.UserName ?? string.Empty,
            FullName = entity.User?.FullName ?? string.Empty,
            Email = entity.User?.Email,
            RoleId = entity.RoleId,
            IsActive = entity.IsActive,
            PhoneNumber = entity.PhoneNumber,
            PositionName = entity.PositionName,
            JoinedDate = entity.JoinedDate,
            Note = entity.Note,
            AvailableRoles = roles.Select(x => new RoleLookupItemDto
            {
                RoleId = x.Id,
                RoleName = x.Name,
                RoleCode = x.Code,
                IsActive = !x.IsDeleted
            }).ToList()
        };
    }

    public async Task<(bool Success, string? ErrorMessage, int? Id)> CreateAsync(
        int storeId,
        int? actorUserId,
        CreateUserInStoreRequest request,
        CancellationToken ct = default)
    {
        // Linking an existing global identity is a host operation. A store's
        // employee manager must not claim another store's account by guessing its ID.
        if (!await CanAssignExistingUsersAsync(actorUserId, ct))
            return (false, "Chỉ quản trị hệ thống được gán tài khoản đã tồn tại. Hãy dùng chức năng tạo nhân viên mới.", null);

        var user = await _userRepository.GetByIdAsync(request.UserId, ct);
        if (user == null || user.IsHostAdmin)
            return (false, "Người dùng không tồn tại.", null);

        if (!user.IsActive || user.IsDeleted)
            return (false, "Người dùng đang bị khóa ở mức hệ thống.", null);

        var role = await _roleRepository.GetByIdAsync(storeId, request.RoleId, ct);
        if (role == null || role.IsDeleted)
            return (false, "Vai trò không thuộc cửa hàng hiện tại.", null);

        var exists = await _userInStoreRepository.ExistsAsync(storeId, request.UserId, ct);
        if (exists)
            return (false, "Người dùng đã được gán vào cửa hàng này.", null);

        var entity = new UserInStore
        {
            StoreId = storeId,
            UserId = request.UserId,
            RoleId = request.RoleId,
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.UtcNow,
            PhoneNumber = request.PhoneNumber?.Trim(),
            PositionName = request.PositionName?.Trim(),
            JoinedDate = request.JoinedDate,
            Note = request.Note?.Trim(),
            CreatedBy = actorUserId
        };

        await _userInStoreRepository.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        return (true, null, entity.Id);
    }

    public async Task<(bool Success, string? ErrorMessage)> UpdateAsync(
        int storeId,
        int? actorUserId,
        UpdateUserInStoreRequest request,
        CancellationToken ct = default)
    {


        var entity = await _userInStoreRepository.GetByIdAsync(storeId, request.Id, ct);
        if (entity == null)
            return (false, "Không tìm thấy bản ghi gán người dùng.");
        if (actorUserId.HasValue && entity.UserId == actorUserId.Value && request.IsActive == false)
            return (false, "Không được tự khóa chính mình.");

        if (entity.User?.IsHostAdmin == true)
            return (false, "Không được sửa tài khoản Host Admin.");

        var role = await _roleRepository.GetByIdAsync(storeId, request.RoleId, ct);
        if (role == null || role.IsDeleted)
            return (false, "Vai trò không thuộc cửa hàng hiện tại.");

        entity.RoleId = request.RoleId;
        entity.IsActive = request.IsActive;
        entity.PhoneNumber = request.PhoneNumber?.Trim();
        entity.PositionName = request.PositionName?.Trim();
        entity.JoinedDate = request.JoinedDate;
        entity.Note = request.Note?.Trim();
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorUserId;

        await _userInStoreRepository.UpdateAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        return (true, null);
    }

    public async Task<(bool Success, string? ErrorMessage)> DeleteAsync(
     int storeId,
     int id,
     int? actorUserId,
     CancellationToken ct = default)
    {
        var entity = await _userInStoreRepository.GetByIdAsync(storeId, id, ct);
        if (entity == null)
            return (false, "Không tìm thấy nhân viên.");

        if (actorUserId.HasValue && entity.UserId == actorUserId.Value)
            return (false, "Không được tự khóa/xóa chính mình.");

        if (entity.User?.IsHostAdmin == true)
            return (false, "Không được khóa tài khoản Host Admin.");

        var ok = await _userInStoreRepository.SoftDeleteAsync(storeId, id, actorUserId, ct);
        if (!ok)
            return (false, "Xóa bản ghi gán người dùng không thành công.");

        await _uow.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<List<UserLookupItemDto>> SearchUsersAsync(string? keyword, int maxResults = 20, CancellationToken ct = default)
    {
        var users = await _userRepository.SearchForSecurityAssignmentAsync(keyword, maxResults, ct);

        return users.Select(x => new UserLookupItemDto
        {
            UserId = x.Id,
            Username = x.UserName ?? string.Empty,
            FullName = x.FullName ?? string.Empty,
            Email = x.Email
        }).ToList();
    }

    public async Task<bool> CanAssignExistingUsersAsync(int? actorUserId, CancellationToken ct = default)
    {
        var actor = actorUserId.HasValue ? await _userRepository.GetByIdAsync(actorUserId.Value, ct) : null;
        return actor is { IsHostAdmin: true, IsActive: true, IsDeleted: false };
    }

    public async Task<List<RoleLookupItemDto>> GetRoleLookupAsync(int storeId, bool onlyActive = true, CancellationToken ct = default)
    {
        var roles = await _roleRepository.GetLookupAsync(storeId, onlyActive, ct);

        return roles.Select(x => new RoleLookupItemDto
        {
            RoleId = x.Id,
            RoleName = x.Name,
            RoleCode = x.Code,
            IsActive = !x.IsDeleted
        }).ToList();
    }
    public async Task<(bool Success, string? ErrorMessage, int? Id)> CreateEmployeeAsync(
    int storeId,
    int? actorUserId,
    CreateEmployeeInStoreRequest request,
    CancellationToken ct = default)
    {
        if (!IsAcceptablePassword(request.Password))
            return (false, "Mật khẩu phải có từ 12 đến 128 ký tự.", null);
        var userName = request.UserName.Trim();

        var existsUser = await _userRepository.ExistsByUserNameAsync(userName, ct);
        if (existsUser)
            return (false, "Tên đăng nhập đã tồn tại.", null);

        var role = await _roleRepository.GetByIdAsync(storeId, request.RoleId, ct);
        if (role == null || role.IsDeleted)
            return (false, "Vai trò không thuộc cửa hàng hiện tại.", null);

        var user = new User
        {
            UserName = userName,
            FullName = request.FullName.Trim(),
            Email = request.Email?.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            IsActive = true,
            IsHostAdmin = false,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actorUserId
        };

        await _userRepository.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);

        var userInStore = new UserInStore
        {
            StoreId = storeId,
            UserId = user.Id,
            RoleId = request.RoleId,
            IsActive = request.IsActive,

            PhoneNumber = request.PhoneNumber?.Trim(),
            PositionName = request.PositionName?.Trim(),
            JoinedDate = request.JoinedDate,
            Note = request.Note?.Trim(),

            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actorUserId
        };

        await _userInStoreRepository.AddAsync(userInStore, ct);
        await _uow.SaveChangesAsync(ct);

        return (true, null, userInStore.Id);
    }
    public async Task<(bool Success, string? ErrorMessage)> ResetPasswordAsync(
    int storeId,
    int userId,
    string newPassword,
    int? actorUserId,
    CancellationToken ct = default)
    {
        if (!IsAcceptablePassword(newPassword))
            return (false, "Mật khẩu phải có từ 12 đến 128 ký tự.");
        var mapping = await _userInStoreRepository
            .GetByUserIdAsync(storeId, userId, ct);

        if (mapping == null)
            return (false, "Không tìm thấy nhân viên.");

        if (mapping.User.IsHostAdmin)
            return (false, "Không được đổi mật khẩu Host Admin.");

        if (await _userRepository.HasOtherStoreMembershipAsync(userId, storeId, ct))
            return (false, "Tài khoản dùng ở nhiều cửa hàng cần quản trị hệ thống xử lý mật khẩu.");

        mapping.User.PasswordHash =
            _passwordHasher.Hash(newPassword);

        mapping.User.UpdatedAtUtc = DateTime.UtcNow;
        mapping.User.UpdatedBy = actorUserId;

        await _uow.SaveChangesAsync(ct);

        return (true, null);
    }
    private static bool IsAcceptablePassword(string? password)
        => !string.IsNullOrWhiteSpace(password) && password.Length is >= 12 and <= 128;
    public async Task<(bool Success, string? ErrorMessage)> ToggleActiveAsync(
    int storeId,
    int id,
    int? actorUserId,
    CancellationToken ct = default)
    {
        var entity = await _userInStoreRepository.GetByIdAsync(
            storeId,
            id,
            ct);

        if (entity == null)
            return (false, "Không tìm thấy nhân viên.");

        if (entity.User?.IsHostAdmin == true)
            return (false, "Không được khóa Host Admin.");

        if (actorUserId.HasValue &&
            entity.UserId == actorUserId.Value)
        {
            return (false, "Không được tự khóa chính mình.");
        }

        entity.IsActive = !entity.IsActive;

        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorUserId;

        await _uow.SaveChangesAsync(ct);

        return (true, null);
    }
}
