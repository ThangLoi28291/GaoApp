-- Run manually in SSMS on the TEST SQL Server using Windows Authentication.
-- Read-only inspection. Does not back up, reset, or import either database.
-- Save all result grids and any error messages before proceeding.
USE [master];
GO
SET NOCOUNT ON;
IF DB_ID(N'DataGaoStore') IS NULL OR DB_ID(N'GaoAppDb') IS NULL
    THROW 55200, 'Required TEST databases DataGaoStore and GaoAppDb were not found. Stop here.', 1;

SELECT @@SERVERNAME AS ServerName, ORIGINAL_LOGIN() AS LoginName,
       SYSDATETIMEOFFSET() AS InspectedAt;

SELECT d.name AS DatabaseName, d.state_desc, d.user_access_desc,
       d.is_read_only, d.recovery_model_desc,
       f.type_desc AS FileType,
       CAST(SUM(CONVERT(bigint, f.size)) / 128.0 AS decimal(18,2)) AS AllocatedMB
FROM sys.databases d
JOIN sys.master_files f ON f.database_id = d.database_id
WHERE d.name IN (N'DataGaoStore', N'GaoAppDb')
GROUP BY d.name, d.state_desc, d.user_access_desc,
         d.is_read_only, d.recovery_model_desc, f.type_desc;

-- Latest recorded restore on this instance; an empty result is not an error.
SELECT TOP (5) rh.destination_database_name, rh.restore_date,
       bs.database_name AS BackupDatabaseName,
       bs.backup_start_date, bs.backup_finish_date,
       bs.type AS BackupType
FROM msdb.dbo.restorehistory rh
LEFT JOIN msdb.dbo.backupset bs ON bs.backup_set_id = rh.backup_set_id
WHERE rh.destination_database_name = N'DataGaoStore'
ORDER BY rh.restore_date DESC;

-- Metadata row counts are approximate inventory, not migration reconciliation.
SELECT N'DataGaoStore' AS DatabaseName, s.name AS SchemaName,
       t.name AS TableName, SUM(p.rows) AS ApproximateRows
FROM [DataGaoStore].sys.tables t
JOIN [DataGaoStore].sys.schemas s ON s.schema_id = t.schema_id
JOIN [DataGaoStore].sys.partitions p ON p.object_id = t.object_id
WHERE p.index_id IN (0,1) AND t.is_ms_shipped = 0
GROUP BY s.name, t.name
ORDER BY s.name, t.name;

SELECT N'GaoAppDb' AS DatabaseName, s.name AS SchemaName,
       t.name AS TableName, SUM(p.rows) AS ApproximateRows
FROM [GaoAppDb].sys.tables t
JOIN [GaoAppDb].sys.schemas s ON s.schema_id = t.schema_id
JOIN [GaoAppDb].sys.partitions p ON p.object_id = t.object_id
WHERE p.index_id IN (0,1) AND t.is_ms_shipped = 0
GROUP BY s.name, t.name
ORDER BY s.name, t.name;

IF OBJECT_ID(N'GaoAppDb.dbo.__EFMigrationsHistory', N'U') IS NOT NULL
    EXEC(N'SELECT TOP (10) MigrationId, ProductVersion
           FROM [GaoAppDb].dbo.__EFMigrationsHistory ORDER BY MigrationId DESC;');
ELSE
    SELECT N'MISSING __EFMigrationsHistory: stop before schema/reset/import.' AS Finding;

IF OBJECT_ID(N'GaoAppDb.dbo.GaoStoreMigrationRunsV2', N'U') IS NOT NULL
    EXEC(N'SELECT PackageId, SourceDatabase, SqlSha256, CommittedAtUtc
           FROM [GaoAppDb].dbo.GaoStoreMigrationRunsV2 ORDER BY CommittedAtUtc;');
ELSE
    SELECT N'No V2 journal. This does not mean target business tables are empty.' AS Finding;

SELECT N'Inspection finished. No database data was changed. Do not run import until target preparation is complete.' AS NextStep;
