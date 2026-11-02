using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptRevisionReviewPolicyTests
{
    [Fact]
    public void Added_and_adjusted_lines_are_marked_without_touching_previously_reviewed_prices()
    {
        var document = Document(1, 2, 3);
        document.Lines[0].UnitPriceBeforeVat = 123m;
        PurchaseReceiptRevisionReviewPolicy.Apply(document, 7,
        [
            Audit(1, PurchaseReceiptAuditEventType.PhysicalLineChanged, 1,
                "{\"UnitPriceBeforeVat\":100}", "{\"UnitPriceBeforeVat\":123}"),
            Audit(2, PurchaseReceiptAuditEventType.ReceiptRejected),
            Audit(3, PurchaseReceiptAuditEventType.PhysicalLineChanged, 2,
                "{\"Quantity\":2,\"LineTotal\":200}", "{\"Quantity\":5,\"LineTotal\":500}"),
            Audit(4, PurchaseReceiptAuditEventType.PhysicalLineAdded, 3,
                "{}", "{\"Quantity\":1,\"UnitId\":4}"),
            Audit(5, PurchaseReceiptAuditEventType.SubmittedForApproval),
            Audit(6, PurchaseReceiptAuditEventType.PhysicalLineChanged, 1,
                "{\"UnitPriceBeforeVat\":123}", "{\"UnitPriceBeforeVat\":150}")
        ]);

        Assert.Null(document.Lines[0].RevisionChangeKind);
        Assert.Equal(123m, document.Lines[0].UnitPriceBeforeVat);
        Assert.Equal("changed", document.Lines[1].RevisionChangeKind);
        Assert.Equal(2m, document.Lines[1].PreviousRevisionQuantity);
        Assert.Equal(["Quantity"], document.Lines[1].RevisionChangedFields);
        Assert.Contains("2 → 5", document.Lines[1].RevisionChangeSummary);
        Assert.Equal("added", document.Lines[2].RevisionChangeKind);
        Assert.Null(document.Lines[2].PreviousRevisionQuantity);
        Assert.Equal(1, document.RevisionReview!.AddedLineCount);
        Assert.Equal(1, document.RevisionReview.ChangedLineCount);
        Assert.Equal(Time(5), document.RevisionReview.ResubmittedAtUtc);
    }

    [Fact]
    public void Only_the_latest_return_round_is_compared()
    {
        var document = Document(1, 2);
        PurchaseReceiptRevisionReviewPolicy.Apply(document, 7,
        [
            Audit(1, PurchaseReceiptAuditEventType.ReceiptRejected),
            Audit(2, PurchaseReceiptAuditEventType.PhysicalLineChanged, 1,
                "{\"Quantity\":1}", "{\"Quantity\":2}"),
            Audit(3, PurchaseReceiptAuditEventType.SubmittedForApproval),
            Audit(4, PurchaseReceiptAuditEventType.RevisionReturnedForEditing),
            Audit(5, PurchaseReceiptAuditEventType.PhysicalLineChanged, 2,
                "{\"Note\":null}", "{\"Note\":\"Đã kiểm lại\"}"),
            Audit(6, PurchaseReceiptAuditEventType.ReceivingFinished)
        ]);

        Assert.Null(document.Lines[0].RevisionChangeKind);
        Assert.Equal("changed", document.Lines[1].RevisionChangeKind);
        Assert.Equal(Time(4), document.RevisionReview!.ReturnedAtUtc);
        Assert.Equal(Time(6), document.RevisionReview.ResubmittedAtUtc);
    }

    [Fact]
    public void Reverted_changes_and_numeric_formatting_do_not_need_review()
    {
        var document = Document(1, 2);
        PurchaseReceiptRevisionReviewPolicy.Apply(document, 7,
        [
            Audit(1, PurchaseReceiptAuditEventType.ReceiptRejected),
            Audit(2, PurchaseReceiptAuditEventType.PhysicalLineChanged, 1,
                "{\"Quantity\":2}", "{\"Quantity\":4}"),
            Audit(3, PurchaseReceiptAuditEventType.PhysicalLineChanged, 1,
                "{\"Quantity\":4}", "{\"Quantity\":2.000}"),
            Audit(4, PurchaseReceiptAuditEventType.PhysicalLineChanged, 2,
                "{\"UnitPriceBeforeVat\":15}", "{\"UnitPriceBeforeVat\":15.00}"),
            Audit(5, PurchaseReceiptAuditEventType.SubmittedForApproval)
        ]);

        Assert.All(document.Lines, line => Assert.Null(line.RevisionChangeKind));
        Assert.Equal(0, document.RevisionReview!.ChangedLineCount);
    }

    [Fact]
    public void Added_then_deleted_lines_cancel_and_existing_deleted_lines_are_counted()
    {
        var document = Document(1);
        PurchaseReceiptRevisionReviewPolicy.Apply(document, 7,
        [
            Audit(1, PurchaseReceiptAuditEventType.ReceiptRejected),
            Audit(2, PurchaseReceiptAuditEventType.PhysicalLineAdded, 2,
                "{}", "{\"Quantity\":3}"),
            Audit(3, PurchaseReceiptAuditEventType.PhysicalLineDeleted, 2,
                "{\"Quantity\":3}", "{}"),
            Audit(4, PurchaseReceiptAuditEventType.PhysicalLineDeleted, 3,
                "{\"Quantity\":4}", "{}")
        ]);

        Assert.Equal(0, document.RevisionReview!.AddedLineCount);
        Assert.Equal(1, document.RevisionReview.RemovedLineCount);
    }

    [Fact]
    public void Soft_delete_then_restore_cancels_removal_but_preserves_quantity_changes()
    {
        var document = Document(1, 2);
        PurchaseReceiptRevisionReviewPolicy.Apply(document, 7,
        [
            Audit(1, PurchaseReceiptAuditEventType.ReceiptRejected),
            Audit(2, PurchaseReceiptAuditEventType.PhysicalLineDeleted, 1,
                "{\"Quantity\":2,\"IsDeleted\":false}", "{}"),
            Audit(3, PurchaseReceiptAuditEventType.PhysicalLineChanged, 1,
                "{\"Quantity\":2,\"IsDeleted\":true}", "{\"Quantity\":2,\"IsDeleted\":false}"),
            Audit(4, PurchaseReceiptAuditEventType.PhysicalLineDeleted, 2,
                "{\"Quantity\":3,\"IsDeleted\":false}", "{}"),
            Audit(5, PurchaseReceiptAuditEventType.PhysicalLineChanged, 2,
                "{\"Quantity\":3,\"IsDeleted\":true}", "{\"Quantity\":5,\"IsDeleted\":false}")
        ]);

        Assert.Null(document.Lines[0].RevisionChangeKind);
        Assert.Equal("changed", document.Lines[1].RevisionChangeKind);
        Assert.Equal(3m, document.Lines[1].PreviousRevisionQuantity);
        Assert.Equal(0, document.RevisionReview!.AddedLineCount);
        Assert.Equal(0, document.RevisionReview.RemovedLineCount);
    }

    [Fact]
    public void Restoring_a_line_that_was_already_deleted_at_return_is_a_new_addition()
    {
        var document = Document(1);
        PurchaseReceiptRevisionReviewPolicy.Apply(document, 7,
        [
            Audit(1, PurchaseReceiptAuditEventType.ReceiptRejected),
            Audit(2, PurchaseReceiptAuditEventType.PhysicalLineChanged, 1,
                "{\"Quantity\":2,\"IsDeleted\":true}", "{\"Quantity\":2,\"IsDeleted\":false}")
        ]);
        Assert.Equal("added", document.Lines[0].RevisionChangeKind);
        Assert.Equal(1, document.RevisionReview!.AddedLineCount);
    }

    [Fact]
    public void Failed_and_foreign_evidence_and_malformed_json_are_ignored()
    {
        var document = Document(1);
        var foreignReturn = Audit(5, PurchaseReceiptAuditEventType.ReceiptRejected);
        foreignReturn.StoreId = 99;
        var failedChange = Audit(2, PurchaseReceiptAuditEventType.PhysicalLineChanged, 1,
            "{\"Quantity\":2}", "{\"Quantity\":3}");
        failedChange.IsSuccess = false;
        var foreignDocument = Audit(3, PurchaseReceiptAuditEventType.PhysicalLineChanged, 1,
            "{\"Quantity\":2}", "{\"Quantity\":4}");
        foreignDocument.StockDocumentId = 999;
        PurchaseReceiptRevisionReviewPolicy.Apply(document, 7,
        [
            Audit(1, PurchaseReceiptAuditEventType.ReceiptRejected), failedChange, foreignDocument,
            Audit(4, PurchaseReceiptAuditEventType.PhysicalLineChanged, 1, "bad-json", "[]"),
            foreignReturn,
            Audit(6, PurchaseReceiptAuditEventType.SubmittedForApproval)
        ]);
        Assert.Null(document.Lines[0].RevisionChangeKind);
        Assert.Equal(Time(1), document.RevisionReview!.ReturnedAtUtc);
        Assert.Equal(0, document.RevisionReview.ChangedLineCount);
    }

    [Theory]
    [InlineData(PurchaseReceiptAuditEventType.SubmittedForApproval)]
    [InlineData(PurchaseReceiptAuditEventType.ReceivingFinished)]
    public void Same_request_line_evidence_is_included_after_the_submission_event(
        PurchaseReceiptAuditEventType submitType)
    {
        var document = Document(1, 2);
        var submitted = Audit(2, submitType);
        submitted.TraceId = "submit-request";
        var sameRequest = Audit(3, PurchaseReceiptAuditEventType.PhysicalLineChanged, 1,
            "{\"ShortageDisposition\":\"None\"}", "{\"ShortageDisposition\":\"WaitForBackorder\"}");
        sameRequest.TraceId = submitted.TraceId;
        PurchaseReceiptRevisionReviewPolicy.Apply(document, 7,
        [
            Audit(1, PurchaseReceiptAuditEventType.ReceiptRejected), submitted, sameRequest,
            Audit(4, PurchaseReceiptAuditEventType.PhysicalLineChanged, 2,
                "{\"Quantity\":1}", "{\"Quantity\":2}")
        ]);
        Assert.Equal("changed", document.Lines[0].RevisionChangeKind);
        Assert.Null(document.Lines[1].RevisionChangeKind);
    }

    [Theory]
    [InlineData(StockDocumentStatus.Confirmed)]
    [InlineData(StockDocumentStatus.Cancelled)]
    [InlineData(StockDocumentStatus.Draft)]
    public void Closed_or_initial_draft_documents_have_no_active_review(StockDocumentStatus status)
    {
        var document = Document(1);
        document.Status = status;
        PurchaseReceiptRevisionReviewPolicy.Apply(document, 7,
            [Audit(1, PurchaseReceiptAuditEventType.ReceiptRejected)]);
        Assert.Null(document.RevisionReview);
    }

    [Fact]
    public void Missing_legacy_audit_or_no_return_does_not_invent_changes()
    {
        var document = Document(1);
        PurchaseReceiptRevisionReviewPolicy.Apply(document, 7, null);
        Assert.Null(document.RevisionReview);
        PurchaseReceiptRevisionReviewPolicy.Apply(document, 7,
            [Audit(1, PurchaseReceiptAuditEventType.PhysicalLineAdded, 1, "{}", "{\"Quantity\":1}")]);
        Assert.Null(document.RevisionReview);
        Assert.Null(document.Lines[0].RevisionChangeKind);
    }

    private static StockDocumentDto Document(params int[] lineIds) => new()
    {
        Id = 4460, Type = StockDocumentType.Receipt, Status = StockDocumentStatus.PendingApproval,
        Lines = lineIds.Select(id => new StockDocumentLineDto { Id = id }).ToList()
    };

    private static DateTime Time(int step) => new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc)
        .AddSeconds(step);

    private static PurchaseReceiptAuditEvent Audit(int step, PurchaseReceiptAuditEventType type,
        int? lineId = null, string before = "{}", string after = "{}") => new()
    {
        Id = step, StoreId = 7, StockDocumentId = 4460, StockDocumentLineId = lineId,
        EventType = type, OccurredAtUtc = Time(step), IsSuccess = true,
        OldValuesJson = before, NewValuesJson = after
    };
}
