using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Purchases;

/// <summary>
/// Liên kết một dòng hàng mô tả trên PO với danh mục thật trước khi nhập kho.
/// Snapshot tên/đơn vị ban đầu của PO không bị thay đổi.
/// </summary>
public sealed class ResolvePurchaseOrderLineRequest
{
    [Range(1, int.MaxValue)]
    public int ProductVariantId { get; set; }

    [Range(1, int.MaxValue)]
    public int ProductUnitConversionId { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Note { get; set; }
}

/// <summary>
/// Dữ liệu tối thiểu để tạo nhanh một sản phẩm chờ hoàn thiện danh mục.
/// Sản phẩm được tạo active cho mua/nhập kho nhưng IsSellable=false.
/// </summary>
public sealed class QuickCreateProcurementProductRequest
{
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }

    [Range(1, int.MaxValue)]
    public int SupplierId { get; set; }

    public int? UnitId { get; set; }

    [StringLength(200)]
    public string? NewUnitName { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool GenerateDefaultBarcode { get; set; } = true;
}

public sealed class QuickCreateAndResolvePurchaseOrderLineRequest
{
    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(500)]
    public string? ResolutionNote { get; set; }

    [Required]
    public QuickCreateProcurementProductRequest Product { get; set; } = new();
}

public sealed class ProcurementCatalogOptionDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Code { get; set; }
}

public sealed class ProcurementQuickCreateOptionsDto
{
    public List<ProcurementCatalogOptionDto> Categories { get; set; } = new();
    public List<ProcurementCatalogOptionDto> Units { get; set; } = new();
}

public sealed class ProcurementCreatedProductDto
{
    public int ProductId { get; set; }
    public int ProductVariantId { get; set; }
    public int ProductUnitConversionId { get; set; }
    public int UnitId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public decimal Factor { get; set; } = 1m;
    public bool IsSellable { get; set; }
}
