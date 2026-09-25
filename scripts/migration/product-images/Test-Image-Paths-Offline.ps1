$ErrorActionPreference='Stop'
. (Join-Path (Split-Path -Parent $PSCommandPath) 'Image-Paths.ps1')
$testRoot=Join-Path ([IO.Path]::GetTempPath()) 'gao-image-path-offline'
$checks=0
function Assert-Equal($actual,$expected) {
    if ($actual -cne $expected) { throw "Expected '$expected', got '$actual'." }
    $script:checks++
}
$plain=Resolve-LegacyImagePath $testRoot '/Data/images/test.jpg'
Assert-Equal $plain.StoragePath 'uploads/legacy-data/images/test.jpg'
Assert-Equal $plain.ContentType 'image/jpeg'
$unicode=Resolve-LegacyImagePath $testRoot '/Data/files/t%C3%BAi%20g%E1%BA%A1o.png'
Assert-Equal $unicode.StoragePath ('uploads/legacy-data/files/t'+[char]0xfa+'i g'+[char]0x1ea1+'o.png')
$plus=Resolve-LegacyImagePath $testRoot '/Data/images/A+B.jpg'
Assert-Equal $plus.StoragePath 'uploads/legacy-data/images/A+B.jpg'
$encodedPlus=Resolve-LegacyImagePath $testRoot '/Data/images/A%2BB.jpg'
Assert-Equal $encodedPlus.PhysicalPath $plus.PhysicalPath
$once=Resolve-LegacyImagePath $testRoot '/Data/images/A%2520B.jpg'
Assert-Equal $once.StoragePath 'uploads/legacy-data/images/A%20B.jpg'
$uppercase=Resolve-LegacyImagePath $testRoot '/DATA/IMAGES/A.JPEG'
Assert-Equal $uppercase.ContentType 'image/jpeg'
foreach ($unsafe in @(
    '/Data/images/../secret.jpg',
    '/Data/images/%2e%2e/secret.jpg',
    '/Data/images/a%5c..%5csecret.jpg',
    '/Data/images/C:%5csecret.jpg',
    '/Data/images/a.jpg:stream',
    '/Data/images//server/share.jpg',
    '/Data/images/x/./a.jpg',
    '/Data/images/x./a.jpg',
    '/Data/images/x%20/a.jpg',
    '/Data/images/a%00.jpg',
    '/Data/images/script.svg',
    'https://example.com/Data/images/a.jpg',
    '/Data/private/a.jpg',
    ('/Data/images/'+('a'*270)+'.jpg')
)) {
    $blocked=$false
    try { $null=Resolve-LegacyImagePath $testRoot $unsafe } catch { $blocked=$true }
    if (-not $blocked) { throw "Unsafe path accepted: $unsafe" }
    $checks++
}
$real=Resolve-LegacyImagePath 'C:\GaoAppData\Uploads\legacy-data' '/Data/images/020033.jpg'
if (-not (Test-Path -LiteralPath $real.PhysicalPath -PathType Leaf)) { throw 'Known local sample was not resolved.' }
$checks++
Write-Output "IMAGE_PATH_OFFLINE_PASS: $checks checks. No SQL connection or file writes."
