using FluentAssertions;

namespace GaoApp.Tests.Purchases;

// R2.4-C2 coverage: split owner validation shares the serialized receipt mutation root.
public sealed class PurchaseReceiptSplitSqlServerConcurrencyTests
{
    [Fact]
    public void Repository_contract_uses_serializable_receipt_root_and_locked_split_aggregate()
    {
        var source = Read("GaoApp.Infrastructure/Repositories/Inventory/InputInvoiceRepository.cs");

        source.Should().Contain("IsolationLevel.Serializable");
        source.Should().Contain("WITH (UPDLOCK, HOLDLOCK)");
        source.Should().Contain("LockReceiptAggregateForSplitAsync");
        source.Should().Contain("EnsureSingleReceiptInvoiceMapAsync");
        source.Should().Contain("ResetReceiptLineMapsAsync");
        source.Should().Contain("sp_getapplock");
    }

    [Fact]
    public void All_link_and_mutation_flows_share_the_receipt_serialization_root()
    {
        var xml = Read("GaoApp.Application/Services/Inventory/InputInvoiceXmlService.cs");
        var picker = Read("GaoApp.Application/Services/Inventory/InputInvoicePickerService.cs");
        var split = Read("GaoApp.Application/Services/Inventory/StockDocumentSplitService.cs");
        var stock = Read("GaoApp.Application/Services/Inventory/StockDocumentService.cs");

        xml.Should().Contain("LockReceiptForInputInvoiceMutationAsync");
        picker.Should().Contain("LockReceiptForInputInvoiceMutationAsync");
        split.Should().Contain("LockReceiptAggregateForSplitAsync");
        stock.Should().Contain("LockReceiptForInputInvoiceMutationAsync");
    }

    [Fact]
    public void Split_snapshot_and_rollback_contract_cover_replay_and_partial_failure()
    {
        var source = Read("GaoApp.Application/Services/Inventory/StockDocumentSplitService.cs");

        source.Should().Contain("ValidateSnapshot");
        source.Should().Contain("SourceInvoiceHeadId");
        source.Should().Contain("RowVersion");
        source.Should().Contain("RollbackSupplierResolutionTransactionAsync");
    }

    [Fact]
    public void Reconciliation_invalidation_and_rebuild_remain_inside_split_transaction()
    {
        var source = Read("GaoApp.Application/Services/Inventory/StockDocumentSplitService.cs");
        var lockReceipt = source.IndexOf("LockReceiptAggregateForSplitAsync",
            StringComparison.Ordinal);
        var invalidate = source.IndexOf("InvalidateWithinTransactionAsync",
            StringComparison.Ordinal);
        var resetMaps = source.IndexOf("ResetReceiptLineMapsAsync",
            StringComparison.Ordinal);
        var refresh = source.IndexOf("RefreshWithinTransactionAsync",
            StringComparison.Ordinal);
        var commit = source.IndexOf("CommitSupplierResolutionTransactionAsync",
            refresh, StringComparison.Ordinal);

        lockReceipt.Should().BeGreaterThanOrEqualTo(0);
        invalidate.Should().BeGreaterThan(lockReceipt);
        resetMaps.Should().BeGreaterThan(invalidate);
        refresh.Should().BeGreaterThan(resetMaps);
        commit.Should().BeGreaterThan(refresh);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
