using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Invoices;

public sealed partial class AutoInvoiceServiceTests
{
    [Fact]
    public async Task Paused_admin_keeps_draft_and_does_not_call_provider()
    {
        var repo = CreateRepository(enabled: false);
        var issue = new FakeIssueService();
        var invoice = Invoice(1, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        repo.Candidates.Add(invoice);
        var service = CreateService(repo, issue);

        var result = await service.RunOnceAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, issue.Calls);
        Assert.Equal(InvoiceProviderStatus.LocalDraft, invoice.ProviderStatus);
    }

    [Fact]
    public async Task Missing_unit_is_blocked_without_sending()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService();
        var invoice = Invoice(2, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        invoice.Details.First().UnitName = null;
        repo.Candidates.Add(invoice);
        var service = CreateService(repo, issue);

        await service.RunOnceAsync();

        Assert.Equal(0, issue.Calls);
        Assert.Equal("Invoice.UnitMissing", invoice.LastErrorCode);
        Assert.Contains(repo.Operations, x => x.Status == AutoInvoiceOperationStatus.Blocked);
    }
    [Fact]
    public async Task Insufficient_input_invoice_stock_blocks_each_group_member_with_shortage_details()
    {
        var nowUtc = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
        var repo =
            CreateRepository(enabled: true);

        var issue =
            new FakeIssueService();

        var first =
            Invoice(
                20,
                completedAtUtc:
                    nowUtc.AddHours(-2));

        var second =
            Invoice(
                21,
                completedAtUtc:
                    nowUtc.AddHours(-1));

        first.GrandTotal = 60_000m;
        second.GrandTotal = 60_000m;

        repo.Candidates.AddRange(
            [first, second]);

        var service =
            CreateService(
                repo,
                issue,
                new InsufficientStockRepository(),
                nowUtc: nowUtc);

        await service.RunOnceAsync();

        Assert.Equal(
            0,
            issue.Calls);

        var blocked = repo.Operations
            .Where(x =>
                x.Status ==
                AutoInvoiceOperationStatus.Blocked)
            .OrderBy(x => x.InvoiceHeadId)
            .ToList();

        Assert.Equal(
            2,
            blocked.Count);

        Assert.All(
            blocked,
            operation =>
                Assert.True(
                    operation.InvoiceHeadId.HasValue));

        Assert.Equal(
            new[] { first.Id, second.Id },
            blocked
                .Select(x => x.InvoiceHeadId!.Value)
                .ToArray());

        Assert.All(
            blocked,
            operation =>
            {
                Assert.Equal(
                    "Invoice.InputInvoiceStockInsufficient",
                    operation.ErrorCode);

                Assert.Contains(
                    "cần",
                    operation.ErrorMessage,
                    StringComparison.OrdinalIgnoreCase);

                Assert.Contains(
                    "thiếu",
                    operation.ErrorMessage,
                    StringComparison.OrdinalIgnoreCase);

                var source =
                    Assert.Single(operation.Sources);

                Assert.Equal(
                    AutoInvoiceSourceStatus.Failed,
                    source.Status);

                Assert.False(
                    source.IsActive);

                Assert.Equal(
                    "Invoice.InputInvoiceStockInsufficient",
                    source.ErrorCode);
            });

        Assert.Equal(
            "Invoice.InputInvoiceStockInsufficient",
            first.LastErrorCode);

        Assert.Equal(
            "Invoice.InputInvoiceStockInsufficient",
            second.LastErrorCode);
    }

    [Fact]
    public async Task Manual_issue_and_worker_share_active_source_claim()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService();
        var invoice = Invoice(3, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        invoice.Order!.InvoiceIssuanceRoute =
    InvoiceIssuanceRoute.Manual;

        invoice.BuyerType =
            InvoiceBuyerTypes.Individual;

        invoice.BuyerName =
            "Khách test";

        invoice.BuyerAddress =
            "Đồng Nai";
        repo.Candidates.Add(invoice);
        repo.Operations.Add(new AutoInvoiceOperation
        {
            Id = 77,
            StoreId = 1,
            Status = AutoInvoiceOperationStatus.Processing,
            InvoiceHeadId = invoice.Id,
            Sources =
            [new AutoInvoiceOperationSource
            {
                StoreId = 1,
                InvoiceHeadId = invoice.Id,
                IsActive = true,
                Status = AutoInvoiceSourceStatus.Claimed
            }]
        });
        var service = CreateService(repo, issue);

        var result = await service.IssueManualAsync(invoice.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Equal(0, issue.Calls);
    }
    [Fact]
    public async Task Automatic_route_cannot_be_issued_manually()
    {
        var repo =
            CreateRepository(enabled: true);

        var issue =
            new FakeIssueService();

        var invoice =
            Invoice(
                50,
                completedAtUtc:
                    DateTime.UtcNow.AddHours(-1));

        invoice.Order!.InvoiceIssuanceRoute =
            InvoiceIssuanceRoute.Automatic;

        repo.Candidates.Add(invoice);

        var service =
            CreateService(
                repo,
                issue);

        var result =
            await service.IssueManualAsync(
                invoice.Id);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            "Conflict",
            result.Error!.Code);

        Assert.Equal(
            0,
            issue.Calls);

        Assert.Empty(repo.Operations);
    }
    [Fact]
    public async Task Manual_route_with_valid_buyer_can_be_issued_manually()
    {
        var repo =
            CreateRepository(enabled: true);

        var issue =
            new FakeIssueService();

        var invoice =
            Invoice(
                51,
                completedAtUtc:
                    DateTime.UtcNow.AddHours(-1));

        invoice.Order!.InvoiceIssuanceRoute =
            InvoiceIssuanceRoute.Manual;

        invoice.BuyerType =
            InvoiceBuyerTypes.Individual;

        invoice.BuyerName =
            "Nguyễn Văn A";

        invoice.BuyerAddress =
            "Đồng Nai";

        repo.Candidates.Add(invoice);

        var service =
            CreateService(
                repo,
                issue);

        var result =
            await service.IssueManualAsync(
                invoice.Id);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            1,
            issue.Calls);

        var operation =
            Assert.Single(repo.Operations);

        Assert.True(operation.IsManual);

