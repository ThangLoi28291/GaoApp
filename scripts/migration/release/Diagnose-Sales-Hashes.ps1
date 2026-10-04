[CmdletBinding()]
param([string]$PackageRoot='C:\GaoMigration-20260929',[switch]$CheckSetup)
# Read-only SQL diagnostics. Does not adopt new hashes or change migration state.
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath($PackageRoot)
& (Join-Path $root 'Verify-Package.ps1') -PackageRoot $root
$config=Get-Content -LiteralPath (Join-Path $root 'config.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($config.Server -ine 'WIN-HU6RO2EMIJF\SQLEXPRESS' -or $config.ExpectedServer -ine $config.Server -or
 $config.SourceDatabase -cne 'DataGaoStore' -or $config.TargetDatabase -cne 'GaoAppDb' -or
 $config.RunName -notmatch '^[A-Za-z0-9_-]{1,60}$'){throw 'Unexpected migration server/database configuration.'}
$initial=Join-Path $root 'scripts/migration/initial-import'
# Compile exactly the existing streaming hasher without connecting to SQL Server.
& (Join-Path $initial 'Invoke-Migration.ps1') -Package 03-sales -SourceDatabase $config.SourceDatabase -TargetDatabase $config.TargetDatabase -CheckSetup
$package=Get-Content -LiteralPath (Join-Path $initial '03-sales/package.json') -Raw|ConvertFrom-Json
$tables=@($package.TargetTables)+@($package.RetainedTargetTables)
$expectedNames='POSShifts,Orders,OrderLines,OrderPayments,POSShiftCashTransactions,SalesReturns,SalesReturnLines,SalesReturnPayments,Users,UserInStores'
if(($tables -join ',') -cne $expectedNames){throw 'Unexpected sales fingerprint table contract.'}
$runs=Join-Path $root ('runs/'+$config.RunName)
$state=Get-Content -LiteralPath (Join-Path $runs 'state.json') -Raw -Encoding UTF8|ConvertFrom-Json
$baseline=Get-Content -LiteralPath (Join-Path $state.'03-sales-COMMIT' 'manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
$sqlHash=(Get-FileHash -LiteralPath (Join-Path $initial '03-sales/Migration.sql')).Hash
if($baseline.Status -cne 'COMMIT_PASS' -or $baseline.Package -cne '03-sales' -or $baseline.SqlSha256 -cne $sqlHash -or
 $baseline.SourceDatabase -cne $config.SourceDatabase -or $baseline.TargetDatabase -cne $config.TargetDatabase -or
 $baseline.Server -ine $config.Server){throw 'Commit evidence does not match the current package/server.'}
if($CheckSetup){Write-Output 'DIAGNOSTIC_SETUP_PASS: hasher and evidence loaded; no SQL connection.';return}
$output=Join-Path $runs ('Diagnose-sales-hashes-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
if(Test-Path -LiteralPath $output){throw 'Choose a new evidence path.'}
[void][IO.Directory]::CreateDirectory($output)
$manifest=[ordered]@{Status='STARTED';DatabaseWrites=$false;StateChanged=$false;Server=$config.Server;TargetDatabase=$config.TargetDatabase;StartedAtUtc=[datetime]::UtcNow.ToString('o')}
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$builder['Data Source']=$config.Server;$builder['Initial Catalog']=$config.TargetDatabase
$builder['Integrated Security']=$true;$builder['Encrypt']=$true;$builder['TrustServerCertificate']=$true
$builder['Connect Timeout']=15;$builder['Application Name']='GaoAppReadOnlySalesHashDiagnostic'
$connection=[System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
$transaction=$null
function Read-Table([string]$sql){
 $cmd=$connection.CreateCommand();$cmd.Transaction=$transaction;$cmd.CommandTimeout=1800;$cmd.CommandText=$sql
 $adapter=[System.Data.SqlClient.SqlDataAdapter]::new($cmd);$table=[System.Data.DataTable]::new()
 try{[void]$adapter.Fill($table);return ,$table}finally{$adapter.Dispose();$cmd.Dispose()}
}
try{
 $connection.Open();$transaction=$connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
 $info=Read-Table "SET LOCK_TIMEOUT 15000; SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) ServerName,DB_NAME() DatabaseName;"
 if($info.Rows[0].ServerName -ine $config.Server -or $info.Rows[0].DatabaseName -cne $config.TargetDatabase){throw 'Connected to an unexpected SQL Server/database.'}
 $journal=Read-Table "SELECT SourceDatabase,SqlSha256,TargetHashes,CommittedAtUtc FROM dbo.GaoStoreMigrationRunsV2 WITH(HOLDLOCK) WHERE PackageId=N'GSTORE-03-SALES-V1';"
 if($journal.Rows.Count -ne 1){throw 'Expected one committed sales journal.'}
 $row=$journal.Rows[0]
 if($row.SourceDatabase -cne $config.SourceDatabase -or $row.SqlSha256 -cne $sqlHash){throw 'Database journal differs from the current sales source/SQL contract.'}
 $expected=$row.TargetHashes|ConvertFrom-Json
 foreach($name in $tables){
  if($expected.$name -cne $baseline.TargetHashes.$name){throw "Database journal disagrees with the saved COMMIT evidence for $name."}
 }
 # Shared locks hold a consistent view; no NOLOCK, dirty reads or target writes.
 foreach($name in $tables){$null=Read-Table "SELECT COUNT_BIG(*) RowCount FROM dbo.[$name] WITH(TABLOCK,HOLDLOCK);"}
 $results=[Collections.Generic.List[object]]::new()
 foreach($name in $tables){
  Write-Output "Fingerprinting $name ..."
  $query="SELECT HASHBYTES('SHA2_256',(SELECT t.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)) h FROM dbo.[$name] t ORDER BY h;"
  $actual=[GaoStoreTableHasherV2]::Read($connection,$transaction,$query)
  $results.Add([pscustomobject]@{Table=$name;ExpectedRows=([string]$expected.$name).Split(':')[0];ActualRows=$actual.Split(':')[0];
    Matches=($actual -ceq $expected.$name);ExpectedFingerprint=$expected.$name;ActualFingerprint=$actual})
  $results.ToArray()|Export-Csv -LiteralPath (Join-Path $output 'table-comparison.csv') -NoTypeInformation -Encoding UTF8
 }
 $manifest['Comparison']=$results.ToArray();$manifest['SalesCommittedAtUtc']=([datetime]$row.CommittedAtUtc).ToString('o')
 if(@($results|Where-Object {$_.Table -in @('Users','UserInStores') -and -not $_.Matches}).Count){
  # Deliberately exclude passwords, tokens, email, and permission payloads.
  $users=Read-Table 'SELECT Id,UserName,IsActive,IsHostAdmin,IsDeleted,CreatedAtUtc,UpdatedAtUtc,UpdatedBy FROM dbo.Users ORDER BY Id;'
  $users|Select-Object -Property $users.Columns.ColumnName|Export-Csv -LiteralPath (Join-Path $output 'users-metadata.csv') -NoTypeInformation -Encoding UTF8
  $stores=Read-Table 'SELECT Id,UserId,StoreId,RoleId,IsActive,IsDeleted,CreatedAtUtc,UpdatedAtUtc,UpdatedBy FROM dbo.UserInStores ORDER BY Id;'
  $stores|Select-Object -Property $stores.Columns.ColumnName|Export-Csv -LiteralPath (Join-Path $output 'user-stores-metadata.csv') -NoTypeInformation -Encoding UTF8
  Write-Output 'Account metadata saved without credentials: users-metadata.csv, user-stores-metadata.csv'
 }
 $transaction.Rollback();$transaction.Dispose();$transaction=$null
 $results.ToArray()|Select-Object Table,ExpectedRows,ActualRows,Matches|Format-Table -AutoSize|Out-String -Width 180|Write-Output
 $manifest['Status']='READ_ONLY_DIAGNOSTIC_COMPLETE'
 Write-Output 'READ_ONLY_DIAGNOSTIC_COMPLETE. No rows, journal hashes or run state were changed.'
}catch{
 $manifest['Status']='FAILED';$manifest['Error']=$_.Exception.Message
 throw
}finally{
 if($null -ne $transaction){try{$transaction.Rollback()}catch{};$transaction.Dispose()}
 $connection.Dispose();$manifest['FinishedAtUtc']=[datetime]::UtcNow.ToString('o')
 $manifest|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding UTF8
 Write-Output "Reports: $output"
}
