[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][ValidateSet('Inspect','BackupSource','BackupTarget','VerifySourceBackup','VerifyTargetBackup','ResetPreview','ResetDryRun','ResetCommit','ArchiveSchema','01-products','02-customers','03-sales','03-return-archive','04-inventory','05-invoice-stock','06-invoices','ImagePreview','Images','VerifyAll','ReadVerify')][string]$Step,
 [ValidateSet('PREVIEW','DRYRUN','COMMIT','VERIFY')][string]$Mode='PREVIEW',
 [string]$BackupFile,[switch]$AllowCommit,[switch]$CheckSetup
)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$root=Split-Path -Parent $PSCommandPath
$config=Get-Content -LiteralPath (Join-Path $root 'config.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($config.Server -ne $config.ExpectedServer -or $config.Server -ne 'WIN-HU6RO2EMIJF\SQLEXPRESS'){throw 'This handoff is bound to WIN-HU6RO2EMIJF\SQLEXPRESS. Do not point it at the rehearsal machine.'}
if($config.SourceDatabase -ne 'DataGaoStore' -or $config.TargetDatabase -ne 'GaoAppDb' -or $config.StoreId -ne 1 -or $config.WarehouseId -ne 1 -or $config.LegalEntityId -ne 1){throw 'Database/store contract differs from the reviewed handoff.'}
if($config.RunName -notmatch '^[A-Za-z0-9_-]{1,60}$'){throw 'Invalid RunName.'}
& (Join-Path $root 'Verify-Package.ps1') -PackageRoot $root
if($CheckSetup){Write-Output "RELEASE_SETUP_PASS: $Step / $Mode; $($config.Server); no SQL connection or execution.";return}
$runs=Join-Path $root ('runs/'+$config.RunName)
[void][IO.Directory]::CreateDirectory($runs)
$statePath=Join-Path $runs 'state.json'
if(Test-Path -LiteralPath $statePath){$state=Get-Content -Raw -LiteralPath $statePath -Encoding UTF8|ConvertFrom-Json}else{$state=[pscustomobject]@{Server=$config.Server;SourceDatabase=$config.SourceDatabase;TargetDatabase=$config.TargetDatabase;ResetCommitted=$false}}
if($state.Server -ne $config.Server -or $state.SourceDatabase -ne $config.SourceDatabase -or $state.TargetDatabase -ne $config.TargetDatabase){throw 'Run state belongs to a different database contract.'}
function Set-State([string]$name,$value){$state|Add-Member NoteProperty $name $value -Force}
function Save-State {$state|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $statePath -Encoding UTF8}
function Remove-State([string]$name){$state.PSObject.Properties.Remove($name)}
function Get-State([string]$name){if($state.PSObject.Properties.Name -notcontains $name){throw "Missing successful prior step: $name"};return $state.$name}
function Invoke-PowerShell([string]$file,[object[]]$arguments){& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $file @arguments;if($LASTEXITCODE -ne 0){throw "Step failed: $file. Stop and review the output; do not continue."}}
$migration=Join-Path $root 'scripts/migration'
$initial=Join-Path $migration 'initial-import'
$output=Join-Path $runs ($Step+'-'+$Mode.ToLowerInvariant()+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$base=@('-Server',$config.Server,'-SourceDatabase',$config.SourceDatabase,'-TargetDatabase',$config.TargetDatabase)
$resetBase=@('-Server',$config.Server,'-ExpectedServer',$config.ExpectedServer,'-TargetDatabase',$config.TargetDatabase)
$importSteps=@('01-products','02-customers','03-sales','03-return-archive','04-inventory','05-invoice-stock','06-invoices')
$packageHash=(Get-FileHash -LiteralPath (Join-Path $root 'checksums.json') -Algorithm SHA256).Hash
try{
 if($Step -notin @('Inspect','ResetPreview','ArchiveSchema')){
  if((Get-State 'ReviewedPackageSha256') -cne $packageHash){throw 'Package changed. Obtain a fresh ResetPreview before starting this cutover; do not reuse old approvals.'}
 }
 if($Step -in $importSteps -or $Step -in @('ImagePreview','Images','VerifyAll','ReadVerify')){
  if(-not $state.ResetCommitted){throw 'Complete reviewed target preparation before importing.'}
  Invoke-PowerShell (Join-Path $root 'Invoke-DatabaseOperation.ps1') ($base+@('-Operation','TargetReady','-ExpectedServer',$config.ExpectedServer,'-OutputDirectory',($output+'-contract')))
 }
 if($Step -in @('Inspect','BackupSource','BackupTarget','VerifySourceBackup','VerifyTargetBackup')){
  if($Step -ne 'Inspect'){
   if($state.ResetCommitted){throw 'Pre-cutover backup steps cannot replace the reset baseline after reset commit.'}
   Remove-State 'ResetDryRunDirectory'
   if($Step -like '*Source*'){Remove-State 'VerifiedSourceBackup'}else{Remove-State 'VerifiedTargetBackup'}
   Save-State
  }
  $arguments=@('-Operation',$Step)+$base+@('-ExpectedServer',$config.ExpectedServer,'-OutputDirectory',$output)
  if($Step -like 'Verify*Backup'){
   if([string]::IsNullOrWhiteSpace($BackupFile)){$BackupFile=if($Step -eq 'VerifySourceBackup'){Get-State 'CreatedSourceBackup'}else{Get-State 'CreatedTargetBackup'}}
   $arguments+=@('-BackupFile',$BackupFile)
   $review=Get-Content -LiteralPath (Join-Path (Get-State 'ResetPreviewDirectory') 'manifest.json') -Raw|ConvertFrom-Json
   $arguments+=@('-NotBeforeUtc',$review.StartedAtUtc)
  }
  if(-not [string]::IsNullOrWhiteSpace($config.BackupDirectory)){$arguments+=@('-BackupDirectory',$config.BackupDirectory)}
  Invoke-PowerShell (Join-Path $root 'Invoke-DatabaseOperation.ps1') $arguments
  $report=Get-Content -LiteralPath (Join-Path $output 'manifest.json') -Raw|ConvertFrom-Json
  if($Step -in @('BackupSource','BackupTarget')){
   if($report.Status -ne 'BACKUP_CREATED_UNVERIFIED'){throw 'Backup did not finish.'}
   $key=if($Step -eq 'BackupSource'){'CreatedSourceBackup'}else{'CreatedTargetBackup'}
   Set-State $key $report.BackupFile
  }
  if($Step -like 'Verify*Backup'){
   if($report.Status -ne 'BACKUP_VERIFYONLY_PASS'){throw 'Backup verification did not pass.'}
   $key=if($Step -eq 'VerifySourceBackup'){'VerifiedSourceBackup'}else{'VerifiedTargetBackup'}
   Set-State $key $report.BackupFile
  }
 }elseif($Step -eq 'ResetPreview'){
  if($state.ResetCommitted){throw 'Reset already committed in this run. Resume imports; never reset to retry a later step.'}
  if(-not $config.TargetContainsOnlyTestBusinessData){throw 'Reset is only for the target containing TEST business data.'}
  foreach($key in @('ResetPreviewDirectory','ResetDryRunDirectory','ReviewedPackageSha256','VerifiedSourceBackup','VerifiedTargetBackup')){Remove-State $key};Save-State
  Invoke-PowerShell (Join-Path $initial '00-Preview-Reset.ps1') @('-Server',$config.Server,'-TargetDatabase',$config.TargetDatabase,'-OutputDirectory',$output,'-AllowImportedTestReset')
  $preview=Get-Content -LiteralPath (Join-Path $output 'manifest.json') -Raw|ConvertFrom-Json
  if($preview.Status -ne 'PREVIEW_READY_FOR_REVIEW'){throw 'Reset preview is blocked. Review its blockers before continuing.'}
  Set-State 'ResetPreviewDirectory' $output
  Set-State 'ReviewedPackageSha256' $packageHash
 }elseif($Step -in @('ResetDryRun','ResetCommit')){
  if($state.ResetCommitted){throw 'Reset already committed. Do not repeat reset.'}
  $null=Get-State 'VerifiedSourceBackup'
  $arguments=$resetBase+@('-PreviewDirectory',(Get-State 'ResetPreviewDirectory'),'-BackupFile',(Get-State 'VerifiedTargetBackup'),'-OutputDirectory',$output)
  if($Step -eq 'ResetCommit'){
   if(-not $AllowCommit){throw 'ResetCommit requires -AllowCommit after reviewing successful ResetDryRun.'}
   Invoke-PowerShell (Join-Path $initial '00-Commit-Reset-LowLog.ps1') ($arguments+@('-SuccessfulDryRunDirectory',(Get-State 'ResetDryRunDirectory'),'-AllowCommit'))
   Set-State 'ResetCommitted' $true
  }else{Invoke-PowerShell (Join-Path $initial '00-DryRun-Reset-LowLog.ps1') $arguments;Set-State 'ResetDryRunDirectory' $output}
 }elseif($Step -eq 'ArchiveSchema'){
  throw 'This release requires the existing 20260928 schema. Do not apply old ArchiveSchema scripts; run Inspect.'
 }elseif($Step -in $importSteps){
  if(-not $state.ResetCommitted){throw 'Complete reviewed target preparation before importing.'}
  $index=[array]::IndexOf($importSteps,$Step)
  if($index -gt 0){$null=Get-State ($importSteps[$index-1]+'-VERIFY')}
  if($Mode -eq 'DRYRUN'){$null=Get-State ($Step+'-PREVIEW')}
  if($Mode -eq 'VERIFY'){$null=Get-State ($Step+'-COMMIT')}
  if($Mode -eq 'COMMIT'){
   if(-not $AllowCommit){throw 'COMMIT requires -AllowCommit after reviewing DRYRUN.'}
   $null=Get-State ($Step+'-DRYRUN')
  }
  $arguments=$base+@('-Mode',$Mode,'-OutputDirectory',$output)
  if($Mode -eq 'COMMIT'){$arguments+='-AllowCommit'}
  if($Step -eq '05-invoice-stock'){$file=Join-Path $migration 'invoice-input-stock/Invoke-Migration.ps1';$arguments+=@('-StoreId','1','-WarehouseId','1')}
  elseif($Step -eq '06-invoices'){$file=Join-Path $migration 'invoices/Invoke-Migration.ps1';$arguments+=@('-StoreId','1','-WarehouseId','1','-LegalEntityId','1')}
  else{$file=Join-Path $initial 'Invoke-Migration.ps1';$arguments+=@('-Package',$Step)}
  Invoke-PowerShell $file $arguments
  Set-State ($Step+'-'+$Mode) $output
 }elseif($Step -eq 'ImagePreview'){
  $null=Get-State '01-products-VERIFY'
  Remove-State 'ImagePreviewDirectory';Remove-State 'ImageDryRunDirectory';Save-State
  Invoke-PowerShell (Join-Path $migration 'product-images/Preview-Images.ps1') ($base+@('-StoreId','1','-LegacyImageRoot',$config.LegacyImageRoot,'-OutputDirectory',$output))
  $preview=Get-Content -LiteralPath (Join-Path $output 'manifest.json') -Raw|ConvertFrom-Json
  if($preview.Status -ne 'IMAGE_PREVIEW_READY_FOR_REVIEW'){throw 'Image preview requires review. Resolve its findings before continuing.'}
  Set-State 'ImagePreviewDirectory' $output
 }elseif($Step -eq 'Images'){
  if($Mode -eq 'PREVIEW'){throw 'Use -Step ImagePreview.'}
  $arguments=@('-PreviewDirectory',(Get-State 'ImagePreviewDirectory'),'-Mode',$Mode,'-OutputDirectory',$output)
  if($Mode -eq 'COMMIT'){
   if(-not $AllowCommit){throw 'Image COMMIT requires -AllowCommit.'}
   $arguments+=@('-AllowCommit','-SuccessfulDryRunDirectory',(Get-State 'ImageDryRunDirectory'))
  }
  Invoke-PowerShell (Join-Path $migration 'product-images/Invoke-Images.ps1') $arguments
  if($Mode -eq 'DRYRUN'){Set-State 'ImageDryRunDirectory' $output}
 }elseif($Step -eq 'VerifyAll'){
  foreach($package in $importSteps){$null=Get-State ($package+'-COMMIT')}
  foreach($package in $importSteps){
   $folder=Join-Path $output $package
   $arguments=$base+@('-Mode','VERIFY','-OutputDirectory',$folder)
   if($package -eq '05-invoice-stock'){$file=Join-Path $migration 'invoice-input-stock/Invoke-Migration.ps1'}
   elseif($package -eq '06-invoices'){$file=Join-Path $migration 'invoices/Invoke-Migration.ps1'}
   else{$file=Join-Path $initial 'Invoke-Migration.ps1';$arguments+=@('-Package',$package)}
   Invoke-PowerShell $file $arguments
  }
  Invoke-PowerShell (Join-Path $migration 'product-images/Invoke-Images.ps1') @('-PreviewDirectory',(Get-State 'ImagePreviewDirectory'),'-Mode','VERIFY','-OutputDirectory',(Join-Path $output 'images'))
  Write-Output 'FINAL_CHAIN_VERIFY_PASS'
 }elseif($Step -eq 'ReadVerify'){
  $null=Get-State 'FinalChainVerified'
  [void][IO.Directory]::CreateDirectory($output)
  & dotnet (Join-Path $root 'tools/invoice-verifier/Verifier.dll') $config.Server $config.TargetDatabase $config.SourceDatabase 1 | Tee-Object -FilePath (Join-Path $output 'invoice-stock-read.txt')
  if($LASTEXITCODE -ne 0){throw 'Invoice/stock read verifier failed.'}
  & dotnet (Join-Path $root 'tools/return-verifier/Verifier.dll') $config.Server $config.TargetDatabase | Tee-Object -FilePath (Join-Path $output 'return-archive-read.txt')
  if($LASTEXITCODE -ne 0){throw 'Return archive read verifier failed.'}
 }
 Set-State 'LastSuccessfulStep' ($Step+'/'+$Mode)
 if($Step -eq 'VerifyAll'){Set-State 'FinalChainVerified' $output}
 $state|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $statePath -Encoding UTF8
 Write-Output "STEP_FINISHED: $Step / $Mode"
}catch{throw}
