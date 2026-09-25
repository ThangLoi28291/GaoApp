using GaoApp.Application.Common;
using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Rewards;

public sealed class RewardSettingsRepository
    : IRewardSettingsRepository
{
    private const string StoreUniqueIndexName =
        "IX_RewardSettings_StoreId";

    private readonly AppDbContext _db;

    public RewardSettingsRepository(
        AppDbContext db)
    {
        _db = db;
    }

    public Task<RewardSettings?> GetCurrentAsync(
        CancellationToken ct = default)
    {
        /*
         * D1:
         * Disabled != missing.
         *
         * GetBalance / Redeem vẫn cần đọc policy hiện hành
         * để khách sử dụng số dư đã tích lũy.
         */
        return _db.RewardSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
    }

    public Task<RewardSettings?> GetForAdminAsync(
        int storeId,
        CancellationToken ct = default)
    {
        return _db.RewardSettings
            .FirstOrDefaultAsync(
                x => x.StoreId == storeId,
                ct);
    }

    public Task<List<Category>> GetCategoriesForAdminAsync(int storeId, CancellationToken ct = default)
        => _db.Categories.Where(x => x.StoreId == storeId && !x.IsDeleted)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);

    public Task AddAsync(
        RewardSettings settings,
        CancellationToken ct = default)
    {
        return _db.RewardSettings
            .AddAsync(settings, ct)
            .AsTask();
    }

    public async Task SaveChangesAsync(
        CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(
                "Cấu hình tích điểm đã được người khác thay đổi. Vui lòng tải lại trước khi lưu.",
                ex);
        }
        catch (DbUpdateException ex)
            when (IsStoreUniqueConflict(ex))
        {
            throw new ConcurrencyException(
                "Cửa hàng đã có cấu hình tích điểm. Vui lòng tải lại trước khi lưu.",
                ex);
        }
    }

    private static bool IsStoreUniqueConflict(
        Exception exception)
    {
        var sqlException =
            FindSqlException(exception);

        return sqlException?.Number is 2601 or 2627
            && sqlException.Message.Contains(
                StoreUniqueIndexName,
                StringComparison.Ordinal);
    }

    private static SqlException? FindSqlException(
        Exception exception)
    {
        for (
            Exception? current = exception;
            current is not null;
            current = current.InnerException)
        {
            if (current is SqlException sqlException)
                return sqlException;
        }

        return null;
    }
}
