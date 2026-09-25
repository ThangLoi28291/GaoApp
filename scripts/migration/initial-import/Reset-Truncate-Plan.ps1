# Pure SQL generation; no connection or database execution.
function Get-ResetForeignKeyPlan {
    param([object[]]$Tables,[object[]]$ForeignKeys)
    $actions=@{}
    foreach($t in $Tables){
        if($t.Schema -ne 'dbo' -or $actions.ContainsKey($t.Table)){throw 'Unsupported or duplicate table in truncate plan.'}
        $actions[$t.Table]=[string]$t.Action
    }
    $actionSql=@{'NO_ACTION'='NO ACTION';'CASCADE'='CASCADE';'SET_NULL'='SET NULL';'SET_DEFAULT'='SET DEFAULT'}
    function Quote-Name([string]$name){return '['+$name.Replace(']',']]')+']'}
    $result=@(foreach($group in ($ForeignKeys | Group-Object ChildSchema,ConstraintName)){
        $columns=@($group.Group | Sort-Object ColumnOrdinal)
        $fk=$columns[0]
        if($actions[$fk.ParentTable] -ne 'CLEAR'){continue}
        if($fk.ChildSchema -ne 'dbo' -or $fk.ParentSchema -ne 'dbo' -or $actions[$fk.ChildTable] -ne 'CLEAR'){
            throw "Cannot remove a foreign key belonging to preserved/unclassified data: $($fk.ConstraintName)"
        }
        if($fk.IsDisabled -or $fk.IsNotTrusted -or $fk.IsSystemNamed){throw 'Only enabled/trusted explicitly named foreign keys are supported.'}
        if(-not $actionSql.ContainsKey($fk.DeleteAction) -or -not $actionSql.ContainsKey($fk.UpdateAction)){throw 'Unknown FK action.'}
        $ordinal=0
        foreach($col in $columns){
            $ordinal++
            if($col.ColumnOrdinal -ne $ordinal -or $col.ChildTable -ne $fk.ChildTable -or $col.ParentTable -ne $fk.ParentTable -or
                $col.UpdateAction -ne $fk.UpdateAction -or $col.DeleteAction -ne $fk.DeleteAction -or
                $col.NotForReplication -ne $fk.NotForReplication){throw 'Inconsistent or incomplete composite FK metadata.'}
        }
        $child='[dbo].'+(Quote-Name $fk.ChildTable)
        $parent='[dbo].'+(Quote-Name $fk.ParentTable)
        $name=Quote-Name $fk.ConstraintName
        $childColumns=($columns | ForEach-Object {Quote-Name $_.ChildColumn}) -join ','
        $parentColumns=($columns | ForEach-Object {Quote-Name $_.ParentColumn}) -join ','
        $replication=if($fk.NotForReplication){' NOT FOR REPLICATION'}else{''}
        [pscustomobject]@{
            Name=$fk.ConstraintName; Child=$fk.ChildTable; Parent=$fk.ParentTable
            DropSql="ALTER TABLE $child DROP CONSTRAINT $name;"
            CreateSql="ALTER TABLE $child WITH CHECK ADD CONSTRAINT $name FOREIGN KEY ($childColumns) REFERENCES $parent ($parentColumns) ON DELETE $($actionSql[$fk.DeleteAction]) ON UPDATE $($actionSql[$fk.UpdateAction])$replication; ALTER TABLE $child CHECK CONSTRAINT $name;"
        }
    })
    return $result
}
