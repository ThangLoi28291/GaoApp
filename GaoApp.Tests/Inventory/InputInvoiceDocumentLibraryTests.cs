using GaoApp.Application.Common.Options;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Services.Inventory;
using GaoApp.Infrastructure.Services.Invoices;
using Microsoft.Extensions.Options;
using System.Text;

namespace GaoApp.Tests.Inventory;

// R2.4-C2 coverage: document candidates expose buyer-owner evidence without hiding invalid previews.
// R2.4-C2 coverage: document candidates expose buyer-owner evidence without hiding invalid previews.
public sealed class InputInvoiceDocumentLibraryTests
{
    [Fact]
    public async Task Pdf_xml_and_original_siblings_should_be_one_selectable_candidate()
    {
        using var fixture = new LibraryFixture();
        fixture.Write("2026/0312770607/07/02_C26MVP_7941.pdf", "%PDF-synthetic");
        fixture.Write("2026/0312770607/07/02_C26MVP_7941.xml", Xml("0312770607", "7941").Replace("Supplier synthetic", "Normal XML", StringComparison.Ordinal));
        fixture.Write("2026/0312770607/07/02_C26MVP_7941_misa_original.xml", Xml("0312770607", "7941").Replace("Supplier synthetic", "Original XML", StringComparison.Ordinal));

        var result = await fixture.Library.BrowseAsync(
            "0312770607",
            new InputInvoicePickerBrowseRequest { Year = 2026, Month = 7 });

        var candidate = Assert.Single(result);
        Assert.True(candidate.HasPdf);
        Assert.True(candidate.HasXml);
        Assert.True(candidate.SelectionAllowed);
        Assert.Equal("Normal XML", candidate.SellerName);
        Assert.Equal("2026:07:02_C26MVP_7941", candidate.DocumentKey);
        Assert.DoesNotContain(fixture.Root, candidate.DocumentKey, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("2026:07:../outside")]
    [InlineData("C:\\outside.xml")]
    [InlineData("2026:07:../3700723994-042:invoice")]
    [InlineData("2026:07:3700723994-042:../outside")]
    [InlineData("2026:07:3700723994-042:invoice:extra")]
    public async Task Logical_key_traversal_or_rooted_path_should_be_rejected(string key)
    {
        using var fixture = new LibraryFixture();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            fixture.Library.ResolveAsync("0312770607", key));
    }

