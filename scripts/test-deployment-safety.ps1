#requires -Version 5.1
# Local synthetic files and mocked IIS/process boundaries only. Never deploys or opens SQL.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $repo 'eng/deployment/Deployment.Common.ps1')
. (Join-Path $repo 'eng/deployment/SchemaMigration.ps1')
$checks = 0
function Check([string]$Name, [scriptblock]$Action) { & $Action; $script:checks++; Write-Output "PASS $Name" }
function Reject([scriptblock]$Action, [string]$Expected) {
    try { & $Action; throw 'EXPECTED_REJECTION_MISSING' }
    catch { if ($_.Exception.Message -notlike "*$Expected*") { throw } }
}
$work = Join-Path ([IO.Path]::GetTempPath()) ('gaoapp-deploy-safety-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$originalConnection = [Environment]::GetEnvironmentVariable('GAOAPP_DEPLOY_CONNECTION', 'Process')
try {
    Check 'PowerShell syntax for every deployment file' {
        foreach ($file in @(Get-ChildItem (Join-Path $repo 'eng/deployment') -Filter '*.ps1') + @(Get-Item (Join-Path $repo 'scripts/publish-staging-release.ps1'), (Join-Path $repo 'scripts/test-release-package.ps1'))) {
            $errors = $null; $tokens = $null
            [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
            if (@($errors).Count) { throw "Parse failed: $($file.Name)" }
        }
    }
    Check 'Protected path overlap including case and parent traversal' {
        foreach ($pair in @(@('C:\GaoAppData','c:\gaoappdata\keys'), @('D:\GaoMart','D:\GaoMart\..\GaoMart\web'))) {
            if (!(Test-GaoOverlap $pair[0] $pair[1])) { throw 'Overlap not detected.' }
        }
        if (Test-GaoOverlap 'D:\GaoApp' 'D:\GaoApp2') { throw 'Sibling confused with child.' }
    }
    $release = Join-Path $work 'release'
    foreach ($component in @('Web','Migrator')) {
        $dir = Join-Path $release $component.ToLowerInvariant()
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repo "eng/deployment/$($component.ToLowerInvariant()).appsettings.json") -Destination (Join-Path $dir 'appsettings.json')
        foreach ($file in @("GaoApp.$component.dll", "GaoApp.$component.deps.json", "GaoApp.$component.runtimeconfig.json", 'GaoApp.Domain.dll','GaoApp.Application.dll','GaoApp.Infrastructure.dll')) {
            [IO.File]::WriteAllText((Join-Path $dir $file), 'synthetic package bytes')
        }
    }
    Copy-Item -LiteralPath (Join-Path $repo 'GaoApp.Web/web.config') -Destination (Join-Path $release 'web/web.config')
    $manifest = @{ files = @(Get-ChildItem $release -Recurse -File | ForEach-Object { @{path=$_.FullName.Substring($release.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash} }) }
    $manifestPath = Join-Path $release 'release-manifest.json'
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6))
    $sha = (Get-FileHash $manifestPath -Algorithm SHA256).Hash
    Check 'Paired package verified against trusted manifest' { Assert-GaoPackage $release $sha $true }
    Check 'Normal package rejects a Web recovery command' {
        $configPath = Join-Path $release 'web/web.config'
        $normalConfig = [IO.File]::ReadAllText($configPath)
        try {
            [IO.File]::WriteAllText($configPath, $normalConfig.Replace('arguments=".\GaoApp.Web.dll"', 'arguments=".\GaoApp.Web.dll --recover-admin-menus-only"'))
            Reject { Assert-GaoPackage $release $sha $true } 'IIS release is missing'
        }
        finally { [IO.File]::WriteAllText($configPath, $normalConfig) }
    }
    Check 'TH1 rejects a package containing Migrator' { Reject { Assert-GaoPackage $release $sha $false } 'Web-only package' }
    Check 'Wrong trusted manifest hash rejected' { Reject { Assert-GaoPackage $release ('0' * 64) $true } 'Manifest SHA-256 mismatch' }
    [IO.File]::AppendAllText((Join-Path $release 'web/GaoApp.Web.dll'), 'tamper')
    Check 'Changed package bytes rejected' { Reject { Assert-GaoPackage $release $sha $true } 'Release integrity failed' }
    [IO.File]::WriteAllText((Join-Path $release 'web/GaoApp.Web.dll'), 'synthetic package bytes')
    $webOnly = Join-Path $work 'web-only'
    New-Item -ItemType Directory $webOnly | Out-Null
    Copy-Item -LiteralPath (Join-Path $release 'web') -Destination $webOnly -Recurse
    $webManifest = @{ files = @($manifest.files | Where-Object { $_.path -like 'web/*' }) }
    [IO.File]::WriteAllText((Join-Path $webOnly 'release-manifest.json'), ($webManifest | ConvertTo-Json -Depth 6))
    Check 'TH1 accepts an intact Web-only package' { Assert-GaoPackage $webOnly (Get-FileHash (Join-Path $webOnly 'release-manifest.json') -Algorithm SHA256).Hash $false }
    $env:GAOAPP_DEPLOY_CONNECTION = 'Server=synthetic.example.test;Database=GaoAppTest;Integrated Security=True;Encrypt=True;TrustServerCertificate=False'
    Check 'Schema child receives one mode and safe isolated environment' {
        $info = New-GaoSchemaProcessInfo (Join-Path $release 'migrator')
        if ($info.Arguments -cne 'GaoApp.Migrator.dll --schema-only' -or $info.UseShellExecute -or !$info.CreateNoWindow) { throw 'Unsafe process arguments.' }
        foreach ($key in @('SeedData__EnableDemoSeed','SeedData__EnableDefaultAdminSeed','ProductionBootstrap__Enabled')) {
            if ($info.EnvironmentVariables[$key] -ne 'false') { throw 'Unsafe seed flag.' }
        }
        if ($info.Arguments.Contains($env:GAOAPP_DEPLOY_CONNECTION) -or $info.EnvironmentVariables.ContainsKey('GAOAPP_DEPLOY_CONNECTION')) { throw 'Secret leaked beyond required child SQL configuration.' }
    }
    $review = Join-Path $work 'review.json'
    $reviewData = @{server='synthetic.example.test';database='GaoAppTest';environment='Test';releaseManifestSha256=$sha;approvedBy='Synthetic reviewer';reviewedMigrationIds=@('SyntheticMigration');dataTransformationsReviewed=$true;rollbackReviewed=$true}
    [IO.File]::WriteAllText($review, ($reviewData | ConvertTo-Json))
    Check 'Explicit test backup waiver with matching target/release' { Assert-GaoSchemaApproval 'Test' '' $true $review $sha $env:GAOAPP_DEPLOY_CONNECTION }
    Check 'Production cannot waive backup' { Reject { Assert-GaoSchemaApproval 'Production' '' $true $review $sha $env:GAOAPP_DEPLOY_CONNECTION } 'only for' }
    Check 'Different SQL target rejected before migration' { Reject { Assert-GaoSchemaApproval 'Test' '' $true $review $sha $env:GAOAPP_DEPLOY_CONNECTION.Replace('GaoAppTest','AnotherDb') } 'must match' }
    Check 'Review for a different release rejected' { Reject { Assert-GaoSchemaApproval 'Test' '' $true $review ('1' * 64) $env:GAOAPP_DEPLOY_CONNECTION } 'must bind' }
    $script:events = New-Object 'Collections.Generic.List[string]'
    function Wait-GaoPoolStopped { }
    function Stop-Website([string]$Name) { if ($Name -ne 'GaoApp') { throw 'Wrong site' }; $script:events.Add('StopSite') }
    function Stop-WebAppPool([string]$Name) { if ($Name -ne 'GaoAppPool') { throw 'Wrong pool' }; $script:events.Add('StopPool') }
    function Start-Website([string]$Name) { if ($Name -ne 'GaoApp') { throw 'Wrong site' }; $script:events.Add('StartSite') }
    function Start-WebAppPool([string]$Name) { if ($Name -ne 'GaoAppPool') { throw 'Wrong pool' }; $script:events.Add('StartPool') }
    function Set-ItemProperty([string]$LiteralPath, [string]$Name, [string]$Value) {
        if ($LiteralPath -ne 'IIS:\Sites\GaoApp' -or $Name -ne 'physicalPath') { throw 'Wrong IIS mutation' }
        $script:events.Add('Switch:' + $Value)
    }
    function Assert-GaoMartUnchanged($Before) { $script:events.Add('CheckMart') }
    function Invoke-GaoSmoke([uri]$Url) { $script:events.Add('Smoke'); if ($script:failSmoke) { throw 'Synthetic smoke failure' } }
    $oldWeb = Join-Path $work 'old-web'; $backup = Join-Path $work 'backups'
    New-Item -ItemType Directory $oldWeb,$backup | Out-Null
    [IO.File]::WriteAllText((Join-Path $oldWeb 'old.dll'), 'prior deployed bytes')
    $ctx = [pscustomobject]@{OldWeb=$oldWeb;NewWeb=(Join-Path $webOnly 'web');BackupRoot=$backup;SmokeUrl=[uri]'https://gaoapp.example.test/health/ready';Mart=@{};AfterSchema=$false}
    $script:failSmoke = $false
    Check 'TH1 backup/activation touches only GaoApp and checks smoke/GaoMart' {
        Invoke-GaoWebSwitch $ctx
        if (($script:events -join ',') -ne "StopSite,StopPool,Switch:$($ctx.NewWeb),StartPool,StartSite,Smoke,CheckMart") { throw 'Wrong Web-only execution order' }
        $copy = @(Get-ChildItem $backup -Directory)[0].FullName
        Assert-GaoTreeEqual @(Get-GaoTree $oldWeb) @(Get-GaoTree $copy)
    }
    $script:events.Clear(); $script:failSmoke = $true
    Check 'TH1 smoke failure restores previous Web path' {
        Reject { Invoke-GaoWebSwitch $ctx } 'Synthetic smoke failure'
        if (!$script:events.Contains('Switch:' + $oldWeb)) { throw 'Previous Web was not restored' }
    }
    $script:events.Clear(); $ctx.AfterSchema = $true
    Check 'TH2 failure never auto-restarts old Web on changed schema' {
        Reject { Invoke-GaoWebSwitch $ctx } 'remains stopped'
        if ($script:events.Contains('Switch:' + $oldWeb)) { throw 'Unsafe schema rollback' }
    }
    function Invoke-GaoSchemaOnly([string]$Path) { $script:events.Add('SchemaOnly'); if ($script:failMigration) { throw 'Synthetic migration failure' } }
    function Assert-GaoPackage([string]$Path,[string]$Hash,[bool]$WithSchema) { if (!$WithSchema) { throw 'Wrong mode' }; $script:events.Add('VerifyPackage') }
    function Invoke-GaoWebSwitch($Context) { $script:events.Add('DeployWeb') }
    $script:events.Clear(); $script:failMigration = $true
    Check 'Migration failure blocks Web deployment and has no automatic retry' {
        Reject { Invoke-GaoSchemaDeployment $ctx $release $sha } 'Synthetic migration failure'
        if (($script:events -join ',') -ne 'StopSite,StopPool,SchemaOnly') { throw 'Migration failure order broken' }
    }
    $script:events.Clear(); $script:failMigration = $false
    Check 'TH2 invokes schema-only once, verifies, then deploys Web' {
        Invoke-GaoSchemaDeployment $ctx $release $sha
        if (($script:events -join ',') -ne 'StopSite,StopPool,SchemaOnly,CheckMart,VerifyPackage,DeployWeb') { throw 'TH2 order broken' }
    }
    Write-Output "RESULT: $checks/$checks PASS (synthetic files; mocked IIS/process boundaries; no deployment or SQL)."
}
finally {
    [Environment]::SetEnvironmentVariable('GAOAPP_DEPLOY_CONNECTION', $originalConnection, 'Process')
    # Retain the isolated synthetic workspace for inspection; no recursive cleanup is needed.
}
