using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

/// <summary>
/// Repository cấp số chứng từ.
/// 
/// Giai đoạn hiện tại:
/// - dùng transaction + unique key + retry nhẹ ở tầng repository
/// - đủ tốt cho môi trường vận hành vừa và nhỏ
/// </summary>
public class DocumentNumberSequenceRepository : IDocumentNumberSequenceRepository
{
    private readonly AppDbContext _db;

    public DocumentNumberSequenceRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetNextNumberAsync(
        int storeId,
        DocumentNumberSequenceType sequenceType,
        DateTime sequenceDate,
        CancellationToken ct = default)
    {
        var date = sequenceDate.Date;

        // Retry nhẹ nếu đụng race condition lúc insert dòng đầu tiên trong ngày
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var entity = await _db.DocumentNumberSequences
                .FirstOrDefaultAsync(x =>
                    !x.IsDeleted
                    && x.StoreId == storeId
                    && x.SequenceType == sequenceType
                    && x.SequenceDate == date, ct);

            if (entity == null)
            {
                entity = new DocumentNumberSequence
                {
                    StoreId = storeId,
                    SequenceType = sequenceType,
                    SequenceDate = date,
                    LastNumber = 1
                };

                _db.DocumentNumberSequences.Add(entity);

                try
                {
                    await _db.SaveChangesAsync(ct);
                    return entity.LastNumber;
                }
                catch (DbUpdateException)
                {
                    // Có thể bị race: 2 request cùng thấy null và cùng insert.
                    // Request thua sẽ retry, lần sau sẽ đọc được dòng đã tồn tại.
                    _db.Entry(entity).State = EntityState.Detached;

                    if (attempt == maxAttempts)
                        throw;
                }
            }
            else
            {
                entity.LastNumber += 1;

                try
                {
                    await _db.SaveChangesAsync(ct);
                    return entity.LastNumber;
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Trường hợp có concurrency token trong tương lai
                    _db.Entry(entity).Reload();

                    if (attempt == maxAttempts)
                        throw;
                }
            }
        }

        throw new InvalidOperationException("Không thể cấp số chứng từ.");
    }
}