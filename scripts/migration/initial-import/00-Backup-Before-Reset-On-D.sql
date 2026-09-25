-- Operator-run full backup of GaoAppDb TEST. Keep GaoApp stopped.
-- Use this if the previous backup has been removed; if moved, use its new path.
-- Creates a new file in an existing SQL-accessible folder on D, not a log file.
USE [GaoAppDb];
GO
SET NOCOUNT ON;
IF CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))<>N'DESKTOP-E059ENS\SQLEXPRESS'
    OR DB_NAME()<>N'GaoAppDb'
    THROW 55210,'Unexpected server/database. Stop and review this script.',1;
IF @@TRANCOUNT<>0
    THROW 55212,'Run the backup outside a user transaction.',1;

DECLARE @FreeBytes bigint, @DataBytes bigint, @ActiveLogBytes bigint;
SELECT @FreeBytes=free_space_in_bytes FROM sys.dm_os_enumerate_fixed_drives WHERE fixed_drive_path=N'D:\';
SELECT @DataBytes=SUM(CONVERT(bigint,size)*8192) FROM sys.database_files WHERE type=0;
SELECT @ActiveLogBytes=used_log_space_in_bytes FROM sys.dm_db_log_space_usage;
-- Conservative initial check, not a guarantee if another application consumes disk.
IF @FreeBytes IS NULL OR @FreeBytes < @DataBytes+@ActiveLogBytes+CONVERT(bigint,10)*1024*1024*1024
    THROW 55213,'Insufficient free space on D for the estimated backup plus 10 GiB headroom.',1;

DECLARE @BackupFile nvarchar(4000)=
    N'D:\GaoApp\GAOAPP-SA-20260819-01\.artifacts\migration-review-db\GaoAppDb-before-reset-'
    +CONVERT(nvarchar(8),GETDATE(),112)+N'-'
    +REPLACE(CONVERT(nvarchar(8),GETDATE(),108),N':',N'')+N'-'
    +CONVERT(nvarchar(36),NEWID())+N'.bak';

SELECT @BackupFile AS BackupFile,N'BACKUP_STARTING' AS Status;
BACKUP DATABASE [GaoAppDb] TO DISK=@BackupFile
WITH COPY_ONLY,CHECKSUM,NOINIT,STATS=5;
RESTORE VERIFYONLY FROM DISK=@BackupFile WITH FILE=1,CHECKSUM;
RESTORE HEADERONLY FROM DISK=@BackupFile WITH FILE=1;
SELECT @BackupFile AS BackupFile,N'BACKUP_VERIFYONLY_PASS' AS Status;
-- Copy BackupFile from the final successful result into the PowerShell parameter.
-- VERIFYONLY checks readability/completeness; it is not a full restore rehearsal.
