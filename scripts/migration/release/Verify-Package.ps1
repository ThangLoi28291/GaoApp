[CmdletBinding()]
param([string]$PackageRoot)
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($PackageRoot)){$PackageRoot=Split-Path -Parent $PSCommandPath}
$root=[IO.Path]::GetFullPath($PackageRoot).TrimEnd([char[]]'\/')
$manifest=Get-Content -LiteralPath (Join-Path $root 'checksums.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$count=0
foreach($file in $manifest.Files){
 $path=[IO.Path]::GetFullPath((Join-Path $root $file.Path))
 if(-not $path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid package manifest path.'}
 if(-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $file.Sha256){throw "Package file changed or missing: $($file.Path)"}
 $count++
}
Write-Output "PACKAGE_FILES_PASS: $count files. config.json and runs/ are intentionally not part of the immutable payload."
