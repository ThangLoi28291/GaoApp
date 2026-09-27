using System.Reflection;
using System.Text.RegularExpressions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Ui;

public sealed class InvoiceManualIssuanceUiContractTests
{
    private const string ViewPath = "GaoApp.Web/Areas/Admin/Views/Invoice/Index.cshtml";
    private const string RepositoryPath = "GaoApp.Infrastructure/Repositories/Invoices/InvoiceRepository.cs";

    [Fact]
    public void Display_modes_preserve_existing_numeric_values_and_append_manual_workspaces()
    {
        var expected = new Dictionary<string, int>
        {
            ["All"] = 0, ["HasQuantity"] = 1, ["Empty"] = 2, ["Locked"] = 3,
            ["Unlocked"] = 4, ["HasAutoLines"] = 5, ["HasManualLines"] = 6,
            ["ManualWaitingInfo"] = 7, ["ManualReady"] = 8, ["ManualNeedsAttention"] = 9
        };
        var source = Read("GaoApp.Domain/Enums/InvoiceListDisplayMode.cs");
        foreach (var (name, value) in expected)
        {
            Assert.Equal(value, (int)Enum.Parse<InvoiceListDisplayMode>(name));
            Assert.Matches($@"\b{name}\s*=\s*{value}\b", source);
        }
    }

    [Fact]
    public void Navigation_exposes_exactly_all_and_three_manual_workspaces_with_filter_preservation()
    {
        var view = Read(ViewPath);
        var workspaceDefinition = view[view.IndexOf("var workspaces =", StringComparison.Ordinal)..
            view.IndexOf("var listTitle =", StringComparison.Ordinal)];
        Assert.Equal(new[] { "All", "ManualWaitingInfo", "ManualReady", "ManualNeedsAttention" },
            Regex.Matches(workspaceDefinition, @"\[InvoiceListDisplayMode\.(\w+)\]")
                .Select(match => match.Groups[1].Value).ToArray());
        foreach (var label in new[] { "Tất cả hóa đơn", "Chờ khách nhập thông tin", "Sẵn sàng phát hành", "Cần xử lý" })
            Assert.Contains(label, workspaceDefinition);
        Assert.Contains("@foreach (var workspace in workspaces)", view);
        Assert.Contains("href=\"@PageUrl(1, workspace.Key)\"", view);
        foreach (var parameter in new[] { "pageSize = q.PageSize", "fromDate = q.FromDate", "toDate = q.ToDate",
                     "orderId = q.OrderId", "keyword = q.Keyword", "sortMode = (int)q.SortMode",
                     "displayMode = (int)(displayMode ?? q.DisplayMode)" })
            Assert.Contains(parameter, view);
        foreach (var field in new[] { "Keyword", "FromDate", "ToDate", "OrderId", "PageSize", "SortMode", "DisplayMode" })
            Assert.Contains($"name=\"{field}\"", view);
    }

