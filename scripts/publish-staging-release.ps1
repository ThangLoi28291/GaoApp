[CmdletBinding()]
param(
    [string]$ArtifactsPath = 'Logs/security-phase6-build',
    [switch]$NoRestore,
    [switch]$WebOnly
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $ArtifactsPath))
if (!$artifactRoot.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ArtifactsPath must stay inside this workspace.'
}
$releaseId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$releaseRoot = Join-Path $repoRoot "publish/releases/$releaseId"
New-Item -ItemType Directory -Path $releaseRoot | Out-Null
Push-Location $repoRoot
try {
    if (!$NoRestore) {
        & dotnet restore GaoApp.sln --artifacts-path $artifactRoot
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed; no release manifest was created.' }
    }
    $components = if ($WebOnly) { @('Web') } else { @('Web', 'Migrator') }
    foreach ($component in $components) {
        $destination = Join-Path $releaseRoot $component.ToLowerInvariant()
        # Build both components from current source. Never copy the running app's bin folder.
        & dotnet publish "GaoApp.$component/GaoApp.$component.csproj" --configuration Release --no-restore --artifacts-path $artifactRoot --output $destination -p:UseAppHost=false -p:EnvironmentName=Production
        if ($LASTEXITCODE -ne 0) { throw "Publish $component failed; no release manifest was created." }
    }
    & (Join-Path $PSScriptRoot 'test-release-package.ps1') -ReleasePath $releaseRoot -WebOnly:$WebOnly -SkipManifest
    $gitHead = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read source revision.' }
    $dirty = @(& git status --porcelain).Count -gt 0
    $manifest = [ordered]@{
        components = $components
        releaseId = $releaseId
        createdUtc = [DateTime]::UtcNow.ToString('o')
        sourceCommit = $gitHead
        includesUncommittedChanges = $dirty
        configuration = 'Release'
        hostingEnvironment = 'Production'
        deployment = 'framework-dependent; one Web instance; configure host before starting'
        files = @(Get-ChildItem -LiteralPath $releaseRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
            [ordered]@{ path = $_.FullName.Substring($releaseRoot.Length + 1).Replace('\', '/'); bytes = $_.Length;
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
    }
    [IO.File]::WriteAllText((Join-Path $releaseRoot 'release-manifest.json'), ($manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    & (Join-Path $PSScriptRoot 'test-release-package.ps1') -ReleasePath $releaseRoot -WebOnly:$WebOnly
    Write-Host "Release ready for configuration and smoke test: $releaseRoot"
    Write-Output $releaseRoot
}
finally { Pop-Location }
