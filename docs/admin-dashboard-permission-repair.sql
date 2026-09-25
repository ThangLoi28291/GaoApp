-- Repair missing Dashboard permission for an existing store.
-- In SSMS, select the application's database. Preview with @Apply = 0.
-- Apply with @Apply = 1 after checking the selected store and roles.
-- Only restore the existing default ADMIN/MANAGER/CASHIER system-role grants.
-- Custom roles, other stores and business data are not modified.
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @StoreSubDomain nvarchar(255) = N'chonthanh';
DECLARE @Apply bit = 0;

IF @@TRANCOUNT <> 0 THROW 51070, 'Run this script outside an existing transaction.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @StoreId int;
    IF (SELECT COUNT(*) FROM dbo.Stores WITH (UPDLOCK, HOLDLOCK)
        WHERE SubDomainNormalized = LOWER(LTRIM(RTRIM(@StoreSubDomain))) AND IsDeleted = 0 AND IsActive = 1) <> 1
        THROW 51071, 'Expected exactly one active store matching the subdomain. No changes made.', 1;
    SELECT @StoreId = Id FROM dbo.Stores
    WHERE SubDomainNormalized = LOWER(LTRIM(RTRIM(@StoreSubDomain))) AND IsDeleted = 0 AND IsActive = 1;

    SELECT DB_NAME() AS DatabaseName, Id AS StoreId, Name AS StoreName, SubDomain, @Apply AS ApplyChanges
    FROM dbo.Stores WHERE Id = @StoreId;
    SELECT r.Id AS RoleId, r.Code AS RoleCode,
        CASE WHEN r.IsSystemRole = 1 AND r.Code IN (N'ADMIN', N'MANAGER', N'CASHIER') THEN 1 ELSE 0 END AS IncludedInRepair,
        CASE WHEN EXISTS (SELECT 1 FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.Id = rp.PermissionId
            WHERE rp.RoleId = r.Id AND p.Code = N'admin.dashboard.view') THEN 1 ELSE 0 END AS HasDashboardPermission
    FROM dbo.Roles r WHERE r.StoreId = @StoreId AND r.IsDeleted = 0 ORDER BY r.Code;

    IF @Apply = 0
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT 'Preview only. No changes made. Set @Apply = 1 to restore the default Dashboard grants.';
        RETURN;
    END;

    DECLARE @PermissionId int, @AddedPermission int = 0, @AddedGrants int = 0;
    SELECT @PermissionId = Id FROM dbo.Permissions WITH (UPDLOCK, HOLDLOCK) WHERE Code = N'admin.dashboard.view';
    IF @PermissionId IS NULL
    BEGIN
        INSERT dbo.Permissions (Code, Name, GroupName)
        VALUES (N'admin.dashboard.view', N'Xem trang tổng quan quản trị', N'Admin');
        SET @PermissionId = CONVERT(int, SCOPE_IDENTITY());
        SET @AddedPermission = 1;
    END;

    INSERT dbo.RolePermissions (RoleId, PermissionId)
    SELECT r.Id, @PermissionId FROM dbo.Roles r WITH (UPDLOCK, HOLDLOCK)
    WHERE r.StoreId = @StoreId AND r.IsDeleted = 0 AND r.IsSystemRole = 1
        AND r.Code IN (N'ADMIN', N'MANAGER', N'CASHIER')
        AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WITH (UPDLOCK, HOLDLOCK)
            WHERE rp.RoleId = r.Id AND rp.PermissionId = @PermissionId);
    SET @AddedGrants = @@ROWCOUNT;
    COMMIT TRANSACTION;
    SELECT @StoreId AS StoreId, @AddedPermission AS AddedPermission, @AddedGrants AS AddedRoleGrants;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
