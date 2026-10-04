[CmdletBinding()]
param([string]$PackageRoot='C:\GaoMigration-20260929')
# FILES ONLY. No SQL connection, account creation, reset or data import.
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$here=Split-Path -Parent $PSCommandPath
& (Join-Path $here 'Verify-Package.ps1') -PackageRoot $here
$root=[IO.Path]::GetFullPath($PackageRoot)
$manifestPath=Join-Path $root 'checksums.json'
& (Join-Path $root 'Verify-Package.ps1') -PackageRoot $root
$oldHash=(Get-FileHash -LiteralPath $manifestPath).Hash
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
$expected=Get-Content -LiteralPath (Join-Path $here 'expected-package-files.json') -Raw -Encoding UTF8|ConvertFrom-Json
# Earlier installer timestamps make the full manifest hash server-specific.
# Pin every one of the 373 payload hashes instead; accept no other changes.
if($manifest.AppliedPatch -cne 'EMPLOYEE-MAPPING-20260929-V1' -or $manifest.Files.Count -ne 373 -or $expected.Files.Count -ne 373){
 throw 'Requires the reviewed 373-file package with the employee patch, before the inventory exception patch.'
}
foreach($f in $expected.Files){
 $entry=@($manifest.Files|Where-Object {$_.Path.Replace('\','/') -ceq $f.Path.Replace('\','/')})
 if($entry.Count -ne 1 -or $entry[0].Sha256 -cne $f.Sha256 -or $entry[0].Bytes -ne $f.Bytes){
  throw "Package differs from the reviewed employee-patched baseline: $($f.Path)"
 }
}
$config=Get-Content -LiteralPath (Join-Path $root 'config.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($config.Server -ine 'WIN-HU6RO2EMIJF\SQLEXPRESS' -or $config.ExpectedServer -ine $config.Server -or
 $config.SourceDatabase -cne 'DataGaoStore' -or $config.TargetDatabase -cne 'GaoAppDb' -or
 $config.StoreId -ne 1 -or $config.WarehouseId -ne 1 -or $config.LegalEntityId -ne 1 -or
 $config.RunName -notmatch '^[A-Za-z0-9_-]{1,60}$'){throw 'Unexpected migration configuration.'}
$runs=Join-Path $root ('runs/'+$config.RunName);$statePath=Join-Path $runs 'state.json'
$stateHash=(Get-FileHash -LiteralPath $statePath).Hash
$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
if($state.ResetCommitted -ne $true -or $state.ReviewedPackageSha256 -cne $oldHash -or
 $state.Server -ine $config.Server -or $state.SourceDatabase -cne $config.SourceDatabase -or
 $state.TargetDatabase -cne $config.TargetDatabase){throw 'Existing run state does not match this successfully reset package.'}
if(@($state.PSObject.Properties.Name|Where-Object {$_ -match '^(04-inventory|05-invoice-stock|06-invoices)-(COMMIT|VERIFY)$'}).Count){
 throw 'Inventory or a later step already committed. Do not patch or reset; review its receipt.'
}
foreach($package in @('01-products','02-customers','03-sales','03-return-archive')){
 $sqlEntry=@($manifest.Files|Where-Object {$_.Path.Replace('\','/') -ceq "scripts/migration/initial-import/$package/Migration.sql"})[0]
 foreach($mode in @('COMMIT','VERIFY')){
  $key=$package+'-'+$mode
  if($state.PSObject.Properties.Name -notcontains $key){throw "Missing successful $key."}
  $report=Get-Content -LiteralPath (Join-Path $state.$key 'manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
  if($report.Status -cne ($mode+'_PASS') -or $report.Package -cne $package -or $report.Mode -cne $mode -or
   $report.Server -ine $config.Server -or $report.SourceDatabase -cne $config.SourceDatabase -or
   $report.TargetDatabase -cne $config.TargetDatabase -or $report.SqlSha256 -cne $sqlEntry.Sha256){
   throw "Receipt does not confirm the existing $key. No files changed."
  }
 }
}
$replacements=@(
 @{Payload='Migration.sql';Relative='scripts/migration/initial-import/04-inventory/Migration.sql'}
)
$output=Join-Path $runs ('Inventory-negative-patch-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($output)
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $output 'checksums.before.json')
Copy-Item -LiteralPath $statePath -Destination (Join-Path $output 'state.before.json')
foreach($item in $replacements){
 Copy-Item -LiteralPath (Join-Path $root $item.Relative) -Destination (Join-Path $output ($item.Payload+'.before'))
}
$receipt=[ordered]@{Status='STARTED';Patch='INVENTORY-NEGATIVE-20260929-V1';DatabaseWrites=$false;ResetRepeated=$false;
 OriginalPackageSha256=$oldHash;StartedAtUtc=[datetime]::UtcNow.ToString('o');ChangedFiles=@($replacements|ForEach-Object {$_.Relative});
 ApprovedNegativeOrders=@(1825885,1832994);ExpectedMerchandiseLines=4;ExpectedQuantityOut=11}
$changed=$false
try{
 if((Get-FileHash -LiteralPath $statePath).Hash -cne $stateHash -or (Get-FileHash -LiteralPath $manifestPath).Hash -cne $oldHash){
  throw 'Run state changed concurrently. Stop other migration commands.'
 }
 $changed=$true
 foreach($item in $replacements){
  $target=Join-Path $root $item.Relative
  Copy-Item -LiteralPath (Join-Path $here $item.Payload) -Destination $target -Force
  $entry=@($manifest.Files|Where-Object {$_.Path.Replace('\','/') -ceq $item.Relative})[0]
  $entry.Sha256=(Get-FileHash -LiteralPath $target).Hash;$entry.Bytes=(Get-Item -LiteralPath $target).Length
 }
 $manifest|Add-Member NoteProperty AppliedPatch 'INVENTORY-NEGATIVE-20260929-V1' -Force
 $manifest|Add-Member NoteProperty PreviousAppliedPatch 'EMPLOYEE-MAPPING-20260929-V1' -Force
 $manifest|Add-Member NoteProperty PatchedAtUtc ([datetime]::UtcNow.ToString('o')) -Force
 $manifest|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $manifestPath -Encoding UTF8
 $newHash=(Get-FileHash -LiteralPath $manifestPath).Hash
 # Keep all four completed package receipts and all reset/backup evidence.
 foreach($key in @('04-inventory-PREVIEW','04-inventory-DRYRUN','FinalChainVerified')){$state.PSObject.Properties.Remove($key)}
 $state|Add-Member NoteProperty ReviewedPackageSha256 $newHash -Force
 $state|Add-Member NoteProperty InventoryNegativePatchEvidence (Join-Path $output 'manifest.json') -Force
 $state|Add-Member NoteProperty LastSuccessfulStep 'InventoryNegativePatch/FILES_ONLY' -Force
 $state|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $statePath -Encoding UTF8
 & (Join-Path $root 'Verify-Package.ps1') -PackageRoot $root
 $receipt['Status']='INVENTORY_NEGATIVE_PATCH_PASS';$receipt['UpdatedPackageSha256']=$newHash
 Write-Output 'INVENTORY_NEGATIVE_PATCH_PASS. Run 04-inventory PREVIEW, then DRYRUN. Do not reset or repeat completed steps.'
}catch{
 $failure=$_;$receipt['Status']='FAILED';$receipt['Error']=$failure.Exception.Message
 if($changed){
  try{
   foreach($item in $replacements){
    Copy-Item -LiteralPath (Join-Path $output ($item.Payload+'.before')) -Destination (Join-Path $root $item.Relative) -Force
   }
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
