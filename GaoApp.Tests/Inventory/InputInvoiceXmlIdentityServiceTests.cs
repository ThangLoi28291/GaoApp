using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Inventory;

public sealed class InputInvoiceXmlIdentityServiceTests
{
    private const int StoreId = 1;

    [Fact]
    public async Task Different_xml_serializations_with_same_business_identity_should_reuse_invoice()
    {
        await using var context = CreateContext(out var tenant);
        SeedReceipt(context, documentId: 100, lineId: 101);
        SeedReceipt(context, documentId: 200, lineId: 201);
        await context.SaveChangesAsync();

        tenant.SetStore(StoreId, "identity-store");
        context.ChangeTracker.Clear();

        var service = new InputInvoiceXmlService(
            new InputInvoiceRepository(context));

        var firstBytes = BuildXml(
            "031.277.0607-001",
            "c26 taa",
            "00 0123",
            "first");

        var secondBytes = BuildXml(
            "0312770607001",
            " C26TAA ",
            "000123",
            "second");

        var first = await service.UploadXmlAsync(
            StoreId,
            Request(100, firstBytes));

        var second = await service.UploadXmlAsync(
            StoreId,
            Request(200, secondBytes));

        first.IsExistingInvoice.Should().BeFalse();
        second.IsExistingInvoice.Should().BeTrue();
        second.InputInvoiceHeadId.Should().Be(first.InputInvoiceHeadId);

        (await context.InputInvoiceHeads.CountAsync())
            .Should().Be(1);

        (await context.StockDocumentInputInvoiceMaps.CountAsync())
            .Should().Be(2);

        var persisted = await context.InputInvoiceHeads
            .Include(x => x.Details)
            .SingleAsync();

        persisted.NormalizedSellerTaxCode
            .Should().Be("0312770607001");

        persisted.NormalizedInvoiceSeries
            .Should().Be("C26TAA");

        persisted.NormalizedInvoiceNumber
            .Should().Be("000123");

        persisted.InvoiceIdentityDate
            .Should().Be(new DateTime(2026, 8, 17));

        persisted.SellerName
            .Should().Be("Seller first");

        persisted.OriginalFileName
            .Should().Be("invoice-100.xml");

        persisted.Details
            .Should().ContainSingle();

        persisted.Details.Single().ItemName
            .Should().Be("Rice first");

        var firstHash = Convert.ToHexString(
            SHA256.HashData(firstBytes));

        var secondHash = Convert.ToHexString(
            SHA256.HashData(secondBytes));

        persisted.XmlHash
            .Should().Be(firstHash);

        persisted.XmlHash
            .Should().NotBe(secondHash);
    }

