using GaoApp.Application.Common.Exceptions.Pos;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Display;
using GaoApp.Application.Interfaces.Repositories.Display;
using GaoApp.Application.Interfaces.Services.Display;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Display;

public class DisplayPromotionService : IDisplayPromotionService
{
    private readonly IDisplayPromotionRepository _repository;
    private readonly ICurrentStore _currentStore;

    public DisplayPromotionService(
        IDisplayPromotionRepository repository,
        ICurrentStore currentStore)
    {
        _repository = repository;
        _currentStore = currentStore;
    }

    public async Task<List<DisplayPromotionDto>> GetActiveForCustomerDisplayAsync(
        CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;

        var items = await _repository.GetActiveForCustomerDisplayAsync(
            storeId,
            DateTime.Now,
            ct);

        return items.Select(MapToDto).ToList();
    }

    public async Task<List<DisplayPromotionDto>> GetListAsync(
        CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;

        var items = await _repository.GetListAsync(storeId, ct);

        return items.Select(MapToDto).ToList();
    }

    public async Task<DisplayPromotionDto?> GetByIdAsync(
        int id,
        CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(
            _currentStore.StoreId,
            id,
            ct);

        return entity == null ? null : MapToDto(entity);
    }

    public async Task<int> CreateAsync(
        UpsertDisplayPromotionDto dto,
        CancellationToken ct = default)
    {
        var entity = new DisplayPromotion
        {
            StoreId = _currentStore.StoreId,
            Title = dto.Title.Trim(),
            Description = Normalize(dto.Description),
            MediaType = dto.MediaType.Trim().ToLowerInvariant(),
            MediaUrl = Normalize(dto.MediaUrl),
            ButtonText = Normalize(dto.ButtonText),
            BackgroundColor = Normalize(dto.BackgroundColor),
            TextColor = Normalize(dto.TextColor),
            SortOrder = dto.SortOrder,
            DurationSeconds = dto.DurationSeconds,
            StartAt = dto.StartAt,
            EndAt = dto.EndAt,
            Priority = dto.Priority,
            IsFlashSale = dto.IsFlashSale,
            CountdownToUtc = dto.CountdownToUtc,
            IsFullscreen = dto.IsFullscreen,
            IsActive = dto.IsActive
        };

        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);

        return entity.Id;
    }

    public async Task UpdateAsync(
        int id,
        UpsertDisplayPromotionDto dto,
        CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(
            _currentStore.StoreId,
            id,
            ct);

        if (entity == null)
        {
            throw PosAppException.Business(
                errorCode: "DISPLAY_PROMOTION_NOT_FOUND",
                message: "Không tìm thấy nội dung hiển thị.",
                actionHint: "Vui lòng tải lại danh sách rồi thử lại.");
        }

        entity.Title = dto.Title.Trim();
        entity.Description = Normalize(dto.Description);
        entity.MediaType = dto.MediaType.Trim().ToLowerInvariant();
        entity.MediaUrl = Normalize(dto.MediaUrl);
        entity.ButtonText = Normalize(dto.ButtonText);
        entity.BackgroundColor = Normalize(dto.BackgroundColor);
        entity.TextColor = Normalize(dto.TextColor);
        entity.SortOrder = dto.SortOrder;
        entity.DurationSeconds = dto.DurationSeconds;
        entity.StartAt = dto.StartAt;
        entity.EndAt = dto.EndAt;
        entity.IsActive = dto.IsActive;
        entity.Priority = dto.Priority;
        entity.IsFlashSale = dto.IsFlashSale;
        entity.CountdownToUtc = dto.CountdownToUtc;
        entity.IsFullscreen = dto.IsFullscreen;

        _repository.Update(entity);
        await _repository.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(
        int id,
        CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(
            _currentStore.StoreId,
            id,
            ct);

        if (entity == null)
        {
            throw PosAppException.Business(
                errorCode: "DISPLAY_PROMOTION_NOT_FOUND",
                message: "Không tìm thấy nội dung hiển thị.",
                actionHint: "Vui lòng tải lại danh sách rồi thử lại.");
        }

        _repository.Remove(entity);
        await _repository.SaveChangesAsync(ct);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static DisplayPromotionDto MapToDto(DisplayPromotion x)
    {
        return new DisplayPromotionDto
        {
            Id = x.Id,
            Title = x.Title,
            Description = x.Description,
            MediaType = x.MediaType,
            MediaUrl = x.MediaUrl,
            ButtonText = x.ButtonText,
            BackgroundColor = x.BackgroundColor,
            TextColor = x.TextColor,
            SortOrder = x.SortOrder,
            DurationSeconds = x.DurationSeconds,
            StartAt = x.StartAt,
            EndAt = x.EndAt,
            Priority = x.Priority,
            IsFlashSale = x.IsFlashSale,
            CountdownToUtc = x.CountdownToUtc,
            IsFullscreen = x.IsFullscreen,
            IsActive = x.IsActive
        };
    }
    public async Task SetActiveAsync(
    int id,
    bool isActive,
    CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(
            _currentStore.StoreId,
            id,
            ct);

        if (entity == null)
        {
            throw PosAppException.Business(
                errorCode: "DISPLAY_PROMOTION_NOT_FOUND",
                message: "Không tìm thấy nội dung hiển thị.",
                actionHint: "Vui lòng tải lại danh sách rồi thử lại.");
        }

        entity.IsActive = isActive;

        _repository.Update(entity);
        await _repository.SaveChangesAsync(ct);
    }
}