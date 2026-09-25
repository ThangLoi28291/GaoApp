[CmdletBinding()]
param(
    [string]$Server='.\SQLEXPRESS',
    [string]$SourceDatabase='DataGaoStore',
    [string]$TargetDatabase='GaoAppDb',
    [int]$StoreId=1,
    [string]$LegacyImageRoot='C:\GaoAppData\Uploads\legacy-data',
    [string]$OutputDirectory,
    [switch]$CheckSetup
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$scriptDirectory=Split-Path -Parent $PSCommandPath
. (Join-Path $scriptDirectory 'Image-Paths.ps1')
if ($SourceDatabase -eq $TargetDatabase -or $TargetDatabase -in @('master','model','msdb','tempdb')) { throw 'Invalid target database.' }
foreach ($name in @($SourceDatabase,$TargetDatabase)) {
    if ([string]::IsNullOrWhiteSpace($name) -or $name.Length -gt 128) { throw 'Invalid database name.' }
}
if ($StoreId -lt 1) { throw 'Invalid store.' }
if (-not (Test-Path -LiteralPath $LegacyImageRoot -PathType Container)) { throw 'LegacyImageRoot does not exist.' }
$LegacyImageRoot=[IO.Path]::GetFullPath($LegacyImageRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory=Join-Path $scriptDirectory ('evidence/'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$sqlPath=Join-Path $scriptDirectory 'Preview.sql'
$sql=[IO.File]::ReadAllText($sqlPath).Replace('__SOURCE__',('['+$SourceDatabase.Replace(']',']]')+']'))
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$builder['Data Source']=$Server
$builder['Initial Catalog']=$TargetDatabase
$builder['Integrated Security']=$true
$builder['Encrypt']=$true
$builder['TrustServerCertificate']=$true
$builder['Application Name']='GaoAppProductImagesPreview'
$builder['Connect Timeout']=15
if ($CheckSetup) {
    Write-Output 'CHECK_SETUP_PASS: SQL loaded; no database connection or file changes.'
    Write-Output "Image root: $LegacyImageRoot"
    return
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new OutputDirectory.' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$started=[datetime]::UtcNow
$connection=[System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
try {
    Write-Output "Reading image candidates: $SourceDatabase -> $TargetDatabase (no database writes)."
    $connection.Open()
    $command=$connection.CreateCommand()
    $command.CommandTimeout=600
    $command.CommandText=$sql
    [void]$command.Parameters.Add('@StoreId',[System.Data.SqlDbType]::Int)
    $command.Parameters['@StoreId'].Value=$StoreId
    [void]$command.Parameters.Add('@SourceDatabase',[System.Data.SqlDbType]::NVarChar,128)
    $command.Parameters['@SourceDatabase'].Value=$SourceDatabase
    $adapter=[System.Data.SqlClient.SqlDataAdapter]::new($command)
    $data=[System.Data.DataSet]::new()
    try { [void]$adapter.Fill($data) } finally { $adapter.Dispose(); $command.Dispose() }
} finally { $connection.Dispose() }
if ($data.Tables.Count -ne 2) { throw 'Unexpected preview result sets.' }
$data.Tables[0] | Select-Object -Property $data.Tables[0].Columns.ColumnName | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'source-candidates.csv') -NoTypeInformation -Encoding UTF8
$data.Tables[1] | Select-Object -Property $data.Tables[1].Columns.ColumnName | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'target-before.csv') -NoTypeInformation -Encoding UTF8
$ready=[Collections.Generic.List[object]]::new()
$skipped=[Collections.Generic.List[object]]::new()
$duplicates=[Collections.Generic.List[object]]::new()
$seen=@{}; $fileCache=@{}; $rank=@{}; $examined=0; $ambiguous=0
foreach ($row in $data.Tables[0].Rows) {
    $examined++
    if ($examined % 1000 -eq 0) { Write-Output "Checking files: $examined / $($data.Tables[0].Rows.Count)" }
    $reason=$null; $resolved=$null
    if ([long]$row.CandidateCount -eq 0) { $reason='UnmappedProductCode' }
    elseif ([long]$row.CandidateCount -ne 1) { $reason='AmbiguousProductMapping'; $ambiguous++ }
    else {
        try { $resolved=Resolve-LegacyImagePath -LegacyImageRoot $LegacyImageRoot -SourceImage ([string]$row.SourceImage) }
        catch { $reason=$_.Exception.Message }
    }
    if (-not $reason -and -not (Test-Path -LiteralPath $resolved.PhysicalPath -PathType Leaf)) { $reason='FileMissing' }
    if ($reason) {
        $skipped.Add([pscustomobject]@{SourceProductDetailId=$row.SourceProductDetailId;SourceCode=$row.SourceCode;SourceImage=$row.SourceImage;Reason=$reason})
        continue
    }
    $key=([string]$row.ProductId)+'|'+$resolved.StoragePath
    if ($seen.ContainsKey($key)) {
        $duplicates.Add([pscustomobject]@{ProductId=$row.ProductId;SourceProductDetailId=$row.SourceProductDetailId;KeptSourceProductDetailId=$seen[$key];StoragePath=$resolved.StoragePath})
        continue
    }
    if (-not $fileCache.ContainsKey($resolved.PhysicalPath)) {
        $file=Get-Item -LiteralPath $resolved.PhysicalPath
        if ($file.Length -eq 0) {
            $skipped.Add([pscustomobject]@{SourceProductDetailId=$row.SourceProductDetailId;SourceCode=$row.SourceCode;SourceImage=$row.SourceImage;Reason='EmptyFile'})
            continue
        }
        $length=$file.Length; $modified=$file.LastWriteTimeUtc
        $hash=(Get-FileHash -LiteralPath $resolved.PhysicalPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $file.Refresh()
        if ($file.Length -ne $length -or $file.LastWriteTimeUtc -ne $modified) { throw 'Image changed while hashing; rerun preview with a stable image folder.' }
        $fileCache[$resolved.PhysicalPath]=[pscustomobject]@{SizeBytes=$length;Sha256=$hash;OriginalFileName=$file.Name}
    }
    $meta=$fileCache[$resolved.PhysicalPath]
    $productKey=[string]$row.ProductId
    if (-not $rank.ContainsKey($productKey)) { $rank[$productKey]=0 }
    $order=$rank[$productKey]; $rank[$productKey]++
    $seen[$key]=$row.SourceProductDetailId
    $ready.Add([pscustomobject]@{
        ProductId=$row.ProductId;VariantId=$row.VariantId;ProductName=$row.ProductName
        SourceProductDetailId=$row.SourceProductDetailId;SourceCode=$row.SourceCode;SourceImage=$row.SourceImage
        StoragePath=$resolved.StoragePath;PhysicalPath=$resolved.PhysicalPath;OriginalFileName=$meta.OriginalFileName
        ContentType=$resolved.ContentType;SizeBytes=$meta.SizeBytes;Sha256=$meta.Sha256;IsPrimary=($order -eq 0);SortOrder=$order
    })
}
$ready | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'ready-images.csv') -NoTypeInformation -Encoding UTF8
$skipped | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'skipped-images.csv') -NoTypeInformation -Encoding UTF8
$duplicates | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'duplicate-images.csv') -NoTypeInformation -Encoding UTF8
$status=if($ambiguous -gt 0 -or $ready.Count -eq 0){'IMAGE_PREVIEW_REQUIRES_REVIEW'}else{'IMAGE_PREVIEW_READY_FOR_REVIEW'}
$summary=[pscustomobject]@{
    Status=$status;Server=$Server;SourceDatabase=$SourceDatabase;TargetDatabase=$TargetDatabase;StoreId=$StoreId
    LegacyImageRoot=$LegacyImageRoot;StartedAtUtc=$started.ToString('o');FinishedAtUtc=[datetime]::UtcNow.ToString('o')
    SourceImageRows=$examined;ReadyImageLinks=$ready.Count;ProductsWithImages=$rank.Count;UniqueFiles=$fileCache.Count
    SkippedRows=$skipped.Count;DuplicateLinks=$duplicates.Count;AmbiguousRows=$ambiguous
    ExistingProductImageRows=[long]$data.Tables[1].Rows[0].ProductImageRows
    ExistingMediaAssetRows=[long]$data.Tables[1].Rows[0].MediaAssetRows
    ExistingVariantPrimaryImages=[long]$data.Tables[1].Rows[0].VariantsWithPrimaryImage
    SqlSha256=(Get-FileHash -LiteralPath $sqlPath).Hash
    RunnerSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash
    PathsHelperSha256=(Get-FileHash -LiteralPath (Join-Path $scriptDirectory 'Image-Paths.ps1')).Hash
    Reports=@(Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.csv' | ForEach-Object { [pscustomobject]@{File=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'manifest.json') -Encoding UTF8
$summary | Select-Object Status,SourceImageRows,ReadyImageLinks,ProductsWithImages,UniqueFiles,SkippedRows,DuplicateLinks,ExistingProductImageRows,ExistingMediaAssetRows,ExistingVariantPrimaryImages | Format-List | Out-String | Write-Output
$skipped | Group-Object Reason | Select-Object Name,Count | Format-Table -AutoSize | Out-String | Write-Output
Write-Output "Evidence: $OutputDirectory"
if ($ambiguous -gt 0 -or $ready.Count -eq 0) { throw 'Review image preview before preparing import.' }
