-- One-time repair for the development migration observed on 2026-09-10.
-- Stop this database's Web/Migrator before running. Select the database explicitly.
-- Only accepts the known legacy table/history; never creates, drops or clears tables.
-- The JSON column is widened and the history ID reconciled in one transaction.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT <> 0 THROW 51040, 'Run this repair outside an existing transaction.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
        OR OBJECT_ID(N'dbo.PosReceiptTemplates', N'U') IS NULL
        THROW 51041, 'Receipt repair: expected history/table missing. No changes made.', 1;

    DECLARE @Legacy nvarchar(150) = N'20260909171948_AddPosReceiptTemplates';
    DECLARE @Current nvarchar(150) = N'20260910002000_AddPosReceiptTemplates';
    DECLARE @OldCount int, @NewCount int, @HistoryCount int, @RowsBefore bigint;
    SELECT @HistoryCount = COUNT(*) FROM dbo.__EFMigrationsHistory WITH (TABLOCKX, HOLDLOCK);
    SELECT @OldCount = COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = @Legacy AND ProductVersion = N'8.0.29';
    SELECT @NewCount = COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = @Current AND ProductVersion = N'8.0.29';
    IF @OldCount + @NewCount <> 1
        THROW 51041, 'Receipt repair: ambiguous or unknown migration history. No changes made.', 1;
    IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId IN (@Legacy, @Current) AND ProductVersion <> N'8.0.29')
        THROW 51041, 'Receipt repair: unexpected EF version. No changes made.', 1;

    DECLARE @TableId int = OBJECT_ID(N'dbo.PosReceiptTemplates');
    DECLARE @Expected TABLE (Name sysname, TypeName sysname, MaxLength smallint, Nullable bit, IdentityColumn bit);
    INSERT @Expected VALUES
        (N'Id', N'int', 4, 0, 1), (N'Name', N'nvarchar', 200, 0, 0),
        (N'DefinitionJson', N'nvarchar', -1, 0, 0), (N'CreatedAtUtc', N'datetime2', 8, 0, 0),
        (N'CreatedBy', N'int', 4, 1, 0), (N'UpdatedAtUtc', N'datetime2', 8, 1, 0),
        (N'UpdatedBy', N'int', 4, 1, 0), (N'IsDeleted', N'bit', 1, 0, 0),
        (N'DeletedAtUtc', N'datetime2', 8, 1, 0), (N'DeletedBy', N'int', 4, 1, 0),
        (N'RowVersion', N'timestamp', 8, 0, 0), (N'StoreId', N'int', 4, 0, 0);
    IF EXISTS (
        SELECT 1 FROM @Expected e
        FULL OUTER JOIN (SELECT * FROM sys.columns WHERE object_id = @TableId) c ON c.name = e.Name
        WHERE e.Name IS NULL OR c.name IS NULL OR TYPE_NAME(c.user_type_id) <> e.TypeName
            OR c.is_nullable <> e.Nullable OR c.is_identity <> e.IdentityColumn OR c.is_computed <> 0
            OR (c.max_length <> e.MaxLength AND NOT (e.Name = N'DefinitionJson' AND c.max_length = 8000))
            OR (e.TypeName = N'datetime2' AND c.scale <> 7))
        THROW 51041, 'Receipt repair: table columns differ from the known migration. No changes made.', 1;
    IF IDENT_SEED(N'dbo.PosReceiptTemplates') <> 1 OR IDENT_INCR(N'dbo.PosReceiptTemplates') <> 1
        THROW 51041, 'Receipt repair: unexpected identity definition. No changes made.', 1;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        WHERE i.object_id = @TableId AND i.name = N'PK_PosReceiptTemplates' AND i.is_primary_key = 1
            AND i.is_disabled = 0 AND ic.key_ordinal = 1 AND COL_NAME(@TableId, ic.column_id) = N'Id'
            AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id = @TableId AND x.index_id = i.index_id AND x.key_ordinal > 0) = 1)
        THROW 51041, 'Receipt repair: unexpected primary key. No changes made.', 1;
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes i WHERE i.object_id = @TableId AND i.name = N'IX_PosReceiptTemplates_StoreId_IsDeleted'
            AND i.is_unique = 0 AND i.is_disabled = 0 AND i.has_filter = 0
            AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id = @TableId AND x.index_id = i.index_id AND x.key_ordinal > 0) = 2
            AND EXISTS (SELECT 1 FROM sys.index_columns x WHERE x.object_id = @TableId AND x.index_id = i.index_id AND x.key_ordinal = 1 AND COL_NAME(@TableId, x.column_id) = N'StoreId')
            AND EXISTS (SELECT 1 FROM sys.index_columns x WHERE x.object_id = @TableId AND x.index_id = i.index_id AND x.key_ordinal = 2 AND COL_NAME(@TableId, x.column_id) = N'IsDeleted'))
        THROW 51041, 'Receipt repair: unexpected store index. No changes made.', 1;
    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c ON c.constraint_object_id = f.object_id
        WHERE f.parent_object_id = @TableId AND f.name = N'FK_PosReceiptTemplates_Stores_StoreId'
            AND f.referenced_object_id = OBJECT_ID(N'dbo.Stores') AND f.is_disabled = 0 AND f.is_not_trusted = 0
            AND f.delete_referential_action = 0 AND f.update_referential_action = 0
            AND COL_NAME(@TableId, c.parent_column_id) = N'StoreId'
            AND COL_NAME(f.referenced_object_id, c.referenced_column_id) = N'Id'
            AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id = f.object_id) = 1)
        THROW 51041, 'Receipt repair: unexpected store foreign key. No changes made.', 1;

    IF @NewCount = 1
    BEGIN
        IF COL_LENGTH(N'dbo.PosReceiptTemplates', N'DefinitionJson') <> -1
            THROW 51041, 'Receipt repair: current migration has incompatible JSON storage. No changes made.', 1;
        COMMIT;
        PRINT 'Receipt template migration already reconciled; no changes needed.';
        RETURN;
    END;

    SELECT @RowsBefore = COUNT_BIG(*) FROM dbo.PosReceiptTemplates WITH (TABLOCKX, HOLDLOCK);
    ALTER TABLE dbo.PosReceiptTemplates ALTER COLUMN DefinitionJson nvarchar(max) NOT NULL;
    UPDATE dbo.__EFMigrationsHistory SET MigrationId = @Current WHERE MigrationId = @Legacy AND ProductVersion = N'8.0.29';
    IF @@ROWCOUNT <> 1 THROW 51041, 'Receipt repair: history changed concurrently.', 1;
    IF (SELECT COUNT_BIG(*) FROM dbo.PosReceiptTemplates) <> @RowsBefore
        OR (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> @HistoryCount
        THROW 51041, 'Receipt repair: row counts changed unexpectedly.', 1;
    COMMIT;
    PRINT 'Receipt template migration reconciled; template records preserved.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
