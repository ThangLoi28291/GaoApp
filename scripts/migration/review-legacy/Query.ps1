param([Parameter(Mandatory=$true)][string]$Sql,[string]$Database='GaoAppDb',[string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$cs=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$cs['Data Source']='.\SQLEXPRESS'; $cs['Initial Catalog']=$Database
$cs['Integrated Security']=$true; $cs['Encrypt']=$true; $cs['TrustServerCertificate']=$true
$c=[System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString)
try { $c.Open(); $cmd=$c.CreateCommand(); $cmd.CommandTimeout=600; $cmd.CommandText=$Sql
 $ds=[System.Data.DataSet]::new(); $a=[System.Data.SqlClient.SqlDataAdapter]::new($cmd); [void]$a.Fill($ds)
 $i=0
 if($OutputDirectory){[void][IO.Directory]::CreateDirectory($OutputDirectory)}
 foreach($t in $ds.Tables){
  $i++;$rows=@($t | Select-Object -Property $t.Columns.ColumnName)
  if($OutputDirectory){
   ConvertTo-Json -InputObject $rows -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory ('report-{0:00}.json' -f $i)) -Encoding utf8
   Write-Output "Report $i : $($rows.Count) rows saved"
  }else{ConvertTo-Json -InputObject $rows -Depth 4}
 }
}finally{$c.Dispose()}
