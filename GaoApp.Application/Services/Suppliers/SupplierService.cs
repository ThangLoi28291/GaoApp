using FluentValidation;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Errors;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Common;
using GaoApp.Application.DTOs.Suppliers;
using GaoApp.Application.Interfaces.Repositories.Suppliers;
using GaoApp.Application.Interfaces.Services.Suppliers;
using GaoApp.Application.Mappings.Suppliers;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Suppliers;

public sealed class SupplierService : ISupplierService
{
    private readonly ISupplierRepository _repo;
    private readonly IValidator<SupplierEditDto> _validator;

    public SupplierService(
        ISupplierRepository repo,
        IValidator<SupplierEditDto> validator)
    {
        _repo = repo;
        _validator = validator;
    }

    public async Task<PagedResult<SupplierListItemDto>> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(storeId, search, page, pageSize, ct);

        return new PagedResult<SupplierListItemDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            Items = items.Select(static item => item.ToListItemDto()).ToList()
        };
    }

    public async Task<Result<SupplierEditDto>> GetForEditAsync(
        int storeId, int id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity is null)
            return Result.Failure<SupplierEditDto>(SupplierErrors.NotFound(id));

        var dto = entity.ToEditDto();
        return Result.Success(dto);
    }

    public async Task<Result<int>> CreateAsync(
        int storeId, CreateSupplierRequest dto, int? userId, CancellationToken ct = default)
    {
        var editDto = NormalizeForValidation(new SupplierEditDto
        {
            Code = dto.Code ?? string.Empty,
            Name = dto.Name,
            Phone = dto.Phone,
            Email = dto.Email,
            Address = dto.Address,
            ContactName = dto.ContactName,
            TaxCode = dto.TaxCode,
            Note = dto.Note,
            Status = dto.Status
        });

        var validation = await _validator.ValidateAsync(editDto, ct);
        if (!validation.IsValid)
            return Result.ValidationFailure<int>(ToValidationErrors(validation));

        var code = string.IsNullOrWhiteSpace(editDto.Code)
            ? await GenerateSupplierCodeAsync(storeId, ct)
            : editDto.Code!;

        if (await _repo.ExistsCodeAsync(storeId, code, null, ct))
            return Result.Failure<int>(SupplierErrors.DuplicateCode(code));

        if (await _repo.ExistsNameAsync(storeId, editDto.Name!, null, ct))
            return Result.Failure<int>(SupplierErrors.DuplicateName(editDto.Name!));

        var entity = new Supplier
        {
            StoreId = storeId,
            Code = code,
            Name = editDto.Name!,
            Phone = editDto.Phone,
            Email = editDto.Email,
            Address = editDto.Address,
            ContactName = editDto.ContactName,
            TaxCode = editDto.TaxCode,
            Note = editDto.Note,
            IsActive = editDto.Status,
            SortOrder = 0,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = userId
        };

        await _repo.AddAsync(entity, ct);
        await _repo.SaveChangesAsync(ct);

        if (entity.Id <= 0)
            return Result.Failure<int>(SupplierErrors.CreateFailed);

        return Result.Success(entity.Id);
    }

    public async Task<Result> UpdateAsync(
        int storeId, UpdateSupplierRequest dto, int? userId, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(storeId, dto.Id, ct);
        if (entity is null)
            return Result.Failure(SupplierErrors.NotFound(dto.Id));

        var editDto = NormalizeForValidation(new SupplierEditDto
        {
            Id = dto.Id,
            Code = dto.Code,
            Name = dto.Name,
            Phone = dto.Phone,
            Email = dto.Email,
            Address = dto.Address,
            ContactName = dto.ContactName,
            TaxCode = dto.TaxCode,
            Note = dto.Note,
            Status = dto.Status,
            RowVersion = dto.RowVersion
        });

        var validation = await _validator.ValidateAsync(editDto, ct);
        if (!validation.IsValid)
            return Result.ValidationFailure(ToValidationErrors(validation));

        // Giữ hành vi hiện tại: edit bắt buộc có code.
        if (string.IsNullOrWhiteSpace(editDto.Code))
        {
            return Result.Failure(SupplierErrors.CodeRequired);
        }

        if (await _repo.ExistsCodeAsync(storeId, editDto.Code, dto.Id, ct))
            return Result.Failure(SupplierErrors.DuplicateCode(editDto.Code));

        if (await _repo.ExistsNameAsync(storeId, editDto.Name!, dto.Id, ct))
            return Result.Failure(SupplierErrors.DuplicateName(editDto.Name!));

        // Gán RowVersion để EF check concurrency như flow hiện tại
        if (editDto.RowVersion is { Length: > 0 })
            entity.RowVersion = editDto.RowVersion;

        // Update an toàn từng field, không map đè toàn bộ entity
        entity.Code = editDto.Code;
        entity.Name = editDto.Name!;
        entity.Phone = editDto.Phone;
        entity.Email = editDto.Email;
        entity.Address = editDto.Address;
        entity.ContactName = editDto.ContactName;
        entity.TaxCode = editDto.TaxCode;
        entity.Note = editDto.Note;
        entity.IsActive = editDto.Status;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = userId;

        try
        {
            await _repo.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            if (ex.GetType().Name == "DbUpdateConcurrencyException")
                return Result.Failure(SupplierErrors.ConcurrencyConflict);

            return Result.Failure(SupplierErrors.UpdateFailed);
        }
    }

    public async Task<Result> ToggleStatusAsync(
        int storeId, int id, int? userId, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity is null)
            return Result.Failure(SupplierErrors.NotFound(id));

        entity.IsActive = !entity.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = userId;

        await _repo.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SoftDeleteAsync(
        int storeId, int id, int? userId, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity is null)
            return Result.Failure(SupplierErrors.NotFound(id));

        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedBy = userId;

        await _repo.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<List<Select2OptionDto>> SearchSelect2Async(
        int storeId, string? term, CancellationToken ct = default)
    {
        var result = await _repo.GetPagedAsync(
            storeId: storeId,
            search: term,
            page: 1,
            pageSize: 20,
            ct: ct);

        return result.Items
            .OrderBy(x => x.Name)
            .Select(x => new Select2OptionDto
            {
                Id = x.Id.ToString(),
                Text = x.Name
            })
            .ToList();
    }

    private async Task<string> GenerateSupplierCodeAsync(int storeId, CancellationToken ct)
    {
        for (var i = 0; i < 50; i++)
        {
            var code = "NCC" + Random.Shared.Next(100000, 999999);
            if (!await _repo.ExistsCodeAsync(storeId, code, null, ct))
                return code;
        }

        return "NCC" + DateTime.UtcNow.ToString("yyMMddHHmmss");
    }

    private static SupplierEditDto NormalizeForValidation(SupplierEditDto dto)
    {
        dto.Code = NormalizeRequired(dto.Code).ToUpperInvariant();
        dto.Name = NormalizeRequired(dto.Name);

        dto.Phone = NormalizeNullable(dto.Phone);
        dto.Email = NormalizeNullable(dto.Email)?.ToLowerInvariant();
        dto.Address = NormalizeNullable(dto.Address);
        dto.ContactName = NormalizeNullable(dto.ContactName);
        dto.TaxCode = NormalizeNullable(dto.TaxCode);
        dto.Note = NormalizeNullable(dto.Note);

        return dto;
    }

    private static string NormalizeRequired(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static List<GaoApp.Application.Common.Results.ValidationError> ToValidationErrors(
    FluentValidation.Results.ValidationResult validationResult)
    {
        return validationResult.Errors
            .Select(x => new GaoApp.Application.Common.Results.ValidationError(
                x.PropertyName,
                x.ErrorMessage))
            .ToList();
    }
}