using System.Reflection;
using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.LegalEntities;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GaoApp.Tests.Inventory;

[Collection("R1FinalDatabasePreflight")]
// R2.4-C2 coverage: serialized owner-safe link/unlink and post-confirm metadata-only behavior.
// R2.4-C2 coverage: serialized owner-safe link/unlink and post-confirm metadata-only behavior.
public sealed class InputInvoicePickerLinkUnlinkSqlServerTests
{
    [Fact]
    public async Task Confirmed_late_link_unlink_and_atomic_relink_preserve_non_zero_posted_effects()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await database.SeedInventoryCatalogAsync();
        var seed = await SeedAsync(database, catalog);
        await SeedGenuinePostedEffectsAsync(database, catalog, seed);
        var before = await SnapshotAsync(database, seed);
        before.InventoryTransactions.Should().BeGreaterThan(0);
        before.InventoryBalances.Should().BeGreaterThan(0);
        before.InventoryValuationEntries.Should().BeGreaterThan(0);
        before.InventoryCostLayers.Should().BeGreaterThan(0);
        before.InventoryCostLayerAllocations.Should().BeGreaterThan(0);
        before.PurchasePayables.Should().BeGreaterThan(0);
        before.PurchaseOrderReceivedQuantity.Should().BeGreaterThan(0);

        await using (var unlinkContext = CreateContext(database, seed.StoreId))
        {
            var result = await CreatePicker(unlinkContext).UnlinkAsync(
                seed.StoreId, seed.ReceiptId, new UnlinkInputInvoiceRequest
                {
                    ExpectedCurrentInputInvoiceHeadId = seed.TargetInvoiceId,
                    Reason = "XML ban đầu không đúng"
            });
            result.Outcome.Should().Be(InputInvoiceAssociationMutationOutcomes.Applied);
            result.Association.LifecycleState.Should().Be(
                InputInvoiceAssociationLifecycleStates.WaitingXml);
            result.Association.IsWaitingXml.Should().BeTrue();
        }

        await using (var linkContext = CreateContext(database, seed.StoreId))
        {
            var result = await CreateXmlService(linkContext).ImportAndLinkAsync(
                seed.StoreId,
                seed.ReceiptId,
                (await linkContext.StockDocuments.AsNoTracking()
                    .Where(x => x.Id == seed.ReceiptId)
                    .Select(x => x.SupplierId)
                    .SingleAsync())!.Value,
                "0312770607",
                "late-9001.xml",
                BuildInvoiceXml("9001"));
            result.InputInvoiceHeadId.Should().Be(seed.TargetInvoiceId);
        }

        await using (var blankUnlinkContext = CreateContext(database, seed.StoreId))
        {
            var blank = () => CreatePicker(blankUnlinkContext).UnlinkAsync(
                seed.StoreId, seed.ReceiptId, new UnlinkInputInvoiceRequest
                {
                    ExpectedCurrentInputInvoiceHeadId = seed.TargetInvoiceId,
                    Reason = "   "
                });
            await blank.Should().ThrowAsync<BusinessRuleException>()
                .WithMessage("*lý do*");
        }

        await using (var staleContext = CreateContext(database, seed.StoreId))
        {
            var stale = () => CreateRelinkPicker(staleContext, "9002").RelinkAsync(
                seed.StoreId, seed.ReceiptId, new RelinkInputInvoiceRequest
                {
                    DocumentKey = "replacement-9002",
                    ExpectedCurrentInputInvoiceHeadId = seed.TargetInvoiceId + 999,
                    Reason = "Yêu cầu stale"
                });
            await stale.Should().ThrowAsync<InputInvoiceAssociationException>()
                .Where(x => x.Code == InputInvoiceAssociationException.AssociationChangedCode);
        }
        await using (var verifyStale = CreateContext(database, seed.StoreId))
        {
            (await verifyStale.StockDocumentInputInvoiceMaps.SingleAsync(x =>
                x.StockDocumentId == seed.ReceiptId)).InputInvoiceHeadId
                .Should().Be(seed.TargetInvoiceId);
        }

        await using (var blankContext = CreateContext(database, seed.StoreId))
        {
            var blank = () => CreateRelinkPicker(blankContext, "9002").RelinkAsync(
                seed.StoreId, seed.ReceiptId, new RelinkInputInvoiceRequest
                {
                    DocumentKey = "replacement-9002",
                    ExpectedCurrentInputInvoiceHeadId = seed.TargetInvoiceId,
                    Reason = "   "
                });
            await blank.Should().ThrowAsync<BusinessRuleException>()
                .WithMessage("*lý do*");
        }

        await using (var relinkContext = CreateContext(database, seed.StoreId))
        {
            var result = await CreateRelinkPicker(relinkContext, "9002").RelinkAsync(
                seed.StoreId, seed.ReceiptId, new RelinkInputInvoiceRequest
                {
                    DocumentKey = "replacement-9002",
                    ExpectedCurrentInputInvoiceHeadId = seed.TargetInvoiceId,
                    Reason = "Thay bằng XML đúng"
                });
            result.Outcome.Should().Be(InputInvoiceAssociationMutationOutcomes.Applied);
            result.Association.CurrentInputInvoiceHeadId.Should().Be(seed.OtherInvoiceId);
            result.Association.IsConfirmed.Should().BeTrue();
        }

        await using (var verify = CreateContext(database, seed.StoreId))
        {
            var active = await verify.StockDocumentInputInvoiceMaps
                .SingleAsync(x => x.StockDocumentId == seed.ReceiptId);
            active.InputInvoiceHeadId.Should().Be(seed.OtherInvoiceId);
            var audit = await verify.PurchaseReceiptAuditEvents.SingleAsync(x =>
                x.StockDocumentId == seed.ReceiptId &&
                x.EventType == PurchaseReceiptAuditEventType.InputInvoiceRelinked);
            audit.Reason.Should().Be("Thay bằng XML đúng");
            audit.OldValuesJson.Should().Contain(seed.TargetInvoiceId.ToString());
            audit.NewValuesJson.Should().Contain(seed.OtherInvoiceId.ToString());
            (await CreateReconciliation(new InputInvoiceRepository(verify))
                .GetForReceiptAsync(seed.StoreId, seed.ReceiptId))
                .IsConfirmedReadOnly.Should().BeTrue();
        }

