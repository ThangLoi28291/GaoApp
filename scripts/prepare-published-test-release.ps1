[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$LogPath,
    [Parameter(Mandatory)][string]$ResultPath
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'TestResults/security-phase6')) + [IO.Path]::DirectorySeparatorChar
foreach ($target in @($LogPath, $ResultPath)) {
    if (![IO.Path]::GetFullPath($target).StartsWith($evidenceRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Test publish evidence must stay under TestResults/security-phase6.'
    }
}
try {
    & (Join-Path $PSScriptRoot 'publish-staging-release.ps1') -ArtifactsPath '.artifacts/published-test-release' *> $LogPath
    $releasePath = (Get-Content -LiteralPath $LogPath -Tail 1).Trim()
    $result = @{ success = $true; releasePath = $releasePath; error = $null }
}
catch {
    $result = @{ success = $false; releasePath = $null; error = $_.Exception.Message }
    $_ | Out-String | Add-Content -LiteralPath $LogPath
}
[IO.File]::WriteAllText($ResultPath, ($result | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
if (!$result.success) { exit 1 }