    [Fact]
    public async Task Original_xml_should_be_fallback_when_normal_variant_is_missing_or_invalid()
    {
        using var fixture = new LibraryFixture();
        fixture.Write("2026/0312770607/07/fallback.xml", "not xml");
        fixture.Write("2026/0312770607/07/fallback_misa_original.xml", Xml("0312770607", "7001"));

        var candidate = Assert.Single(await fixture.Library.BrowseAsync(
            "0312770607", new InputInvoicePickerBrowseRequest { Year = 2026, Month = 7 }));

        Assert.True(candidate.SelectionAllowed);
        Assert.Equal("7001", candidate.InvoiceNumber);
        var selectedBytes = await fixture.Library.GetXmlBytesAsync(
            "0312770607", candidate.DocumentKey);
        Assert.Contains("7001", Encoding.UTF8.GetString(selectedBytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conflicting_normal_and_original_xml_should_be_visible_but_blocked()
    {
        using var fixture = new LibraryFixture();
        fixture.Write("2026/0312770607/07/conflict.pdf", "%PDF-synthetic");
        fixture.Write("2026/0312770607/07/conflict.xml", Xml("0312770607", "7001"));
        fixture.Write("2026/0312770607/07/conflict_misa_original.xml", Xml("0312770607", "7002"));

        var candidate = Assert.Single(await fixture.Library.BrowseAsync(
            "0312770607", new InputInvoicePickerBrowseRequest { Year = 2026, Month = 7 }));

        Assert.True(candidate.HasPdf);
        Assert.True(candidate.HasXml);
        Assert.False(candidate.XmlValid);
        Assert.False(candidate.SelectionAllowed);
        Assert.Equal("ConflictingXmlVariants", candidate.SelectionBlockReasonCode);
    }

    [Fact]
    public async Task Pdf_only_should_preview_but_not_select_while_xml_only_should_select()
    {
        using var fixture = new LibraryFixture();
        fixture.Write("2026/0312770607/07/pdf-only.pdf", "%PDF-synthetic");
        fixture.Write("2026/0312770607/07/xml-only.xml", Xml("0312770607", "7003"));

        var candidates = await fixture.Library.BrowseAsync(
            "0312770607", new InputInvoicePickerBrowseRequest { Year = 2026, Month = 7 });
        var pdf = candidates.Single(x => x.DocumentKey.EndsWith("pdf-only", StringComparison.Ordinal));
        var xml = candidates.Single(x => x.DocumentKey.EndsWith("xml-only", StringComparison.Ordinal));

        Assert.True(pdf.HasPdf);
        Assert.False(pdf.SelectionAllowed);
        Assert.Equal("MissingOrInvalidXml", pdf.SelectionBlockReasonCode);
        Assert.False(xml.HasPdf);
        Assert.True(xml.SelectionAllowed);
    }

    [Fact]
    public async Task Wrong_seller_tax_code_in_supplier_partition_should_be_blocked()
    {
        using var fixture = new LibraryFixture();
        fixture.Write("2026/0312770607/07/wrong.xml", Xml("0319999999", "7004"));

        var candidate = Assert.Single(await fixture.Library.BrowseAsync(
            "0312770607", new InputInvoicePickerBrowseRequest { Year = 2026, Month = 7 }));

        Assert.True(candidate.XmlValid);
        Assert.False(candidate.FolderTaxCodeMatches);
        Assert.False(candidate.SelectionAllowed);
        Assert.Equal("SellerTaxCodeMismatch", candidate.SelectionBlockReasonCode);
    }

    [Fact]
    public async Task Same_business_identity_in_multiple_month_partitions_should_be_deduplicated()
    {
        using var fixture = new LibraryFixture();
        fixture.Write("2026/0312770607/06/copy.xml", Xml("0312770607", "7005"));
        fixture.Write("2026/0312770607/07/copy.pdf", "%PDF-synthetic");
        fixture.Write("2026/0312770607/07/copy.xml", Xml("0312770607", "7005"));

        var candidates = await fixture.Library.BrowseAsync(
            "0312770607", new InputInvoicePickerBrowseRequest { Year = 2026 });

        var candidate = Assert.Single(candidates);
        Assert.True(candidate.HasPdf);
    }

    [Theory]
    [InlineData("3700723994-042", "3700723994-042", "3700723994-042")]
    [InlineData("3700723994042", "3700723994-042", "3700723994042")]
    [InlineData("3700723994-042", "3700723994042", "3700723994-042")]
    [InlineData("3700723994042", "3700723994042", "3700723994042")]
    public async Task Branch_tax_code_should_browse_and_preview_both_folder_spellings(
        string supplierTaxCode, string folderTaxCode, string xmlTaxCode)
    {
        using var fixture = new LibraryFixture();
        var xml = Xml(xmlTaxCode, "0042");
        fixture.Write($"2026/{folderTaxCode}/07/branch.xml", xml);
        fixture.Write($"2026/{folderTaxCode}/07/branch.pdf", "%PDF-branch");

        var candidate = Assert.Single(await fixture.Library.BrowseAsync(
            supplierTaxCode, new InputInvoicePickerBrowseRequest { Year = 2026, Month = 7 }));

        Assert.True(candidate.SelectionAllowed);
        Assert.True(candidate.FolderTaxCodeMatches);
        Assert.Equal(folderTaxCode, candidate.SourceSupplierTaxCode);
        Assert.Equal(xmlTaxCode, candidate.SellerTaxCode);
        Assert.Equal("0042", (await fixture.Library.ResolveAsync(supplierTaxCode, candidate.DocumentKey)).InvoiceNumber);
        Assert.Contains(xml, Encoding.UTF8.GetString(await fixture.Library.GetXmlBytesAsync(supplierTaxCode, candidate.DocumentKey)), StringComparison.Ordinal);
        Assert.Equal("%PDF-branch", Encoding.UTF8.GetString((await fixture.Library.GetPdfAsync(supplierTaxCode, candidate.DocumentKey)).Content).TrimStart('\uFEFF'));
        // Existing keys continue to resolve when only the hyphenated folder exists.
        Assert.Equal("0042", (await fixture.Library.ResolveAsync(supplierTaxCode, "2026:07:branch")).InvoiceNumber);
    }

    [Fact]
    public async Task Both_branch_folders_with_same_file_name_should_preview_their_own_source()
    {
        using var fixture = new LibraryFixture();
        fixture.Write("2026/3700723994042/07/same.xml", Xml("3700723994-042", "0042"));
        fixture.Write("2026/3700723994-042/07/same.xml", Xml("3700723994-042", "0043"));

        var candidates = await fixture.Library.BrowseAsync("3700723994-042",
            new InputInvoicePickerBrowseRequest { Year = 2026, Month = 7 });

        Assert.Equal(2, candidates.Count);
        Assert.Equal(2, candidates.Select(x => x.DocumentKey).Distinct().Count());
        foreach (var candidate in candidates)
            Assert.Equal(candidate.InvoiceNumber,
                (await fixture.Library.ResolveAsync("3700723994042", candidate.DocumentKey)).InvoiceNumber);
    }

    [Fact]
    public async Task Alias_copies_should_deduplicate_without_including_parent_or_other_branches()
    {
        using var fixture = new LibraryFixture();
        fixture.Write("2026/3700723994042/07/copy.xml", Xml("3700723994042", "0042"));
        fixture.Write("2026/3700723994-042/07/copy.xml", Xml("3700723994-042", "0042"));
        fixture.Write("2026/3700723994-042/07/copy.pdf", "%PDF-branch");
        fixture.Write("2026/3700723994/07/parent.xml", Xml("3700723994", "0042"));
        fixture.Write("2026/3700723994-043/07/other.xml", Xml("3700723994-043", "0042"));

        var candidate = Assert.Single(await fixture.Library.BrowseAsync("3700723994-042",
            new InputInvoicePickerBrowseRequest { Year = 2026, Month = 7 }));

        Assert.True(candidate.HasPdf);
        Assert.True(candidate.SelectionAllowed);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            fixture.Library.ResolveAsync("3700723994-043", candidate.DocumentKey));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            fixture.Library.ResolveAsync("3700723994", candidate.DocumentKey));
    }

    [Fact]
    public async Task Branch_folder_must_still_block_xml_for_a_different_branch()
    {
        using var fixture = new LibraryFixture();
        fixture.Write("2026/3700723994-042/07/wrong.xml", Xml("3700723994-043", "0042"));

        var candidate = Assert.Single(await fixture.Library.BrowseAsync("3700723994-042",
            new InputInvoicePickerBrowseRequest { Year = 2026, Month = 7 }));

        Assert.True(candidate.XmlValid);
        Assert.False(candidate.FolderTaxCodeMatches);
        Assert.False(candidate.SelectionAllowed);
        Assert.Equal("SellerTaxCodeMismatch", candidate.SelectionBlockReasonCode);
    }

    [Fact]
    public async Task Catalog_should_include_hyphenated_branch_folders_and_keep_readable_source_keys()
    {
        using var fixture = new LibraryFixture(maxCandidates: 1);
        fixture.Write("2026/3700723994-042/07/branch.xml", Xml("3700723994-042", "0042"));
        fixture.Write("2026/3700723994-042/07/second.xml", Xml("3700723994042", "0043"));
        fixture.Write("2026/not-a-tax-folder/07/ignore.xml", Xml("3700723994-042", "0044"));
        var candidates = new List<InputInvoicePickerCandidateDto>();

        await foreach (var candidate in fixture.Library.EnumerateCatalogAsync(2026, 7, default))
            candidates.Add(candidate);

        Assert.Equal(2, candidates.Count);
        foreach (var candidate in candidates)
        {
            Assert.True(candidate.FolderTaxCodeMatches);
            Assert.Equal("3700723994-042", candidate.SourceSupplierTaxCode);
            Assert.NotEmpty(await fixture.Library.GetXmlBytesAsync(candidate.SourceSupplierTaxCode!, candidate.DocumentKey));
        }
    }

    [Fact]
    public async Task Browse_should_enforce_partition_and_candidate_bounds()
    {
        using var fixture = new LibraryFixture(maxMonthPartitions: 2, maxCandidates: 1);
        fixture.Write("2026/0312770607/07/a.xml", Xml("0312770607", "7101"));
        fixture.Write("2026/0312770607/07/b.xml", Xml("0312770607", "7102"));

        var candidates = await fixture.Library.BrowseAsync(
            "0312770607", new InputInvoicePickerBrowseRequest { Year = 2026, Month = 7 });
        Assert.Single(candidates);

        await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Library.BrowseAsync(
            "0312770607",
            new InputInvoicePickerBrowseRequest
            {
                FromDate = new DateTime(2026, 1, 1),
                ToDate = new DateTime(2026, 3, 1)
            }));
    }

