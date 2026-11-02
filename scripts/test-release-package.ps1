[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleasePath,
    [switch]$SkipManifest,
    [switch]$WebOnly
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../eng/deployment/RuntimeSupport.ps1')
$releaseRoot = (Resolve-Path -LiteralPath $ReleasePath).Path
$forbidden = '(?i)(^|[/\\])(ChannelCredentials|App_Data|Logs|uploads|Properties)([/\\]|$)|(^|[/\\])appsettings\.[^/\\]+\.json$|(^|[/\\])\.env[^/\\]*$|(^|[/\\])secrets[^/\\]*\.json$|\.(pfx|p12|pem|key|bak|mdf|ldf|db|sqlite|zip|log|pdb|user)$'
function Assert-BlankLabelClientPackage([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $seen = @{}
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name -match '(^/|:|(^|/)\.\.(/|$))' -or $seen.ContainsKey($name) -or
                $name -match $forbidden -or
                ($name -match '(?i)(^|/)appsettings[^/]*\.json$' -and $name -cne 'appsettings.json')) {
                throw 'Label client package contains an unsafe or duplicate path.'
            }
            $seen[$name] = $true
        }
        foreach ($name in @('GaoApp.LabelPrintClient.exe', 'GaoApp.LabelPrintClient.dll',
                'GaoApp.LabelPrintClient.deps.json', 'GaoApp.LabelPrintClient.runtimeconfig.json')) {
            if (!$seen.ContainsKey($name)) { throw 'Incomplete label client package.' }
        }
        $runtimeEntry = $archive.GetEntry('GaoApp.LabelPrintClient.runtimeconfig.json')
        if ($runtimeEntry.Length -gt 65536) { throw 'Oversized label client runtime configuration.' }
        $reader = [IO.StreamReader]::new($runtimeEntry.Open())
        try { Assert-GaoAppRuntimeConfiguration ($reader.ReadToEnd() | ConvertFrom-Json) }
        finally { $reader.Dispose() }
        $config = $archive.GetEntry('appsettings.json')
        if ($null -eq $config -or $config.Length -gt 65536) { throw 'Missing or oversized label client configuration.' }
        $reader = [IO.StreamReader]::new($config.Open())
        try { $settings = $reader.ReadToEnd() | ConvertFrom-Json }
        finally { $reader.Dispose() }
        if ($null -eq $settings.LabelClient -or $settings.LabelClient.PairingToken -or
            @($settings.LabelClient.AllowedOrigins).Count -ne 0 -or
            @($settings.PSObject.Properties.Name | Where-Object { $_ -notin @('LabelClient', 'Logging', 'AllowedHosts') }).Count -gt 0 -or
            @($settings.LabelClient.PSObject.Properties.Name | Where-Object { $_ -notin @('Port', 'AllowedOrigins', 'PairingToken') }).Count -gt 0) {
            throw 'Label client package must contain only blank pairing configuration.'
        }
    }
    finally { $archive.Dispose() }
}
foreach ($entry in Get-ChildItem -LiteralPath $releaseRoot -Force) {
    if ($entry.Name -notin @('web', 'migrator', 'autoinvoiceworker', 'labelprintserver', 'release-manifest.json') -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Unexpected release-root content or link.'
    }
}
$components = @(if ($WebOnly) { 'web' } else { 'web'; 'migrator' })
foreach ($optional in @('autoinvoiceworker', 'labelprintserver')) {
    if (Test-Path -LiteralPath (Join-Path $releaseRoot $optional)) { $components += $optional }
}
if ($WebOnly -and ((Test-Path -LiteralPath (Join-Path $releaseRoot 'migrator')) -or $components.Count -ne 1)) { throw 'Web-only package must not include other services.' }
foreach ($component in $components) {
    $componentRoot = Join-Path $releaseRoot $component
    if (!(Test-Path -LiteralPath $componentRoot -PathType Container)) { throw "Missing component: $component" }
    foreach ($entry in Get-ChildItem -LiteralPath $componentRoot -Recurse -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Release contains a link/reparse point.' }
        $relative = $entry.FullName.Substring($componentRoot.Length + 1)
        if ($component -eq 'web' -and !$entry.PSIsContainer -and
            $relative.Replace('\', '/') -ceq 'ClientPackages/GaoApp.LabelPrintClient-win-x64.zip') {
            Assert-BlankLabelClientPackage $entry.FullName
            continue
        }
        if ($relative -match $forbidden) { throw "Forbidden publish path: $component/$relative" }
    }
    $settings = Get-Content -LiteralPath (Join-Path $componentRoot 'appsettings.json') -Raw | ConvertFrom-Json
    if((Get-GaoProperty $settings 'CustomerChannels.CredentialDirectory') -or @(Get-GaoProperty $settings 'CustomerChannels.Connections').Where({$null -ne $_}).Count){throw 'Channel connections/credential paths must be supplied outside the release package.'}
    if ((Get-GaoProperty $settings 'ConnectionStrings.DefaultConnection') -or (Get-GaoProperty $settings 'DataProtection.CertificatePassword') -or
        (Get-GaoProperty $settings 'DataProtection.CertificatePath') -or (Get-GaoProperty $settings 'DataProtection.KeysPath') -or
        ($component -in @('web', 'migrator') -and !(Get-GaoProperty $settings 'DataProtection.RequirePortableKeys')) -or (Get-GaoProperty $settings 'SeedData.EnableDemoSeed') -or
        (Get-GaoProperty $settings 'SeedData.EnableDefaultAdminSeed') -or (Get-GaoProperty $settings 'ProductionBootstrap.Enabled')) {
        throw "Unsafe release defaults in $component. Supply host secrets outside the release."
    }
    if ($component -eq 'labelprintserver' -and (Get-GaoProperty $settings 'LabelPrinting.StoreId') -ne 0) { throw 'Label server package must have a blank store configuration.' }
    $assembly = @{
        web='GaoApp.Web'; migrator='GaoApp.Migrator';
        autoinvoiceworker='GaoApp.AutoInvoiceWorker'; labelprintserver='GaoApp.LabelPrintServer'
    }[$component]
    foreach ($suffix in @('.dll', '.deps.json', '.runtimeconfig.json')) {
        if (!(Test-Path -LiteralPath (Join-Path $componentRoot ($assembly + $suffix)) -PathType Leaf)) {
            throw "Incomplete $component publish output."
        }
    }
    Assert-GaoAppRuntimeConfiguration (Get-Content -LiteralPath (Join-Path $componentRoot "$assembly.runtimeconfig.json") -Raw | ConvertFrom-Json)
    if ($component -in @('autoinvoiceworker', 'labelprintserver') -and !(Test-Path -LiteralPath (Join-Path $componentRoot "$assembly.exe") -PathType Leaf)) { throw 'Windows service package is missing its executable.' }
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
if(!@($webServer.security.requestFiltering.hiddenSegments.add | Where-Object segment -eq 'ChannelCredentials').Count){throw 'IIS must block the reserved ChannelCredentials directory.'}
if (!$WebOnly) {
foreach ($assembly in @('GaoApp.Domain.dll', 'GaoApp.Application.dll', 'GaoApp.Infrastructure.dll')) {
    $webHash = (Get-FileHash -LiteralPath (Join-Path $releaseRoot "web/$assembly") -Algorithm SHA256).Hash
    foreach ($component in $components | Where-Object { $_ -ne 'web' }) {
        $pairedHash = (Get-FileHash -LiteralPath (Join-Path $releaseRoot "$component/$assembly") -Algorithm SHA256).Hash
        if ($webHash -ne $pairedHash) { throw "Web/$component version mismatch: $assembly" }
    }
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
