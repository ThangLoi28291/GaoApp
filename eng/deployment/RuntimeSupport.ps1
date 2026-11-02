# Shared by packaging and host preflight. Never install a runtime or start a service here.
function Get-GaoProperty($Object, [string]$Path) {
    foreach ($part in $Path.Split('.')) {
        if ($null -eq $Object) { return $null }
        if ($Object -is [Collections.IDictionary]) { $Object = $Object[$part] }
        else {
            $property = $Object.PSObject.Properties[$part]
            $Object = if ($null -ne $property) { $property.Value } else { $null }
        }
    }
    return $Object
}
function Assert-GaoAppRuntimeConfiguration {
    param([Parameter(Mandatory)]$Configuration)
    $options = $Configuration.runtimeOptions
    $shared = @(@(Get-GaoProperty $options 'framework') + @(Get-GaoProperty $options 'frameworks') | Where-Object { $_ })
    $included = @(Get-GaoProperty $options 'includedFrameworks' | Where-Object { $_ })
    if (($shared.Count -eq 0) -eq ($included.Count -eq 0) -or
        ((Get-GaoProperty $options 'framework') -and (Get-GaoProperty $options 'frameworks'))) {
        throw 'Runtime configuration must declare exactly one deployment mode.'
    }
    $frameworks = @($shared) + @($included)
    if ($options.tfm -ne 'net10.0' -or !$frameworks) { throw 'Release must target net10.0 with explicit framework versions.' }
    foreach ($framework in $frameworks) {
        $version = $null
        if (![Version]::TryParse([string]$framework.version, [ref]$version) -or
            $version.Major -ne 10 -or $version.Minor -ne 0 -or $version -lt [Version]'10.0.13') {
            throw 'Release requires the reviewed .NET 10.0.13 or newer 10.0 servicing patch.'
        }
    }
    foreach ($name in @('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App')) {
        if ($name -notin $frameworks.name) { throw "Missing required framework declaration: $name" }
    }
    if (!(Get-GaoProperty $options 'includedFrameworks') -and (Get-GaoProperty $options 'rollForward') -ne 'LatestPatch') {
        throw 'Framework-dependent releases must restrict roll-forward to LatestPatch.'
    }
}

function Assert-GaoAppInstalledRuntimes {
    param([Parameter(Mandatory)]$Configuration, [Parameter(Mandatory)][string[]]$InstalledRuntimes)
    Assert-GaoAppRuntimeConfiguration $Configuration
    if (Get-GaoProperty $Configuration 'runtimeOptions.includedFrameworks') { return }
    $frameworks = @(Get-GaoProperty $Configuration 'runtimeOptions.framework') + @(Get-GaoProperty $Configuration 'runtimeOptions.frameworks') | Where-Object { $_ }
    foreach ($framework in $frameworks) {
        $minimum = [Version]$framework.version
        $found = $false
        foreach ($line in $InstalledRuntimes) {
            if ($line -match ('^' + [Regex]::Escape($framework.name) + ' (\d+\.\d+\.\d+) \[')) {
                $installed = [Version]$Matches[1]
                if ($installed.Major -eq $minimum.Major -and $installed.Minor -eq $minimum.Minor -and $installed -ge $minimum) { $found = $true }
            }
        }
        if (!$found) { throw "Missing serviced runtime: $($framework.name) >= $minimum (10.0 stable, same process architecture)." }
    }
}
