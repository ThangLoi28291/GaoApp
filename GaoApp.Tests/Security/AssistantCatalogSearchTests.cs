using GaoApp.Web.Services.Assistant;

namespace GaoApp.Tests.Security;

public sealed class AssistantCatalogSearchTests
{
    private static SearchDocument Doc(int id, string name, string? brand = null, params string[] aliases) =>
        new(id, id + 100, name, "SKU-" + id, AssistantCatalogSearch.Infer(name, brand), aliases);
    private static readonly SearchDocument[] Products = [
        Doc(1, "Sữa tươi TH ít đường hộp 180ml"), Doc(2, "Sữa tươi TH không đường hộp 180ml"),
        Doc(3, "Sữa tươi TH có đường hộp 180ml"), Doc(4, "Sữa tươi TH có đường hộp 200ml"),
        Doc(5, "Sữa tươi TH có đường hộp 1L"), Doc(6, "Sữa tươi Vinamilk có đường hộp 180ml"),
        Doc(7, "Nước ngọt Pepsi lon 330ml", "Pepsi"), Doc(8, "Nước rửa chén Sunlight chanh chai 750g", "Sunlight"),
        Doc(9, "Gạo ST25 túi 5kg"), Doc(10, "Mì Hảo Hảo tôm chua cay gói 75g")
    ];
    private static CatalogRankedPage Search(string query, SearchDocument[]? documents = null, int limit = 20, string? cursor = null, string store = "1") =>
        AssistantCatalogSearch.Rank(documents ?? Products, new() { Query = query, Limit = limit, Cursor = cursor }, store);
    [Theory]
    [InlineData("sua tuoi th co duong 180ml", 3)]
    [InlineData("SỮA TƯƠI TH KHÔNG ĐƯỜNG 180 ML", 2)]
    [InlineData("TH có đường 1000ml", 5)]
    [InlineData("gạo ST25 5000g", 9)]
    [InlineData("mì Hảo Hảo tôm chua cay", 10)]
    [InlineData("Sunlight chanh 750g", 8)]
    [InlineData("SKU-7", 7)]
    public void Names_units_accents_and_sku_resolve_the_actual_variant(string query, int id)
    {
        var page = Search(query); Assert.Equal(id, Assert.Single(page.Hits).Document.Id); Assert.False(page.Search.RequiresConfirmation);
    }
    [Theory]
    [InlineData("TH không đường 200ml")]
    [InlineData("TH có đường 190ml")]
    [InlineData("Pepsi 3300ml")]
    [InlineData("Sunlight cam 750g")]
    [InlineData("TH có đường không đường 180ml")]
    [InlineData("Gạo ST25 5kg 10kg")]
    [InlineData("hàng không tồn tại xyz")]
    public void Missing_or_conflicting_attributes_are_never_silently_replaced(string query) => Assert.Empty(Search(query).Hits);
    [Fact]
    public void Slight_spelling_errors_offer_confirmation_instead_of_an_automatic_quote()
    {
        var page = Search("Pepsy 330ml"); Assert.Equal(7, Assert.Single(page.Hits).Document.Id);
        Assert.Equal("fuzzy", page.Search.MatchKind); Assert.True(page.Search.RequiresConfirmation);
    }
    [Fact]
    public void Broad_query_preserves_all_sugar_and_capacity_choices()
    {
        var page = Search("sữa TH"); Assert.Equal(new[] { 1, 2, 3, 4, 5 }, page.Hits.Select(h => h.Document.Id).Order());
    }
    [Fact]
    public void Exact_full_name_precedes_lower_ids_with_added_qualifiers()
    {
        var products = new[] { Doc(1, "Bánh quy bơ hương dừa 200g"), Doc(999, "Bánh quy bơ 200g") };
        Assert.Equal(999, Search("bánh quy bơ 200g", products).Hits[0].Document.Id);
    }
    [Fact]
    public void Learned_names_are_candidates_and_do_not_override_explicit_attributes()
    {
        var products = new[] { Doc(1, "Sữa tươi TH có đường 180ml", null, "TH ngọt hộp bé"), Doc(2, "Sữa tươi TH không đường 180ml", null, "TH không đường 200ml") };
        var match = Search("TH ngọt hộp bé", products); Assert.Equal(1, Assert.Single(match.Hits).Document.Id); Assert.True(match.Search.RequiresConfirmation);
        Assert.Empty(Search("TH không đường 200ml", products).Hits);
    }
    [Fact]
    public void Imported_wrong_brand_does_not_make_a_different_product_a_confident_match()
    {
        var documents = new[] { Doc(1, "Nước rửa chén Sunlight chanh 750g", "OMO"), Doc(2, "Nước rửa chén Sunlight chanh 1kg", "Sunlight") };
        Assert.Null(documents[0].Attributes.Brand);
        Assert.Empty(Search("OMO 750g", documents).Hits);
        Assert.Equal(1, Assert.Single(Search("Sunlight 750g", documents).Hits).Document.Id);
    }
    [Fact]
    public void Conflicting_imported_name_and_attribute_only_search_require_review()
    {
        var attributes = AssistantCatalogSearch.Infer("Sữa TH có đường 180ml - Sữa TH không đường 200ml");
        Assert.Null(attributes.Sugar); Assert.Null(attributes.Capacity);
        Assert.True(Search("330ml").Search.RequiresConfirmation);
        var page = AssistantCatalogSearch.Rank(Products, new() { Query = "TH có đường 180ml", Attributes = new() { Sugar = "unsweetened" } }, "1");
        Assert.Empty(page.Hits);
    }
    [Fact]
    public void One_phrase_can_keep_multiple_real_products_ambiguous()
    {
        var products = new[] { Doc(1, "Bánh ABC 100g", null, "bánh nhà hay lấy"), Doc(2, "Bánh XYZ 200g", null, "bánh nhà hay lấy") };
        var match = Search("bánh nhà hay lấy", products); Assert.Equal(2, match.Hits.Length); Assert.True(match.Search.RequiresConfirmation);
    }
    [Fact]
    public void Pagination_is_ranked_without_duplicates_and_bound_to_query_store_and_catalog()
    {
        var page = Search("sữa TH", limit: 2); var ids = page.Hits.Select(h => h.Document.Id).ToList();
        while (page.Cursor is not null) { page = Search("sữa TH", limit: 2, cursor: page.Cursor); ids.AddRange(page.Hits.Select(h => h.Document.Id)); }
        Assert.Equal(5, ids.Count); Assert.Equal(5, ids.Distinct().Count());
        var cursor = Search("sữa TH", limit: 2).Cursor!;
        Assert.Throws<AssistantException>(() => Search("sữa Vinamilk", limit: 2, cursor: cursor));
        Assert.Throws<AssistantException>(() => Search("sữa TH", limit: 2, cursor: cursor, store: "2"));
        Assert.Throws<AssistantException>(() => Search("sữa TH", Products.Append(Doc(99, "Sữa TH mới 120ml")).ToArray(), 2, cursor));
    }
    [Theory]
    [InlineData("200ml")]
    [InlineData("có đường")]
    [InlineData("180")]
    public void A_fragment_alone_is_not_a_teachable_product_name(string query) => Assert.False(AssistantSearchKnowledge.ValidQuery(query));
}

