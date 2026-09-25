-- Run manually in SSMS. READ-ONLY diagnostic after error 9002.
-- Does not shrink/grow files, kill sessions, change recovery model, or delete data.
USE [GaoAppDb];
GO
SET NOCOUNT ON;
SET LOCK_TIMEOUT 5000;

SELECT @@SERVERNAME AS ServerName, SYSDATETIMEOFFSET() AS InspectedAt,
       name, state_desc, recovery_model_desc, log_reuse_wait_desc
FROM sys.databases WHERE database_id=DB_ID();

SELECT f.name AS LogicalFileName, f.type_desc AS FileType, f.physical_name,
       CAST(CONVERT(bigint,f.size)/128.0 AS decimal(18,2)) AS CurrentSizeMB,
       f.max_size AS MaxSizeIn8KPages,
       CASE WHEN f.max_size=-1 THEN N'UNLIMITED'
            WHEN f.max_size=0 THEN N'NO_GROWTH'
            ELSE CONVERT(nvarchar(30),CAST(CONVERT(bigint,f.max_size)/128.0 AS decimal(18,2)))+N' MB' END AS MaxSize,
       f.is_percent_growth, f.growth AS GrowthRaw,
       CASE WHEN f.is_percent_growth=1 THEN CONVERT(nvarchar(30),f.growth)+N' %'
            ELSE CONVERT(nvarchar(30),CAST(CONVERT(bigint,f.growth)/128.0 AS decimal(18,2)))+N' MB' END AS AutoGrowth,
       v.volume_mount_point,
       CAST(v.total_bytes/1073741824.0 AS decimal(18,2)) AS VolumeSizeGB,
       CAST(v.available_bytes/1073741824.0 AS decimal(18,2)) AS VolumeFreeGB
FROM sys.database_files f
OUTER APPLY sys.dm_os_volume_stats(DB_ID(),f.file_id) v
ORDER BY f.type_desc,f.file_id;

SELECT CAST(total_log_size_in_bytes/1048576.0 AS decimal(18,2)) AS TotalLogMB,
       CAST(used_log_space_in_bytes/1048576.0 AS decimal(18,2)) AS UsedLogMB,
       CAST(used_log_space_in_percent AS decimal(8,2)) AS UsedLogPercent
FROM sys.dm_db_log_space_usage;

-- Includes requests currently rolling back. The inspection session is excluded.
SELECT r.session_id, s.program_name, r.command, r.status, r.percent_complete,
       r.wait_type, r.wait_time, r.blocking_session_id,
       r.total_elapsed_time, r.open_transaction_count
FROM sys.dm_exec_requests r
LEFT JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id
WHERE r.session_id<>@@SPID AND
      (r.database_id=DB_ID() OR s.program_name=N'GaoStoreResetDryRunAlwaysRollback')
ORDER BY r.session_id;

-- Empty results mean no database transaction is visible to this query at this moment.
SELECT dt.transaction_id, st.session_id, s.program_name,
       tx.transaction_begin_time, tx.transaction_state,
       dt.database_transaction_state,
       CAST(dt.database_transaction_log_bytes_used/1048576.0 AS decimal(18,2)) AS TransactionLogUsedMB,
       CAST(dt.database_transaction_log_bytes_reserved/1048576.0 AS decimal(18,2)) AS TransactionLogReservedMB
FROM sys.dm_tran_database_transactions dt
LEFT JOIN sys.dm_tran_active_transactions tx ON tx.transaction_id=dt.transaction_id
LEFT JOIN sys.dm_tran_session_transactions st ON st.transaction_id=dt.transaction_id
LEFT JOIN sys.dm_exec_sessions s ON s.session_id=st.session_id
WHERE dt.database_id=DB_ID()
ORDER BY dt.transaction_id,st.session_id;

-- Approximate counts for initial diagnosis ONLY; these do not prove full rollback.
SELECT t.name AS TableName, SUM(p.rows) AS ApproximateRows
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN(0,1)
WHERE s.name=N'dbo' AND t.name IN
 (N'Orders',N'OrderLines',N'Products',N'Customers',N'InvoiceHeads',N'InvoiceDetails',
  N'InventoryTransactions',N'InventoryValuationEntries',N'InventoryCostLayers')
GROUP BY t.name ORDER BY t.name;

SELECT N'READ_ONLY_DIAGNOSTIC_FINISHED. Do not rerun cleanup until log capacity and rollback state are reviewed.' AS NextStep;
