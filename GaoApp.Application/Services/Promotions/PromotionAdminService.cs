using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Promotions;
using GaoApp.Application.Features.Promotions;
using GaoApp.Application.Interfaces.Repositories.Promotions;
using GaoApp.Application.Interfaces.Services.Promotions;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Promotions;

public sealed class PromotionAdminService : IPromotionAdminService
{
    private readonly IPromotionRepository _repo;

    public PromotionAdminService(IPromotionRepository repo)
    {
        _repo = repo;
    }

    public async Task<PagedResult<PromotionListItemDto>> GetPagedAsync(
        int storeId,
        PromotionType? type,
        bool? isActive,
        string? customerPriceTier,
        string? keyword,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(
            storeId,
            type,
            isActive,
            NormalizeTierOrNull(customerPriceTier),
            keyword,
            fromUtc,
            toUtc,
            page,
            pageSize,
            ct);

        return new PagedResult<PromotionListItemDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            Items = items.Select(ToListItemDto).ToList()
        };
    }

    public async Task<Result<PromotionEditDto>> GetForEditAsync(
        int storeId,
        int id,
        CancellationToken ct = default)
    {
        if (id <= 0)
            return Result.Failure<PromotionEditDto>(PromotionErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure<PromotionEditDto>(PromotionErrors.NotFound);

        return Result.Success(ToEditDto(entity));
    }

    public async Task<Result<int>> CreateAsync(
        int storeId,
        SavePromotionRequest request,
        int? userId,
        CancellationToken ct = default)
    {
        Normalize(request);

        var valid = ValidateRequest(request);
        if (!valid.IsSuccess)
            return Result.Failure<int>(valid.Error);

        if (await _repo.ExistsNameAsync(storeId, request.Name, null, ct))
            return Result.Failure<int>(PromotionErrors.DuplicateName);

        var entity = new Promotion
        {
            StoreId = storeId,
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            DiscountType = request.DiscountType,
            DiscountValue = request.DiscountValue,
            ComboFixedPrice = request.ComboFixedPrice,
            ComboNote = request.ComboNote,
            BuyQuantity = request.BuyQuantity,
            GetQuantity = request.GetQuantity,
            RequireGiftQuantityInCart = request.RequireGiftQuantityInCart,
            StartAtUtc = request.StartAtUtc,
            EndAtUtc = request.EndAtUtc,
            IsActive = request.IsActive,
            Priority = request.Priority,
            CustomerPriceTier = NormalizeTierOrNull(request.CustomerPriceTier),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = userId
        };

        ReplaceChildren(entity, request, storeId, userId);

        await _repo.AddAsync(entity, ct);
        await _repo.SaveChangesAsync(ct);

        return Result.Success(entity.Id);
    }

    public async Task<Result> UpdateAsync(
        int storeId,
        SavePromotionRequest request,
        int? userId,
        CancellationToken ct = default)
    {
        if (request.Id <= 0)
            return Result.Failure(PromotionErrors.InvalidId);

        Normalize(request);

        var valid = ValidateRequest(request);
        if (!valid.IsSuccess)
            return Result.Failure(valid.Error);

        var entity = await _repo.GetByIdForUpdateAsync(storeId, request.Id, ct);
        if (entity == null)
            return Result.Failure(PromotionErrors.NotFound);

        if (await _repo.ExistsNameAsync(storeId, request.Name, request.Id, ct))
            return Result.Failure(PromotionErrors.DuplicateName);

        if (request.RowVersion != null)
            entity.RowVersion = request.RowVersion;

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.Type = request.Type;
        entity.DiscountType = request.DiscountType;
        entity.DiscountValue = request.DiscountValue;
        entity.ComboFixedPrice = request.ComboFixedPrice;
        entity.ComboNote = request.ComboNote;
        entity.BuyQuantity = request.BuyQuantity;
        entity.GetQuantity = request.GetQuantity;
        entity.RequireGiftQuantityInCart = request.RequireGiftQuantityInCart;
        entity.StartAtUtc = request.StartAtUtc;
        entity.EndAtUtc = request.EndAtUtc;
        entity.IsActive = request.IsActive;
        entity.Priority = request.Priority;
        entity.CustomerPriceTier = NormalizeTierOrNull(request.CustomerPriceTier);
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = userId;

        ReplaceChildren(entity, request, storeId, userId);

        try
        {
            await _repo.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (ConcurrencyException)
        {
            return Result.Failure(PromotionErrors.ConcurrencyConflict);
        }
    }

    public async Task<Result<bool>> ToggleStatusAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdForUpdateAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure<bool>(PromotionErrors.NotFound);

        entity.IsActive = !entity.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = userId;

        await _repo.SaveChangesAsync(ct);
        return Result.Success(entity.IsActive);
    }

    public async Task<Result> SoftDeleteAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdForUpdateAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure(PromotionErrors.NotFound);

        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedBy = userId;

        foreach (var item in entity.Items.Where(x => !x.IsDeleted))
        {
            item.IsDeleted = true;
            item.DeletedAtUtc = DateTime.UtcNow;
            item.DeletedBy = userId;
        }

        foreach (var rule in entity.ComboRules.Where(x => !x.IsDeleted))
        {
            rule.IsDeleted = true;
            rule.DeletedAtUtc = DateTime.UtcNow;
            rule.DeletedBy = userId;
        }

        await _repo.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<int>> DuplicateAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default)
    {
        var source = await _repo.GetByIdAsync(storeId, id, ct);
        if (source == null)
            return Result.Failure<int>(PromotionErrors.NotFound);

        var copyName = $"{source.Name} - Copy";

        var request = ToSaveRequest(source);
        request.Id = 0;
        request.Name = copyName;
        request.IsActive = false;
        request.RowVersion = null;

        return await CreateAsync(storeId, request, userId, ct);
    }

    private static Result ValidateRequest(SavePromotionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure(PromotionErrors.InvalidInput);

        if (request.EndAtUtc <= request.StartAtUtc)
            return Result.Failure(PromotionErrors.InvalidDate);

        if (request.Type == PromotionType.ProductDiscount)
        {
            if (request.DiscountValue <= 0)
                return Result.Failure(PromotionErrors.InvalidInput);

            if (!request.Items.Any())
                return Result.Failure(PromotionErrors.ProductItemRequired);
        }

        if (request.Type == PromotionType.BuyXGetY)
        {
            if ((request.BuyQuantity ?? 0) <= 0 || (request.GetQuantity ?? 0) <= 0)
                return Result.Failure(PromotionErrors.InvalidBuyGet);

            if (!request.Items.Any())
                return Result.Failure(PromotionErrors.ProductItemRequired);
        }

        if (request.Type == PromotionType.ComboFixedPrice)
        {
            if ((request.ComboFixedPrice ?? 0) <= 0)
                return Result.Failure(PromotionErrors.InvalidComboPrice);

            if (request.ComboRules.Count < 2)
                return Result.Failure(PromotionErrors.ComboRuleRequired);
        }

        return Result.Success();
    }

    private static void ReplaceChildren(
        Promotion entity,
        SavePromotionRequest request,
        int storeId,
        int? userId)
    {
        var now = DateTime.UtcNow;

        foreach (var item in entity.Items.Where(x => !x.IsDeleted))
        {
            item.IsDeleted = true;
            item.DeletedAtUtc = now;
            item.DeletedBy = userId;
        }

        foreach (var rule in entity.ComboRules.Where(x => !x.IsDeleted))
        {
            rule.IsDeleted = true;
            rule.DeletedAtUtc = now;
            rule.DeletedBy = userId;
        }

        if (request.Type == PromotionType.ProductDiscount ||
            request.Type == PromotionType.BuyXGetY)
        {
            foreach (var item in request.Items.Where(x => x.ProductId > 0))
            {
                entity.Items.Add(new PromotionItem
                {
                    StoreId = storeId,
                    ProductId = item.ProductId,
                    VariantId = item.VariantId,
                    ProductUnitConversionId = item.ProductUnitConversionId,
                    MinQuantity = item.MinQuantity <= 0 ? 1m : item.MinQuantity,
                    CreatedAtUtc = now,
                    CreatedBy = userId
                });
            }
        }

        if (request.Type == PromotionType.ComboFixedPrice)
        {
            foreach (var rule in request.ComboRules.Where(x => x.ProductId > 0))
            {
                entity.ComboRules.Add(new PromotionComboRule
                {
                    StoreId = storeId,
                    ProductId = rule.ProductId,
                    VariantId = rule.VariantId,
                    ProductUnitConversionId = rule.ProductUnitConversionId,
                    RequiredQuantity = rule.RequiredQuantity <= 0 ? 1m : rule.RequiredQuantity,
                    CreatedAtUtc = now,
                    CreatedBy = userId
                });
            }
        }
    }

    private static PromotionListItemDto ToListItemDto(Promotion x)
    {
        var now = DateTime.UtcNow;

        var statusText = !x.IsActive
            ? "Đã khóa"
            : now < x.StartAtUtc
                ? "Sắp chạy"
                : now > x.EndAtUtc
                    ? "Hết hạn"
                    : "Đang chạy";

        return new PromotionListItemDto
        {
            Id = x.Id,
            Name = x.Name,
            Type = x.Type,
            TypeText = GetTypeText(x.Type),
            DiscountType = x.DiscountType,
            DiscountValue = x.DiscountValue,
            ComboFixedPrice = x.ComboFixedPrice,
            BuyQuantity = x.BuyQuantity,
            GetQuantity = x.GetQuantity,
            StartAtUtc = x.StartAtUtc,
            EndAtUtc = x.EndAtUtc,
            IsActive = x.IsActive,
            Priority = x.Priority,
            CustomerPriceTier = x.CustomerPriceTier,
            ItemCount = x.Items.Count(i => !i.IsDeleted),
            ComboRuleCount = x.ComboRules.Count(r => !r.IsDeleted),
            StatusText = statusText
        };
    }

    private static PromotionEditDto ToEditDto(Promotion x)
    {
        return new PromotionEditDto
        {
            Id = x.Id,
            Name = x.Name,
            Description = x.Description,
            Type = x.Type,
            DiscountType = x.DiscountType,
            DiscountValue = x.DiscountValue,
            ComboFixedPrice = x.ComboFixedPrice,
            ComboNote = x.ComboNote,
            BuyQuantity = x.BuyQuantity,
            GetQuantity = x.GetQuantity,
            RequireGiftQuantityInCart = x.RequireGiftQuantityInCart,
            StartAtUtc = x.StartAtUtc,
            EndAtUtc = x.EndAtUtc,
            IsActive = x.IsActive,
            Priority = x.Priority,
            CustomerPriceTier = x.CustomerPriceTier,
            RowVersion = x.RowVersion,
            Items = x.Items.Where(i => !i.IsDeleted).Select(i => new PromotionItemEditDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                VariantId = i.VariantId,
                ProductUnitConversionId = i.ProductUnitConversionId,
                MinQuantity = i.MinQuantity
            }).ToList(),
            ComboRules = x.ComboRules.Where(r => !r.IsDeleted).Select(r => new PromotionComboRuleEditDto
            {
                Id = r.Id,
                ProductId = r.ProductId,
                VariantId = r.VariantId,
                ProductUnitConversionId = r.ProductUnitConversionId,
                RequiredQuantity = r.RequiredQuantity
            }).ToList()
        };
    }

    private static SavePromotionRequest ToSaveRequest(Promotion x)
    {
        var dto = ToEditDto(x);

        return new SavePromotionRequest
        {
            Id = dto.Id,
            Name = dto.Name,
            Description = dto.Description,
            Type = dto.Type,
            DiscountType = dto.DiscountType,
            DiscountValue = dto.DiscountValue,
            ComboFixedPrice = dto.ComboFixedPrice,
            ComboNote = dto.ComboNote,
            BuyQuantity = dto.BuyQuantity,
            GetQuantity = dto.GetQuantity,
            RequireGiftQuantityInCart = dto.RequireGiftQuantityInCart,
            StartAtUtc = dto.StartAtUtc,
            EndAtUtc = dto.EndAtUtc,
            IsActive = dto.IsActive,
            Priority = dto.Priority,
            CustomerPriceTier = dto.CustomerPriceTier,
            Items = dto.Items,
            ComboRules = dto.ComboRules
        };
    }

    private static string GetTypeText(PromotionType type)
    {
        return type switch
        {
            PromotionType.ProductDiscount => "Giảm giá sản phẩm",
            PromotionType.ComboFixedPrice => "Combo giá cố định",
            PromotionType.BuyXGetY => "Mua X tặng Y",
            _ => "Khác"
        };
    }

    private static void Normalize(SavePromotionRequest request)
    {
        request.Name = (request.Name ?? string.Empty).Trim();
        request.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        request.ComboNote = string.IsNullOrWhiteSpace(request.ComboNote) ? null : request.ComboNote.Trim();
        request.CustomerPriceTier = NormalizeTierOrNull(request.CustomerPriceTier);

        if (request.Type == PromotionType.ProductDiscount)
        {
            request.ComboFixedPrice = null;
            request.ComboNote = null;
            request.BuyQuantity = null;
            request.GetQuantity = null;
        }

        if (request.Type == PromotionType.ComboFixedPrice)
        {
            request.DiscountValue = 0;
            request.BuyQuantity = null;
            request.GetQuantity = null;
            request.Items.Clear();
        }

        if (request.Type == PromotionType.BuyXGetY)
        {
            request.DiscountValue = 0;
            request.ComboFixedPrice = null;
            request.ComboNote = null;
        }
    }

    private static string? NormalizeTierOrNull(string? tier)
    {
        tier = (tier ?? string.Empty).Trim().ToUpperInvariant();

        if (tier == CustomerPriceTiers.Retail)
            return CustomerPriceTiers.Retail;

        if (tier == CustomerPriceTiers.Wholesale)
            return CustomerPriceTiers.Wholesale;

        return null;
    }
    public Task<List<PromotionProductLookupDto>> SearchProductsForPromotionAsync(
    int storeId,
    string keyword,
    int take = 20,
    CancellationToken ct = default)
    {
        return _repo.SearchProductsForPromotionAsync(storeId, keyword, take, ct);
    }

    public Task<List<PromotionProductUnitLookupDto>> GetUnitsForPromotionAsync(
        int storeId,
        int variantId,
        CancellationToken ct = default)
    {
        return _repo.GetUnitsForPromotionAsync(storeId, variantId, ct);
    }
    public Task<PromotionProductLookupDto?> GetProductForPromotionAsync(
    int storeId,
    int variantId,
    CancellationToken ct = default)
    {
        return _repo.GetProductForPromotionAsync(storeId, variantId, ct);
    }
}
