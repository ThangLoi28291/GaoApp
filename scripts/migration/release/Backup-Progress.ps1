# PS 5.1 compatible. SQL messages are queued by C#, never invoke a PowerShell
# scriptblock from a SqlClient worker thread. No SQL connection at module load.
function Initialize-GaoBackupProgress {
 if ('GaoBackupProgressV2' -as [type]) { return }
 $definition = @'
using System;
using System.Collections.Concurrent;
using System.Data.SqlClient;
using System.Threading.Tasks;
public sealed class GaoBackupProgressV2 : IDisposable {
 private readonly SqlCommand command;
 private readonly SqlConnection connection;
 private readonly ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
 private readonly SqlInfoMessageEventHandler handler;
 private Task task;
 public GaoBackupProgressV2(SqlCommand command) {
  this.command=command; this.connection=command.Connection;
  handler=(sender,args)=>{ foreach(SqlError e in args.Errors) messages.Enqueue(e.Message); };
  connection.InfoMessage+=handler;
 }
 public void Start() { task=Task.Run(()=>{command.ExecuteNonQuery();}); }
 public bool IsCompleted { get {return task!=null && task.IsCompleted;} }
 public string[] Drain() {
  var list=new System.Collections.Generic.List<string>(); string value;
  while(messages.TryDequeue(out value)) list.Add(value); return list.ToArray();
 }
 public void Complete() {task.GetAwaiter().GetResult();}
 public void Dispose() {
  if(task!=null && !task.IsCompleted) {try {command.Cancel();} catch {} }
  connection.InfoMessage-=handler;
 }
}
'@
 if ($PSVersionTable.PSEdition -eq 'Desktop') {
  Add-Type -TypeDefinition $definition -ReferencedAssemblies @('System.dll','System.Core.dll','System.Data.dll','System.Xml.dll')
 } else { Add-Type -TypeDefinition $definition }
}
function Invoke-GaoBackupCommand {
 param([System.Data.SqlClient.SqlCommand]$Command,[string]$Stage)
 Initialize-GaoBackupProgress
 $progress=[GaoBackupProgressV2]::new($Command)
 $watch=[Diagnostics.Stopwatch]::StartNew()
 $nextHeartbeat=10
 try {
  Write-Host "$Stage started. SQL session remains active; Ctrl+C requests cancellation."
  $progress.Start()
  while (-not $progress.IsCompleted) {
   foreach($message in $progress.Drain()) { Write-Host "$Stage : $message" }
   if ($watch.Elapsed.TotalSeconds -ge $nextHeartbeat) {
    Write-Host ("{0} still running; elapsed {1:hh\:mm\:ss}. No progress message does not prove a stall." -f $Stage,$watch.Elapsed)
    $nextHeartbeat+=10
   }
   Start-Sleep -Milliseconds 250
  }
  foreach($message in $progress.Drain()) { Write-Host "$Stage : $message" }
  $progress.Complete() # SQL errors remain exceptions; no false PASS on cancellation/errors.
  Write-Host ("{0} finished in {1:N1} seconds." -f $Stage,$watch.Elapsed.TotalSeconds)
 } finally { $progress.Dispose(); $watch.Stop() }
}
