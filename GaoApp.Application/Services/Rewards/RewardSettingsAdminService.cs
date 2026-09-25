using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Rewards;
using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Application.Interfaces.Services.Rewards;
using GaoApp.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace GaoApp.Application.Services.Rewards;

public sealed class RewardSettingsAdminService
    : IRewardSettingsAdminService
{
    private const decimal MaximumMoneyValue =
        1_000_000_000m;

    private const int MaximumPointsPerVoucher =
        1_000_000;

    private const int MaximumNoteLength =
        500;

    private static readonly Error InvalidStore =
        new(
            "RewardSettings.InvalidStore",
            "Cửa hàng hiện tại không hợp lệ.");

    private static readonly Error ConfirmationRequired =
        new(
            "RewardSettings.RateChangeConfirmationRequired",
            "Thay đổi tỷ lệ tích điểm/voucher cần được xác nhận trước khi lưu.");

    private static readonly Error ConcurrencyConflict =
        new(
            "RewardSettings.ConcurrencyConflict",
            "Cấu hình đã được người khác thay đổi. Vui lòng tải lại trước khi lưu.");

    private static readonly Error SaveFailed =
        new(
            "RewardSettings.SaveFailed",
            "Không thể lưu cấu hình tích điểm. Vui lòng thử lại.");

    private readonly IRewardSettingsRepository _repository;
    private readonly ILogger<RewardSettingsAdminService> _logger;

    public RewardSettingsAdminService(
        IRewardSettingsRepository repository,
        ILogger<RewardSettingsAdminService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<RewardSettingsAdminDto> GetAsync(
        int storeId,
        CancellationToken ct = default)
    {
        if (storeId <= 0)
            throw new InvalidOperationException(
                InvalidStore.Message);

        var entity =
            await _repository.GetForAdminAsync(
                storeId,
                ct);

        var categories = await _repository.GetCategoriesForAdminAsync(storeId, ct);
        if (entity is null)
        {
            return new RewardSettingsAdminDto
            {
                Exists = false,
                IsEnabled = true,
                Categories = MapCategories(categories),
                CategorySelectionVersion = CategoryVersion(categories)
            };
        }

        return Map(entity, categories);
    }

    public async Task<Result<RewardSettingsAdminDto>> SaveAsync(
        int storeId,
        SaveRewardSettingsRequest request,
        CancellationToken ct = default)
    {
        if (storeId <= 0)
        {
            return Result<RewardSettingsAdminDto>
                .Failure(InvalidStore);
        }

        if (request is null)
        {
            return Result<RewardSettingsAdminDto>
                .Failure(
                    Error.Validation(
                        "Dữ liệu cấu hình không hợp lệ."));
        }

        request.Note =
            CleanNote(request.Note);

        var validationErrors =
            Validate(request);

        if (validationErrors.Count > 0)
        {
            return Result<RewardSettingsAdminDto>
                .ValidationFailure(
                    validationErrors);
        }

        var entity =
            await _repository.GetForAdminAsync(
                storeId,
                ct);

        var categories = await _repository.GetCategoriesForAdminAsync(storeId, ct);
        var excludedIds = (request.ExcludedCategoryIds ?? new()).ToHashSet();
        if (request.UpdateCategoryExclusions)
        {
            if (request.CategorySelectionVersion != CategoryVersion(categories))
                return Result<RewardSettingsAdminDto>.Failure(ConcurrencyConflict);
            if (excludedIds.Except(categories.Select(x => x.Id)).Any())
                return Result<RewardSettingsAdminDto>.Failure(Error.Validation("Ngành hàng loại trừ không hợp lệ hoặc không thuộc cửa hàng hiện tại."));
        }

        if (entity is null)
        {
            entity = new RewardSettings
            {
                StoreId = storeId,
                MoneyPerPoint = request.MoneyPerPoint,
                PointsPerVoucher = request.PointsPerVoucher,
                VoucherValue = request.VoucherValue,
                IsEnabled = request.IsEnabled,
                Note = request.Note
            };

            try
            {
                await _repository.AddAsync(
                    entity,
                    ct);

                ApplyCategoryExclusions(categories, request.UpdateCategoryExclusions, excludedIds);

                await _repository.SaveChangesAsync(
                    ct);

                return Result<RewardSettingsAdminDto>
                    .Success(
                        Map(entity, categories));
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (ConcurrencyException ex)
            {
                _logger.LogWarning(
                    ex,
                    "RewardSettings create conflict. StoreId={StoreId}",
                    storeId);

                return Result<RewardSettingsAdminDto>
                    .Failure(
                        ConcurrencyConflict);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Creating RewardSettings failed. StoreId={StoreId}",
                    storeId);

                return Result<RewardSettingsAdminDto>
                    .Failure(
                        SaveFailed);
            }
        }

        if (!HasMatchingRowVersion(
                entity,
                request.RowVersion))
        {
            return Result<RewardSettingsAdminDto>
                .Failure(
                    ConcurrencyConflict);
        }

        var rateChanged =
            entity.MoneyPerPoint != request.MoneyPerPoint
            || entity.PointsPerVoucher != request.PointsPerVoucher
            || entity.VoucherValue != request.VoucherValue;

        if (
            rateChanged
            && !request.ConfirmRateChange)
        {
            return Result<RewardSettingsAdminDto>
                .Failure(
                    ConfirmationRequired);
        }

        entity.MoneyPerPoint =
            request.MoneyPerPoint;

        entity.PointsPerVoucher =
            request.PointsPerVoucher;

        entity.VoucherValue =
            request.VoucherValue;

        entity.IsEnabled =
            request.IsEnabled;

        entity.Note =
            request.Note;

        try
        {
            ApplyCategoryExclusions(categories, request.UpdateCategoryExclusions, excludedIds);
            await _repository.SaveChangesAsync(
                ct);

            return Result<RewardSettingsAdminDto>
                .Success(
                        Map(entity, categories));
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ConcurrencyException ex)
        {
            _logger.LogWarning(
                ex,
                "RewardSettings update conflict. StoreId={StoreId}; SettingsId={SettingsId}",
                storeId,
                entity.Id);

            return Result<RewardSettingsAdminDto>
                .Failure(
                    ConcurrencyConflict);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Updating RewardSettings failed. StoreId={StoreId}; SettingsId={SettingsId}",
                storeId,
                entity.Id);

            return Result<RewardSettingsAdminDto>
                .Failure(
                    SaveFailed);
        }
    }

    private static List<ValidationError> Validate(
        SaveRewardSettingsRequest request)
    {
        var errors =
            new List<ValidationError>();

        if (
            request.MoneyPerPoint <= 0
            || request.MoneyPerPoint > MaximumMoneyValue)
        {
            errors.Add(
                new ValidationError(
                    nameof(request.MoneyPerPoint),
                    "Tiền để nhận 1 điểm phải lớn hơn 0 và không vượt quá 1.000.000.000đ."));
        }

        if (
            request.PointsPerVoucher <= 0
            || request.PointsPerVoucher > MaximumPointsPerVoucher)
        {
            errors.Add(
                new ValidationError(
                    nameof(request.PointsPerVoucher),
                    "Số điểm đổi 1 voucher phải lớn hơn 0 và không vượt quá 1.000.000."));
        }

        if (
            request.VoucherValue <= 0
            || request.VoucherValue > MaximumMoneyValue)
        {
            errors.Add(
                new ValidationError(
                    nameof(request.VoucherValue),
                    "Giá trị voucher phải lớn hơn 0 và không vượt quá 1.000.000.000đ."));
        }

        if (
            request.Note is { Length: > MaximumNoteLength })
        {
            errors.Add(
                new ValidationError(
                    nameof(request.Note),
                    "Ghi chú không được vượt quá 500 ký tự."));
        }

        return errors;
    }

    private static bool HasMatchingRowVersion(
        RewardSettings entity,
        string? postedRowVersion)
    {
        if (
            entity.RowVersion is not { Length: > 0 }
            || string.IsNullOrWhiteSpace(
                postedRowVersion))
        {
            return false;
        }

        try
        {
            var expected =
                Convert.FromBase64String(
                    postedRowVersion);

            return entity.RowVersion
                .SequenceEqual(expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static RewardSettingsAdminDto Map(
        RewardSettings entity, List<Category> categories)
    {
        return new RewardSettingsAdminDto
        {
            Categories = MapCategories(categories),
            CategorySelectionVersion = CategoryVersion(categories),
            Exists = true,
            Id = entity.Id,
            MoneyPerPoint = entity.MoneyPerPoint,
            PointsPerVoucher = entity.PointsPerVoucher,
            VoucherValue = entity.VoucherValue,
            IsEnabled = entity.IsEnabled,
            Note = entity.Note,

            RowVersion =
                entity.RowVersion is { Length: > 0 }
                    ? Convert.ToBase64String(
                        entity.RowVersion)
                    : null
        };
    }

    private static string? CleanNote(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    private static void ApplyCategoryExclusions(List<Category> categories, bool update, HashSet<int> excludedIds)
    {
        if (!update) return;
        foreach (var category in categories)
            category.IsRewardEligible = !excludedIds.Contains(category.Id);
    }

    private static string CategoryVersion(List<Category> categories)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", categories
            .OrderBy(x => x.Id).Select(x => $"{x.Id}:{Convert.ToBase64String(x.RowVersion ?? Array.Empty<byte>())}:{x.IsRewardEligible}:{x.ParentId}")))));

    private static List<RewardCategoryOptionDto> MapCategories(List<Category> categories)
    {
        var byId = categories.ToDictionary(x => x.Id);
        return categories.Select(category =>
        {
            var names = new List<string> { category.Name };
            var visited = new HashSet<int> { category.Id };
            var parentId = category.ParentId;
            while (parentId.HasValue && byId.TryGetValue(parentId.Value, out var parent) && visited.Add(parent.Id))
            {
                names.Insert(0, parent.Name);
                parentId = parent.ParentId;
            }
            return new RewardCategoryOptionDto { Id = category.Id, Name = string.Join(" / ", names), IsExcluded = !category.IsRewardEligible };
        }).OrderBy(x => x.Name).ThenBy(x => x.Id).ToList();
    }
}
