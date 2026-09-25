-- Run manually in SSMS on the TEST instance. Stop the GaoApp application first.
-- Creates a new, uniquely named backup. Does not reset or import any data.
USE [master];
GO
SET NOCOUNT ON;
IF CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) <> N'DESKTOP-E059ENS\SQLEXPRESS'
    THROW 55210, 'Unexpected SQL Server. Stop and review this script.', 1;
IF DB_ID(N'GaoAppDb') IS NULL
    THROW 55211, 'GaoAppDb was not found.', 1;

DECLARE @BackupFile nvarchar(4000) =
    N'C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup\GaoAppDb-before-reset-'
    + CONVERT(nvarchar(8),GETDATE(),112) + N'-'
    + REPLACE(CONVERT(nvarchar(8),GETDATE(),108),N':',N'') + N'-'
    + CONVERT(nvarchar(36),NEWID()) + N'.bak';

SELECT @BackupFile AS BackupFile, N'BACKUP_STARTING' AS Status;
BACKUP DATABASE [GaoAppDb] TO DISK = @BackupFile
WITH COPY_ONLY, CHECKSUM, NOINIT, STATS = 5;

RESTORE VERIFYONLY FROM DISK = @BackupFile WITH FILE = 1, CHECKSUM;
RESTORE HEADERONLY FROM DISK = @BackupFile WITH FILE = 1;
SELECT @BackupFile AS BackupFile, N'BACKUP_VERIFYONLY_PASS' AS Status;
-- VERIFYONLY checks the backup, but is not a full restore rehearsal.
