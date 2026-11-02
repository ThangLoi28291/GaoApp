[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublicConfigurationPath,
    [string]$RepositoryRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [ValidateSet('ClosedTemplate','OperatorConfiguration')][string]$Mode = 'ClosedTemplate',
    [string]$PreparedPackagePath
)
$ErrorActionPreference = 'Stop'
# Offline validation only. Never print configuration values, connect, publish,
# start a process, read a key file, migrate, change IIS, or activate anything.
$checks = [System.Collections.Generic.List[object]]::new()
function Add-Check([string]$Code,[bool]$Passed) { $checks.Add([ordered]@{code=$Code;passed=$Passed}) }
try { $config = Get-Content -LiteralPath $PublicConfigurationPath -Raw | ConvertFrom-Json -AsHashtable }
catch { throw 'CONFIGURATION_JSON_INVALID' }
$hostSettings = $config.StorefrontHost
Add-Check 'RELEASE_FLAG_BOOLEAN' ($hostSettings.ReleaseEnabled -is [bool])
Add-Check 'PREVIEW_FLAG_BOOLEAN' ($hostSettings.PreviewEnabled -is [bool])
if($Mode -eq 'ClosedTemplate') {
    Add-Check 'DEFAULT_RELEASE_CLOSED' ($hostSettings.ReleaseEnabled -ceq $false)
    Add-Check 'DEFAULT_PREVIEW_CLOSED' ($hostSettings.PreviewEnabled -ceq $false)
    Add-Check 'TEMPLATE_NO_CONNECTION' (-not $config.ConnectionStrings -or -not $config.ConnectionStrings.DefaultConnection)
} else {
    $hostName=[string]$hostSettings.Host
    Add-Check 'EXACT_HOST_CONFIGURED' (-not [string]::IsNullOrWhiteSpace($hostName) -and $hostName.IndexOfAny([char[]]'/\@?#') -lt 0 -and $hostName -notmatch '\s')
    Add-Check 'FIXED_STORE_CONFIGURED' (-not [string]::IsNullOrWhiteSpace([string]$hostSettings.StoreSubdomain))
    Add-Check 'CONNECTION_CONFIGURED_NOT_TESTED' (-not [string]::IsNullOrWhiteSpace([string]$config.ConnectionStrings.DefaultConnection))
    Add-Check 'WINDOWS_PROTECTION_CONFIGURED_NOT_DECRYPTED' ($hostSettings.KeyProtection -ceq 'WindowsDpapiCurrentUser')
    $keyPath=[string]$hostSettings.KeyRingPath
    $privatePath=[IO.Path]::IsPathFullyQualified($keyPath)
    if($privatePath) {
        $keyFull=[IO.Path]::GetFullPath($keyPath).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
        $projectFull=[IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'GaoApp.Storefront.Web')).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
        $privatePath=-not ($keyFull.Equals($projectFull,[StringComparison]::OrdinalIgnoreCase) -or $keyFull.StartsWith($projectFull+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase))
        foreach($unsafe in @($hostSettings.UploadsRoot,[Environment]::GetFolderPath('ApplicationData'),[Environment]::GetFolderPath('LocalApplicationData'))) {
            if($unsafe) { $unsafeFull=[IO.Path]::GetFullPath($unsafe).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar); if($keyFull.Equals($unsafeFull,[StringComparison]::OrdinalIgnoreCase) -or $keyFull.StartsWith($unsafeFull+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){$privatePath=$false} }
        }
    }
    Add-Check 'KEY_PATH_EXTERNAL_NOT_INSPECTED' $privatePath
}
$projectPath=Join-Path $RepositoryRoot 'GaoApp.Storefront.Web/GaoApp.Storefront.Web.csproj'
[xml]$project=Get-Content -LiteralPath $projectPath -Raw
$refs=@($project.Project.ItemGroup.ProjectReference.Include)
Add-Check 'SEPARATE_PUBLIC_PROJECT_REFERENCES' ($refs.Count -eq 2 -and @($refs|Where-Object {$_ -notin @('../GaoApp.Application/GaoApp.Application.csproj','../GaoApp.Infrastructure/GaoApp.Infrastructure.csproj')}).Count -eq 0)
Add-Check 'PUBLIC_PUBLISH_GUARD_IMPORTED' (@($project.Project.Import.Project) -contains '../eng/deployment/StorefrontPublishSafety.targets')
Add-Check 'PUBLIC_TARGET_NET10' ($project.Project.PropertyGroup.TargetFramework -ceq 'net10.0')
$packages=@($project.Project.ItemGroup.PackageReference|Where-Object {$_})
Add-Check 'PINNED_PUBLIC_IMAGE_DEPENDENCIES' ($packages.Count -eq 2 -and @($packages|Where-Object {$_.Include -notin @('SkiaSharp','SkiaSharp.NativeAssets.Linux.NoDependencies') -or $_.Version -cne '4.154.0'}).Count -eq 0)
foreach($required in @('eng/deployment/StorefrontPublishSafety.targets','eng/deployment/storefront.appsettings.json','GaoApp.Storefront.Web/Data/vn-address-catalog-observed-20261031.json','GaoApp.Storefront.Web/Data/ADDRESS-SOURCE.txt','GaoApp.Storefront.Web/wwwroot/assets/vendor/three-r187/LICENSE.txt','GaoApp.Storefront.Web/wwwroot/assets/fonts/OFL.txt','GaoApp.Storefront.Web/wwwroot/assets/fonts/InterVariable.woff2','GaoApp.Storefront.Web/wwwroot/assets/licenses/SkiaSharp-LICENSE.txt')) {
    Add-Check ('SOURCE_CLOSURE_'+([IO.Path]::GetFileName($required) -replace '[^A-Za-z0-9]','_').ToUpperInvariant()) (Test-Path -LiteralPath (Join-Path $RepositoryRoot $required) -PathType Leaf)
}
if($PreparedPackagePath) {
    # Package metadata only; no private/configuration file contents are inspected.
    $stack=[Collections.Generic.Stack[string]]::new(); $stack.Push([IO.Path]::GetFullPath($PreparedPackagePath)); $safe=$true
    while($stack.Count) {
        $entry=Get-Item -LiteralPath $stack.Pop() -Force
        if(($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){$safe=$false;continue}
        if($entry.PSIsContainer){ foreach($child in Get-ChildItem -LiteralPath $entry.FullName -Force){$stack.Push($child.FullName)}; continue }
        if($entry.Name -match '(?i)\.(pfx|p12|pem|key|xml|bak|mdf|ldf|db|sqlite|zip|log|user)$' -or $entry.Name -match '(?i)^GaoApp\.(Web|Tests|Migrator|LabelServer|LabelPrintClient|InvoiceWorker)\.(dll|exe)$' -or $entry.Name -match '(?i)^appsettings.+\.json$'){$safe=$false}
    }
    Add-Check 'PACKAGE_METADATA_NO_PRIVATE_OR_ADMIN_INPUTS' $safe
}
$passed=@($checks|Where-Object {-not $_.passed}).Count -eq 0
[ordered]@{status=$(if($passed){'OFFLINE_CHECKS_PASSED'}else{'BLOCKED'});mode=$Mode;published=$false;runtimeVerified=$false;ownerUatVerified=$false;checks=$checks}|ConvertTo-Json -Depth 6
if(-not $passed){exit 2}

