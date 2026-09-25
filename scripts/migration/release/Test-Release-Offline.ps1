[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PackageRoot)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$package=[IO.Path]::GetFullPath($PackageRoot)
$fixture=Join-Path $repo ('.artifacts/release-offline-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$checks=0
function Check([bool]$pass,[string]$name){if(-not $pass){throw $name};$script:checks++;Write-Output "PASS: $name"}
foreach($file in Get-ChildItem -LiteralPath $package -Recurse -Filter '*.ps1'){
 $tokens=$null;$errors=$null
 $null=[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
 Check ($errors.Count -eq 0) ('PS5 parse '+$file.Name)
}
& (Join-Path $package 'Verify-Package.ps1') -PackageRoot $package
$initial=Join-Path $package 'scripts/migration/initial-import'
$plan=Get-Content -LiteralPath (Join-Path $initial 'TABLE-PLAN.json') -Raw|ConvertFrom-Json
Check (@($plan.Tables|Where-Object Action -eq 'KEEP').Count -eq 26) '26 retained tables'
foreach($name in @('GaoStoreMigrationRunsV2','GaoStoreProductImageRunsV1','LegacyReturnArchives')){
 Check (@($plan.Tables|Where-Object { $_.Table -eq $name -and $_.Action -eq 'CLEAR' }).Count -eq 1) ('Fresh TEST reset includes '+$name)
}
# Synthetic reviewed-preview files only. These never authorize a database action.
$preview=Join-Path $fixture 'preview';[void][IO.Directory]::CreateDirectory($preview)
$old=Join-Path $repo 'scripts/migration/initial-import/evidence/reset-preview-20260923-145830-707'
foreach($name in @('tables.json','foreign-keys.json','schema.json','environment.json','manifest.json')){Copy-Item -LiteralPath (Join-Path $old $name) -Destination (Join-Path $preview $name)}
$tables=[object[]](Get-Content -LiteralPath (Join-Path $preview 'tables.json') -Raw|ConvertFrom-Json)
foreach($name in @('LegacyReturnArchives','GaoStoreMigrationRunsV2','GaoStoreProductImageRunsV1')){
 if($name -notin $tables.Table){$tables+=[pscustomobject]@{Schema='dbo';Table=$name;Action='CLEAR';ApproximateRows=1}}
}
$tables|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $preview 'tables.json') -Encoding UTF8
$m=Get-Content -LiteralPath (Join-Path $preview 'manifest.json') -Raw|ConvertFrom-Json
$m.PlanSha256=(Get-FileHash -LiteralPath (Join-Path $initial 'TABLE-PLAN.json')).Hash
$m|Add-Member NoteProperty AllowImportedTestReset $true -Force
$m|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $preview 'manifest.json') -Encoding UTF8
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $initial '00-DryRun-Reset-LowLog.ps1') -PreviewDirectory $preview -CheckSetup
Check ($LASTEXITCODE -eq 0) 'Portable reset compiles C# under PS5 without SQL'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $initial 'Test-Reset-Truncate-Offline.ps1') -PreviewDirectory $preview
Check ($LASTEXITCODE -eq 0) 'FK planner with extra receipt tables, no SQL'
$approvalDir=Join-Path $fixture 'fake-approval';[void][IO.Directory]::CreateDirectory($approvalDir)
$oldDry=Join-Path $repo 'scripts/migration/initial-import/evidence/reset-lowlog-dryrun-20260923-184533-092'
$approval=Get-Content -LiteralPath (Join-Path $oldDry 'manifest.json') -Raw|ConvertFrom-Json
$approval.Server='WIN-HU6RO2EMIJF\SQLEXPRESS'
$approval|Add-Member NoteProperty ExpectedServer $approval.Server -Force
$approval|Add-Member NoteProperty AllowImportedTestReset $true -Force
$approval.ScriptSha256=(Get-FileHash -LiteralPath (Join-Path $initial '00-DryRun-Reset-LowLog.ps1')).Hash
$approval.PlanSha256=$m.PlanSha256
$approval.PreviewManifestSha256=(Get-FileHash -LiteralPath (Join-Path $preview 'manifest.json')).Hash
$approval|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $approvalDir 'manifest.json') -Encoding UTF8
$baseline=[ordered]@{}
foreach($t in $tables){$baseline[$t.Table]='0:E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855'}
foreach($name in @('before-hashes.json','after-rollback-hashes.json')){$baseline|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $approvalDir $name) -Encoding UTF8}
foreach($name in @('foreign-keys-before.json','identities-before.json')){Copy-Item -LiteralPath (Join-Path $oldDry $name) -Destination (Join-Path $approvalDir $name)}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $initial '00-Commit-Reset-LowLog.ps1') -Server 'WIN-HU6RO2EMIJF\SQLEXPRESS' -ExpectedServer 'WIN-HU6RO2EMIJF\SQLEXPRESS' -PreviewDirectory $preview -SuccessfulDryRunDirectory $approvalDir -CheckSetup
Check ($LASTEXITCODE -eq 0) 'Reset COMMIT setup validates matching approval without SQL'
foreach($pkg in @('01-products','02-customers','03-sales','03-return-archive','04-inventory')){
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $initial 'Invoke-Migration.ps1') -Package $pkg -SourceDatabase DataGaoStore -TargetDatabase GaoAppDb -Server 'WIN-HU6RO2EMIJF\SQLEXPRESS' -CheckSetup
 Check ($LASTEXITCODE -eq 0) ('Core offline setup '+$pkg)
}
foreach($name in @('Test-Image-Paths-Offline.ps1','Test-Image-Receipt-Offline.ps1')){
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $package ('scripts/migration/product-images/'+$name))
 Check ($LASTEXITCODE -eq 0) $name
}
# Exercise actual wrapper dispatch against isolated fake scripts. Fake endpoints
# have no SQL/network code, and every receipt says this is an offline fixture.
$fake=Join-Path $fixture 'wrapper';[void][IO.Directory]::CreateDirectory($fake)
foreach($name in @('Run-Step.ps1','Verify-Package.ps1','config.json')){Copy-Item -LiteralPath (Join-Path $package $name) -Destination (Join-Path $fake $name)}
$stub=@'
param([string]$Operation,[string]$Server,[string]$ExpectedServer,[string]$SourceDatabase,[string]$TargetDatabase,[string]$OutputDirectory,[string]$BackupDirectory,[string]$Package,[string]$Mode,[string]$PreviewDirectory,[string]$BackupFile,[string]$SuccessfulDryRunDirectory,[string]$LegacyImageRoot,[int]$StoreId,[int]$WarehouseId,[int]$LegalEntityId,[switch]$AllowCommit,[switch]$AllowImportedTestReset,[switch]$DryRun)
$ErrorActionPreference='Stop'
if($OutputDirectory){
 [void][IO.Directory]::CreateDirectory($OutputDirectory)
 $status=switch([IO.Path]::GetFileName($PSCommandPath)){'00-Preview-Reset.ps1'{'PREVIEW_READY_FOR_REVIEW'}'Preview-Images.ps1'{'IMAGE_PREVIEW_READY_FOR_REVIEW'}default{'OFFLINE_FAKE_ONLY'}}
 [pscustomobject]@{OfflineFixture=$true;Status=$status;BackupFile='C:\Backup With Spaces\fixture.bak';Arguments=$PSBoundParameters}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $OutputDirectory 'manifest.json') -Encoding UTF8
}
Write-Output 'OFFLINE_FAKE_ENDPOINT_NO_SQL'
'@
foreach($relative in @('Invoke-DatabaseOperation.ps1','scripts/migration/initial-import/00-Preview-Reset.ps1','scripts/migration/initial-import/00-DryRun-Reset-LowLog.ps1','scripts/migration/initial-import/00-Commit-Reset-LowLog.ps1','scripts/migration/initial-import/Invoke-Migration.ps1','scripts/migration/initial-import/03-return-archive/Apply-Schema.ps1','scripts/migration/invoice-input-stock/Invoke-Migration.ps1','scripts/migration/invoices/Invoke-Migration.ps1','scripts/migration/product-images/Preview-Images.ps1','scripts/migration/product-images/Invoke-Images.ps1')){
 $p=Join-Path $fake $relative;[void][IO.Directory]::CreateDirectory((Split-Path -Parent $p));$stub|Set-Content -LiteralPath $p -Encoding UTF8
}
$entries=@(Get-ChildItem -LiteralPath $fake -File -Recurse|Where-Object Name -ne 'config.json'|ForEach-Object{[pscustomobject]@{Path=$_.FullName.Substring($fake.Length+1);Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
@{Files=$entries}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $fake 'checksums.json') -Encoding UTF8
function Fake-Step([string]$step,[string]$mode='PREVIEW',[switch]$Commit){
 $argList=@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $fake 'Run-Step.ps1'),'-Step',$step,'-Mode',$mode)
 if($Commit){$argList+='-AllowCommit'}
 & powershell.exe @argList|Out-Null
 Check ($LASTEXITCODE -eq 0) ('Wrapper fake '+$step+'/'+$mode)
}
Fake-Step Inspect
Fake-Step BackupSource
Fake-Step ResetPreview
Fake-Step BackupTarget
Fake-Step ArchiveSchema DRYRUN
Fake-Step ArchiveSchema COMMIT -Commit
Fake-Step ResetDryRun
Fake-Step ResetCommit COMMIT -Commit
$state=Get-Content -LiteralPath (Join-Path $fake 'runs/real-01/state.json') -Raw|ConvertFrom-Json
$call=Get-Content -LiteralPath (Join-Path $state.ResetDryRunDirectory 'manifest.json') -Raw|ConvertFrom-Json
Check ($call.Arguments.BackupFile -eq 'C:\Backup With Spaces\fixture.bak') 'Backup path with spaces survives nested PS5 invocation'
Check ($call.Arguments.ExpectedServer -eq 'WIN-HU6RO2EMIJF\SQLEXPRESS') 'Expected instance passed to reset'
foreach($pkg in @('01-products','02-customers','03-sales','03-return-archive','04-inventory','05-invoice-stock','06-invoices')){
 Fake-Step $pkg DRYRUN
 Fake-Step $pkg COMMIT -Commit
 Fake-Step $pkg VERIFY
}
Fake-Step ImagePreview
Fake-Step Images DRYRUN
Fake-Step Images COMMIT -Commit
Fake-Step Images VERIFY
Fake-Step VerifyAll
$blocked=$false
try{& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fake 'Run-Step.ps1') -Step ResetCommit -AllowCommit 2>&1|Out-Null;$blocked=($LASTEXITCODE -ne 0)}catch{$blocked=$_.Exception.Message -like '*Reset already committed*'}
Check $blocked 'Wrapper refuses a repeated reset'
Write-Output "RELEASE_OFFLINE_PASS: $checks checks; no SQL connection; fixture=$fixture"
