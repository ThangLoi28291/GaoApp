param([Parameter(Mandatory=$true)][string]$PatchRoot)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$patch=[IO.Path]::GetFullPath($PatchRoot)
$fixture=Join-Path $repo ('.artifacts/employee-patch-test-'+[guid]::NewGuid().ToString('N'))
Copy-Item -LiteralPath (Join-Path $repo '.artifacts/handoff-WIN-20260929-final/MIGRATION') -Destination $fixture -Recurse
$checks=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw $name};$script:checks++;Write-Output "PASS: $name"}
foreach($f in Get-ChildItem -LiteralPath $patch -Filter '*.ps1'){
 $tokens=$null;$errors=$null;$null=[Management.Automation.Language.Parser]::ParseFile($f.FullName,[ref]$tokens,[ref]$errors)
 Check ($errors.Count -eq 0) ('PowerShell 5 syntax: '+$f.Name)
}
$dom='C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\Application\Microsoft.SqlServer.TransactSql.ScriptDom.dll'
Add-Type -Path $dom
$parser=[Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser]::new($true)
$sql=[IO.File]::ReadAllText((Join-Path $patch 'Migration.sql'))
$reader=[IO.StringReader]::new($sql);$errors=$null;$null=$parser.Parse($reader,[ref]$errors);$reader.Dispose()
if($errors.Count){$errors|Format-Table Line,Column,Message|Out-String|Write-Output}
Check ($errors.Count -eq 0) 'Patched sales SQL parses (ScriptDom, no SQL connection)'

# Guard against omissions in this migration's multiple actor columns.
$mapSection=$sql.Substring($sql.IndexOf('-- Remap each source actor once,'))
$mapSection=$mapSection.Substring(0,$mapSection.IndexOf("SELECT N'SALES_PLAN'"))
$expectedColumns=@{
 'POSShifts'=@('OpenedByUserId','ClosedByUserId');'Orders'=@('CreatedBy');'OrderLines'=@('CreatedBy');
 'OrderPayments'=@('CreatedBy');'POSShiftCashTransactions'=@('CreatedByUserId');
 'SalesReturns'=@('CreatedBy','CreatedByUserId','CompletedByUserId');'SalesReturnLines'=@('CreatedBy');'SalesReturnPayments'=@('CreatedBy')
}
$seen=@{}
foreach($m in [regex]::Matches($mapSection,'(?s)UPDATE t SET (.*?)FROM #Stage(\w+) t JOIN #EmployeeMap m ON m\.SourceId=t\.(\w+);')){
 $table=$m.Groups[2].Value
 foreach($column in [regex]::Matches($m.Groups[1].Value,'(\w+)=m\.TargetId')){
  $key=$table+'.'+$column.Groups[1].Value
  if($seen.ContainsKey($key)){throw "Actor is mapped twice: $key"};$seen[$key]=$true
 }
}
foreach($table in $expectedColumns.Keys){foreach($column in $expectedColumns[$table]){
 Check ($seen.ContainsKey($table+'.'+$column)) ('Actor mapped exactly once: '+$table+'.'+$column)
}}
Check ($seen.Count -eq 11) 'Exactly the 11 staged actor columns are remapped'
Check ($sql.IndexOf("IF @Mode='PREVIEW' RETURN;") -lt $sql.IndexOf('INSERT dbo.Users(')) 'PREVIEW exits before historical account insertion'
Check ($sql -notmatch '(?i)(INSERT|UPDATE|DELETE|MERGE)\s+(?:INTO\s+)?\[__SOURCE__\]') 'No direct source database writes'
Check ($sql -notmatch '(?i)(UPDATE|DELETE|MERGE)\s+dbo\.Users\b' -and $sql -notmatch '(?i)(INSERT|UPDATE|DELETE|MERGE)\s+(?:INTO\s+)?dbo\.UserInStores\b') 'No existing-account mutation or store-role assignment'
Check ($sql.Contains('IF EXISTS(SELECT TargetId FROM #EmployeeMap GROUP BY TargetId HAVING COUNT_BIG(*)>1)')) 'Ambiguous many-to-one employee identity is rejected'
Check ($sql.Contains("(662309,N'nguyet',6)") -and $sql.Contains("IsDeleted=0") -and $sql.Contains("=N'NGUYET'")) 'nguyet uses the explicit confirmed ID and login contract'
Check ($sql.Contains('0,0,@Now,NULL,0 FROM #HistoricalUsers') -and $sql.Contains('u.IsActive<>0 OR u.IsHostAdmin<>0') -and $sql.Contains('WHERE x.UserId=h.Id')) 'Historical accounts are inactive, non-admin and have no store access'
Check ($sql.Contains('SELECT Id,RowHash FROM #UsersBefore EXCEPT') -and $sql.Contains('SELECT Id,RowHash FROM #UserInStoresBefore')) 'Existing passwords/rowversions and store mappings are compared in SQL'
$runner=[IO.File]::ReadAllText((Join-Path $patch 'Invoke-Migration.ps1'))
Check (([regex]::Matches($runner,'Hash-Tables \$TargetDatabase \$fingerprintedTargets')).Count -eq 2) 'Commit and later VERIFY both fingerprint retained employees and store access'
Check ($runner.Contains('foreach($name in $config.TargetTables){') -and $runner.Contains('foreach($name in $fingerprintedTargets){Execute')) 'Empty-table gate stays on business tables; retained accounts are locked separately'
$cfg=Get-Content -LiteralPath (Join-Path $patch 'package.json') -Raw|ConvertFrom-Json
Check (($cfg.RetainedTargetTables -join ',') -ceq 'Users,UserInStores') 'Retained account tables are declared only for the sales package'