    [Fact]
    public void Manual_ready_only_links_to_payload_and_other_actions_keep_detail_navigation()
    {
        var view = Read(ViewPath);
        var actions = view[view.IndexOf("<td class=\"invoice-cell-action\">", StringComparison.Ordinal)..];
        var ready = actions[..actions.IndexOf("else if", StringComparison.Ordinal)];
        Assert.Contains("item.InvoiceIssuanceRoute == InvoiceIssuanceRoute.Manual", ready);
        Assert.Contains("q.DisplayMode == InvoiceListDisplayMode.ManualReady", ready);
        Assert.Contains("asp-action=\"ViettelPayload\"", ready);
        Assert.Contains("asp-route-id=\"@item.Id\"", ready);
        Assert.Contains("Chuẩn bị phát hành", ready);
        Assert.DoesNotContain("asp-action=\"Detail\"", ready);
        Assert.DoesNotContain("ViettelIssue", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("(?i)method\\s*=\\s*[\"']post[\"']", view);
        Assert.Matches("(?s)ManualWaitingInfo.*?asp-action=\"Detail\"[^>]*>Nhập thông tin</a>", actions);
        Assert.Matches("(?s)ManualNeedsAttention.*?asp-action=\"Detail\"[^>]*>Xử lý</a>", actions);
        Assert.Contains("aria-label=\"Xem chi tiết hóa đơn @item.Id\"", actions);
        Assert.Contains("Chi tiết <i", actions);
    }

    [Fact]
    public void View_keeps_route_badges_workspace_headers_and_encodes_error_messages()
    {
        var view = Read(ViewPath);
        Assert.Contains("case InvoiceIssuanceRoute.Automatic:", view);
        Assert.Contains("Tự động", view);
        Assert.Contains("case InvoiceIssuanceRoute.Manual:", view);
        Assert.Contains("Thủ công", view);
        foreach (var title in new[] { "Chờ khách nhập thông tin", "Sẵn sàng phát hành thủ công", "Hóa đơn cần xử lý", "Danh sách hóa đơn" })
            Assert.Contains(title, view);
        Assert.Contains(">@listTitle</h2>", view);
        foreach (var badge in new[] { "Chờ thông tin", "Sẵn sàng", "Cần xử lý" })
            Assert.Contains($">{badge}</span>", view);
        Assert.Contains("!string.IsNullOrWhiteSpace(item.LastErrorMessage)", view);
        Assert.Contains(">@item.LastErrorMessage</span>", view);
        Assert.DoesNotContain("Html.Raw", view);
    }

    [Fact]
    public void Repository_source_routes_all_three_modes_through_manual_guard_before_paging()
    {
        var source = Read(RepositoryPath);
        Assert.Matches(@"(?s)case InvoiceListDisplayMode.ManualWaitingInfo:\s*case InvoiceListDisplayMode.ManualReady:\s*case InvoiceListDisplayMode.ManualNeedsAttention:\s*query = ApplyManualWorkspaceFilter\(query, displayMode\);", source);
        var filter = source[source.IndexOf("private static IQueryable<InvoiceHead> ApplyManualWorkspaceFilter", StringComparison.Ordinal)..
            source.IndexOf("public Task<InvoiceHead?> GetInvoiceHeadDetailAsync", StringComparison.Ordinal)];
        Assert.Contains("x.Order != null", filter);
        Assert.Contains("x.Order.InvoiceIssuanceRoute == InvoiceIssuanceRoute.Manual", filter);
        Assert.DoesNotContain("InvoiceIssuanceRoute.Automatic", filter);
        Assert.DoesNotContain("ToList", filter);
        Assert.DoesNotContain("AsEnumerable", filter);
        Assert.True(source.IndexOf("query = ApplyManualWorkspaceFilter", StringComparison.Ordinal) <
                    source.IndexOf("var total = await query.CountAsync", StringComparison.Ordinal));
        Assert.Contains(".Skip((page - 1) * pageSize)", source);
        Assert.Contains(".Take(pageSize)", source);
    }

    [Theory]
    [InlineData(InvoiceListDisplayMode.All)]
    [InlineData(InvoiceListDisplayMode.ManualWaitingInfo)]
    [InlineData(InvoiceListDisplayMode.ManualReady)]
    [InlineData(InvoiceListDisplayMode.ManualNeedsAttention)]
    public async Task Repository_classifies_buyers_provider_states_errors_and_details_exclusively(InvoiceListDisplayMode mode)
    {
        await using var db = CreateDb();
        var cases = ClassificationCases();
        // Repeat every state for Automatic, Unselected and missing Order to prove exclusion,
        // while All remains a registry for every route and provider state.
        var rows = new List<(InvoiceHead Head, InvoiceListDisplayMode? Expected)>();
        var id = 0;
        foreach (var route in new InvoiceIssuanceRoute?[] { InvoiceIssuanceRoute.Manual, InvoiceIssuanceRoute.Automatic,
                     InvoiceIssuanceRoute.Unselected, null })
        {
            foreach (var (_, change, expected) in cases)
            {
                var head = NewInvoice(++id, route);
                change(head);
                rows.Add((head, route == InvoiceIssuanceRoute.Manual ? expected : null));
            }
        }
        var deletedDetails = rows.SelectMany(row => row.Head.Details).Where(detail => detail.IsDeleted).ToList();
        db.InvoiceHeads.AddRange(rows.Select(row => row.Head));
        await db.SaveChangesAsync();
        // Added entities have IsDeleted reset by AppDbContext; soft-delete after insertion.
        db.InvoiceDetails.RemoveRange(deletedDetails);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Query(new InvoiceRepository(db), mode);
        var expectedIds = rows.Where(row => mode == InvoiceListDisplayMode.All || row.Expected == mode)
            .Select(row => row.Head.Id).OrderBy(value => value).ToArray();
        Assert.Equal(expectedIds.Length, result.Total);
        Assert.Equal(expectedIds, result.Items.Select(head => head.Id).OrderBy(value => value).ToArray());
        if (mode != InvoiceListDisplayMode.All)
            Assert.All(result.Items, head => Assert.Equal(InvoiceIssuanceRoute.Manual, head.Order!.InvoiceIssuanceRoute));
    }

    [Fact]
    public async Task Repository_combines_workspace_with_dates_order_keyword_sort_and_paging()
    {
        await using var db = CreateDb();
        var day = new DateTime(2026, 9, 20);
        var rows = Enumerable.Range(1, 5).Select(id => NewInvoice(id)).ToArray();
        foreach (var row in rows)
        {
            row.InvoiceDate = day.AddHours(12);
            row.BuyerName = "Matched buyer";
        }
        rows[3].InvoiceDate = day.AddDays(1);
        rows[4].BuyerName = "Other buyer";
        db.InvoiceHeads.AddRange(rows);
        await db.SaveChangesAsync();
        var repository = new InvoiceRepository(db);
        var page = await repository.QueryInvoiceHeadsAsync(day, day, null, " Matched ",
            InvoiceListDisplayMode.ManualReady, InvoiceListSortMode.OrderIdAsc, 2, 1);
        Assert.Equal(3, page.Total);
        Assert.Equal(2, Assert.Single(page.Items).Id);
        var byOrder = await repository.QueryInvoiceHeadsAsync(day, day, 3, "Matched",
            InvoiceListDisplayMode.ManualReady, InvoiceListSortMode.OrderIdDesc, 1, 20);
        Assert.Equal(1, byOrder.Total);
        Assert.Equal(3, Assert.Single(byOrder.Items).Id);
    }

    [Fact]
    public async Task List_mapping_preserves_route_provider_status_errors_and_missing_order_fallback()
    {
        await using var db = CreateDb();
        var manual = NewInvoice(1);
        manual.ProviderStatus = InvoiceProviderStatus.IssueFailed;
        manual.LastErrorCode = "TIMEOUT";
        manual.LastErrorMessage = "<script>alert('provider error')</script>";
        db.InvoiceHeads.AddRange(manual, NewInvoice(2, null));
        await db.SaveChangesAsync();
        var result = await new InvoiceReadService(new InvoiceRepository(db), null!).GetInvoicesAsync(new InvoiceListQueryDto());
        Assert.True(result.IsSuccess);
        var dto = Assert.Single(result.Value.Items, item => item.Id == 1);
        Assert.Equal(InvoiceIssuanceRoute.Manual, dto.InvoiceIssuanceRoute);
        Assert.Equal(manual.ProviderStatus, dto.ProviderStatus);
        Assert.Equal(manual.LastErrorCode, dto.LastErrorCode);
        Assert.Equal(manual.LastErrorMessage, dto.LastErrorMessage);
        Assert.Equal(InvoiceIssuanceRoute.Unselected,
            Assert.Single(result.Value.Items, item => item.Id == 2).InvoiceIssuanceRoute);
    }

    [Theory]
    [InlineData(InvoiceListDisplayMode.ManualWaitingInfo)]
    [InlineData(InvoiceListDisplayMode.ManualReady)]
    [InlineData(InvoiceListDisplayMode.ManualNeedsAttention)]
    public void Manual_filters_translate_to_sql_server_without_opening_a_database(InvoiceListDisplayMode mode)
    {
        // ToQueryString only compiles SQL; this context never opens a connection or migrates a DB.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=TranslationOnly;Integrated Security=true;TrustServerCertificate=true")
            .Options, new Tenant(), new User());
        var method = typeof(InvoiceRepository).GetMethod("ApplyManualWorkspaceFilter", BindingFlags.NonPublic | BindingFlags.Static)!;
        var query = (IQueryable<InvoiceHead>)method.Invoke(null, new object[] { db.InvoiceHeads.AsQueryable(), mode })!;
        var sql = query.Include(head => head.Order).Include(head => head.LegalEntity).Include(head => head.Details)
            .OrderBy(head => head.Id).Skip(20).Take(20).ToQueryString();
        Assert.Matches($@"\[InvoiceIssuanceRoute\] = (?:CAST\()?{(byte)InvoiceIssuanceRoute.Manual}(?: AS tinyint\))?(?!\d)", sql);
        Assert.Contains("[ProviderStatus]", sql);
        Assert.Contains("[IssuedAtUtc] IS NULL", sql);
        if (mode != InvoiceListDisplayMode.ManualNeedsAttention)
            Assert.Contains("[BuyerAddress]", sql);
        Assert.Contains("EXISTS", sql);
        Assert.Contains("OFFSET", sql);
        Assert.Contains("FETCH NEXT", sql);
    }

