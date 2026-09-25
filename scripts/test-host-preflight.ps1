#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleasePath,
    [ValidateSet('Web', 'Migrator')][string]$Component = 'Web'
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'test-release-package.ps1') -ReleasePath $ReleasePath
$releaseRoot = (Resolve-Path -LiteralPath $ReleasePath).Path
function Required-Environment([string]$name) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Missing environment setting: $name" }
    return $value
}
function Is-Inside([string]$candidate, [string]$root) {
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $root = $root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    return $candidate.Equals($root, $comparison) -or $candidate.StartsWith($root + [IO.Path]::DirectorySeparatorChar, $comparison)
}
function External-Path([string]$key, [switch]$File, [switch]$ParentDirectory) {
    $value = Required-Environment $key
    if (![IO.Path]::IsPathFullyQualified($value)) { throw "$key must be absolute." }
    $path = [IO.Path]::GetFullPath($value)
    if ($ParentDirectory) { $path = [IO.Path]::GetDirectoryName($path) }
    if (Is-Inside $path $releaseRoot) { throw "$key must be outside the release." }
    $kind = if ($File) { 'Leaf' } else { 'Container' }
    if (!(Test-Path -LiteralPath $path -PathType $kind)) { throw "$key does not exist or is not accessible to this account." }
    $entry = Get-Item -LiteralPath $path -Force
    while ($entry) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "$key contains a link/reparse point; resolve and review the physical path first." }
        $entry = if ($entry -is [IO.FileInfo]) { $entry.Directory } else { $entry.Parent }
    }
    if (!$File) {
        $probe = Join-Path $path ('.gaoapp-preflight-' + [Guid]::NewGuid().ToString('N') + '.tmp')
        try {
            $stream = [IO.File]::Open($probe, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            $stream.Dispose()
        }
        finally { if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe -Force } }
    }
    return $path
}
if ((Required-Environment 'DOTNET_ENVIRONMENT') -ne 'Production') { throw 'DOTNET_ENVIRONMENT must be Production, including staging.' }
if ($Component -eq 'Web' -and (Required-Environment 'ASPNETCORE_ENVIRONMENT') -ne 'Production') { throw 'ASPNETCORE_ENVIRONMENT must be Production.' }
foreach ($key in @('SeedData__EnableDemoSeed', 'SeedData__EnableDefaultAdminSeed')) {
    if ([Environment]::GetEnvironmentVariable($key) -notin @($null, '', 'false')) { throw "$key must stay false." }
}
if ([Environment]::GetEnvironmentVariable('DataProtection__RequirePortableKeys') -notin @($null, '', 'true')) { throw 'Portable key encryption must stay enabled.' }
if ($Component -eq 'Web' -and [Environment]::GetEnvironmentVariable('ProductionBootstrap__Enabled') -notin @($null, '', 'false')) {
    throw 'ProductionBootstrap is a Migrator-only operation; remove it from the Web environment.'
}
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
try { $connection.set_ConnectionString((Required-Environment 'ConnectionStrings__DefaultConnection')) }
catch { throw 'ConnectionStrings__DefaultConnection is missing or malformed. Values are not printed.' }
function Sql-Value([string[]]$keys) {
    foreach ($key in $keys) { if ($connection.ContainsKey($key)) { return [string]$connection[$key] } }
    return ''
}
$server = Sql-Value @('Server', 'Data Source', 'Address', 'Addr', 'Network Address')
$database = Sql-Value @('Database', 'Initial Catalog')
if (!$server -or !$database -or $server -match '(?i)\(localdb\)' -or $database -in @('master', 'tempdb', 'model', 'msdb')) {
    throw 'Configure a dedicated host SQL Server database; LocalDB and system databases are not staging targets.'
}
if ((Sql-Value @('Encrypt')) -notin @('true', 'mandatory', 'strict') -or (Sql-Value @('TrustServerCertificate', 'Trust Server Certificate')) -ne 'false') {
    throw 'Host SQL must explicitly use Encrypt=True (or Strict) and TrustServerCertificate=False with a trusted SQL certificate.'
}
$keys = External-Path 'DataProtection__KeysPath'
$certificatePath = External-Path 'DataProtection__CertificatePath' -File
$password = Required-Environment 'DataProtection__CertificatePassword'
$certificate = $null
try {
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath, $password, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate)
    try {
        if (!$rsa -or $rsa.KeySize -lt 2048 -or $certificate.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow -or $certificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow) {
            throw 'Invalid key certificate.'
        }
    }
    finally { if ($rsa) { $rsa.Dispose() } }
}
catch { throw 'Data Protection certificate could not be validated; check private RSA key, password, validity and read permission.' }
finally { if ($certificate) { $certificate.Dispose() }; $password = $null }
if ($Component -eq 'Web') {
    $uploads = External-Path 'Storage__UploadRoot'
    if ((Is-Inside $keys $uploads) -or (Is-Inside $certificatePath $uploads)) { throw 'Keys/certificate must not be inside upload storage.' }
    $domain = Required-Environment 'Tenant__RootDomain'
    if ($domain -match '(?i)(localhost|configure-host|\.invalid$)' -or [Uri]::CheckHostName($domain) -ne [UriHostNameType]::Dns -or !$domain.Contains('.')) { throw 'Configure the actual staging root domain.' }
    $admin = Required-Environment 'Tenant__AdminSubdomain'
    if ($admin -notmatch '^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$') { throw 'Tenant__AdminSubdomain must be one DNS label.' }
    foreach ($pair in @(@('AppUrl__BaseUrl', $domain), @('AppUrl__AdminUrl', "$admin.$domain"))) {
        $url = $null
        if (![Uri]::TryCreate((Required-Environment $pair[0]), [UriKind]::Absolute, [ref]$url) -or $url.Scheme -ne 'https' -or $url.Host -ne $pair[1] -or $url.UserInfo -or $url.AbsolutePath -ne '/' -or $url.Query -or $url.Fragment) {
            throw "$($pair[0]) must be the matching HTTPS origin."
        }
    }
    $hosts = (Required-Environment 'AllowedHosts').Split(';', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object Trim
    if ($hosts -contains '*' -or $hosts -notcontains $domain -or $hosts -notcontains "*.$domain") { throw 'AllowedHosts must include the root and its scoped wildcard; a bare * is forbidden.' }
    foreach ($hostName in $hosts) {
        if ($hostName -ne $domain -and $hostName -ne "*.$domain" -and !$hostName.EndsWith(".$domain", [StringComparison]::OrdinalIgnoreCase)) { throw 'AllowedHosts contains a host outside the staging domain.' }
    }
    if ((Required-Environment 'Proxy__EnableForwardedHeaders') -ne 'true') { throw 'This preflight profile requires a trusted HTTPS reverse proxy.' }
    $proxy = $null
    if (![Net.IPAddress]::TryParse((Required-Environment 'Proxy__KnownProxies__0'), [ref]$proxy)) { throw 'Set the actual reverse proxy IP in Proxy__KnownProxies__0.' }
}
foreach ($entry in Get-ChildItem Env: | Where-Object { $_.Name -match '^Serilog__WriteTo__\d+__Name$' -and $_.Value -eq 'File' }) {
    $pathKey = $entry.Name.Substring(0, $entry.Name.Length - 4) + 'Args__path'
    $logDirectory = External-Path $pathKey -ParentDirectory
    if ($Component -eq 'Web' -and (Is-Inside $logDirectory $uploads)) { throw 'Log files must not be inside upload storage.' }
}
$runtimeConfig = Get-Content -LiteralPath (Join-Path $releaseRoot "$($Component.ToLowerInvariant())/GaoApp.$Component.runtimeconfig.json") -Raw | ConvertFrom-Json
$runtimes = & dotnet --list-runtimes
if ($LASTEXITCODE -ne 0) { throw 'dotnet runtime is not installed for this service account.' }
$frameworks = @($runtimeConfig.runtimeOptions.frameworks) + @($runtimeConfig.runtimeOptions.framework)
foreach ($framework in $frameworks | Where-Object { $_ }) {
    $version = [Version]$framework.version
    $pattern = '^' + [Regex]::Escape($framework.name) + ' ' + $version.Major + '\.' + $version.Minor + '\.'
    if (!($runtimes -match $pattern)) { throw "Missing runtime: $($framework.name) $($version.Major).$($version.Minor).x" }
}
Write-Host 'PASS: release, environment, HTTPS origins, storage access, encryption certificate and installed runtimes.'
Write-Host 'This checks the current account. SQL connectivity/permissions, DNS, proxy TLS and WebSockets still require the running staging checks.'
