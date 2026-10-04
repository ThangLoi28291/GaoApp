namespace GaoApp.Application.DTOs.Products;

public sealed record ProductListFilters(int? SupplierId = null, int? BrandId = null, int? BaseUnitId = null, string? DataIssue = null);
public sealed record ProductFilterOptionDto(int Id, string Name, string Code, bool IsActive);
