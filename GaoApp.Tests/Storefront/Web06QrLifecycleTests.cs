using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06QrLifecycleTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Actual_received_QR_three_owners_posts_real_sale_and_fee_invoices_without_new_bank_income_or_stock_cost()
    {
        int feeOwner = 0, feeWarehouse = 0;
        var c = await Web06Case.CreateAsync(fixture, multipleSources: true, shipping: 30_000m, payment: StorefrontPaymentMethod.Qr,
            beforeQuote: async web =>
            {
                await using var db = web.Context(); var owner = new LegalEntity { StoreId = web.StoreId, Code = "W06-C", Name = "WEB06 service C", LegalName = "WEB06 service C", IsActive = true, SalePriority = 90 };
                db.Add(owner); await db.SaveChangesAsync(); feeOwner = owner.Id;
                var empty = new Warehouse { StoreId = web.StoreId, LegalEntityId = owner.Id, Code = "W06-FEE-EMPTY", Name = "WEB06 real empty service-owner warehouse", IsActive = true };
                db.Add(empty); await db.SaveChangesAsync(); feeWarehouse = empty.Id; owner.DefaultWarehouseId = empty.Id; await db.SaveChangesAsync();
                var tax = new Tax { StoreId = web.StoreId, Code = "W06-C", Name = "WEB06 fee tax", Rate = 8, IsActive = true }; db.Add(tax); await db.SaveChangesAsync();
                db.Add(new StorefrontServiceRevenueSetting { StoreId = web.StoreId, LegalEntityId = owner.Id, TaxId = tax.Id, Name = "WEB06 sealed fee C", InvoiceRoute = InvoiceIssuanceRoute.Automatic }); await db.SaveChangesAsync();
                await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(web);
            });
        var bankInspection = await Web06BankInspection.CaptureAsync(c);
        await Web06FinanceSupport.DeliveredAsync(c); var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c);
        await using (var db = c.Context())
        {
            var binding = System.Text.Json.JsonSerializer.Deserialize<StorefrontBankBinding>((await db.Set<StorefrontPaymentIntent>().SingleAsync()).BindingSnapshotJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            Assert.Equal(2, binding.SourceLegalEntityIds.Count); Assert.DoesNotContain(feeOwner, binding.SourceLegalEntityIds); Assert.Equal(feeOwner, binding.ServiceFee!.LegalEntityId); Assert.True(binding.ServiceFee.AuthorityId > 0);
            Assert.False(await db.Set<StorefrontFulfillmentSource>().AnyAsync(x => x.SourceLegalEntityId == feeOwner));
            Assert.False(await db.InventoryTransactions.AnyAsync(x => x.WarehouseId == feeWarehouse));
            Assert.All(await db.InventoryBalances.Where(x => x.WarehouseId == feeWarehouse).ToListAsync(), x => { Assert.Equal(0m, x.OnHandQty); Assert.Equal(0m, x.ReservedQty); });
            // Current product eligibility changes do not rewrite an accepted, eligible price/invoice snapshot.
            await db.ProductVariants.Where(x => x.Id == c.Seed.VariantId).ExecuteUpdateAsync(x => x.SetProperty(v => v.HasInputInvoice, false));
        }
        var request = await c.RequestAsync<StorefrontOperationRequest>();
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, request);
        await using var check = c.Context(); Assert.Single(await bankInspection.Receipts(check).ToListAsync()); Assert.Single(await bankInspection.Consumptions(check).ToListAsync()); await bankInspection.AssertDeltaAsync(check);
        Assert.Equal(230_000m, (await bankInspection.Receipts(check).SingleAsync()).Amount); Assert.Equal(230_000m, await check.OrderPayments.SumAsync(x => x.Amount) + await check.Set<StorefrontServiceFunding>().SumAsync(x => x.Amount));
        Assert.Equal(200_000m, (await check.Orders.SingleAsync()).GrandTotal); Assert.Equal(3, await check.InvoiceHeads.CountAsync()); Assert.Equal(230_000m, await check.InvoiceHeads.SumAsync(x => x.GrandTotal));
        var sale = await check.Orders.Include(x => x.Lines).SingleAsync(); Assert.All(sale.Lines, x => Assert.True(x.HasInputInvoiceSnapshot));
        Assert.Equal(55_000m, sale.Lines.Sum(x => x.LineCostTotal)); Assert.Equal(2, await check.InventoryTransactions.CountAsync(x => x.ReferenceType == InventoryReferenceType.Delivery));
        var shift = await check.POSShifts.SingleAsync(); Assert.Equal(0m, shift.NonCashSalesTotal); Assert.Equal(0m, shift.CashSalesTotal); Assert.Equal(0m, shift.CashInTotal); Assert.Equal(0m, shift.ClosingCashExpected);
        Assert.Empty(await check.POSShiftCashTransactions.ToListAsync()); Assert.Empty(await check.TreasuryEntries.ToListAsync());
        Assert.Equal(feeOwner, (await check.Set<StorefrontServiceRevenue>().SingleAsync()).LegalEntityId);
        Assert.False(await check.OrderLegalEntityAllocations.AnyAsync(x => x.LegalEntityId == feeOwner)); Assert.False(await check.InventoryTransactions.AnyAsync(x => x.WarehouseId == feeWarehouse));
        var reconciliation = await Web06FinanceSupport.ReconciliationAsync(c, terminal);
        Assert.Equal(0m, reconciliation.ExpectedCash); Assert.Equal(0m, reconciliation.DetailExpectedCash); Assert.Equal(0m, reconciliation.PaymentBreakdown.DetailNonCashSalesTotal);
        var visibleSale = Assert.Single(reconciliation.Orders); Assert.Equal(200_000m, visibleSale.GrandTotal); Assert.Equal(0m, visibleSale.CashApplied); Assert.Equal(0m, visibleSale.NonCashApplied);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Received_QR_then_real_extra_money_Review_then_failed_full_return_requests_refund_without_erasing_money(bool incident)
    {
        var c = await Web06Case.CreateAsync(fixture, payment: StorefrontPaymentMethod.Qr); await c.PrepareAsync(); await c.PickAllAsync(); await c.DispatchAsync();
        var bankInspection = await Web06BankInspection.CaptureAsync(c);
        c.QrTransport!.Pay(c.ProviderReference!, 1_000m); await c.QrCase!.ProcessAsync(c.IntentId!.Value, c.QrTransport);
        Assert.Equal(StorefrontPaymentState.Review, (await c.DetailAsync()).PaymentState);
        if (incident)
        {
            await using (var db = c.Context()) await c.Service(db).RecordIncidentAsync(c.OrderId, await c.IncidentRequestAsync());
            int id; await using (var db = c.Context()) id = (await db.Set<StorefrontCustodyIncident>().SingleAsync()).Id;
            await using (var db = c.Context()) await c.Service(db).ReconcileIncidentAsync(c.OrderId, await c.ReconcileRequestAsync(id, StorefrontIncidentResolution.AllGoodsRecoveredAtSource));
        }
        else
        {
            await c.OutcomeAllAsync(false); await using var db = c.Context(); await c.Service(db).ReceiveReturnAsync(c.OrderId, await c.ReturnRequestAsync());
        }
        await using var check = c.Context(); var parent = await check.StorefrontOrders.SingleAsync();
        Assert.Equal(StorefrontRefundState.Requested, parent.RefundState); Assert.Equal(StorefrontPaymentState.Review, parent.PaymentState);
        Assert.Equal(201_000m, await bankInspection.Receipts(check).SumAsync(x => x.Amount)); Assert.Single(await bankInspection.Consumptions(check).ToListAsync()); await bankInspection.AssertDeltaAsync(check, receipts: 1, amount: 1_000m);
        Assert.Equal(10m, (await check.InventoryBalances.SingleAsync()).OnHandQty); Assert.Equal(4m, await check.DeliveryReturnedCostFragments.SumAsync(x => x.BaseQuantity));
        Assert.Empty(await check.Orders.ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Real_authenticated_wrong_beneficiary_evidence_stops_both_normal_and_incident_indirect_dispatch(bool incidentEntry)
    {
        var c = await Web06Case.CreateAsync(fixture, payment: StorefrontPaymentMethod.Qr); await c.PrepareAsync(); await c.PickAllAsync();
        if (incidentEntry) { await using var db = c.Context(); await c.Service(db).RecordIncidentAsync(c.OrderId, await c.IncidentRequestAsync()); }
        c.QrTransport!.BeneficiaryOverride = "UNRELATED-BENEFICIARY"; c.QrTransport.Pay(c.ProviderReference!, 1_000m);
        await c.QrCase!.ProcessAsync(c.IntentId!.Value, c.QrTransport);
        await using (var db = c.Context()) Assert.NotEmpty(await db.Set<StorefrontUnattributedBankReceipt>().ToListAsync());
        var before = await c.PhysicalSnapshotAsync();
        var dispatchRequest = await c.DispatchRequestAsync();
        await using (var db = c.Context())
        {
            if (!incidentEntry) await Assert.ThrowsAsync<ConflictAppException>(() => c.Service(db).DispatchAsync(c.OrderId, dispatchRequest));
            else
            {
                var id = (await db.Set<StorefrontCustodyIncident>().SingleAsync()).Id;
                var request = await c.ReconcileRequestAsync(id, StorefrontIncidentResolution.AllGoodsHandedToCarrier);
                await Assert.ThrowsAsync<ConflictAppException>(() => c.Service(db).ReconcileIncidentAsync(c.OrderId, request));
            }
        }
        Assert.Equal(before, await c.PhysicalSnapshotAsync()); await using var check = c.Context();
        Assert.Empty(await check.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Delivery).ToListAsync()); Assert.Empty(await check.DeliveryJournalEntries.ToListAsync());
    }

    [Fact]
    public async Task New_QR_ineligible_goods_are_rejected_before_any_order_hold_or_intent_while_COD_does_not_invent_goods_invoice_eligibility()
    {
        Web04Case? captured = null;
        await Assert.ThrowsAsync<ValidationAppException>(() => Web06Case.CreateAsync(fixture, payment: StorefrontPaymentMethod.Qr, hasInputInvoice: false, beforeQuote: web => { captured = web; return Task.CompletedTask; }));
        Assert.NotNull(captured); await using (var db = captured.Context()) { Assert.Empty(await db.StorefrontOrders.ToListAsync()); Assert.Empty(await db.InventoryReservations.ToListAsync()); Assert.Empty(await db.Set<StorefrontPaymentIntent>().ToListAsync()); }
        var c = await Web06Case.CreateAsync(fixture, hasInputInvoice: false, shipping: 30_000m);
        await Web06FinanceSupport.ServiceSettingAsync(c); await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, 230_000m));
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, await c.RequestAsync<StorefrontOperationRequest>());
        await using var check = c.Context(); Assert.False((await check.OrderLines.SingleAsync()).HasInputInvoiceSnapshot); Assert.Equal(200_000m, (await check.Orders.SingleAsync()).GrandTotal);
        var invoice = Assert.Single(await check.InvoiceHeads.ToListAsync()); Assert.NotNull(invoice.StorefrontServiceRevenueId); Assert.Equal(30_000m, invoice.GrandTotal); Assert.NotNull((await check.Set<StorefrontInvoiceWork>().SingleAsync()).CompletedAtUtc);
    }
}
