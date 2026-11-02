#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$runtimeRoot = Join-Path $repoRoot ('.artifacts/label-client-smoke/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
. (Join-Path $repoRoot 'eng/deployment/RuntimeSupport.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try {
    $seen = @{}
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.Replace('\','/')
        if ($name -match '(^/|:|(^|/)\.\.(/|$))' -or $seen.ContainsKey($name)) { throw 'Unsafe package entry.' }
        $seen[$name] = $true
    }
} finally { $archive.Dispose() }
Expand-Archive -LiteralPath $PackagePath -DestinationPath $runtimeRoot
$config = Get-Content -LiteralPath (Join-Path $runtimeRoot 'appsettings.json') -Raw | ConvertFrom-Json
if ($config.LabelClient.PairingToken -or @($config.LabelClient.AllowedOrigins).Count -ne 0) { throw 'Client package must have blank pairing settings.' }
$runtime = Get-Content -LiteralPath (Join-Path $runtimeRoot 'GaoApp.LabelPrintClient.runtimeconfig.json') -Raw | ConvertFrom-Json
Assert-GaoAppRuntimeConfiguration $runtime
if (!$runtime.runtimeOptions.includedFrameworks) { throw 'This smoke test requires the self-contained client package.' }
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$token = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$origin = 'https://upgrade.example.test'
$settings = @{
    'LabelClient__Port'=[string]$port; 'LabelClient__PairingToken'=$token;
    'LabelClient__AllowedOrigins__0'=$origin; 'LabelClient__StateDirectory'=(Join-Path $runtimeRoot 'state');
    'DOTNET_ENVIRONMENT'='Production'; 'ASPNETCORE_ENVIRONMENT'='Production'
}
$original = @{}
$process = $null
$client = [Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(3)
function Health([string]$requestOrigin, [bool]$authorized) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, "http://127.0.0.1:$port/health")
    try {
        [void]$request.Headers.TryAddWithoutValidation('Origin', $requestOrigin)
        if ($authorized) { [void]$request.Headers.TryAddWithoutValidation('Authorization', 'Bearer ' + $token) }
        return $client.SendAsync($request).GetAwaiter().GetResult()
    } finally { $request.Dispose() }
}
try {
    foreach ($key in $settings.Keys) {
        $original[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $settings[$key], 'Process')
    }
    $process = Start-Process -FilePath (Join-Path $runtimeRoot 'GaoApp.LabelPrintClient.exe') -WorkingDirectory $runtimeRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtimeRoot 'stdout.log') -RedirectStandardError (Join-Path $runtimeRoot 'stderr.log')
    $healthy = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw 'Self-contained client exited before the health check; inspect its isolated logs.' }
        try {
            $response = Health $origin $true
            try {
                if ($response.StatusCode -eq [Net.HttpStatusCode]::OK) {
                    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
                    if ($body.ok -and $body.protocol -eq 'gao-label-client-v1') { $healthy = $true; break }
                }
            } finally { $response.Dispose() }
        } catch [Net.Http.HttpRequestException] { Start-Sleep -Milliseconds 150 }
    }
    if (!$healthy) { throw 'Self-contained client health/protocol check failed.' }
    $response = Health $origin $false
    try { if ([int]$response.StatusCode -ne 401) { throw 'Client accepted an unauthenticated request.' } } finally { $response.Dispose() }
    $response = Health 'https://untrusted.example.test' $true
    try { if ([int]$response.StatusCode -ne 403) { throw 'Client accepted an untrusted origin.' } } finally { $response.Dispose() }
    Write-Host 'PASS: self-contained .NET 10 client starts, preserves print protocol, requires pairing and rejects an untrusted origin. No print endpoint was called.'
    Write-Host "Evidence: $runtimeRoot"
} finally {
    if ($process) {
        if (!$process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
        $process.Dispose()
    }
    $client.Dispose()
    foreach ($key in $original.Keys) { [Environment]::SetEnvironmentVariable($key, $original[$key], 'Process') }
}
