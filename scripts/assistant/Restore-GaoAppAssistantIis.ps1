#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess=$true, ConfirmImpact='Medium')]
param(
    [string]$SiteName='GaoApp',
    [string]$AppPoolName='GaoAppPool',
    [string]$GaoAppDirectory='C:\inetpub\GaoApp',
    [string]$ChatbotRuntimeFile='C:\ProgramData\ChatbotLocal\config\runtime.json',
    [string]$PrivateKeyFile='C:\ProgramData\GaoApp\assistant\secrets\gaoapp-assistant-private.pem',
    [string]$StoreSettingsDirectory='C:\ProgramData\GaoApp\assistant\data',
    [string]$BackupDirectory='C:\ProgramData\GaoApp\assistant\backups',
    [string]$BaseUrl='https://chat.gaomart.com.vn:8444',
    [string]$KeyId='gaoapp-assistant-1'
)
$ErrorActionPreference='Stop'
function Get-TrustedPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'A persistent path is required.' }
    $resolved=[IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($Path)).TrimEnd('\','/')
    $cursor=$resolved
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Persistent paths must not traverse reparse points.'
        }
        $parent=[IO.Path]::GetDirectoryName($cursor)
        if ($parent -eq $cursor) { break }; $cursor=$parent
    }
    return $resolved
}
if ($SiteName -notmatch '^[A-Za-z0-9_. -]+$' -or $AppPoolName -notmatch '^[A-Za-z0-9_. -]+$') { throw 'Invalid IIS site or pool name.' }
$endpoint=$null
if (![Uri]::TryCreate($BaseUrl,[UriKind]::Absolute,[ref]$endpoint) -or $endpoint.Scheme -ne 'https' -or
    $endpoint.Host -ne 'chat.gaomart.com.vn' -or $endpoint.UserInfo -or $endpoint.Query -or $endpoint.Fragment -or $endpoint.AbsolutePath -ne '/') {
    throw 'The assistant endpoint must be the trusted HTTPS service.'
}
$appPath=Get-TrustedPath $GaoAppDirectory
$runtimePath=Get-TrustedPath $ChatbotRuntimeFile
$keyPath=Get-TrustedPath $PrivateKeyFile
$settingsPath=Get-TrustedPath $StoreSettingsDirectory
$backupPath=Get-TrustedPath $BackupDirectory
foreach ($path in @($runtimePath,$keyPath,$settingsPath,$backupPath)) {
    if ($path.Equals($appPath,[StringComparison]::OrdinalIgnoreCase) -or $path.StartsWith($appPath+'\',[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Persistent assistant paths must stay outside the published application.'
    }
}
foreach ($file in @((Join-Path $appPath 'GaoApp.Web.dll'),$runtimePath,$keyPath,(Join-Path $appPath 'web.config'))) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw 'A required application or persistent assistant file is missing.' }
}
foreach ($directory in @($settingsPath,$backupPath)) {
    if (!(Test-Path -LiteralPath $directory -PathType Container)) { throw 'Create the persistent assistant directories before configuration.' }
}
# Read only the public connection contract; never read private-key contents or copy provider secrets.
$runtime=Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json
$assistant=$runtime.AssistantApi
if (!$assistant -or $assistant.Enabled -ne $true -or [string]::IsNullOrWhiteSpace($assistant.Issuer) -or
    !$assistant.PublicKeyFiles -or !$assistant.PublicKeyFiles.PSObject.Properties[$KeyId]) {
    throw 'The existing GaoApp issuer/key identifier must already be enabled in ChatbotLocal.'
}
$issuer=$null
if (![Uri]::TryCreate([string]$assistant.Issuer,[UriKind]::Absolute,[ref]$issuer) -or $issuer.Scheme -ne 'https' -or $issuer.UserInfo) {
    throw 'The existing GaoApp issuer/key identifier must use HTTPS.'
}
$desired=[ordered]@{
    Assistant__Enabled='true'; Assistant__BaseUrl=$BaseUrl.TrimEnd('/'); Assistant__Issuer=[string]$assistant.Issuer;
    Assistant__KeyId=$KeyId; Assistant__PrivateKeyFile=$keyPath; Assistant__StoreSettingsDirectory=$settingsPath
}
$xml=New-Object System.Xml.XmlDocument
$xml.XmlResolver=$null
$readerSettings=New-Object System.Xml.XmlReaderSettings
$readerSettings.DtdProcessing=[Xml.DtdProcessing]::Prohibit
$readerSettings.XmlResolver=$null
$reader=[Xml.XmlReader]::Create((Join-Path $appPath 'web.config'),$readerSettings)
try { $xml.Load($reader) } finally { $reader.Dispose() }
foreach ($entry in $xml.SelectNodes('//aspNetCore/environmentVariables/environmentVariable')) {
    if ($entry.name -like 'Assistant__*' -and (!$desired.Contains($entry.name) -or $desired[$entry.name] -cne $entry.value)) {
        throw "Conflicting legacy assistant environment variable: $($entry.name)."
    }
}
Import-Module WebAdministration -ErrorAction Stop
$site=Get-Website -Name $SiteName
if (!$site -or $site.applicationPool -ne $AppPoolName -or (Get-TrustedPath $site.physicalPath) -ne $appPath) {
    throw 'The selected IIS site/pool does not match the published application.'
}
foreach ($other in @(Get-Website)) {
    if ($other.Name -ne $SiteName -and $other.applicationPool -eq $AppPoolName) { throw 'The application pool is shared by another site.' }
    foreach ($application in @(Get-WebApplication -Site $other.Name)) {
        if ($application.applicationPool -eq $AppPoolName) { throw 'The application pool is shared by another site or application.' }
    }
}
$poolState=(Get-WebAppPoolState -Name $AppPoolName).Value
if ($poolState -notin @('Started','Stopped')) { throw 'The application pool is transitioning; retry when stable.' }
$configPath='MACHINE/WEBROOT/APPHOST'
$collection="system.applicationHost/applicationPools/add[@name='$AppPoolName']/environmentVariables"
$current=@{}
foreach ($entry in @((Get-WebConfigurationProperty -PSPath $configPath -Filter $collection -Name '.').Collection)) { $current[[string]$entry.name]=[string]$entry.value }
$changes=@($desired.Keys | Where-Object { !$current.ContainsKey($_) -or $current[$_] -cne $desired[$_] })
if (!$changes.Count) {
    @{status='GAOAPP_ASSISTANT_ALREADY_CONFIGURED';changed=$false;restarted=$false} | ConvertTo-Json -Compress
    return
}
if (!$PSCmdlet.ShouldProcess("$SiteName / $AppPoolName",'Restore persistent assistant connection settings')) {
    @{status='GAOAPP_ASSISTANT_VALIDATED';changed=$false;restarted=$false} | ConvertTo-Json -Compress
    return
}
$before=@{}
foreach ($name in $desired.Keys) { if ($current.ContainsKey($name)) { $before[$name]=$current[$name] } }
$backupFile=Join-Path $backupPath ('assistant-iis-'+[Guid]::NewGuid().ToString('N')+'.json')
@{site=$SiteName;pool=$AppPoolName;createdUtc=[DateTime]::UtcNow.ToString('o');variables=$before} | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath $backupFile -Encoding UTF8
$applied=New-Object 'System.Collections.Generic.List[string]'
try {
    foreach ($name in $changes) {
        if ($current.ContainsKey($name)) {
            Set-WebConfigurationProperty -PSPath $configPath -Filter "$collection/add[@name='$name']" -Name value -Value $desired[$name]
        } else {
            Add-WebConfigurationProperty -PSPath $configPath -Filter $collection -Name '.' -Value @{name=$name;value=$desired[$name]}
        }
        $applied.Add($name)
    }
} catch {
    $rollbackFailed=$false
    foreach ($name in @($applied)) {
        try {
            if ($before.ContainsKey($name)) { Set-WebConfigurationProperty -PSPath $configPath -Filter "$collection/add[@name='$name']" -Name value -Value $before[$name] }
            else { Remove-WebConfigurationProperty -PSPath $configPath -Filter $collection -Name '.' -AtElement @{name=$name} }
        } catch { $rollbackFailed=$true }
    }
    if ($rollbackFailed) { throw "IIS configuration failed and rollback needs administrator review. Backup: $backupFile" }
    throw 'IIS configuration failed; prior variables were restored. The pool was not restarted.'
}
if ($poolState -eq 'Started') { Restart-WebAppPool -Name $AppPoolName }
@{status='GAOAPP_ASSISTANT_CONFIGURED';changed=$true;restarted=($poolState -eq 'Started');backupFile=$backupFile} | ConvertTo-Json -Compress
