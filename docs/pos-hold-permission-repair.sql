-- Sua loi POST /admin/pos/cart/current/hold tra 403 sau khi publish Web.
-- Mo trong SSMS, chon dung database host. Chay @Apply = 0 de xem truoc.
-- Neu dung cua hang/role, doi @Apply = 1 va chay lai ca file.
-- Chi bo sung pos.order.hold cho ADMIN/CASHIER cua cua hang duoc chon.
-- Role tuy chinh: cap quyen qua man hinh phan quyen sau khi permission ton tai.
-- Khong chay migration, khong sua don hang/ca/tien/ton kho; chay lai khong tao trung.
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @StoreSubDomain nvarchar(255) = N'chonthanh';
DECLARE @Apply bit = 0;

IF @@TRANCOUNT <> 0 THROW 51060, 'Run this script outside an existing transaction.', 1;
DECLARE @StoreId int;
IF (SELECT COUNT(*) FROM dbo.Stores
    WHERE SubDomainNormalized = UPPER(LTRIM(RTRIM(@StoreSubDomain))) AND IsDeleted = 0 AND IsActive = 1) <> 1
    THROW 51061, 'Expected exactly one active store matching the subdomain. Check the selected database. No changes made.', 1;
SELECT @StoreId = Id FROM dbo.Stores
WHERE SubDomainNormalized = UPPER(LTRIM(RTRIM(@StoreSubDomain))) AND IsDeleted = 0 AND IsActive = 1;

SELECT DB_NAME() AS DatabaseName, Id AS StoreId, Name AS StoreName, SubDomain, @Apply AS ApplyChanges
FROM dbo.Stores WHERE Id = @StoreId;
SELECT r.Id AS RoleId, r.Code AS RoleCode, r.Name AS RoleName,
    CASE WHEN r.Code IN (N'ADMIN', N'CASHIER') THEN 1 ELSE 0 END AS IncludedInRepair,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.Id = rp.PermissionId
        WHERE rp.RoleId = r.Id AND p.Code = N'pos.order.hold') THEN 1 ELSE 0 END AS HasHoldPermission
FROM dbo.Roles r WHERE r.StoreId = @StoreId AND r.IsDeleted = 0 ORDER BY r.Code;
SELECT u.UserName, r.Id AS RoleId, r.Code AS RoleCode
FROM dbo.UserInStores us JOIN dbo.Users u ON u.Id = us.UserId
JOIN dbo.Roles r ON r.Id = us.RoleId AND r.StoreId = us.StoreId
WHERE us.StoreId = @StoreId AND us.IsActive = 1 AND us.IsDeleted = 0
    AND u.IsActive = 1 AND u.IsDeleted = 0 AND r.IsDeleted = 0
ORDER BY u.UserName;

IF @Apply = 0
BEGIN
    PRINT 'Preview only. No changes made. Set @Apply = 1 to add missing hold permission for the selected store ADMIN/CASHIER roles.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @PermissionId int, @AddedPermission int = 0, @AddedGrants int = 0;
    SELECT @PermissionId = Id FROM dbo.Permissions WITH (UPDLOCK, HOLDLOCK) WHERE Code = N'pos.order.hold';
    IF @PermissionId IS NULL
    BEGIN
        INSERT dbo.Permissions (Code, Name, GroupName)
        VALUES (N'pos.order.hold', N'Giữ và lấy lại đơn POS', N'POS');
        SET @PermissionId = CONVERT(int, SCOPE_IDENTITY());
        SET @AddedPermission = 1;
    END;
    INSERT dbo.RolePermissions (RoleId, PermissionId)
    SELECT r.Id, @PermissionId FROM dbo.Roles r
    WHERE r.StoreId = @StoreId AND r.IsDeleted = 0 AND r.Code IN (N'ADMIN', N'CASHIER')
        AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WITH (UPDLOCK, HOLDLOCK)
            WHERE rp.RoleId = r.Id AND rp.PermissionId = @PermissionId);
    SET @AddedGrants = @@ROWCOUNT;
    COMMIT TRANSACTION;
    SELECT @StoreId AS StoreId, @AddedPermission AS AddedPermission, @AddedGrants AS AddedRoleGrants;
    PRINT 'Hold permission repair completed. Reload POS. Custom roles require an explicit grant in role management.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
