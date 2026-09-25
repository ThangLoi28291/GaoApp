[CmdletBinding()]
param([string]$Server='.\SQLEXPRESS', [Parameter(Mandatory)][string]$TargetDatabase)
$ErrorActionPreference='Stop'
$cs=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$cs['Data Source']=$Server; $cs['Initial Catalog']=$TargetDatabase
$cs['Integrated Security']=$true; $cs['Encrypt']=$true; $cs['TrustServerCertificate']=$true
$cs['Application Name']='GaoAppInvoiceStockReferenceSchema'
$connection=[System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString)
try {
    $connection.Open()
    $command=$connection.CreateCommand(); $command.CommandTimeout=120
    $command.CommandText=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Apply-ReferenceSchema.sql'))
    [void]$command.Parameters.Add('@ExpectedTargetDatabase',[System.Data.SqlDbType]::NVarChar,128)
    $command.Parameters['@ExpectedTargetDatabase'].Value=$TargetDatabase
    $result=[System.Data.DataTable]::new(); $result.Load($command.ExecuteReader())
    $result | Format-Table -AutoSize
} finally { $connection.Dispose() }
