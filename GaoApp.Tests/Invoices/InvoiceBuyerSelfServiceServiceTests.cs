using System.Security.Cryptography;
using System.Text;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Invoices;

public sealed class InvoiceBuyerSelfServiceServiceTests
{
    private static readonly DateTime SaleAtUtc =
        new(2026, 9, 26, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_link_uses_hash_only_token_and_fixed_two_hour_expiry()
    {
        await using var h = CreateHarness();

        var result =
            await h.Service.CreateLinkAsync(100);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            SaleAtUtc.AddHours(2),
            result.Value.ExpiresAtUtc);

        Assert.Equal(64, result.Value.Token.Length);

        var row = Assert.Single(h.Requests.Items);

        var expectedHash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    result.Value.Token));

        Assert.Equal(expectedHash, row.TokenHash);

        // Plain token không được lưu vào entity.
        Assert.NotEqual(
            result.Value.Token,
            Convert.ToHexString(row.TokenHash)
                .ToLowerInvariant());
    }

    [Fact]
    public async Task Reprint_creates_new_token_but_does_not_extend_expiry()
    {
        await using var h = CreateHarness();

        var first =
            await h.Service.CreateLinkAsync(100);

        var second =
            await h.Service.CreateLinkAsync(100);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        Assert.NotEqual(
            first.Value.Token,
            second.Value.Token);

        Assert.Equal(
            first.Value.ExpiresAtUtc,
            second.Value.ExpiresAtUtc);

        Assert.Equal(2, h.Requests.Items.Count);
    }

    [Fact]
    public async Task Automatic_order_cannot_create_manual_self_service_link()
    {
        await using var h = CreateHarness(
            InvoiceIssuanceRoute.Automatic);

        var result =
            await h.Service.CreateLinkAsync(100);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error.Code);
        Assert.Empty(h.Requests.Items);
    }

    [Fact]
    public async Task Manual_noinvoice_draft_displays_as_individual_for_public_form()
    {
        await using var h = CreateHarness();

        var link =
            await h.Service.CreateLinkAsync(100);

        Assert.True(link.IsSuccess);

        var page =
            await h.Service.GetAsync(link.Value.Token);

        Assert.True(page.IsSuccess);

        Assert.Equal(
            InvoiceBuyerTypes.Individual,
            page.Value.BuyerType);

        Assert.True(page.Value.CanEdit);
        Assert.False(page.Value.IsExpired);
    }

    [Fact]
    public async Task Submit_before_expiry_updates_buyer_and_submission_timestamp()
    {
        await using var h = CreateHarness();

        var link =
            await h.Service.CreateLinkAsync(100);

        var result =
            await h.Service.SubmitAsync(
                link.Value.Token,
                new SubmitInvoiceBuyerSelfServiceRequest
                {
                    BuyerType =
                        InvoiceBuyerTypes.Business,

                    BuyerName = "Nguyễn Văn A",

                    BuyerLegalName =
                        "Công ty ABC",

                    BuyerTaxCode =
                        "3801234567",

                    BuyerCitizenId =
                        "079123456789",

                    BuyerAddress =
                        "Đồng Nai",

                    BuyerEmail =
                        "buyer@example.com",

                    BuyerPhone =
                        "0900000000"
                });

        Assert.True(result.IsSuccess);

        Assert.Equal(
            InvoiceBuyerTypes.Business,
            result.Value.BuyerType);

        Assert.Equal(
            "Công ty ABC",
            result.Value.BuyerLegalName);

        Assert.Equal(
            "079123456789",
            result.Value.BuyerCitizenId);

        Assert.Equal(
            h.Clock.GetUtcNow().UtcDateTime,
            result.Value.LastSubmittedAtUtc);

        Assert.Equal(1, h.BuyerService.Calls);

        Assert.False(
            h.BuyerService.LastRequest!.SaveToProfile);

        Assert.Equal(
            "qr-self-service",
            h.BuyerService.LastRequest.Source);
    }

    [Fact]
    public async Task At_two_hour_boundary_page_is_read_only_and_post_is_rejected()
    {
        await using var h = CreateHarness();

        var link =
            await h.Service.CreateLinkAsync(100);

        h.Clock.NowUtc =
            SaleAtUtc.AddHours(2);

        var page =
            await h.Service.GetAsync(
                link.Value.Token);

        Assert.True(page.IsSuccess);
        Assert.True(page.Value.IsExpired);
        Assert.False(page.Value.CanEdit);

        var submit =
            await h.Service.SubmitAsync(
                link.Value.Token,
                new SubmitInvoiceBuyerSelfServiceRequest
                {
                    BuyerType =
                        InvoiceBuyerTypes.Individual,
                    BuyerName = "Khách",
                    BuyerAddress = "Đồng Nai"
                });

        Assert.False(submit.IsSuccess);
        Assert.Equal("Conflict", submit.Error.Code);
        Assert.Equal(0, h.BuyerService.Calls);
    }

    [Fact]
    public async Task Route_changed_to_automatic_blocks_old_qr_post()
    {
        await using var h = CreateHarness();

        var link =
            await h.Service.CreateLinkAsync(100);

        var order =
            await h.Context.Orders
                .SingleAsync(x => x.Id == 100);

        order.InvoiceIssuanceRoute =
            InvoiceIssuanceRoute.Automatic;

        await h.Context.SaveChangesAsync();

        var submit =
            await h.Service.SubmitAsync(
                link.Value.Token,
                new SubmitInvoiceBuyerSelfServiceRequest
                {
                    BuyerType =
                        InvoiceBuyerTypes.Individual,
                    BuyerName = "Khách",
                    BuyerAddress = "Đồng Nai"
                });

        Assert.False(submit.IsSuccess);
        Assert.Equal("Conflict", submit.Error.Code);
        Assert.Equal(0, h.BuyerService.Calls);
    }

    [Fact]
    public async Task Invalid_token_returns_generic_not_found()
    {
        await using var h = CreateHarness();

        var result =
            await h.Service.GetAsync("not-a-valid-token");

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error.Code);
    }

    private static Harness CreateHarness(
        InvoiceIssuanceRoute route =
            InvoiceIssuanceRoute.Manual)
    {
        var tenant = new TestTenantContext();
        var currentUser = new TestCurrentUser();

        var options =
            new DbContextOptionsBuilder<InMemoryAppDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString())
                .Options;

        var db =
            new InMemoryAppDbContext(
                options,
                tenant,
                currentUser);

        db.VerifyRowVersionConfiguration();

        db.Orders.Add(
            new Order
            {
                Id = 100,
                StoreId = 1,
                POSShiftId = 1,
                Status = OrderStatus.Completed,
                CompletedAtUtc = SaleAtUtc,
                GrandTotal = 200_000m,
                OrderNumber = "ORD-100",
                InvoiceIssuanceRoute = route
            });

        db.InvoiceHeads.Add(
            new InvoiceHead
            {
                Id = 200,
                StoreId = 1,
                OrderId = 100,
                LegalEntityId = 1,
                ProviderStatus =
                    InvoiceProviderStatus.LocalDraft,

                BuyerType =
                    InvoiceBuyerTypes.NoInvoice,

                GrandTotal = 200_000m
            });

        db.SaveChanges();

        var requests =
            new FakeSelfServiceRepository(db);

        var buyer =
            new RecordingBuyerService(db);

        var clock =
            new MutableTimeProvider(
                SaleAtUtc.AddHours(1));

        var service =
            new InvoiceBuyerSelfServiceService(
                requests,
                new OrderRepository(db),
                new InvoiceRepository(db),
                buyer,
                new TestUnitOfWork(db),
                tenant,
                clock);

        return new Harness(
            db,
            requests,
            buyer,
            clock,
            service);
    }

    private sealed class Harness(
        InMemoryAppDbContext context,
        FakeSelfServiceRepository requests,
        RecordingBuyerService buyerService,
        MutableTimeProvider clock,
        InvoiceBuyerSelfServiceService service)
        : IAsyncDisposable
    {
        public InMemoryAppDbContext Context { get; } =
            context;

        public FakeSelfServiceRepository Requests { get; } =
            requests;

        public RecordingBuyerService BuyerService { get; } =
            buyerService;

        public MutableTimeProvider Clock { get; } =
            clock;

        public InvoiceBuyerSelfServiceService Service { get; } =
            service;

        public ValueTask DisposeAsync()
            => Context.DisposeAsync();
    }

    private sealed class FakeSelfServiceRepository(
        InMemoryAppDbContext db)
        : IInvoiceBuyerSelfServiceRepository
    {
        public List<InvoiceBuyerSelfServiceRequest> Items
        {
            get;
        } = [];

        public Task<InvoiceBuyerSelfServiceRequest?>
            GetByTokenHashAsync(
                byte[] tokenHash,
                CancellationToken ct = default)
        {
            var item =
                Items.FirstOrDefault(
                    x => x.TokenHash.SequenceEqual(
                        tokenHash));

            if (item != null)
            {
                item.Order =
                    db.Orders
                        .IgnoreQueryFilters()
                        .Single(x => x.Id == item.OrderId);
            }

            return Task.FromResult(item);
        }

        public Task AddAsync(
            InvoiceBuyerSelfServiceRequest request,
            CancellationToken ct = default)
        {
            request.Id = Items.Count + 1;
            Items.Add(request);

            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(
            CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingBuyerService(
        InMemoryAppDbContext db)
        : IInvoiceBuyerService
    {
        public int Calls { get; private set; }

        public UpdateInvoiceBuyerInfoRequest?
            LastRequest
        { get; private set; }

        public Task<Result<InvoiceBuyerLookupDto>>
            LookupBuyerByTaxCodeAsync(
                int invoiceHeadId,
                string buyerType,
                string taxCode,
                CancellationToken ct = default)
        {
            return Task.FromResult(
                Result<InvoiceBuyerLookupDto>.Failure(
                    Error.NotFound("Không dùng trong test.")));
        }

        public async Task<Result<InvoiceHeadDto>>
            UpdateBuyerInfoAsync(
                UpdateInvoiceBuyerInfoRequest request,
                CancellationToken ct = default)
        {
            Calls++;
            LastRequest = request;

            var first =
                await db.InvoiceHeads
                    .SingleAsync(
                        x => x.Id ==
                            request.InvoiceHeadId,
                        ct);

            var siblings =
                await db.InvoiceHeads
                    .Where(
                        x =>
                            x.OrderId == first.OrderId &&
                            x.OriginalInvoiceHeadId == null)
                    .ToListAsync(ct);

            foreach (var head in siblings)
            {
                head.BuyerType =
                    request.BuyerType;

                head.BuyerName =
                    request.BuyerName;

                head.BuyerLegalName =
                    request.BuyerLegalName;

                head.BuyerTaxCode =
                    request.BuyerTaxCode;

                head.BuyerCitizenId =
                    request.BuyerCitizenId;

                head.BuyerAddress =
                    request.BuyerAddress;

                head.BuyerEmail =
                    request.BuyerEmail;

                head.BuyerPhone =
                    request.BuyerPhone;
            }

            await db.SaveChangesAsync(ct);

            return Result<InvoiceHeadDto>.Success(
                new InvoiceHeadDto
                {
                    Id = first.Id,
                    StoreId = first.StoreId,
                    OrderId = first.OrderId,
                    BuyerType =
                        request.BuyerType,
                    BuyerName =
                        request.BuyerName,
                    BuyerLegalName =
                        request.BuyerLegalName,
                    BuyerTaxCode =
                        request.BuyerTaxCode,
                    BuyerCitizenId =
                        request.BuyerCitizenId,
                    BuyerAddress =
                        request.BuyerAddress,
                    BuyerEmail =
                        request.BuyerEmail,
                    BuyerPhone =
                        request.BuyerPhone
                });
        }
    }

    private sealed class TestUnitOfWork(
        InMemoryAppDbContext db)
        : IAppUnitOfWork
    {
        public Task<int> SaveChangesAsync(
            CancellationToken ct = default)
            => db.SaveChangesAsync(ct);

        public Task<IAppTransaction> BeginTransactionAsync(
            CancellationToken ct = default)
            => Task.FromResult<IAppTransaction>(
                new NoopTransaction());
    }

    private sealed class NoopTransaction
        : IAppTransaction
    {
        public Task CommitAsync(
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RollbackAsync(
            CancellationToken ct = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class TestTenantContext
        : ITenantContext
    {
        public int? StoreId => 1;

        public bool IsHostAdmin => false;

        public string? Subdomain => "test";
    }

    private sealed class TestCurrentUser
        : ICurrentUser
    {
        public int? UserId => 9;

        public string? UserName => "qr-test";

        public int? TerminalId => null;

        public string? TerminalCode => null;

        public bool IsAuthenticated => true;
    }

    private sealed class MutableTimeProvider(
        DateTime initialUtc)
        : TimeProvider
    {
        public DateTime NowUtc { get; set; } =
            initialUtc;

        public override DateTimeOffset GetUtcNow()
            => new(
                DateTime.SpecifyKind(
                    NowUtc,
                    DateTimeKind.Utc));
    }
}