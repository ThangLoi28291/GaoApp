[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PreviewDirectory,
    [ValidateSet('DRYRUN','COMMIT','VERIFY')][string]$Mode='DRYRUN',
    [switch]$AllowCommit,
    [string]$SuccessfulDryRunDirectory,
    [string]$OutputDirectory,
    [switch]$CheckSetup
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$scriptDirectory=Split-Path -Parent $PSCommandPath
. (Join-Path $scriptDirectory 'Image-Paths.ps1')
. (Join-Path $scriptDirectory 'Image-Hash.ps1')
$previewPath=Join-Path $PreviewDirectory 'manifest.json'
$preview=Get-Content -LiteralPath $previewPath -Raw -Encoding UTF8 | ConvertFrom-Json
$previewHash=(Get-FileHash -LiteralPath $previewPath).Hash
$sqlPath=Join-Path $scriptDirectory 'Apply-Images.sql'
$sql=[IO.File]::ReadAllText($sqlPath)
$sqlHash=(Get-FileHash -LiteralPath $sqlPath).Hash
$runnerHash=(Get-FileHash -LiteralPath $PSCommandPath).Hash
$hashHelperHash=(Get-FileHash -LiteralPath (Join-Path $scriptDirectory 'Image-Hash.ps1')).Hash
if ($preview.Status -ne 'IMAGE_PREVIEW_READY_FOR_REVIEW' -or $preview.StoreId -ne 1 -or $preview.AmbiguousRows -ne 0) { throw 'Unsupported or unsuccessful image preview.' }
if ($preview.SourceDatabase -eq $preview.TargetDatabase -or $preview.TargetDatabase -in @('master','model','msdb','tempdb')) { throw 'Invalid preview database contract.' }
if ($preview.ExistingProductImageRows -ne 0 -or $preview.ExistingMediaAssetRows -ne 0 -or $preview.ExistingVariantPrimaryImages -ne 0) { throw 'This image importer requires a preview of an empty image target.' }
foreach ($pair in @(@('SqlSha256','Preview.sql'),@('RunnerSha256','Preview-Images.ps1'),@('PathsHelperSha256','Image-Paths.ps1'))) {
    if ($preview.($pair[0]) -cne (Get-FileHash -LiteralPath (Join-Path $scriptDirectory $pair[1])).Hash) { throw 'Preview code changed. Review and regenerate preview.' }
}
foreach ($name in @('ready-images.csv','source-candidates.csv','skipped-images.csv','duplicate-images.csv','target-before.csv')) {
    $entries=@($preview.Reports | Where-Object {$_.File -ceq $name})
    if ($entries.Count -ne 1 -or $entries[0].Sha256 -cne (Get-FileHash -LiteralPath (Join-Path $PreviewDirectory $name)).Hash) { throw "Preview report changed: $name" }
}
if ($Mode -eq 'COMMIT' -and -not $CheckSetup) {
    if (-not $AllowCommit -or [string]::IsNullOrWhiteSpace($SuccessfulDryRunDirectory)) { throw 'COMMIT requires -AllowCommit and -SuccessfulDryRunDirectory.' }
    $dry=Get-Content -LiteralPath (Join-Path $SuccessfulDryRunDirectory 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($dry.Status -ne 'IMAGE_DRYRUN_PASS_ROLLED_BACK' -or $dry.PreviewManifestSha256 -cne $previewHash -or
        $dry.SqlSha256 -cne $sqlHash -or $dry.RunnerSha256 -cne $runnerHash -or $dry.HashHelperSha256 -cne $hashHelperHash) { throw 'Successful DRYRUN must match this preview and current importer.' }
}
$rows=@(Import-Csv -LiteralPath (Join-Path $PreviewDirectory 'ready-images.csv') -Encoding UTF8)
if ($rows.Count -eq 0 -or $rows.Count -ne $preview.ReadyImageLinks) { throw 'Image plan count differs from preview.' }
$plan=[Collections.Generic.List[object]]::new()
$files=@{}
foreach ($row in $rows) {
    $path=Resolve-LegacyImagePath -LegacyImageRoot $preview.LegacyImageRoot -SourceImage $row.SourceImage
    if ($path.StoragePath -cne $row.StoragePath -or -not $path.PhysicalPath.Equals($row.PhysicalPath,[StringComparison]::OrdinalIgnoreCase) -or
        $path.ContentType -cne $row.ContentType -or [IO.Path]::GetFileName($path.PhysicalPath) -cne $row.OriginalFileName) { throw 'Image path differs from preview.' }
    if ($row.Sha256 -cnotmatch '^[a-f0-9]{64}$' -or [long]$row.SizeBytes -le 0 -or $row.ProductName.Length -gt 200 -or
        [int]$row.ProductId -lt 1 -or [int]$row.VariantId -lt 1 -or [int]$row.SortOrder -lt 0) { throw 'Invalid image plan field.' }
    if ($files.ContainsKey($path.PhysicalPath)) {
        if ($files[$path.PhysicalPath].Sha256 -cne $row.Sha256 -or $files[$path.PhysicalPath].SizeBytes -ne $row.SizeBytes) { throw 'Conflicting file metadata.' }
    } else { $files[$path.PhysicalPath]=$row }
    $plan.Add([pscustomobject]@{
        ProductId=[int]$row.ProductId;VariantId=[int]$row.VariantId;ProductName=$row.ProductName
        StoragePath=$row.StoragePath;OriginalFileName=$row.OriginalFileName;ContentType=$row.ContentType
        SizeBytes=[long]$row.SizeBytes;Sha256=$row.Sha256;IsPrimary=[bool]::Parse($row.IsPrimary);SortOrder=[int]$row.SortOrder
    })
}
if ($files.Count -ne $preview.UniqueFiles) { throw 'Unique file count differs from preview.' }
$planJson=ConvertTo-Json -InputObject $plan.ToArray() -Depth 4 -Compress
if ($CheckSetup) {
    Write-Output "IMAGE_SETUP_PASS: $($plan.Count) image links / $($files.Count) files; preview integrity, paths and C# helper checked."
    Write-Output 'No database connection, file writes or image hashing performed.'
    return
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory=Join-Path $scriptDirectory ('evidence/'+$Mode.ToLowerInvariant()+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new OutputDirectory.' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$manifest=[ordered]@{
    Mode=$Mode;Status='STARTED';Server=$preview.Server;SourceDatabase=$preview.SourceDatabase;TargetDatabase=$preview.TargetDatabase
    StoreId=1;PreviewDirectory=[IO.Path]::GetFullPath($PreviewDirectory);PreviewManifestSha256=$previewHash
    SqlSha256=$sqlHash;RunnerSha256=$runnerHash;HashHelperSha256=$hashHelperHash;StartedAtUtc=[datetime]::UtcNow.ToString('o')
}
$cs=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$cs['Data Source']=$preview.Server; $cs['Initial Catalog']=$preview.TargetDatabase
$cs['Integrated Security']=$true; $cs['Encrypt']=$true; $cs['TrustServerCertificate']=$true
$cs['Application Name']='GaoAppImageMetadataImportV1'; $cs['Connect Timeout']=15
$connection=[System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString)
$transaction=$null; $commitAttempted=$false; $commitConfirmed=$false
function Command([string]$text) {
    $cmd=$connection.CreateCommand(); $cmd.Transaction=$transaction; $cmd.CommandTimeout=1800; $cmd.CommandText=$text; return $cmd
}
function Execute([string]$text) { $cmd=Command $text; try { [void]$cmd.ExecuteNonQuery() } finally { $cmd.Dispose() } }
function Scalar([string]$text) { $cmd=Command $text; try { return $cmd.ExecuteScalar() } finally { $cmd.Dispose() } }
function Read-Table([string]$text) {
    $cmd=Command $text; $adapter=[System.Data.SqlClient.SqlDataAdapter]::new($cmd); $table=[System.Data.DataTable]::new()
    try { [void]$adapter.Fill($table); return ,$table } finally { $adapter.Dispose(); $cmd.Dispose() }
}
function Hash-Table([string]$database,[string]$name,[string]$columns='t.*') {
    $db='['+$database.Replace(']',']]')+']'
    $query="SELECT HASHBYTES('SHA2_256',(SELECT $columns FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)) h FROM $db.dbo.[$name] t ORDER BY h;"
    return [GaoImageTableHasher]::Read($connection,$transaction,$query)
}
try {
    $index=0
    foreach ($file in $files.Values) {
        $index++
        if ($index % 1000 -eq 0) { Write-Output "Verifying image files: $index / $($files.Count)" }
        $info=Get-Item -LiteralPath $file.PhysicalPath
        if ($info.Length -ne [long]$file.SizeBytes -or (Get-FileHash -LiteralPath $file.PhysicalPath).Hash.ToLowerInvariant() -cne $file.Sha256) { throw "File changed since preview: $($file.PhysicalPath)" }
    }
    $connection.Open()
    $transaction=$connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
    Execute "SET XACT_ABORT ON; SET LOCK_TIMEOUT 30000;
DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'GSTORE-INITIAL-IMPORT-V2',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
IF @r<0 THROW 55400,'Another migration is running.',1;
IF (SELECT COUNT_BIG(*) FROM dbo.Stores WITH(UPDLOCK,HOLDLOCK))<>1 OR NOT EXISTS(SELECT 1 FROM dbo.Stores WHERE Id=1 AND IsDeleted=0) THROW 55400,'Single StoreId=1 required.',1;"
    $sourceQuoted='['+$preview.SourceDatabase.Replace(']',']]')+']'
    Execute "DECLARE @n bigint; SELECT @n=COUNT_BIG(*) FROM $sourceQuoted.dbo.ProductDetail WITH(TABLOCK,HOLDLOCK);"
    $coreConfig=Get-Content -Raw -LiteralPath (Join-Path $scriptDirectory '../initial-import/01-products/package.json') | ConvertFrom-Json
    foreach ($table in @($coreConfig.TargetTables)+@('MediaAssets','ProductImages')) { Execute "DECLARE @n bigint; SELECT @n=COUNT_BIG(*) FROM dbo.[$table] WITH(TABLOCKX,HOLDLOCK);" }
    $journal=Read-Table "SELECT SourceDatabase,SqlSha256,SourceHashes,TargetHashes FROM dbo.GaoStoreMigrationRunsV2 WHERE PackageId=N'GSTORE-01-products-V2';"
    if ($journal.Rows.Count -ne 1) { throw 'Committed product import receipt is required.' }
    $prior=$journal.Rows[0]
    if ($prior.SourceDatabase -ne $preview.SourceDatabase -or $prior.SqlSha256 -cne (Get-FileHash -LiteralPath (Join-Path $scriptDirectory '../initial-import/01-products/Migration.sql')).Hash) { throw 'Product receipt does not match source/package.' }
    $sourceHashes=$prior.SourceHashes | ConvertFrom-Json
    if ((Hash-Table $preview.SourceDatabase 'ProductDetail') -cne $sourceHashes.ProductDetail) { throw 'Source catalogue changed since product import. Review a new snapshot.' }
    $expectedCore=$prior.TargetHashes | ConvertFrom-Json
    $beforeCore=[ordered]@{}
    foreach ($table in $coreConfig.TargetTables) { $beforeCore[$table]=Hash-Table $preview.TargetDatabase $table }
    $columns=(Read-Table "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ProductVariant') AND name NOT IN(N'PrimaryProductImageId',N'RowVersion') ORDER BY column_id;").Rows
    $nonImageColumns=($columns | ForEach-Object { 't.['+([string]$_.name).Replace(']',']]')+']' }) -join ','
    $otherBefore=Hash-Table $preview.TargetDatabase 'ProductVariant' $nonImageColumns
    $receipt=$null
    if ([int](Scalar "SELECT CASE WHEN OBJECT_ID(N'dbo.GaoStoreProductImageRunsV1',N'U') IS NULL THEN 0 ELSE 1 END;") -eq 1) {
        $receipts=Read-Table 'SELECT * FROM dbo.GaoStoreProductImageRunsV1 WHERE StoreId=1;'
        if ($receipts.Rows.Count -gt 1) { throw 'Duplicate image receipt.' }
        if ($receipts.Rows.Count -eq 1) { $receipt=$receipts.Rows[0] }
    }
    foreach ($table in $coreConfig.TargetTables) {
        $expected=$expectedCore.$table
        if ($table -eq 'ProductVariant' -and $null -ne $receipt) {
            if ($receipt.ProductVariantBeforeHash -cne $expected) { throw 'Image receipt product baseline differs.' }
            $expected=$receipt.ProductVariantAfterHash
        }
        if ($beforeCore[$table] -cne $expected) { throw "Catalogue changed outside the reviewed import: $table" }
    }
    $mediaBefore=Hash-Table $preview.TargetDatabase 'MediaAssets'
    $imagesBefore=Hash-Table $preview.TargetDatabase 'ProductImages'
    if ($null -ne $receipt) {
        if ($receipt.SourceDatabase -ne $preview.SourceDatabase -or $receipt.PreviewManifestSha256 -cne $previewHash -or
            $receipt.SqlSha256 -cne $sqlHash -or $receipt.ProductVariantOtherColumnsHash -cne $otherBefore -or
            $receipt.MediaAssetsHash -cne $mediaBefore -or $receipt.ProductImagesHash -cne $imagesBefore) { throw 'Existing image import differs; no overwrite is allowed.' }
    } elseif ($Mode -eq 'VERIFY') { throw 'No committed image receipt.' }
    $apply=$null -eq $receipt
    $cmd=Command $sql
    [void]$cmd.Parameters.Add('@PlanJson',[System.Data.SqlDbType]::NVarChar,-1); $cmd.Parameters['@PlanJson'].Value=$planJson
    [void]$cmd.Parameters.Add('@StoreId',[System.Data.SqlDbType]::Int); $cmd.Parameters['@StoreId'].Value=1
    [void]$cmd.Parameters.Add('@Apply',[System.Data.SqlDbType]::Bit); $cmd.Parameters['@Apply'].Value=$apply
    $adapter=[System.Data.SqlClient.SqlDataAdapter]::new($cmd); $verified=[System.Data.DataTable]::new()
    try { [void]$adapter.Fill($verified) } finally { $adapter.Dispose(); $cmd.Dispose() }
    $verified | Select-Object -Property $verified.Columns.ColumnName | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'exact-verification.csv') -NoTypeInformation -Encoding UTF8
    $verified | Format-Table -AutoSize | Out-String | Write-Output
    $otherAfter=Hash-Table $preview.TargetDatabase 'ProductVariant' $nonImageColumns
    if ($otherAfter -cne $otherBefore) { throw 'ProductVariant fields other than primary image and rowversion changed.' }
    foreach ($table in $coreConfig.TargetTables) {
        if ($table -ne 'ProductVariant' -and (Hash-Table $preview.TargetDatabase $table) -cne $beforeCore[$table]) { throw "Protected catalogue changed: $table" }
    }
    $variantAfter=Hash-Table $preview.TargetDatabase 'ProductVariant'
    $mediaAfter=Hash-Table $preview.TargetDatabase 'MediaAssets'
    $imagesAfter=Hash-Table $preview.TargetDatabase 'ProductImages'
    $manifest['ProductVariantBeforeHash']=$beforeCore.ProductVariant; $manifest['ProductVariantAfterHash']=$variantAfter
    $manifest['OtherVariantColumnsUnchanged']=$true
    if ($apply) {
        Execute "IF OBJECT_ID(N'dbo.GaoStoreProductImageRunsV1',N'U') IS NULL
CREATE TABLE dbo.GaoStoreProductImageRunsV1(StoreId int NOT NULL PRIMARY KEY,SourceDatabase sysname NOT NULL,
PreviewManifestSha256 char(64) NOT NULL,SqlSha256 char(64) NOT NULL,
ProductVariantBeforeHash varchar(100) NOT NULL,ProductVariantAfterHash varchar(100) NOT NULL,
ProductVariantOtherColumnsHash varchar(100) NOT NULL,MediaAssetsHash varchar(100) NOT NULL,ProductImagesHash varchar(100) NOT NULL,
CommittedAtUtc datetime2 NOT NULL);"
        $cmd=Command 'INSERT dbo.GaoStoreProductImageRunsV1 VALUES(1,@Source,@Preview,@Sql,@Before,@After,@Other,@Media,@Images,SYSUTCDATETIME());'
        foreach ($entry in @(@('@Source',$preview.SourceDatabase),@('@Preview',$previewHash),@('@Sql',$sqlHash),@('@Before',$beforeCore.ProductVariant),@('@After',$variantAfter),@('@Other',$otherAfter),@('@Media',$mediaAfter),@('@Images',$imagesAfter))) { [void]$cmd.Parameters.AddWithValue($entry[0],[string]$entry[1]) }
        try { [void]$cmd.ExecuteNonQuery() } finally { $cmd.Dispose() }
    }
    if ($Mode -eq 'COMMIT' -and $apply) {
        $manifest['Status']='READY_TO_COMMIT'
        $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'manifest.json') -Encoding UTF8
        $commitAttempted=$true
        Execute 'COMMIT TRANSACTION;'
        $commitConfirmed=$true; $transaction.Dispose(); $transaction=$null
        $manifest['Status']='IMAGE_COMMIT_PASS'
    } else {
        Execute 'ROLLBACK TRANSACTION;'; $transaction.Dispose(); $transaction=$null
        if ($apply) {
            foreach ($table in $coreConfig.TargetTables) { if ((Hash-Table $preview.TargetDatabase $table) -cne $beforeCore[$table]) { throw "Post-rollback catalogue differs: $table" } }
            if ((Hash-Table $preview.TargetDatabase 'MediaAssets') -cne $mediaBefore -or
                (Hash-Table $preview.TargetDatabase 'ProductImages') -cne $imagesBefore -or
                [int](Scalar "IF OBJECT_ID(N'dbo.GaoStoreProductImageRunsV1',N'U') IS NULL SELECT 0; ELSE EXEC(N'SELECT COUNT(*) FROM dbo.GaoStoreProductImageRunsV1 WHERE StoreId=1;');") -ne 0) { throw 'Post-rollback image state differs.' }
        }
        $manifest['Status']=if ($Mode -eq 'VERIFY') {'IMAGE_VERIFY_PASS'} elseif ($apply) {'IMAGE_DRYRUN_PASS_ROLLED_BACK'} else {'IMAGE_ALREADY_IMPORTED_NO_CHANGE'}
    }
    Write-Output $manifest.Status
} catch {
    $failure=$_
    if ($null -ne $transaction) {
        try { if ($null -ne $transaction.Connection) { Execute 'IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;' } } catch { $manifest['RollbackError']=$_.Exception.Message }
        $transaction.Dispose(); $transaction=$null
    }
    $manifest['Status']=if($commitConfirmed){'IMAGE_COMMITTED_REPORT_FAILED'}elseif($commitAttempted){'IMAGE_COMMIT_OUTCOME_REQUIRES_REVIEW'}else{'IMAGE_FAILED'}
    $manifest['Error']=$failure.Exception.Message
    throw $failure
} finally {
    $connection.Dispose()
    $manifest['FinishedAtUtc']=[datetime]::UtcNow.ToString('o')
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'manifest.json') -Encoding UTF8
    Write-Output "Evidence: $([IO.Path]::GetFullPath($OutputDirectory))"
}
