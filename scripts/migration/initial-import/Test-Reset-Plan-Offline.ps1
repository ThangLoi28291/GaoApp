[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PreviewDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Reset-Plan.ps1')
. (Join-Path $PSScriptRoot 'Reset-Hash.ps1')
Initialize-GaoResetHasher
# Exercise the real compiled fingerprint code with in-memory rows, not SQL.
$hashRows=[System.Data.DataTable]::new()
[void]$hashRows.Columns.Add('Hash',[byte[]])
$emptyReader=$hashRows.CreateDataReader()
try {
    $emptyHash=[GaoResetHasher]::HashReader($emptyReader)
    if($emptyHash -cne '0:E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855') { throw 'Empty table fingerprint failed.' }
} finally { $emptyReader.Dispose() }
[byte[]]$sample=0..31
foreach($iteration in 1..2) { $row=$hashRows.NewRow(); $row['Hash']=$sample; $hashRows.Rows.Add($row) }
$reader=$hashRows.CreateDataReader()
$sha=[System.Security.Cryptography.SHA256]::Create()
try {
    $expected='2:'+([BitConverter]::ToString($sha.ComputeHash([byte[]]($sample+$sample))).Replace('-',''))
    if([GaoResetHasher]::HashReader($reader) -cne $expected) { throw 'Duplicate row fingerprint failed.' }
} finally { $reader.Dispose(); $sha.Dispose(); $hashRows.Dispose() }
Write-Output 'PASS: compiled C# helper; empty and duplicate rows hashed in memory'
[object[]]$tables=Get-Content -LiteralPath (Join-Path $PreviewDirectory 'tables.json') -Raw | ConvertFrom-Json
[object[]]$fks=Get-Content -LiteralPath (Join-Path $PreviewDirectory 'foreign-keys.json') -Raw | ConvertFrom-Json
$result=Get-ResetDeletePlan -Tables $tables -ForeignKeys $fks
$clear=@($tables | Where-Object Action -eq 'CLEAR')
if ($result.DeleteOrder.Count -ne $clear.Count) { throw 'Deletion plan does not cover all CLEAR tables.' }
$position=@{}
for($i=0;$i -lt $result.DeleteOrder.Count;$i++) {
    $name=$result.DeleteOrder[$i]
    if($position.ContainsKey($name)){throw 'Duplicate deletion.'}
    if(@($clear | Where-Object Table -eq $name).Count -ne 1){throw 'Plan would delete a retained table.'}
    $position[$name]=$i
}
foreach($fk in $fks) {
    if(-not $position.ContainsKey($fk.ChildTable) -or -not $position.ContainsKey($fk.ParentTable) -or $fk.ChildTable -eq $fk.ParentTable){continue}
    $broken=@($result.Breaks | Where-Object { $_.Table -eq $fk.ChildTable -and $_.Column -eq $fk.ChildColumn -and $_.Parent -eq $fk.ParentTable }).Count
    if(-not $broken -and $position[$fk.ChildTable] -ge $position[$fk.ParentTable]){throw "Invalid dependency order: $($fk.ConstraintName)"}
}
function Expect-Blocked([scriptblock]$action,[string]$name) {
    $blocked=$false
    try { & $action | Out-Null } catch { $blocked=$true }
    if(-not $blocked){throw "Unsafe scenario was not blocked: $name"}
    Write-Output "PASS: $name"
}
Expect-Blocked {
    $missing=@($fks | Where-Object { -not ($_.ChildTable -eq 'POSShifts' -and $_.ChildColumn -eq 'CurrentOrderId') })
    Get-ResetDeletePlan -Tables $tables -ForeignKeys $missing
} 'Missing reviewed cycle link'
Expect-Blocked {
    [object[]]$changed=$tables | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    ($changed | Where-Object Table -eq 'POSShifts').Action='KEEP'
    Get-ResetDeletePlan -Tables $changed -ForeignKeys $fks
} 'Cycle break would alter a KEEP table'
Expect-Blocked {
    [object[]]$changed=$fks | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    ($changed | Where-Object { $_.ChildTable -eq 'POSShifts' -and $_.ChildColumn -eq 'CurrentOrderId' }).ChildNullable=$false
    Get-ResetDeletePlan -Tables $tables -ForeignKeys $changed
} 'Nonnullable cycle link'
Expect-Blocked {
    $extra=[pscustomobject]@{ConstraintName='Unreviewed';ChildSchema='dbo';ParentSchema='dbo';ChildTable='Customers';ChildColumn='NewOrderId';ParentTable='Orders';ParentColumn='Id';ChildNullable=$true;ColumnOrdinal=1;IsDisabled=$false;IsNotTrusted=$false;DeleteAction='NO_ACTION'}
    Get-ResetDeletePlan -Tables $tables -ForeignKeys ($fks+@($extra))
} 'New dependency cycle'
Write-Output "PASS: every CLEAR table exactly once; child-before-parent dependency order; $($result.Breaks.Count) reviewed cycle breaks."
Write-Output 'OFFLINE_TEST_PASS - no SQL connection or database execution.'
