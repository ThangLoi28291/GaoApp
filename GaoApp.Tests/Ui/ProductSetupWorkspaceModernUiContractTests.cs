namespace GaoApp.Tests.Ui;

public sealed class ProductSetupWorkspaceModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ProductViewRoot = Path.Combine(
        RepositoryRoot, "GaoApp.Web", "Areas", "Admin", "Views", "Product");
    private static readonly string StylePath = Path.Combine(
        RepositoryRoot, "GaoApp.Web", "wwwroot", "Admin", "css", "product-setup-workspace.css");
    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot, "GaoApp.Web", "wwwroot", "Admin", "js", "product-setup-workspace.js");

    [Fact]
    public void Create_should_present_the_first_stage_and_locked_variant_continuation()
    {
        var view = File.ReadAllText(Path.Combine(ProductViewRoot, "Create.cshtml"));

        Assert.Contains("data-product-setup-workspace", view, StringComparison.Ordinal);
        Assert.Contains("data-product-setup-stage=\"create\"", view, StringComparison.Ordinal);
        Assert.Contains("data-variant-step-locked", view, StringComparison.Ordinal);
        Assert.Contains("Lưu và thiết lập biến thể", view, StringComparison.Ordinal);
        Assert.Contains("Giá bán lẻ", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Đơn giá cơ bản", view, StringComparison.Ordinal);
        Assert.Contains("psw-pos-switch", view, StringComparison.Ordinal);
        Assert.Contains("data-set-primary-image", view, StringComparison.Ordinal);
        Assert.Contains("data-upload-state", view, StringComparison.Ordinal);
        Assert.Contains("data-readiness-checklist", view, StringComparison.Ordinal);
        Assert.Contains("data-completion-progress", view, StringComparison.Ordinal);
        Assert.Contains("data-completion-percent", view, StringComparison.Ordinal);
        Assert.Contains("data-completion-panel", view, StringComparison.Ordinal);
        Assert.Contains("~/admin/css/product-setup-workspace.css", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~/admin/js/product-setup-workspace.js", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Edit_should_continue_the_workspace_at_variants_and_offer_explicit_finish()
    {
        var view = File.ReadAllText(Path.Combine(ProductViewRoot, "Edit.cshtml"));

        Assert.Contains("data-product-setup-workspace", view, StringComparison.Ordinal);
        Assert.Contains("data-product-setup-stage=\"edit\"", view, StringComparison.Ordinal);
        Assert.Contains("id=\"variants\"", view, StringComparison.Ordinal);
        Assert.Contains("initialVariantSetup", view, StringComparison.Ordinal);
        Assert.Contains("Hoàn tất và về danh sách", view, StringComparison.Ordinal);
        Assert.Contains("Giá bán lẻ", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Đơn giá cơ bản", view, StringComparison.Ordinal);
        Assert.Contains("psw-pos-switch", view, StringComparison.Ordinal);
        Assert.Contains("data-set-primary-image", view, StringComparison.Ordinal);
        Assert.Contains("data-upload-state", view, StringComparison.Ordinal);
        Assert.Contains("data-readiness-checklist", view, StringComparison.Ordinal);
        Assert.Contains("data-completion-progress", view, StringComparison.Ordinal);
        Assert.Contains("data-completion-backdrop", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Kéo thả để đổi thứ tự ảnh • Chọn thumbnail để xem", view, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Mẹo ảnh:</b>", view, StringComparison.Ordinal);
        Assert.Contains("~/admin/css/product-setup-workspace.css", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~/admin/js/product-setup-workspace.js", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Variant_workspace_should_use_three_stage_list_detail_layout()
    {
        var view = File.ReadAllText(Path.Combine(ProductViewRoot, "_Variants.cshtml"));

        Assert.Contains("Nhóm thuộc tính", view, StringComparison.Ordinal);
        Assert.Contains("Danh sách biến thể", view, StringComparison.Ordinal);
        Assert.Contains("Chi tiết biến thể", view, StringComparison.Ordinal);
        Assert.Contains("data-variant-count", view, StringComparison.Ordinal);
        Assert.Contains("vv-variant-item", view, StringComparison.Ordinal);
        Assert.Contains("Giá bán lẻ", view, StringComparison.Ordinal);
        Assert.Contains("gao:product-variants-state", view, StringComparison.Ordinal);
        Assert.DoesNotContain("<th>Giá gốc</th>", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_assets_should_be_scoped_fluid_and_viewport_safe()
    {
        Assert.True(File.Exists(StylePath), $"Missing PSW1 stylesheet: {StylePath}");
        Assert.True(File.Exists(ScriptPath), $"Missing PSW1 script: {ScriptPath}");

        var style = File.ReadAllText(StylePath);
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains(".product-setup-workspace", style, StringComparison.Ordinal);
        Assert.Contains("max-width: none", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("env(safe-area-inset-bottom)", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overflow-x: hidden", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("position: sticky", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-readiness-item", script, StringComparison.Ordinal);
        Assert.Contains("completionCriteria", script, StringComparison.Ordinal);
        Assert.Contains("data-completion-panel", script, StringComparison.Ordinal);
        Assert.Contains("gao:product-variants-state", script, StringComparison.Ordinal);
        Assert.Contains("Escape", script, StringComparison.Ordinal);
        Assert.Contains("gao:product-upload-state", script, StringComparison.Ordinal);
        Assert.Contains("scrollIntoView", script, StringComparison.Ordinal);
        Assert.Contains(".psw-completion-panel", style, StringComparison.Ordinal);
        Assert.Contains(".psw-completion-backdrop", style, StringComparison.Ordinal);
        Assert.Contains("max-height: min(72dvh", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".vv-variant-card", style, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("GAOAPP_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot) &&
            File.Exists(Path.Combine(configuredRoot, "GaoApp.sln")))
        {
            return configuredRoot;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GaoApp.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate GaoApp repository root.");
    }
}
