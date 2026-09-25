[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleasePath,
    [switch]$SkipManifest,
    [switch]$WebOnly
)
$ErrorActionPreference = 'Stop'
$releaseRoot = (Resolve-Path -LiteralPath $ReleasePath).Path
$forbidden = '(?i)(^|[/\\])(App_Data|Logs|uploads|Properties)([/\\]|$)|(^|[/\\])appsettings\.[^/\\]+\.json$|(^|[/\\])\.env[^/\\]*$|(^|[/\\])secrets[^/\\]*\.json$|\.(pfx|p12|pem|key|bak|mdf|ldf|db|sqlite|zip|log|pdb|user)$'
foreach ($entry in Get-ChildItem -LiteralPath $releaseRoot -Force) {
    if ($entry.Name -notin @('web', 'migrator', 'release-manifest.json') -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Unexpected release-root content or link.'
    }
}
$components = if ($WebOnly) { @('web') } else { @('web', 'migrator') }
if ($WebOnly -and (Test-Path -LiteralPath (Join-Path $releaseRoot 'migrator'))) { throw 'Web-only package must not include Migrator.' }
foreach ($component in $components) {
    $componentRoot = Join-Path $releaseRoot $component
    if (!(Test-Path -LiteralPath $componentRoot -PathType Container)) { throw "Missing component: $component" }
    foreach ($entry in Get-ChildItem -LiteralPath $componentRoot -Recurse -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Release contains a link/reparse point.' }
        $relative = $entry.FullName.Substring($componentRoot.Length + 1)
        if ($relative -match $forbidden) { throw "Forbidden publish path: $component/$relative" }
    }
    $settings = Get-Content -LiteralPath (Join-Path $componentRoot 'appsettings.json') -Raw | ConvertFrom-Json
    if ($settings.ConnectionStrings.DefaultConnection -or $settings.DataProtection.CertificatePassword -or
        $settings.DataProtection.CertificatePath -or $settings.DataProtection.KeysPath -or
        !$settings.DataProtection.RequirePortableKeys -or $settings.SeedData.EnableDemoSeed -or
        $settings.SeedData.EnableDefaultAdminSeed -or $settings.ProductionBootstrap.Enabled) {
        throw "Unsafe release defaults in $component. Supply host secrets outside the release."
    }
    $assembly = if ($component -eq 'web') { 'GaoApp.Web' } else { 'GaoApp.Migrator' }
    foreach ($suffix in @('.dll', '.deps.json', '.runtimeconfig.json')) {
        if (!(Test-Path -LiteralPath (Join-Path $componentRoot ($assembly + $suffix)) -PathType Leaf)) {
            throw "Incomplete $component publish output."
        }
    }
}
[xml]$iisConfig = Get-Content -LiteralPath (Join-Path $releaseRoot 'web/web.config') -Raw
$webServer = $iisConfig.configuration.location.'system.webServer'
if ($webServer.aspNetCore.processPath -ne 'dotnet' -or $webServer.aspNetCore.arguments -notin @('.\GaoApp.Web.dll', './GaoApp.Web.dll') -or
    $webServer.aspNetCore.hostingModel -ne 'inprocess' -or $webServer.aspNetCore.stdoutLogEnabled -ne 'false' -or
    $webServer.directoryBrowse.enabled -ne 'false' -or
    $webServer.security.requestFiltering.requestLimits.maxAllowedContentLength -ne '67108864') {
    throw 'IIS release is missing bounded requests, disabled directory listing or safe process/log settings.'
}
foreach ($extension in @('.pfx', '.p12', '.pem', '.key', '.bak', '.mdf', '.ldf')) {
    if (!@($webServer.security.requestFiltering.fileExtensions.add | Where-Object {
        $_.fileExtension -eq $extension -and $_.allowed -eq 'false'
    }).Count) { throw "IIS must block sensitive extension: $extension" }
}
if (!$WebOnly) {
foreach ($assembly in @('GaoApp.Domain.dll', 'GaoApp.Application.dll', 'GaoApp.Infrastructure.dll')) {
    $webHash = (Get-FileHash -LiteralPath (Join-Path $releaseRoot "web/$assembly") -Algorithm SHA256).Hash
    $migratorHash = (Get-FileHash -LiteralPath (Join-Path $releaseRoot "migrator/$assembly") -Algorithm SHA256).Hash
    if ($webHash -ne $migratorHash) { throw "Web/Migrator version mismatch: $assembly" }
}
}
if (!$SkipManifest) {
    $manifest = Get-Content -LiteralPath (Join-Path $releaseRoot 'release-manifest.json') -Raw | ConvertFrom-Json
    $actual = @(Get-ChildItem -LiteralPath $releaseRoot -Recurse -File -Force | Where-Object FullName -ne (Join-Path $releaseRoot 'release-manifest.json'))
    if ($actual.Count -ne @($manifest.files).Count) { throw 'Release file count differs from manifest.' }
    $seen = @{}
    foreach ($file in $manifest.files) {
        if ($file.path -match '(^[/\\]|:|(^|[/\\])\.\.([/\\]|$))' -or $seen.ContainsKey($file.path)) { throw 'Invalid manifest path.' }
        $seen[$file.path] = $true
        $target = Join-Path $releaseRoot $file.path
        if (!(Test-Path -LiteralPath $target -PathType Leaf) -or
            (Get-Item -LiteralPath $target).Length -ne $file.bytes -or
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.sha256) {
            throw "Release integrity failed: $($file.path)"
        }
    }
}
Write-Host 'PASS: publish exclusions, safe defaults, paired assemblies and release integrity.'
