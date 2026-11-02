[CmdletBinding()]
param(
    [string]$ArtifactsPath = 'Logs/security-phase6-build',
    [switch]$NoRestore,
    [switch]$WebOnly,
    [switch]$AllComponents
)
$ErrorActionPreference = 'Stop'
if ($WebOnly -and $AllComponents) { throw 'WebOnly and AllComponents cannot be combined.' }
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
    if ($AllComponents) {
        & (Join-Path $PSScriptRoot 'printing/Build-LabelClientPackage.ps1')
    }
    if (!$NoRestore) {
        & dotnet restore GaoApp.sln --artifacts-path $artifactRoot
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed; no release manifest was created.' }
    }
    $components = if ($WebOnly) { @('Web') } else { @('Web', 'Migrator') }
    if ($AllComponents) { $components += @('AutoInvoiceWorker', 'LabelPrintServer') }
    foreach ($component in $components) {
        $destination = Join-Path $releaseRoot $component.ToLowerInvariant()
        # Build both components from current source. Never copy the running app's bin folder.
        $appHost = if ($component -in @('AutoInvoiceWorker', 'LabelPrintServer')) { 'true' } else { 'false' }
        & dotnet publish "GaoApp.$component/GaoApp.$component.csproj" --configuration Release --no-restore --artifacts-path $artifactRoot --output $destination "-p:UseAppHost=$appHost" -p:EnvironmentName=Production -p:DebugType=None -p:DebugSymbols=false -p:UseSharedCompilation=false -m:1 -nr:false
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
        targetFramework = 'net10.0'
        minimumRuntime = '10.0.13'
        labelClient = if ($AllComponents) { 'self-contained win-x64; blank pairing; embedded in Web/ClientPackages' } else { 'inspect embedded client package separately' }
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
