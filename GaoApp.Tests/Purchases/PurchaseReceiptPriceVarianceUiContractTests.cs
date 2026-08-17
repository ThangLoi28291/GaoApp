using FluentAssertions;
using GaoApp.Application.DTOs.Inventory;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptPriceVarianceUiContractTests
{
    [Fact]
    public void Confirmation_exposes_explicit_acceptance_and_keeps_reason_optional()
    {
        var edit = Read("GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Edit.cshtml");
        var script = Read("GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js");

        edit.Should().Contain("id=\"priceVarianceAcceptancePanel\"");
        edit.Should().Contain("id=\"acceptPriceVariance\"");
        edit.Should().Contain("Không bắt buộc nhập lý do chỉ vì giá thay đổi.");
        edit.Should().NotContain("required id=\"acceptPriceVariance\"");
        script.Should().Contain("acceptPriceVariance:");
        script.Should().Contain("expectedLastPurchaseUnitPriceBeforeVat:");
        script.Should().Contain("getPriceVariances().length > 0");
        script.Should().Contain("renderPriceVarianceAcceptance()");
        script.Should().Contain("const lastPrice = readLastPurchasePrice(row)");
        script.Should().Contain("expectedLastPurchaseUnitPriceBeforeVat: lastPrice");
        script.Should().Contain("previous === null");
        script.Should().Contain("if (previous === 0)");
        script.Should().NotContain("lastPrice > 0 ? roundMoney(lastPrice) : null");
    }

    [Fact]
    public void Expected_snapshot_accepts_zero_but_current_purchase_price_remains_positive()
    {
        var range = typeof(PurchaseReceiptFinancialLineInputDto)
            .GetProperty(nameof(PurchaseReceiptFinancialLineInputDto
                .ExpectedLastPurchaseUnitPriceBeforeVat))!
            .GetCustomAttribute<RangeAttribute>();

        range.Should().NotBeNull();
        range!.Minimum.Should().Be("0");
    }

    [Fact]
    public void Authoritative_service_recomputes_history_and_rejects_missing_acceptance()
    {
        var service = Read("GaoApp.Application/Services/Inventory/StockDocumentService.cs");

        service.Should().Contain("GetLastPurchaseBaseUnitPricesBeforeVatAsync");
        service.Should().Contain("PurchaseReceiptPriceVariancePolicy.Evaluate");
        service.Should().Contain("authoritativeLastUnitPrice != expectedLastUnitPrice");
        service.Should().Contain("priceVariances.Count > 0 && !acceptPriceVariance");
        service.Should().Contain("LockPurchasePriceHistoryVariantsAsync");
        service.Should().Contain("Đã xác nhận chênh lệch giá nhập so với lần gần nhất");
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
