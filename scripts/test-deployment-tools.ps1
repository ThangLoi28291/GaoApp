#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleasePath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$releaseRoot = (Resolve-Path -LiteralPath $ReleasePath).Path
# Scope all synthetic environment changes to this invocation and restore on exit.
$environmentPattern = '^(DOTNET_ENVIRONMENT|ASPNETCORE_|ConnectionStrings|AppUrl|Tenant|AllowedHosts|Storage|DataProtection|SeedData|ProductionBootstrap|Proxy|Serilog)'
$originalEnvironment = @{}
foreach ($entry in Get-ChildItem Env: | Where-Object Name -match $environmentPattern) { $originalEnvironment[$entry.Name] = $entry.Value }
$workRoot = Join-Path $repoRoot ('Logs/security-phase6-tool-runtime/' + [Guid]::NewGuid().ToString('N'))
$preflight = Join-Path $repoRoot 'scripts/test-host-preflight.ps1'
$checker = Join-Path $repoRoot 'scripts/test-release-package.ps1'
$checks = 0
function Expected-Rejection([string]$name, [scriptblock]$action, [string]$message) {
    try { & $action; throw 'EXPECTED_REJECTION_NOT_RAISED' }
    catch {
        if ($_.Exception.Message -notlike "*$message*") { throw }
        Write-Output "PASS $name"
        $script:checks++
    }
}
try {
    foreach ($dir in @('uploads', 'keys', 'logs')) { New-Item -ItemType Directory -Path (Join-Path $workRoot $dir) -Force | Out-Null }
    foreach ($entry in @(Get-ChildItem Env: | Where-Object Name -match '^(DOTNET_ENVIRONMENT|ASPNETCORE_|ConnectionStrings|AppUrl|Tenant|AllowedHosts|Storage|DataProtection|SeedData|ProductionBootstrap|Proxy|Serilog)')) {
        [Environment]::SetEnvironmentVariable($entry.Name, $null, 'Process')
    }
    $rsa = [Security.Cryptography.RSA]::Create(2048)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Preflight isolated test', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-1), [DateTimeOffset]::UtcNow.AddDays(1))
    $password = [Guid]::NewGuid().ToString('N')
    $pfxPath = Join-Path $workRoot 'test.pfx'
    [IO.File]::WriteAllBytes($pfxPath, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password))
    $certificate.Dispose()
    $rsa.Dispose()
    $environmentValues = @{
        DOTNET_ENVIRONMENT='Production'; ASPNETCORE_ENVIRONMENT='Production';
        ConnectionStrings__DefaultConnection='Server=preflight-sql.example.test;Database=GaoAppStaging;Integrated Security=True;Encrypt=True;TrustServerCertificate=False';
        DataProtection__KeysPath=(Join-Path $workRoot 'keys'); DataProtection__CertificatePath=$pfxPath; DataProtection__CertificatePassword=$password;
        Storage__UploadRoot=(Join-Path $workRoot 'uploads'); Tenant__RootDomain='staging.example.test'; Tenant__AdminSubdomain='admin';
        AppUrl__BaseUrl='https://staging.example.test'; AppUrl__AdminUrl='https://admin.staging.example.test';
        AllowedHosts='staging.example.test;*.staging.example.test'; Proxy__EnableForwardedHeaders='true'; Proxy__KnownProxies__0='127.0.0.1';
        Serilog__WriteTo__1__Name='File'; Serilog__WriteTo__1__Args__path=(Join-Path $workRoot 'logs/web-.log')
    }
    foreach ($entry in $environmentValues.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process') }
    & $preflight -ReleasePath $releaseRoot
    Write-Output 'PASS valid preflight configuration (no SQL/network connection attempted)'
    $checks++
    $env:DOTNET_ENVIRONMENT = 'Development'
    Expected-Rejection 'reject Development' { & $preflight -ReleasePath $releaseRoot } 'must be Production'
    $env:DOTNET_ENVIRONMENT = 'Production'
    $env:ConnectionStrings__DefaultConnection = $environmentValues.ConnectionStrings__DefaultConnection.Replace('Encrypt=True', 'Encrypt=False')
    Expected-Rejection 'reject unencrypted SQL setting' { & $preflight -ReleasePath $releaseRoot } 'Host SQL must'
    $env:ConnectionStrings__DefaultConnection = $environmentValues.ConnectionStrings__DefaultConnection
    $env:AllowedHosts = '*'
    Expected-Rejection 'reject unrestricted host wildcard' { & $preflight -ReleasePath $releaseRoot } 'AllowedHosts must'
    $env:AllowedHosts = $environmentValues.AllowedHosts
    $env:DataProtection__KeysPath = $env:Storage__UploadRoot
    Expected-Rejection 'reject keys in uploads' { & $preflight -ReleasePath $releaseRoot } 'Keys/certificate must'
    $env:DataProtection__KeysPath = $environmentValues.DataProtection__KeysPath
    $env:DataProtection__CertificatePassword = 'incorrect-test-password'
    Expected-Rejection 'reject wrong certificate password' { & $preflight -ReleasePath $releaseRoot } 'certificate could not be validated'
    $env:DataProtection__CertificatePassword = $password
    $env:Serilog__WriteTo__1__Args__path = 'relative/web-.log'
    Expected-Rejection 'reject relative log path' { & $preflight -ReleasePath $releaseRoot } 'must be absolute'
    $env:Serilog__WriteTo__1__Args__path = $environmentValues.Serilog__WriteTo__1__Args__path
    $fakeRoot = Join-Path $workRoot 'synthetic-release'
    foreach ($component in @('Web', 'Migrator')) {
        $dir = Join-Path $fakeRoot $component.ToLowerInvariant()
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot "eng/deployment/$($component.ToLowerInvariant()).appsettings.json") -Destination (Join-Path $dir 'appsettings.json')
        foreach ($name in @("GaoApp.$component.dll", "GaoApp.$component.deps.json", "GaoApp.$component.runtimeconfig.json", 'GaoApp.Domain.dll', 'GaoApp.Application.dll', 'GaoApp.Infrastructure.dll')) {
            [IO.File]::WriteAllText((Join-Path $dir $name), 'synthetic test bytes')
        }
    }
    Copy-Item -LiteralPath (Join-Path $releaseRoot 'web/web.config') -Destination (Join-Path $fakeRoot 'web/web.config')
    $manifest = @{ files = @(Get-ChildItem -LiteralPath $fakeRoot -Recurse -File | ForEach-Object {
        @{ path=$_.FullName.Substring($fakeRoot.Length+1).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }) }
    [IO.File]::WriteAllText((Join-Path $fakeRoot 'release-manifest.json'), ($manifest | ConvertTo-Json -Depth 5))
    & $checker -ReleasePath $fakeRoot
    Write-Output 'PASS synthetic manifest integrity baseline'
    $checks++
    $fakeIisPath = Join-Path $fakeRoot 'web/web.config'
    [xml]$unsafeIis = Get-Content -LiteralPath $fakeIisPath -Raw
    $unsafeIis.SelectSingleNode("//fileExtensions/add[@fileExtension='.pfx']").SetAttribute('allowed', 'true')
    $unsafeIis.Save($fakeIisPath)
    Expected-Rejection 'reject IIS allowing certificate downloads' { & $checker -ReleasePath $fakeRoot } 'IIS must block sensitive extension'
    Copy-Item -LiteralPath (Join-Path $releaseRoot 'web/web.config') -Destination $fakeIisPath -Force
    [IO.File]::AppendAllText((Join-Path $fakeRoot 'web/GaoApp.Web.dll'), 'tamper')
    Expected-Rejection 'reject changed release bytes' { & $checker -ReleasePath $fakeRoot } 'Release integrity failed'
    [IO.File]::WriteAllText((Join-Path $fakeRoot 'web/appsettings.Development.json'), '{}')
    Expected-Rejection 'reject bundled development config' { & $checker -ReleasePath $fakeRoot } 'Forbidden publish path'
    Write-Output "RESULT: $checks/$checks deployment tool checks passed."
}
finally {
    foreach ($entry in @(Get-ChildItem Env: | Where-Object Name -match $environmentPattern)) {
        [Environment]::SetEnvironmentVariable($entry.Name, $null, 'Process')
    }
    foreach ($entry in $originalEnvironment.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process') }
    $resolved = [IO.Path]::GetFullPath($workRoot)
    $expectedParent = Join-Path $repoRoot 'Logs/security-phase6-tool-runtime'
    if (!$resolved.StartsWith($expectedParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^[a-f0-9]{32}$') { throw 'Unsafe cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
