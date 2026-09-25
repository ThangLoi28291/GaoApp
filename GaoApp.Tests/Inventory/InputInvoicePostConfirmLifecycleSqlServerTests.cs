using FluentAssertions;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Inventory;

public sealed class InputInvoicePostConfirmLifecycleSqlServerTests
{
    [Fact]
    public void Model_enforces_one_active_invoice_per_receipt_with_the_approved_filter()
    {
        var entity = typeof(AppDbContext).Assembly
            .GetType("GaoApp.Infrastructure.Data.AppDbContext")!
            .GetProperty(nameof(AppDbContext.StockDocumentInputInvoiceMaps))!
            .PropertyType.GenericTypeArguments.Single();

        entity.Should().Be(typeof(StockDocumentInputInvoiceMap));
        var configuration = File.ReadAllText(FindRepositoryPath(
            "GaoApp.Infrastructure", "Data", "Configurations", "InputInvoiceConfiguration.cs"));
        var entitySource = File.ReadAllText(FindRepositoryPath(
            "GaoApp.Domain", "Entities", "StockDocumentInputInvoiceMap.cs"));
        entitySource.Should().Contain(
            "UX_StockDocumentInputInvoiceMap_StoreId_StockDocumentId_Active");
        configuration.Should().Contain(
            "HasDatabaseName(StockDocumentInputInvoiceMap.ActiveReceiptIndexName)");
        configuration.Should().Contain("[IsDeleted] = 0");
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found."),
            Path.Combine(segments));
    }
}
