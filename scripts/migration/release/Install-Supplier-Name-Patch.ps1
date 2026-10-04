[CmdletBinding()]
param([string]$PackageRoot='C:\GaoMigration-20260929')
# File-only, narrowly scoped patch. No SQL connection, reset or business data write.
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$here=Split-Path -Parent $PSCommandPath
& (Join-Path $here 'Verify-Package.ps1') -PackageRoot $here
$root=[IO.Path]::GetFullPath($PackageRoot)
$manifestPath=Join-Path $root 'checksums.json'
$oldHash='FA43A454BB4CA6064A323C4A74C7B13E57B7B2A3896878D88C307319FD7AB983'
if((Get-FileHash -LiteralPath $manifestPath).Hash -cne $oldHash){throw 'Expected the original reviewed 373-file package; do not patch an unknown or already patched package.'}
& (Join-Path $root 'Verify-Package.ps1') -PackageRoot $root
$config=Get-Content -LiteralPath (Join-Path $root 'config.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($config.Server -ine 'WIN-HU6RO2EMIJF\SQLEXPRESS' -or $config.ExpectedServer -ine $config.Server -or
   $config.SourceDatabase -cne 'DataGaoStore' -or $config.TargetDatabase -cne 'GaoAppDb' -or
   $config.RunName -notmatch '^[A-Za-z0-9_-]{1,60}$'){throw 'Unexpected migration configuration.'}
$runs=Join-Path $root ('runs/'+$config.RunName);$statePath=Join-Path $runs 'state.json'
$stateHash=(Get-FileHash -LiteralPath $statePath).Hash
$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
if($state.ResetCommitted -ne $true -or $state.ReviewedPackageSha256 -cne $oldHash -or
   $state.Server -ine $config.Server -or $state.SourceDatabase -cne $config.SourceDatabase -or
   $state.TargetDatabase -cne $config.TargetDatabase){throw 'Patch requires the existing successfully reset target state.'}
if(@($state.PSObject.Properties.Name|Where-Object {$_ -match '^(01-products|02-customers|03-sales|03-return-archive|04-inventory|05-invoice-stock|06-invoices)-(COMMIT|VERIFY)$'}).Count){
 throw 'An import already committed. Review its receipt before changing SQL; never reset to apply this patch.'
}
$relative='scripts/migration/initial-import/01-products/Migration.sql'
$target=Join-Path $root $relative
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
$entry=@($manifest.Files|Where-Object {$_.Path.Replace('\','/') -ceq $relative})
if($entry.Count -ne 1){throw 'Expected exactly one product SQL entry.'}
$replacement=Join-Path $here 'Migration.sql'
$output=Join-Path $runs ('Supplier-name-patch-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($output)
Copy-Item -LiteralPath $target -Destination (Join-Path $output 'Migration.sql.before')
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $output 'checksums.before.json')
Copy-Item -LiteralPath $statePath -Destination (Join-Path $output 'state.before.json')
$receipt=[ordered]@{Status='STARTED';Patch='SUPPLIER-110276-NAME-V1';DatabaseWrites=$false;ResetRepeated=$false;OriginalPackageSha256=$oldHash;OriginalSqlSha256=$entry[0].Sha256;StartedAtUtc=[datetime]::UtcNow.ToString('o')}
$changed=$false
try{
 if((Get-FileHash -LiteralPath $statePath).Hash -cne $stateHash){throw 'State changed concurrently. Stop other migration commands.'}
 $changed=$true
 Copy-Item -LiteralPath $replacement -Destination $target -Force
 $entry[0].Sha256=(Get-FileHash -LiteralPath $target).Hash
 $entry[0].Bytes=(Get-Item -LiteralPath $target).Length
 $manifest|Add-Member NoteProperty AppliedPatch 'SUPPLIER-110276-NAME-V1' -Force
 $manifest|Add-Member NoteProperty PatchedAtUtc ([datetime]::UtcNow.ToString('o')) -Force
 $manifest|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $manifestPath -Encoding UTF8
 $newHash=(Get-FileHash -LiteralPath $manifestPath).Hash
 # Keep reset/source-backup evidence. Only this product step's uncommitted approvals expire.
 foreach($key in @('01-products-PREVIEW','01-products-DRYRUN','FinalChainVerified')){$state.PSObject.Properties.Remove($key)}
 $state|Add-Member NoteProperty ReviewedPackageSha256 $newHash -Force
 $state|Add-Member NoteProperty SupplierNamePatchEvidence (Join-Path $output 'manifest.json') -Force
 $state|Add-Member NoteProperty LastSuccessfulStep 'SupplierNamePatch/FILES_ONLY' -Force
 $state|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $statePath -Encoding UTF8
 & (Join-Path $root 'Verify-Package.ps1') -PackageRoot $root
 $receipt['Status']='SUPPLIER_NAME_PATCH_PASS';$receipt['UpdatedPackageSha256']=$newHash;$receipt['UpdatedSqlSha256']=$entry[0].Sha256
 Write-Output 'SUPPLIER_NAME_PATCH_PASS. Run 01-products PREVIEW, then DRYRUN. Do not reset again.'
}catch{
 $failure=$_
 $receipt['Status']='FAILED';$receipt['Error']=$failure.Exception.Message
 if($changed){
  try{
   Copy-Item -LiteralPath (Join-Path $output 'Migration.sql.before') -Destination $target -Force
   Copy-Item -LiteralPath (Join-Path $output 'checksums.before.json') -Destination $manifestPath -Force
   Copy-Item -LiteralPath (Join-Path $output 'state.before.json') -Destination $statePath -Force
   $receipt['FilesRestored']=$true
  }catch{$receipt['RestoreError']=$_.Exception.Message}
 }
 throw $failure
}finally{
 $receipt['FinishedAtUtc']=[datetime]::UtcNow.ToString('o')
 $receipt|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding UTF8
 Write-Output "Reports: $output"
}
