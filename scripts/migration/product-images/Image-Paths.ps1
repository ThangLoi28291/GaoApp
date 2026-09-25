Set-StrictMode -Version Latest

function Resolve-LegacyImagePath {
    param([Parameter(Mandatory=$true)][string]$LegacyImageRoot, [AllowEmptyString()][string]$SourceImage)
    if ($SourceImage -notmatch '^/Data/(images|files)/(.+)$') { throw 'UnsupportedSourcePath' }
    $folder=$Matches[1].ToLowerInvariant()
    $relative=[Uri]::UnescapeDataString($Matches[2])
    if ($relative -match '[\\:\x00-\x1f]' -or $relative.StartsWith('/') -or
        @($relative.Split('/') | Where-Object { [string]::IsNullOrWhiteSpace($_) -or $_ -in @('.','..') -or $_.EndsWith('.') -or $_.EndsWith(' ') }).Count) {
        throw 'UnsafeSourcePath'
    }
    $root=[IO.Path]::GetFullPath($LegacyImageRoot).TrimEnd([char[]]'\/')
    $physical=[IO.Path]::GetFullPath((Join-Path $root ($folder+'/'+$relative)))
    if (-not $physical.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'OutsideImageRoot' }
    $cursor=$physical
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'ReparsePointNotAllowed' }
        $parent=Split-Path -Parent $cursor
        if ($parent -eq $cursor) { break }
        $cursor=$parent
    }
    $types=@{'.jpg'='image/jpeg';'.jpeg'='image/jpeg';'.png'='image/png';'.gif'='image/gif';'.webp'='image/webp'}
    $extension=[IO.Path]::GetExtension($physical).ToLowerInvariant()
    if (-not $types.ContainsKey($extension)) { throw 'UnsupportedImageExtension' }
    $storage='uploads/legacy-data/'+$folder+'/'+$relative
    if ($storage.Length -gt 260 -or [IO.Path]::GetFileName($physical).Length -gt 260) { throw 'PathExceedsSchemaLength' }
    [pscustomobject]@{PhysicalPath=$physical;StoragePath=$storage;ContentType=$types[$extension]}
}
