using GaoApp.Application.Interfaces.Common;

namespace GaoApp.Infrastructure.Data;

/// <summary>
/// Implementation cụ thể của IAppUnitOfWork.
/// 
/// Nhiệm vụ:
/// - dùng AppDbContext để mở transaction DB
/// - bọc transaction EF Core thành IAppTransaction
/// 
/// Lưu ý:
/// - không chứa business logic
/// - chỉ là lớp hạ tầng để hỗ trợ transaction cho Application
/// </summary>
public sealed class AppUnitOfWork : IAppUnitOfWork
{
    private readonly AppDbContext _db;

    public AppUnitOfWork(AppDbContext db)
    {
        _db = db;
    }
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
    /// <summary>
    /// Mở transaction mới từ AppDbContext.
    /// </summary>
    public async Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        var tx = await _db.Database.BeginTransactionAsync(ct);
        return new AppTransaction(tx);
    }
}