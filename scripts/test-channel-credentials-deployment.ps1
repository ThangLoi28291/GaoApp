#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$WebPath)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
. (Join-Path $repo 'eng/deployment/Deployment.Common.ps1')
$checks=0
function Check([string]$Name,[scriptblock]$Action){& $Action;$script:checks++;Write-Output "PASS $Name"}
function Reject([scriptblock]$Action,[string]$Message){try{& $Action;throw 'EXPECTED_REJECTION_MISSING'}catch{if($_.Exception.Message -notlike "*$Message*"){throw}}}
$work=Join-Path ([IO.Path]::GetTempPath()) ('gao-c2-deployment-'+[Guid]::NewGuid().ToString('N'))
$environmentKey='CustomerChannels__Connections__49__VerifyTokenFile'
$original=[Environment]::GetEnvironmentVariable($environmentKey)
try{
    $private=Join-Path $work 'private';$project=Join-Path $work 'project';$public=Join-Path $project 'wwwroot'
    [void](New-Item -ItemType Directory -Path $private,$public -Force)
    $token=Join-Path $private 'arbitrary-file.txt';[IO.File]::WriteAllText($token,'SYNTHETIC_C2_TOKEN_ONLY_NEVER_REAL')
    $settings=@{CustomerChannels__Enabled='true';CustomerChannels__CredentialDirectory=$private;CustomerChannels__WorkerEnabled='false';CustomerChannels__Connections__0__Id='facebook-c2';CustomerChannels__Connections__0__StoreId='1';CustomerChannels__Connections__0__Provider='facebook';CustomerChannels__Connections__0__AccountId='12345';CustomerChannels__Connections__0__Enabled='true';CustomerChannels__Connections__0__GraphVersion='v26.0';CustomerChannels__Connections__0__AccessTokenFile=$token;CustomerChannels__Connections__0__AppSecretFile=$token;CustomerChannels__Connections__0__VerifyTokenFile=$token}
    Check 'Real offline CLI accepts private credentials without SQL, host startup or network' {
        $result=Invoke-GaoChannelCredentialCheck $WebPath $settings @($public)
        if($result.status -ne 'verified' -or $result.files -ne 3){throw 'Missing credential verification'}
        if(($result | ConvertTo-Json) -match 'SYNTHETIC|arbitrary-file|gao-c2-deployment'){throw 'Credential metadata leaked'}
    }
    Check 'Another IIS mapping exposing credential storage is rejected' {Reject {Invoke-GaoChannelCredentialCheck $WebPath $settings @($private)} 'preflight rejected'}
    Check 'Global off skips unused credentials and reports no active files for ACL checks' {
        $copy=$settings.Clone();$copy['CustomerChannels__Enabled']='false';$copy['CustomerChannels__CredentialDirectory']='legacy/missing';$copy['CustomerChannels__Connections__0__AccessTokenFile']='wwwroot/missing-token.txt'
        $result=Invoke-GaoChannelCredentialCheck $WebPath $copy @($private)
        if($result.enabled -ne $false -or $result.files -ne 0 -or $result.connections -ne 0){throw 'Disabled channels retained active credential requirements'}
    }
    Check 'Every site application and virtual directory is included in IIS exposure validation' {
        $sites=@([pscustomobject]@{Applications=@([pscustomobject]@{VirtualDirectories=@([pscustomobject]@{PhysicalPath=$public})})},[pscustomobject]@{Applications=@([pscustomobject]@{VirtualDirectories=@([pscustomobject]@{PhysicalPath=$project},[pscustomobject]@{PhysicalPath=$private})})})
        $roots=@(Get-GaoChannelPublicRootsFromSites $sites)
        if($roots.Count -ne 3 -or $roots -notcontains $private){throw 'IIS mapping was omitted'}
        Reject {Invoke-GaoChannelCredentialCheck $WebPath $settings $roots} 'preflight rejected'
    }
    Check 'Upload overlap is rejected independently of webroot' {
        $copy=$settings.Clone();$copy['Storage__UploadRoot']=$private
        Reject {Invoke-GaoChannelCredentialCheck $WebPath $copy @()} 'preflight rejected'
    }
    Check 'Missing enabled credential is rejected before activation' {
        $copy=$settings.Clone();$copy['CustomerChannels__Connections__0__AccessTokenFile']=Join-Path $private 'missing.txt'
        Reject {Invoke-GaoChannelCredentialCheck $WebPath $copy @()} 'preflight rejected'
    }
    Check 'Relative credential path is rejected' {
        $copy=$settings.Clone();$copy['CustomerChannels__Connections__0__AccessTokenFile']='wwwroot/channel-access.txt'
        Reject {Invoke-GaoChannelCredentialCheck $WebPath $copy @()} 'preflight rejected'
    }
    Check 'Disabled channel retains path validation while worker is off' {
        $copy=$settings.Clone();$copy['CustomerChannels__Connections__0__Enabled']='false';$copy['CustomerChannels__CredentialDirectory']=$public
        Reject {Invoke-GaoChannelCredentialCheck $WebPath $copy @($public)} 'preflight rejected'
    }
    $publisher=Join-Path $repo 'eng/deployment/Assert-ChannelCredentialPublish.ps1'
    Check 'Publish inspects disabled channels and arbitrary secret filenames' {
        [IO.File]::WriteAllText((Join-Path $project 'appsettings.json'),(@{CustomerChannels=@{Connections=@(@{Enabled=$false;AccessTokenFile='wwwroot/random-name.txt'})}} | ConvertTo-Json -Depth 5))
        Reject {& $publisher -ProjectDirectory $project} 'publish rejected'
    }
    Check 'Publish checks every environment index including verify tokens' {
        [IO.File]::WriteAllText((Join-Path $project 'appsettings.json'),'{}')
        [Environment]::SetEnvironmentVariable($environmentKey,(Join-Path $public 'innocent-name.txt'))
        Reject {& $publisher -ProjectDirectory $project} 'publish rejected'
        [Environment]::SetEnvironmentVariable($environmentKey,$null)
    }
    Check 'Normalized colon keys cannot bypass preflight or conflict with double-underscore keys' {
        $copy=$settings.Clone();$copy.Remove('CustomerChannels__Connections__0__AccessTokenFile');$copy['CustomerChannels:Connections:0:AccessTokenFile']=Join-Path $public 'random.txt'
        Reject {Invoke-GaoChannelCredentialCheck $WebPath $copy @($public)} 'preflight rejected'
        $copy['CustomerChannels__Connections__0__AccessTokenFile']=$token
        Reject {Invoke-GaoChannelCredentialCheck $WebPath $copy @($public)} 'Conflicting normalized'
    }
    Check 'Publish keeps ordinary public text files and external credential references valid' {
        [IO.File]::WriteAllText((Join-Path $public 'readme.txt'),'ordinary public documentation')
        [IO.File]::WriteAllText((Join-Path $project 'appsettings.json'),(@{CustomerChannels=@{CredentialDirectory=$private;Connections=@(@{AccessTokenFile=$token})}} | ConvertTo-Json -Depth 5))
        & $publisher -ProjectDirectory $project
    }
    Check 'Offline check filters unrelated SQL secrets rather than forwarding configuration' {
        $copy=$settings.Clone();$copy['ConnectionStrings__DefaultConnection']='SYNTHETIC_SQL_SECRET_NEVER_FOR_CHILD';$copy['ProductionBootstrap__Enabled']='true'
        $result=Invoke-GaoChannelCredentialCheck $WebPath $copy @()
        if($result.status -ne 'verified' -or ($result | ConvertTo-Json) -match 'SQL_SECRET'){throw 'Unrelated configuration escaped the allowlist'}
    }
    $poolSid='S-1-5-82-101-202-303-404-505'
    function Descriptor([string]$Mode){
        $acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetOwner([Security.Principal.SecurityIdentifier]::new($(if($Mode -eq 'owner'){'S-1-5-21-101-202-303-1001'}else{'S-1-5-32-544'})))
        foreach($sid in @('S-1-5-18','S-1-5-32-544')){$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid),'FullControl','Allow'))}
        if($Mode -ne 'missing'){$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($poolSid),$(if($Mode -eq 'write'){'Modify'}else{'ReadAndExecute'}),'Allow'))}
        if($Mode -eq 'public'){$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'),'ReadAndExecute','Allow'))}
        if($Mode -eq 'deny'){$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($poolSid),'Read','Deny'))}
        if($Mode -eq 'delete-child'){$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($poolSid),'DeleteSubdirectoriesAndFiles','Allow'))}
        return $acl
    }
    Check 'ACL descriptor grants read-only pool access and rejects broad readers, writers, unsafe owners and denied access' {
        Assert-GaoChannelCredentialDescriptor (Descriptor 'valid') $poolSid
        foreach($mode in @('public','write','owner','deny','delete-child')){Reject {Assert-GaoChannelCredentialDescriptor (Descriptor $mode) $poolSid} 'Unsafe channel credential ACL'}
        Reject {Assert-GaoChannelCredentialDescriptor (Descriptor 'missing') $poolSid} 'does not grant'
    }
    Check 'Parent ACL cannot let another identity replace or delete the protected credential directory' {
        Assert-GaoChannelCredentialParentDescriptor (Descriptor 'public') 'S-1-5-80-1'
        foreach($mode in @('delete-child','write','owner')){Reject {Assert-GaoChannelCredentialParentDescriptor (Descriptor $mode) 'S-1-5-80-1'} 'Unsafe credential ancestor ACL'}
    }
    # Only applying/reading host-specific ACLs is mocked below. Policy above is real.
    # Exercise real bounded
    # byte copy, path-update generation, source preservation and application CLI.
    $script:aclEvents=[Collections.Generic.List[string]]::new()
    function Set-GaoChannelCredentialDirectoryAcl([string]$Directory){if(@(Get-ChildItem -LiteralPath $Directory).Count){throw 'Files were copied before the directory was secured'};$script:aclEvents.Add('SecureDirectory')}
    function Assert-GaoChannelCredentialParents([string]$Directory){$script:aclEvents.Add('VerifyParents')}
    function Set-GaoChannelCredentialOwner([string]$Path){$script:aclEvents.Add('SetOwner')}
    function Assert-GaoChannelCredentialAcl([string]$Directory){$script:aclEvents.Add('VerifyAcl')}
    $legacy=Join-Path $public 'legacy-token.txt';$legacyText='SYNTHETIC_LEGACY_CHANNEL_TOKEN_ONLY'
    [IO.File]::WriteAllText($legacy,$legacyText,[Text.UTF8Encoding]::new($true))
    $legacyBytes=[IO.File]::ReadAllBytes($legacy);$legacySettings=$settings.Clone()
    foreach($key in @('AccessTokenFile','AppSecretFile','VerifyTokenFile')){$legacySettings['CustomerChannels__Connections__0__'+$key]='wwwroot/legacy-token.txt'}
    $destination=Join-Path $work 'migrated-private'
    Check 'Legacy relative paths copy byte-for-byte without modifying source or connection identity' {
        $script:prepared=New-GaoChannelCredentialCopy $legacySettings $project $WebPath $destination @($public)
        if($prepared.status -ne 'prepared' -or $prepared.files -ne 1){throw 'Preparation did not preserve shared credential mapping'}
        if([Convert]::ToHexString($legacyBytes) -cne [Convert]::ToHexString([IO.File]::ReadAllBytes($legacy))){throw 'Legacy source changed'}
        $updates=Get-Content -LiteralPath $prepared.configurationUpdates -Raw | ConvertFrom-Json -AsHashtable
        if($updates.Count -ne 4 -or ($updates | ConvertTo-Json) -match $legacyText){throw 'Updates contain raw secrets or unrelated account settings'}
        $newFile=$updates['CustomerChannels__Connections__0__AccessTokenFile']
        if([Convert]::ToHexString($legacyBytes) -cne [Convert]::ToHexString([IO.File]::ReadAllBytes($newFile))){throw 'Credential bytes were changed'}
        if($aclEvents[0] -ne 'VerifyParents' -or $aclEvents[1] -ne 'SecureDirectory' -or $aclEvents[-1] -ne 'VerifyAcl'){throw 'Wrong ACL verification order'}
    }
    Check 'Preparation refuses existing destination and leaves prepared bytes unchanged' {
        Reject {New-GaoChannelCredentialCopy $legacySettings $project $WebPath $destination @($public)} 'NEW absolute private'
        if([Convert]::ToHexString($legacyBytes) -cne [Convert]::ToHexString([IO.File]::ReadAllBytes((Join-Path $destination 'credential-1.txt')))){throw 'Prepared file overwritten'}
    }
    Check 'Preparation blocks a destination inside public mappings before creating it' {
        $bad=Join-Path $public 'new-secret-directory'
        Reject {New-GaoChannelCredentialCopy $legacySettings $project $WebPath $bad @($public)} 'overlaps'
        if(Test-Path -LiteralPath $bad){throw 'Unsafe destination was created'}
    }
    Check 'Preparation rejects UNC alternate separators ADS and traversal before touching the destination' {
        foreach($bad in @('//127.0.0.1/unreachable/private',($destination+':stream'),(Join-Path $work 'part/../invalid-destination'))){
            Reject {New-GaoChannelCredentialCopy $legacySettings $project $WebPath $bad @($public)} 'NEW absolute private'
        }
    }
    Check 'Missing legacy source aborts without creating a destination' {
        $missing=$legacySettings.Clone();$missing['CustomerChannels__Connections__0__AccessTokenFile']='wwwroot/absent.txt'
        $bad=Join-Path $work 'missing-source-destination'
        Reject {New-GaoChannelCredentialCopy $missing $project $WebPath $bad @($public)} 'source credential file'
        if(Test-Path -LiteralPath $bad){throw 'Incomplete migration destination created'}
    }
    Write-Output "RESULT: $checks/$checks PASS (synthetic credentials; real copy/offline CLI; ACL boundary mocked; no IIS changes or external requests)."
}finally{
    [Environment]::SetEnvironmentVariable($environmentKey,$original)
    $full=[IO.Path]::GetFullPath($work);$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if(!$full.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($full) -notmatch '^gao-c2-deployment-[a-f0-9]{32}$'){throw 'Unsafe test cleanup path'}
    if(Test-Path -LiteralPath $full){Remove-Item -LiteralPath $full -Recurse -Force}
}
