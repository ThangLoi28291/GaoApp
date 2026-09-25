[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PreviewDirectory)
$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Reset-Truncate-Plan.ps1')
[object[]]$tables=Get-Content -LiteralPath (Join-Path $PreviewDirectory 'tables.json') -Raw | ConvertFrom-Json
[object[]]$sourceFks=Get-Content -LiteralPath (Join-Path $PreviewDirectory 'foreign-keys.json') -Raw | ConvertFrom-Json
# Old PREVIEW did not capture these properties. Defaults here are TEST FIXTURES
# only: the real runner MUST read their actual values from sys.foreign_keys.
foreach($fk in $sourceFks){
    $fk | Add-Member NoteProperty UpdateAction 'NO_ACTION'
    $fk | Add-Member NoteProperty NotForReplication $false
    $fk | Add-Member NoteProperty IsSystemNamed $false
}
$plan=@(Get-ResetForeignKeyPlan -Tables $tables -ForeignKeys $sourceFks)
$clear=@($tables | Where-Object Action -eq 'CLEAR' | ForEach-Object Table)
$expected=@($sourceFks | Where-Object { $_.ParentTable -in $clear } | Select-Object -ExpandProperty ConstraintName -Unique)
if($plan.Count -ne $expected.Count){throw 'Not every inbound FK is represented.'}
foreach($fk in $plan){
    if($fk.Child -notin $clear -or $fk.Parent -notin $clear){throw 'Would alter a retained table FK.'}
    if($fk.CreateSql -notmatch 'WITH CHECK ADD CONSTRAINT' -or $fk.CreateSql -match 'NOCHECK'){throw 'FK would not be recreated trusted.'}
}
function Expect-Blocked([scriptblock]$action,[string]$scenario){
    $blocked=$false
    try{& $action | Out-Null}catch{$blocked=$true}
    if(-not $blocked){throw "Unsafe scenario passed: $scenario"}
    Write-Output "PASS: $scenario"
}
$fixtureTables=@([pscustomobject]@{Schema='dbo';Table='Child';Action='CLEAR'},[pscustomobject]@{Schema='dbo';Table='Parent';Action='CLEAR'})
$fixture=@(foreach($i in 1..2){
    [pscustomobject]@{ChildSchema='dbo';ParentSchema='dbo';ChildTable='Child';ParentTable='Parent';
        ConstraintName='FK_Composite]';ChildColumn="C$i";ParentColumn="P$i";ColumnOrdinal=$i;
        DeleteAction='SET_NULL';UpdateAction='CASCADE';NotForReplication=$true;IsDisabled=$false;IsNotTrusted=$false;IsSystemNamed=$false}
})
$composite=@(Get-ResetForeignKeyPlan -Tables $fixtureTables -ForeignKeys @($fixture[1],$fixture[0]))
$expectedSql='ALTER TABLE [dbo].[Child] WITH CHECK ADD CONSTRAINT [FK_Composite]]] FOREIGN KEY ([C1],[C2]) REFERENCES [dbo].[Parent] ([P1],[P2]) ON DELETE SET NULL ON UPDATE CASCADE NOT FOR REPLICATION; ALTER TABLE [dbo].[Child] CHECK CONSTRAINT [FK_Composite]]];'
if($composite.Count -ne 1 -or $composite[0].CreateSql -cne $expectedSql){throw 'Composite order/actions/replication/identifier escaping failed.'}
Write-Output 'PASS: composite column order, DELETE/UPDATE actions, replication option, quoted identifiers'
Expect-Blocked {
    Get-ResetForeignKeyPlan -Tables @([pscustomobject]@{Schema='dbo';Table='Child';Action='KEEP'},$fixtureTables[1]) -ForeignKeys $fixture
} 'Inbound FK from a KEEP table'
Expect-Blocked {
    [object[]]$bad=$fixture | ConvertTo-Json | ConvertFrom-Json
    $bad[0].IsNotTrusted=$true
    Get-ResetForeignKeyPlan -Tables $fixtureTables -ForeignKeys $bad
} 'Untrusted FK'
Expect-Blocked {
    [object[]]$bad=$fixture | ConvertTo-Json | ConvertFrom-Json
    $bad[0].IsSystemNamed=$true
    Get-ResetForeignKeyPlan -Tables $fixtureTables -ForeignKeys $bad
} 'System-generated FK naming'
Expect-Blocked { Get-ResetForeignKeyPlan -Tables $fixtureTables -ForeignKeys @($fixture[1]) } 'Incomplete composite metadata'
Expect-Blocked {
    [object[]]$bad=$fixture | ConvertTo-Json | ConvertFrom-Json
    $bad[1].UpdateAction='NO_ACTION'
    Get-ResetForeignKeyPlan -Tables $fixtureTables -ForeignKeys $bad
} 'Inconsistent FK metadata'
Write-Output "PASS: all $($plan.Count) inbound CLEAR-table FK definitions generated; no KEEP-table changes."
Write-Output 'OFFLINE_TEST_PASS - no SQL connection/execution. Runtime metadata must still be checked.'
