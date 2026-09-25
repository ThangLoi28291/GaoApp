#requires -Version 5.1
# Shared implementation for the two deployment entry points. No SQL operation here.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-GaoOverlap([string]$First, [string]$Second) {
    $a = [IO.Path]::GetFullPath($First).TrimEnd('\')
    $b = [IO.Path]::GetFullPath($Second).TrimEnd('\')
    return $a.Equals($b, [StringComparison]::OrdinalIgnoreCase) -or
        $a.StartsWith($b + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $b.StartsWith($a + '\', [StringComparison]::OrdinalIgnoreCase)
}
function Assert-GaoNoLinks([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    while ($null -ne $item) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Link/reparse path rejected.' }
        $item = $item.Parent
    }
    foreach ($entry in Get-ChildItem -LiteralPath $Path -Recurse -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Link/reparse content rejected.' }
    }
}
function Get-GaoTree([string]$Path) {
    $root = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\')
    Assert-GaoNoLinks $root
    return @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
        '{0}|{1}|{2}' -f $_.FullName.Substring($root.Length + 1), $_.Length,
            (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
}
function Assert-GaoTreeEqual($Before, $After) {
    if (@(Compare-Object @($Before) @($After)).Count) { throw 'File/byte verification failed.' }
}
function Assert-GaoPackage([string]$ReleasePath, [string]$ExpectedManifestSha256, [bool]$WithSchema) {
    if ($ExpectedManifestSha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'A trusted release manifest SHA-256 is required.' }
    Assert-GaoNoLinks $ReleasePath
    $manifest = Join-Path $ReleasePath 'release-manifest.json'
    if ((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -ne $ExpectedManifestSha256) { throw 'Manifest SHA-256 mismatch.' }
    & (Join-Path $PSScriptRoot '../../scripts/test-release-package.ps1') -ReleasePath $ReleasePath -WebOnly:(!$WithSchema)
}
function Get-GaoMartState {
    $site = Get-Website -Name 'GaoMart'
    if (!$site) { throw 'GaoMart must exist for isolation verification.' }
    $pool = Get-WebAppPoolState -Name $site.applicationPool
    $apps = @(Get-WebApplication -Site 'GaoMart' | Select-Object path, physicalPath, applicationPool)
    return [pscustomobject]@{
        Configuration = (@{ Name=$site.Name; State=[string]$site.State; Pool=$site.applicationPool; PoolState=[string]$pool.Value;
            Path=$site.physicalPath; Bindings=@($site.Bindings.Collection | ForEach-Object { "$($_.protocol)|$($_.bindingInformation)" }); Apps=$apps } | ConvertTo-Json -Depth 8 -Compress)
        Files = @(Get-GaoTree ([Environment]::ExpandEnvironmentVariables($site.physicalPath)))
    }
}
function Assert-GaoMartUnchanged($Before) {
    $after = Get-GaoMartState
    if ($Before.Configuration -cne $after.Configuration) { throw 'GaoMart IIS state changed; stop and investigate.' }
    Assert-GaoTreeEqual $Before.Files $after.Files
}
function Get-GaoDeploymentContext([string]$ReleasePath, [string]$BackupRoot, [uri]$SmokeUrl) {
    Import-Module WebAdministration -ErrorAction Stop
    $site = Get-Website -Name 'GaoApp'
    $mart = Get-Website -Name 'GaoMart'
    if (!$site -or !$mart -or $site.applicationPool -ne 'GaoAppPool') { throw 'Expected GaoApp/GaoAppPool and GaoMart IIS topology.' }
    if ([string]$site.State -ne 'Started' -or (Get-WebAppPoolState -Name 'GaoAppPool').Value -ne 'Started') { throw 'GaoApp must be running before deployment; investigate an existing outage first.' }
    foreach ($other in Get-Website) {
        if ($other.Name -ne 'GaoApp' -and $other.applicationPool -eq 'GaoAppPool') { throw 'GaoAppPool is shared by another site.' }
        foreach ($app in Get-WebApplication -Site $other.Name) {
            if ($app.applicationPool -eq 'GaoAppPool' -or $other.Name -eq 'GaoApp') { throw 'Child application/shared pool requires separate reviewed deployment.' }
        }
    }
    $oldWeb = [Environment]::ExpandEnvironmentVariables($site.physicalPath)
    $newWeb = Join-Path (Resolve-Path -LiteralPath $ReleasePath).Path 'web'
    $martWeb = [Environment]::ExpandEnvironmentVariables($mart.physicalPath)
    if (!(Test-Path -LiteralPath $BackupRoot -PathType Container)) { throw 'Create and secure a separate Web backup directory first.' }
    $backup = (Resolve-Path -LiteralPath $BackupRoot).Path
    $protected = @($oldWeb, $newWeb, $backup, $martWeb, 'C:\GaoAppData')
    for ($i=0; $i -lt $protected.Count; $i++) {
        for ($j=$i+1; $j -lt $protected.Count; $j++) {
            if (Test-GaoOverlap $protected[$i] $protected[$j]) { throw 'Deployment, backup, external data and GaoMart paths must be disjoint.' }
        }
    }
    foreach ($path in @($oldWeb, $newWeb, $backup, $martWeb, 'C:\GaoAppData')) { Assert-GaoNoLinks $path }
    if ($SmokeUrl.Scheme -ne 'https' -or $SmokeUrl.AbsolutePath -ne '/health/ready' -or $SmokeUrl.Query -or $SmokeUrl.UserInfo) { throw 'SmokeUrl must be an HTTPS /health/ready URL without credentials or query.' }
    if (!@($site.Bindings.Collection | Where-Object { $_.protocol -eq 'https' -and ($_.bindingInformation -split ':')[-1] -eq $SmokeUrl.DnsSafeHost }).Count) { throw 'Smoke URL must match an explicit GaoApp HTTPS host binding.' }
    # Host configuration stays in the existing GaoApp pool, outside the versioned package.
    $collection = Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter "system.applicationHost/applicationPools/add[@name='GaoAppPool']/environmentVariables" -Name '.'
    $envMap = @{}
    foreach ($entry in $collection.Collection) { $envMap[$entry.name] = $entry.value }
    foreach ($key in @('DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT')) {
        if ($envMap[$key] -ne 'Production') { throw 'GaoApp pool must explicitly configure Production.' }
    }
    foreach ($key in @('SeedData__EnableDemoSeed', 'SeedData__EnableDefaultAdminSeed', 'ProductionBootstrap__Enabled')) {
        if ($envMap[$key] -ne 'false') { throw 'GaoApp pool must explicitly disable all seed/bootstrap flags.' }
    }
    foreach ($key in @('ConnectionStrings__DefaultConnection', 'Storage__UploadRoot', 'DataProtection__KeysPath', 'DataProtection__CertificatePath')) {
        if ([string]::IsNullOrWhiteSpace($envMap[$key])) { throw "Missing pool configuration key: $key" }
    }
    foreach ($key in @('Storage__UploadRoot', 'DataProtection__KeysPath', 'DataProtection__CertificatePath')) {
        $path = [IO.Path]::GetFullPath($envMap[$key])
        if (!$path.StartsWith('C:\GaoAppData\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Persistent GaoApp storage/keys/certificate must remain under C:\GaoAppData.' }
    }
    return [pscustomobject]@{ OldWeb=$oldWeb; NewWeb=$newWeb; BackupRoot=$backup; SmokeUrl=$SmokeUrl; Mart=(Get-GaoMartState); PoolConnection=$envMap['ConnectionStrings__DefaultConnection']; AfterSchema=$false }
}
function Invoke-GaoSmoke([uri]$Url) {
    # Readiness probes only; never invoke a business endpoint to make smoke pass.
    for ($attempt=0; $attempt -lt 10; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 5
            if ($response.StatusCode -eq 200 -and ($response.Content | ConvertFrom-Json).status -eq 'Healthy') { return }
        }
        catch { }
        Start-Sleep -Seconds 1
    }
    throw 'GaoApp readiness smoke failed.'
}
function Wait-GaoPoolStopped {
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        if ((Get-WebAppPoolState -Name 'GaoAppPool').Value -eq 'Stopped') { return }
        Start-Sleep -Seconds 1
    }
    throw 'GaoAppPool did not stop within 30 seconds.'
}
function Invoke-GaoWebSwitch($Context) {
    $backup = Join-Path $Context.BackupRoot ('GaoApp-Web-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N'))
    $before = @(Get-GaoTree $Context.OldWeb)
    New-Item -ItemType Directory -Path $backup -ErrorAction Stop | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $Context.OldWeb -Force) { Copy-Item -LiteralPath $entry.FullName -Destination $backup -Recurse -ErrorAction Stop }
    Assert-GaoTreeEqual $before @(Get-GaoTree $backup)
    Assert-GaoTreeEqual $before @(Get-GaoTree $Context.OldWeb)
    $newBytes = @(Get-GaoTree $Context.NewWeb)
    try {
        Stop-Website -Name 'GaoApp'
        Stop-WebAppPool -Name 'GaoAppPool'
        Wait-GaoPoolStopped
        Set-ItemProperty -LiteralPath 'IIS:\Sites\GaoApp' -Name physicalPath -Value $Context.NewWeb
        Assert-GaoTreeEqual $newBytes @(Get-GaoTree $Context.NewWeb)
        Start-WebAppPool -Name 'GaoAppPool'
        Start-Website -Name 'GaoApp'
        Invoke-GaoSmoke $Context.SmokeUrl
        Assert-GaoMartUnchanged $Context.Mart
        Write-Output "Web deployed; backup=$backup. Human practical validation is required."
    }
    catch {
        # Web rollback only. Schema rollback/DB restore is a separate human decision.
        Stop-Website -Name 'GaoApp' -ErrorAction SilentlyContinue
        Stop-WebAppPool -Name 'GaoAppPool' -ErrorAction SilentlyContinue
        Wait-GaoPoolStopped
        if ($Context.AfterSchema) {
            throw 'Web activation after schema change failed. GaoApp remains stopped; human must decide DB restore plus previous Web. No automatic Down or old-Web restart.'
        }
        Set-ItemProperty -LiteralPath 'IIS:\Sites\GaoApp' -Name physicalPath -Value $Context.OldWeb
        Start-WebAppPool -Name 'GaoAppPool'
        Start-Website -Name 'GaoApp'
        Assert-GaoMartUnchanged $Context.Mart
        throw
    }
}
function Enter-GaoDeploymentLock {
    $mutex = New-Object Threading.Mutex($false, 'Global\GaoApp.Deployment')
    if (!$mutex.WaitOne(0)) { $mutex.Dispose(); throw 'Another GaoApp deployment is active.' }
    return $mutex
}