        var after = await SnapshotAsync(database, seed);
        after.Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Blocked_link_persists_attempted_identity_without_fake_invoice_id()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await database.SeedInventoryCatalogAsync();
        var seed = await SeedLinkReceiptAsync(database, catalog);

        await using (var context = CreateContext(database, seed.StoreId))
        {
            var action = () => CreateXmlService(context).ImportAndLinkAsync(
                seed.StoreId,
                seed.ReceiptId,
                seed.SupplierId,
                "0312770607",
                "blocked.xml",
                BuildInvoiceXml("9499", "999-888 777"));

            await action.Should().ThrowAsync<GaoApp.Application.Common.Exceptions.InputInvoiceOwnerGuardException>();
        }

        await using var verify = CreateContext(database, seed.StoreId);
        (await verify.StockDocumentInputInvoiceMaps.CountAsync(x =>
            x.StockDocumentId == seed.ReceiptId)).Should().Be(0);
        (await verify.InputInvoiceHeads.CountAsync(x =>
            x.InvoiceNumber == "9499")).Should().Be(0);
        var audit = await verify.PurchaseReceiptAuditEvents.SingleAsync(x =>
            x.StockDocumentId == seed.ReceiptId &&
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceOwnerLinkBlocked);
        audit.ActorUserId.Should().Be(602);
        audit.OccurredAtUtc.Should().NotBe(default);
        using var payload = JsonDocument.Parse(audit.NewValuesJson!);
        var root = payload.RootElement;
        root.GetProperty("InputInvoiceHeadId").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("BuyerTaxCode").GetString().Should().Be("999-888 777");
        root.GetProperty("NormalizedBuyerTaxCode").GetString().Should().Be("999888777");
        root.GetProperty("NormalizedSellerTaxCode").GetString().Should().Be("0312770607");
        root.GetProperty("NormalizedInvoiceSeries").GetString().Should().Be("C26MVP");
        root.GetProperty("NormalizedInvoiceNumber").GetString().Should().Be("9499");
        root.GetProperty("InvoiceIdentityDate").GetDateTime().Should().Be(new DateTime(2026, 8, 22));
        root.GetProperty("ResolutionStatus").GetString().Should().Be("NotFound");
        root.GetProperty("ReasonCode").GetString().Should().Be("NotFound");
        root.GetProperty("ReceiptOwnerLegalEntityId").GetInt32().Should().Be(seed.LegalEntityId);
        root.GetProperty("InvoiceOwnerLegalEntityId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task One_receipt_rejects_a_different_invoice_but_same_invoice_can_link_another_receipt()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await database.SeedInventoryCatalogAsync();
        var first = await SeedLinkReceiptAsync(database, catalog);
        var second = await SeedLinkReceiptAsync(database, catalog);

        int invoiceId;
        await using (var context = CreateContext(database, first.StoreId))
        {
            var service = CreateXmlService(context);
            invoiceId = (await service.ImportAndLinkAsync(
                first.StoreId, first.ReceiptId, first.SupplierId,
                "0312770607", "same.xml", BuildInvoiceXml("9401"))).InputInvoiceHeadId;

            var different = () => service.ImportAndLinkAsync(
                first.StoreId, first.ReceiptId, first.SupplierId,
                "0312770607", "different.xml", BuildInvoiceXml("9402"));
            await different.Should().ThrowAsync<GaoApp.Application.Common.Exceptions.BusinessRuleException>()
                .WithMessage("*gỡ liên kết trước*");
        }

        await using (var context = CreateContext(database, second.StoreId))
        {
            var linked = await CreateXmlService(context).ImportAndLinkAsync(
                second.StoreId, second.ReceiptId, second.SupplierId,
                "0312770607", "same.xml", BuildInvoiceXml("9401"));
            linked.InputInvoiceHeadId.Should().Be(invoiceId);
        }

        await using var verify = CreateContext(database, first.StoreId);
        (await verify.StockDocumentInputInvoiceMaps.CountAsync(x =>
                x.InputInvoiceHeadId == invoiceId))
            .Should().Be(2);
        (await verify.StockDocumentInputInvoiceMaps.CountAsync(x =>
                x.StockDocumentId == first.ReceiptId))
            .Should().Be(1);
    }

    [Fact]
    public async Task Unlink_then_relink_same_invoice_should_reactivate_soft_deleted_maps()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await database.SeedInventoryCatalogAsync();
        var seed = await SeedLinkReceiptAsync(database, catalog);
        var xmlBytes = BuildInvoiceXml("9201");

        int invoiceId;
        int detailId;
        int headerMapId;
        int lineMapId;
        await using (var linkContext = CreateContext(database, seed.StoreId))
        {
            var firstLink = await CreateXmlService(linkContext).ImportAndLinkAsync(
                seed.StoreId, seed.ReceiptId, seed.SupplierId,
                "0312770607", "invoice.xml", xmlBytes);
            invoiceId = firstLink.InputInvoiceHeadId;
            detailId = await linkContext.InputInvoiceDetails
                .Where(x => x.InputInvoiceHeadId == invoiceId)
                .Select(x => x.Id)
                .SingleAsync();
            var lineMap = await linkContext.StockDocumentLineInputInvoiceMaps
                .SingleAsync(x => x.StockDocumentId == seed.ReceiptId);
            lineMap.InputInvoiceDetailId = detailId;
            lineMap.UseInputInvoice = true;
            await linkContext.SaveChangesAsync();
            headerMapId = await linkContext.StockDocumentInputInvoiceMaps
                .Where(x => x.StockDocumentId == seed.ReceiptId &&
                            x.InputInvoiceHeadId == invoiceId)
                .Select(x => x.Id)
                .SingleAsync();
            lineMapId = lineMap.Id;
        }

        await using (var unlinkContext = CreateContext(database, seed.StoreId))
        {
            (await CreatePicker(unlinkContext).UnlinkAsync(
                seed.StoreId, seed.ReceiptId, invoiceId)).Should().BeTrue();
        }

