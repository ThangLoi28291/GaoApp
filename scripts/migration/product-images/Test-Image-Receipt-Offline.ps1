$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path (Split-Path -Parent $PSCommandPath) 'Image-Receipt.ps1')
$expected=[pscustomobject]@{Products='products-before';ProductVariant='variant-before';Unit='units-before'}
$current=[pscustomobject]@{Products='products-before';ProductVariant='variant-after';Unit='units-before'}
$receipt=[pscustomobject]@{
 StoreId=1;SourceDatabase='DataGaoStore';SqlSha256=('B'*64);PreviewManifestSha256=('A'*64)
 ProductVariantBeforeHash='variant-before';ProductVariantAfterHash='variant-after'
 ProductVariantOtherColumnsHash='other-columns';MediaAssetsHash='media';ProductImagesHash='images'
}
$arguments=@{Expected=$expected;Current=$current;Receipt=$receipt;SourceDatabase='DataGaoStore';ImageSqlHash=('B'*64);OtherVariantColumnsHash='other-columns';MediaAssetsHash='media';ProductImagesHash='images'}
if (-not (Test-ImageCatalogueExtension @arguments)) { throw 'Reviewed image change rejected.' }
$count=1
foreach($field in @('ProductVariantBeforeHash','ProductVariantAfterHash','ProductVariantOtherColumnsHash','MediaAssetsHash','ProductImagesHash','SourceDatabase','SqlSha256','PreviewManifestSha256')) {
 $saved=$receipt.$field; $receipt.$field='changed'
 if (Test-ImageCatalogueExtension @arguments) { throw "Changed receipt accepted: $field" }
 $receipt.$field=$saved; $count++
}
foreach($field in @('Products','ProductVariant','Unit')) {
 $saved=$current.$field; $current.$field='changed'
 if (Test-ImageCatalogueExtension @arguments) { throw "Changed catalogue accepted: $field" }
 $current.$field=$saved; $count++
}
foreach($field in @('OtherVariantColumnsHash','MediaAssetsHash','ProductImagesHash')) {
 $saved=$arguments[$field]; $arguments[$field]='changed'
 if (Test-ImageCatalogueExtension @arguments) { throw "Changed live state accepted: $field" }
 $arguments[$field]=$saved; $count++
}
Write-Output "IMAGE_RECEIPT_OFFLINE_PASS: $count checks. No SQL connection."
