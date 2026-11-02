#requires -Version 5.1
# No output includes credential values, hashes, or supplied paths.
function Get-GaoIisPublicRoots {
    Add-Type -Path (Join-Path $env:windir 'System32/inetsrv/Microsoft.Web.Administration.dll')
    $manager=New-Object Microsoft.Web.Administration.ServerManager
    try {
        $pool=$manager.ApplicationPools['GaoAppPool']
        if(!$pool -or $pool.ProcessModel.IdentityType.ToString() -ne 'ApplicationPoolIdentity'){throw 'Channel credentials require the reviewed GaoAppPool ApplicationPoolIdentity.'}
        Get-GaoChannelPublicRootsFromSites $manager.Sites
    } finally {$manager.Dispose()}
}
function Get-GaoChannelPublicRootsFromSites($Sites) {
        foreach($site in $Sites) {
            foreach($app in $site.Applications) {
                foreach($directory in $app.VirtualDirectories) {
                    $path=[Environment]::ExpandEnvironmentVariables($directory.PhysicalPath)
                    if (![IO.Path]::IsPathRooted($path) -or !(Test-Path -LiteralPath $path -PathType Container)) {throw 'Cannot verify all IIS public roots.'}
                    # A link anywhere in a public tree could expose private storage by an alias.
                    Assert-GaoNoLinks $path
                    $path
                }
            }
        }
}
function Assert-GaoChannelCredentialAcl([string]$Directory) {
    if(!$Directory){throw 'A private channel credential directory is required.'}
    try {
        $poolSid=(New-Object Security.Principal.NTAccount('IIS APPPOOL\GaoAppPool')).Translate([Security.Principal.SecurityIdentifier]).Value
        Assert-GaoChannelCredentialParents $Directory
        foreach($entry in @(Get-Item -LiteralPath $Directory -Force)+@(Get-ChildItem -LiteralPath $Directory -Recurse -Force)) {
            $acl=Get-Acl -LiteralPath $entry.FullName
            Assert-GaoChannelCredentialDescriptor $acl $poolSid
        }
    }catch{throw 'Channel credential ACL must allow only SYSTEM, Administrators and read-only IIS APPPOOL\GaoAppPool; secure it before activation.'}
}
function Assert-GaoChannelCredentialParents([string]$Directory) {
    $trustedInstaller=(New-Object Security.Principal.NTAccount('NT SERVICE\TrustedInstaller')).Translate([Security.Principal.SecurityIdentifier]).Value
    $parent=[IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Directory)))
    while($parent){Assert-GaoChannelCredentialParentDescriptor (Get-Acl -LiteralPath $parent.FullName) $trustedInstaller;$parent=$parent.Parent}
}
function Assert-GaoChannelCredentialParentDescriptor($Acl,[string]$TrustedInstallerSid) {
    $trusted=@('S-1-5-18','S-1-5-32-544',$TrustedInstallerSid)
    if($Acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted){throw 'Unsafe credential ancestor ACL.'}
    $dangerous=[Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
    foreach($rule in $Acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
        if($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly){continue}
        if($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin $trusted -and ($rule.FileSystemRights -band $dangerous)){throw 'Unsafe credential ancestor ACL.'}
    }
}
function Assert-GaoChannelCredentialDescriptor($Acl,[string]$PoolSid) {
    $allowed=@('S-1-5-18','S-1-5-32-544',$PoolSid)
    if(!$PoolSid -or $Acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin @('S-1-5-18','S-1-5-32-544')){throw 'Unsafe channel credential ACL.'}
    $read=[Security.AccessControl.FileSystemRights]::ReadAndExecute
    $permitted=[int]($read -bor [Security.AccessControl.FileSystemRights]::Synchronize)
    $poolRights=0
    foreach($rule in $Acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
        if($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly){continue}
        $sid=$rule.IdentityReference.Value
        if($rule.AccessControlType -ne 'Allow' -or $sid -notin $allowed){throw 'Unsafe channel credential ACL.'}
        if($sid -eq $PoolSid){
            if([int]$rule.FileSystemRights -band (-bnot $permitted)){throw 'Unsafe channel credential ACL.'}
            $poolRights=$poolRights -bor [int]$rule.FileSystemRights
        }
    }
    if(($poolRights -band [int]$read) -ne [int]$read){throw 'Channel credential ACL does not grant the pool read access.'}
}
function Invoke-GaoChannelCredentialCheck([string]$WebPath,[hashtable]$Environment,[string[]]$PublicRoots,[switch]$CheckAcl) {
    $settings=@{}
    foreach($entry in $Environment.GetEnumerator()) {
        $key=$entry.Key.Replace(':','__')
        if($key -match '(?i)^CustomerChannels__' -or $key -eq 'Storage__UploadRoot'){
            if($settings.ContainsKey($key) -and $settings[$key] -cne [string]$entry.Value){throw 'Conflicting normalized channel environment keys.'}
            $settings[$key]=[string]$entry.Value
        }
    }
    # Deployment uses pool environment as the authoritative channel configuration.
    # Refuse web.config overrides rather than checking a different effective configuration.
    [xml]$webConfig=Get-Content -LiteralPath (Join-Path $WebPath 'web.config') -Raw
    if(@($webConfig.SelectNodes('//aspNetCore/environmentVariables/environmentVariable') | Where-Object {$_.name -match '(?i)^CustomerChannels__|^Storage__UploadRoot$'}).Count){throw 'Channel settings must be configured in the pool environment, not web.config.'}
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName='dotnet';$start.Arguments='"'+(Join-Path $WebPath 'GaoApp.Web.dll')+'" --channel-credentials'
    $start.WorkingDirectory=$WebPath;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $start.EnvironmentVariables.Clear()
    foreach($key in @('PATH','DOTNET_ROOT','DOTNET_ROOT_X64','SystemRoot','WINDIR','TEMP','TMP','USERPROFILE','ProgramFiles','ProgramFiles(x86)','ProgramW6432')) {
        $value=[Environment]::GetEnvironmentVariable($key);if($value){$start.EnvironmentVariables[$key]=$value}
    }
    $process=New-Object Diagnostics.Process
    $process.StartInfo=$start
    try {
        [void]$process.Start()
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        $process.StandardInput.Write((@{webPath=$WebPath;settings=$settings;publicRoots=@($PublicRoots)} | ConvertTo-Json -Depth 6 -Compress));$process.StandardInput.Close()
        if(!$process.WaitForExit(30000)){$process.Kill();throw 'Channel credential preflight timed out.'}
        [void]$stderr.GetAwaiter().GetResult()
        if($process.ExitCode -ne 0){throw 'Channel credential preflight rejected unsafe, missing or unreadable private files.'}
        $result=$stdout.GetAwaiter().GetResult() | ConvertFrom-Json
        if($result.status -ne 'verified'){throw 'Channel credential verification was not confirmed.'}
        if($CheckAcl -and $result.files -gt 0){Assert-GaoChannelCredentialAcl $settings['CustomerChannels__CredentialDirectory']}
        return $result
    }finally{$process.Dispose();$start.EnvironmentVariables.Clear()}
}

function Set-GaoChannelCredentialDirectoryAcl([string]$Directory) {
$acl=[Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true,$false)
$admins=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$acl.SetOwner($admins)
foreach($sid in @($admins,[Security.Principal.SecurityIdentifier]::new('S-1-5-18'))){$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))}
$pool=[Security.Principal.NTAccount]::new('IIS APPPOOL\GaoAppPool')
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($pool,'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $Directory -AclObject $acl
}
function Set-GaoChannelCredentialOwner([string]$Path) {
    $acl=Get-Acl -LiteralPath $Path;$acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'));Set-Acl -LiteralPath $Path -AclObject $acl
}
function New-GaoChannelCredentialCopy([hashtable]$Environment,[string]$SourceWebPath,[string]$PreparedWebPath,[string]$DestinationDirectory,[string[]]$PublicRoots) {
if($PSVersionTable.PSVersion.Major -lt 7 -or !$IsWindows){throw 'Channel credential preparation requires Windows PowerShell 7.'}
$destination=[IO.Path]::GetFullPath($DestinationDirectory)
if(![IO.Path]::IsPathFullyQualified($DestinationDirectory) -or $destination.StartsWith('\\') -or $destination.Contains('~') -or $destination.Substring(2).Contains(':') -or @($DestinationDirectory -split '[\\/]' | Where-Object {$_ -in @('.','..')}).Count){throw 'Use a NEW absolute private directory outside existing storage.'}
if(Test-Path -LiteralPath $destination){throw 'Use a NEW absolute private directory outside existing storage.'}
$roots=@($PublicRoots)+@($SourceWebPath,$PreparedWebPath)
foreach($key in @('Storage__UploadRoot','Assistant__StoreSettingsDirectory','CustomerService__DatabasePath','DataProtection__KeysPath','DataProtection__CertificatePath')){if($environment[$key]){$roots+=@($environment[$key])}}
foreach($public in $roots){if(Test-GaoOverlap $destination $public){throw 'Credential directory overlaps public/release/upload or other private storage.'}}
# Validate the existing ancestor chain before creating anything.
$parent=[IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($destination))
while($parent){if($parent.Exists -and ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Credential ancestor contains a link/reparse point.'};$parent=$parent.Parent}
$keys=@($environment.Keys | Where-Object {$_ -match '(?i)^CustomerChannels__Connections__\d+__(AccessTokenFile|AppSecretFile|VerifyTokenFile)$' -and $environment[$_]})
if(!$keys.Count){throw 'No configured channel credential file paths were found in the host environment file.'}
$sources=@{}
foreach($key in $keys){
    try {
        $source=[IO.Path]::GetFullPath([string]$environment[$key],$SourceWebPath)
        if($source.StartsWith('\\') -or $source.Contains('~') -or $source.Substring(2).Contains(':')){throw 'unsafe local credential path'}
        Assert-GaoNoLinks $source
        if((Get-Item -LiteralPath $source).Length -gt 32768){throw 'oversized'}
        $sources[$key]=$source
    }catch{throw 'A source credential file is missing, linked or too large; no source files were changed.'}
}
Assert-GaoChannelCredentialParents $destination
[void](New-Item -ItemType Directory -Path $destination)
Set-GaoChannelCredentialDirectoryAcl $destination
$updates=@{CustomerChannels__CredentialDirectory=$destination}
$copied=@{};$index=0
foreach($key in $keys | Sort-Object){
    $source=$sources[$key]
    if(!$copied.ContainsKey($source)){
        $index++;$target=Join-Path $destination ("credential-$index.txt")
        $inputFile=[IO.File]::Open($source,'Open','Read','Read');$outputFile=$null
        try {
            if($inputFile.Length -lt 16 -or $inputFile.Length -gt 32768){throw 'Invalid credential size.'}
            $bytes=[byte[]]::new(32769);$count=0
            while($count -lt $bytes.Length){$read=$inputFile.Read($bytes,$count,$bytes.Length-$count);if($read -eq 0){break};$count+=$read}
            if($count -lt 16 -or $count -gt 32768){throw 'Credential changed while copying.'}
            $outputFile=[IO.File]::Open($target,'CreateNew','Write','None');$outputFile.Write($bytes,0,$count);$outputFile.Dispose();$outputFile=$null
            if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]$bytes[0..($count-1)])) -cne [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($target)))){throw 'Credential copy verification failed.'}
            Set-GaoChannelCredentialOwner $target
            $copied[$source]=$target
        }finally{if($outputFile){$outputFile.Dispose()};$inputFile.Dispose()}
    }
    $updates[$key]=$copied[$source]
}
# Validate content/paths with the new application before presenting configuration.
$checkEnvironment=@{};foreach($entry in $environment.GetEnumerator()){$checkEnvironment[$entry.Key]=$entry.Value};foreach($entry in $updates.GetEnumerator()){$checkEnvironment[$entry.Key]=$entry.Value}
[void](Invoke-GaoChannelCredentialCheck $PreparedWebPath $checkEnvironment $roots -CheckAcl)
$updatePath=Join-Path $destination 'channel-path-updates.json'
[IO.File]::WriteAllText($updatePath,($updates | ConvertTo-Json -Depth 4))
Set-GaoChannelCredentialOwner $updatePath
Assert-GaoChannelCredentialAcl $destination
return [pscustomobject]@{status='prepared';files=$copied.Count;configurationUpdates=$updatePath}
}