        await using (var deletedState = CreateContext(database, seed.StoreId))
        {
            (await deletedState.StockDocumentInputInvoiceMaps
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == headerMapId)).IsDeleted.Should().BeTrue();
            (await deletedState.StockDocumentLineInputInvoiceMaps
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == lineMapId)).IsDeleted.Should().BeTrue();
        }

        await using (var relinkContext = CreateContext(database, seed.StoreId))
        {
            var relink = await CreateXmlService(relinkContext).ImportAndLinkAsync(
                seed.StoreId, seed.ReceiptId, seed.SupplierId,
                "0312770607", "invoice.xml", xmlBytes);
            relink.InputInvoiceHeadId.Should().Be(invoiceId);
            relink.WasAlreadyLinked.Should().BeFalse();
            var retry = await CreateXmlService(relinkContext).ImportAndLinkAsync(
                seed.StoreId, seed.ReceiptId, seed.SupplierId,
                "0312770607", "invoice.xml", xmlBytes);
            retry.WasAlreadyLinked.Should().BeTrue();
        }

        await using (var verify = CreateContext(database, seed.StoreId))
        {
            var headers = await verify.StockDocumentInputInvoiceMaps
                .IgnoreQueryFilters()
                .Where(x => x.StockDocumentId == seed.ReceiptId &&
                            x.InputInvoiceHeadId == invoiceId)
                .ToListAsync();
            headers.Should().ContainSingle();
            headers.Single().Id.Should().Be(headerMapId);
            headers.Single().IsDeleted.Should().BeFalse();
            headers.Single().DeletedAtUtc.Should().BeNull();
            headers.Single().DeletedBy.Should().BeNull();
            var lines = await verify.StockDocumentLineInputInvoiceMaps
                .IgnoreQueryFilters()
                .Where(x => x.StockDocumentId == seed.ReceiptId)
                .ToListAsync();
            lines.Should().ContainSingle();
            lines.Single().Id.Should().Be(lineMapId);
            lines.Single().IsDeleted.Should().BeFalse();
            lines.Single().DeletedAtUtc.Should().BeNull();
            lines.Single().DeletedBy.Should().BeNull();
            lines.Single().UseInputInvoice.Should().BeFalse();
            lines.Single().InputInvoiceDetailId.Should().BeNull();
            lines.Single().MatchStatus.Should().Be(InputInvoiceMatchStatus.None);
            (await verify.InputInvoiceHeads.CountAsync(x => x.Id == invoiceId))
                .Should().Be(1);
            (await verify.InputInvoiceDetails.CountAsync(x =>
                x.InputInvoiceHeadId == invoiceId)).Should().Be(1);
            (await verify.PurchaseReceiptAuditEvents.CountAsync(x =>
                x.StockDocumentId == seed.ReceiptId &&
                x.EventType == PurchaseReceiptAuditEventType.InputInvoiceLinked))
                .Should().Be(2);
            (await verify.PurchaseReceiptAuditEvents.CountAsync(x =>
                x.StockDocumentId == seed.ReceiptId &&
                x.EventType == PurchaseReceiptAuditEventType.InputInvoiceUnlinked))
                .Should().Be(1);
        }

        await using (var finalUnlinkContext = CreateContext(database, seed.StoreId))
        {
            (await CreatePicker(finalUnlinkContext).UnlinkAsync(
                seed.StoreId, seed.ReceiptId, invoiceId)).Should().BeTrue();
            (await CreatePicker(finalUnlinkContext).UnlinkAsync(
                seed.StoreId, seed.ReceiptId, invoiceId)).Should().BeFalse();
        }

        await using var finalVerify = CreateContext(database, seed.StoreId);
        (await finalVerify.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.StockDocumentId == seed.ReceiptId &&
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceUnlinked))
            .Should().Be(2);
    }

    [Fact]
    public async Task Concurrent_links_should_create_one_map_and_one_semantic_audit()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await database.SeedInventoryCatalogAsync();
        var seed = await SeedLinkReceiptAsync(database, catalog);
        var xmlBytes = System.Text.Encoding.UTF8.GetBytes("""
            <HDon><DLHDon><TTChung><KHMSHDon>1</KHMSHDon><KHHDon>C26MVP</KHHDon><SHDon>9101</SHDon><NLap>2026-08-22</NLap></TTChung><NDHDon><NBan><Ten>Supplier</Ten><MST>0312770607</MST></NBan><NMua><Ten>Buyer</Ten><MST>0101234567</MST></NMua><DSHHDVu><HHDVu><STT>1</STT><THHDVu>Product</THHDVu><DVTinh>Unit</DVTinh><SLuong>2</SLuong><DGia>5</DGia><ThTien>10</ThTien></HHDVu></DSHHDVu><TToan><TgTCThue>10</TgTCThue><TgTThue>0</TgTThue><TgTTTBSo>10</TgTTTBSo></TToan></NDHDon></DLHDon></HDon>
            """);

        await using var firstContext = CreateContext(database, seed.StoreId);
        await using var secondContext = CreateContext(database, seed.StoreId);
        var results = await Task.WhenAll(
            CreateXmlService(firstContext).ImportAndLinkAsync(
                seed.StoreId, seed.ReceiptId, seed.SupplierId,
                "0312770607", "invoice.xml", xmlBytes),
            CreateXmlService(secondContext).ImportAndLinkAsync(
                seed.StoreId, seed.ReceiptId, seed.SupplierId,
                "0312770607", "invoice.xml", xmlBytes));

        results.Count(x => x.WasAlreadyLinked).Should().Be(1);
        results.Count(x => !x.WasAlreadyLinked).Should().Be(1);
        results.Select(x => x.InputInvoiceHeadId).Distinct().Should().ContainSingle();

        await using var verify = CreateContext(database, seed.StoreId);
        (await verify.StockDocumentInputInvoiceMaps.CountAsync(x =>
            x.StockDocumentId == seed.ReceiptId)).Should().Be(1);
        (await verify.StockDocumentLineInputInvoiceMaps.CountAsync(x =>
            x.StockDocumentId == seed.ReceiptId)).Should().Be(1);
        (await verify.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.StockDocumentId == seed.ReceiptId &&
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceLinked))
            .Should().Be(1);
    }

    [Fact]
    public async Task Linked_pending_receipt_rejects_Supplier_change_without_relation_or_posting_mutation()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await database.SeedInventoryCatalogAsync();
        var seed = await SeedLinkReceiptAsync(database, catalog);
        var replacementSupplierId = await AddReplacementSupplierAsync(database, seed.StoreId);
        var xmlBytes = BuildInvoiceXml("9301");

        int invoiceId;
        await using (var linkContext = CreateContext(database, seed.StoreId))
        {
            var result = await CreateXmlService(linkContext).ImportAndLinkAsync(
                seed.StoreId, seed.ReceiptId, seed.SupplierId,
                "0312770607", "invoice.xml", xmlBytes);
            invoiceId = result.InputInvoiceHeadId;
        }
        var before = await PendingSnapshotAsync(database, seed.StoreId, seed.ReceiptId);

        await using (var changeContext = CreateContext(database, seed.StoreId))
        {
            var service = CreateHeaderService(changeContext);
            var action = () => service.UpdateHeaderAsync(new UpdateStockDocumentHeaderRequest
            {
                StockDocumentId = seed.ReceiptId,
                LegalEntityId = seed.LegalEntityId,
                WarehouseId = catalog.WarehouseId,
                SupplierId = replacementSupplierId
            });

            await action.Should().ThrowAsync<GaoApp.Application.Common.Exceptions.BusinessRuleException>()
                .WithMessage("Phiếu đang liên kết hóa đơn đầu vào. Vui lòng gỡ liên kết hóa đơn trước khi thay đổi nhà cung cấp.");
        }

        await using var verify = CreateContext(database, seed.StoreId);
        (await verify.StockDocuments.AsNoTracking()
            .SingleAsync(x => x.Id == seed.ReceiptId)).SupplierId.Should().Be(seed.SupplierId);
        (await verify.StockDocumentInputInvoiceMaps.CountAsync(x =>
            x.StockDocumentId == seed.ReceiptId &&
            x.InputInvoiceHeadId == invoiceId)).Should().Be(1);
        (await verify.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.StockDocumentId == seed.ReceiptId &&
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceUnlinked)).Should().Be(0);
        var after = await PendingSnapshotAsync(database, seed.StoreId, seed.ReceiptId);
        after.Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Concurrent_Supplier_change_and_link_converge_without_same_Supplier_violation()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await database.SeedInventoryCatalogAsync();
        var seed = await SeedLinkReceiptAsync(database, catalog);
        var replacementSupplierId = await AddReplacementSupplierAsync(database, seed.StoreId);
        var before = await PendingSnapshotAsync(database, seed.StoreId, seed.ReceiptId);
        var xmlBytes = BuildInvoiceXml("9302");

        await using var changeContext = CreateContext(database, seed.StoreId);
        await using var linkContext = CreateContext(database, seed.StoreId);
        var changeService = CreateHeaderService(changeContext);
        var linkService = CreateXmlService(linkContext);

        var changeTask = CaptureAsync(() => changeService.UpdateHeaderAsync(
            new UpdateStockDocumentHeaderRequest
            {
                StockDocumentId = seed.ReceiptId,
                LegalEntityId = seed.LegalEntityId,
                WarehouseId = catalog.WarehouseId,
                SupplierId = replacementSupplierId
            }));
        var linkTask = CaptureAsync(async () =>
        {
            await linkService.ImportAndLinkAsync(
                seed.StoreId, seed.ReceiptId, seed.SupplierId,
                "0312770607", "invoice.xml", xmlBytes);
        });

        var outcomes = await Task.WhenAll(changeTask, linkTask);
        outcomes.Count(x => x is null).Should().Be(1);
        outcomes.Count(x => x is GaoApp.Application.Common.Exceptions.BusinessRuleException)
            .Should().Be(1);

        await using var verify = CreateContext(database, seed.StoreId);
        var receipt = await verify.StockDocuments.AsNoTracking()
            .SingleAsync(x => x.Id == seed.ReceiptId);
        var activeLinks = await verify.StockDocumentInputInvoiceMaps
            .Where(x => x.StockDocumentId == seed.ReceiptId)
            .Include(x => x.InputInvoiceHead)
            .ToListAsync();
        if (receipt.SupplierId == replacementSupplierId)
        {
            activeLinks.Should().BeEmpty();
        }
        else
        {
            receipt.SupplierId.Should().Be(seed.SupplierId);
            activeLinks.Should().ContainSingle();
            activeLinks.Single().InputInvoiceHead.ResolvedSupplierId
                .Should().Be(seed.SupplierId);
        }
        (await verify.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.StockDocumentId == seed.ReceiptId &&
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceUnlinked)).Should().Be(0);

        var after = await PendingSnapshotAsync(database, seed.StoreId, seed.ReceiptId);
        after.Status.Should().Be(before.Status);
        after.Lines.Should().BeEquivalentTo(before.Lines);
        after.InventoryTransactions.Should().Be(before.InventoryTransactions);
        after.InventoryBalances.Should().Be(before.InventoryBalances);
        after.InventoryValuationEntries.Should().Be(before.InventoryValuationEntries);
        after.InventoryCostLayers.Should().Be(before.InventoryCostLayers);
        after.InventoryCostLayerAllocations.Should().Be(before.InventoryCostLayerAllocations);
        after.PurchasePayables.Should().Be(before.PurchasePayables);
    }

    [Fact]
    public async Task Concurrent_post_confirm_unlink_should_be_idempotent_and_metadata_only()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await database.SeedInventoryCatalogAsync();
        var seed = await SeedAsync(database, catalog);
        var before = await SnapshotAsync(database, seed);

        await using var firstContext = CreateContext(database, seed.StoreId);
        await using var secondContext = CreateContext(database, seed.StoreId);
        var outcomes = await Task.WhenAll(
            CreatePicker(firstContext).UnlinkAsync(
                seed.StoreId, seed.ReceiptId, new UnlinkInputInvoiceRequest
                {
                    ExpectedCurrentInputInvoiceHeadId = seed.TargetInvoiceId,
                    Reason = "Hóa đơn không thuộc phiếu"
                }),
            CreatePicker(secondContext).UnlinkAsync(
                seed.StoreId, seed.ReceiptId, new UnlinkInputInvoiceRequest
                {
                    ExpectedCurrentInputInvoiceHeadId = seed.TargetInvoiceId,
                    Reason = "Hóa đơn không thuộc phiếu"
                }));

        outcomes.Count(x => x.Outcome == InputInvoiceAssociationMutationOutcomes.Applied)
            .Should().Be(1);
        outcomes.Count(x => x.Outcome == InputInvoiceAssociationMutationOutcomes.AlreadyApplied)
            .Should().Be(1);

        await using var verify = CreateContext(database, seed.StoreId);
        (await verify.StockDocumentInputInvoiceMaps.AnyAsync(x =>
            x.StockDocumentId == seed.ReceiptId &&
            x.InputInvoiceHeadId == seed.TargetInvoiceId)).Should().BeFalse();
        (await verify.StockDocumentInputInvoiceMaps.AnyAsync(x =>
            x.StockDocumentId == seed.OtherReceiptId &&
            x.InputInvoiceHeadId == seed.TargetInvoiceId)).Should().BeTrue();
        (await verify.StockDocumentLineInputInvoiceMaps.AnyAsync(x =>
            x.StockDocumentId == seed.ReceiptId &&
            x.InputInvoiceDetailId == seed.TargetDetailId)).Should().BeFalse();
        (await verify.InputInvoiceHeads.AnyAsync(x => x.Id == seed.TargetInvoiceId))
            .Should().BeTrue();
        (await verify.InputInvoiceDetails.AnyAsync(x => x.Id == seed.TargetDetailId))
            .Should().BeTrue();
        (await verify.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.StockDocumentId == seed.ReceiptId &&
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceUnlinked))
            .Should().Be(1);

        var after = await SnapshotAsync(database, seed);
        after.Should().BeEquivalentTo(before);
    }

    private static InputInvoicePickerService CreatePicker(AppDbContext context)
    {
        var repository = new InputInvoiceRepository(context);
        var reconciliation = CreateReconciliation(repository);
        return new(
            repository,
            new ThrowingLibrary(),
            new InputInvoiceXmlDocumentParser(),
            DispatchProxy.Create<IInputInvoiceXmlService, ThrowingXmlServiceProxy>(),
            reconciliationService: reconciliation);
    }

    private static InputInvoicePickerService CreateRelinkPicker(
        AppDbContext context,
        string invoiceNumber)
    {
        var repository = new InputInvoiceRepository(context);
        var supplierResolution = new InputInvoiceSupplierResolutionService(repository, new UserStub());
        var itemMapping = new InputInvoiceItemCatalogMappingService(repository, new UserStub());
        var reconciliation = new InputInvoiceReconciliationService(
            repository, itemMapping, new UserStub());
        var ownerGuard = new InputInvoiceReceiptOwnerGuard(
            new WarehouseRepository(context),
            new InputInvoiceBuyerOwnerResolutionService(new LegalEntityRepository(context)));
        var receiptLink = new InputInvoiceReceiptLinkService(
            repository, ownerGuard, supplierResolution, reconciliation);
        var xml = new InputInvoiceXmlService(
            repository,
            supplierResolution,
            new InputInvoiceXmlDocumentParser(),
            receiptLink,
            new InputInvoiceOwnerGuardAuditService(repository),
            buyerOwnerResolver: null,
            itemCatalogMappingService: itemMapping,
            reconciliationService: reconciliation);
        return new InputInvoicePickerService(
            repository,
            new FixedLibrary(invoiceNumber),
            new InputInvoiceXmlDocumentParser(),
            xml,
            reconciliationService: reconciliation,
            receiptLinkService: receiptLink,
            itemCatalogMappingService: itemMapping);
    }

    private static InputInvoiceXmlService CreateXmlService(AppDbContext context)
    {
        var repository = new InputInvoiceRepository(context);
        var supplierResolution = new InputInvoiceSupplierResolutionService(repository, new UserStub());
        var itemMapping = new InputInvoiceItemCatalogMappingService(repository, new UserStub());
        var reconciliation = new InputInvoiceReconciliationService(
            repository, itemMapping, new UserStub());
        var ownerGuard = new InputInvoiceReceiptOwnerGuard(
            new WarehouseRepository(context),
            new InputInvoiceBuyerOwnerResolutionService(new LegalEntityRepository(context)));
        return new InputInvoiceXmlService(
            repository,
            supplierResolution,
            new InputInvoiceXmlDocumentParser(),
            new InputInvoiceReceiptLinkService(
                repository, ownerGuard, supplierResolution, reconciliation),
            new InputInvoiceOwnerGuardAuditService(repository),
            buyerOwnerResolver: null,
            itemCatalogMappingService: itemMapping,
            reconciliationService: reconciliation);
    }

    private static InputInvoiceReconciliationService CreateReconciliation(
        InputInvoiceRepository repository)
        => new(repository,
            new InputInvoiceItemCatalogMappingService(repository, new UserStub()),
            new UserStub());

    private static StockDocumentService CreateHeaderService(AppDbContext context)
    {
        var inputInvoiceRepository = new InputInvoiceRepository(context);
        var dependencies = new Dictionary<Type, object?>
        {
            [typeof(IStockDocumentRepository)] = new StockDocumentRepository(context),
            [typeof(ILegalEntityRepository)] = new LegalEntityRepository(context),
            [typeof(IWarehouseRepository)] = new WarehouseRepository(context),
            [typeof(IBarcodeLookupService)] = Throwing<IBarcodeLookupService>(),
            [typeof(IInventoryUnitResolver)] = Throwing<IInventoryUnitResolver>(),
            [typeof(IInventoryMovementService)] = Throwing<IInventoryMovementService>(),
            [typeof(IInventoryMovementFactory)] = Throwing<IInventoryMovementFactory>(),
            [typeof(IInventoryRevaluationService)] = Throwing<IInventoryRevaluationService>(),
            [typeof(IDocumentNumberSequenceRepository)] = Throwing<IDocumentNumberSequenceRepository>(),
            [typeof(ITenantContext)] = new TenantStub(context.CurrentStoreId!.Value),
            [typeof(IInventoryValuationEntryRepository)] = Throwing<IInventoryValuationEntryRepository>(),
            [typeof(ICurrentUser)] = new UserStub(),
            [typeof(IInputInvoiceSupplierResolutionService)] =
                new InputInvoiceSupplierResolutionService(inputInvoiceRepository, new UserStub()),
            [typeof(IInputInvoiceRepository)] = inputInvoiceRepository,
            [typeof(IInputInvoiceReconciliationService)] = null,
            [typeof(IInputInvoiceReceiptLinkService)] = null,
            [typeof(IInputInvoiceOwnerGuardAuditService)] = null
        };
        var constructor = typeof(StockDocumentService).GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select(parameter => dependencies.TryGetValue(parameter.ParameterType, out var value)
                ? value
                : throw new InvalidOperationException(
                    $"Unsupported StockDocumentService dependency: {parameter.ParameterType.Name}"))
            .ToArray();
        return (StockDocumentService)constructor.Invoke(arguments);
    }

    private static T Throwing<T>() where T : class
        => DispatchProxy.Create<T, ThrowingDependencyProxy>();

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static AppDbContext CreateContext(
        InventoryPostingLocalDb database,
        int storeId)
        => new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(database.ConnectionString)
                .Options,
            new TenantStub(storeId),
            new UserStub());

    private static async Task<Seed> SeedAsync(
        InventoryPostingLocalDb database,
        InventoryPostingSeed catalog)
    {
        await using var db = CreateContext(database, catalog.StoreId);
        var supplier = await db.Suppliers.SingleAsync();
        supplier.TaxCode = "0312770607";
        var legalEntity = await db.LegalEntities.SingleAsync();
        legalEntity.TaxCode = "0101234567";
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == catalog.WarehouseId);
        var order = new PurchaseOrder
        {
            StoreId = catalog.StoreId,
            OrderNumber = $"PO-UX-{Guid.NewGuid():N}"[..20],
            SupplierId = supplier.Id,
            ExpectedWarehouseId = warehouse.Id,
            LegalEntityId = warehouse.LegalEntityId,
            Status = PurchaseOrderStatus.Approved
        };
        order.Lines.Add(new PurchaseOrderLine
        {
            StoreId = catalog.StoreId,
            LineNo = 1,
            ProductVariantId = catalog.ProductVariantId,
            ProductNameSnapshot = "Invoice picker product",
            UnitNameSnapshot = "Unit",
            ConversionFactor = 1,
            OrderedQuantity = 10,
            ReceivedQuantity = 7
        });
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();

        var receipt = new StockDocument
        {
            StoreId = catalog.StoreId,
            DocumentNo = $"PN-UX-{Guid.NewGuid():N}"[..20],
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.Confirmed,
            ApprovedAtUtc = new DateTime(2026, 8, 22, 8, 30, 0, DateTimeKind.Utc),
            ApprovedByUserId = 602,
            ConfirmedAtUtc = new DateTime(2026, 8, 22, 8, 30, 0, DateTimeKind.Utc),
            ConfirmedByUserId = 602,
            ConfirmedLegalEntityId = warehouse.LegalEntityId,
            ReceiptSource = PurchaseReceiptSource.PurchaseOrder,
            WarehouseId = warehouse.Id,
            SupplierId = supplier.Id,
            PurchaseOrderId = order.Id,
            DocumentDate = new DateTime(2026, 8, 22)
        };
        receipt.Lines.Add(new StockDocumentLine
        {
            LineNo = 1,
            ProductVariantId = catalog.ProductVariantId,
            ProductNameSnapshot = "Target line",
            UnitNameSnapshot = "Unit",
            PurchaseOrderLineId = order.Lines.Single().Id,
            Quantity = 2,
            Factor = 1,
            BaseQuantity = 2,
            UnitCost = 5,
            LineTotal = 10
        });
        receipt.Lines.Add(new StockDocumentLine
        {
            LineNo = 2,
            ProductVariantId = catalog.ProductVariantId,
            ProductNameSnapshot = "Other line",
            UnitNameSnapshot = "Unit",
            Quantity = 3,
            Factor = 1,
            BaseQuantity = 3,
            UnitCost = 6,
            LineTotal = 18
        });
        var otherReceipt = new StockDocument
        {
            StoreId = catalog.StoreId,
            DocumentNo = $"PN-OTHER-{Guid.NewGuid():N}"[..20],
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.PendingApproval,
            ReceiptSource = PurchaseReceiptSource.Direct,
            DirectReceiptReason = "Preserve another receipt link",
            WarehouseId = warehouse.Id,
            SupplierId = supplier.Id
        };
        db.StockDocuments.AddRange(receipt, otherReceipt);
        await db.SaveChangesAsync();

        var targetInvoice = Invoice(catalog.StoreId, supplier.Id, "9001");
        targetInvoice.Details.Add(new InputInvoiceDetail
        {
            LineNo = 1,
            ItemName = "Target line",
            Quantity = 2,
            UnitPrice = 5,
            LineAmount = 10
        });
        var otherInvoice = Invoice(catalog.StoreId, supplier.Id, "9002");
        otherInvoice.Details.Add(new InputInvoiceDetail
        {
            LineNo = 1,
            ItemName = "Other line",
            Quantity = 3,
            UnitPrice = 6,
            LineAmount = 18
        });
        db.InputInvoiceHeads.AddRange(targetInvoice, otherInvoice);
        await db.SaveChangesAsync();

        db.StockDocumentInputInvoiceMaps.AddRange(
            new StockDocumentInputInvoiceMap
            {
                StoreId = catalog.StoreId,
                StockDocumentId = receipt.Id,
                InputInvoiceHeadId = targetInvoice.Id
            },
            new StockDocumentInputInvoiceMap
            {
                StoreId = catalog.StoreId,
                StockDocumentId = otherReceipt.Id,
                InputInvoiceHeadId = targetInvoice.Id
            });
        db.StockDocumentLineInputInvoiceMaps.Add(
            new StockDocumentLineInputInvoiceMap
            {
                StoreId = catalog.StoreId,
                StockDocumentId = receipt.Id,
                StockDocumentLineId = receipt.Lines.ElementAt(0).Id,
                InputInvoiceDetailId = targetInvoice.Details.Single().Id,
                UseInputInvoice = true
            });
        await db.SaveChangesAsync();

        return new Seed(
            catalog.StoreId,
            receipt.Id,
            otherReceipt.Id,
            targetInvoice.Id,
            otherInvoice.Id,
            targetInvoice.Details.Single().Id,
            otherInvoice.Details.Single().Id,
            order.Lines.Single().Id);
    }

    private static async Task<LinkSeed> SeedLinkReceiptAsync(
        InventoryPostingLocalDb database,
        InventoryPostingSeed catalog)
    {
        await using var db = CreateContext(database, catalog.StoreId);
        var supplier = await db.Suppliers.SingleAsync();
        supplier.TaxCode = "0312770607";
        var legalEntity = await db.LegalEntities.SingleAsync();
        legalEntity.TaxCode = "0101234567";
        var receipt = new StockDocument
        {
            StoreId = catalog.StoreId,
            DocumentNo = $"PN-LINK-{Guid.NewGuid():N}"[..20],
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.PendingApproval,
            ReceiptSource = PurchaseReceiptSource.Direct,
            DirectReceiptReason = "Concurrent picker link",
            WarehouseId = catalog.WarehouseId,
            SupplierId = supplier.Id,
            DocumentDate = new DateTime(2026, 8, 22)
        };
        receipt.Lines.Add(new StockDocumentLine
        {
            LineNo = 1,
            ProductVariantId = catalog.ProductVariantId,
            ProductNameSnapshot = "Product",
            UnitNameSnapshot = "Unit",
            Quantity = 2,
            Factor = 1,
            BaseQuantity = 2,
            UnitCost = 5,
            LineTotal = 10
        });
        db.StockDocuments.Add(receipt);
        await db.SaveChangesAsync();
        return new LinkSeed(
            catalog.StoreId,
            receipt.Id,
            supplier.Id,
            (await db.Warehouses.AsNoTracking()
                .SingleAsync(x => x.Id == catalog.WarehouseId)).LegalEntityId);
    }

    private static async Task<int> AddReplacementSupplierAsync(
        InventoryPostingLocalDb database,
        int storeId)
    {
        await using var db = CreateContext(database, storeId);
        var supplier = new Supplier
        {
            StoreId = storeId,
            Code = $"NCC-{Guid.NewGuid():N}"[..20],
            Name = "Replacement Supplier",
            TaxCode = "0312770608"
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    }

    private static InputInvoiceHead Invoice(int storeId, int supplierId, string number)
    {
        var invoice = new InputInvoiceHead
        {
            StoreId = storeId,
            SellerName = "Supplier",
            SellerTaxCode = "0312770607",
            BuyerTaxCode = "0101234567",
            InvoiceSeries = "C26MVP",
            InvoiceNumber = number,
            InvoiceDate = new DateTime(2026, 8, 22),
            ResolvedSupplierId = supplierId,
            SupplierResolutionStatus = InputInvoiceSupplierResolutionStatus.Resolved
        };
        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(invoice);
        return invoice;
    }

    private static async Task SeedGenuinePostedEffectsAsync(
        InventoryPostingLocalDb database,
        InventoryPostingSeed catalog,
        Seed seed)
    {
        await using var db = CreateContext(database, seed.StoreId);
        var line = await db.StockDocumentLines.AsNoTracking()
            .Where(x => x.StockDocumentId == seed.ReceiptId)
            .OrderBy(x => x.LineNo)
            .FirstAsync();
        var movement = new InventoryMovementService(
            new InventoryBalanceRepository(db),
            new InventoryTransactionRepository(db),
            new InventoryValuationEntryRepository(db),
            new InventoryCostLayerRepository(db),
            new InventoryCostLayerAllocationRepository(db),
            new WarehouseRepository(db),
            new InventoryPostingTransactionCoordinator(db));
        var factory = new InventoryMovementFactory();
        await movement.CreateAsync(factory.CreatePurchaseReceipt(
            catalog.WarehouseId,
            catalog.ProductVariantId,
            2,
            5,
            seed.ReceiptId.ToString(),
            line.Id,
            "B3-POSTED",
            line.LineNo));
        await movement.CreateAsync(factory.CreateSaleFinalize(
            catalog.WarehouseId,
            catalog.ProductVariantId,
            99001,
            99002,
            "Allocation proof",
            1,
            5));
        db.PurchasePayables.Add(new PurchasePayable
        {
            StoreId = seed.StoreId,
            StockDocumentId = seed.ReceiptId,
            PurchaseOrderId = await db.StockDocuments
                .Where(x => x.Id == seed.ReceiptId)
                .Select(x => x.PurchaseOrderId)
                .SingleAsync(),
            Type = PurchasePayableType.Merchandise,
            SourceKey = $"B3-POSTED-{seed.ReceiptId}",
            SupplierId = await db.StockDocuments
                .Where(x => x.Id == seed.ReceiptId)
                .Select(x => x.SupplierId)
                .SingleAsync(),
            Amount = 10,
            Status = PurchasePayableStatus.Outstanding
        });
        await db.SaveChangesAsync();
    }

    private static byte[] BuildInvoiceXml(
        string invoiceNumber,
        string buyerTaxCode = "0101234567")
        => System.Text.Encoding.UTF8.GetBytes($$"""
            <HDon><DLHDon><TTChung><KHMSHDon>1</KHMSHDon><KHHDon>C26MVP</KHHDon><SHDon>{{invoiceNumber}}</SHDon><NLap>2026-08-22</NLap></TTChung><NDHDon><NBan><Ten>Supplier</Ten><MST>0312770607</MST></NBan><NMua><Ten>Buyer</Ten><MST>{{buyerTaxCode}}</MST></NMua><DSHHDVu><HHDVu><STT>1</STT><THHDVu>Product</THHDVu><DVTinh>Unit</DVTinh><SLuong>2</SLuong><DGia>5</DGia><ThTien>10</ThTien></HHDVu></DSHHDVu><TToan><TgTCThue>10</TgTCThue><TgTThue>0</TgTThue><TgTTTBSo>10</TgTTTBSo></TToan></NDHDon></DLHDon></HDon>
            """);

    private static async Task<PostingSnapshot> SnapshotAsync(
        InventoryPostingLocalDb database,
        Seed seed)
    {
        await using var db = CreateContext(database, seed.StoreId);
        var receipt = await db.StockDocuments
            .AsNoTracking()
            .SingleAsync(x => x.Id == seed.ReceiptId);
        var lines = await db.StockDocumentLines
            .AsNoTracking()
            .Where(x => x.StockDocumentId == seed.ReceiptId)
            .OrderBy(x => x.LineNo)
            .Select(x => new LineSnapshot(
                x.Id, x.Quantity, x.BaseQuantity, x.UnitCost, x.LineTotal, x.RowVersion))
            .ToListAsync();
        var transactionIdentity = await db.InventoryTransactions.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new PostingTransactionSnapshot(
                x.Id,
                x.ReferenceType,
                x.ReferenceId,
                x.ReferenceLineId,
                x.ReferenceSubKey,
                x.QuantityChange,
                x.TotalCost))
            .ToListAsync();
        return new PostingSnapshot(
            receipt.Status,
            receipt.SupplierId,
            receipt.DocumentNo,
            receipt.ApprovedAtUtc,
            receipt.ApprovedByUserId,
            receipt.ConfirmedAtUtc,
            receipt.ConfirmedByUserId,
            receipt.ConfirmedLegalEntityId,
            receipt.RowVersion,
            lines,
            transactionIdentity,
            await db.InventoryTransactions.CountAsync(),
            await db.InventoryBalances.CountAsync(),
            await db.InventoryValuationEntries.CountAsync(),
            await db.InventoryCostLayers.CountAsync(),
            await db.InventoryCostLayerAllocations.CountAsync(),
            await db.PurchasePayables.CountAsync(),
            await db.PurchaseOrderLines
                .Where(x => x.Id == seed.PurchaseOrderLineId)
                .Select(x => x.ReceivedQuantity)
                .SingleAsync());
    }

    private static async Task<PendingMutationSnapshot> PendingSnapshotAsync(
        InventoryPostingLocalDb database,
        int storeId,
        int receiptId)
    {
        await using var db = CreateContext(database, storeId);
        var receipt = await db.StockDocuments.AsNoTracking()
            .SingleAsync(x => x.Id == receiptId);
        var lines = await db.StockDocumentLines.AsNoTracking()
            .Where(x => x.StockDocumentId == receiptId)
            .OrderBy(x => x.LineNo)
            .Select(x => new LineSnapshot(
                x.Id, x.Quantity, x.BaseQuantity, x.UnitCost, x.LineTotal, x.RowVersion))
            .ToListAsync();
        return new PendingMutationSnapshot(
            receipt.Status,
            receipt.SupplierId,
            receipt.WarehouseId,
            lines,
            await db.InventoryTransactions.CountAsync(),
            await db.InventoryBalances.CountAsync(),
            await db.InventoryValuationEntries.CountAsync(),
            await db.InventoryCostLayers.CountAsync(),
            await db.InventoryCostLayerAllocations.CountAsync(),
            await db.PurchasePayables.CountAsync());
    }

    private sealed class ThrowingLibrary : IInputInvoiceDocumentLibrary
    {
        public Task<IReadOnlyList<InputInvoicePickerCandidateDto>> BrowseAsync(
            string normalizedSupplierTaxCode, InputInvoicePickerBrowseRequest request,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InputInvoicePickerCandidateDto> ResolveAsync(
            string normalizedSupplierTaxCode, string documentKey,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InputInvoicePdfPreviewDto> GetPdfAsync(
            string normalizedSupplierTaxCode, string documentKey,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]> GetXmlBytesAsync(
            string normalizedSupplierTaxCode, string documentKey,
            CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FixedLibrary(string invoiceNumber) : IInputInvoiceDocumentLibrary
    {
        private readonly byte[] _xml = BuildInvoiceXml(invoiceNumber);

        public Task<IReadOnlyList<InputInvoicePickerCandidateDto>> BrowseAsync(
            string normalizedSupplierTaxCode, InputInvoicePickerBrowseRequest request,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InputInvoicePickerCandidateDto>>([Candidate()]);

        public Task<InputInvoicePickerCandidateDto> ResolveAsync(
            string normalizedSupplierTaxCode, string documentKey,
            CancellationToken ct = default) => Task.FromResult(Candidate());

        public Task<InputInvoicePdfPreviewDto> GetPdfAsync(
            string normalizedSupplierTaxCode, string documentKey,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<byte[]> GetXmlBytesAsync(
            string normalizedSupplierTaxCode, string documentKey,
            CancellationToken ct = default) => Task.FromResult(_xml);

        private InputInvoicePickerCandidateDto Candidate() => new()
        {
            DocumentKey = $"replacement-{invoiceNumber}",
            XmlValid = true,
            HasXml = true,
            SellerTaxCode = "0312770607",
            BuyerTaxCode = "0101234567",
            InvoiceSeries = "C26MVP",
            InvoiceNumber = invoiceNumber,
            InvoiceDate = new DateTime(2026, 8, 22),
            SelectionAllowed = true
        };
    }

    private class ThrowingXmlServiceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException(targetMethod?.Name);
    }

    private class ThrowingDependencyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException(
                $"Unexpected posting/dependency call: {targetMethod?.Name}");
    }

    private sealed class TenantStub(int storeId) : ITenantContext
    {
        public int? StoreId => storeId;
        public bool IsHostAdmin => false;
        public string? Subdomain => "input-invoice-unlink";
    }

    private sealed class UserStub : ICurrentUser
    {
        public int? UserId => 602;
        public string? UserName => "input-invoice-unlink";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed record Seed(
        int StoreId,
        int ReceiptId,
        int OtherReceiptId,
        int TargetInvoiceId,
        int OtherInvoiceId,
        int TargetDetailId,
        int OtherDetailId,
        int PurchaseOrderLineId);

    private sealed record LinkSeed(
        int StoreId,
        int ReceiptId,
        int SupplierId,
        int LegalEntityId);

    private sealed record LineSnapshot(
        int Id,
        decimal Quantity,
        decimal BaseQuantity,
        decimal UnitCost,
        decimal LineTotal,
        byte[] RowVersion);

    private sealed record PostingTransactionSnapshot(
        int Id,
        InventoryReferenceType ReferenceType,
        string? ReferenceId,
        int? ReferenceLineId,
        string? ReferenceSubKey,
        decimal QuantityChange,
        decimal TotalCost);

    private sealed record PostingSnapshot(
        StockDocumentStatus Status,
        int? SupplierId,
        string DocumentNo,
        DateTime? ApprovedAtUtc,
        int? ApprovedByUserId,
        DateTime? ConfirmedAtUtc,
        int? ConfirmedByUserId,
        int? ConfirmedLegalEntityId,
        byte[] ReceiptRowVersion,
        IReadOnlyList<LineSnapshot> Lines,
        IReadOnlyList<PostingTransactionSnapshot> PostingTransactionIdentity,
        int InventoryTransactions,
        int InventoryBalances,
        int InventoryValuationEntries,
        int InventoryCostLayers,
        int InventoryCostLayerAllocations,
        int PurchasePayables,
        decimal PurchaseOrderReceivedQuantity);

    private sealed record PendingMutationSnapshot(
        StockDocumentStatus Status,
        int? SupplierId,
        int WarehouseId,
        IReadOnlyList<LineSnapshot> Lines,
        int InventoryTransactions,
        int InventoryBalances,
        int InventoryValuationEntries,
        int InventoryCostLayers,
        int InventoryCostLayerAllocations,
        int PurchasePayables);
}
