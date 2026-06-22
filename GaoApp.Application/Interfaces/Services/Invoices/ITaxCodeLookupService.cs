using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface ITaxCodeLookupService
{
    /// <summary>
    /// Hàm cũ giữ lại để không vỡ code cũ.
    /// Nội bộ sẽ gọi LookupBusinessAsync.
    /// </summary>
    Task<Result<TaxCodeLookupResultDto>> LookupAsync(
        string taxCode,
        CancellationToken ct = default);

    /// <summary>
    /// Tra cứu thông tin doanh nghiệp theo MST.
    /// Chỉ dùng làm gợi ý điền thông tin hóa đơn, không phải xác thực pháp lý tuyệt đối.
    /// </summary>
    Task<Result<TaxCodeLookupResultDto>> LookupBusinessAsync(
        string taxCode,
        CancellationToken ct = default);
}