$runs=Join-Path $fixture 'runs/real-01';[void][IO.Directory]::CreateDirectory($runs)
$statePath=Join-Path $runs 'state.json';$manifestPath=Join-Path $fixture 'checksums.json'
$state=[pscustomobject]@{Server='WIN-HU6RO2EMIJF\SQLEXPRESS';SourceDatabase='DataGaoStore';TargetDatabase='GaoAppDb';ResetCommitted=$true;
 ReviewedPackageSha256=(Get-FileHash -LiteralPath $manifestPath).Hash;VerifiedSourceBackup='C:\Data\DataGaoStore.bak';
 SourceBackupEvidence='OFFLINE_SOURCE';TargetBackupWaivedByUser=$true;ResetPreviewDirectory='OFFLINE_RESET'}
$state|ConvertTo-Json|Set-Content -LiteralPath $statePath -Encoding UTF8
# Reproduce the real prerequisite patch rather than manufacturing its timestamp/hash.
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo '.artifacts/supplier-name-patch-20260929/Install-Supplier-Name-Patch.ps1') -PackageRoot $fixture
Check ($LASTEXITCODE -eq 0) 'Build isolated supplier-patched prerequisite package'
$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
$before=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
foreach($package in @('01-products','02-customers')){
 $entry=@($before.Files|Where-Object {$_.Path -eq "scripts/migration/initial-import/$package/Migration.sql"})[0]
 foreach($mode in @('COMMIT','VERIFY')){
  $folder=Join-Path $runs ($package+'-'+$mode+'-OFFLINE-TEST')
  [void][IO.Directory]::CreateDirectory($folder)
  @{Status=$mode+'_PASS';Mode=$mode;Package=$package;SqlSha256=$entry.Sha256;Server=$state.Server;
   SourceDatabase=$state.SourceDatabase;TargetDatabase=$state.TargetDatabase}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $folder 'manifest.json') -Encoding UTF8
  $state|Add-Member NoteProperty ($package+'-'+$mode) $folder -Force
 }
}
$state|Add-Member NoteProperty '03-sales-PREVIEW' 'OLD_PREVIEW'
$state|Add-Member NoteProperty '03-sales-DRYRUN' 'OLD_DRYRUN'
$state|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $statePath -Encoding UTF8
$baselineState=[IO.File]::ReadAllBytes($statePath)
$baselineManifest=[IO.File]::ReadAllBytes($manifestPath)
$beforeHash=(Get-FileHash -LiteralPath $manifestPath).Hash
function Invoke-Installer {
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $patch 'Install-Employee-Mapping-Patch.ps1') -PackageRoot $fixture
 return $LASTEXITCODE
}
function Expect-Blocked([string]$label){
 $blocked=$false
 try{$result=@(Invoke-Installer 2>&1);$blocked=($result[-1] -ne 0)}catch{$blocked=$true}
 Check $blocked $label
 Check ((Get-FileHash -LiteralPath $manifestPath).Hash -ceq $beforeHash) 'Rejected patch leaves payload manifest untouched'
}
$state|Add-Member NoteProperty '03-sales-COMMIT' 'OFFLINE_ALREADY_COMMITTED'
$state|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $statePath -Encoding UTF8
Expect-Blocked 'Reject patch after sales COMMIT'
[IO.File]::WriteAllBytes($statePath,$baselineState)
$badState=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json
$badState.PSObject.Properties.Remove('02-customers-VERIFY')
$badState|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $statePath -Encoding UTF8
Expect-Blocked 'Reject missing customer VERIFY'
[IO.File]::WriteAllBytes($statePath,$baselineState)
$output=@(Invoke-Installer)
$output|Write-Output
Check ($output[-1] -eq 0) 'Install employee patch on isolated fixture (no SQL)'
$after=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
$changed=@(foreach($f in $before.Files){$new=@($after.Files|Where-Object Path -eq $f.Path);if($new.Count -ne 1 -or $f.Sha256 -cne $new[0].Sha256){$f.Path}})
$wanted=@('scripts/migration/initial-import/03-sales/Migration.sql','scripts/migration/initial-import/03-sales/package.json','scripts/migration/initial-import/Invoke-Migration.ps1')
Check ($changed.Count -eq 3 -and @($changed|Where-Object {$_ -notin $wanted}).Count -eq 0) 'Only 3 approved files changed; product/customer SQL hashes unchanged'
$result=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json
foreach($key in @('01-products-COMMIT','01-products-VERIFY','02-customers-COMMIT','02-customers-VERIFY','VerifiedSourceBackup','SourceBackupEvidence','ResetPreviewDirectory','TargetBackupWaivedByUser','ResetCommitted')){
 Check ($result.$key -ceq $state.$key) ('Preserved successful evidence: '+$key)
}
Check ($result.PSObject.Properties.Name -notcontains '03-sales-PREVIEW' -and $result.PSObject.Properties.Name -notcontains '03-sales-DRYRUN') 'Only stale sales approvals expire'
Check ($result.ReviewedPackageSha256 -ceq (Get-FileHash -LiteralPath $manifestPath).Hash) 'Wrapper accepts the new reviewed package hash'
$receipt=Get-Content -LiteralPath $result.EmployeeMappingPatchEvidence -Raw|ConvertFrom-Json
Check ($receipt.Status -ceq 'EMPLOYEE_MAPPING_PATCH_PASS' -and $receipt.DatabaseWrites -eq $false -and $receipt.ResetRepeated -eq $false) 'File-only patch receipt never claims a database import'
foreach($package in @('01-products','02-customers','03-sales')){
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'scripts/migration/initial-import/Invoke-Migration.ps1') -Package $package -SourceDatabase DataGaoStore -TargetDatabase GaoAppDb -CheckSetup
 Check ($LASTEXITCODE -eq 0) ('Actual runner offline setup: '+$package)
}
$beforeHash=(Get-FileHash -LiteralPath $manifestPath).Hash
Expect-Blocked 'Repeat employee patch is refused'
Write-Output "EMPLOYEE_PATCH_OFFLINE_PASS: $checks checks; no SQL connections; fixture=$fixture"