public sealed partial class AssistantIntegrationTests
{
    [Fact]
    public async Task Ranked_search_reads_current_names_and_isolates_active_store_products()
    {
        using var f = new Fixture(); await f.Seed();
        var service = new AssistantCatalogService(f.Db, f.Clock); var who = await f.Access.CurrentAsync(null, default);
        var result = await service.SearchAsync(who, new() { Query = "TH có đường 180ml" }, default);
        Assert.Equal("10", Assert.Single(result.Variants).Product.VariantId); Assert.NotNull(result.Search);
        Assert.False(result.Search.RequiresConfirmation);
        var other = await service.SearchAsync(who with { Scope = new("2", null, null) }, new() { Query = "TH có đường 180ml" }, default);
        Assert.Equal("20", Assert.Single(other.Variants).Product.VariantId);
        var variant = await f.Db.ProductVariants.FindAsync(10); variant!.IsActive = false; await f.Db.SaveChangesAsync();
        Assert.Empty((await service.SearchAsync(who, new() { Query = "TH có đường 180ml" }, default)).Variants);
    }
    [Fact]
    public async Task Corrections_require_approval_persist_per_store_and_can_be_revoked()
    {
        using var f = new Fixture(); await f.Seed();
        var knowledge = new AssistantSearchKnowledge(new EnvironmentStub(f.DirectoryPath), f.Options, f.Clock);
        var who = await f.Access.CurrentAsync(null, default); var catalog = new AssistantCatalogService(f.Db, f.Clock, knowledge);
        var id = Guid.NewGuid(); var proposed = await knowledge.ProposeAsync(1, 1, "sữa ngọt nhà hay lấy", 10, 10, id, null, null, default);
        Assert.Equal(proposed.Id, (await knowledge.ProposeAsync(1, 1, "sữa ngọt nhà hay lấy", 10, 10, id, null, null, default)).Id);
        Assert.Empty((await catalog.SearchAsync(who, new() { Query = proposed.Query }, default)).Variants);
        Assert.Null(await catalog.ApprovedAliasAsync(who, proposed.Query, default));
        Assert.Equal(400, (await Assert.ThrowsAsync<AssistantException>(() => catalog.ApprovedAliasAsync(who, "", default))).Status);
        Assert.Empty((await knowledge.ReadAsync(2, default)).Entries);
        var approved = await knowledge.ReviewAsync(1, 1, proposed.Id, proposed.Version, "approve", Guid.NewGuid(), new(), default);
        Assert.Equal(proposed.Query, await catalog.ApprovedAliasAsync(who, proposed.Query, default));
        Assert.Null(await catalog.ApprovedAliasAsync(who with { Scope = new("2", null, null) }, proposed.Query, default));
        var replay = await knowledge.ProposeAsync(1, 1, "sữa ngọt nhà hay lấy", 10, 10, id, null, null, default);
        Assert.Equal(approved.Id, replay.Id); Assert.Equal("approved", replay.Status);
        Assert.Single((await knowledge.ReadAsync(1, default)).Entries);
        var matched = await catalog.SearchAsync(who, new() { Query = proposed.Query }, default);
        Assert.Equal("10", Assert.Single(matched.Variants).Product.VariantId); Assert.True(matched.Search!.RequiresConfirmation);
        var recreated = new AssistantSearchKnowledge(new EnvironmentStub(f.DirectoryPath), f.Options, f.Clock);
        Assert.Equal("approved", Assert.Single((await recreated.ReadAsync(1, default)).Entries).Status);
        await recreated.ReviewAsync(1, 1, approved.Id, approved.Version, "reject", Guid.NewGuid(), null, default);
        Assert.Empty((await catalog.SearchAsync(who, new() { Query = proposed.Query }, default)).Variants);
        Assert.Equal(409, (await Assert.ThrowsAsync<AssistantException>(() => recreated.ReviewAsync(1, 1, approved.Id, approved.Version, "approve", Guid.NewGuid(), new(), default))).Status);
    }
    [Fact]
    public async Task Correction_approval_rejects_a_different_size_and_pending_entries_expire()
    {
        using var f = new Fixture(); var knowledge = new AssistantSearchKnowledge(new EnvironmentStub(f.DirectoryPath), f.Options, f.Clock);
        var row = await knowledge.ProposeAsync(1, 1, "sữa TH có đường 200ml", 10, 10, Guid.NewGuid(), null, null, default);
        Assert.Equal(422, (await Assert.ThrowsAsync<AssistantException>(() => knowledge.ReviewAsync(1, 1, row.Id, row.Version, "approve", Guid.NewGuid(), new() { Sugar = "sweetened", Capacity = "180ml" }, default))).Status);
        f.Clock.Now += TimeSpan.FromDays(8); Assert.Empty((await knowledge.ReadAsync(1, default)).Entries);
    }
    [Fact]
    public async Task Concurrent_knowledge_writers_preserve_all_proposals_and_broken_storage_fails_closed()
    {
        using var f = new Fixture(); var first = new AssistantSearchKnowledge(new EnvironmentStub(f.DirectoryPath), f.Options, f.Clock);
        var second = new AssistantSearchKnowledge(new EnvironmentStub(f.DirectoryPath), f.Options, f.Clock);
        await Task.WhenAll(Enumerable.Range(1, 12).Select(i => (i % 2 == 0 ? first : second).ProposeAsync(1, 1, "cách gọi sản phẩm " + i, i, i, Guid.NewGuid(), null, null, default)));
        Assert.Equal(12, (await first.ReadAsync(1, default)).Entries.Length);
        await File.WriteAllTextAsync(Path.Combine(f.DirectoryPath, "App_Data", "Assistant", "store-1-search-knowledge.json"), "{broken}");
        Assert.Equal(503, (await Assert.ThrowsAsync<AssistantException>(() => first.ReadAsync(1, default))).Status);
    }
}