param([Parameter(Mandatory=$true)][string]$PatchRoot)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$patch=[IO.Path]::GetFullPath($PatchRoot)
$fixture=Join-Path $repo ('.artifacts/supplier-patch-test-'+[guid]::NewGuid().ToString('N'))
Copy-Item -LiteralPath (Join-Path $repo '.artifacts/handoff-WIN-20260929-final/MIGRATION') -Destination $fixture -Recurse
$checks=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw $name};$script:checks++;Write-Output "PASS: $name"}
foreach($f in Get-ChildItem -LiteralPath $patch -Filter '*.ps1'){
 $t=$null;$e=$null;$null=[Management.Automation.Language.Parser]::ParseFile($f.FullName,[ref]$t,[ref]$e)
 Check ($e.Count -eq 0) ('PS5 syntax '+$f.Name)
}
# Parse actual T-SQL, without SQL Server access.
$dom='C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\Application\Microsoft.SqlServer.TransactSql.ScriptDom.dll'
Add-Type -Path $dom
$parser=[Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser]::new($true)
$reader=[IO.StringReader]::new([IO.File]::ReadAllText((Join-Path $patch 'Migration.sql')))
$errors=$null;$null=$parser.Parse($reader,[ref]$errors);$reader.Dispose()
if($errors.Count){$errors|Format-Table Line,Column,Message|Out-String|Write-Output}
Check ($errors.Count -eq 0) 'Patched T-SQL parses (SQL Server 2022 grammar)'
$runs=Join-Path $fixture 'runs/real-01';[void][IO.Directory]::CreateDirectory($runs)
$statePath=Join-Path $runs 'state.json';$manifestPath=Join-Path $fixture 'checksums.json'
$state=[pscustomobject]@{Server='WIN-HU6RO2EMIJF\SQLEXPRESS';SourceDatabase='DataGaoStore';TargetDatabase='GaoAppDb';ResetCommitted=$true;ReviewedPackageSha256=(Get-FileHash -LiteralPath $manifestPath).Hash;ResetPreviewDirectory='OFFLINE_ONLY_PREVIEW';VerifiedSourceBackup='C:\Data\DataGaoStore.bak';SourceBackupEvidence='OFFLINE_ONLY_SOURCE_RECEIPT';TargetBackupWaivedByUser=$true;'01-products-PREVIEW'='OLD_PREVIEW';'01-products-DRYRUN'='OLD_DRYRUN'}
$state|ConvertTo-Json|Set-Content -LiteralPath $statePath -Encoding UTF8
$original=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
$beforeSql=Join-Path $fixture 'scripts/migration/initial-import/01-products/Migration.sql'
$originalSqlHash=(Get-FileHash -LiteralPath $beforeSql).Hash
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $patch 'Install-Supplier-Name-Patch.ps1') -PackageRoot $fixture
Check ($LASTEXITCODE -eq 0) 'Install on isolated fixture only (no SQL)'
$after=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
$changed=@(foreach($f in $original.Files){$new=@($after.Files|Where-Object Path -eq $f.Path);if($new.Count -ne 1 -or $f.Sha256 -cne $new[0].Sha256){$f.Path}})
Check ($changed.Count -eq 1 -and $changed[0] -eq 'scripts/migration/initial-import/01-products/Migration.sql') 'Only product SQL changed among 373 files'
$result=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json
Check ($result.ResetCommitted -eq $true -and $result.ResetPreviewDirectory -ceq $state.ResetPreviewDirectory) 'Committed reset and its preview remain intact'
Check ($result.VerifiedSourceBackup -ceq $state.VerifiedSourceBackup -and $result.SourceBackupEvidence -ceq $state.SourceBackupEvidence -and $result.TargetBackupWaivedByUser -eq $true) 'Source backup evidence and target waiver preserved'
Check ($result.PSObject.Properties.Name -notcontains '01-products-PREVIEW' -and $result.PSObject.Properties.Name -notcontains '01-products-DRYRUN') 'Product approvals invalidated, requiring new preview/dryrun'
Check ($result.ReviewedPackageSha256 -ceq (Get-FileHash -LiteralPath $manifestPath).Hash) 'Updated package hash registered without reset'
$blocked=$false;try{& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $patch 'Install-Supplier-Name-Patch.ps1') -PackageRoot $fixture 2>&1|Out-Null;$blocked=$LASTEXITCODE -ne 0}catch{$blocked=$true}
Check $blocked 'Repeat/unknown package patch refused'
$receipt=Get-Content -LiteralPath $result.SupplierNamePatchEvidence -Raw|ConvertFrom-Json
Check ($receipt.DatabaseWrites -eq $false -and $receipt.ResetRepeated -eq $false) 'Patch receipt distinguishes file update from database import'
$history=Split-Path -Parent $result.SupplierNamePatchEvidence
Copy-Item -LiteralPath (Join-Path $history 'Migration.sql.before') -Destination $beforeSql -Force
Copy-Item -LiteralPath (Join-Path $history 'checksums.before.json') -Destination $manifestPath -Force
$state|Add-Member NoteProperty '01-products-COMMIT' 'OFFLINE_ALREADY_COMMITTED' -Force
$state|ConvertTo-Json|Set-Content -LiteralPath $statePath -Encoding UTF8
$blocked=$false;try{& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $patch 'Install-Supplier-Name-Patch.ps1') -PackageRoot $fixture 2>&1|Out-Null;$blocked=$LASTEXITCODE -ne 0}catch{$blocked=$true}
Check $blocked 'Patch refuses committed products'
Check ((Get-FileHash -LiteralPath $beforeSql).Hash -ceq $originalSqlHash) 'Rejected patch leaves SQL unchanged'
Write-Output "SUPPLIER_PATCH_OFFLINE_PASS: $checks checks; no database connection; fixture=$fixture"