    [Fact]
    public async Task Missing_business_identity_should_reject_before_persistence()
    {
        await using var context = CreateContext(out var tenant);
        SeedReceipt(context, documentId: 100, lineId: 101);
        await context.SaveChangesAsync();
        tenant.SetStore(StoreId, "identity-store");
        context.ChangeTracker.Clear();
        var service = new InputInvoiceXmlService(
            new InputInvoiceRepository(context));

        var action = () => service.UploadXmlAsync(
            StoreId,
            Request(100, BuildXml(
                sellerTaxCode: string.Empty,
                invoiceSeries: "C26TAA",
                invoiceNumber: "123",
                marker: "missing")));

        (await action.Should().ThrowAsync<BusinessRuleException>())
            .Which.SafeMessage.Should().Be(
                "XML thiếu mã số thuế người bán để định danh hóa đơn.");
        (await context.InputInvoiceHeads.CountAsync()).Should().Be(0);
        (await context.StockDocumentInputInvoiceMaps.CountAsync())
            .Should().Be(0);
    }
    [Fact]
    public async Task Exact_hash_incomplete_legacy_invoice_should_be_reused_without_enrichment()
    {
        await using var context = CreateContext(out var tenant);
        SeedReceipt(context, documentId: 100, lineId: 101);

        var bytes = BuildXml(
            sellerTaxCode: string.Empty,
            invoiceSeries: "C26TAA",
            invoiceNumber: "000123",
            marker: "legacy-reupload");

        var hash = Convert.ToHexString(
            SHA256.HashData(bytes));

        var legacy = new InputInvoiceHead
        {
            Id = 501,
            StoreId = StoreId,
            InvoiceTemplateCode = "1",
            InvoiceSeries = "C26TAA",
            InvoiceNumber = "000123",
            InvoiceDate = new DateTime(2026, 8, 17),
            SellerTaxCode = null,
            SellerName = "Legacy seller",

            // Mô phỏng legacy row được migration backfill một phần:
            // thiếu seller identity nên business identity chưa complete.
            NormalizedSellerTaxCode = null,
            NormalizedInvoiceSeries = "C26TAA",
            NormalizedInvoiceNumber = "000123",
            InvoiceIdentityDate = new DateTime(2026, 8, 17),

            OriginalFileName = "legacy-original.xml",
            XmlHash = hash,
            RowVersion = new byte[8]
        };

        context.InputInvoiceHeads.Add(legacy);
        await context.SaveChangesAsync();

        tenant.SetStore(StoreId, "identity-store");
        context.ChangeTracker.Clear();

        var service = new InputInvoiceXmlService(
            new InputInvoiceRepository(context));

        var result = await service.UploadXmlAsync(
            StoreId,
            Request(100, bytes));

        result.IsExistingInvoice.Should().BeTrue();
        result.InputInvoiceHeadId.Should().Be(legacy.Id);
        result.DetailCount.Should().Be(0);

        (await context.InputInvoiceHeads.CountAsync())
            .Should().Be(1);

        (await context.InputInvoiceDetails.CountAsync())
            .Should().Be(0);

        (await context.StockDocumentInputInvoiceMaps.CountAsync())
            .Should().Be(1);

        (await context.StockDocumentLineInputInvoiceMaps.CountAsync())
            .Should().Be(1);

        var persisted = await context.InputInvoiceHeads.SingleAsync();

        persisted.SellerTaxCode.Should().BeNull();
        persisted.NormalizedSellerTaxCode.Should().BeNull();
        persisted.NormalizedInvoiceSeries.Should().Be("C26TAA");
        persisted.NormalizedInvoiceNumber.Should().Be("000123");
        persisted.InvoiceIdentityDate
            .Should().Be(new DateTime(2026, 8, 17));

        persisted.SellerName.Should().Be("Legacy seller");
        persisted.OriginalFileName.Should().Be("legacy-original.xml");
        persisted.XmlHash.Should().Be(hash);
    }
    [Fact]
    public async Task Split_business_identity_and_hash_matches_should_fail_closed_without_link()
    {
        await using var context = CreateContext(out var tenant);
        SeedReceipt(context, documentId: 100, lineId: 101);

        var bytes = BuildXml(
            sellerTaxCode: "031.277.0607-001",
            invoiceSeries: " c26 taa ",
            invoiceNumber: "00 0123",
            marker: "candidate");

        var candidateHash = Convert.ToHexString(
            SHA256.HashData(bytes));

        var businessMatchHash = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes("business-match")));

        // Invoice A chỉ khớp exact XmlHash,
        // nhưng business identity khác candidate.
        var hashMatch = new InputInvoiceHead
        {
            Id = 601,
            StoreId = StoreId,
            SellerTaxCode = "0999999999",
            InvoiceSeries = "OTHER",
            InvoiceNumber = "999999",
            InvoiceDate = new DateTime(2026, 8, 17),
            SellerName = "Hash match",
            OriginalFileName = "hash-match.xml",
            XmlHash = candidateHash,
            RowVersion = new byte[8]
        };
        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(hashMatch);

        // Invoice B khớp business identity,
        // nhưng XmlHash khác candidate.
        var businessMatch = new InputInvoiceHead
        {
            Id = 602,
            StoreId = StoreId,
            SellerTaxCode = "0312770607001",
            InvoiceSeries = "C26TAA",
            InvoiceNumber = "000123",
            InvoiceDate = new DateTime(2026, 8, 17),
            SellerName = "Business match",
            OriginalFileName = "business-match.xml",
            XmlHash = businessMatchHash,
            RowVersion = new byte[8]
        };
        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(businessMatch);

        context.InputInvoiceHeads.AddRange(
            hashMatch,
            businessMatch);

        await context.SaveChangesAsync();

        tenant.SetStore(StoreId, "identity-store");
        context.ChangeTracker.Clear();

        var service = new InputInvoiceXmlService(
            new InputInvoiceRepository(context));

        var action = () => service.UploadXmlAsync(
            StoreId,
            Request(100, bytes));

        var exception = (
            await action.Should()
                .ThrowAsync<BusinessRuleException>())
            .Which;

        exception.SafeMessage.Should().Be(
            "Dữ liệu định danh hóa đơn đang mâu thuẫn. " +
            "Vui lòng xử lý dữ liệu trước khi liên kết.");

        (await context.InputInvoiceHeads.CountAsync())
            .Should().Be(2);

        (await context.StockDocumentInputInvoiceMaps.CountAsync())
            .Should().Be(0);

        (await context.StockDocumentLineInputInvoiceMaps.CountAsync())
            .Should().Be(0);

        var persistedHashMatch = await context.InputInvoiceHeads
            .SingleAsync(x => x.Id == hashMatch.Id);

        var persistedBusinessMatch = await context.InputInvoiceHeads
            .SingleAsync(x => x.Id == businessMatch.Id);

        persistedHashMatch.XmlHash.Should().Be(candidateHash);
        persistedHashMatch.SellerName.Should().Be("Hash match");

        persistedBusinessMatch.XmlHash.Should().Be(businessMatchHash);
        persistedBusinessMatch.SellerName.Should().Be("Business match");
    }
    [Fact]
    public async Task Same_hash_with_different_complete_identity_should_fail_closed_without_link()
    {
        await using var context = CreateContext(out var tenant);
        SeedReceipt(context, documentId: 100, lineId: 101);

        var bytes = BuildXml(
            sellerTaxCode: "031.277.0607-001",
            invoiceSeries: "C26TAA",
            invoiceNumber: "000123",
            marker: "candidate");

        var hash = Convert.ToHexString(
            SHA256.HashData(bytes));

        var existing = new InputInvoiceHead
        {
            StoreId = StoreId,
            SellerTaxCode = "0999999999",
            InvoiceSeries = "OTHER",
            InvoiceNumber = "999999",
            InvoiceDate = new DateTime(2026, 8, 17),
            SellerName = "Existing different identity",
            OriginalFileName = "existing-different.xml",
            XmlHash = hash,
            RowVersion = new byte[8]
        };

        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(
            existing);

        context.InputInvoiceHeads.Add(existing);
        await context.SaveChangesAsync();

        tenant.SetStore(StoreId, "identity-store");
        context.ChangeTracker.Clear();

        var service = new InputInvoiceXmlService(
            new InputInvoiceRepository(context));

        Func<Task> action = () =>
            service.UploadXmlAsync(
                StoreId,
                Request(100, bytes));

        var exception = (
            await action.Should()
                .ThrowAsync<BusinessRuleException>())
            .Which;

        exception.SafeMessage.Should().Be(
            "Dữ liệu định danh hóa đơn đang mâu thuẫn. " +
            "Vui lòng xử lý dữ liệu trước khi liên kết.");

        (await context.InputInvoiceHeads.CountAsync())
            .Should().Be(1);

        (await context.StockDocumentInputInvoiceMaps.CountAsync())
            .Should().Be(0);

        (await context.StockDocumentLineInputInvoiceMaps.CountAsync())
            .Should().Be(0);

        var persisted =
            await context.InputInvoiceHeads.SingleAsync();

        persisted.SellerName
            .Should().Be("Existing different identity");

        persisted.NormalizedSellerTaxCode
            .Should().Be("0999999999");

        persisted.NormalizedInvoiceSeries
            .Should().Be("OTHER");

        persisted.NormalizedInvoiceNumber
            .Should().Be("999999");

        persisted.XmlHash.Should().Be(hash);
    }

    [Fact]
    public async Task Direct_repository_candidate_without_normalized_identity_should_be_rejected()
    {
        await using var context = CreateContext(out var tenant);
        tenant.SetStore(StoreId, "identity-store");

        var repository =
            new InputInvoiceRepository(context);

        var candidate = new InputInvoiceHead
        {
            StoreId = StoreId,

            // Raw display values có vẻ đầy đủ,
            // nhưng direct caller cố tình bỏ qua policy.
            SellerTaxCode = "0312770607",
            InvoiceSeries = "C26TAA",
            InvoiceNumber = "000123",
            InvoiceDate = new DateTime(2026, 8, 17),

            XmlHash = "DIRECT-INCOMPLETE",
            OriginalFileName = "direct-incomplete.xml",
            RowVersion = new byte[8]
        };

        Func<Task> action = () =>
            repository.ResolveInputInvoiceAsync(candidate);

        var exception = (
            await action.Should()
                .ThrowAsync<BusinessRuleException>())
            .Which;

        exception.SafeMessage.Should().Be(
            "Dữ liệu định danh hóa đơn chưa đầy đủ.");

        (await context.InputInvoiceHeads.CountAsync())
            .Should().Be(0);

        (await context.InputInvoiceDetails.CountAsync())
            .Should().Be(0);
    }

    [Fact]
    public async Task Matching_soft_deleted_invoice_should_create_new_active_invoice()
    {
        await using var context = CreateContext(out var tenant);

        SeedReceipt(
            context,
            documentId: 100,
            lineId: 101);

        var bytes = BuildXml(
            sellerTaxCode: "0312770607",
            invoiceSeries: "C26TAA",
            invoiceNumber: "000123",
            marker: "active");

        var hash = Convert.ToHexString(
            SHA256.HashData(bytes));

        //
        // Bước 1:
        // Tạo invoice ACTIVE bình thường trước.
        //
        // Không set IsDeleted = true ở entity Added,
        // vì AppDbContext chủ động ép Added entity về
        // IsDeleted = false.
        //
        var deleted = new InputInvoiceHead
        {
            StoreId = StoreId,

            SellerTaxCode = "0312770607",
            InvoiceSeries = "C26TAA",
            InvoiceNumber = "000123",
            InvoiceDate = new DateTime(2026, 8, 17),

            SellerName = "Deleted historical invoice",
            OriginalFileName = "deleted.xml",

            XmlHash = hash,

            RowVersion = new byte[8]
        };

        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(
            deleted);

        context.InputInvoiceHeads.Add(deleted);

        await context.SaveChangesAsync();

        var deletedId = deleted.Id;

        //
        // Bước 2:
        // Soft-delete theo đúng lifecycle thực tế của GaoApp.
        //
        // Remove() đưa entity vào EntityState.Deleted.
        // AppDbContext.SaveChangesAsync() sau đó chuyển nó
        // thành soft delete thông qua ApplySafeSoftDelete().
        //
        context.InputInvoiceHeads.Remove(deleted);

        await context.SaveChangesAsync();

        //
        // Xóa ChangeTracker để mọi assertion sau đọc lại
        // durable/database state thay vì entity đang tracked.
        //
        context.ChangeTracker.Clear();

        //
        // Preconditions:
        // fixture bắt buộc phải chứng minh row lịch sử
        // thật sự đang IsDeleted = true.
        //
        var persistedDeleted = await context
            .InputInvoiceHeads
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == deletedId);

        persistedDeleted.IsDeleted
            .Should().BeTrue();

        persistedDeleted.SellerName
            .Should().Be(
                "Deleted historical invoice");

        persistedDeleted.XmlHash
            .Should().Be(hash);

        //
        // Với normal query filter, hiện không được thấy
        // invoice active nào.
        //
        (await context.InputInvoiceHeads.CountAsync())
            .Should().Be(0);

        //
        // Bước 3:
        // Chuyển sang đúng tenant/store rồi upload XML
        // có cùng identity/hash với historical deleted row.
        //
        tenant.SetStore(
            StoreId,
            "identity-store");

        context.ChangeTracker.Clear();

        var service = new InputInvoiceXmlService(
            new InputInvoiceRepository(context));

        var result = await service.UploadXmlAsync(
            StoreId,
            Request(100, bytes));

        //
        // FR-05:
        // deleted row không được reuse.
        // Phải tạo một active invoice mới.
        //
        result.IsExistingInvoice
            .Should().BeFalse();

        result.InputInvoiceHeadId
            .Should().NotBe(deletedId);

        //
        // IgnoreQueryFilters:
        // 1 deleted historical + 1 active new.
        //
        (await context.InputInvoiceHeads
                .IgnoreQueryFilters()
                .CountAsync())
            .Should().Be(2);

        //
        // Normal query:
        // chỉ nhìn thấy active invoice mới.
        //
        (await context.InputInvoiceHeads.CountAsync())
            .Should().Be(1);

        //
        // Upload mới vẫn phải tạo đúng một receipt map
        // và một line map.
        //
        (await context
                .StockDocumentInputInvoiceMaps
                .CountAsync())
            .Should().Be(1);

        (await context
                .StockDocumentLineInputInvoiceMaps
                .CountAsync())
            .Should().Be(1);

        //
        // Historical deleted row tuyệt đối không được
        // revive/enrich/overwrite.
        //
        persistedDeleted = await context
            .InputInvoiceHeads
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == deletedId);

        persistedDeleted.IsDeleted
            .Should().BeTrue();

        persistedDeleted.SellerName
            .Should().Be(
                "Deleted historical invoice");

        persistedDeleted.OriginalFileName
            .Should().Be("deleted.xml");

        persistedDeleted.XmlHash
            .Should().Be(hash);

        //
        // Active row mới phải là record khác.
        //
        var activeInvoice =
            await context.InputInvoiceHeads
                .SingleAsync();

        activeInvoice.Id
            .Should().Be(
                result.InputInvoiceHeadId);

        activeInvoice.Id
            .Should().NotBe(deletedId);

        activeInvoice.IsDeleted
            .Should().BeFalse();

        activeInvoice.NormalizedSellerTaxCode
            .Should().Be("0312770607");

        activeInvoice.NormalizedInvoiceSeries
            .Should().Be("C26TAA");

        activeInvoice.NormalizedInvoiceNumber
            .Should().Be("000123");

        activeInvoice.InvoiceIdentityDate
            .Should().Be(
                new DateTime(2026, 8, 17));
    }

    [Fact]
    public async Task Different_invoice_number_or_date_should_create_distinct_invoices()
    {
        await using var context = CreateContext(out var tenant);

        SeedReceipt(context, documentId: 100, lineId: 101);
        SeedReceipt(context, documentId: 200, lineId: 201);
        SeedReceipt(context, documentId: 300, lineId: 301);

        await context.SaveChangesAsync();

        tenant.SetStore(StoreId, "identity-store");
        context.ChangeTracker.Clear();

        var service = new InputInvoiceXmlService(
            new InputInvoiceRepository(context));

        var first = await service.UploadXmlAsync(
            StoreId,
            Request(
                100,
                BuildXml(
                    "0312770607",
                    "C26TAA",
                    "000123",
                    "base",
                    "2026-08-17")));

        var differentNumber =
            await service.UploadXmlAsync(
                StoreId,
                Request(
                    200,
                    BuildXml(
                        "0312770607",
                        "C26TAA",
                        "000124",
                        "different-number",
                        "2026-08-17")));

        var differentDate =
            await service.UploadXmlAsync(
                StoreId,
                Request(
                    300,
                    BuildXml(
                        "0312770607",
                        "C26TAA",
                        "000123",
                        "different-date",
                        "2026-08-18")));

        first.IsExistingInvoice.Should().BeFalse();
        differentNumber.IsExistingInvoice.Should().BeFalse();
        differentDate.IsExistingInvoice.Should().BeFalse();

        new[]
        {
            first.InputInvoiceHeadId,
            differentNumber.InputInvoiceHeadId,
            differentDate.InputInvoiceHeadId
        }
            .Distinct()
            .Should().HaveCount(3);

        (await context.InputInvoiceHeads.CountAsync())
            .Should().Be(3);

        (await context.StockDocumentInputInvoiceMaps.CountAsync())
            .Should().Be(3);

        (await context.StockDocumentLineInputInvoiceMaps.CountAsync())
            .Should().Be(3);
    }
    private static UploadInputInvoiceXmlRequest Request(
        int stockDocumentId,
        byte[] bytes)
        => new()
        {
            StockDocumentId = stockDocumentId,
            OriginalFileName = $"invoice-{stockDocumentId}.xml",
            FileBytes = bytes
        };

    private static byte[] BuildXml(
    string sellerTaxCode,
    string invoiceSeries,
    string invoiceNumber,
    string marker,
    string invoiceDate = "2026-08-17")
    => Encoding.UTF8.GetBytes(
            $"""
             <HDon>
               <DLHDon>
                 <TTChung>
                   <KHMSHDon>1</KHMSHDon>
                   <KHHDon>{invoiceSeries}</KHHDon>
                   <SHDon>{invoiceNumber}</SHDon>
                   <NLap>{invoiceDate}</NLap>
                 </TTChung>
                 <NDHDon>
                   <NBan><MST>{sellerTaxCode}</MST><Ten>Seller {marker}</Ten></NBan>
                   <NMua><MST>0100000001</MST><Ten>Buyer</Ten></NMua>
                   <DSHHDVu>
                     <HHDVu>
                       <STT>1</STT><THHDVu>Rice {marker}</THHDVu><DVTinh>kg</DVTinh>
                       <SLuong>1</SLuong><DGia>10</DGia><ThTien>10</ThTien><TSuat>10%</TSuat>
                     </HHDVu>
                   </DSHHDVu>
                   <TToan><TgTCThue>10</TgTCThue><TgTThue>1</TgTThue><TgTTTBSo>11</TgTTTBSo></TToan>
                 </NDHDon>
               </DLHDon>
             </HDon>
             """);

    private static InMemoryAppDbContext CreateContext(
        out TenantContext tenant)
    {
        tenant = new TenantContext();
        tenant.SetHostAdmin();
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new InMemoryAppDbContext(
            options,
            tenant,
            new UserStub());
        context.VerifyRowVersionConfiguration();
        return context;
    }

    private static void SeedReceipt(
        InMemoryAppDbContext context,
        int documentId,
        int lineId)
    {
        if (!context.Stores.Local.Any())
        {
            context.Stores.Add(new Store
            {
                Id = StoreId,
                Name = "Identity Store",
                SubDomain = "identity-store",
                SubDomainNormalized = "IDENTITY-STORE",
                IsActive = true,
                RowVersion = new byte[8]
            });
            context.LegalEntities.Add(new LegalEntity
            {
                Id = 1,
                StoreId = StoreId,
                Code = "LE-1",
                Name = "Legal Entity",
                LegalName = "Legal Entity",
                IsActive = true,
                RowVersion = new byte[8]
            });
            context.Warehouses.Add(new Warehouse
            {
                Id = 1,
                StoreId = StoreId,
                LegalEntityId = 1,
                Code = "WH-1",
                Name = "Warehouse",
                RowVersion = new byte[8]
            });
        }

        context.StockDocuments.Add(new StockDocument
        {
            Id = documentId,
            StoreId = StoreId,
            DocumentNo = $"PN-{documentId}",
            Type = StockDocumentType.Receipt,
            WarehouseId = 1,
            RowVersion = new byte[8]
        });
        context.StockDocumentLines.Add(new StockDocumentLine
        {
            Id = lineId,
            StockDocumentId = documentId,
            LineNo = 1,
            ProductVariantId = 1,
            ProductNameSnapshot = "Rice",
            Quantity = 1m,
            Factor = 1m,
            BaseQuantity = 1m,
            UnitCost = 10m,
            LineTotal = 10m,
            RowVersion = new byte[8]
        });
    }

    private sealed class UserStub : ICurrentUser
    {
        public int? UserId => 602;
        public string? UserName => "input-invoice-service";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
