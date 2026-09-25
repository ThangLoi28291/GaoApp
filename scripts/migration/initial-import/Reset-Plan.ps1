# Pure planning functions. No SQL connection or file mutation.
function Get-ResetDeletePlan {
    param([object[]]$Tables, [object[]]$ForeignKeys)
    $remaining = @{}
    foreach ($table in $Tables) {
        if ($table.Action -eq 'CLEAR') { $remaining[[string]$table.Table] = $true }
    }
    # These three nullable links form cycles in the reviewed schema.
    # Never infer arbitrary columns to null, disable constraints, or alter KEEP tables.
    $allowedBreaks = @(
        @{ Table='POSShifts'; Column='CurrentOrderId'; Parent='Orders' },
        @{ Table='InventoryValuationEntries'; Column='InventoryCostLayerId'; Parent='InventoryCostLayers' },
        @{ Table='ProductVariant'; Column='PrimaryProductImageId'; Parent='ProductImages' }
    )
    $breaks = @()
    foreach ($item in $allowedBreaks) {
        $match = @($ForeignKeys | Where-Object {
            $_.ChildSchema -eq 'dbo' -and $_.ParentSchema -eq 'dbo' -and
            $_.ChildTable -eq $item.Table -and $_.ChildColumn -eq $item.Column -and $_.ParentTable -eq $item.Parent
        })
        if ($match.Count -ne 1 -or -not $match[0].ChildNullable) { throw "Expected nullable cycle link changed: $($item.Table).$($item.Column)" }
        if (@($ForeignKeys | Where-Object ConstraintName -eq $match[0].ConstraintName).Count -ne 1) { throw 'Composite cycle link is not supported.' }
        if (-not $remaining.ContainsKey($item.Table) -or -not $remaining.ContainsKey($item.Parent)) { throw 'Cycle break must remain wholly inside CLEAR tables.' }
        $breaks += [pscustomobject]$item
    }
    $edges = @($ForeignKeys | Where-Object {
        $fk = $_
        $isBreak = @($breaks | Where-Object { $_.Table -eq $fk.ChildTable -and $_.Column -eq $fk.ChildColumn -and $_.Parent -eq $fk.ParentTable }).Count -gt 0
        -not $isBreak -and $fk.ChildTable -ne $fk.ParentTable
    })
    $order = [Collections.Generic.List[string]]::new()
    while ($remaining.Count) {
        $ready = @($remaining.Keys | Where-Object {
            $parent = $_
            @($edges | Where-Object { $_.ParentTable -eq $parent -and $remaining.ContainsKey([string]$_.ChildTable) }).Count -eq 0
        } | Sort-Object)
        if (-not $ready.Count) { throw ('Unreviewed FK cycle: ' + (($remaining.Keys | Sort-Object) -join ', ')) }
        foreach ($name in $ready) { $order.Add($name); $remaining.Remove($name) }
    }
    [pscustomobject]@{ Breaks=$breaks; DeleteOrder=$order.ToArray() }
}