        Assert.Equal(
            AutoInvoiceOperationStatus.Succeeded,
            operation.Status);
    }

    [Fact]
    public async Task Worker_resumes_durable_claim_left_by_previous_worker()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService();
        var invoice = Invoice(6, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        repo.Candidates.Add(invoice);
        repo.Operations.Add(new AutoInvoiceOperation
        {
            Id = 78,
            StoreId = 1,
            Status = AutoInvoiceOperationStatus.Processing,
            InvoiceHeadId = invoice.Id,
            Sources =
            [new AutoInvoiceOperationSource
            {
                StoreId = 1,
                InvoiceHeadId = invoice.Id,
                IsActive = true,
                Status = AutoInvoiceSourceStatus.Claimed
            }]
        });

        var service = CreateService(repo, issue);

        await service.RunOnceAsync();

        Assert.Equal(1, issue.Calls);
        Assert.Equal(AutoInvoiceOperationStatus.Succeeded, repo.Operations.Single().Status);
        Assert.False(repo.Operations.Single().Sources.Single().IsActive);
    }

    [Fact]
    public async Task Unknown_result_is_resolved_by_uuid_lookup_before_reissue()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService
        {
            Result = Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("TIMEOUT", "Timeout khi gọi Viettel."))
        };
        var invoice = Invoice(4, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        repo.Candidates.Add(invoice);
        var service = CreateService(repo, issue);

        await service.RunOnceAsync();
        Assert.Contains(repo.Operations, x => x.Status == AutoInvoiceOperationStatus.Unknown);

        var lookup = await service.SyncUnknownAsync(invoice.Id);

        Assert.True(lookup.IsSuccess);
        Assert.True(lookup.Value!.IsFound);
        Assert.Contains(repo.Operations, x => x.Status == AutoInvoiceOperationStatus.Succeeded);
    }

    [Fact]
    public async Task Timeout_exception_is_recorded_as_unknown_instead_of_retried()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService { Exception = new TimeoutException("provider timeout") };
        repo.Candidates.Add(Invoice(5, completedAtUtc: DateTime.UtcNow.AddHours(-1)));
        var service = CreateService(repo, issue);

        await service.RunOnceAsync();

        Assert.Contains(repo.Operations, x => x.Status == AutoInvoiceOperationStatus.Unknown);
        Assert.Equal("TIMEOUT", repo.Operations.Single().ErrorCode);
    }

    [Fact]
    public async Task Failed_invoice_is_left_for_errors_and_does_not_stop_next_invoice()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService();
        var failed = Invoice(30, completedAtUtc: DateTime.UtcNow.AddHours(-2));
        failed.LastErrorCode = "Invoice.UnitMissing";
        failed.LastErrorMessage = "Thiếu đơn vị tính.";
        var next = Invoice(31, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        repo.Candidates.AddRange([failed, next]);
        var service = CreateService(repo, issue);

        await service.RunOnceAsync();

        Assert.Equal(1, issue.Calls);
        Assert.Contains(repo.Operations, x => x.InvoiceHeadId == next.Id && x.Status == AutoInvoiceOperationStatus.Succeeded);
        Assert.DoesNotContain(repo.Operations, x => x.InvoiceHeadId == failed.Id);
    }

    [Fact]
    public async Task Resolved_stock_error_reenters_queue_without_retrying_unresolved_errors()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService();
        var invoice = Invoice(36, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        invoice.LastErrorCode = "Invoice.InputInvoiceStockInsufficient";
        invoice.LastErrorMessage = "Còn thiếu tồn trước đó.";
        repo.Candidates.Add(invoice);

        var service = CreateService(repo, issue, new SufficientStockRepository());

        await service.RunOnceAsync();

        Assert.Equal(1, issue.Calls);
        Assert.Contains(repo.Operations, x => x.InvoiceHeadId == invoice.Id && x.Status == AutoInvoiceOperationStatus.Succeeded);
    }

    [Fact]
    public async Task Invoice_before_minimum_age_stays_waiting_without_provider_call()
    {
        var repo = CreateRepository(enabled: true);
        repo.Settings.MinimumAgeMinutes = 5;
        var issue = new FakeIssueService();
        repo.Candidates.Add(Invoice(32, completedAtUtc: DateTime.UtcNow.AddMinutes(-1)));
        var service = CreateService(repo, issue);

        await service.RunOnceAsync();

        Assert.Equal(0, issue.Calls);
        Assert.Empty(repo.Operations);
    }

    [Fact]
    public async Task Forced_admin_cycle_bypasses_previous_send_interval()
    {
        var repo = CreateRepository(enabled: true);
        repo.WorkerStates.Add(new AutoInvoiceWorkerState
        {
            Id = 1,
            StoreId = 1,
            WorkerName = "auto-invoice-worker",
            NextRunAtUtc = DateTime.UtcNow.AddHours(1)
        });
        var issue = new FakeIssueService();
        repo.Candidates.Add(Invoice(33, completedAtUtc: DateTime.UtcNow.AddHours(-1)));
        var service = CreateService(repo, issue);

        await service.RunOnceAsync(force: true);

        Assert.Equal(1, issue.Calls);
    }
    [Fact]
    public async Task Minimum_age_uses_last_issuance_relevant_change_not_original_sale_time()
    {
        var repo = CreateRepository(enabled: true);
        repo.Settings.MinimumAgeMinutes = 30;

        var issue = new FakeIssueService();

        var nowUtc = DateTime.UtcNow;

        var invoice = Invoice(
            501,
            completedAtUtc: nowUtc.AddHours(-2));

        invoice.LastIssuanceRelevantChangeAtUtc =
            nowUtc.AddMinutes(-10);

        repo.Candidates.Add(invoice);

        var service = CreateService(repo, issue);

        await service.RunOnceAsync();

        Assert.Equal(0, issue.Calls);

        Assert.DoesNotContain(
            repo.Operations,
            x => x.Sources.Any(
                s => s.InvoiceHeadId == invoice.Id));
    }
    [Fact]
    public async Task Failed_group_marks_all_sources_and_is_not_submitted_again()
    {
        var nowUtc = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService
        {
            Result = Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("InvoiceProvider.CredentialKeyUnavailable", "Key không đọc được."))
        };
        var first = Invoice(34, completedAtUtc: nowUtc.AddHours(-2));
        var second = Invoice(35, completedAtUtc: nowUtc.AddHours(-1));
        first.GrandTotal = 60_000m;
        second.GrandTotal = 60_000m;
        repo.Candidates.AddRange([first, second]);
        var service = CreateService(repo, issue, nowUtc: nowUtc);

        await service.RunOnceAsync();
        await service.RunOnceAsync(force: true);

        Assert.Equal(1, issue.Calls);
        Assert.Equal("InvoiceProvider.CredentialKeyUnavailable", first.LastErrorCode);
        Assert.Equal("InvoiceProvider.CredentialKeyUnavailable", second.LastErrorCode);
    }
    [Fact]
    public async Task Automatic_route_with_retained_buyer_info_still_uses_group_lane()
    {
        var nowUtc = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
        var repo = CreateRepository(enabled: true);

        repo.Settings.SeparateAmountThreshold =
            100_000m;

        repo.Settings.GroupTargetAmount =
            100_000m;

        var issue =
            new FakeIssueService();

        var first =
            Invoice(
                40,
                completedAtUtc:
                    nowUtc.AddHours(-2));

        var second =
            Invoice(
                41,
                completedAtUtc:
                    nowUtc.AddHours(-1));

        first.GrandTotal = 60_000m;
        first.SubTotal = 60_000m;

        first.Details.Single().Amount = 60_000m;
        first.Details.Single().TotalAmount = 60_000m;
        first.Details.Single().UnitPrice = 60_000m;

        second.GrandTotal = 60_000m;
        second.SubTotal = 60_000m;

        second.Details.Single().Amount = 60_000m;
        second.Details.Single().TotalAmount = 60_000m;
        second.Details.Single().UnitPrice = 60_000m;

        // Buyer info được giữ lại từ Manual,
        // nhưng route hiện tại đã là Automatic.
        first.BuyerType =
            InvoiceBuyerTypes.Business;

        first.BuyerLegalName =
            "Công ty test A";

        first.BuyerTaxCode =
            "0100000001";

        first.BuyerAddress =
            "Địa chỉ test";

        second.BuyerType =
            InvoiceBuyerTypes.Business;

        second.BuyerLegalName =
            "Công ty test B";

        second.BuyerTaxCode =
            "0100000002";

        second.BuyerAddress =
            "Địa chỉ test";

        first.Order!.InvoiceIssuanceRoute =
            InvoiceIssuanceRoute.Automatic;

        second.Order!.InvoiceIssuanceRoute =
            InvoiceIssuanceRoute.Automatic;

        repo.Candidates.AddRange(
            [first, second]);

        var service =
            CreateService(
                repo,
                issue,
                nowUtc: nowUtc);

        await service.RunOnceAsync();

        Assert.Equal(
            1,
            issue.Calls);

        var operation =
            Assert.Single(
                repo.Operations,
                x =>
                    x.Kind ==
                    AutoInvoiceOperationKind.Group);

        Assert.Equal(
            2,
            operation.Sources.Count);

        Assert.Contains(
            operation.Sources,
            x => x.InvoiceHeadId == first.Id);

        Assert.Contains(
            operation.Sources,
            x => x.InvoiceHeadId == second.Id);
    }
    [Fact]
    public async Task Group_revalidates_current_totals_before_claim()
    {
        var repo =
            CreateRepository(enabled: true);

        repo.Settings.SeparateAmountThreshold =
            100_000m;

        repo.Settings.GroupTargetAmount =
            100_000m;

        repo.Settings.ClosingTimeLocal =
            new TimeSpan(23, 59, 59);

        repo.Settings.IssueOldDayRemainder =
            false;

        var issue =
            new FakeIssueService();

        var now =
            DateTime.UtcNow;

        var first =
            Invoice(
                60,
                now.AddHours(-2));

        var second =
            Invoice(
                61,
                now.AddHours(-1));

        SetInvoiceTotal(
            first,
            60_000m);

        SetInvoiceTotal(
            second,
            60_000m);

        repo.Candidates.AddRange(
            [first, second]);

        // Queue scan thấy 120k => ready.
        //
        // Nhưng trước claim, return làm second
        // chỉ còn 20k => current group = 80k.
        repo.BeforeGroupClaimRead = () =>
        {
            SetInvoiceTotal(
                second,
                20_000m);

            second.LastIssuanceRelevantChangeAtUtc =
                now.AddMinutes(-10);
        };

        var service =
            CreateService(
                repo,
                issue);

        await service.RunOnceAsync();

        Assert.Equal(
            0,
            issue.Calls);

        Assert.DoesNotContain(
            repo.Operations,
            x =>
                x.Kind ==
                AutoInvoiceOperationKind.Group);
    }
    [Fact]
    public async Task Group_revalidation_drops_member_changed_to_manual()
    {
        var repo =
            CreateRepository(enabled: true);

        repo.Settings.SeparateAmountThreshold =
            100_000m;

        repo.Settings.GroupTargetAmount =
            100_000m;

        repo.Settings.ClosingTimeLocal =
            TimeSpan.Zero;

        var issue =
            new FakeIssueService();

        var now =
            DateTime.UtcNow;

        var first =
            Invoice(
                62,
                now.AddHours(-2));

        var second =
            Invoice(
                63,
                now.AddHours(-1));

        SetInvoiceTotal(
            first,
            60_000m);

        SetInvoiceTotal(
            second,
            60_000m);

        repo.Candidates.AddRange(
            [first, second]);

        repo.BeforeGroupClaimRead = () =>
        {
            second.Order!.InvoiceIssuanceRoute =
                InvoiceIssuanceRoute.Manual;
        };

        var service =
            CreateService(
                repo,
                issue);

        await service.RunOnceAsync();

        Assert.Equal(
            1,
            issue.Calls);

        var groupOperation =
            Assert.Single(
                repo.Operations,
                x =>
                    x.Kind ==
                    AutoInvoiceOperationKind.Group);

        Assert.Single(
            groupOperation.Sources);

        Assert.Equal(
     first.Id,
     groupOperation.Sources.Single()
         .InvoiceHeadId);

        Assert.DoesNotContain(
            groupOperation.Sources,
            x =>
                x.InvoiceHeadId ==
                    second.Id);
    }
    [Fact]
    public async Task Invalid_group_member_does_not_hold_valid_cutoff_remainder()
    {
        var repo =
            CreateRepository(enabled: true);

        repo.Settings.SeparateAmountThreshold =
            100_000m;

        repo.Settings.GroupTargetAmount =
            100_000m;

        repo.Settings.ClosingTimeLocal =
            TimeSpan.Zero;

        var issue =
            new FakeIssueService();

        var now =
            new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

        var valid =
            Invoice(
                64,
                now.AddHours(-2));

        var invalid =
            Invoice(
                65,
                now.AddHours(-1));

        SetInvoiceTotal(
            valid,
            60_000m);

        SetInvoiceTotal(
            invalid,
            60_000m);

        repo.Candidates.AddRange(
            [valid, invalid]);

        repo.BeforeGroupClaimRead = () =>
        {
            invalid.Details.Single()
                .UnitName = null;
        };

        var service =
            CreateService(
                repo,
                issue,
                nowUtc: now);

        await service.RunOnceAsync();

        Assert.Equal(
            1,
            issue.Calls);

        var groupOperation =
            Assert.Single(
                repo.Operations,
                x =>
                    x.Kind ==
                    AutoInvoiceOperationKind.Group);

        Assert.Single(
            groupOperation.Sources);

        Assert.Equal(
            valid.Id,
            groupOperation.Sources.Single()
                .InvoiceHeadId);

        var blocked =
            Assert.Single(
                repo.Operations,
                x =>
                    x.Status ==
                        AutoInvoiceOperationStatus.Blocked &&
                    x.InvoiceHeadId ==
                        invalid.Id);

        Assert.Equal(
            "Invoice.UnitMissing",
            blocked.ErrorCode);

        Assert.Equal(
            "Invoice.UnitMissing",
            invalid.LastErrorCode);
    }
    [Fact]
    public async Task Recheck_incident_clears_resolved_stock_error_without_issuing()
    {
        var repo =
            CreateRepository(enabled: true);

        var issue =
            new FakeIssueService();

        var invoice =
            Invoice(
                70,
                DateTime.UtcNow.AddHours(-1));

        invoice.LastErrorCode =
            "Invoice.InputInvoiceStockInsufficient";

        invoice.LastErrorMessage =
            "Thiếu tồn.";

        repo.Candidates.Add(invoice);

        var service =
            CreateService(
                repo,
                issue,
                new SufficientStockRepository());

        var result =
            await service.RecheckIncidentAsync(
                invoice.Id);

        Assert.True(result.IsSuccess);

        Assert.Null(
            invoice.LastErrorCode);

        Assert.Null(
            invoice.LastErrorMessage);

        Assert.Equal(
            0,
            issue.Calls);

        Assert.Empty(
            repo.Operations);
    }
    [Fact]
    public async Task Recheck_incident_keeps_stock_error_when_shortage_remains()
    {
        var repo =
            CreateRepository(enabled: true);

        var issue =
            new FakeIssueService();

        var invoice =
            Invoice(
                71,
                DateTime.UtcNow.AddHours(-1));

        invoice.LastErrorCode =
            "Invoice.InputInvoiceStockInsufficient";

        repo.Candidates.Add(invoice);

        var service =
            CreateService(
                repo,
                issue,
                new InsufficientStockRepository());

        var result =
            await service.RecheckIncidentAsync(
                invoice.Id);

        Assert.False(
            result.IsSuccess);

        Assert.Equal(
            "Invoice.InputInvoiceStockInsufficient",
            result.Error!.Code);

        Assert.Equal(
            "Invoice.InputInvoiceStockInsufficient",
            invoice.LastErrorCode);

        Assert.Equal(
            0,
            issue.Calls);
    }
    [Fact]
    public async Task Recheck_incident_repairs_missing_unit_after_product_unit_is_fixed()
    {
        var repo =
            CreateRepository(enabled: true);

        repo.RepairUnitName =
            "cái";

        var issue =
            new FakeIssueService();

        var invoice =
            Invoice(
                72,
                DateTime.UtcNow.AddHours(-1));

        invoice.Details.Single()
            .UnitName = null;

        invoice.LastErrorCode =
            "Invoice.UnitMissing";

        invoice.LastErrorMessage =
            "Thiếu đơn vị tính.";

        repo.Candidates.Add(invoice);

        var service =
            CreateService(
                repo,
                issue);

        var result =
            await service.RecheckIncidentAsync(
                invoice.Id);

        Assert.True(
            result.IsSuccess);

        Assert.Equal(
            "cái",
            invoice.Details.Single()
                .UnitName);

        Assert.Null(
            invoice.LastErrorCode);

        Assert.Equal(
            0,
            issue.Calls);
    }
    [Fact]
    public async Task Recheck_incident_rejects_unknown_without_issue_or_uuid_lookup()
    {
        var repo =
            CreateRepository(enabled: true);

        var issue =
            new FakeIssueService();

        var lookup =
            new CountingLookupService();

        var invoice =
            Invoice(
                73,
                DateTime.UtcNow.AddHours(-1));

        invoice.ProviderStatus =
            InvoiceProviderStatus.Issuing;

        invoice.LastErrorCode =
            "TIMEOUT";

        invoice.LastErrorMessage =
            "Timeout Viettel.";

        repo.Candidates.Add(invoice);

        repo.Operations.Add(
            new AutoInvoiceOperation
            {
                Id = 900,
                StoreId = 1,
                Kind =
                    AutoInvoiceOperationKind.Single,
                Status =
                    AutoInvoiceOperationStatus.Unknown,
                InvoiceHeadId =
                    invoice.Id,

                Sources =
                [
                    new AutoInvoiceOperationSource
                {
                    Id = 901,
                    StoreId = 1,
                    InvoiceHeadId =
                        invoice.Id,

                    Status =
                        AutoInvoiceSourceStatus.Unknown,

                    IsActive = true
                }
                ]
            });

        var service =
            CreateService(
                repo,
                issue,
                sync: lookup);

        var result =
            await service.RecheckIncidentAsync(
                invoice.Id);

        Assert.False(
            result.IsSuccess);

        Assert.Equal(
            "Conflict",
            result.Error!.Code);

        Assert.Equal(
            0,
            issue.Calls);

        Assert.Equal(
            0,
            lookup.Calls);

        Assert.Equal(
            "TIMEOUT",
            invoice.LastErrorCode);
    }
    [Fact]
    public async Task Recheck_incident_does_not_clear_nonlocal_provider_failure()
    {
        var repo =
            CreateRepository(enabled: true);

        var issue =
            new FakeIssueService();

        var invoice =
            Invoice(
                74,
                DateTime.UtcNow.AddHours(-1));

        invoice.LastErrorCode =
            "VIETTEL_REJECTED";

        invoice.LastErrorMessage =
            "Provider từ chối hóa đơn.";

        repo.Candidates.Add(invoice);

        var service =
            CreateService(
                repo,
                issue);

        var result =
            await service.RecheckIncidentAsync(
                invoice.Id);

        Assert.False(
            result.IsSuccess);

        Assert.Equal(
            "Conflict",
            result.Error!.Code);

        Assert.Equal(
            "VIETTEL_REJECTED",
            invoice.LastErrorCode);

        Assert.Equal(
            0,
            issue.Calls);
    }
    [Theory]
    [InlineData(InvoiceCorrectionType.Replacement)]
    [InlineData(InvoiceCorrectionType.AdjustmentAmount)]
    [InlineData(InvoiceCorrectionType.AdjustmentInfo)]
    public async Task Manual_correction_of_automatic_original_keeps_durable_claim(InvoiceCorrectionType type)
    {
        var repo = CreateRepository(enabled: true);
        var invoice = Invoice(201, DateTime.UtcNow.AddHours(-1));
        invoice.OriginalInvoiceHeadId = 200;
        invoice.OriginalInvoiceHead = Invoice(200, DateTime.UtcNow.AddHours(-2));
        invoice.OriginalInvoiceHead.ProviderStatus = InvoiceProviderStatus.Issued;
        invoice.CorrectionType = type;
        // An information-only adjustment legitimately has no quantity, amount or unit.
        if (type == InvoiceCorrectionType.AdjustmentInfo)
        {
            SetInvoiceTotal(invoice, 0);
            invoice.Details.Single().Quantity = 0;
            invoice.Details.Single().UnitName = null;
        }
        if (type == InvoiceCorrectionType.AdjustmentAmount)
            SetInvoiceTotal(invoice, -50_000m);
        repo.Candidates.Add(invoice);
        var issue = new FakeIssueService();

        var result = await CreateService(repo, issue).IssueManualAsync(invoice.Id);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1, issue.Calls);
        var operation = Assert.Single(repo.Operations);
        Assert.True(operation.IsManual);
        Assert.Equal(invoice.Id, Assert.Single(operation.Sources).InvoiceHeadId);
        Assert.Equal(AutoInvoiceOperationStatus.Succeeded, operation.Status);
    }

    [Theory]
    [InlineData("missing-original")]
    [InlineData("foreign-store")]
    [InlineData("unissued-original")]
    [InlineData("invalid-type")]
    [InlineData("unknown")]
    [InlineData("issued")]
    public async Task Unsafe_correction_is_rejected_before_claim_and_provider(string condition)
    {
        var repo = CreateRepository(enabled: true);
        var invoice = Invoice(201, DateTime.UtcNow.AddHours(-1));
        invoice.OriginalInvoiceHeadId = 200;
        invoice.OriginalInvoiceHead = Invoice(200, DateTime.UtcNow.AddHours(-2));
        invoice.OriginalInvoiceHead.ProviderStatus = InvoiceProviderStatus.Issued;
        invoice.CorrectionType = InvoiceCorrectionType.Replacement;
        switch (condition)
        {
            case "missing-original": invoice.OriginalInvoiceHead = null; break;
            case "foreign-store": invoice.OriginalInvoiceHead.StoreId = 2; break;
            case "unissued-original": invoice.OriginalInvoiceHead.ProviderStatus = InvoiceProviderStatus.LocalDraft; break;
            case "invalid-type": invoice.CorrectionType = (InvoiceCorrectionType)999; break;
            case "unknown": invoice.ProviderStatus = InvoiceProviderStatus.IssuedWaitingNumber; break;
            case "issued": invoice.ProviderStatus = InvoiceProviderStatus.Issued; break;
        }
        repo.Candidates.Add(invoice);
        var issue = new FakeIssueService();

        var result = await CreateService(repo, issue).IssueManualAsync(invoice.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, issue.Calls);
        Assert.Empty(repo.Operations);
    }

    [Theory]
    [InlineData("age")]
    [InlineData("group-lane")]
    [InlineData("zero-total")]
    [InlineData("correction")]
    public async Task Single_claim_revalidates_changes_after_queue_scan_under_store_lock(string change)
    {
        var repo = CreateRepository(enabled: true);
        repo.Settings.MinimumAgeMinutes = 30;
        var invoice = Invoice(301, DateTime.UtcNow.AddHours(-2));
        repo.Candidates.Add(invoice);
        var locked = false;
        repo.BeforeSingleClaimRead = () =>
        {
            Assert.True(locked);
            switch (change)
            {
                case "age": invoice.LastIssuanceRelevantChangeAtUtc = DateTime.UtcNow; break;
                case "group-lane": SetInvoiceTotal(invoice, 50_000m); break;
                case "zero-total": SetInvoiceTotal(invoice, 0); break;
                case "correction": invoice.CorrectionType = InvoiceCorrectionType.Replacement; break;
            }
        };
        var issue = new FakeIssueService();
        var stock = new SufficientStockRepository(() => locked = true);

        await CreateService(repo, issue, stock).RunOnceAsync();

        Assert.True(locked);
        Assert.Empty(repo.Operations);
        Assert.Equal(0, issue.Calls);
    }

    [Theory]
    [InlineData(InvoiceProviderStatus.Issuing, "found")]
    [InlineData(InvoiceProviderStatus.Issuing, "not-found")]
    [InlineData(InvoiceProviderStatus.Issuing, "unavailable")]
    [InlineData(InvoiceProviderStatus.Issuing, "credential")]
    [InlineData(InvoiceProviderStatus.IssuedWaitingNumber, "found")]
    [InlineData(InvoiceProviderStatus.IssuedWaitingNumber, "not-found")]
    [InlineData(InvoiceProviderStatus.IssuedWaitingNumber, "unavailable")]
    [InlineData(InvoiceProviderStatus.IssuedWaitingNumber, "credential")]
    public async Task Worker_tenant_fix_recovers_uuid_without_web_tenant_and_never_blindly_reissues(
        InvoiceProviderStatus status, string outcome)
    {
        var repo = CreateRepository(true);
        var invoice = Invoice(70, DateTime.UtcNow.AddHours(-1));
        invoice.ProviderStatus = status;
        invoice.TransactionUuid = "original-durable-uuid";
        repo.Candidates.Add(invoice);
        var operation = RecoveryOperation(700, 1, invoice.Id);
        repo.Operations.Add(operation);
        var issue = new FakeIssueService();
        var sync = new WorkerRecoveryLookupService(id =>
        {
            Assert.Equal(invoice.Id, id);
            Assert.Equal(0, issue.Calls); // Lookup must precede any reissue.
            Assert.Equal("original-durable-uuid", invoice.TransactionUuid);
            return outcome switch
            {
                "unavailable" => Result<ViettelInvoiceLookupResultDto>.Failure(Error.Validation("LOOKUP_TIMEOUT", "Lookup unavailable")),
                "credential" => Result<ViettelInvoiceLookupResultDto>.Failure(Error.Validation("InvoiceProvider.NotConfigured", "Missing credential")),
                _ => Result<ViettelInvoiceLookupResultDto>.Success(new ViettelInvoiceLookupResultDto
                { InvoiceHeadId = id, IsFound = outcome == "found", TransactionUuid = invoice.TransactionUuid })
            };
        });

        await CreateService(repo, issue, sync: sync, tenant: new StoreTenant(null)).RunOnceAsync();

        Assert.Equal(new[] { invoice.Id }, sync.InvoiceIds);
        Assert.Null(Assert.Single(repo.WorkerStates).LastErrorCode);
        Assert.Equal("original-durable-uuid", invoice.TransactionUuid);
        Assert.Equal(outcome == "not-found" ? 1 : 0, issue.Calls);
        Assert.Equal(outcome == "unavailable" ? AutoInvoiceOperationStatus.Unknown :
            outcome == "credential" ? AutoInvoiceOperationStatus.Failed : AutoInvoiceOperationStatus.Succeeded, operation.Status);
        Assert.Equal(outcome == "unavailable", Assert.Single(operation.Sources).IsActive);
        Assert.Equal(outcome == "not-found" ? 1 : 0, operation.AttemptCount);
        Assert.Single(repo.Operations);
    }

    [Fact]
    public async Task Worker_tenant_fix_recovers_each_store_and_unblocks_ready_orders_on_next_cycle()
    {
        var repo = CreateRepository(true);
        repo.ActiveStoreIds.Add(2);
        var secondSettings = CreateRepository(true).Settings;
        secondSettings.StoreId = 2;
        repo.SettingsByStore.Add(2, secondSettings);
        var readyIds = new List<int>();
        foreach (var storeId in repo.ActiveStoreIds)
        {
            var old = Invoice(storeId * 100, DateTime.UtcNow.AddHours(-2));
            old.StoreId = storeId;
            old.ProviderStatus = InvoiceProviderStatus.Issuing;
            old.TransactionUuid = $"store-{storeId}-uuid";
            repo.Candidates.Add(old);
            repo.Operations.Add(RecoveryOperation(storeId * 1000, storeId, old.Id));
            var ready = Invoice(old.Id + 1, DateTime.UtcNow.AddHours(-1));
            ready.StoreId = ready.Order!.StoreId = ready.InvoiceProviderSetting!.StoreId = storeId;
            repo.Candidates.Add(ready);
            readyIds.Add(ready.Id);
        }
        var issue = new FakeIssueService();
        var sync = new WorkerRecoveryLookupService(id =>
        {
            var invoice = repo.Candidates.Single(x => x.Id == id);
            // A found/issued invoice no longer belongs to the real candidate query.
            repo.Candidates.Remove(invoice);
            return Result<ViettelInvoiceLookupResultDto>.Success(new ViettelInvoiceLookupResultDto
            { InvoiceHeadId = id, IsFound = true, TransactionUuid = invoice.TransactionUuid! });
        });
        var tenant = new StoreTenant(null);
        var service = CreateService(repo, issue, sync: sync, tenant: tenant);

        await service.RunOnceAsync(force: true);

        Assert.Equal(new[] { 100, 200 }, sync.InvoiceIds);
        Assert.Equal(0, issue.Calls);
        Assert.All(repo.Operations, x => Assert.Equal(AutoInvoiceOperationStatus.Succeeded, x.Status));
        Assert.Null(tenant.StoreId);

        await service.RunOnceAsync(force: true);

        Assert.Equal(readyIds, issue.InvoiceIds);
        Assert.Equal(4, repo.Operations.Count);
        Assert.All(repo.Operations, x =>
        {
            Assert.Equal(AutoInvoiceOperationStatus.Succeeded, x.Status);
            Assert.All(x.Sources, s => { Assert.Equal(x.StoreId, s.StoreId); Assert.False(s.IsActive); });
        });
        Assert.Equal(2, repo.WorkerStates.Count);
        Assert.All(repo.WorkerStates, x => Assert.Null(x.LastErrorCode));
    }

    [Fact]
    public async Task Worker_tenant_fix_keeps_public_uuid_lookup_requiring_web_tenant()
    {
        var sync = new WorkerRecoveryLookupService(_ => throw new InvalidOperationException("Must not call provider"));
        var service = CreateService(CreateRepository(true), new FakeIssueService(), sync: sync, tenant: new StoreTenant(null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncUnknownAsync(70));

        Assert.Empty(sync.InvoiceIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Worker_tenant_fix_public_lookup_rejects_missing_or_foreign_store_invoice(bool foreign)
    {
        var repo = CreateRepository(true);
        if (foreign)
        {
            var invoice = Invoice(70, DateTime.UtcNow.AddHours(-1));
            invoice.StoreId = 2;
            repo.Candidates.Add(invoice);
        }
        var sync = new WorkerRecoveryLookupService(_ => Result<ViettelInvoiceLookupResultDto>.Success(
            new ViettelInvoiceLookupResultDto { IsFound = true }));

        var result = await CreateService(repo, new FakeIssueService(), sync: sync).SyncUnknownAsync(70);

        Assert.False(result.IsSuccess);
        Assert.Empty(sync.InvoiceIds);
    }

    [Fact]
    public async Task Worker_tenant_fix_rejects_recovery_target_belonging_to_another_store()
    {
        var repo = CreateRepository(true);
        var invoice = Invoice(70, DateTime.UtcNow.AddHours(-1));
        invoice.StoreId = 2;
        invoice.ProviderStatus = InvoiceProviderStatus.Issuing;
        repo.Candidates.Add(invoice);
        var operation = RecoveryOperation(700, 1, invoice.Id);
        repo.Operations.Add(operation);
        var sync = new WorkerRecoveryLookupService(_ => throw new InvalidOperationException("Must not call provider"));
        var issue = new FakeIssueService();

        await CreateService(repo, issue, sync: sync, tenant: new StoreTenant(null)).RunOnceAsync();

        Assert.Empty(sync.InvoiceIds);
        Assert.Equal(0, issue.Calls);
        Assert.Equal(AutoInvoiceOperationStatus.Failed, operation.Status);
        Assert.False(Assert.Single(operation.Sources).IsActive);
    }

    private static AutoInvoiceOperation RecoveryOperation(int id, int storeId, int invoiceId)
        => new()
        {
            Id = id, StoreId = storeId, InvoiceHeadId = invoiceId,
            Status = AutoInvoiceOperationStatus.Unknown,
            Sources = [new AutoInvoiceOperationSource
            { StoreId = storeId, InvoiceHeadId = invoiceId, IsActive = true, Status = AutoInvoiceSourceStatus.Claimed }]
        };

    private sealed class WorkerRecoveryLookupService(Func<int, Result<ViettelInvoiceLookupResultDto>> lookup)
        : IViettelInvoiceSyncService
    {
        public List<int> InvoiceIds { get; } = [];
        public Task<Result<ViettelInvoiceLookupResultDto>> SyncByTransactionUuidAsync(int invoiceHeadId, CancellationToken ct = default)
        {
            InvoiceIds.Add(invoiceHeadId);
            return Task.FromResult(lookup(invoiceHeadId));
        }
    }

    private static AutoInvoiceService CreateService(
    FakeAutoInvoiceRepository repo,
    FakeIssueService issue,
    IInvoiceInputStockRepository? stock = null,
    IViettelInvoiceSyncService? sync = null,
    ITenantContext? tenant = null,
    DateTime? nowUtc = null)
    => new(
        repo,
        stock ?? new SufficientStockRepository(),
        issue,
        sync ?? new FoundLookupService(),
        new NoopUnitOfWork(),
        tenant ?? new StoreTenant(1),
        new TestCurrentUser(),
        new FixedTimeProvider(nowUtc ?? DateTime.UtcNow));

    private static FakeAutoInvoiceRepository CreateRepository(bool enabled)
        => new()
        {
            Settings = new AutoInvoiceSettings
            {
                Id = 1,
                StoreId = 1,
                IsEnabled = enabled,
                MinimumAgeMinutes = 0,
                SeparateAmountThreshold = 100_000m,
                GroupTargetAmount = 100_000m,
                SendIntervalSeconds = 1,
                ClosingTimeLocal = new(23, 0, 0),
                IssueOldDayRemainder = true,
                TimeZoneId = TimeZoneInfo.Utc.Id
            }
        };
    private static void SetInvoiceTotal(
    InvoiceHead invoice,
    decimal total)
    {
        invoice.SubTotal = total;
        invoice.VatAmount = 0m;
        invoice.GrandTotal = total;

        var detail =
            invoice.Details.Single();

        detail.Quantity = 1m;
        detail.UnitPrice = total;
        detail.Amount = total;
        detail.VatAmount = 0m;
        detail.TotalAmount = total;
    }
    private static InvoiceHead Invoice(int id, DateTime completedAtUtc)
        => new()
        {
            Id = id,
            StoreId = 1,
            OrderId = id,
            Order = new Order
            {
                Id = id,
                StoreId = 1,
                CompletedAtUtc = completedAtUtc,
                GrandTotal = 200_000m,
                Payments = [new OrderPayment
                {
                    StoreId = 1, OrderId = id, Method = PaymentMethod.Cash, Amount = 200_000m
                }],

                InvoiceIssuanceRoute =
        InvoiceIssuanceRoute.Automatic
            },
            InvoiceDate = completedAtUtc,
            BuyerType = InvoiceBuyerTypes.NoInvoice,
            GrandTotal = 200_000m,
            SubTotal = 200_000m,
            ProviderCode = "VIETTEL",
            InvoiceProviderSettingId = 10,
            InvoiceProviderSetting = new InvoiceProviderSetting
            {
                Id = 10,
                StoreId = 1,
                ProviderCode = "VIETTEL",
                IsActive = true,
                BaseUrl = "https://example.test",
                Username = "test",
                Password = "test",
                SupplierTaxCode = "0100000000",
                InvoiceType = "1",
                TemplateCode = "1/001",
                InvoiceSeries = "C26TAA"
            },
            Details =
            [new InvoiceDetail
            {
                Id = id * 10,
                StoreId = 1,
                InvoiceHeadId = id,
                ItemName = "Hàng test",
                UnitName = "cái",
                Quantity = 1,
                UnitPrice = 200_000m,
                Amount = 200_000m,
                TotalAmount = 200_000m,
                SourceType = InvoiceDetailSourceType.FromOrderLine
            }]
        };

    private sealed class FakeAutoInvoiceRepository : IAutoInvoiceRepository
    {
        public AutoInvoiceSettings Settings { get; set; } = new();
        public List<int> ActiveStoreIds { get; } = [1];
        public Dictionary<int, AutoInvoiceSettings> SettingsByStore { get; } = [];
        public List<InvoiceHead> Candidates { get; } = [];
        public List<AutoInvoiceOperation> Operations { get; } = [];
        public List<AutoInvoiceWorkerState> WorkerStates { get; } = [];
        public Action? BeforeGroupClaimRead
        {
            get;
            set;
        }
        public Action? BeforeSingleClaimRead { get; set; }
        public string? RepairUnitName
        {
            get;
            set;
        }
        private int _nextOperationId = 100;

        public Task<AutoInvoiceSettings?> GetSettingsAsync(int storeId, CancellationToken ct = default) => Task.FromResult<AutoInvoiceSettings?>(SettingsByStore.GetValueOrDefault(storeId) ?? Settings);
        public Task AddSettingsAsync(AutoInvoiceSettings settings, CancellationToken ct = default) { Settings = settings; return Task.CompletedTask; }
        public Task<List<int>> GetActiveStoreIdsAsync(CancellationToken ct = default) => Task.FromResult(ActiveStoreIds.ToList());
        public Task<AutoInvoiceWorkerState?> GetWorkerStateAsync(int storeId, string workerName, CancellationToken ct = default) => Task.FromResult(WorkerStates.FirstOrDefault(x => x.StoreId == storeId && x.WorkerName == workerName));
        public Task AddWorkerStateAsync(AutoInvoiceWorkerState state, CancellationToken ct = default) { state.Id = WorkerStates.Count + 1; WorkerStates.Add(state); return Task.CompletedTask; }
        public Task<List<InvoiceHead>> GetCandidateInvoicesAsync(int storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) => Task.FromResult(Candidates.Where(x => x.StoreId == storeId).ToList());
        public Task<int> ClearRecoverableCredentialErrorsAsync(int storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) => Task.FromResult(0);
        public Task MarkInvoiceErrorsAsync(int storeId, IReadOnlyCollection<int> invoiceHeadIds, string errorCode, string? errorMessage, CancellationToken ct = default)
        {
            foreach (var invoice in Candidates.Where(x => invoiceHeadIds.Contains(x.Id)))
            {
                invoice.LastErrorCode = errorCode;
                invoice.LastErrorMessage = errorMessage;
            }
            return Task.CompletedTask;
        }
        public Task ClearInvoiceErrorsAsync(
    int storeId,
    IReadOnlyCollection<int> invoiceHeadIds,
    CancellationToken ct = default)
        {
            foreach (var invoice in Candidates.Where(x =>
                         x.StoreId == storeId &&
                         invoiceHeadIds.Contains(x.Id)))
            {
                invoice.LastErrorCode = null;
                invoice.LastErrorMessage = null;
            }

            return Task.CompletedTask;
        }

        public Task<int> RepairMissingUnitNamesAsync(
            int storeId,
            int invoiceHeadId,
            CancellationToken ct = default)
        {
            var invoice =
                Candidates.FirstOrDefault(x =>
                    x.StoreId == storeId &&
                    x.Id == invoiceHeadId);

            if (invoice == null ||
                string.IsNullOrWhiteSpace(
                    RepairUnitName))
            {
                return Task.FromResult(0);
            }

            var changed = 0;

            foreach (var detail in invoice.Details.Where(x =>
                         !x.IsDeleted &&
                         string.IsNullOrWhiteSpace(
                             x.UnitName)))
            {
                detail.UnitName =
                    RepairUnitName;

                changed++;
            }

            return Task.FromResult(changed);
        }
        public Task<List<AutoInvoiceOperation>> GetActiveOperationsAsync(int storeId, CancellationToken ct = default) => Task.FromResult(Operations.Where(x => x.StoreId == storeId && x.Status is AutoInvoiceOperationStatus.Pending or AutoInvoiceOperationStatus.Processing or AutoInvoiceOperationStatus.Unknown).ToList());
        public Task<InvoiceHead?> GetInvoiceHeadForAutomaticIssueAsync(int storeId, int invoiceHeadId, CancellationToken ct = default) => Task.FromResult(Candidates.FirstOrDefault(x => x.StoreId == storeId && x.Id == invoiceHeadId));
        public Task<InvoiceHead?>
    GetInvoiceHeadForClaimAsync(
        int storeId,
        int invoiceHeadId,
        CancellationToken ct = default)
        {
            BeforeSingleClaimRead?.Invoke();
            return Task.FromResult(
                Candidates.FirstOrDefault(
                    x =>
                        x.StoreId == storeId &&
                        x.Id == invoiceHeadId));
        }

        public Task<InvoiceHead?> GetInvoiceHeadForManualClaimAsync(
            int storeId, int invoiceHeadId, CancellationToken ct = default)
            => GetInvoiceHeadForClaimAsync(storeId, invoiceHeadId, ct);

        public Task<List<InvoiceHead>>
           GetInvoiceHeadsForClaimAsync(
               int storeId,
               IReadOnlyCollection<int> invoiceHeadIds,
               CancellationToken ct = default)
        {
            // Test hook: mô phỏng Order/Invoice bị thay đổi
            // sau queue scan nhưng trước durable group claim.
            BeforeGroupClaimRead?.Invoke();

            var ids =
                invoiceHeadIds.ToHashSet();

            return Task.FromResult(
                Candidates
                    .Where(x =>
                        x.StoreId == storeId &&
                        ids.Contains(x.Id))
                    .ToList());
        }

        public Task<bool> HasSuccessfulSourceAsync(
            int storeId,
            int invoiceHeadId,
            CancellationToken ct = default)
        {
            return Task.FromResult(
                Operations.Any(operation =>
                    operation.StoreId == storeId &&
                    operation.Sources.Any(source =>
                        source.InvoiceHeadId ==
                            invoiceHeadId &&
                        !source.IsDeleted &&
                        source.Status ==
                            AutoInvoiceSourceStatus.Succeeded)));
        }
        public Task<List<AutoInvoiceOperation>> GetOperationsAsync(int storeId, int take, CancellationToken ct = default) => Task.FromResult(Operations.Where(x => x.StoreId == storeId).OrderByDescending(x => x.Id).Take(take).ToList());
        public Task<AutoInvoiceOperation?> GetOperationAsync(int storeId, int operationId, CancellationToken ct = default) => Task.FromResult(Operations.FirstOrDefault(x => x.StoreId == storeId && x.Id == operationId));
        public Task<bool> HasActiveSourceAsync(int storeId, int invoiceHeadId, CancellationToken ct = default) => Task.FromResult(Operations.Any(x => x.StoreId == storeId && x.Sources.Any(s => s.InvoiceHeadId == invoiceHeadId && s.IsActive)));
        public Task AddOperationAsync(AutoInvoiceOperation operation, CancellationToken ct = default) { operation.Id = ++_nextOperationId; Operations.Add(operation); return Task.CompletedTask; }
        public Task AddInvoiceHeadAsync(InvoiceHead invoice, CancellationToken ct = default) { invoice.Id = 1000; return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void DiscardFailedAutoInvoiceChanges(bool includeWorkerState = false) { }
    }

    private sealed class SufficientStockRepository(Action? onLock = null) : IInvoiceInputStockRepository
    {
        public Task LockStoreForIssueAsync(int storeId, CancellationToken ct = default) { onLock?.Invoke(); return Task.CompletedTask; }
        public Task<InvoiceInputStockAvailabilityDto> GetAvailabilityAsync(int invoiceHeadId, CancellationToken ct = default) => Task.FromResult(new InvoiceInputStockAvailabilityDto { InvoiceHeadId = invoiceHeadId });
    }

    private sealed class InsufficientStockRepository : IInvoiceInputStockRepository
    {
        public Task LockStoreForIssueAsync(int storeId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<InvoiceInputStockAvailabilityDto> GetAvailabilityAsync(int invoiceHeadId, CancellationToken ct = default)
            => Task.FromResult(new InvoiceInputStockAvailabilityDto
            {
                InvoiceHeadId = invoiceHeadId,
                Lines =
                [new InvoiceInputStockAvailabilityLineDto
                {
                    WarehouseId = 7,
                    ProductVariantId = 8,
                    ItemName = "Hàng thiếu tồn",
                    RequiredBaseQuantity = 3,
                    EligibleInboundBaseQuantity = 1,
                    CommittedOutboundBaseQuantity = 0
                }]
            });
    }

    private sealed class FakeIssueService : IViettelInvoiceIssueService
    {
        public int Calls { get; private set; }
        public List<int> InvoiceIds { get; } = [];
        public Exception? Exception { get; set; }
        public Result<ViettelInvoiceIssueResultDto> Result { get; set; } = Result<ViettelInvoiceIssueResultDto>.Success(new ViettelInvoiceIssueResultDto { IsSuccess = true });
        public Task<Result<ViettelInvoiceIssueResultDto>> IssueAsync(int invoiceHeadId, CancellationToken ct = default)
        {
            Calls++;
            InvoiceIds.Add(invoiceHeadId);
            if (Exception != null)
                throw Exception;
            return Task.FromResult(Result);
        }
    }

    private sealed class FoundLookupService : IViettelInvoiceSyncService
    {
        public Task<Result<ViettelInvoiceLookupResultDto>> SyncByTransactionUuidAsync(int invoiceHeadId, CancellationToken ct = default)
            => Task.FromResult(Result<ViettelInvoiceLookupResultDto>.Success(new ViettelInvoiceLookupResultDto { InvoiceHeadId = invoiceHeadId, IsFound = true, TransactionUuid = "test-uuid" }));
    }
    private sealed class CountingLookupService
    : IViettelInvoiceSyncService
    {
        public int Calls
        {
            get;
            private set;
        }

        public Task<Result<ViettelInvoiceLookupResultDto>>
            SyncByTransactionUuidAsync(
                int invoiceHeadId,
                CancellationToken ct = default)
        {
            Calls++;

            return Task.FromResult(
                Result<ViettelInvoiceLookupResultDto>
                    .Success(
                        new ViettelInvoiceLookupResultDto
                        {
                            InvoiceHeadId =
                                invoiceHeadId,

                            IsFound = false,

                            TransactionUuid =
                                "test-uuid"
                        }));
        }
    }
    private sealed class NoopUnitOfWork : IAppUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default) => Task.FromResult<IAppTransaction>(new NoopTransaction());
    }

    private sealed class NoopTransaction : IAppTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StoreTenant(int? storeId) : ITenantContext
    {
        public int? StoreId => storeId;
        public bool IsHostAdmin => false;
        public string? Subdomain => "test";
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 5;
        public string? UserName => "tester";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedTimeProvider(DateTime nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
    }
}
