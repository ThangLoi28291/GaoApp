# Synthetic launcher checks: dotnet is mocked; no Web process or database is used.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$launcherPath = Join-Path $PSScriptRoot 'start-local-web.ps1'
$fixtureRoot = Join-Path $repositoryRoot ('.artifacts/local-web-launcher-tests/' + [Guid]::NewGuid().ToString('N'))
$calls = [Collections.Generic.List[object]]::new()
$failedCommand = ''
$checks = 0
$originalLocation = Get-Location
$lastExitVariable = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
$originalLastExitCode = if ($lastExitVariable) { $lastExitVariable.Value } else { $null }
$originalArtifactsPath = $env:ArtifactsPath
$originalUseArtifactsOutput = $env:UseArtifactsOutput

function dotnet {
    $commandArguments = @($args)
    $calls.Add([pscustomobject]@{
        Arguments = $commandArguments
        Location = (Get-Location).Path
        ArtifactsPath = $env:ArtifactsPath
        UseArtifactsOutput = $env:UseArtifactsOutput
    })
    $global:LASTEXITCODE = if ($commandArguments[0] -eq $failedCommand) { 17 } else { 0 }
}

function Assert-Launcher([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
}

function Argument-After($call, [string]$name) {
    $position = [Array]::IndexOf([object[]]$call.Arguments, $name)
    if ($position -lt 0 -or $position + 1 -ge $call.Arguments.Count) { throw "Missing argument: $name" }
    return $call.Arguments[$position + 1]
}

function Expect-LauncherFailure([scriptblock]$action, [string]$message) {
    try {
        & $action
        throw 'EXPECTED_LAUNCHER_FAILURE_NOT_RAISED'
    }
    catch {
        if ($_.Exception.Message -notlike "*$message*") { throw }
        $script:checks++
    }
}

try {
    $sourceRoots = @((Join-Path $fixtureRoot 'source one'), (Join-Path $fixtureRoot 'different folder/source two'))
    foreach ($sourceRoot in $sourceRoots) {
        New-Item -ItemType Directory -Path (Join-Path $sourceRoot 'scripts'), (Join-Path $sourceRoot 'GaoApp.Web') -Force | Out-Null
        Copy-Item -LiteralPath $launcherPath -Destination (Join-Path $sourceRoot 'scripts/start-local-web.ps1')
        [IO.File]::WriteAllText((Join-Path $sourceRoot 'GaoApp.Web/GaoApp.Web.csproj'), '<Project />')
    }
    $outsideDirectory = Join-Path $fixtureRoot 'unrelated working directory'
    New-Item -ItemType Directory -Path $outsideDirectory -Force | Out-Null
    Set-Location -LiteralPath $outsideDirectory
    $env:ArtifactsPath = 'synthetic-parent-artifacts'
    $env:UseArtifactsOutput = 'false'

    $artifactPaths = @()
    foreach ($sourceRoot in $sourceRoots) {
        $calls.Clear()
        & (Join-Path $sourceRoot 'scripts/start-local-web.ps1')
        Assert-Launcher ($calls.Count -eq 3) 'Default launcher must restore, build, then run.'
        Assert-Launcher (($calls | ForEach-Object { $_.Arguments[0] }) -join ',' -eq 'restore,build,run') 'dotnet command order changed.'
        $artifactPath = Argument-After $calls[0] '--artifacts-path'
        $artifactPaths += $artifactPath
        Assert-Launcher ($artifactPath.StartsWith((Join-Path $sourceRoot '.artifacts/local-web'), [StringComparison]::OrdinalIgnoreCase)) 'Artifacts must belong to the copied source root.'
        Assert-Launcher ((Split-Path $artifactPath -Leaf) -match '^[0-9a-f]{16}$') 'Artifacts must include the normalized source path hash.'
        foreach ($call in $calls) {
            Assert-Launcher ($call.Location -eq $sourceRoot) 'Launcher must execute from its own source root.'
            Assert-Launcher ((Argument-After $call '--artifacts-path') -eq $artifactPath) 'Restore, build, and run must share the artifact path.'
            Assert-Launcher ($call.Arguments -contains (Join-Path $sourceRoot 'GaoApp.Web/GaoApp.Web.csproj')) 'Project path must derive from the script root.'
            Assert-Launcher ($call.ArtifactsPath -eq $artifactPath -and $call.UseArtifactsOutput -eq 'true') 'All commands must receive the same artifact environment properties.'
        }
        Assert-Launcher ((Argument-After $calls[1] '--configuration') -eq 'Debug') 'Default build configuration must be Debug.'
        Assert-Launcher ((Argument-After $calls[2] '--launch-profile') -eq 'https') 'Default launch profile must be https.'
        Assert-Launcher ($calls[1].Arguments -contains '--no-restore') 'Build must reuse the matching restore.'
        Assert-Launcher ($calls[2].Arguments -contains '--no-build') 'Run must reuse the matching build.'
        Assert-Launcher ($calls[2].Arguments[-2] -eq '--' -and $calls[2].Arguments[-1] -eq '--Database:ApplyMigrationsOnStartup=false') 'Local startup must disable automatic schema changes.'
        Assert-Launcher ($calls[2].ArtifactsPath -eq $artifactPath -and $calls[2].UseArtifactsOutput -eq 'true') 'SDK 8 run path evaluation must receive matching environment properties.'
        Assert-Launcher ($env:ArtifactsPath -eq 'synthetic-parent-artifacts' -and $env:UseArtifactsOutput -eq 'false') 'Launcher must restore existing artifact environment properties.'
        Assert-Launcher ((Get-Location).Path -eq $outsideDirectory) 'Launcher must restore the caller location after running.'
    }
    Assert-Launcher ((Split-Path $artifactPaths[0] -Leaf) -ne (Split-Path $artifactPaths[1] -Leaf)) 'Moving the source must select a different artifact hash.'

    $copiedLauncher = Join-Path $sourceRoots[1] 'scripts/start-local-web.ps1'
    $calls.Clear()
    & $copiedLauncher -Configuration Release -LaunchProfile http -BuildOnly
    Assert-Launcher ($calls.Count -eq 2 -and $calls[1].Arguments[0] -eq 'build') 'BuildOnly must never start Web.'
    Assert-Launcher ((Argument-After $calls[1] '--configuration') -eq 'Release') 'Release configuration must reach dotnet build.'
    Assert-Launcher ((Argument-After $calls[0] '--artifacts-path') -eq $artifactPaths[1]) 'Repeated launches at the same source path must reuse the same hash.'

    $calls.Clear()
    & $copiedLauncher -Configuration Release -LaunchProfile http
    Assert-Launcher ((Argument-After $calls[2] '--launch-profile') -eq 'http') 'Selected launch profile must reach dotnet run.'
    Assert-Launcher ((Argument-After $calls[2] '--configuration') -eq 'Release') 'Selected configuration must reach dotnet run.'

    foreach ($command in @('restore', 'build', 'run')) {
        $calls.Clear()
        $failedCommand = $command
        Expect-LauncherFailure { & $copiedLauncher } $(if ($command -eq 'run') { 'Local Web exited with code 17.' } else { "Local Web $command failed (exit 17)." })
        $expectedCallCount = [Array]::IndexOf(@('restore', 'build', 'run'), $command) + 1
        Assert-Launcher ($calls.Count -eq $expectedCallCount) 'A failed dotnet command must block subsequent commands.'
        Assert-Launcher ((Get-Location).Path -eq $outsideDirectory) 'Launcher must restore the caller location after failure.'
        Assert-Launcher ($env:ArtifactsPath -eq 'synthetic-parent-artifacts' -and $env:UseArtifactsOutput -eq 'false') 'Launcher must restore artifact environment properties after failure.'
    }
    $failedCommand = ''
    $env:ArtifactsPath = $null
    $env:UseArtifactsOutput = $null
    $calls.Clear()
    & $copiedLauncher
    Assert-Launcher (-not (Test-Path Env:ArtifactsPath) -and -not (Test-Path Env:UseArtifactsOutput)) 'Launcher must remove artifact environment properties when originally absent.'
    Write-Output "PASS $checks local Web launcher checks. Synthetic fixtures: $fixtureRoot"
}
finally {
    Set-Location -LiteralPath $originalLocation.Path
    $env:ArtifactsPath = $originalArtifactsPath
    $env:UseArtifactsOutput = $originalUseArtifactsOutput
    if ($lastExitVariable) {
        $global:LASTEXITCODE = $originalLastExitCode
    }
    else {
        Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
    }
}
