param([Parameter(Mandatory=$true)][string]$PatchRoot,[Parameter(Mandatory=$true)][string]$EmployeeFixture)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$patch=[IO.Path]::GetFullPath($PatchRoot)
$fixture=Join-Path $repo ('.artifacts/inventory-negative-test-'+[guid]::NewGuid().ToString('N'))
Copy-Item -LiteralPath $EmployeeFixture -Destination $fixture -Recurse
$checks=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw $name};$script:checks++;Write-Output "PASS: $name"}
foreach($f in Get-ChildItem -LiteralPath $patch -Filter '*.ps1'){
 $t=$null;$e=$null;$null=[Management.Automation.Language.Parser]::ParseFile($f.FullName,[ref]$t,[ref]$e)
 Check ($e.Count -eq 0) ('PS5 syntax '+$f.Name)
}
Add-Type -Path 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\Application\Microsoft.SqlServer.TransactSql.ScriptDom.dll'
$parser=[Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser]::new($true)
$sql=[IO.File]::ReadAllText((Join-Path $patch 'Migration.sql'))
$reader=[IO.StringReader]::new($sql);$errors=$null;$null=$parser.Parse($reader,[ref]$errors);$reader.Dispose()
if($errors.Count){$errors|Format-Table Line,Column,Message|Out-String|Write-Output}
Check ($errors.Count -eq 0) 'Full inventory SQL parses without a SQL connection'
$oldSql=[IO.File]::ReadAllText((Join-Path $fixture 'scripts/migration/initial-import/04-inventory/Migration.sql')).Replace("`r`n","`n")
$stripped=$sql.Replace("`r`n","`n")
$stripped=[regex]::Replace($stripped,'(?s)\n-- BEGIN REVIEWED 20260929 NEGATIVE HEADERS.*?-- END REVIEWED 20260929 NEGATIVE HEADERS\n','')
$stripped=[regex]::Replace($stripped,'(?s)-- BEGIN REVIEWED 20260929 STOCK VERIFICATION.*?-- END REVIEWED 20260929 STOCK VERIFICATION\n\n','')
$stripped=$stripped.Replace('reviewed negative-header rows are inventory-only exceptions.','13 approved negative-header rows are inventory-only exceptions.')
Check ($stripped.TrimEnd([char[]]"`r`n") -ceq $oldSql.TrimEnd([char[]]"`r`n")) 'Original inventory logic unchanged after removing new blocks (line endings normalized)'
Check ($sql.Contains("VALUES(1825885,'20260901',-3000),(1832994,'20260926',-2000)")) 'Approval is bound to the two reviewed order IDs, dates and negative totals'
$reviewBlock=[regex]::Match($sql,'(?s)INSERT INTO @ReviewedNegativeDetails VALUES(.*?);').Groups[1].Value
$rows=@([regex]::Matches($reviewBlock,"\((\d+),(\d+),1,N'([^']+)',(-?\d+),(-?\d+),(-?\d+)\)"))
Check ($rows.Count -eq 6) 'Exactly six source detail records are pinned'
$totals=@{};$qty=@{};$merchandise=0;$discounts=0
foreach($row in $rows){
 $order=$row.Groups[1].Value;$q=[decimal]$row.Groups[4].Value;$price=[decimal]$row.Groups[5].Value;$total=[decimal]$row.Groups[6].Value
 if(-not $totals.ContainsKey($order)){$totals[$order]=0;$qty[$order]=0}
 $totals[$order]+=$total
 if($price -ge 0 -and $total -ge 0){$qty[$order]+=$q;$merchandise++}else{$discounts++}
}
Check ($totals['1825885'] -eq -3000 -and $totals['1832994'] -eq -2000) 'Pinned detail sums agree with both negative headers'
Check ($merchandise -eq 4 -and $discounts -eq 2 -and $qty['1825885'] -eq 8 -and $qty['1832994'] -eq 3) 'Plan is four merchandise lines, quantities 8 and 3, with two discount lines excluded'
Check ($sql.Contains('REVIEWED_NEGATIVE_DETAILS_CHANGED') -and $sql.Contains('REVIEWED_NEGATIVE_STOCK_MISMATCH')) 'Changed source details and skipped inventory mappings have explicit failure guards'
Check ($sql.IndexOf('REVIEWED_NEGATIVE_DETAILS_CHANGED') -lt $sql.IndexOf('INSERT INTO @ApprovedNegativeHeader9 SELECT')) 'Detailed source approval is checked before adding exceptions'
Check ($sql.IndexOf('REVIEWED_NEGATIVE_STOCK_MISMATCH') -lt $sql.IndexOf('INSERT INTO dbo.StockDocument')) 'All four mapped stock movements are checked before target inserts'

