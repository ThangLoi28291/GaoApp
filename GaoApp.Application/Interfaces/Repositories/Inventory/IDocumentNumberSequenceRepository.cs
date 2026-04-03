using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

/// <summary>
/// Repository cấp phát sequence cho số chứng từ.
/// </summary>
public interface IDocumentNumberSequenceRepository
{
    /// <summary>
    /// Tăng sequence và trả về số tiếp theo cho:
    /// store + sequence type + date.
    /// </summary>
    Task<int> GetNextNumberAsync(
        int storeId,
        DocumentNumberSequenceType sequenceType,
        DateTime sequenceDate,
        CancellationToken ct = default);
}