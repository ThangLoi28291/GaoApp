using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Domain.Storefront;

// Independent WEB00 requirements tests. No project reference, packages, EF,
// application boot, bank transport, connection strings, or persistent store.
var suite = new ContractSuite();
suite.Run();
var resultIndex = Array.IndexOf(args, "--results");
var resultDirectory = resultIndex >= 0 && resultIndex + 1 < args.Length
    ? args[resultIndex + 1] : ".artifacts/storefront-web00";
Directory.CreateDirectory(resultDirectory);
var sources = Directory.GetFiles("GaoApp.Domain/Storefront", "*.cs")
    .OrderBy(x => x, StringComparer.Ordinal)
    .ToDictionary(x => x.Replace('\\', '/'), x => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))));
var report = new
{
    runUtc = DateTimeOffset.UtcNow,
    runtime = Environment.Version.ToString(),
    scope = "Six pure Storefront contracts; no database, HTTP, POS, ACB, or UI startup",
    sourceSha256 = sources,
    total = suite.Results.Count,
    passed = suite.Results.Count(x => x.Passed),
    failed = suite.Results.Count(x => !x.Passed),
    cases = suite.Results
};
File.WriteAllText(Path.Combine(resultDirectory, "results.json"),
    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
foreach (var group in suite.Results.GroupBy(x => x.Group))
    Console.WriteLine($"{group.Key}: {group.Count(x => x.Passed)}/{group.Count()} passed");
foreach (var failure in suite.Results.Where(x => !x.Passed))
    Console.WriteLine($"FAIL {failure.Name}: {failure.Error}");
Console.WriteLine($"TOTAL {report.passed}/{report.total} passed; {report.failed} failed");
Console.WriteLine($"Results: {Path.GetFullPath(Path.Combine(resultDirectory, "results.json"))}");
return report.failed == 0 ? 0 : 1;

internal sealed record CaseResult(string Group, string Name, bool Passed, string? Error);

internal sealed class ContractSuite
{
    private const StorefrontCapability All = StorefrontCapability.Prepare | StorefrontCapability.Dispatch |
        StorefrontCapability.RecordDelivery | StorefrontCapability.ApproveCancellation |
        StorefrontCapability.ReceiveReturn | StorefrontCapability.ConfirmRefund |
        StorefrontCapability.PostSale | StorefrontCapability.ProcessBankEvents;
    private const decimal QLimit = 100_000_000_000_000m;
    private const decimal MLimit = 10_000_000_000_000_000m;
    public List<CaseResult> Results { get; } = [];
    private static StorefrontWorkflowFacts Facts() => new()
    {
        StoreId = 7, OrderId = 901, Revision = 12, Method = StorefrontPaymentMethod.Cod
    };
    private static StorefrontActor Staff(StorefrontCapability capabilities = All) =>
        new(7, StorefrontActorKind.Staff, 51, capabilities);
    private static StorefrontActor Guest() =>
        new(7, StorefrontActorKind.Guest, null, StorefrontCapability.None, 901, true);
    private static StorefrontActor SystemActor() =>
        new(7, StorefrontActorKind.System, null, StorefrontCapability.ProcessBankEvents);
    private static StorefrontTransition Do(StorefrontOrderPhase phase, StorefrontCommand command,
        StorefrontWorkflowFacts? facts = null, StorefrontActor? actor = null, int expectedRevision = 12) =>
        StorefrontWorkflowPolicy.Apply(phase, command, facts ?? Facts(), actor ?? Staff(), expectedRevision);
    private static void Check(bool condition, string message = "Requirement not satisfied")
    {
        if (!condition) throw new Exception(message);
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}; actual {actual}");
    }
    private static void Denied(string code, Action action)
    {
        try { action(); }
        catch (StorefrontRuleException ex)
        {
            Equal(code, ex.Code);
            return;
        }
        throw new Exception($"Expected rejection {code}; operation accepted");
    }
    private void Test(string group, string name, Action action)
    {
        try { action(); Results.Add(new(group, name, true, null)); }
        catch (Exception ex) { Results.Add(new(group, name, false, $"{ex.GetType().Name}: {ex.Message}")); }
    }

    public void Run()
    {
        Workflow(); Payment(); Shipping(); Inventory(); Financial(); Values();
    }

    private void Workflow()
    {
        const string g = "workflow";
        Test(g, "COD accepted directly into preparation queue", () =>
            Equal(StorefrontOrderPhase.AwaitingPreparation, StorefrontWorkflowPolicy.InitialPhase(StorefrontPaymentMethod.Cod)));
        Test(g, "QR accepted awaiting verified payment", () =>
            Equal(StorefrontOrderPhase.AwaitingPayment, StorefrontWorkflowPolicy.InitialPhase(StorefrontPaymentMethod.Qr)));
        Test(g, "Unknown payment method cannot open order", () => Denied("PAYMENT_METHOD_INVALID", () =>
            StorefrontWorkflowPolicy.InitialPhase((StorefrontPaymentMethod)88)));
        foreach (var phase in Enum.GetValues<StorefrontOrderPhase>())
        {
            Test(g, $"Guest cancellation state boundary: {phase}", () =>
            {
                if (phase is StorefrontOrderPhase.AwaitingPayment or StorefrontOrderPhase.AwaitingPreparation)
                {
                    var result = Do(phase, StorefrontCommand.SelfCancel, actor: Guest());
                    Equal(StorefrontOrderPhase.Cancelled, result.NextPhase);
                    Check(result.ReleaseOwnHolds && result.Stock == StorefrontStockEffect.None);
                }
                else Denied("TRANSITION_DENIED", () => Do(phase, StorefrontCommand.SelfCancel, actor: Guest()));
            });
            Test(g, $"Cancellation request state boundary: {phase}", () =>
            {
                if (phase is StorefrontOrderPhase.Preparing or StorefrontOrderPhase.Shipping)
                {
                    var result = Do(phase, StorefrontCommand.RequestCancellation, actor: Guest());
                    Equal(phase, result.NextPhase);
                    Equal(StorefrontCancellationEffect.RequestReview, result.Cancellation);
                    Check(!result.ReleaseOwnHolds && result.Stock == StorefrontStockEffect.None);
                }
                else Denied("TRANSITION_DENIED", () => Do(phase, StorefrontCommand.RequestCancellation, actor: Guest()));
            });
            Test(g, $"Start preparation state boundary: {phase}", () =>
            {
                if (phase == StorefrontOrderPhase.AwaitingPreparation)
                    Equal(StorefrontOrderPhase.Preparing, Do(phase, StorefrontCommand.StartPreparation).NextPhase);
                else Denied("TRANSITION_DENIED", () => Do(phase, StorefrontCommand.StartPreparation));
            });
        }
        Test(g, "Self cancellation of funded order creates refund review", () =>
        {
            var result = Do(StorefrontOrderPhase.AwaitingPreparation, StorefrontCommand.SelfCancel,
                Facts() with { HasReceivedFunds = true, PaymentConfirmed = true }, Guest());
            Check(result.CreateRefundReview && !result.ConfirmRefund && result.ReleaseOwnHolds);
        });
        Test(g, "Self cancellation never returns dispatched stock", () => Denied("TRANSITION_DENIED", () =>
            Do(StorefrontOrderPhase.AwaitingPreparation, StorefrontCommand.SelfCancel,
                Facts() with { PhysicalDispatchRecorded = true }, Guest())));
        foreach (var guest in new[]
        {
            Guest() with { GuestAccessVerified = false },
            Guest() with { AccessibleOrderId = 902 },
            Guest() with { AccessibleOrderId = null },
            Staff()
        })
            Test(g, $"Guest order token required ({guest.Kind}/{guest.AccessibleOrderId}/{guest.GuestAccessVerified})", () =>
                Denied("ORDER_ACCESS_REQUIRED", () => Do(StorefrontOrderPhase.AwaitingPayment,
                    StorefrontCommand.SelfCancel, actor: guest)));
        foreach (var facts in new[]
        {
            Facts() with { StoreId = 0 }, Facts() with { OrderId = 0 }, Facts() with { Revision = 0 }
        })
            Test(g, $"Invalid order scope rejected ({facts.StoreId}/{facts.OrderId}/{facts.Revision})", () =>
                Denied("ORDER_SCOPE_INVALID", () => Do(StorefrontOrderPhase.AwaitingPreparation,
                    StorefrontCommand.StartPreparation, facts)));
        foreach (var actor in new[]
        {
            Staff() with { StoreId = 8 }, Staff() with { Kind = (StorefrontActorKind)99 },
            Staff() with { Capabilities = (StorefrontCapability)256 }
        })
            Test(g, $"Invalid actor scope or flags denied ({actor.StoreId}/{actor.Kind}/{actor.Capabilities})", () =>
                Denied("ACTOR_FORBIDDEN", () => Do(StorefrontOrderPhase.AwaitingPreparation,
                    StorefrontCommand.StartPreparation, actor: actor)));
        Test(g, "Stale revision cannot race staff preparation", () => Denied("REVISION_CONFLICT", () =>
            Do(StorefrontOrderPhase.AwaitingPreparation, StorefrontCommand.StartPreparation, expectedRevision: 11)));
        Test(g, "Future revision is also rejected", () => Denied("REVISION_CONFLICT", () =>
            Do(StorefrontOrderPhase.AwaitingPreparation, StorefrontCommand.StartPreparation, expectedRevision: 13)));
        Test(g, "Unknown order phase denied", () => Denied("STATE_INVALID", () =>
            Do((StorefrontOrderPhase)99, StorefrontCommand.StartPreparation)));
        Test(g, "Unknown command denied", () => Denied("STATE_INVALID", () =>
            Do(StorefrontOrderPhase.AwaitingPreparation, (StorefrontCommand)99)));
        Test(g, "Unknown method in projected facts denied", () => Denied("STATE_INVALID", () =>
            Do(StorefrontOrderPhase.AwaitingPreparation, StorefrontCommand.StartPreparation,
                Facts() with { Method = (StorefrontPaymentMethod)99 })));
        foreach (var command in new[]
        {
            StorefrontCommand.StartPreparation, StorefrontCommand.Dispatch, StorefrontCommand.RecordDelivery,
            StorefrontCommand.ApproveCancellation, StorefrontCommand.RejectCancellation, StorefrontCommand.ReceiveReturn,
            StorefrontCommand.ConfirmRefund, StorefrontCommand.PostSale
        })
        {
            Test(g, $"Staff capability enforced for {command}", () => Denied("CAPABILITY_REQUIRED", () =>
                Do(StorefrontOrderPhase.AwaitingPreparation, command, actor: Staff(StorefrontCapability.None))));
            Test(g, $"System cannot impersonate staff for {command}", () => Denied("CAPABILITY_REQUIRED", () =>
                Do(StorefrontOrderPhase.AwaitingPreparation, command, actor: Staff() with { Kind = StorefrontActorKind.System })));
        }
        Test(g, "Staff identity must have positive user ID", () => Denied("CAPABILITY_REQUIRED", () =>
            Do(StorefrontOrderPhase.AwaitingPreparation, StorefrontCommand.StartPreparation, actor: Staff() with { UserId = 0 })));
        Test(g, "Unpaid QR cannot begin preparation", () => Denied("PAYMENT_NOT_READY", () =>
            Do(StorefrontOrderPhase.AwaitingPreparation, StorefrontCommand.StartPreparation,
                Facts() with { Method = StorefrontPaymentMethod.Qr })));
        Test(g, "Verified QR can begin preparation", () => Equal(StorefrontOrderPhase.Preparing,
            Do(StorefrontOrderPhase.AwaitingPreparation, StorefrontCommand.StartPreparation,
                Facts() with { Method = StorefrontPaymentMethod.Qr, PaymentConfirmed = true }).NextPhase));
        Test(g, "COD payment review also blocks preparation", () => Denied("PAYMENT_NOT_READY", () =>
            Do(StorefrontOrderPhase.AwaitingPreparation, StorefrontCommand.StartPreparation,
                Facts() with { PaymentNeedsReview = true })));
        Test(g, "Pending cancellation blocks preparation", () => Denied("TRANSITION_DENIED", () =>
            Do(StorefrontOrderPhase.AwaitingPreparation, StorefrontCommand.StartPreparation,
                Facts() with { CancellationRequested = true })));

        var dispatch = Facts() with { TrustedHandoverConfirmed = true, AllOrderedGoodsPicked = true };
        Test(g, "Dispatch emits typed movement effect only", () =>
        {
            var result = Do(StorefrontOrderPhase.Preparing, StorefrontCommand.Dispatch, dispatch);
            Equal(StorefrontOrderPhase.Shipping, result.NextPhase);
            Equal(StorefrontStockEffect.Dispatch, result.Stock);
            Check(!result.PostSale && !result.ConfirmRefund);
        });
        foreach (var missing in new[] { "handover", "picked" })
            Test(g, $"Dispatch requires full physical proof: {missing}", () => Denied("HANDOVER_REQUIRED", () =>
                Do(StorefrontOrderPhase.Preparing, StorefrontCommand.Dispatch,
                    dispatch with { TrustedHandoverConfirmed = missing != "handover", AllOrderedGoodsPicked = missing != "picked" })));
        Test(g, "Already dispatched cannot dispatch again", () => Denied("TRANSITION_DENIED", () =>
            Do(StorefrontOrderPhase.Preparing, StorefrontCommand.Dispatch, dispatch with { PhysicalDispatchRecorded = true })));
        Test(g, "Cancellation freezes dispatch", () => Denied("TRANSITION_DENIED", () =>
            Do(StorefrontOrderPhase.Preparing, StorefrontCommand.Dispatch, dispatch with { CancellationRequested = true })));
        Test(g, "Unpaid QR cannot dispatch", () => Denied("PAYMENT_NOT_READY", () =>
            Do(StorefrontOrderPhase.Preparing, StorefrontCommand.Dispatch, dispatch with { Method = StorefrontPaymentMethod.Qr })));
        var delivered = Facts() with { PhysicalDispatchRecorded = true, TrustedDeliveryResult = true };
        Test(g, "Actual delivery recorded independently of cash remittance", () =>
        {
            var result = Do(StorefrontOrderPhase.Shipping, StorefrontCommand.RecordDelivery, delivered);
            Equal(StorefrontOrderPhase.Delivered, result.NextPhase);
            Check(!result.PostSale && result.Stock == StorefrontStockEffect.None);
        });
        Test(g, "Delivery during cancellation request requires review", () =>
        {
            var result = Do(StorefrontOrderPhase.Shipping, StorefrontCommand.RecordDelivery,
                delivered with { CancellationRequested = true });
            Check(result.RequiresReview && result.Cancellation == StorefrontCancellationEffect.ClearRequest);
        });
        foreach (var missing in new[] { "dispatch", "proof", "return-approved" })
            Test(g, $"Delivery guard: {missing}", () => Denied("DELIVERY_PROOF_REQUIRED", () =>
                Do(StorefrontOrderPhase.Shipping, StorefrontCommand.RecordDelivery,
                    delivered with { PhysicalDispatchRecorded = missing != "dispatch", TrustedDeliveryResult = missing != "proof",
                        ReturnAuthorised = missing == "return-approved" })));
        Test(g, "Staff approval before dispatch cancels without returning goods", () =>
        {
            var result = Do(StorefrontOrderPhase.Preparing, StorefrontCommand.ApproveCancellation,
                Facts() with { CancellationRequested = true, HasReceivedFunds = true });
            Equal(StorefrontOrderPhase.Cancelled, result.NextPhase);
            Check(result.ReleaseOwnHolds && result.CreateRefundReview && result.Stock == StorefrontStockEffect.None);
        });
        Test(g, "Staff approval after dispatch waits for actual return", () =>
        {
            var result = Do(StorefrontOrderPhase.Shipping, StorefrontCommand.ApproveCancellation,
                Facts() with { CancellationRequested = true, PhysicalDispatchRecorded = true, HasReceivedFunds = true });
            Equal(StorefrontOrderPhase.Shipping, result.NextPhase);
            Equal(StorefrontCancellationEffect.AwaitPhysicalReturn, result.Cancellation);
            Check(!result.ReleaseOwnHolds && !result.CreateRefundReview && result.Stock == StorefrontStockEffect.None);
        });
        Test(g, "Staff cannot cancel without customer request", () => Denied("TRANSITION_DENIED", () =>
            Do(StorefrontOrderPhase.Preparing, StorefrontCommand.ApproveCancellation)));
        Test(g, "Delivered goods cannot be canceled by shipping approval", () => Denied("TRANSITION_DENIED", () =>
            Do(StorefrontOrderPhase.Shipping, StorefrontCommand.ApproveCancellation,
                Facts() with { CancellationRequested = true, PhysicalDispatchRecorded = true, HasDeliveredGoods = true })));
        var rejection = Facts() with { CancellationRequested = true, CancellationRejectionReasonRecorded = true,
            HasReceivedFunds = true };
        foreach (var phase in Enum.GetValues<StorefrontOrderPhase>())
            Test(g, $"Cancellation rejection state boundary: {phase}", () =>
            {
                if (phase is StorefrontOrderPhase.Preparing or StorefrontOrderPhase.Shipping)
                {
                    var result = Do(phase, StorefrontCommand.RejectCancellation, rejection,
                        Staff(StorefrontCapability.ApproveCancellation));
                    Equal(phase, result.NextPhase);
                    Equal(StorefrontCancellationEffect.ClearRequest, result.Cancellation);
                    Check(!result.ReleaseOwnHolds && !result.CreateRefundReview && !result.ConfirmRefund &&
                        result.Stock == StorefrontStockEffect.None && !result.PostSale);
                }
                else Denied("TRANSITION_DENIED", () => Do(phase, StorefrontCommand.RejectCancellation, rejection));
            });
        foreach (var phase in new[] { StorefrontOrderPhase.Preparing, StorefrontOrderPhase.Shipping })
        {
            foreach (var conflict in new[] { "no-pending-request", "no-reason", "return-authorised", "goods-delivered" })
                Test(g, $"Reject cancellation cannot erase obligation at {phase}: {conflict}", () =>
                    Denied("CANCELLATION_REASON_REQUIRED", () => Do(phase, StorefrontCommand.RejectCancellation,
                        rejection with { CancellationRequested = conflict != "no-pending-request",
                            CancellationRejectionReasonRecorded = conflict != "no-reason",
                            ReturnAuthorised = conflict == "return-authorised", HasDeliveredGoods = conflict == "goods-delivered" })));
            Test(g, $"Guest cannot reject staff cancellation decision at {phase}", () =>
                Denied("CAPABILITY_REQUIRED", () => Do(phase, StorefrontCommand.RejectCancellation, rejection, Guest())));
        }
        Test(g, "Rejected request allows physically proven dispatch after pending flag is cleared", () =>
        {
            var result = Do(StorefrontOrderPhase.Preparing, StorefrontCommand.RejectCancellation, rejection);
            Equal(StorefrontCancellationEffect.ClearRequest, result.Cancellation);
            var dispatchAfterRejection = rejection with { CancellationRequested = false,
                TrustedHandoverConfirmed = true, AllOrderedGoodsPicked = true };
            Equal(StorefrontOrderPhase.Shipping,
                Do(StorefrontOrderPhase.Preparing, StorefrontCommand.Dispatch, dispatchAfterRejection).NextPhase);
        });
        var returned = Facts() with { ReturnAuthorised = true, PhysicalDispatchRecorded = true,
            TrustedReturnReceipt = true, AllDispatchedGoodsReturned = true, HasReceivedFunds = true };
        Test(g, "Full actual return cancels and creates refund review", () =>
        {
            var result = Do(StorefrontOrderPhase.Shipping, StorefrontCommand.ReceiveReturn, returned);
            Equal(StorefrontOrderPhase.Cancelled, result.NextPhase);
            Check(result.Stock == StorefrontStockEffect.ReceiveReturn && result.CreateRefundReview && !result.ConfirmRefund);
        });
        foreach (var missing in new[] { "authorisation", "dispatch", "receipt", "full-return", "delivered" })
            Test(g, $"Receive return requires physical and authority guard: {missing}", () => Denied("PHYSICAL_RETURN_REQUIRED", () =>
                Do(StorefrontOrderPhase.Shipping, StorefrontCommand.ReceiveReturn,
                    returned with { ReturnAuthorised = missing != "authorisation", PhysicalDispatchRecorded = missing != "dispatch",
                        TrustedReturnReceipt = missing != "receipt", AllDispatchedGoodsReturned = missing != "full-return",
                        HasDeliveredGoods = missing == "delivered" })));
        var refund = Facts() with { RefundPending = true, HasReceivedFunds = true,
            ActualRefundEvidenceVerified = true, MoneyReconciled = true };
        Test(g, "Refund confirmation requires executed evidence and stays canceled", () =>
        {
            var result = Do(StorefrontOrderPhase.Cancelled, StorefrontCommand.ConfirmRefund, refund);
            Equal(StorefrontOrderPhase.Cancelled, result.NextPhase);
            Check(result.ConfirmRefund && !result.PostSale);
        });
        foreach (var missing in new[] { "pending", "paid", "evidence", "reconciled" })
            Test(g, $"No fictional refund confirmation: {missing}", () => Denied("REFUND_PROOF_REQUIRED", () =>
                Do(StorefrontOrderPhase.Cancelled, StorefrontCommand.ConfirmRefund,
                    refund with { RefundPending = missing != "pending", HasReceivedFunds = missing != "paid",
                        ActualRefundEvidenceVerified = missing != "evidence", MoneyReconciled = missing != "reconciled" })));
        var expiry = Facts() with { Method = StorefrontPaymentMethod.Qr, PaymentWindowExpired = true, BankConfirmedNoReceipt = true };
        Test(g, "Confirmed unpaid QR expiry releases hold without stock movement", () =>
        {
            var result = Do(StorefrontOrderPhase.AwaitingPayment, StorefrontCommand.ExpireUnpaidQr, expiry, SystemActor());
            Equal(StorefrontOrderPhase.Cancelled, result.NextPhase);
            Check(result.ReleaseOwnHolds && !result.CreateRefundReview && result.Stock == StorefrontStockEffect.None);
        });
        foreach (var missing in new[] { "expired", "bank-unknown", "review" })
            Test(g, $"Uncertain QR expiry blocked: {missing}", () => Denied("BANK_OUTCOME_UNCLEAR", () =>
                Do(StorefrontOrderPhase.AwaitingPayment, StorefrontCommand.ExpireUnpaidQr,
                    expiry with { PaymentWindowExpired = missing != "expired", BankConfirmedNoReceipt = missing != "bank-unknown",
                        PaymentNeedsReview = missing == "review" }, SystemActor())));
        foreach (var condition in new[] { "payment-confirmed", "received-funds", "dispatch", "cod" })
            Test(g, $"Cannot expire order with conflicting facts: {condition}", () => Denied("TRANSITION_DENIED", () =>
                Do(StorefrontOrderPhase.AwaitingPayment, StorefrontCommand.ExpireUnpaidQr,
                    expiry with { PaymentConfirmed = condition == "payment-confirmed", HasReceivedFunds = condition == "received-funds",
                        PhysicalDispatchRecorded = condition == "dispatch", Method = condition == "cod" ? StorefrontPaymentMethod.Cod : StorefrontPaymentMethod.Qr }, SystemActor())));
        Test(g, "Staff cannot invoke automated expiry", () => Denied("SYSTEM_BANK_WORK_REQUIRED", () =>
            Do(StorefrontOrderPhase.AwaitingPayment, StorefrontCommand.ExpireUnpaidQr, expiry, Staff())));
        Test(g, "System expiry still needs bank-processing capability", () => Denied("SYSTEM_BANK_WORK_REQUIRED", () =>
            Do(StorefrontOrderPhase.AwaitingPayment, StorefrontCommand.ExpireUnpaidQr, expiry,
                SystemActor() with { Capabilities = StorefrontCapability.None })));
        var sale = Facts() with { PhysicalDispatchRecorded = true, HasDeliveredGoods = true, GoodsReconciled = true,
            MoneyReconciled = true, PaymentConfirmed = true, SettlingShiftId = 23, SettlingShiftOpen = true,
            SettlingShiftStoreId = 7, SettlingShiftOwnerUserId = 51 };
        Test(g, "Financial posting uses real current settling shift", () =>
        {
            var result = Do(StorefrontOrderPhase.Delivered, StorefrontCommand.PostSale, sale);
            Equal(StorefrontOrderPhase.Completed, result.NextPhase);
            Check(result.PostSale && result.Stock == StorefrontStockEffect.None);
        });
        foreach (var missing in new[] { "dispatch", "goods", "goods-reconciled", "money-reconciled", "payment" })
            Test(g, $"Financial posting requires complete facts: {missing}", () => Denied("SETTLEMENT_NOT_READY", () =>
                Do(StorefrontOrderPhase.Delivered, StorefrontCommand.PostSale,
                    sale with { PhysicalDispatchRecorded = missing != "dispatch", HasDeliveredGoods = missing != "goods",
                        GoodsReconciled = missing != "goods-reconciled", MoneyReconciled = missing != "money-reconciled",
                        PaymentConfirmed = missing != "payment" })));
        foreach (var conflict in new[] { "payment-review", "cancel", "return", "refund", "already-posted" })
            Test(g, $"Financial posting blocked by unresolved obligation: {conflict}", () => Denied("SETTLEMENT_NOT_READY", () =>
                Do(StorefrontOrderPhase.Delivered, StorefrontCommand.PostSale,
                    sale with { PaymentNeedsReview = conflict == "payment-review", CancellationRequested = conflict == "cancel",
                        ReturnAuthorised = conflict == "return", RefundPending = conflict == "refund", SaleAlreadyPosted = conflict == "already-posted" })));
        foreach (var missing in new[] { "shift", "open", "store", "owner" })
            Test(g, $"No fake/cross-store settling shift: {missing}", () => Denied("REAL_SETTLING_SHIFT_REQUIRED", () =>
                Do(StorefrontOrderPhase.Delivered, StorefrontCommand.PostSale,
                    sale with { SettlingShiftId = missing == "shift" ? null : 23, SettlingShiftOpen = missing != "open",
                        SettlingShiftStoreId = missing == "store" ? 8 : 7, SettlingShiftOwnerUserId = missing == "owner" ? 52 : 51 })));
        Test(g, "Completed order cannot post sale twice", () => Denied("SETTLEMENT_NOT_READY", () =>
            Do(StorefrontOrderPhase.Completed, StorefrontCommand.PostSale, sale)));

        // Requirements-derived terminal/state denial matrix. Supply every
        // prerequisite so a wrong phase cannot be hidden behind missing proof.
        foreach (var phase in Enum.GetValues<StorefrontOrderPhase>())
        {
            if (phase != StorefrontOrderPhase.Preparing)
                Test(g, $"Dispatch prohibited from {phase} even with handover proof", () =>
                    Denied("TRANSITION_DENIED", () => Do(phase, StorefrontCommand.Dispatch, dispatch)));
            if (phase != StorefrontOrderPhase.Shipping)
            {
                Test(g, $"Delivery prohibited from {phase} even with delivery proof", () =>
                    Denied("DELIVERY_PROOF_REQUIRED", () => Do(phase, StorefrontCommand.RecordDelivery, delivered)));
                Test(g, $"Stock return prohibited from {phase} even with receipt", () =>
                    Denied("PHYSICAL_RETURN_REQUIRED", () => Do(phase, StorefrontCommand.ReceiveReturn, returned)));
            }
            if (phase is not StorefrontOrderPhase.Preparing and not StorefrontOrderPhase.Shipping)
                Test(g, $"Staff cancellation approval prohibited from {phase}", () =>
                    Denied("TRANSITION_DENIED", () => Do(phase, StorefrontCommand.ApproveCancellation,
                        Facts() with { CancellationRequested = true, PhysicalDispatchRecorded = true })));
            if (phase != StorefrontOrderPhase.Cancelled)
                Test(g, $"Refund confirmation prohibited from {phase} even with evidence", () =>
                    Denied("REFUND_PROOF_REQUIRED", () => Do(phase, StorefrontCommand.ConfirmRefund, refund)));
            if (phase != StorefrontOrderPhase.AwaitingPayment)
                Test(g, $"QR expiry prohibited from {phase} even with bank-negative proof", () =>
                    Denied("TRANSITION_DENIED", () => Do(phase, StorefrontCommand.ExpireUnpaidQr, expiry, SystemActor())));
            if (phase != StorefrontOrderPhase.Delivered)
                Test(g, $"Sale posting prohibited from {phase} even with reconciled facts", () =>
                    Denied("SETTLEMENT_NOT_READY", () => Do(phase, StorefrontCommand.PostSale, sale)));
        }
        foreach (var (command, capability, phase, facts) in new[]
        {
            (StorefrontCommand.StartPreparation, StorefrontCapability.Prepare, StorefrontOrderPhase.AwaitingPreparation, Facts()),
            (StorefrontCommand.Dispatch, StorefrontCapability.Dispatch, StorefrontOrderPhase.Preparing, dispatch),
            (StorefrontCommand.RecordDelivery, StorefrontCapability.RecordDelivery, StorefrontOrderPhase.Shipping, delivered),
            (StorefrontCommand.ApproveCancellation, StorefrontCapability.ApproveCancellation, StorefrontOrderPhase.Preparing,
                Facts() with { CancellationRequested = true }),
            (StorefrontCommand.RejectCancellation, StorefrontCapability.ApproveCancellation, StorefrontOrderPhase.Preparing, rejection),
            (StorefrontCommand.ReceiveReturn, StorefrontCapability.ReceiveReturn, StorefrontOrderPhase.Shipping, returned),
            (StorefrontCommand.ConfirmRefund, StorefrontCapability.ConfirmRefund, StorefrontOrderPhase.Cancelled, refund),
            (StorefrontCommand.PostSale, StorefrontCapability.PostSale, StorefrontOrderPhase.Delivered, sale)
        })
        {
            Test(g, $"Exact minimal capability is sufficient: {command}", () =>
                Do(phase, command, facts, Staff(capability)));
            Test(g, $"Every other capability cannot substitute for {capability}", () =>
                Denied("CAPABILITY_REQUIRED", () => Do(phase, command, facts, Staff(All & ~capability))));
        }
        foreach (var conflict in new[] { "delivered", "return-authorised" })
            Test(g, $"Customer cancellation request blocked after conflicting operation: {conflict}", () =>
                Denied("TRANSITION_DENIED", () => Do(StorefrontOrderPhase.Shipping, StorefrontCommand.RequestCancellation,
                    Facts() with { HasDeliveredGoods = conflict == "delivered", ReturnAuthorised = conflict == "return-authorised" }, Guest())));
    }

    private static StorefrontPaymentExpectation Expected() => new(7, 901, "qr-901-r1", 14, 30,
        120_000, new HashSet<int> { 20, 21 }, new HashSet<int> { 20, 21 });
    private static StorefrontVerifiedReceipt Receipt() => new(7, 901, "qr-901-r1", 14, 30,
        "acb-receipt-0001", 120_000, true);
    private static StorefrontPaymentDecision Pay(StorefrontVerifiedReceipt? receipt = null,
        StorefrontOrderPhase phase = StorefrontOrderPhase.AwaitingPayment,
        StorefrontPaymentExpectation? expected = null, bool alreadyPaid = false, IReadOnlySet<string>? seen = null,
        bool unresolvedReview = false) =>
        StorefrontPaymentPolicy.Evaluate(expected ?? Expected(), receipt ?? Receipt(), phase,
            seen ?? new HashSet<string>(), hasUnresolvedReceiptReview: unresolvedReview, customerAlreadyPaid: alreadyPaid);

    private void Payment()
    {
        const string g = "payment";
        Test(g, "Verified exact QR receipt enables preparation only", () =>
        {
            var result = Pay();
            Equal(StorefrontPaymentDisposition.Apply, result.Disposition);
            Equal(StorefrontOrderPhase.AwaitingPreparation, result.NextPhase);
            Check(result.ConfirmCustomerPayment && !result.CreateRefundReview);
            Equal(120_000m, result.RetainedReceiptAmount);
        });
        Test(g, "Duplicate receipt never allocates second amount", () =>
        {
            var result = Pay(seen: new HashSet<string> { Receipt().EvidenceKey });
            Equal(StorefrontPaymentDisposition.Duplicate, result.Disposition);
            Equal(0m, result.RetainedReceiptAmount);
            Check(!result.ConfirmCustomerPayment && !result.CreateRefundReview);
        });
        Test(g, "Duplicate receipt after cancellation is still duplicate", () =>
        {
            var result = Pay(phase: StorefrontOrderPhase.Cancelled, seen: new HashSet<string> { Receipt().EvidenceKey });
            Equal(StorefrontPaymentDisposition.Duplicate, result.Disposition);
            Check(!result.CreateRefundReview);
        });
        Test(g, "Late money never resurrects canceled order", () =>
        {
            var result = Pay(phase: StorefrontOrderPhase.Cancelled);
            Equal(StorefrontPaymentDisposition.Review, result.Disposition);
            Equal(StorefrontOrderPhase.Cancelled, result.NextPhase);
            Check(result.CreateRefundReview && !result.ConfirmCustomerPayment);
            Equal(120_000m, result.RetainedReceiptAmount);
        });
        foreach (var amount in new[] { 1m, 119_999m, 120_001m, 240_000m })
            Test(g, $"Mismatched amount {amount} retained in full for review", () =>
            {
                var result = Pay(Receipt() with { Amount = amount });
                Equal(StorefrontPaymentDisposition.Review, result.Disposition);
                Equal(StorefrontOrderPhase.AwaitingPayment, result.NextPhase);
                Equal(amount, result.RetainedReceiptAmount);
                Equal("RECEIPT_AMOUNT_MISMATCH", result.ReviewCode);
                Check(!result.ConfirmCustomerPayment);
            });
        foreach (var phase in Enum.GetValues<StorefrontOrderPhase>().Where(x => x is not StorefrontOrderPhase.Cancelled and not StorefrontOrderPhase.AwaitingPayment))
            Test(g, $"Unexpected money at {phase} remains review without moving delivery", () =>
            {
                var result = Pay(phase: phase);
                Equal(phase, result.NextPhase);
                Equal(StorefrontPaymentDisposition.Review, result.Disposition);
                Equal(120_000m, result.RetainedReceiptAmount);
                Check(!result.ConfirmCustomerPayment);
            });
        Test(g, "Already paid intent cannot silently allocate extra receipt", () =>
        {
            var result = Pay(alreadyPaid: true);
            Equal(StorefrontPaymentDisposition.Review, result.Disposition);
            Equal("UNEXPECTED_ADDITIONAL_RECEIPT", result.ReviewCode);
            Equal(120_000m, result.RetainedReceiptAmount);
        });
        foreach (var initialAmount in new[] { 100m, 330m })
            Test(g, $"Mismatch {initialAmount}/230 followed by exact230 cannot clear unresolved money review", () =>
            {
                var expectation = Expected() with { AmountDue = 230 };
                var mismatch = Receipt() with { Amount = initialAmount, EvidenceKey = "review-mismatch-1" };
                var first = Pay(mismatch, expected: expectation);
                Equal(StorefrontPaymentDisposition.Review, first.Disposition);
                Equal("RECEIPT_AMOUNT_MISMATCH", first.ReviewCode);
                Equal(initialAmount, first.RetainedReceiptAmount);
                Check(!first.ConfirmCustomerPayment);

                var exact = Receipt() with { Amount = 230, EvidenceKey = "review-exact-2" };
                var second = Pay(exact, expected: expectation, unresolvedReview: true);
                Equal(StorefrontPaymentDisposition.Review, second.Disposition);
                Equal("UNRESOLVED_RECEIPT_REVIEW", second.ReviewCode);
                Equal(StorefrontOrderPhase.AwaitingPayment, second.NextPhase);
                Equal(230m, second.RetainedReceiptAmount);
                Equal(initialAmount + 230, first.RetainedReceiptAmount + second.RetainedReceiptAmount);
                Check(!second.ConfirmCustomerPayment && !second.CreateRefundReview);
            });
        Test(g, "Authorized durable resolution must clear review latch before exact receipt can apply", () =>
        {
            // Pure contract boundary: false is the trusted projection AFTER an
            // authorized durable review resolution, not a client-settable flag.
            // The held receipt has not been allocated, so is not in seen keys.
            var result = Pay(Receipt() with { Amount = 230, EvidenceKey = "resolved-exact-2" },
                expected: Expected() with { AmountDue = 230 }, unresolvedReview: false);
            Equal(StorefrontPaymentDisposition.Apply, result.Disposition);
            Equal(StorefrontOrderPhase.AwaitingPreparation, result.NextPhase);
            Equal(230m, result.RetainedReceiptAmount);
            Check(result.ConfirmCustomerPayment);
        });
        Test(g, "Duplicate during unresolved review adds no money or confirmation", () =>
        {
            var result = Pay(unresolvedReview: true, seen: new HashSet<string> { Receipt().EvidenceKey });
            Equal(StorefrontPaymentDisposition.Duplicate, result.Disposition);
            Equal(0m, result.RetainedReceiptAmount);
            Check(!result.ConfirmCustomerPayment && !result.CreateRefundReview);
        });
        Test(g, "Late canceled receipt still creates refund review despite prior receipt review", () =>
        {
            var result = Pay(phase: StorefrontOrderPhase.Cancelled, unresolvedReview: true);
            Equal(StorefrontPaymentDisposition.Review, result.Disposition);
            Equal("LATE_RECEIPT_AFTER_CANCEL", result.ReviewCode);
            Equal(StorefrontOrderPhase.Cancelled, result.NextPhase);
            Equal(120_000m, result.RetainedReceiptAmount);
            Check(result.CreateRefundReview && !result.ConfirmCustomerPayment);
        });
        foreach (var phase in Enum.GetValues<StorefrontOrderPhase>().Where(x => x != StorefrontOrderPhase.Cancelled))
            Test(g, $"Unresolved receipt review persists at {phase}", () =>
            {
                var result = Pay(phase: phase, unresolvedReview: true);
                Equal(StorefrontPaymentDisposition.Review, result.Disposition);
                Equal("UNRESOLVED_RECEIPT_REVIEW", result.ReviewCode);
                Equal(phase, result.NextPhase);
                Equal(120_000m, result.RetainedReceiptAmount);
                Check(!result.ConfirmCustomerPayment);
            });
        foreach (var mismatch in new[] { "store", "order", "intent", "intent-case", "bank", "receiver" })
            Test(g, $"Wrong receipt allocation binding rejected: {mismatch}", () => Denied("RECEIPT_SCOPE_MISMATCH", () =>
                Pay(Receipt() with { StoreId = mismatch == "store" ? 8 : 7, OrderId = mismatch == "order" ? 902 : 901,
                    IntentId = mismatch == "intent" ? "qr-902-r1" : mismatch == "intent-case" ? "QR-901-R1" : "qr-901-r1",
                    BankAccountId = mismatch == "bank" ? 15 : 14, ReceivingLegalEntityId = mismatch == "receiver" ? 31 : 30 })));
        foreach (var unverified in new[] { "unverified", "empty-key", "blank-key" })
            Test(g, $"No client fabricated receipt: {unverified}", () => Denied("BANK_EVIDENCE_UNVERIFIED", () =>
                Pay(Receipt() with { Verified = unverified != "unverified",
                    EvidenceKey = unverified == "empty-key" ? "" : unverified == "blank-key" ? " " : "acb-receipt-0001" })));
        foreach (var invalid in new[] { "store", "order", "bank", "receiver", "intent" })
            Test(g, $"Expectation needs configured binding: {invalid}", () => Denied("PAYMENT_BINDING_INVALID", () =>
                Pay(expected: Expected() with { StoreId = invalid == "store" ? 0 : 7, OrderId = invalid == "order" ? 0 : 901,
                    BankAccountId = invalid == "bank" ? 0 : 14, ReceivingLegalEntityId = invalid == "receiver" ? 0 : 30,
                    IntentId = invalid == "intent" ? " " : "qr-901-r1" })));
        foreach (var invalid in new[] { "empty-sources", "empty-map", "missing-source", "invalid-source", "invalid-map" })
            Test(g, $"Collection mandate required for all source owners: {invalid}", () => Denied("COLLECTION_OWNER_UNCONFIGURED", () =>
                Pay(expected: Expected() with {
                    StockLegalEntityIds = invalid == "empty-sources" ? new HashSet<int>() : invalid == "invalid-source" ? new HashSet<int> { 0 } : new HashSet<int> { 20, 21 },
                    AuthorisedCollectionLegalEntityIds = invalid == "empty-map" ? new HashSet<int>() : invalid == "missing-source" ? new HashSet<int> { 20 } : invalid == "invalid-map" ? new HashSet<int> { 20, 21, 0 } : new HashSet<int> { 20, 21 }
                })));
        Test(g, "Different bank owner accepted only with explicit source-owner mandate", () =>
            Equal(StorefrontPaymentDisposition.Apply, Pay().Disposition));
        foreach (var amount in new[] { 0m, -1m, 1.5m, MLimit })
        {
            var expectedCode = amount == 0 ? "PAYMENT_AMOUNT_INVALID" : "MONEY_INVALID";
            Test(g, $"Invalid received money rejected: {amount}", () => Denied(expectedCode, () => Pay(Receipt() with { Amount = amount })));
            Test(g, $"Invalid expected money rejected: {amount}", () => Denied(expectedCode, () => Pay(expected: Expected() with { AmountDue = amount })));
        }
        Test(g, "Unknown order state cannot allocate receipt", () => Denied("STATE_INVALID", () => Pay(phase: (StorefrontOrderPhase)99)));
    }

    private static StorefrontShippingRule Rule() => new(7, 3, 8, true, 20_000, 5_000, 1, 0.5m);
    private static StorefrontShippingQuote Ship(decimal qty = 1, decimal? weight = 1,
        decimal goods = 100_000, StorefrontShippingRule? rule = null) =>
        StorefrontShippingPolicy.Quote(7, goods, [new(qty, weight)], rule ?? Rule());
    private void Shipping()
    {
        const string g = "shipping";
        foreach (var (kg, steps, fee) in new[]
        {
            (0.0001m, 0m, 20_000m), (1m, 0m, 20_000m), (1.0001m, 1m, 25_000m),
            (1.5m, 1m, 25_000m), (1.5001m, 2m, 30_000m), (2m, 2m, 30_000m), (2.0001m, 3m, 35_000m)
        })
            Test(g, $"Weight threshold {kg} kg", () =>
            {
                var quote = Ship(weight: kg);
                Equal(kg, quote.TotalWeightKg);
                Equal(steps, quote.AdditionalSteps);
                Equal(fee, quote.ShippingCharge);
                Equal(100_000 + fee, quote.TotalDue);
            });
        Test(g, "Selling-unit weight times quantity without pack-factor multiplication", () =>
        {
            var quote = Ship(qty: 3, weight: 2.4m);
            Equal(7.2m, quote.TotalWeightKg);
            Equal(13m, quote.AdditionalSteps);
            Equal(85_000m, quote.ShippingCharge);
        });
        Test(g, "Fractional selling-unit quantity allowed to four decimals", () =>
            Equal(0.375m, Ship(qty: 1.5m, weight: 0.25m).TotalWeightKg));
        Test(g, "Multiple product lines aggregate before rounding shipping steps", () =>
        {
            var quote = StorefrontShippingPolicy.Quote(7, 100_000,
                [new(1, 0.6m), new(2, 0.3m)], Rule());
            Equal(1.2m, quote.TotalWeightKg);
            Equal(1m, quote.AdditionalSteps);
            Equal(25_000m, quote.ShippingCharge);
        });
        Test(g, "Quote snapshots selected rule revision", () =>
        {
            var result = Ship(); Equal(3, result.RuleId); Equal(8, result.RuleRevision);
        });
        Test(g, "Flat shipping works without any product weight", () =>
        {
            var quote = Ship(qty: 4, weight: null, rule: Rule() with { ByWeight = false });
            Equal(20_000m, quote.ShippingCharge); Equal(0m, quote.AdditionalSteps);
            Check(quote.TotalWeightKg is null);
        });
        Test(g, "Flat mode ignores kg steps", () =>
            Equal(20_000m, Ship(weight: 100, rule: Rule() with { ByWeight = false, FirstWeightKg = 0, AdditionalStepKg = 0 }).ShippingCharge));
        foreach (var (goods, fee) in new[] { (499_999m, 25_000m), (500_000m, 0m), (500_001m, 0m) })
            Test(g, $"Free shipping goods threshold {goods}", () =>
                Equal(fee, Ship(weight: 1.1m, goods: goods, rule: Rule() with { FreeFromGoodsAmount = 500_000 }).ShippingCharge));
        Test(g, "Free shipping does not bypass required kg data", () => Denied("PRODUCT_WEIGHT_REQUIRED", () =>
            Ship(weight: null, goods: 500_000, rule: Rule() with { FreeFromGoodsAmount = 500_000 })));
        foreach (var weight in new decimal?[] { null, 0, -1, QLimit })
            Test(g, $"Invalid kg cannot quote automatic shipping: {weight?.ToString() ?? "missing"}", () =>
                Denied(weight is null ? "PRODUCT_WEIGHT_REQUIRED" : "PRODUCT_WEIGHT_INVALID", () => Ship(weight: weight)));
        foreach (var qty in new[] { 0m, -1m, 0.00001m, QLimit })
            Test(g, $"Invalid quantity cannot quote shipping: {qty}", () => Denied("QUANTITY_INVALID", () => Ship(qty: qty)));
        foreach (var kgRule in new[]
        {
            Rule() with { FirstWeightKg = 0 }, Rule() with { FirstWeightKg = -1 },
            Rule() with { AdditionalStepKg = 0 }, Rule() with { AdditionalStepKg = -1 },
            Rule() with { FirstWeightKg = QLimit }, Rule() with { AdditionalStepKg = QLimit }
        })
            Test(g, $"Invalid weight bands rejected ({kgRule.FirstWeightKg}/{kgRule.AdditionalStepKg})", () =>
                Denied("WEIGHT_RULE_INVALID", () => Ship(rule: kgRule)));
        foreach (var threshold in new[] { 0m, -1m, 1.5m, MLimit })
            Test(g, $"Invalid free threshold rejected {threshold}", () => Denied(threshold == 0 ? "FREE_THRESHOLD_INVALID" : "MONEY_INVALID", () =>
                Ship(rule: Rule() with { FreeFromGoodsAmount = threshold })));
        foreach (var field in new[] { "base-negative", "extra-fractional", "base-limit" })
            Test(g, $"Invalid fee rejected: {field}", () => Denied("MONEY_INVALID", () => Ship(rule:
                Rule() with { BaseFee = field == "base-negative" ? -1 : field == "base-limit" ? MLimit : 20_000,
                    AdditionalFee = field == "extra-fractional" ? 0.5m : 5_000 })));
        foreach (var rule in new[]
        {
            Rule() with { StoreId = 8 }, Rule() with { StoreId = 0 },
            Rule() with { RuleId = 0 }, Rule() with { Revision = 0 }
        })
            Test(g, $"Wrong/unversioned shipping rule ({rule.StoreId}/{rule.RuleId}/{rule.Revision})", () =>
                Denied("SHIPPING_SCOPE_INVALID", () => Ship(rule: rule)));
        Test(g, "Empty cart rejected", () => Denied("CART_INVALID", () =>
            StorefrontShippingPolicy.Quote(7, 100_000, [], Rule())));
        Test(g, "Zero goods amount rejected", () => Denied("CART_INVALID", () => Ship(goods: 0)));
        Test(g, "Product aggregate weight exceeding supported range rejected", () => Denied("WEIGHT_OVERFLOW", () =>
            Ship(qty: 10, weight: QLimit / 10)));
        Test(g, "Shipping money overflow does not produce a quote", () => Denied("MONEY_INVALID", () =>
            Ship(weight: 2, rule: Rule() with { AdditionalFee = MLimit - 1 })));
        Test(g, "Total payable overflow does not produce a quote", () => Denied("MONEY_INVALID", () =>
            Ship(goods: MLimit - 1)));
        Test(g, "Tiny kg increment causing decimal overflow rejected", () => Denied("WEIGHT_RULE_INVALID", () =>
            Ship(weight: 10, rule: Rule() with { AdditionalStepKg = 0.0000000000000000000000000001m })));
        Test(g, "Zero shipping fees are valid configured fees", () =>
            Equal(100_000m, Ship(rule: Rule() with { BaseFee = 0, AdditionalFee = 0 }).TotalDue));
    }

    private void Inventory()
    {
        const string g = "inventory";
        foreach (var onHand in new[] { 10m, 0m, -10m })
            Test(g, $"Negative-enabled reservation accepts on-hand {onHand}", () =>
                Equal(8m, StorefrontInventoryPolicy.ReserveOwn(onHand, 3, 5, true)));
        Test(g, "Nonnegative stock reserves exactly available boundary", () =>
            Equal(10m, StorefrontInventoryPolicy.ReserveOwn(10, 3, 7, false)));
        foreach (var (onHand, held, requested) in new[] { (10m, 3m, 7.0001m), (0m, 0m, 1m), (-1m, 0m, 1m) })
            Test(g, $"Negative-disabled reservation rejected ({onHand}/{held}/{requested})", () => Denied("INSUFFICIENT_INVENTORY", () =>
                StorefrontInventoryPolicy.ReserveOwn(onHand, held, requested, false)));
        Test(g, "Releasing own reservation preserves all other holds", () =>
            Equal(8m, StorefrontInventoryPolicy.ReleaseOwn(13, 5)));
        Test(g, "Release cannot steal another order hold budget", () => Denied("HOLD_BUDGET_EXCEEDED", () =>
            StorefrontInventoryPolicy.ReleaseOwn(4, 5)));
        Test(g, "Releasing zero hold is harmless", () => Equal(8m, StorefrontInventoryPolicy.ReleaseOwn(8, 0)));
        Test(g, "Reservation tracks demand rather than manufacturing on-hand", () =>
        {
            var held = StorefrontInventoryPolicy.ReserveOwn(-2, 4, 3, true);
            Equal(7m, held);
            Equal(-5m, StorefrontInventoryPolicy.DispatchAfter(-2, held - 3, 3, true));
        });
        Test(g, "Negative dispatch may reduce already-negative on-hand", () =>
            Equal(-8m, StorefrontInventoryPolicy.DispatchAfter(-3, 100, 5, true)));
        Test(g, "Nonnegative dispatch protects other orders' holds", () => Denied("INSUFFICIENT_INVENTORY", () =>
            StorefrontInventoryPolicy.DispatchAfter(10, 8, 3, false)));
        Test(g, "Dispatch exact available boundary preserves other holds", () =>
            Equal(8m, StorefrontInventoryPolicy.DispatchAfter(10, 8, 2, false)));
        Test(g, "Return budget accepts remaining actual dispatched quantity", () =>
            Equal(10m, StorefrontInventoryPolicy.ReceiveReturnBudget(10, 4, 6)));
        Test(g, "Partial actual return accumulates only accepted amount", () =>
            Equal(5.25m, StorefrontInventoryPolicy.ReceiveReturnBudget(10, 4, 1.25m)));
        foreach (var (dispatched, returned, accepted) in new[] { (10m, 4m, 6.0001m), (10m, 11m, 1m), (0m, 0m, 1m) })
            Test(g, $"Cannot restore unissued or already-returned stock ({dispatched}/{returned}/{accepted})", () =>
                Denied("RETURN_BUDGET_EXCEEDED", () => StorefrontInventoryPolicy.ReceiveReturnBudget(dispatched, returned, accepted)));
        foreach (var qty in new[] { 0m, -1m, 0.00001m, QLimit })
        {
            Test(g, $"Reserve rejects invalid quantity {qty}", () => Denied("QUANTITY_INVALID", () =>
                StorefrontInventoryPolicy.ReserveOwn(10, 0, qty, true)));
            Test(g, $"Dispatch rejects invalid quantity {qty}", () => Denied("QUANTITY_INVALID", () =>
                StorefrontInventoryPolicy.DispatchAfter(10, 0, qty, true)));
            Test(g, $"Return rejects invalid quantity {qty}", () => Denied("QUANTITY_INVALID", () =>
                StorefrontInventoryPolicy.ReceiveReturnBudget(10, 0, qty)));
        }
        foreach (var bad in new[] { QLimit, -QLimit, 0.00001m })
            Test(g, $"Signed on-hand storage boundary {bad}", () => Denied("QUANTITY_INVALID", () =>
                StorefrontInventoryPolicy.ReserveOwn(bad, 0, 1, true)));
        Test(g, "Aggregate holds cannot exceed storage limit", () => Denied("QUANTITY_INVALID", () =>
            StorefrontInventoryPolicy.ReserveOwn(0, QLimit - 1, 1, true)));
        Test(g, "Negative dispatch result cannot exceed storage limit", () => Denied("QUANTITY_INVALID", () =>
            StorefrontInventoryPolicy.DispatchAfter(-QLimit + 1, 0, 1, true)));
    }

    private void Financial()
    {
        const string g = "financial";
        Test(g, "Store service shipping fee remains separate from goods and expense", () =>
        {
            var split = StorefrontFinancialPolicy.Split(100_000, 20_000, 25_000, StorefrontShippingFeeTreatment.StoreServiceFee);
            Equal(100_000m, split.GoodsSaleAmount); Equal(20_000m, split.ShippingServiceAmount);
            Equal(0m, split.CarrierPassThroughAmount); Equal(120_000m, split.CustomerTotalDue);
            Equal(25_000m, split.SeparateDeliveryExpense); Equal(0m, split.NewReceiptAtSalePosting);
            Equal(0m, split.CarrierPayableSettlement); Equal(0m, split.CarrierPayableDifference);
            Check(!split.RequiresFeeReview);
        });
        Test(g, "Carrier pass-through fee not classified as product revenue", () =>
        {
            var split = StorefrontFinancialPolicy.Split(100_000, 20_000, 25_000, StorefrontShippingFeeTreatment.CarrierPassThrough);
            Equal(100_000m, split.GoodsSaleAmount); Equal(0m, split.ShippingServiceAmount);
            Equal(20_000m, split.CarrierPassThroughAmount); Equal(120_000m, split.CustomerTotalDue);
            Equal(0m, split.SeparateDeliveryExpense); Equal(0m, split.NewReceiptAtSalePosting);
            Equal(25_000m, split.CarrierPayableSettlement); Equal(-5_000m, split.CarrierPayableDifference);
            Check(split.RequiresFeeReview);
        });
        foreach (var (customerShipping, verifiedCharge, difference, review) in new[]
        {
            (20_000m, 20_000m, 0m, false), (30_000m, 25_000m, 5_000m, true),
            (20_000m, 25_000m, -5_000m, true), (0m, 0m, 0m, false),
            (0m, 25_000m, -25_000m, true), (25_000m, 0m, 25_000m, true)
        })
            Test(g, $"Carrier pass-through surplus/deficit requires explicit review ({customerShipping}/{verifiedCharge})", () =>
            {
                var split = StorefrontFinancialPolicy.Split(100_000, customerShipping, verifiedCharge,
                    StorefrontShippingFeeTreatment.CarrierPassThrough);
                Equal(100_000m, split.GoodsSaleAmount);
                Equal(customerShipping, split.CarrierPassThroughAmount);
                Equal(verifiedCharge, split.CarrierPayableSettlement);
                Equal(difference, split.CarrierPayableDifference);
                Equal(review, split.RequiresFeeReview);
                Equal(0m, split.ShippingServiceAmount);
                Equal(0m, split.SeparateDeliveryExpense);
                Equal(0m, split.NewReceiptAtSalePosting);
                Equal(customerShipping, split.CarrierPayableSettlement + split.CarrierPayableDifference);
                Equal(100_000 + customerShipping, split.CustomerTotalDue);
            });
        foreach (var treatment in new[] { StorefrontShippingFeeTreatment.Unconfigured, (StorefrontShippingFeeTreatment)99 })
            Test(g, $"Unconfigured accounting cannot silently pick a fee model ({treatment})", () =>
                Denied("SHIPPING_ACCOUNTING_UNCONFIGURED", () => StorefrontFinancialPolicy.Split(100_000, 20_000, 25_000, treatment)));
        Test(g, "Free shipping still records actual delivery expense separately", () =>
        {
            var split = StorefrontFinancialPolicy.Split(100_000, 0, 25_000, StorefrontShippingFeeTreatment.StoreServiceFee);
            Equal(100_000m, split.CustomerTotalDue); Equal(25_000m, split.SeparateDeliveryExpense);
        });
        foreach (var (collected, remitted, outstanding, paid, settled) in new[]
        {
            (0m, 0m, 0m, false, false), (50_000m, 0m, 50_000m, false, false),
            (120_000m, 0m, 120_000m, true, false), (120_000m, 100_000m, 20_000m, true, false),
            (120_000m, 120_000m, 0m, true, true)
        })
            Test(g, $"COD customer collection and store receipt separate ({collected}/{remitted})", () =>
            {
                var result = StorefrontFinancialPolicy.Cod(120_000, collected, remitted);
                Equal(outstanding, result.OutstandingWithCollector);
                Equal(paid, result.CustomerPaymentSatisfied); Equal(settled, result.ReadyForFinancialSettlement);
            });
        Test(g, "Verified carrier deduction permits fully reconciled net remittance", () =>
        {
            var result = StorefrontFinancialPolicy.Cod(120_000, 120_000, 100_000, 20_000);
            Equal(0m, result.OutstandingWithCollector);
            Check(result.CustomerPaymentSatisfied && result.ReadyForFinancialSettlement);
        });
        Test(g, "Carrier fee does not substitute for money customer never paid", () =>
        {
            var result = StorefrontFinancialPolicy.Cod(120_000, 100_000, 80_000, 20_000);
            Check(!result.CustomerPaymentSatisfied && !result.ReadyForFinancialSettlement);
            Equal(0m, result.OutstandingWithCollector);
        });
        foreach (var (due, collected, remitted, deduction) in new[]
        {
            (0m, 0m, 0m, 0m), (120_000m, 120_001m, 0m, 0m),
            (120_000m, 100_000m, 100_001m, 0m), (120_000m, 120_000m, 100_000m, 20_001m),
            (120_000m, 0m, 0m, 1m)
        })
            Test(g, $"COD budgets reject unsupported balances ({due}/{collected}/{remitted}/{deduction})", () =>
                Denied("COD_BUDGET_INVALID", () => StorefrontFinancialPolicy.Cod(due, collected, remitted, deduction)));
        Test(g, "Negative carrier deduction rejected", () => Denied("MONEY_INVALID", () =>
            StorefrontFinancialPolicy.Cod(120_000, 120_000, 100_000, -1)));
        Test(g, "Exact remaining refund can be confirmed", () =>
            Equal(120_000m, StorefrontFinancialPolicy.RefundBudget(120_000, 20_000, 100_000, 100_000)));
        Test(g, "Verified partial refund accumulates without overpayment", () =>
            Equal(30_000m, StorefrontFinancialPolicy.RefundBudget(120_000, 20_000, 10_000, 10_000)));
        foreach (var (paid, old, proposed, actual) in new[]
        {
            (120_000m, 20_000m, 100_001m, 100_001m), (120_000m, 120_001m, 1m, 1m),
            (120_000m, 0m, 100_000m, 0m), (120_000m, 0m, 100_000m, 99_999m),
            (120_000m, 0m, 0m, 0m), (0m, 0m, 1m, 1m)
        })
            Test(g, $"Refund requires actual matching evidence within remaining funds ({paid}/{old}/{proposed}/{actual})", () =>
                Denied("REFUND_BUDGET_INVALID", () => StorefrontFinancialPolicy.RefundBudget(paid, old, proposed, actual)));
        Test(g, "Financial total rejects storage overflow", () => Denied("MONEY_INVALID", () =>
            StorefrontFinancialPolicy.Split(MLimit - 1, 1, 0, StorefrontShippingFeeTreatment.StoreServiceFee)));
    }

    private void Values()
    {
        const string g = "storage-values";
        foreach (var amount in new[] { 0m, 1m, MLimit - 1 })
            Test(g, $"Integer nonnegative money accepted {amount}", () => StorefrontValues.Money(amount));
        foreach (var amount in new[] { -1m, 0.1m, MLimit, decimal.MaxValue })
            Test(g, $"Invalid/out-of-range money rejected {amount}", () =>
                Denied("MONEY_INVALID", () => StorefrontValues.Money(amount)));
        foreach (var amount in new[] { 0m, 0.0001m, 1.2345m, QLimit - 0.0001m })
            Test(g, $"Four-decimal quantity accepted {amount}", () => StorefrontValues.Quantity(amount));
        foreach (var amount in new[] { -1m, 0.00001m, QLimit })
            Test(g, $"Invalid/out-of-range quantity rejected {amount}", () =>
                Denied("QUANTITY_INVALID", () => StorefrontValues.Quantity(amount)));
        Test(g, "Positive quantity requirement excludes zero", () =>
            Denied("QUANTITY_INVALID", () => StorefrontValues.Quantity(0, positive: true)));
    }
}
