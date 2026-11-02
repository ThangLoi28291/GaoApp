using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Options;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Services.Invoices;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Inventory;

public sealed class InputInvoiceBranchCatalogTests
{
    [Fact]
    public async Task Branch_catalog_should_sync_search_filter_and_preview_without_false_folder_mismatch()
    {
        var tenant = new TenantContext();
        tenant.SetHostAdminForTest();
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new InMemoryAppDbContext(options, tenant, new Actor());
        db.Stores.Add(new Store { Id = 1, Name = "Store", SubDomain = "store", SubDomainNormalized = "STORE", IsActive = true });
        var buyer = new LegalEntity
        {
            Id = 2, StoreId = 1, Code = "LE", Name = "Buyer", LegalName = "Buyer",
            TaxCode = "0101234567", IsActive = true
        };
        db.LegalEntities.Add(buyer);
        // SQL Server computes this column; the in-memory fixture must supply it.
        db.Entry(buyer).Property(x => x.NormalizedTaxCode).CurrentValue = "0101234567";
        await db.SaveChangesAsync();
        tenant.SetStore(1, "store");

        var root = Path.Combine(Path.GetTempPath(), $"gaoapp-branch-catalog-{Guid.NewGuid():N}");
        var folder = Path.Combine(root, "2026", "3700723994-042", "07");
        Directory.CreateDirectory(folder);
        try
        {
            var xml = Encoding.UTF8.GetBytes("""
                <HDon><DLHDon><TTChung><KHHDon>C26TAA</KHHDon><SHDon>0042</SHDon><NLap>2026-07-02</NLap></TTChung>
                <NDHDon><NBan><Ten>Branch</Ten><MST>3700723994-042</MST></NBan><NMua><Ten>Buyer</Ten><MST>0101234567</MST></NMua>
                <DSHHDVu><HHDVu><STT>1</STT><THHDVu>Rice</THHDVu><SLuong>1</SLuong><DGia>100</DGia><ThTien>100</ThTien><TSuat>8%</TSuat></HHDVu></DSHHDVu>
                <TToan><TgTCThue>100</TgTCThue><TgTThue>8</TgTThue><TgTTTBSo>108</TgTTTBSo></TToan></NDHDon></DLHDon></HDon>
                """);
            var pdf = Encoding.ASCII.GetBytes("%PDF-branch-catalog");
            await File.WriteAllBytesAsync(Path.Combine(folder, "branch.xml"), xml);
            await File.WriteAllBytesAsync(Path.Combine(folder, "branch.pdf"), pdf);
            var parser = new InputInvoiceXmlDocumentParser();
            var library = new FileSystemInputInvoiceDocumentLibrary(
                Options.Create(new InputInvoiceLibraryOptions { Enabled = true, RootPath = root }), parser);
            var service = new InputInvoiceCatalogService(db, library, parser);
            var access = new InvoiceCatalogAccess(true, true, true);

            var synced = await service.SyncAsync(1, 2026, 7, default);
            Assert.Equal(1, synced.Added);
            Assert.Equal(0, synced.Unavailable);
            Assert.Equal(0, synced.FolderMismatches);
            var entry = Assert.Single(await db.InputInvoiceLibraryEntries.ToListAsync());
            Assert.Equal("3700723994-042", entry.SourceSupplierTaxCode);
            Assert.Equal("3700723994042", entry.SellerTaxCode);
            // InMemory does not generate SQL Server rowversions for indexed entries.
            entry.RowVersion = [1];
            await db.SaveChangesAsync();

            var list = JsonSerializer.SerializeToElement(await service.ListAsync(1,
                new InvoiceCatalogFilter { Search = "3700723994-042", Supplier = "3700723994-042" }, access, default));
            Assert.Equal(1, list.GetProperty("total").GetInt32());
            Assert.False(Assert.Single(list.GetProperty("rows").EnumerateArray()).GetProperty("FolderMismatch").GetBoolean());
            var detail = JsonSerializer.SerializeToElement(await service.DetailAsync(1, entry.Id, access, default));
            Assert.False(detail.GetProperty("FolderMismatch").GetBoolean());
            Assert.Equal(xml, await service.XmlAsync(1, entry.Id, access, default));
            Assert.Equal(pdf, (await service.PdfAsync(1, entry.Id, access, default)).Content);
            Assert.Equal("3700723994-042", (await service.PreviewAsync(1, entry.Id, access, default)).SellerTaxCode);

            var second = await service.SyncAsync(1, 2026, 7, default);
            Assert.Equal(0, second.Added);
            Assert.Equal(1, second.Existing);
            Assert.Equal(0, second.FolderMismatches);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Actor : ICurrentUser
    {
        public int? UserId => 1;
        public string? UserName => "branch-catalog";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