$runs=Join-Path $fixture 'runs/real-01';$statePath=Join-Path $runs 'state.json';$manifestPath=Join-Path $fixture 'checksums.json'
$before=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
foreach($package in @('01-products','02-customers','03-sales','03-return-archive')){
 $entry=@($before.Files|Where-Object {$_.Path -eq "scripts/migration/initial-import/$package/Migration.sql"})[0]
 foreach($mode in @('COMMIT','VERIFY')){
  $folder=Join-Path $runs ($package+'-'+$mode+'-OFFLINE-TEST')
  [void][IO.Directory]::CreateDirectory($folder)
  @{Status=$mode+'_PASS';Mode=$mode;Package=$package;SqlSha256=$entry.Sha256;Server=$state.Server;
    SourceDatabase=$state.SourceDatabase;TargetDatabase=$state.TargetDatabase}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $folder 'manifest.json') -Encoding UTF8
  $state|Add-Member NoteProperty ($package+'-'+$mode) $folder -Force
 }
}
$state|Add-Member NoteProperty '04-inventory-PREVIEW' 'STALE_PREVIEW' -Force
$state|Add-Member NoteProperty '04-inventory-DRYRUN' 'STALE_DRYRUN' -Force
$state|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $statePath -Encoding UTF8
$baselineState=[IO.File]::ReadAllBytes($statePath)
$beforeHash=(Get-FileHash -LiteralPath $manifestPath).Hash
function Invoke-Installer {
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $patch 'Install-Inventory-Negative-Patch.ps1') -PackageRoot $fixture
 return $LASTEXITCODE
}
function Expect-Blocked([string]$label){
 $blocked=$false;try{$result=@(Invoke-Installer 2>&1);$blocked=$result[-1] -ne 0}catch{$blocked=$true}
 Check $blocked $label
 Check ((Get-FileHash -LiteralPath $manifestPath).Hash -ceq $beforeHash) 'Rejected installation leaves package unchanged'
}
$state|Add-Member NoteProperty '04-inventory-COMMIT' 'ALREADY_COMMITTED_OFFLINE' -Force
$state|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $statePath -Encoding UTF8
Expect-Blocked 'Refuse patch after inventory COMMIT'
[IO.File]::WriteAllBytes($statePath,$baselineState)
$bad=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json
$bad.PSObject.Properties.Remove('03-return-archive-VERIFY')
$bad|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $statePath -Encoding UTF8
Expect-Blocked 'Refuse patch without archive VERIFY'
[IO.File]::WriteAllBytes($statePath,$baselineState)
$output=@(Invoke-Installer);$output|Write-Output
Check ($output[-1] -eq 0) 'Install on isolated employee-patched package (no SQL)'
$after=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
$changed=@(foreach($f in $before.Files){$new=@($after.Files|Where-Object Path -eq $f.Path);if($new.Count -ne 1 -or $f.Sha256 -cne $new[0].Sha256){$f.Path}})
Check ($changed.Count -eq 1 -and $changed[0] -ceq 'scripts/migration/initial-import/04-inventory/Migration.sql') 'Only inventory SQL changes among the 373 payload files'
$result=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json
foreach($key in @('01-products-COMMIT','01-products-VERIFY','02-customers-COMMIT','02-customers-VERIFY','03-sales-COMMIT','03-sales-VERIFY','03-return-archive-COMMIT','03-return-archive-VERIFY','VerifiedSourceBackup','SourceBackupEvidence','TargetBackupWaivedByUser','ResetCommitted')){
 Check ($result.$key -ceq $state.$key) ('Preserved evidence: '+$key)
}
Check ($result.PSObject.Properties.Name -notcontains '04-inventory-PREVIEW' -and $result.PSObject.Properties.Name -notcontains '04-inventory-DRYRUN') 'Stale inventory approvals expire'
Check ($result.ReviewedPackageSha256 -ceq (Get-FileHash -LiteralPath $manifestPath).Hash) 'New checksum registered for Run-Step'
$receipt=Get-Content -LiteralPath $result.InventoryNegativePatchEvidence -Raw|ConvertFrom-Json
Check ($receipt.Status -ceq 'INVENTORY_NEGATIVE_PATCH_PASS' -and $receipt.DatabaseWrites -eq $false -and $receipt.ResetRepeated -eq $false) 'Receipt records file-only patch, not a database import'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'scripts/migration/initial-import/Invoke-Migration.ps1') -Package 04-inventory -SourceDatabase DataGaoStore -TargetDatabase GaoAppDb -CheckSetup
Check ($LASTEXITCODE -eq 0) 'Real inventory runner compiles in offline CheckSetup'
$beforeHash=(Get-FileHash -LiteralPath $manifestPath).Hash
Expect-Blocked 'Refuse repeated patch'
Write-Output "INVENTORY_NEGATIVE_PATCH_OFFLINE_PASS: $checks checks; no SQL connection; fixture=$fixture"
