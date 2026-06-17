using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public class InvoiceProviderSettingService : IInvoiceProviderSettingService
{
    private readonly IInvoiceProviderSettingRepository _repository;
    private readonly IViettelInvoiceAuthClient _viettelAuthClient;

    public InvoiceProviderSettingService(
        IInvoiceProviderSettingRepository repository,
        IViettelInvoiceAuthClient viettelAuthClient)
    {
        _repository = repository;
        _viettelAuthClient = viettelAuthClient;
    }

    public async Task<Result<List<InvoiceProviderSettingListItemDto>>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await _repository.GetAllAsync(ct);

        var result = items.Select(x => new InvoiceProviderSettingListItemDto
        {
            Id = x.Id,
            ProviderCode = x.ProviderCode,
            IsProduction = x.IsProduction,
            BaseUrl = x.BaseUrl,
            Username = x.Username,
            SupplierTaxCode = x.SupplierTaxCode,
            InvoiceType = x.InvoiceType,
            TemplateCode = x.TemplateCode,
            InvoiceSeries = x.InvoiceSeries,
            CurrencyCode = x.CurrencyCode,
            PaymentMethodName = x.PaymentMethodName,
            IsActive = x.IsActive,
            AuthMode = x.AuthMode,
            Note = x.Note
        }).ToList();

        return Result<List<InvoiceProviderSettingListItemDto>>.Success(result);
    }

    public async Task<Result<UpsertInvoiceProviderSettingRequest>> GetForEditAsync(
        int id,
        CancellationToken ct = default)
    {
        if (id <= 0)
        {
            return Result<UpsertInvoiceProviderSettingRequest>.Failure(
                Error.Validation("InvoiceProvider.InvalidId", "Id cấu hình không hợp lệ."));
        }

        var entity = await _repository.GetByIdAsync(id, ct);

        if (entity == null)
        {
            return Result<UpsertInvoiceProviderSettingRequest>.Failure(
                Error.NotFound("Không tìm thấy cấu hình hóa đơn điện tử."));
        }

        return Result<UpsertInvoiceProviderSettingRequest>.Success(new UpsertInvoiceProviderSettingRequest
        {
            Id = entity.Id,
            ProviderCode = entity.ProviderCode,
            IsProduction = entity.IsProduction,
            BaseUrl = entity.BaseUrl,
            Username = entity.Username,
            Password = entity.Password,
            SupplierTaxCode = entity.SupplierTaxCode,
            InvoiceType = entity.InvoiceType,
            TemplateCode = entity.TemplateCode,
            InvoiceSeries = entity.InvoiceSeries,
            CurrencyCode = entity.CurrencyCode,
            ExchangeRate = entity.ExchangeRate,
            PaymentMethodName = entity.PaymentMethodName,
            CusGetInvoiceRight = entity.CusGetInvoiceRight,
            DefaultPaymentStatus = entity.DefaultPaymentStatus,
            IsActive = entity.IsActive,
            AuthMode = entity.AuthMode,
            Note = entity.Note
        });
    }

    public async Task<Result<int>> CreateAsync(
        UpsertInvoiceProviderSettingRequest request,
        CancellationToken ct = default)
    {
        var normalized = Normalize(request);

        var validation = await ValidateAsync(normalized, ct);

        if (!validation.IsSuccess)
            return Result<int>.Failure(validation.Error!);

        var entity = new InvoiceProviderSetting
        {
            ProviderCode = normalized.ProviderCode,
            IsProduction = normalized.IsProduction,
            BaseUrl = normalized.BaseUrl,
            Username = normalized.Username,
            Password = normalized.Password,
            SupplierTaxCode = normalized.SupplierTaxCode,
            InvoiceType = normalized.InvoiceType,
            TemplateCode = normalized.TemplateCode,
            InvoiceSeries = normalized.InvoiceSeries,
            CurrencyCode = normalized.CurrencyCode,
            ExchangeRate = normalized.ExchangeRate,
            PaymentMethodName = normalized.PaymentMethodName,
            CusGetInvoiceRight = normalized.CusGetInvoiceRight,
            DefaultPaymentStatus = normalized.DefaultPaymentStatus,
            IsActive = normalized.IsActive,
            AuthMode = normalized.AuthMode,
            Note = normalized.Note
        };

        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);

        return Result<int>.Success(entity.Id);
    }

    public async Task<Result<int>> UpdateAsync(
        UpsertInvoiceProviderSettingRequest request,
        CancellationToken ct = default)
    {
        if (request.Id <= 0)
        {
            return Result<int>.Failure(
                Error.Validation("InvoiceProvider.InvalidId", "Id cấu hình không hợp lệ."));
        }

        var entity = await _repository.GetByIdAsync(request.Id, ct);

        if (entity == null)
        {
            return Result<int>.Failure(
                Error.NotFound("Không tìm thấy cấu hình hóa đơn điện tử."));
        }

        var normalized = Normalize(request);

        var validation = await ValidateAsync(normalized, ct);

        if (!validation.IsSuccess)
            return Result<int>.Failure(validation.Error!);

        entity.ProviderCode = normalized.ProviderCode;
        entity.IsProduction = normalized.IsProduction;
        entity.BaseUrl = normalized.BaseUrl;
        entity.Username = normalized.Username;
        entity.Password = normalized.Password;
        entity.SupplierTaxCode = normalized.SupplierTaxCode;
        entity.InvoiceType = normalized.InvoiceType;
        entity.TemplateCode = normalized.TemplateCode;
        entity.InvoiceSeries = normalized.InvoiceSeries;
        entity.CurrencyCode = normalized.CurrencyCode;
        entity.ExchangeRate = normalized.ExchangeRate;
        entity.PaymentMethodName = normalized.PaymentMethodName;
        entity.CusGetInvoiceRight = normalized.CusGetInvoiceRight;
        entity.DefaultPaymentStatus = normalized.DefaultPaymentStatus;
        entity.IsActive = normalized.IsActive;
        entity.Note = normalized.Note;
        entity.AuthMode = normalized.AuthMode;

        _repository.Update(entity);
        await _repository.SaveChangesAsync(ct);

        return Result<int>.Success(entity.Id);
    }

    public async Task<Result<bool>> ToggleActiveAsync(int id, CancellationToken ct = default)
    {
        if (id <= 0)
        {
            return Result<bool>.Failure(
                Error.Validation("InvoiceProvider.InvalidId", "Id cấu hình không hợp lệ."));
        }

        var entity = await _repository.GetByIdAsync(id, ct);

        if (entity == null)
        {
            return Result<bool>.Failure(
                Error.NotFound("Không tìm thấy cấu hình hóa đơn điện tử."));
        }

        entity.IsActive = !entity.IsActive;

        _repository.Update(entity);
        await _repository.SaveChangesAsync(ct);

        return Result<bool>.Success(entity.IsActive);
    }

    public async Task<Result<TestInvoiceProviderLoginResultDto>> TestLoginAsync(
        int id,
        CancellationToken ct = default)
    {
        if (id <= 0)
        {
            return Result<TestInvoiceProviderLoginResultDto>.Failure(
                Error.Validation("InvoiceProvider.InvalidId", "Id cấu hình không hợp lệ."));
        }

        var entity = await _repository.GetByIdAsync(id, ct);

        if (entity == null)
        {
            return Result<TestInvoiceProviderLoginResultDto>.Failure(
                Error.NotFound("Không tìm thấy cấu hình hóa đơn điện tử."));
        }

        if (!string.Equals(entity.ProviderCode, "VIETTEL", StringComparison.OrdinalIgnoreCase))
        {
            return Result<TestInvoiceProviderLoginResultDto>.Failure(
                Error.Validation(
                    "InvoiceProvider.UnsupportedProvider",
                    "Hiện tại mới hỗ trợ test kết nối Viettel SInvoice."));
        }

        return await _viettelAuthClient.TestConnectionAsync(
    entity.BaseUrl,
    entity.Username,
    entity.Password,
    entity.AuthMode,
    entity.SupplierTaxCode,
    entity.InvoiceType,
    entity.TemplateCode,
    entity.InvoiceSeries,
    ct);
    }

    private async Task<Result<bool>> ValidateAsync(
        UpsertInvoiceProviderSettingRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderCode))
        {
            return Result<bool>.Failure(
                Error.Validation("InvoiceProvider.ProviderRequired", "ProviderCode không được trống."));
        }

        if (string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            return Result<bool>.Failure(
                Error.Validation("InvoiceProvider.BaseUrlRequired", "BaseUrl không được trống."));
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Result<bool>.Failure(
                Error.Validation("InvoiceProvider.UsernameRequired", "Username không được trống."));
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Result<bool>.Failure(
                Error.Validation("InvoiceProvider.PasswordRequired", "Password không được trống."));
        }

        if (string.IsNullOrWhiteSpace(request.SupplierTaxCode))
        {
            return Result<bool>.Failure(
                Error.Validation("InvoiceProvider.SupplierTaxCodeRequired", "Mã số thuế phát hành không được trống."));
        }

        if (string.IsNullOrWhiteSpace(request.TemplateCode))
        {
            return Result<bool>.Failure(
                Error.Validation("InvoiceProvider.TemplateCodeRequired", "Mẫu hóa đơn không được trống."));
        }

        if (string.IsNullOrWhiteSpace(request.InvoiceSeries))
        {
            return Result<bool>.Failure(
                Error.Validation("InvoiceProvider.InvoiceSeriesRequired", "Ký hiệu hóa đơn không được trống."));
        }

        if (request.ExchangeRate <= 0)
        {
            return Result<bool>.Failure(
                Error.Validation("InvoiceProvider.ExchangeRateInvalid", "Tỷ giá phải lớn hơn 0."));
        }

        var duplicated = await _repository.ExistsDuplicateAsync(
            request.Id,
            request.ProviderCode,
            request.SupplierTaxCode,
            request.TemplateCode,
            request.InvoiceSeries,
            ct);

        if (duplicated)
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceProvider.Duplicated",
                    "Đã tồn tại cấu hình cùng Provider, MST, mẫu hóa đơn và ký hiệu hóa đơn."));
        }

        return Result<bool>.Success(true);
    }

    private static UpsertInvoiceProviderSettingRequest Normalize(UpsertInvoiceProviderSettingRequest request)
    {
        return new UpsertInvoiceProviderSettingRequest
        {
            Id = request.Id,
            ProviderCode = string.IsNullOrWhiteSpace(request.ProviderCode)
                ? "VIETTEL"
                : request.ProviderCode.Trim().ToUpperInvariant(),

            IsProduction = request.IsProduction,

            BaseUrl = (request.BaseUrl ?? string.Empty).Trim().TrimEnd('/'),
            Username = (request.Username ?? string.Empty).Trim(),
            Password = request.Password ?? string.Empty,

            SupplierTaxCode = (request.SupplierTaxCode ?? string.Empty).Trim(),
            InvoiceType = string.IsNullOrWhiteSpace(request.InvoiceType)
                ? "1"
                : request.InvoiceType.Trim(),

            TemplateCode = (request.TemplateCode ?? string.Empty).Trim(),
            InvoiceSeries = (request.InvoiceSeries ?? string.Empty).Trim(),

            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
                ? "VND"
                : request.CurrencyCode.Trim().ToUpperInvariant(),

            ExchangeRate = request.ExchangeRate <= 0 ? 1m : request.ExchangeRate,

            PaymentMethodName = string.IsNullOrWhiteSpace(request.PaymentMethodName)
                ? "TM"
                : request.PaymentMethodName.Trim(),

            CusGetInvoiceRight = request.CusGetInvoiceRight,
            DefaultPaymentStatus = request.DefaultPaymentStatus,
            IsActive = request.IsActive,
            AuthMode = request.AuthMode == 0
    ? InvoiceProviderAuthMode.BasicAuth
    : request.AuthMode,
            Note = string.IsNullOrWhiteSpace(request.Note)
                ? null
                : request.Note.Trim()
        };
    }
}