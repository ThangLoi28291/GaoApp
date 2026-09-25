[CmdletBinding()]
param(
    [string]$Server = '.\SQLEXPRESS',
    [string]$SourceDatabase = 'DataGaoStore',
    [string]$TargetDatabase = 'GaoAppDb',
    [ValidateSet('PREVIEW','DRYRUN','COMMIT','VERIFY')][string]$Mode = 'PREVIEW',
    [int]$StoreId = 1,
    [int]$WarehouseId = 1,
    [int]$LegalEntityId = 1,
    [switch]$AllowCommit,
    [string]$OutputDirectory,
    [switch]$CheckSetup
)
$ErrorActionPreference='Stop'
$scriptDirectory=Split-Path -Parent $PSCommandPath
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory=Join-Path $scriptDirectory ('evidence/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
if ($Mode -eq 'COMMIT' -and -not $AllowCommit) { throw 'Review dry-run output before using -Mode COMMIT -AllowCommit.' }
$connectionBuilder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$connectionBuilder['Data Source']=$Server
$connectionBuilder['Initial Catalog']=$TargetDatabase
$connectionBuilder['Integrated Security']=$true
$connectionBuilder['Encrypt']=$true
$connectionBuilder['TrustServerCertificate']=$true
$connectionBuilder['Connect Timeout']=15
$connectionBuilder['Application Name']='GaoAppInvoiceMigrationV1'
$sqlPath=Join-Path $scriptDirectory 'InvoiceMigration.sql'
$sql=[System.IO.File]::ReadAllText($sqlPath)
$sql=[regex]::Replace($sql,'(?s)-- SETTINGS BEGIN.*?-- SETTINGS END','-- Parameters supplied by Invoke-Migration.ps1')
if ($CheckSetup) {
    Write-Output 'CHECK_SETUP_PASS: SQL loaded and connection settings prepared; no database connection was opened.'
    Write-Output "OutputDirectory: $OutputDirectory"
    return
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'OutputDirectory already exists; use a new directory for this run.' }
$connection=[System.Data.SqlClient.SqlConnection]::new($connectionBuilder.ConnectionString)
$watch=[System.Diagnostics.Stopwatch]::StartNew()
$startedAt=[datetime]::UtcNow
try {
    $connection.Open()
    $command=$connection.CreateCommand()
    $command.CommandTimeout=600
    $command.CommandText=$sql
    [void]$command.Parameters.Add('@Mode',[System.Data.SqlDbType]::VarChar,12)
    $command.Parameters['@Mode'].Value=$Mode
    foreach($pair in @(@('@SourceDatabase',$SourceDatabase),@('@ExpectedTargetDatabase',$TargetDatabase))) {
        [void]$command.Parameters.Add($pair[0],[System.Data.SqlDbType]::NVarChar,128)
        $command.Parameters[$pair[0]].Value=$pair[1]
    }
    foreach($pair in @(@('@StoreId',$StoreId),@('@WarehouseId',$WarehouseId),@('@LegalEntityId',$LegalEntityId))) {
        [void]$command.Parameters.Add($pair[0],[System.Data.SqlDbType]::Int)
        $command.Parameters[$pair[0]].Value=$pair[1]
    }
    [void]$command.Parameters.Add('@AllowCommit',[System.Data.SqlDbType]::Bit)
    $command.Parameters['@AllowCommit'].Value=[bool]$AllowCommit
    $adapter=[System.Data.SqlClient.SqlDataAdapter]::new($command)
    $data=[System.Data.DataSet]::new()
    Write-Output "Running $Mode on $Server / $TargetDatabase; source=$SourceDatabase"
    [void]$adapter.Fill($data)
    [void][System.IO.Directory]::CreateDirectory($OutputDirectory)
    $reportManifest=[System.Collections.Generic.List[object]]::new()
    $tableNumber=0
    foreach($table in $data.Tables) {
        $tableNumber++
        $report=if($table.Rows.Count -gt 0 -and $table.Columns.Contains('Report')){[string]$table.Rows[0]['Report']}else{"EMPTY_$tableNumber"}
        $csvPath=Join-Path $OutputDirectory ('{0:00}-{1}.csv' -f $tableNumber,$report)
        $table | Select-Object -Property $table.Columns.ColumnName | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8
        $reportManifest.Add([pscustomobject]@{Report=$report;Rows=$table.Rows.Count;File=[System.IO.Path]::GetFileName($csvPath)})
        if($table.Rows.Count -le 2){$table|Format-Table -AutoSize|Out-String -Width 200|Write-Output}
        else{Write-Output "$report : $($table.Rows.Count) rows -> $csvPath"}
    }
    $watch.Stop()
    [pscustomobject]@{
        StartedAtUtc=$startedAt.ToString('o');FinishedAtUtc=[datetime]::UtcNow.ToString('o');Mode=$Mode
        Server=$Server;SourceDatabase=$SourceDatabase;TargetDatabase=$TargetDatabase;StoreId=$StoreId;WarehouseId=$WarehouseId
        LegalEntityId=$LegalEntityId
        SqlSha256=(Get-FileHash -LiteralPath $sqlPath -Algorithm SHA256).Hash;ElapsedMilliseconds=$watch.ElapsedMilliseconds
        Reports=$reportManifest.ToArray()
    }|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $OutputDirectory 'manifest.json') -Encoding utf8
    Write-Output "Evidence: $([System.IO.Path]::GetFullPath($OutputDirectory))"
} finally { $connection.Dispose() }
