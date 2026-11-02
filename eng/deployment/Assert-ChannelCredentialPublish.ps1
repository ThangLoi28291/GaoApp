#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ProjectDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($ProjectDirectory).TrimEnd('\','/')
function Assert-ExternalCredential([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    try {
        $full=[IO.Path]::GetFullPath($Path,$root)
        if ($full.Equals($root,[StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $root.StartsWith($full.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
            ($IsWindows -and ($full.StartsWith('\\') -or $full.Contains('~')))) { throw 'unsafe' }
        $item=if([IO.Directory]::Exists($full)){[IO.DirectoryInfo]::new($full)}else{[IO.FileInfo]::new($full)}
        while($item){
            if(($item.Exists -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) -or $null -ne $item.LinkTarget){throw 'unsafe'}
            $item=if($item -is [IO.FileInfo]){$item.Directory}else{$item.Parent}
        }
    } catch { throw 'CustomerChannels credential files/directory must be outside the project; publish rejected.' }
}
# Check every configured connection, including disabled channels and higher indexes.
# Read path metadata only. Never read or print credential contents.
foreach($file in Get-ChildItem -LiteralPath $root -Filter 'appsettings*.json' -File) {
    try { $config=Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -AsHashtable } catch { throw 'Cannot verify channel paths in application configuration.' }
    $channels=$config['CustomerChannels']
    if($null -eq $channels){continue}
    Assert-ExternalCredential $channels['CredentialDirectory']
    foreach($connection in $channels['Connections']) {
        foreach($key in @('AccessTokenFile','AppSecretFile','VerifyTokenFile')){Assert-ExternalCredential $connection[$key]}
    }
}
foreach($entry in Get-ChildItem Env: | Where-Object {$_.Name.Replace(':','__') -match '(?i)^CustomerChannels__(CredentialDirectory|Connections__\d+__(AccessTokenFile|AppSecretFile|VerifyTokenFile))$'}) {
    Assert-ExternalCredential $entry.Value
}
