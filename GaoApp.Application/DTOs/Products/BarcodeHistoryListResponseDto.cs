using GaoApp.Application.Common;

namespace GaoApp.Application.DTOs.Products;

/// <summary>
/// Response trả về cho ajax load lịch sử barcode.
/// </summary>
public sealed class BarcodeHistoryListResponseDto
{
    public PagedResult<BarcodeHistoryRowDto> Data { get; set; } = new();

    public bool HasMore => Data.Page < Data.TotalPages;
}