    private static List<(string Name, Action<InvoiceHead> Change, InvoiceListDisplayMode? Expected)> ClassificationCases()
    {
        const InvoiceListDisplayMode ready = InvoiceListDisplayMode.ManualReady;
        const InvoiceListDisplayMode waiting = InvoiceListDisplayMode.ManualWaitingInfo;
        const InvoiceListDisplayMode attention = InvoiceListDisplayMode.ManualNeedsAttention;
        var cases = new List<(string, Action<InvoiceHead>, InvoiceListDisplayMode?)>
        {
            ("individual without optional contacts", _ => { }, ready),
            ("locked safe draft", head => head.IsLocked = true, ready),
            ("missing name", head => head.BuyerName = null, waiting),
            ("empty name", head => head.BuyerName = "", waiting),
            ("whitespace name", head => head.BuyerName = "   ", waiting),
            ("missing address", head => head.BuyerAddress = null, waiting),
            ("whitespace address", head => head.BuyerAddress = "   ", waiting),
            ("no invoice buyer", head => head.BuyerType = InvoiceBuyerTypes.NoInvoice, waiting),
            ("unknown buyer", head => head.BuyerType = "Unknown", waiting),
            ("business", BusinessBuyer, ready),
            ("business missing legal name", head => { BusinessBuyer(head); head.BuyerLegalName = null; }, waiting),
            ("business whitespace legal name", head => { BusinessBuyer(head); head.BuyerLegalName = " "; }, waiting),
            ("business missing tax code", head => { BusinessBuyer(head); head.BuyerTaxCode = null; }, waiting),
            ("business whitespace tax code", head => { BusinessBuyer(head); head.BuyerTaxCode = " "; }, waiting),
            ("business missing address", head => { BusinessBuyer(head); head.BuyerAddress = null; }, waiting),
            ("error code", head => head.LastErrorCode = "TIMEOUT", attention),
            ("error message and incomplete buyer", head => { head.LastErrorMessage = "Failure"; head.BuyerName = null; }, attention),
            ("blank errors", head => { head.LastErrorCode = " "; head.LastErrorMessage = ""; }, ready),
            ("zero total", head => head.GrandTotal = 0, attention),
            ("negative total", head => head.GrandTotal = -1, attention),
            ("no details", head => head.Details.Clear(), attention),
            ("deleted detail", head => head.Details.Single().IsDeleted = true, attention),
            ("zero quantity", head => head.Details.Single().Quantity = 0, attention),
            ("negative quantity", head => head.Details.Single().Quantity = -1, attention),
            ("zero detail amount", head => head.Details.Single().TotalAmount = 0, attention),
            ("negative detail amount", head => head.Details.Single().TotalAmount = -1, attention),
            ("quantity and total on separate lines", head =>
            {
                head.Details.Single().TotalAmount = 0;
                head.Details.Add(new InvoiceDetail { StoreId = 1, ItemName = "Zero quantity", Quantity = 0, TotalAmount = 100 });
            }, attention),
            ("valid detail alongside invalid line", head => head.Details.Add(new InvoiceDetail
                { StoreId = 1, ItemName = "Invalid line", Quantity = 0, TotalAmount = 0 }), ready),
            ("provider number", head => head.ProviderInvoiceNo = "INV-1", null),
            ("provider number with error", head => { head.ProviderInvoiceNo = "INV-2"; head.LastErrorCode = "ERROR"; }, null),
            ("issued timestamp", head => head.IssuedAtUtc = DateTime.UtcNow, null),
            ("issued timestamp with error", head => { head.IssuedAtUtc = DateTime.UtcNow; head.LastErrorMessage = "ERROR"; }, null)
        };
        foreach (var status in Enum.GetValues<InvoiceProviderStatus>().Append((InvoiceProviderStatus)999))
        {
            InvoiceListDisplayMode? expected = status switch
            {
                InvoiceProviderStatus.LocalDraft or InvoiceProviderStatus.ReadyToIssue or InvoiceProviderStatus.Previewed => ready,
                InvoiceProviderStatus.Issued or InvoiceProviderStatus.PdfDownloaded or InvoiceProviderStatus.ZipDownloaded or InvoiceProviderStatus.EmailSent => null,
                _ => attention
            };
            cases.Add(($"provider state {status}", head => head.ProviderStatus = status, expected));
            cases.Add(($"provider state {status} with missing buyer", head =>
            {
                head.ProviderStatus = status;
                head.BuyerName = null;
            }, expected == ready ? waiting : expected));
        }
        return cases;
    }

