using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Invoices;

public sealed class InvoiceInputStockRepositoryTests
{
    [Fact]
    public async Task Issue_ShouldKeepDraftAndNotCallProvider_WhenInputInvoiceStockIsInsufficient()
    {
        await using var context = CreateContext();
        var data = await SeedBaseAsync(context, requiredQuantity: 4m);
        await AddReceiptAsync(
            context,
            documentId: 101,
            lineId: 201,
            data.VariantId,
            baseQuantity: 3m,
            withInputInvoice: true);
        context.InvoiceProviderSettings.Add(new InvoiceProviderSetting
        {
            Id = 701,
            StoreId = 1,
            ProviderCode = "VIETTEL",
            BaseUrl = "https://example.test",
            Username = "user",
            Password = "protected-password",
            SupplierTaxCode = "0100000001",
            InvoiceType = "1",
            TemplateCode = "1/001",
            InvoiceSeries = "C26TAA",
            CurrencyCode = "VND",
            IsActive = true,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        var issueClient = new RecordingIssueClient();
        var service = new ViettelInvoiceIssueService(
            new InvoiceRepository(context),
            new InvoiceProviderSettingRepository(context, new TestCredentialProtector()),
            new InvoiceCorrectionRepository(context),
            new InvoiceInputStockRepository(context),
            new NoOpUnitOfWork(),
            new SuccessfulPayloadBuilder(),
            issueClient);

        var result = await service.IssueAsync(data.InvoiceHeadId);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Invoice.InputInvoiceStockInsufficient");
        result.Error.Message.Should().Contain("khả dụng 3").And.Contain("thiếu 1");
        issueClient.CallCount.Should().Be(0);
        context.InvoiceHeads.Single(x => x.Id == data.InvoiceHeadId)
            .ProviderStatus.Should().Be(InvoiceProviderStatus.LocalDraft);
    }

    [Fact]
    public async Task Availability_ShouldCountOnlyConfirmedReceiptLinesMappedToInputInvoice()
    {
        await using var context = CreateContext();
        var data = await SeedBaseAsync(context, requiredQuantity: 4m);

        await AddReceiptAsync(
            context,
            documentId: 101,
            lineId: 201,
            data.VariantId,
            baseQuantity: 3m,
            withInputInvoice: true);
        await AddReceiptAsync(
            context,
            documentId: 102,
            lineId: 202,
            data.VariantId,
            baseQuantity: 2m,
            withInputInvoice: false);

        var repository = new InvoiceInputStockRepository(context);

        var result = await repository.GetAvailabilityAsync(data.InvoiceHeadId);

        result.IsSufficient.Should().BeFalse();
        result.Lines.Should().ContainSingle();
        result.Lines.Single().Should().BeEquivalentTo(new
        {
            WarehouseId = 11,
            ProductVariantId = data.VariantId,
            RequiredBaseQuantity = 4m,
            EligibleInboundBaseQuantity = 3m,
            CommittedOutboundBaseQuantity = 0m,
            AvailableBaseQuantity = 3m,
            ShortageBaseQuantity = 1m,
            IsSufficient = false
        });
    }

    [Fact]
    public async Task Availability_ShouldSubtractQuantityAlreadyCommittedByIssuedInvoice()
    {
        await using var context = CreateContext();
        var current = await SeedBaseAsync(context, requiredQuantity: 2m);
        await AddReceiptAsync(
            context,
            documentId: 101,
            lineId: 201,
            current.VariantId,
            baseQuantity: 3m,
            withInputInvoice: true);

        var issuedHead = new InvoiceHead
        {
            Id = 302,
            StoreId = 1,
            OrderId = current.OrderId,
            ProviderStatus = InvoiceProviderStatus.Issued,
            ProviderInvoiceNo = "00000001",
            RowVersion = new byte[8]
        };
        var issuedDetail = new InvoiceDetail
        {
            Id = 402,
            StoreId = 1,
            InvoiceHeadId = issuedHead.Id,
            OrderLineId = current.OrderLineId,
            OrderLegalEntityAllocationId = current.AllocationId,
            ProductVariantId = current.VariantId,
            ItemName = "Sản phẩm có hóa đơn",
            Quantity = 2m,
            RowVersion = new byte[8]
        };
        context.InvoiceHeads.Add(issuedHead);
        context.InvoiceDetails.Add(issuedDetail);
        await context.SaveChangesAsync();

        var repository = new InvoiceInputStockRepository(context);

        var result = await repository.GetAvailabilityAsync(current.InvoiceHeadId);

        result.IsSufficient.Should().BeFalse();
        result.Lines.Single().CommittedOutboundBaseQuantity.Should().Be(2m);
        result.Lines.Single().AvailableBaseQuantity.Should().Be(1m);
        result.Lines.Single().ShortageBaseQuantity.Should().Be(1m);
    }

    private static async Task<SeededData> SeedBaseAsync(
        InMemoryAppDbContext context,
        decimal requiredQuantity)
    {
        context.Stores.Add(new Store
        {
            Id = 1,
            Name = "store-one",
            SubDomain = "store-one",
            SubDomainNormalized = "STORE-ONE",
            IsActive = true,
            RowVersion = new byte[8]
        });

        context.POSShifts.Add(new POSShift
        {
            Id = 21,
            StoreId = 1,
            TerminalId = 1,
            OpenedByUserId = 1,
            WarehouseId = 11,
            RowVersion = new byte[8]
        });

        context.ProductVariants.Add(new ProductVariant
        {
            Id = 31,
            StoreId = 1,
            ProductId = 1,
            Sku = "INPUT-INVOICE-31",
            ProductVariantName = "Sản phẩm có hóa đơn",
            HasInputInvoice = true,
            IsActive = true,
            RowVersion = new byte[8]
        });

        context.Orders.Add(new Order
        {
            Id = 41,
            StoreId = 1,
            POSShiftId = 21,
            GrandTotal = 400_000m,
            RowVersion = new byte[8]
        });

        context.OrderLines.Add(new OrderLine
        {
            Id = 51,
            StoreId = 1,
            OrderId = 41,
            ProductId = 1,
            VariantId = 31,
            ItemName = "Sản phẩm có hóa đơn",
            Quantity = requiredQuantity,
            BaseQuantity = requiredQuantity,
            Multiplier = 1m,
            UnitPrice = 100_000m,
            LineTotal = requiredQuantity * 100_000m,
            RowVersion = new byte[8]
        });

        context.OrderLegalEntityAllocations.Add(new OrderLegalEntityAllocation
        {
            Id = 61,
            StoreId = 1,
            OrderId = 41,
            OrderLineId = 51,
            ProductVariantId = 31,
            LegalEntityId = 1,
            WarehouseId = 11,
            SalePriority = 1,
            Quantity = requiredQuantity,
            BaseQuantity = requiredQuantity,
            UnitPrice = 100_000m,
            LineTotal = requiredQuantity * 100_000m,
            NetAmount = requiredQuantity * 100_000m,
            RowVersion = new byte[8]
        });

        context.InvoiceHeads.Add(new InvoiceHead
        {
            Id = 301,
            StoreId = 1,
            OrderId = 41,
            ProviderStatus = InvoiceProviderStatus.LocalDraft,
            GrandTotal = requiredQuantity * 100_000m,
            RowVersion = new byte[8]
        });

        context.InvoiceDetails.Add(new InvoiceDetail
        {
            Id = 401,
            StoreId = 1,
            InvoiceHeadId = 301,
            OrderLineId = 51,
            OrderLegalEntityAllocationId = 61,
            ProductVariantId = 31,
            ItemName = "Sản phẩm có hóa đơn",
            Quantity = requiredQuantity,
            RowVersion = new byte[8]
        });

        await context.SaveChangesAsync();

        return new SeededData(301, 41, 51, 61, 31);
    }

    private static async Task AddReceiptAsync(
        InMemoryAppDbContext context,
        int documentId,
        int lineId,
        int variantId,
        decimal baseQuantity,
        bool withInputInvoice)
    {
        context.StockDocuments.Add(new StockDocument
        {
            Id = documentId,
            StoreId = 1,
            DocumentNo = $"PN-{documentId}",
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.Confirmed,
            WarehouseId = 11,
            RowVersion = new byte[8]
        });
        context.StockDocumentLines.Add(new StockDocumentLine
        {
            Id = lineId,
            StockDocumentId = documentId,
            LineNo = 1,
            ProductVariantId = variantId,
            Quantity = baseQuantity,
            Factor = 1m,
            BaseQuantity = baseQuantity,
            ProductNameSnapshot = "Sản phẩm có hóa đơn",
            RowVersion = new byte[8]
        });

        if (withInputInvoice)
        {
            var inputHeadId = documentId + 1000;
            context.InputInvoiceHeads.Add(new InputInvoiceHead
            {
                Id = inputHeadId,
                StoreId = 1,
                InvoiceNumber = $"HD-{documentId}",
                RowVersion = new byte[8]
            });
            context.StockDocumentInputInvoiceMaps.Add(new StockDocumentInputInvoiceMap
            {
                Id = documentId + 2000,
                StoreId = 1,
                StockDocumentId = documentId,
                InputInvoiceHeadId = inputHeadId,
                RowVersion = new byte[8]
            });
            context.StockDocumentLineInputInvoiceMaps.Add(new StockDocumentLineInputInvoiceMap
            {
                Id = documentId + 3000,
                StoreId = 1,
                StockDocumentId = documentId,
                StockDocumentLineId = lineId,
                UseInputInvoice = true,
                RowVersion = new byte[8]
            });
        }

        await context.SaveChangesAsync();
    }

    private static InMemoryAppDbContext CreateContext()
    {
        var tenant = new TenantContext();
        tenant.SetStore(1, "store-one");
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new InMemoryAppDbContext(options, tenant, new TestCurrentUser());
        context.VerifyRowVersionConfiguration();
        return context;
    }

    private sealed record SeededData(
        int InvoiceHeadId,
        int OrderId,
        int OrderLineId,
        int AllocationId,
        int VariantId);

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 1;
        public string? UserName => "phase-22-7-input-stock-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed class TestCredentialProtector : IInvoiceProviderCredentialProtector
    {
        public bool IsProtected(string value) => true;
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedOrLegacyPlaintext) => protectedOrLegacyPlaintext;
    }

    private sealed class SuccessfulPayloadBuilder : IViettelInvoicePayloadBuilder
    {
        public Task<Result<ViettelInvoicePayloadResultDto>> BuildAsync(
            int invoiceHeadId,
            CancellationToken ct = default)
            => Task.FromResult(Result<ViettelInvoicePayloadResultDto>.Success(
                new ViettelInvoicePayloadResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    StoreId = 1,
                    SupplierTaxCode = "0100000001",
                    Payload = new ViettelInvoicePayloadDto()
                }));
    }

    private sealed class RecordingIssueClient : IViettelInvoiceIssueClient
    {
        public int CallCount { get; private set; }

        public Task<Result<ViettelInvoiceIssueResultDto>> IssueInvoiceAsync(
            int invoiceHeadId,
            string baseUrl,
            string username,
            string password,
            InvoiceProviderAuthMode authMode,
            string supplierTaxCode,
            ViettelInvoicePayloadDto payload,
            CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(Result<ViettelInvoiceIssueResultDto>.Success(
                new ViettelInvoiceIssueResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = true,
                    InvoiceNo = "00000001"
                }));
        }
    }

    private sealed class NoOpUnitOfWork : IAppUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Task.FromResult<IAppTransaction>(new NoOpTransaction());
    }

    private sealed class NoOpTransaction : IAppTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
