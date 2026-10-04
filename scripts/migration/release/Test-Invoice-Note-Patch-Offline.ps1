param([Parameter(Mandatory=$true)][string]$PatchRoot,[Parameter(Mandatory=$true)][string]$InventoryFixture)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$patch=[IO.Path]::GetFullPath($PatchRoot)
$fixture=Join-Path $repo ('.artifacts/invoice-note-test-'+[guid]::NewGuid().ToString('N'))
Copy-Item -LiteralPath $InventoryFixture -Destination $fixture -Recurse
$checks=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw $name};$script:checks++;Write-Output "PASS: $name"}
foreach($f in Get-ChildItem -LiteralPath $patch -Filter '*.ps1'){
 $t=$null;$e=$null;$null=[Management.Automation.Language.Parser]::ParseFile($f.FullName,[ref]$t,[ref]$e)
 Check ($e.Count -eq 0) ('PS5 syntax '+$f.Name)
}
Add-Type -Path 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\Application\Microsoft.SqlServer.TransactSql.ScriptDom.dll'
$parser=[Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser]::new($true)
foreach($name in @('InvoiceMigration.sql','Read-Original-Invoice-Notes.sql')){
 $reader=[IO.StringReader]::new([IO.File]::ReadAllText((Join-Path $patch $name)))
 $errors=$null;$null=$parser.Parse($reader,[ref]$errors);$reader.Dispose()
 if($errors.Count){$errors|Format-Table Line,Column,Message|Out-String|Write-Output}
 Check ($errors.Count -eq 0) ('T-SQL parses without SQL connection: '+$name)
}
$sql=[IO.File]::ReadAllText((Join-Path $patch 'InvoiceMigration.sql')).Replace("`r`n","`n")
$oldSql=[IO.File]::ReadAllText((Join-Path $fixture 'scripts/migration/invoices/InvoiceMigration.sql')).Replace("`r`n","`n")
$stripped=[regex]::Replace($sql,'(?s) -- BEGIN REVIEWED 20260929 LONG NOTES.*? -- END REVIEWED 20260929 LONG NOTES\n','')
$stripped=[regex]::Replace($stripped,'(?s) -- BEGIN REVIEWED 20260929 ORIGINAL NOTE VERIFICATION.*? -- END REVIEWED 20260929 ORIGINAL NOTE VERIFICATION\n','')
$stripped=$stripped.Replace('Note nvarchar(max), IdGop','Note nvarchar(2000), IdGop')
Check ($stripped.TrimEnd([char[]]"`r`n") -ceq $oldSql.TrimEnd([char[]]"`r`n")) 'Existing invoice migration logic remains unchanged outside the two review blocks and full-note staging'
Check ($sql.Contains('INSERT @ReviewedLongNotes VALUES(1828849),(1828927)')) 'Shortening is limited to the two approved source IDs'
Check ($sql.Contains('LEN(h.Note)<>519') -and $sql.Contains('h.OrderCategoryID<>12') -and $sql.Contains("CONVERT(date,h.CreatedAt)<>'20260912'") -and $sql.Contains('h.InvoiceNumber IS NOT NULL OR h.IssuedDate IS NOT NULL')) 'Changed source length, category, day or issuance state is rejected'
Check ($sql.Contains('OR LEN(Note)>500 OR LEN(InvoiceNumber)>35')) 'Unapproved long notes and original number/date validation still block'
Check ($sql.Contains('DATALENGTH(DisplayNote)>1000') -and $sql.Contains('BETWEEN 55296 AND 56319')) 'Display length respects nvarchar storage and split surrogate boundaries'
Check ($sql.Contains('OUTER APPLY OPENJSON(e.LegacySnapshotJson) WITH(Note nvarchar(max)) raw') -and $sql.Contains('DATALENGTH(raw.Note)<>DATALENGTH(r.OriginalNote)')) 'Original full note is checked against the immutable snapshot'
Check ($sql.Contains("N'LONG_NOTE_ADJUSTMENTS' Report,LegacySourceId,LEN(OriginalNote)") -and $sql.Contains('OriginalNotePreserved,OriginalNote,DisplayNote')) 'Evidence exports both full original text and shortened display text'
Check ($sql -notmatch '(?i)UPDATE\s+#SourceHead\b' -and $sql.Contains('"') -eq $false) 'Raw source staging is not updated'

