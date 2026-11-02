#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleasePath,
    [Parameter(Mandatory)][string]$HostingBundlePath
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $PSScriptRoot 'test-release-package.ps1') -ReleasePath $ReleasePath
$releaseRoot = (Resolve-Path -LiteralPath $ReleasePath).Path
if (!$releaseRoot.StartsWith((Join-Path $repoRoot 'publish/releases') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build the kit from a verified workspace release.' }
$requirements = Get-Content -LiteralPath (Join-Path $repoRoot 'eng/deployment/net10-host-requirements.json') -Raw | ConvertFrom-Json
$installer = (Resolve-Path -LiteralPath $HostingBundlePath).Path
if ((Get-FileHash -LiteralPath $installer -Algorithm SHA512).Hash -ne $requirements.hostingBundle.sha512 -or
    (Get-AuthenticodeSignature -LiteralPath $installer).Status -ne 'Valid') { throw 'Hosting Bundle checksum/signature verification failed.' }
$releaseManifestPath = Join-Path $releaseRoot 'release-manifest.json'
$releaseManifest = Get-Content -LiteralPath $releaseManifestPath -Raw | ConvertFrom-Json
$releaseSha = (Get-FileHash -LiteralPath $releaseManifestPath -Algorithm SHA256).Hash
$suffix = [Guid]::NewGuid().ToString('N').Substring(0,8)
$webOnlyId = $releaseManifest.releaseId + '-web-only-' + $suffix
$webOnlyRoot = Join-Path $repoRoot "publish/releases/$webOnlyId"
$kitRoot = Join-Path $repoRoot "publish/deployment-kits/$($releaseManifest.releaseId)-$suffix"
foreach ($path in @($webOnlyRoot, $kitRoot)) {
    if (Test-Path -LiteralPath $path) { throw 'Kit output must be a new directory.' }
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $releaseRoot 'web') -Destination (Join-Path $webOnlyRoot 'web') -Recurse
$webManifest = [ordered]@{
    components=@('Web'); releaseId=$webOnlyId; createdUtc=[DateTime]::UtcNow.ToString('o');
    sourceCommit=$releaseManifest.sourceCommit; includesUncommittedChanges=$releaseManifest.includesUncommittedChanges;
    configuration='Release'; hostingEnvironment='Production'; targetFramework='net10.0'; minimumRuntime=$requirements.minimumRuntime;
    parentReleaseManifestSha256=$releaseSha;
    files=@(Get-ChildItem -LiteralPath $webOnlyRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{path=$_.FullName.Substring($webOnlyRoot.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
}
$webManifestPath = Join-Path $webOnlyRoot 'release-manifest.json'
[IO.File]::WriteAllText($webManifestPath, ($webManifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'test-release-package.ps1') -ReleasePath $webOnlyRoot -WebOnly
$webSha = (Get-FileHash -LiteralPath $webManifestPath -Algorithm SHA256).Hash
$files = @(
    'eng/deployment/Deployment.Common.ps1', 'eng/deployment/Deploy-WebOnly.ps1',
    'eng/deployment/Deploy-WithSchemaMigration.ps1', 'eng/deployment/SchemaMigration.ps1',
    'eng/deployment/RuntimeSupport.ps1', 'eng/deployment/net10-host-requirements.json',
    'eng/deployment/AssistantStorage.ps1', 'eng/deployment/Prepare-AssistantStorage.ps1',
    'eng/deployment/Resume-AssistantStorage.ps1', 'docs/development/SECURITY-C1-PERSISTENT-STORAGE-20261024.txt',
    'eng/deployment/ChannelCredentials.ps1', 'docs/development/SECURITY-C2-CHANNEL-CREDENTIALS-20261024.txt',
    'docs/development/SECURITY-C3-ZALO-SYNC-20261024.txt',
    'docs/development/SECURITY-C4-STORAGE-BOUNDS-20261024.txt',
    'eng/deployment/Prepare-ChannelCredentials.ps1',
    'eng/deployment/host-environment.example.json',
    'scripts/test-release-package.ps1', 'scripts/test-host-preflight.ps1', 'scripts/test-net10-host.ps1',
    'scripts/printing/Test-LabelClientPackage.ps1',
    'docs/development/SECURITY-ISSUE13-DOTNET10-20261023.md',
    'docs/development/SECURITY-ISSUE13-REGRESSION-FOLLOWUP-20261023.md',
    'docs/development/SECURITY-ISSUE13-AUDIT-79-TO-80.sql'
)
foreach ($file in $files) {
    $destination = Join-Path $kitRoot $file
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $destination
}
New-Item -ItemType Directory -Path (Join-Path $kitRoot 'installers') | Out-Null
Copy-Item -LiteralPath $installer -Destination (Join-Path $kitRoot 'installers/dotnet-hosting-10.0.13-win.exe')
$readme = @"
GAOAPP .NET 10 DEPLOYMENT KIT — PREPARED ONLY, NOT ACTIVATED

Full release folder: $($releaseManifest.releaseId)
Full release manifest SHA256: $releaseSha
Web-only folder: $webOnlyId
Web-only manifest SHA256: $webSha

Copy this kit plus the selected release folder to the server. Read
docs/development/SECURITY-ISSUE13-DOTNET10-20261023.md before activation.
The installer is verified but has NOT been executed. Installing/restarting IIS,
stopping services and applying SQL require the planned deployment approval.
The reviewed local GaoAppDb dataset has 79 migrations; verify the actual host
separately. This source requires reviewed audit migration 80. Do not use
Web-only activation against schema 79.
Keep host secrets/storage/keys outside all release folders. No credentials are
included. Schema deployment still requires genuine review/backup evidence.
Before first activation with C1, prepare persistent Assistant/CSKH storage and
configure all three host settings as documented in
docs/development/SECURITY-C1-PERSISTENT-STORAGE-20261024.txt.
Web-only activation also requires -WritersQuiesced. No automatic DB restore occurs.
Before C2 activation with configured Facebook/Zalo credentials, prepare the private
credential directory, apply path-only updates and pass IIS/ACL preflight; see
docs/development/SECURITY-C2-CHANNEL-CREDENTIALS-20261024.txt. This kit does not move
real credentials or modify the running host configuration.
For C3, keep the existing private CSKH SQLite file and follow the C1 snapshot/hold
procedure before activation. The new Web adds sync-progress tables (schema 7);
never run the older Web as a concurrent writer. Read
docs/development/SECURITY-C3-ZALO-SYNC-20261024.txt for sync status and recovery.
For C4, the Web adds visitor accounting and bounded maintenance (CSKH schema 8).
Keep existing unexpired revocations. Read
docs/development/SECURITY-C4-STORAGE-BOUNDS-20261024.txt before changing quotas.

Read-only checks from this kit:
  scripts/test-release-package.ps1 -ReleasePath <full-release>
  scripts/test-release-package.ps1 -ReleasePath <web-only-release> -WebOnly
  scripts/test-net10-host.ps1 -ReleasePath <full-release> -IisAppPoolName GaoAppPool

No command in this packaging operation installed a runtime, opened SQL,
stopped a service, changed IIS or printed a document.
"@
[IO.File]::WriteAllText((Join-Path $kitRoot 'README.txt'), $readme, [Text.UTF8Encoding]::new($false))
$kitManifest = [ordered]@{
    createdUtc=[DateTime]::UtcNow.ToString('o'); releaseId=$releaseManifest.releaseId; releaseManifestSha256=$releaseSha;
    webOnlyReleaseId=$webOnlyId; webOnlyManifestSha256=$webSha; minimumRuntime=$requirements.minimumRuntime;
    installerExecuted=$false; activationPerformed=$false;
    files=@(Get-ChildItem -LiteralPath $kitRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{path=$_.FullName.Substring($kitRoot.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
}
[IO.File]::WriteAllText((Join-Path $kitRoot 'kit-manifest.json'), ($kitManifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
Write-Host 'PASS: deployment kit and byte-preserving Web-only recovery package created; no activation.'
[pscustomobject]@{ReleasePath=$releaseRoot;ReleaseManifestSha256=$releaseSha;WebOnlyPath=$webOnlyRoot;WebOnlyManifestSha256=$webSha;KitPath=$kitRoot;KitManifestSha256=(Get-FileHash -LiteralPath (Join-Path $kitRoot 'kit-manifest.json') -Algorithm SHA256).Hash}
