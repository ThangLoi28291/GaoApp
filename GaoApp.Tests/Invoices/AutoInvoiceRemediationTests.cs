using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Invoices;

public sealed class AutoInvoiceRemediationTests
{
    [Theory]
    [InlineData(null, false, "Individual", "111", "222")]
    [InlineData(" 333 ", false, "Individual", "333", "333")]
    [InlineData(null, true, "Individual", null, null)]
    [InlineData("", false, "Individual", null, null)]
    [InlineData(null, false, "NoInvoice", null, null)]
    public async Task Staff_buyer_edit_preserves_omitted_citizen_ids_and_supports_explicit_clearing(
        string? citizenId, bool clear, string buyerType, string? expectedFirst, string? expectedSibling)
    {
        await using var db = CreateDb();
        var first = Head(1);
        first.BuyerCitizenId = "111";
        var sibling = Head(2);
        sibling.BuyerCitizenId = "222";
        var issued = Head(3);
        issued.BuyerCitizenId = "444";
        issued.ProviderStatus = InvoiceProviderStatus.Issued;
        db.InvoiceHeads.AddRange(first, sibling, issued);
        await db.SaveChangesAsync();
        var service = new InvoiceBuyerService(new InvoiceRepository(db), null!, null!, null!);

        var result = await service.UpdateBuyerInfoAsync(new UpdateInvoiceBuyerInfoRequest
        {
            InvoiceHeadId = 1, BuyerType = buyerType, BuyerName = "Updated buyer",
            BuyerAddress = "Updated address", BuyerPhone = "0900000000", SaveToProfile = false,
            BuyerCitizenId = citizenId, ClearBuyerCitizenId = clear
        });

        Assert.True(result.IsSuccess, result.Error?.Message);
        db.ChangeTracker.Clear();
        Assert.Equal(expectedFirst, (await db.InvoiceHeads.SingleAsync(x => x.Id == 1)).BuyerCitizenId);
        Assert.Equal(expectedSibling, (await db.InvoiceHeads.SingleAsync(x => x.Id == 2)).BuyerCitizenId);
        Assert.Equal("444", (await db.InvoiceHeads.SingleAsync(x => x.Id == 3)).BuyerCitizenId);
    }

    [Fact]
    public async Task Claim_repository_reads_manual_correction_and_original_fresh_but_excludes_it_from_group_claims()
    {
        await using var db = CreateDb();
        var original = Head(1);
        original.ProviderStatus = InvoiceProviderStatus.Issued;
        var correction = Head(2);
        correction.OriginalInvoiceHeadId = 1;
        correction.CorrectionType = InvoiceCorrectionType.Replacement;
        db.InvoiceHeads.AddRange(original, correction);
        await db.SaveChangesAsync();
        // Unsaved tracked data must never replace the database snapshot used for claiming.
        correction.ProviderStatus = InvoiceProviderStatus.Issued;
        original.StoreId = 2;
        var repository = new AutoInvoiceRepository(db);

        var claim = await repository.GetInvoiceHeadForManualClaimAsync(1, 2);

        Assert.NotNull(claim);
        Assert.Equal(InvoiceCorrectionType.Replacement, claim.CorrectionType);
        Assert.Equal(InvoiceProviderStatus.LocalDraft, claim.ProviderStatus);
        Assert.Equal(1, claim.OriginalInvoiceHead!.StoreId);
        Assert.Null(await repository.GetInvoiceHeadForManualClaimAsync(2, 2));
        Assert.Null(await repository.GetInvoiceHeadForClaimAsync(1, 2));
        Assert.Empty(await repository.GetInvoiceHeadsForClaimAsync(1, new[] { 2 }));
    }