$runs=Join-Path $fixture 'runs/real-01';$statePath=Join-Path $runs 'state.json';$manifestPath=Join-Path $fixture 'checksums.json'
$before=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
foreach($package in @('01-products','02-customers','03-sales','03-return-archive','04-inventory')){
 $entry=@($before.Files|Where-Object {$_.Path -eq "scripts/migration/initial-import/$package/Migration.sql"})[0]
 foreach($mode in @('COMMIT','VERIFY')){
  $folder=Join-Path $runs ($package+'-'+$mode+'-OFFLINE-TEST');[void][IO.Directory]::CreateDirectory($folder)
  @{Status=$mode+'_PASS';Mode=$mode;Package=$package;SqlSha256=$entry.Sha256;Server=$state.Server;
    SourceDatabase=$state.SourceDatabase;TargetDatabase=$state.TargetDatabase}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $folder 'manifest.json') -Encoding UTF8
  $state|Add-Member NoteProperty ($package+'-'+$mode) $folder -Force
 }
}
$stockSql=@($before.Files|Where-Object {$_.Path -eq 'scripts/migration/invoice-input-stock/InvoiceInputStockMigration.sql'})[0]
foreach($mode in @('COMMIT','VERIFY')){
 $folder=Join-Path $runs ('05-invoice-stock-'+$mode+'-OFFLINE-TEST');[void][IO.Directory]::CreateDirectory($folder)
 $csv='11-'+$mode+'_PASS.csv'
 [pscustomobject]@{Report=$mode+'_PASS'}|Export-Csv -LiteralPath (Join-Path $folder $csv) -NoTypeInformation -Encoding UTF8
 @{Mode=$mode;SqlSha256=$stockSql.Sha256;Server=$state.Server;SourceDatabase=$state.SourceDatabase;TargetDatabase=$state.TargetDatabase;
 StoreId=1;WarehouseId=1;Reports=@(@{Report=$mode+'_PASS';Rows=1;File=$csv})}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $folder 'manifest.json') -Encoding UTF8
 $state|Add-Member NoteProperty ('05-invoice-stock-'+$mode) $folder -Force
}
$state|Add-Member NoteProperty '06-invoices-PREVIEW' 'STALE_PREVIEW' -Force
$state|Add-Member NoteProperty '06-invoices-DRYRUN' 'STALE_DRYRUN' -Force
$state|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $statePath -Encoding UTF8
$baselineState=[IO.File]::ReadAllBytes($statePath);$beforeHash=(Get-FileHash -LiteralPath $manifestPath).Hash
function Invoke-Installer {
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $patch 'Install-Invoice-Note-Patch.ps1') -PackageRoot $fixture
 return $LASTEXITCODE
}
function Expect-Blocked([string]$label){
 $blocked=$false;try{$r=@(Invoke-Installer 2>&1);$blocked=$r[-1] -ne 0}catch{$blocked=$true}
 Check $blocked $label
 Check ((Get-FileHash -LiteralPath $manifestPath).Hash -ceq $beforeHash) 'Rejected installation leaves package unchanged'
}
$state|Add-Member NoteProperty '06-invoices-COMMIT' 'ALREADY_COMMITTED_OFFLINE' -Force
$state|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $statePath -Encoding UTF8
Expect-Blocked 'Refuse patch after invoice COMMIT'
[IO.File]::WriteAllBytes($statePath,$baselineState)
$verifyCsv=Join-Path $state.'05-invoice-stock-VERIFY' '11-VERIFY_PASS.csv'
[pscustomobject]@{Report='NOT_VERIFIED'}|Export-Csv -LiteralPath $verifyCsv -NoTypeInformation -Encoding UTF8
Expect-Blocked 'Refuse a stock receipt whose actual CSV did not PASS'
[pscustomobject]@{Report='VERIFY_PASS'}|Export-Csv -LiteralPath $verifyCsv -NoTypeInformation -Encoding UTF8
$output=@(Invoke-Installer);$output|Write-Output
Check ($output[-1] -eq 0) 'Install on isolated inventory-patched package (no SQL)'
$after=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
$changed=@(foreach($f in $before.Files){$new=@($after.Files|Where-Object Path -eq $f.Path);if($new.Count -ne 1 -or $f.Sha256 -cne $new[0].Sha256){$f.Path}})
$wanted=@('scripts/migration/invoices/InvoiceMigration.sql','scripts/migration/invoices/Build-Sql.py')
Check ($changed.Count -eq 2 -and @($changed|Where-Object {$_ -notin $wanted}).Count -eq 0) 'Only invoice SQL and its generator change among the 373 payload files'
$result=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json
foreach($key in @('01-products-COMMIT','01-products-VERIFY','02-customers-COMMIT','02-customers-VERIFY','03-sales-COMMIT','03-sales-VERIFY','03-return-archive-COMMIT','03-return-archive-VERIFY','04-inventory-COMMIT','04-inventory-VERIFY','05-invoice-stock-COMMIT','05-invoice-stock-VERIFY','VerifiedSourceBackup','SourceBackupEvidence','TargetBackupWaivedByUser','ResetCommitted')){
 Check ($result.$key -ceq $state.$key) ('Preserved evidence: '+$key)
}
Check ($result.PSObject.Properties.Name -notcontains '06-invoices-PREVIEW' -and $result.PSObject.Properties.Name -notcontains '06-invoices-DRYRUN') 'Stale invoice approvals expire'
Check ($result.ReviewedPackageSha256 -ceq (Get-FileHash -LiteralPath $manifestPath).Hash) 'New package checksum registered for Run-Step'
$receipt=Get-Content -LiteralPath $result.InvoiceNotePatchEvidence -Raw|ConvertFrom-Json
Check ($receipt.Status -ceq 'INVOICE_NOTE_PATCH_PASS' -and $receipt.DatabaseWrites -eq $false -and $receipt.ResetRepeated -eq $false) 'Receipt distinguishes file patch from data import'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'scripts/migration/invoices/Invoke-Migration.ps1') -CheckSetup
Check ($LASTEXITCODE -eq 0) 'Actual invoice runner loads patched SQL in offline CheckSetup'
$sqlPath=Join-Path $fixture 'scripts/migration/invoices/InvoiceMigration.sql'
$generatedHash=(Get-FileHash -LiteralPath $sqlPath).Hash
& python (Join-Path $fixture 'scripts/migration/invoices/Build-Sql.py')
Check ($LASTEXITCODE -eq 0 -and (Get-FileHash -LiteralPath $sqlPath).Hash -ceq $generatedHash) 'Generator reproduces delivered SQL byte-for-byte'
$beforeHash=(Get-FileHash -LiteralPath $manifestPath).Hash
Expect-Blocked 'Refuse repeated patch'
Write-Output "INVOICE_NOTE_PATCH_OFFLINE_PASS: $checks checks; no SQL connections; fixture=$fixture"
