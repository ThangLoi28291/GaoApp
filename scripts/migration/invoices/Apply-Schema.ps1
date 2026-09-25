[CmdletBinding()]
param([string]$Server='.\SQLEXPRESS',[Parameter(Mandatory)][string]$TargetDatabase,[switch]$DryRun)
$ErrorActionPreference='Stop'
$cs=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$cs['Data Source']=$Server; $cs['Initial Catalog']=$TargetDatabase
$cs['Integrated Security']=$true; $cs['Encrypt']=$true; $cs['TrustServerCertificate']=$true
$cs['Application Name']='GaoAppLegacyInvoiceSchema'
$connection=[System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString)
try {
    $connection.Open(); $command=$connection.CreateCommand(); $command.CommandTimeout=120
    $command.CommandText="SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory"
    $current=[string]$command.ExecuteScalar()
    if($current -eq '20260923160000_AddLegacyInvoiceImport'){Write-Output 'Schema already applied.'; return}
    if($current -ne '20260923140000_AddInvoiceStockLegacyDocumentReferences'){throw "Unexpected schema predecessor: $current"}
    $sql=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Apply-Schema.sql'))
    foreach($batch in [regex]::Split($sql,'(?im)^GO\s*$')) {
        if([string]::IsNullOrWhiteSpace($batch)){continue}
        $command.CommandText=if($DryRun -and $batch.Trim() -eq 'COMMIT;'){'ROLLBACK;'}else{$batch}
        [void]$command.ExecuteNonQuery()
    }
    Write-Output $(if($DryRun){'SCHEMA_DRYRUN_ROLLED_BACK'}else{'SCHEMA_APPLIED'})
} catch {
    if($connection.State -eq 'Open'){$command.CommandText='IF @@TRANCOUNT>0 ROLLBACK;';[void]$command.ExecuteNonQuery()}
    throw
} finally {$connection.Dispose()}
