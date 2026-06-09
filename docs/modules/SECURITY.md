# Module Security / Auth / Permission

## File chính

- `GaoApp.Domain/Entities/User.cs`
- `GaoApp.Domain/Entities/UserInStore.cs`
- `GaoApp.Domain/Entities/Role.cs`
- `GaoApp.Domain/Entities/Permission.cs`
- `GaoApp.Domain/Entities/RolePermission.cs`
- `GaoApp.Application/Common/Security/PermissionCatalog.cs`
- `GaoApp.Application/Common/Security/PermissionCodes.cs`
- `GaoApp.Application/Services/Auth/AuthService.cs`
- `GaoApp.Application/Services/Security/*.cs`
- `GaoApp.Infrastructure/Security/PermissionAuthorizationHandler.cs`
- `GaoApp.Infrastructure/Security/PermissionPolicyProvider.cs`
- `GaoApp.Web/Areas/Admin/Controllers/AccountController.cs`
- `GaoApp.Web/Areas/Admin/Controllers/RolesController.cs`
- `GaoApp.Web/Areas/Admin/Controllers/RolePermissionsController.cs`
- `GaoApp.Web/Areas/Admin/Controllers/UserInStoresController.cs`

## Nghiệp vụ

- Đăng nhập admin.
- User global.
- UserInStore theo cửa hàng.
- Role.
- Permission.
- RolePermission matrix.
- Permission policy provider.
- Current user.
- Current store permission.

## Quy tắc phải giữ

1. Không hard-code bỏ qua phân quyền ở controller admin.
2. User bị inactive trong store không được thao tác store đó.
3. Permission mới phải thêm vào catalog/seed/menu nếu cần.
4. Không lộ password/hash/secret.
5. Khi thêm chức năng admin mới phải nghĩ tới quyền tương ứng.