    [Fact]
    public void Correction_toolbar_keeps_manual_post_permission_and_allows_correction_payloads()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "GaoApp.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var toolbar = File.ReadAllText(Path.Combine(root.FullName, "GaoApp.Web/Areas/Admin/Views/Invoice/_ViettelActionToolbar.cshtml"));
        Assert.Contains("(isManualRoute || isCorrectionPayload)", toolbar);
        Assert.Contains("isAutomaticRoute && !isCorrectionPayload", toolbar);
        var controller = File.ReadAllText(Path.Combine(root.FullName, "GaoApp.Web/Areas/Admin/Controllers/InvoiceController.Viettel.cs"));
        Assert.Contains("Policy = PermissionCodes.System.Invoice.ManualIssue", controller);
        Assert.Contains("_autoInvoiceService.IssueManualAsync(", controller);
    }

    [Theory]
    [InlineData("queue")]
    [InlineData("single")]
    [InlineData("group")]
    public async Task Cash_payment_routing_repository_loads_fresh_payments_without_tracking_fixup(string query)
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        db.Stores.Add(new Store { Id = 1, Name = "Test", SubDomain = "test", SubDomainNormalized = "TEST" });
        var shift = new POSShift
        {
            StoreId = 1, OpenedByUserId = 9, OpenedAtUtc = now.AddHours(-1),
            Terminal = new POSTerminal { StoreId = 1, Code = "POS-REMEDIATION", Name = "Remediation POS" },
            Warehouse = new Warehouse
            {
                StoreId = 1, Code = "WH-REMEDIATION", Name = "Remediation warehouse",
                LegalEntity = new LegalEntity
                {
                    StoreId = 1, Code = "LEGAL-REMEDIATION", Name = "Remediation owner", LegalName = "Remediation owner"
                }
            }
        };
        db.POSShifts.Add(shift);
        await db.SaveChangesAsync();
        var head = Head(1);
        head.LegalEntityId = null;
        head.InvoiceDate = now;
        head.Order = new Order
        {
            Id = 100, StoreId = 1, CompletedAtUtc = now, POSShiftId = shift.Id, POSShift = shift,
            InvoiceIssuanceRoute = InvoiceIssuanceRoute.Automatic,
            GrandTotal = 60_000m,
            Payments = [new OrderPayment
            {
                Id = 1, StoreId = 1, OrderId = 100, Method = PaymentMethod.BankTransfer, Amount = 60_000m
            }]
        };
        db.InvoiceHeads.Add(head);
        await db.SaveChangesAsync();
        // The tracked order now looks cash-only, but the persisted payment is bank.
        head.Order.Payments.Single().Method = PaymentMethod.Cash;
        var repository = new AutoInvoiceRepository(db);
        var fresh = query switch
        {
            "queue" => Assert.Single(await repository.GetCandidateInvoicesAsync(1, now.Date, now.Date.AddDays(1))),
            "single" => await repository.GetInvoiceHeadForClaimAsync(1, head.Id),
            _ => Assert.Single(await repository.GetInvoiceHeadsForClaimAsync(1, new[] { head.Id }))
        };
        Assert.NotNull(fresh);
        Assert.NotSame(head.Order, fresh.Order);
        Assert.Equal(PaymentMethod.BankTransfer, Assert.Single(fresh.Order!.Payments).Method);
        Assert.Equal(PaymentMethod.Cash, head.Order.Payments.Single().Method);
    }

    private static InvoiceHead Head(int id) => new()
    {
        Id = id, StoreId = 1, OrderId = 100, LegalEntityId = id,
        BuyerType = InvoiceBuyerTypes.Individual, BuyerName = "Buyer", BuyerAddress = "Address",
        ProviderStatus = InvoiceProviderStatus.LocalDraft
    };

    private static InMemoryAppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<InMemoryAppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Tenant(), new User());

    private sealed class Tenant : ITenantContext
    {
        public int? StoreId => 1;
        public bool IsHostAdmin => false;
        public string? Subdomain => "test";
    }

    private sealed class User : ICurrentUser
    {
        public int? UserId => 9;
        public string? UserName => "remediation-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
