-- SUPERSEDED: do not allocate this log for the revised low-log rehearsal.
-- Deliberately stop the old script before its configuration statements.
THROW 55399,'Superseded capacity proposal. Use 00-DryRun-Reset-LowLog.ps1; no 64 GiB log allocation is required by that script.',1;
-- Operator-run capacity preparation for this TEST machine only.
-- Adds a SQL-managed 64 GiB log file on D (maximum 96 GiB).
-- Keeps the original C log file but disables its automatic growth.
-- No business rows are changed. The added file remains part of GaoAppDb afterward.
-- Do not delete/move the LDF in Explorer; removal is a separate SQL operation later.
-- Keep GaoApp stopped. Run the entire script in SSMS, outside a user transaction.
USE [GaoAppDb];
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 15000;

IF @@TRANCOUNT<>0
    THROW 55300,'Run this script outside a user transaction.',1;
IF CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))<>N'DESKTOP-E059ENS\SQLEXPRESS'
    OR DB_NAME()<>N'GaoAppDb'
    THROW 55301,'This script is restricted to the reviewed TEST instance/database.',1;
IF NOT EXISTS(SELECT 1 FROM sys.databases WHERE database_id=DB_ID() AND state_desc=N'ONLINE' AND recovery_model_desc=N'SIMPLE')
    THROW 55302,'Expected ONLINE/SIMPLE TEST database.',1;
IF EXISTS(SELECT 1 FROM sys.dm_tran_database_transactions dt
          JOIN sys.dm_tran_session_transactions st ON st.transaction_id=dt.transaction_id
          JOIN sys.dm_exec_sessions s ON s.session_id=st.session_id
          WHERE dt.database_id=DB_ID() AND s.is_user_process=1 AND s.session_id<>@@SPID
            AND dt.database_transaction_type=1)
    THROW 55303,'An application write transaction is active. Keep GaoApp stopped.',1;
IF EXISTS(SELECT 1 FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND session_id<>@@SPID AND command LIKE N'%ROLLBACK%')
    THROW 55304,'Rollback is still active. Wait before changing log capacity.',1;
IF EXISTS(SELECT 1 FROM sys.dm_db_log_space_usage WHERE used_log_space_in_bytes>CONVERT(bigint,1024)*1024*1024)
    THROW 55305,'More than 1 GiB of log is active; review activity before proceeding.',1;

DECLARE @FilePath nvarchar(260)=N'D:\GaoApp\GAOAPP-SA-20260819-01\.artifacts\migration-review-db\GaoAppDb_migration_log.ldf';
DECLARE @InitialMB bigint=65536, @MaximumMB bigint=98304, @ReserveMB bigint=20480;
DECLARE @CurrentMB bigint=0, @FreeMB bigint;

IF NOT EXISTS(SELECT 1 FROM sys.database_files WHERE name=N'GaoAppDb_log' AND type=1
    AND physical_name=N'C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\DATA\GaoAppDb_log.ldf')
    THROW 55306,'The original log file does not match the reviewed layout.',1;
IF EXISTS(SELECT 1 FROM sys.database_files WHERE type=1 AND name NOT IN(N'GaoAppDb_log',N'GaoAppDb_migration_log'))
    THROW 55307,'An unexpected log file exists; review layout first.',1;
IF EXISTS(SELECT 1 FROM sys.master_files WHERE physical_name=@FilePath
          AND (database_id<>DB_ID() OR name<>N'GaoAppDb_migration_log' OR type<>1))
    THROW 55308,'The intended filename is already assigned to another database/file.',1;
IF EXISTS(SELECT 1 FROM sys.database_files WHERE name=N'GaoAppDb_migration_log'
          AND (type<>1 OR physical_name<>@FilePath))
    THROW 55309,'The migration log name points to an unexpected file.',1;

SELECT @CurrentMB=CONVERT(bigint,size)/128 FROM sys.database_files WHERE name=N'GaoAppDb_migration_log';
SELECT @FreeMB=free_space_in_bytes/1048576 FROM sys.dm_os_enumerate_fixed_drives WHERE fixed_drive_path=N'D:\';
IF @FreeMB IS NULL OR @CurrentMB>@MaximumMB OR @FreeMB < (@MaximumMB-@CurrentMB+@ReserveMB)
    THROW 55310,'D needs room for the maximum log allocation plus a 20 GiB reserve. Nothing has been changed.',1;

-- This existing directory already holds the reviewed rehearsal databases.
-- SQL Server creates the file; no overwrite/replace of another file is requested.
SELECT N'PREPARING_LOG_ON_D' AS Status, @FilePath AS FilePath,
       @InitialMB AS InitialMB, @MaximumMB AS MaximumMB, @FreeMB AS DriveFreeMBBefore;

IF NOT EXISTS(SELECT 1 FROM sys.database_files WHERE name=N'GaoAppDb_migration_log')
BEGIN
    ALTER DATABASE [GaoAppDb] ADD LOG FILE
    (NAME=N'GaoAppDb_migration_log',
     FILENAME=N'D:\GaoApp\GAOAPP-SA-20260819-01\.artifacts\migration-review-db\GaoAppDb_migration_log.ldf',
     SIZE=65536MB, MAXSIZE=98304MB, FILEGROWTH=1024MB);
END
ELSE
BEGIN
    IF @CurrentMB<@InitialMB
        ALTER DATABASE [GaoAppDb] MODIFY FILE (NAME=N'GaoAppDb_migration_log',SIZE=65536MB);
    ALTER DATABASE [GaoAppDb] MODIFY FILE (NAME=N'GaoAppDb_migration_log',MAXSIZE=98304MB,FILEGROWTH=1024MB);
END;

IF NOT EXISTS(SELECT 1 FROM sys.database_files WHERE name=N'GaoAppDb_migration_log'
    AND type=1 AND physical_name=@FilePath AND CONVERT(bigint,size)>=@InitialMB*128
    AND max_size=@MaximumMB*128 AND is_percent_growth=0 AND growth=1024*128)
    THROW 55311,'The D log did not reach the required configuration. Do not start cleanup.',1;

-- Apply this only after the D file exists with sufficient allocated space.
ALTER DATABASE [GaoAppDb] MODIFY FILE (NAME=N'GaoAppDb_log',FILEGROWTH=0);

IF EXISTS(SELECT 1 FROM sys.database_files WHERE type=1 AND physical_name LIKE N'C:\%' AND growth<>0)
    THROW 55312,'A log on C still permits automatic growth. Do not start cleanup.',1;

SELECT name AS LogicalName, physical_name,
       CAST(CONVERT(bigint,size)/128.0 AS decimal(18,2)) AS SizeMB,
       max_size AS MaxSizeIn8KPages, growth AS GrowthIn8KPages, is_percent_growth
FROM sys.database_files WHERE type=1 ORDER BY file_id;
SELECT fixed_drive_path AS Drive, CAST(free_space_in_bytes/1073741824.0 AS decimal(18,2)) AS FreeGB
FROM sys.dm_os_enumerate_fixed_drives ORDER BY fixed_drive_path;
SELECT N'LOG_CAPACITY_READY_FOR_DRYRUN' AS Status;
-- Configuration changes are not part of the cleanup transaction and are not rolled
-- back by DRYRUN. Keep this file attached until a later reviewed SQL cleanup.
