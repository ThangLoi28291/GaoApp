using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Customers;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public class InvoiceBuyerService : IInvoiceBuyerService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IInvoiceBuyerProfileRepository _invoiceBuyerProfileRepository;
    private readonly ITaxCodeLookupService _taxCodeLookupService;

    public InvoiceBuyerService(
        IInvoiceRepository invoiceRepository,
        ICustomerRepository customerRepository,
        IInvoiceBuyerProfileRepository invoiceBuyerProfileRepository,
        ITaxCodeLookupService taxCodeLookupService)
    {
        _invoiceRepository = invoiceRepository;
        _customerRepository = customerRepository;
        _invoiceBuyerProfileRepository = invoiceBuyerProfileRepository;
        _taxCodeLookupService = taxCodeLookupService;
    }

    public async Task<Result<InvoiceBuyerLookupDto>> LookupBuyerByTaxCodeAsync(
        int invoiceHeadId,
        string buyerType,
        string taxCode,
        CancellationToken ct = default)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<InvoiceBuyerLookupDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidInvoiceHeadId",
                    "InvoiceHeadId không hợp lệ."));
        }

        buyerType = InvoiceBuyerInfoHelper.NormalizeBuyerType(buyerType);
        taxCode = InvoiceBuyerInfoHelper.NormalizeBuyerTaxCodeForViettel(taxCode);

        if (string.IsNullOrWhiteSpace(taxCode))
        {
            return Result<InvoiceBuyerLookupDto>.Failure(
                Error.Validation(
                    "InvoiceBuyer.TaxCodeRequired",
                    "Vui lòng nhập MST/mã định danh."));
        }

        if (!InvoiceBuyerInfoHelper.IsValidBuyerTaxCodeForViettel(taxCode))
        {
            return Result<InvoiceBuyerLookupDto>.Failure(
                Error.Validation(
                    "InvoiceBuyer.TaxCodeInvalid",
                    "MST/mã định danh không hợp lệ. Chỉ cho phép chữ, số, dấu gạch ngang và tối đa 20 ký tự."));
        }

        var invoiceHead = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(
            invoiceHeadId,
            ct);

        if (invoiceHead == null)
        {
            return Result<InvoiceBuyerLookupDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

        var storeId = invoiceHead.StoreId;

        var profile = await _invoiceBuyerProfileRepository.GetBestByTaxCodeAsync(
            storeId,
            taxCode,
            buyerType,
            ct);

        if (profile != null)
        {
            profile.LastUsedAtUtc = DateTime.UtcNow;
            profile.UseCount += 1;

            await _invoiceRepository.SaveChangesAsync(ct);

            return Result<InvoiceBuyerLookupDto>.Success(
                new InvoiceBuyerLookupDto
                {
                    IsFound = true,
                    BuyerType = profile.BuyerType,
                    BuyerName = profile.BuyerName,
                    BuyerLegalName = profile.BuyerLegalName,
                    BuyerTaxCode = profile.TaxCode,
                    BuyerAddress = profile.BuyerAddress,
                    BuyerEmail = profile.BuyerEmail,
                    BuyerPhone = profile.BuyerPhone,
                    Source = "profile"
                });
        }

        var customer = await _customerRepository.GetActiveByTaxCodeAsync(
            storeId,
            taxCode,
            ct);

        if (customer != null)
        {
            var dto = InvoiceBuyerInfoHelper.MapCustomerToBuyerLookup(
                customer,
                buyerType,
                taxCode);

            return Result<InvoiceBuyerLookupDto>.Success(dto);
        }

        var latestInvoice = await _invoiceRepository.GetLatestBuyerInfoByTaxCodeAsync(
            storeId,
            taxCode,
            ct);

        if (latestInvoice != null)
        {
            return Result<InvoiceBuyerLookupDto>.Success(
                InvoiceBuyerInfoHelper.MapInvoiceHistoryToBuyerLookup(
                    latestInvoice,
                    taxCode));
        }

        string? vietQrError = null;

        if (buyerType == InvoiceBuyerTypes.Business)
        {
            var vietQrResult = await _taxCodeLookupService.LookupBusinessAsync(
                taxCode,
                ct);

            if (vietQrResult.IsSuccess && vietQrResult.Value.IsFound)
            {
                return Result<InvoiceBuyerLookupDto>.Success(
                    new InvoiceBuyerLookupDto
                    {
                        IsFound = true,
                        BuyerType = InvoiceBuyerTypes.Business,
                        BuyerName = null,
                        BuyerLegalName = vietQrResult.Value.CompanyName,
                        BuyerTaxCode = taxCode,
                        BuyerAddress = vietQrResult.Value.Address,
                        BuyerEmail = null,
                        BuyerPhone = null,
                        Source = "vietqr"
                    });
            }

            vietQrError = vietQrResult.Error?.Message;
        }

        return Result<InvoiceBuyerLookupDto>.Failure(
            Error.NotFound(
                string.IsNullOrWhiteSpace(vietQrError)
                    ? "Không tìm thấy thông tin người mua trong nội bộ. Bạn có thể tự nhập thủ công."
                    : $"Không tìm thấy thông tin người mua trong nội bộ. VietQR trả về: {vietQrError}"));
    }

    public async Task<Result<InvoiceHeadDto>> UpdateBuyerInfoAsync(
        UpdateInvoiceBuyerInfoRequest request,
        CancellationToken ct = default)
    {
        if (request.InvoiceHeadId <= 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidInvoiceHeadId",
                    "InvoiceHeadId không hợp lệ."));
        }

        var invoiceHead = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(
            request.InvoiceHeadId,
            ct);

        if (invoiceHead == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

        if (invoiceHead.IsLocked)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.Locked",
                    "Hóa đơn đã khóa, không thể sửa thông tin người mua."));
        }

        if (InvoiceBuyerInfoHelper.IsIssuedLike(invoiceHead))
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.AlreadyIssued",
                    "Hóa đơn đã phát hành Viettel, không được sửa trực tiếp thông tin người mua. Vui lòng lập hóa đơn điều chỉnh/thay thế."));
        }

        var buyerType = InvoiceBuyerInfoHelper.NormalizeBuyerType(request.BuyerType);

        var buyerName = InvoiceBuyerInfoHelper.NormalizeNullableText(request.BuyerName);
        var buyerLegalName = InvoiceBuyerInfoHelper.NormalizeNullableText(request.BuyerLegalName);
        var buyerTaxCode = InvoiceBuyerInfoHelper.NormalizeBuyerTaxCodeForViettel(request.BuyerTaxCode);
        var buyerAddress = InvoiceBuyerInfoHelper.NormalizeNullableText(request.BuyerAddress);
        var buyerEmail = InvoiceBuyerInfoHelper.NormalizeNullableText(request.BuyerEmail);
        var buyerPhone = InvoiceBuyerInfoHelper.NormalizeNullableText(request.BuyerPhone);

        var validation = InvoiceBuyerInfoHelper.ValidateBuyerInfo(
            buyerType,
            buyerName,
            buyerLegalName,
            buyerTaxCode);

        if (!validation.IsSuccess)
        {
            return Result<InvoiceHeadDto>.Failure(validation.Error!);
        }

        var buyerTargets = new List<InvoiceHead> { invoiceHead };
        if (invoiceHead.OrderId.HasValue && invoiceHead.LegalEntityId.HasValue && !invoiceHead.OriginalInvoiceHeadId.HasValue)
        {
            var siblingHeads = await _invoiceRepository
                .GetOriginalInvoiceHeadsWithDetailsByOrderIdAsync(invoiceHead.OrderId.Value, ct);
            buyerTargets = siblingHeads
                .Where(x =>
                    x.LegalEntityId.HasValue &&
                    !x.IsLocked &&
                    !InvoiceBuyerInfoHelper.IsIssuedLike(x))
                .ToList();

            if (buyerTargets.All(x => x.Id != invoiceHead.Id))
                buyerTargets.Add(invoiceHead);
        }

        foreach (var target in buyerTargets)
        {
            InvoiceBuyerInfoHelper.ApplyBuyerInfoToInvoiceHead(
                target,
                buyerType,
                buyerName,
                buyerLegalName,
                buyerTaxCode,
                buyerAddress,
                buyerEmail,
                buyerPhone);

            if (target.ProviderStatus == InvoiceProviderStatus.IssueFailed)
            {
                target.ProviderStatus = InvoiceProviderStatus.ReadyToIssue;
                target.LastErrorCode = null;
                target.LastErrorMessage = null;
            }
        }

        if (request.SaveToProfile &&
            buyerType != InvoiceBuyerTypes.NoInvoice &&
            !string.IsNullOrWhiteSpace(buyerTaxCode))
        {
            await UpsertBuyerProfileAsync(
                invoiceHead,
                buyerType,
                buyerName,
                buyerLegalName,
                buyerTaxCode,
                buyerAddress,
                buyerEmail,
                buyerPhone,
                request.Source,
                ct);
        }

        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<InvoiceHeadDto>.Success(
            InvoiceDtoMapper.ToDetailDto(invoiceHead));
    }

    private async Task UpsertBuyerProfileAsync(
        InvoiceHead invoiceHead,
        string buyerType,
        string? buyerName,
        string? buyerLegalName,
        string buyerTaxCode,
        string? buyerAddress,
        string? buyerEmail,
        string? buyerPhone,
        string? source,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(buyerTaxCode))
            return;

        var profile = await _invoiceBuyerProfileRepository.GetBestByTaxCodeAsync(
            invoiceHead.StoreId,
            buyerTaxCode,
            buyerType,
            ct);

        var now = DateTime.UtcNow;

        var normalizedSource = string.IsNullOrWhiteSpace(source)
            ? "manual"
            : source.Trim().ToLowerInvariant();

        if (profile == null)
        {
            profile = new InvoiceBuyerProfile
            {
                StoreId = invoiceHead.StoreId,
                BuyerType = buyerType,
                TaxCode = buyerTaxCode,
                Source = normalizedSource,
                IsVerifiedByUser = true,
                LastLookupAtUtc = normalizedSource == "vietqr" ? now : null,
                LastUsedAtUtc = now,
                UseCount = 1,
                IsActive = true
            };

            await _invoiceBuyerProfileRepository.AddAsync(profile, ct);
        }
        else
        {
            profile.BuyerType = buyerType;
            profile.Source = normalizedSource;
            profile.IsVerifiedByUser = true;
            profile.LastUsedAtUtc = now;
            profile.UseCount += 1;

            if (normalizedSource == "vietqr")
            {
                profile.LastLookupAtUtc = now;
            }

            _invoiceBuyerProfileRepository.Update(profile);
        }

        profile.BuyerName = buyerName;
        profile.BuyerLegalName = buyerLegalName;
        profile.BuyerAddress = buyerAddress;
        profile.BuyerEmail = buyerEmail;
        profile.BuyerPhone = buyerPhone;
    }
}
