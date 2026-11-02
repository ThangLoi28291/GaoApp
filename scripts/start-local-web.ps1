<#
.SYNOPSIS
Build and run the local Web project after moving the source folder.
.DESCRIPTION
Uses a build directory derived from this script's source root. Moving or copying
the source selects a fresh build directory instead of reusing static web assets
and restore metadata containing the previous absolute path. Existing artifacts
are preserved. Local startup does not automatically apply database migrations.
.EXAMPLE
./scripts/start-local-web.ps1
.EXAMPLE
./scripts/start-local-web.ps1 -Configuration Release -LaunchProfile http -BuildOnly
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('http', 'https')]
    [string]$LaunchProfile = 'https',
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'GaoApp.Web/GaoApp.Web.csproj'
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Web project was not found beside the launcher: $projectPath"
}

$normalizedRoot = $repositoryRoot.TrimEnd([char[]]'\/').Replace('\', '/')
if ([IO.Path]::DirectorySeparatorChar -eq '\') {
    $normalizedRoot = $normalizedRoot.ToUpperInvariant()
}
$hasher = [Security.Cryptography.SHA256]::Create()
try {
    $rootHash = [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($normalizedRoot))).Replace('-', '').ToLowerInvariant().Substring(0, 16)
}
finally {
    $hasher.Dispose()
}
$artifactsPath = Join-Path $repositoryRoot ('.artifacts/local-web/' + $rootHash)
$previousArtifactsPath = $env:ArtifactsPath
$previousUseArtifactsOutput = $env:UseArtifactsOutput

Push-Location -LiteralPath $repositoryRoot
try {
    # SDK 8 evaluates the --no-build executable path before applying the
    # --artifacts-path option. Environment properties reach that early
    # evaluation and keep all three commands in the same output layout.
    $env:ArtifactsPath = $artifactsPath
    $env:UseArtifactsOutput = 'true'
    Write-Host "Local Web build output: $artifactsPath"
    & dotnet restore $projectPath --artifacts-path $artifactsPath --disable-parallel -m:1
    if ($LASTEXITCODE -ne 0) { throw "Local Web restore failed (exit $LASTEXITCODE)." }

    & dotnet build $projectPath --configuration $Configuration --artifacts-path $artifactsPath --no-restore -m:1
    if ($LASTEXITCODE -ne 0) { throw "Local Web build failed (exit $LASTEXITCODE)." }

    if (-not $BuildOnly) {
        Write-Host 'Starting local Web with automatic database migrations disabled.'
        & dotnet run --project $projectPath --configuration $Configuration --artifacts-path $artifactsPath --launch-profile $LaunchProfile --no-build '--' '--Database:ApplyMigrationsOnStartup=false'
        if ($LASTEXITCODE -ne 0) { throw "Local Web exited with code $LASTEXITCODE." }
    }
}
finally {
    $env:ArtifactsPath = $previousArtifactsPath
    $env:UseArtifactsOutput = $previousUseArtifactsOutput
    Pop-Location
}
