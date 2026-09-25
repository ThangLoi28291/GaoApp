namespace GaoApp.Tests.Ui;

public sealed class ProductSetupWorkspaceFlowContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ControllerPath = Path.Combine(
        RepositoryRoot, "GaoApp.Web", "Areas", "Admin", "Controllers", "ProductController.cs");
    private static readonly string EditPath = Path.Combine(
        RepositoryRoot, "GaoApp.Web", "Areas", "Admin", "Views", "Product", "Edit.cshtml");
    private static readonly string VariantsPath = Path.Combine(
        RepositoryRoot, "GaoApp.Web", "Areas", "Admin", "Views", "Product", "_Variants.cshtml");

    [Fact]
    public void Create_success_should_issue_a_one_time_marker_and_redirect_to_edit_variants()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("ProductSetupFreshId", controller, StringComparison.Ordinal);
        Assert.Contains("TempData", controller, StringComparison.Ordinal);
        Assert.Contains("#variants", controller, StringComparison.Ordinal);
        Assert.Contains("ViewBag.IsInitialProductSetup", controller, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TempData[\"ToastSuccess\"] = \"Đã tạo sản phẩm.\";\r\n        return RedirectToAction(nameof(Index));",
            controller,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Edit_should_expose_only_the_server_verified_fresh_state_to_variant_script()
    {
        var view = File.ReadAllText(EditPath);

        Assert.Contains("initialVariantSetup", view, StringComparison.Ordinal);
        Assert.Contains("isInitialProductSetup", view, StringComparison.Ordinal);
        Assert.Contains("window.gaoProductCtx", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Variant_generation_should_guard_default_reuse_and_keep_writes_explicit()
    {
        var partial = File.ReadAllText(VariantsPath);

        Assert.Contains("canReuseFreshDefaultVariant", partial, StringComparison.Ordinal);
        Assert.Contains("isFreshDefaultCandidate", partial, StringComparison.Ordinal);
        Assert.Contains("initialVariantSetup", partial, StringComparison.Ordinal);
        Assert.Contains("class=\"vv-variant-item", partial, StringComparison.Ordinal);
        Assert.Contains("Lưu biến thể trước", partial, StringComparison.Ordinal);
        Assert.Contains("disabled", partial, StringComparison.Ordinal);
        Assert.Contains("btnSaveVariants", partial, StringComparison.Ordinal);
        Assert.Contains("SaveVariants", partial, StringComparison.Ordinal);
        Assert.DoesNotContain("autoSaveGeneratedVariants", partial, StringComparison.Ordinal);
    }

    [Fact]
    public void Product_submit_should_be_blocked_until_image_tokens_are_ready()
    {
        var create = File.ReadAllText(Path.Combine(
            RepositoryRoot, "GaoApp.Web", "Areas", "Admin", "Views", "Product", "Create.cshtml"));
        var edit = File.ReadAllText(EditPath);

        Assert.Contains("hasPendingProductUploads", create, StringComparison.Ordinal);
        Assert.Contains("serverId", create, StringComparison.Ordinal);
        Assert.Contains("setUploadState", create, StringComparison.Ordinal);
        Assert.Contains("hasPendingProductUploads", edit, StringComparison.Ordinal);
        Assert.Contains("serverId", edit, StringComparison.Ordinal);
        Assert.Contains("setUploadState", edit, StringComparison.Ordinal);
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
