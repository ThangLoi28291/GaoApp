namespace GaoApp.Domain.Storefront;

public static class StorefrontPaymentPolicy
{
    public static StorefrontPaymentDecision Evaluate(StorefrontPaymentExpectation expected,
        StorefrontVerifiedReceipt receipt, StorefrontOrderPhase currentPhase,
        IReadOnlySet<string> previouslyAllocatedEvidenceKeys, bool hasUnresolvedReceiptReview,
        bool customerAlreadyPaid = false)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(previouslyAllocatedEvidenceKeys);
        StorefrontValues.Require(Enum.IsDefined(currentPhase), "STATE_INVALID", "Trạng thái đơn không hợp lệ.");
        StorefrontValues.Money(expected.AmountDue);
        StorefrontValues.Money(receipt.Amount);
        StorefrontValues.Require(expected.AmountDue > 0 && receipt.Amount > 0, "PAYMENT_AMOUNT_INVALID", "Khoản thu phải dương.");
        StorefrontValues.Require(expected.StoreId > 0 && expected.OrderId > 0 && expected.BankAccountId > 0 &&
            expected.ReceivingLegalEntityId > 0 && !string.IsNullOrWhiteSpace(expected.IntentId),
            "PAYMENT_BINDING_INVALID", "Thiếu phạm vi và chủ thể nhận tiền đã cấu hình.");
        StorefrontValues.Require(expected.StockLegalEntityIds is { Count: > 0 } &&
            expected.AuthorisedCollectionLegalEntityIds is { Count: > 0 } &&
            expected.AuthorisedCollectionLegalEntityIds.All(x => x > 0) &&
            expected.StockLegalEntityIds.All(x => x > 0 && expected.AuthorisedCollectionLegalEntityIds.Contains(x)),
            "COLLECTION_OWNER_UNCONFIGURED", "Cần ánh xạ quyền thu tiền cho các chủ thể nguồn hàng.");
        StorefrontValues.Require(receipt.Verified && !string.IsNullOrWhiteSpace(receipt.EvidenceKey),
            "BANK_EVIDENCE_UNVERIFIED", "Bằng chứng ngân hàng chưa được xác thực.");
        StorefrontValues.Require(receipt.StoreId == expected.StoreId && receipt.OrderId == expected.OrderId &&
            string.Equals(receipt.IntentId, expected.IntentId, StringComparison.Ordinal) &&
            receipt.BankAccountId == expected.BankAccountId && receipt.ReceivingLegalEntityId == expected.ReceivingLegalEntityId,
            "RECEIPT_SCOPE_MISMATCH", "Khoản thu không thuộc đơn/tài khoản/chủ thể này; giữ ở inbox để đối soát.");
        if (previouslyAllocatedEvidenceKeys.Contains(receipt.EvidenceKey))
            return new(StorefrontPaymentDisposition.Duplicate, currentPhase, 0, false, false);
        if (currentPhase == StorefrontOrderPhase.Cancelled)
            return new(StorefrontPaymentDisposition.Review, currentPhase, receipt.Amount, false, true, "LATE_RECEIPT_AFTER_CANCEL");
        // A later exact receipt cannot clear a review caused by earlier money.
        // Only an authorised, durable review resolution can clear this latch.
        if (hasUnresolvedReceiptReview)
            return new(StorefrontPaymentDisposition.Review, currentPhase, receipt.Amount, false, false, "UNRESOLVED_RECEIPT_REVIEW");
        if (customerAlreadyPaid || currentPhase != StorefrontOrderPhase.AwaitingPayment)
            return new(StorefrontPaymentDisposition.Review, currentPhase, receipt.Amount, false, false, "UNEXPECTED_ADDITIONAL_RECEIPT");
        if (receipt.Amount != expected.AmountDue)
            return new(StorefrontPaymentDisposition.Review, currentPhase, receipt.Amount, false, false, "RECEIPT_AMOUNT_MISMATCH");
        return new(StorefrontPaymentDisposition.Apply, StorefrontOrderPhase.AwaitingPreparation, receipt.Amount, true, false);
    }
}
