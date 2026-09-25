#requires -Version 5.1
function Assert-GaoSchemaApproval([string]$TargetEnvironment, [string]$BackupEvidence, [bool]$Waiver,
    [string]$ReviewEvidence, [string]$ManifestSha256, [string]$PoolConnection) {
    if ($Waiver -and $TargetEnvironment -ne 'Test') { throw 'Backup waiver is allowed only for an explicitly identified Test target.' }
    $connection = [Environment]::GetEnvironmentVariable('GAOAPP_DEPLOY_CONNECTION', 'Process')
    if ([string]::IsNullOrWhiteSpace($connection)) { throw 'GAOAPP_DEPLOY_CONNECTION is required.' }
    try {
        $target = New-Object Data.SqlClient.SqlConnectionStringBuilder($connection)
        $pool = New-Object Data.SqlClient.SqlConnectionStringBuilder($PoolConnection)
    }
    catch { throw 'Invalid SQL configuration; connection details suppressed.' }
    if (!$target.InitialCatalog -or !$target.DataSource -or $target.InitialCatalog -ne $pool.InitialCatalog -or $target.DataSource -ne $pool.DataSource) {
        throw 'Migration database identity must match the GaoApp pool database; aliases must match exactly.'
    }
    if (!$target.Encrypt -or $target.TrustServerCertificate) { throw 'Deployment SQL requires Encrypt=True;TrustServerCertificate=False.' }
    foreach ($file in @($ReviewEvidence) + $(if (!$Waiver) { @($BackupEvidence) } else { @() })) {
        if ([string]::IsNullOrWhiteSpace($file) -or !(Test-Path -LiteralPath $file -PathType Leaf)) { throw 'Human review/backup evidence is missing.' }
        $evidence = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
        if ($evidence.server -ne $target.DataSource -or $evidence.database -ne $target.InitialCatalog -or
            $evidence.releaseManifestSha256 -ne $ManifestSha256 -or $evidence.environment -ne $TargetEnvironment -or
            [string]::IsNullOrWhiteSpace($evidence.approvedBy)) { throw 'Human evidence must bind the release, exact SQL target and environment.' }
    }
    $review = Get-Content -LiteralPath $ReviewEvidence -Raw | ConvertFrom-Json
    if (!@($review.reviewedMigrationIds).Count -or ($review.dataTransformationsReviewed -isnot [bool]) -or ($review.dataTransformationsReviewed -ne $true) -or
        ($review.rollbackReviewed -isnot [bool]) -or ($review.rollbackReviewed -ne $true)) {
        throw 'Migration IDs, data transformations and rollback review are required.'
    }
    if (!$Waiver) {
        $backup = Get-Content -LiteralPath $BackupEvidence -Raw | ConvertFrom-Json
        if (($backup.restoreVerified -isnot [bool]) -or ($backup.restoreVerified -ne $true) -or [string]::IsNullOrWhiteSpace($backup.backupReference) -or
            [DateTimeOffset]::Parse($backup.completedUtc) -gt [DateTimeOffset]::UtcNow -or
            [DateTimeOffset]::Parse($backup.completedUtc) -lt [DateTimeOffset]::UtcNow.AddHours(-24)) {
            throw 'Database backup requires a restore-verified reference completed within 24 hours. Human must verify recovery point and quiescence.'
        }
    }
}
function New-GaoSchemaProcessInfo([string]$MigratorPath) {
    $connection = [Environment]::GetEnvironmentVariable('GAOAPP_DEPLOY_CONNECTION', 'Process')
    if ([string]::IsNullOrWhiteSpace($connection)) { throw 'Set GAOAPP_DEPLOY_CONNECTION securely in this process; never place it in command arguments or a release file.' }
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = 'dotnet'
    $start.WorkingDirectory = (Resolve-Path -LiteralPath $MigratorPath).Path
    $start.Arguments = 'GaoApp.Migrator.dll --schema-only'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    # Allowlist inheritance prevents unrelated host seed flags/JSON/SQL settings leaking in.
    $start.EnvironmentVariables.Clear()
    foreach ($key in @('PATH','SystemRoot','TEMP','TMP','USERPROFILE','LOCALAPPDATA','APPDATA','ProgramFiles','ProgramFiles(x86)','DOTNET_ROOT','DOTNET_ROOT_X64')) {
        $value = [Environment]::GetEnvironmentVariable($key, 'Process')
        if ($value) { $start.EnvironmentVariables[$key] = $value }
    }
    $start.EnvironmentVariables['DOTNET_ENVIRONMENT'] = 'Production'
    $start.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Production'
    $start.EnvironmentVariables['ConnectionStrings__DefaultConnection'] = $connection
    $start.EnvironmentVariables['SeedData__EnableDemoSeed'] = 'false'
    $start.EnvironmentVariables['SeedData__EnableDefaultAdminSeed'] = 'false'
    $start.EnvironmentVariables['ProductionBootstrap__Enabled'] = 'false'
    $start.EnvironmentVariables['Serilog__MinimumLevel__Default'] = 'Warning'
    return $start
}
function Invoke-GaoSchemaOnly([string]$MigratorPath) {
    $start = New-GaoSchemaProcessInfo $MigratorPath
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $stdout.GetAwaiter().GetResult()
        [void]$stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0 -or $output -notmatch '(?m)^SCHEMA_ONLY_VERIFIED; SourceMigrations=\d+; AppliedMigrations=\d+\s*$') {
            throw 'Schema-only failed or verification marker is missing. Web was not deployed. Review SQL/schema safely; no automatic retry or Down.'
        }
        Write-Output 'SCHEMA_ONLY_VERIFIED. No seed/bootstrap operation requested.'
    }
    finally {
        $process.Dispose()
        $start.EnvironmentVariables.Clear()
        $connection = $null
    }
}

function Invoke-GaoSchemaDeployment($Context, [string]$ReleasePath, [string]$ManifestSha256) {
    Stop-Website -Name 'GaoApp'
    Stop-WebAppPool -Name 'GaoAppPool'
    Wait-GaoPoolStopped
    $Context.AfterSchema = $true
    Invoke-GaoSchemaOnly (Join-Path $ReleasePath 'migrator')
    Assert-GaoMartUnchanged $Context.Mart
    Assert-GaoPackage $ReleasePath $ManifestSha256 $true
    Invoke-GaoWebSwitch $Context
}
