using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07AuthorizationTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("read")] [InlineData("sources")] [InlineData("approve-refund")] [InlineData("reject-refund")]
    [InlineData("execute")] [InlineData("approve-cancel")] [InlineData("reject-cancel")] [InlineData("ack-delivery")]
    public async Task Real_services_independently_deny_staff_without_the_required_permission_even_outside_controller(string action)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        // Valid syntax isolates permission checks. These positive IDs remain
        // untrusted client input; no financial source is seeded or claimed.
        var decision = await c.CommandAsync<StorefrontRefundDecisionRequest>();
        decision.RefundId = int.MaxValue; decision.Reason = "Phải kiểm tra quyền trước khi tìm hồ sơ không thuộc yêu cầu này";
        var cancellationDecision = await c.CommandAsync<StorefrontCancellationDecisionRequest>();
        cancellationDecision.CancellationRequestId = int.MaxValue; cancellationDecision.Reason = decision.Reason;
        var execution = await c.CommandAsync<StorefrontRefundExecutionRequest>();
        execution.RefundId = int.MaxValue; execution.SourceId = int.MaxValue; execution.SourceKind = StorefrontRefundPayoutSourceKind.CashOut;
        execution.SourceVersion = execution.ExpectedVersion; execution.PayerLegalEntityId = c.Seed.LegalEntityId;
        execution.ExternalTransactionId = "W07-AUTH-UNTRUSTED-SOURCE"; execution.EvidenceReference = "Đầu vào đúng cú pháp, chưa có quyền đọc hoặc xác nhận nguồn chi";
        var before = await c.SnapshotAsync();
        await using var db = c.Context();
        var cancellation = Web07Support.Cancellation(c, db, permissions: []); var refunds = Web07Support.Refunds(c, db, permissions: []);
        Task Attempt() => action switch
        {
            "read" => cancellation.GetAsync(c.OrderId),
            "sources" => refunds.PayoutSourcesAsync(c.OrderId, 1),
            "approve-refund" => refunds.ApproveAsync(c.OrderId, decision),
            "reject-refund" => refunds.RejectAsync(c.OrderId, decision),
            "execute" => refunds.ConfirmExecutionAsync(c.OrderId, execution),
            "approve-cancel" => cancellation.ApproveAsync(c.OrderId, cancellationDecision),
            "reject-cancel" => cancellation.RejectAsync(c.OrderId, cancellationDecision),
            _ => cancellation.AcknowledgeDeliveryConflictAsync(c.OrderId, cancellationDecision)
        };
        await Assert.ThrowsAsync<ForbiddenAppException>(Attempt); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData(StorefrontPermissions.ConfirmRefund)] [InlineData(PermissionCodes.Report.Treasury.Manage)]
    public async Task Both_execution_permissions_are_revalidated_before_exact_completed_command_replay(string removed)
    {
        var (c, refund) = await RequestedAsync();
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund, "WEB07 kiểm tra nguồn nhận nguyên thủy"));
        var debit = await Web07RecordedDebit.BankAsync(c, 230_000m);
        var request = await c.ExecutionRequestAsync(refund, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt);
        StorefrontOperationAck original; await using (var db = c.Context()) original = await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, request);
        var after = await c.SnapshotAsync(); var reduced = Web07Support.Permissions.Where(x => x != removed).ToArray();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ForbiddenAppException>(() => Web07Support.Refunds(c, db, reduced).ConfirmExecutionAsync(c.OrderId, request));
        Assert.Equal(after, await c.SnapshotAsync());
        await using (var db = c.Context())
        {
            var replay = await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, request);
            Assert.True(replay.Replayed); Assert.Equal(original with { Replayed = true }, replay);
        }
        Assert.Equal(after, await c.SnapshotAsync());
    }

    [Fact]
    public async Task Approve_replay_requires_current_approval_permission_and_the_original_actor_payload()
    {
        var (c, refund) = await RequestedAsync(); var request = await c.RefundDecisionAsync(refund, "WEB07 quyết định có người chịu trách nhiệm");
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, request);
        var after = await c.SnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ForbiddenAppException>(() => Web07Support.Refunds(c, db,
            Web07Support.Permissions.Where(x => x != StorefrontPermissions.ApproveRefund)).ApproveAsync(c.OrderId, request));
        Assert.Equal(after, await c.SnapshotAsync());
        request.Reason += " changed";
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, request));
        Assert.Equal(after, await c.SnapshotAsync());
    }

    [Fact]
    public async Task Foreign_store_staff_and_missing_staff_permission_dependency_cannot_use_a_genuine_capability_as_admin_authority()
    {
        var (c, refund) = await RequestedAsync(); var other = await Web07Case.CreatePhysicalAsync(fixture);
        var request = await c.RefundDecisionAsync(refund, "Khác nhân viên/cửa hàng không được quyết định hồ sơ"); var before = await c.SnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ForbiddenAppException>(() => Web07Support.Refunds(c, db, actorId: other.Seed.UserId).ApproveAsync(c.OrderId, request));
        await using (var db = c.Context()) await Assert.ThrowsAsync<ForbiddenAppException>(() => Web07Support.Cancellation(c, db, guest: true).GetAsync(c.OrderId));
        await using (var db = other.Context()) await Assert.ThrowsAsync<NotFoundAppException>(() => Web07Support.Refunds(other, db).GetAsync(c.OrderId));
        Assert.Equal(before, await c.SnapshotAsync());
    }

    private async Task<(Web07Case Case, int Refund)> RequestedAsync()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        await using var read = c.Context(); return (c, (await read.Set<StorefrontRefund>().SingleAsync()).Id);
    }
}