    private static void BusinessBuyer(InvoiceHead head)
    {
        head.BuyerType = InvoiceBuyerTypes.Business;
        head.BuyerName = null;
        head.BuyerLegalName = "Company";
        head.BuyerTaxCode = "0101234567";
    }

    private static InvoiceHead NewInvoice(int id, InvoiceIssuanceRoute? route = InvoiceIssuanceRoute.Manual) => new()
    {
        Id = id, StoreId = 1,
        Order = route.HasValue ? new Order { Id = id, StoreId = 1, POSShiftId = 1, InvoiceIssuanceRoute = route.Value } : null,
        BuyerType = InvoiceBuyerTypes.Individual, BuyerName = "Buyer", BuyerAddress = "Address",
        GrandTotal = 100, ProviderStatus = InvoiceProviderStatus.LocalDraft,
        Details = new List<InvoiceDetail>
        {
            new() { StoreId = 1, ItemName = "Item", Quantity = 1, TotalAmount = 100 }
        }
    };

    private static InMemoryAppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<InMemoryAppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Tenant(), new User());

    private static Task<(List<InvoiceHead> Items, int Total)> Query(InvoiceRepository repository, InvoiceListDisplayMode mode) =>
        repository.QueryInvoiceHeadsAsync(null, null, null, null, mode, InvoiceListSortMode.OrderIdAsc, 1, 1000);

    private static string Read(string path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
                return File.ReadAllText(Path.Combine(directory.FullName, path));
        throw new InvalidOperationException("Could not find GaoApp repository root.");
    }

    private sealed class Tenant : ITenantContext
    {
        public int? StoreId => 1;
        public bool IsHostAdmin => false;
        public string? Subdomain => "test";
    }

    private sealed class User : ICurrentUser
    {
        public int? UserId => 9;
        public string? UserName => "manual-workspace-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
