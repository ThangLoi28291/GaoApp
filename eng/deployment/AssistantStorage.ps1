#requires -Version 5.1
# Private CSKH/Assistant storage preflight and offline maintenance. No SQL Server or message dispatch.
function Invoke-GaoAssistantStorage([string]$WebPath, [hashtable]$Request) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = 'dotnet'
    $start.WorkingDirectory = (Resolve-Path -LiteralPath $WebPath).Path
    $start.Arguments = 'GaoApp.Web.dll --assistant-storage'
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.EnvironmentVariables.Clear()
    foreach ($key in @('PATH','SystemRoot','TEMP','TMP','USERPROFILE','LOCALAPPDATA','APPDATA','ProgramFiles','ProgramFiles(x86)','DOTNET_ROOT','DOTNET_ROOT_X64')) {
        $value = [Environment]::GetEnvironmentVariable($key, 'Process')
        if ($value) { $start.EnvironmentVariables[$key] = $value }
    }
    $process = New-Object Diagnostics.Process; $process.StartInfo = $start
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.WriteLine(($Request | ConvertTo-Json -Compress -Depth 8)); $process.StandardInput.Close()
        if (!$process.WaitForExit(300000)) { $process.Kill(); throw 'Assistant storage maintenance timed out.' }
        $output = $stdout.GetAwaiter().GetResult(); [void]$stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw 'Assistant storage maintenance failed. No automatic overwrite or restore is permitted.' }
        try { $result = $output | ConvertFrom-Json } catch { throw 'Invalid assistant storage verification response.' }
        $expected = @{check='storage_verified';snapshot='snapshot_verified';verify='continuity_verified';initialize='storage_prepared';migrate='storage_prepared';'migrate-settings'='storage_prepared'}
        if (!$expected.ContainsKey($Request.operation) -or $result.status -cne $expected[$Request.operation]) { throw 'Missing assistant storage verification marker.' }
        return $result
    }
    finally { $process.Dispose(); $start.EnvironmentVariables.Clear() }
}

function Get-GaoAssistantStorage([hashtable]$Environment, [string[]]$PublicRoots, [string]$UploadRoot, [string]$KeysPath, [string]$CertificatePath) {
    foreach ($key in @('Assistant__StoreSettingsDirectory','CustomerService__DatabasePath','Assistant__StorageId')) {
        if ([string]::IsNullOrWhiteSpace($Environment[$key])) { throw "Missing persistent storage setting: $key. Run explicit storage migration/initialization first." }
    }
    $id = [Guid]::Empty
    if (![Guid]::TryParseExact($Environment['Assistant__StorageId'], 'D', [ref]$id) -or $id -eq [Guid]::Empty) { throw 'Assistant__StorageId must identify prepared storage.' }
    $directory = $Environment['Assistant__StoreSettingsDirectory']; $database = $Environment['CustomerService__DatabasePath']
    foreach ($path in @($directory, $database)) {
        if ($path -notmatch '^[a-zA-Z]:[\\/]') { throw 'Persistent assistant storage paths must be absolute.' }
        if (![IO.Path]::GetFullPath($path).StartsWith('C:\GaoAppData\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Assistant storage must remain under C:\GaoAppData, outside releases.' }
        foreach ($root in @($PublicRoots) + @($UploadRoot,$KeysPath,$CertificatePath)) {
            if ($root -and (Test-GaoOverlap $path $root)) { throw 'Assistant storage must be separate from release, public/upload storage, backup and keys.' }
        }
        Assert-GaoNoLinks $path
    }
    if (!(Test-Path -LiteralPath $directory -PathType Container) -or !(Test-Path -LiteralPath $database -PathType Leaf)) { throw 'Prepared assistant directory/database missing; do not create an empty replacement.' }
    return [pscustomobject]@{SettingsDirectory=[IO.Path]::GetFullPath($directory);DatabasePath=[IO.Path]::GetFullPath($database);StorageId=$id.ToString('D');HoldOwner='';HoldFiles=@();SnapshotDirectory=''}
}

function New-GaoAssistantRequest($Storage, [string]$Operation) {
    return @{operation=$Operation;databasePath=$Storage.DatabasePath;settingsDirectory=$Storage.SettingsDirectory;
        storageId=$Storage.StorageId;snapshotDirectory=$Storage.SnapshotDirectory;writersQuiesced=$true}
}

function Enter-GaoAssistantHold($Storage) {
    $Storage.HoldOwner = [Guid]::NewGuid().ToString('D')
    $Storage.HoldFiles = @()
    foreach ($directory in @($Storage.SettingsDirectory, (Split-Path -Parent $Storage.DatabasePath)) | Select-Object -Unique) {
        $file = Join-Path $directory '.gao-assistant-storage-hold'
        Assert-GaoNoLinks $directory
        $stream = [IO.File]::Open($file, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $bytes = [Text.Encoding]::UTF8.GetBytes($Storage.HoldOwner); $stream.Write($bytes,0,$bytes.Length) }
        finally { $stream.Dispose() }
        $Storage.HoldFiles += $file
    }
}
function Exit-GaoAssistantHold($Storage) {
    foreach ($file in $Storage.HoldFiles) {
        Assert-GaoNoLinks $file
        if ([IO.File]::ReadAllText($file) -cne $Storage.HoldOwner) { throw 'Assistant hold ownership changed; manual investigation required.' }
        Remove-Item -LiteralPath $file -Force
    }
    $Storage.HoldFiles = @()
}
function Assert-GaoAssistantContinuity($Context) {
    [void](Invoke-GaoAssistantStorage $Context.NewWeb (New-GaoAssistantRequest $Context.AssistantStorage 'check'))
    [void](Invoke-GaoAssistantStorage $Context.NewWeb (New-GaoAssistantRequest $Context.AssistantStorage 'verify'))
}
function Complete-GaoAssistantRecovery([string]$WebPath, [string]$HoldEvidence, [hashtable]$Environment, [bool]$WritersQuiesced) {
    if (!$WritersQuiesced) { throw 'All writers must be quiesced before continuity recovery.' }
    Assert-GaoNoLinks $HoldEvidence
    $storage = Get-Content -LiteralPath $HoldEvidence -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($storage.SnapshotDirectory) -or !(Test-Path -LiteralPath (Join-Path $storage.SnapshotDirectory 'evidence.json') -PathType Leaf)) { throw 'No verified snapshot exists. Keep writers stopped and verify the source manually before removing any hold.' }
    if ($storage.DatabasePath -cne $Environment['CustomerService__DatabasePath'] -or $storage.SettingsDirectory -cne $Environment['Assistant__StoreSettingsDirectory'] -or $storage.StorageId -cne $Environment['Assistant__StorageId']) { throw 'Recovery evidence does not match the pool storage target.' }
    $expectedHolds = @($storage.SettingsDirectory, (Split-Path -Parent $storage.DatabasePath)) | Select-Object -Unique | ForEach-Object { Join-Path $_ '.gao-assistant-storage-hold' }
    if (@(Compare-Object @($storage.HoldFiles) @($expectedHolds)).Count) { throw 'Unexpected hold paths in recovery evidence.' }
    [void](Invoke-GaoAssistantStorage $WebPath (New-GaoAssistantRequest $storage 'check'))
    [void](Invoke-GaoAssistantStorage $WebPath (New-GaoAssistantRequest $storage 'verify'))
    Exit-GaoAssistantHold $storage
}
