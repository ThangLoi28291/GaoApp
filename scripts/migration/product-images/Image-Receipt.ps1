# Pure validation of the explicitly recorded primary-image change. Never rewrites the core receipt.
function Test-ImageCatalogueExtension {
    param($Expected,$Current,$Receipt,[string]$SourceDatabase,[string]$ImageSqlHash,
          [string]$OtherVariantColumnsHash,[string]$MediaAssetsHash,[string]$ProductImagesHash)
    if ($Receipt.StoreId -ne 1 -or $Receipt.SourceDatabase -ne $SourceDatabase -or
        $Receipt.SqlSha256 -cne $ImageSqlHash -or $Receipt.PreviewManifestSha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
        $Receipt.ProductVariantBeforeHash -cne $Expected.ProductVariant -or
        $Receipt.ProductVariantAfterHash -cne $Current.ProductVariant -or
        $Receipt.ProductVariantOtherColumnsHash -cne $OtherVariantColumnsHash -or
        $Receipt.MediaAssetsHash -cne $MediaAssetsHash -or $Receipt.ProductImagesHash -cne $ProductImagesHash) { return $false }
    if (@($Expected.PSObject.Properties).Count -ne @($Current.PSObject.Properties).Count) { return $false }
    foreach ($property in $Expected.PSObject.Properties) {
        if ($property.Name -ne 'ProductVariant' -and $Current.($property.Name) -cne $property.Value) { return $false }
    }
    return $true
}