    [Theory]
    [InlineData("0312770607")]
    [InlineData("3700723994-042")]
    public async Task Reparse_month_partition_should_be_rejected_when_platform_allows_creation(string supplierTaxCode)
    {
        using var fixture = new LibraryFixture();
        var outside = Path.Combine(Path.GetTempPath(), $"gaoapp-picker-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        var link = Path.Combine(fixture.Root, "2026", supplierTaxCode, "07");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(outside, recursive: true);
            return;
        }
        catch (PlatformNotSupportedException)
        {
            Directory.Delete(outside, recursive: true);
            return;
        }
        catch (IOException)
        {
            Directory.Delete(outside, recursive: true);
            return;
        }

        try
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Library.BrowseAsync(
                supplierTaxCode, new InputInvoicePickerBrowseRequest { Year = 2026, Month = 7 }));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(outside, recursive: true);
        }
    }

    private static string Xml(string sellerTaxCode, string number) => $$"""
        <HDon><DLHDon><TTChung><KHMSHDon>1</KHMSHDon><KHHDon>C26MVP</KHHDon><SHDon>{{number}}</SHDon><NLap>2026-07-02</NLap></TTChung><NDHDon><NBan><Ten>Supplier synthetic</Ten><MST>{{sellerTaxCode}}</MST></NBan><NMua><Ten>Buyer synthetic</Ten></NMua><DSHHDVu><HHDVu><STT>1</STT><THHDVu>Rice</THHDVu><DVTinh>kg</DVTinh><SLuong>1</SLuong><DGia>100</DGia><ThTien>100</ThTien><TSuat>8%</TSuat></HHDVu></DSHHDVu><TToan><TgTCThue>100</TgTCThue><TgTThue>8</TgTThue><TgTTTBSo>108</TgTTTBSo></TToan></NDHDon></DLHDon></HDon>
        """;

    private sealed class LibraryFixture : IDisposable
    {
        public LibraryFixture(int maxMonthPartitions = 12, int maxCandidates = 200)
        {
            Root = Path.Combine(Path.GetTempPath(), $"gaoapp-picker-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Library = new FileSystemInputInvoiceDocumentLibrary(
                Options.Create(new InputInvoiceLibraryOptions
                {
                    Enabled = true,
                    RootPath = Root,
                    MaxMonthPartitions = maxMonthPartitions,
                    MaxCandidates = maxCandidates
                }),
                new InputInvoiceXmlDocumentParser());
        }

        public string Root { get; }
        public FileSystemInputInvoiceDocumentLibrary Library { get; }

        public void Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
