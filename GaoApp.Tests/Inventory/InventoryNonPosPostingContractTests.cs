using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Configuration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GaoApp.Tests.Inventory;

[Collection("R1FinalDatabasePreflight")]
public sealed class InventoryNonPosPostingContractTests
{
    private static readonly TimeSpan RelationalTestTimeout =
        TimeSpan.FromSeconds(90);

    [Fact]
    public async Task Empty_batch_is_no_op_without_active_transaction()
    {
        var balances = new RecordingBalanceRepository();
        var warehouses = new RecordingWarehouseRepository();
        var service = CreateService(
            balances,
            warehouses,
            hasActiveTransaction: false);

        await service.PreLockBalancesAsync([]);

        warehouses.RequestedIds.Should().BeEmpty();
        balances.LockedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Non_empty_batch_requires_active_transaction_before_repository_access()
    {
        var balances = new RecordingBalanceRepository();
        var warehouses = new RecordingWarehouseRepository();
        var service = CreateService(
            balances,
            warehouses,
            hasActiveTransaction: false);

        var action = () => service.PreLockBalancesAsync(
            [new InventoryPostingLockKey(1, 10, 100)]);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*active database transaction*");
        warehouses.RequestedIds.Should().BeEmpty();
        balances.LockedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Batch_validates_every_key_before_first_lock()
    {
        var balances = new RecordingBalanceRepository();
        var warehouses = new RecordingWarehouseRepository(
            new Warehouse { Id = 10, StoreId = 1 });
        var service = CreateService(
            balances,
            warehouses,
            hasActiveTransaction: true);

        var action = () => service.PreLockBalancesAsync(
        [
            new InventoryPostingLockKey(1, 10, 100),
            new InventoryPostingLockKey(1, 10, 0)
        ]);

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*greater than zero*");
        warehouses.RequestedIds.Should().BeEmpty();
        balances.LockedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Batch_deduplicates_and_locks_in_store_warehouse_variant_order()
    {
        var balances = new RecordingBalanceRepository();
        var warehouses = new RecordingWarehouseRepository(
            new Warehouse { Id = 20, StoreId = 2 },
            new Warehouse { Id = 10, StoreId = 1 },
            new Warehouse { Id = 11, StoreId = 1 });
        var service = CreateService(
            balances,
            warehouses,
            hasActiveTransaction: true);

        await service.PreLockBalancesAsync(
        [
            new InventoryPostingLockKey(2, 20, 200),
            new InventoryPostingLockKey(1, 11, 101),
            new InventoryPostingLockKey(1, 10, 102),
            new InventoryPostingLockKey(1, 10, 100),
            new InventoryPostingLockKey(1, 10, 100)
        ]);

        balances.LockedKeys.Should().Equal(
            new InventoryPostingLockKey(1, 10, 100),
            new InventoryPostingLockKey(1, 10, 102),
            new InventoryPostingLockKey(1, 11, 101),
            new InventoryPostingLockKey(2, 20, 200));
    }

    [Fact]
    public async Task Warehouse_store_mismatch_fails_before_first_lock()
    {
        var balances = new RecordingBalanceRepository();
        var warehouses = new RecordingWarehouseRepository(
            new Warehouse { Id = 10, StoreId = 9 },
            new Warehouse { Id = 11, StoreId = 1 });
        var service = CreateService(
            balances,
            warehouses,
            hasActiveTransaction: true);

        var action = () => service.PreLockBalancesAsync(
        [
            new InventoryPostingLockKey(1, 10, 100),
            new InventoryPostingLockKey(1, 11, 101)
        ]);

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*does not belong to store*");
        balances.LockedKeys.Should().BeEmpty();
    }

    [Fact]
    public void Adjustment_document_factory_uses_stable_document_and_line_identity()
    {
        var factory = new InventoryMovementFactory();
        var document = CreatePendingAdjustmentDocument();
        var expectedReferenceId =
            document.Id.ToString(CultureInfo.InvariantCulture);
        var persistedLineIds = document.Lines
            .OrderBy(line => line.Id)
            .Select(line => line.Id)
            .ToArray();
        var occurredAtUtc = new DateTime(
            2026,
            8,
            1,
            12,
            0,
            0,
            DateTimeKind.Utc);

        var increase = factory.CreateAdjustmentIncrease(
            10,
            100,
            2m,
            7.5m,
            expectedReferenceId,
            persistedLineIds[0],
            "count correction",
            occurredAtUtc);
        var decrease = factory.CreateAdjustmentDecrease(
            10,
            100,
            2m,
            7.5m,
            expectedReferenceId,
            persistedLineIds[1],
            "damage correction",
            occurredAtUtc);

        increase.ReferenceType.Should().Be(
            InventoryReferenceType.Adjustment);
        increase.ReferenceId.Should().Be(expectedReferenceId);
        increase.ReferenceLineId.Should().Be(persistedLineIds[0]);
        increase.ReferenceSubKey.Should().BeNull();
        increase.SkipIfExists.Should().BeTrue();
        decrease.ReferenceType.Should().Be(
            InventoryReferenceType.Adjustment);
        decrease.ReferenceId.Should().Be(expectedReferenceId);
        decrease.ReferenceLineId.Should().Be(persistedLineIds[1]);
        decrease.ReferenceSubKey.Should().BeNull();
        decrease.SkipIfExists.Should().BeTrue();
    }

    [Fact]
    public void Adjustment_approval_derives_one_invariant_document_reference_for_both_paths()
    {
        var approveSource = ReadAdjustmentApprovalSource();

        GetAdjustmentReferenceContractViolations(approveSource)
            .Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AdjustmentReferenceContractMutants))]
    public void Adjustment_reference_contract_rejects_mutant(
        string mutant,
        string approveSource,
        string[] expectedViolations)
    {
        var baselineViolations =
            GetAdjustmentReferenceContractViolations(
                BuildSyntheticAdjustmentReferenceContract());
        var violations =
            GetAdjustmentReferenceContractViolations(approveSource);

        baselineViolations.Should().BeEmpty(
            because: $"{mutant} must start from the valid contract baseline");
        violations.Should().NotBeEmpty(
            because: $"{mutant} must violate the durable identity contract");
        foreach (var expectedViolation in expectedViolations)
        {
            violations.Should().Contain(
                expectedViolation,
                because: $"{mutant} must be rejected for this violation");
        }
    }

    public static IEnumerable<object[]>
        AdjustmentReferenceContractMutants()
    {
        yield return
        [
            "M1 CurrentCulture reassignment",
            BuildSyntheticAdjustmentReferenceContract(
                reassignment:
                "documentReferenceId = " +
                "document.Id.ToString(CultureInfo.CurrentCulture);"),
            new[]
            {
                "DocumentReferenceAssignmentCount",
                "CurrentCulture"
            }
        ];
        yield return
        [
            "M2 parameterless reassignment",
            BuildSyntheticAdjustmentReferenceContract(
                reassignment:
                "documentReferenceId = document.Id.ToString();"),
            new[]
            {
                "DocumentReferenceAssignmentCount",
                "ParameterlessDocumentIdConversion"
            }
        ];
        yield return
        [
            "M3 interpolation reassignment",
            BuildSyntheticAdjustmentReferenceContract(
                reassignment:
                "documentReferenceId = $\"{document.Id}\";"),
            new[]
            {
                "DocumentReferenceAssignmentCount"
            }
        ];
        yield return
        [
            "M4 decrease direct conversion",
            BuildSyntheticAdjustmentReferenceContract(
                decreaseReferenceArgument:
                "document.Id.ToString(CultureInfo.InvariantCulture)"),
            new[]
            {
                "InvariantConversionCount",
                "DecreaseReferenceArgument"
            }
        ];
        yield return
        [
            "M5 separate decrease variable",
            BuildSyntheticAdjustmentReferenceContract(
                additionalIdentityDeclaration:
                "var decreaseReferenceId = " +
                "document.Id.ToString(CultureInfo.InvariantCulture);",
                decreaseReferenceArgument: "decreaseReferenceId"),
            new[]
            {
                "InvariantConversionCount",
                "DecreaseReferenceArgument"
            }
        ];
        yield return
        [
            "M6 CurrentUICulture compound reassignment",
            BuildSyntheticAdjustmentReferenceContract(
                reassignment:
                "documentReferenceId ??= " +
                "document.Id.ToString(CultureInfo.CurrentUICulture);"),
            new[]
            {
                "DocumentReferenceAssignmentCount",
                "CurrentUICulture"
            }
        ];
        yield return
        [
            "M7 legal comment-trivia reassignment",
            BuildSyntheticAdjustmentReferenceContract(
                reassignment:
                "documentReferenceId /* legal C# trivia */ = " +
                "$\"{document.Id}\";"),
            new[]
            {
                "DocumentReferenceAssignmentCount"
            }
        ];
        yield return
        [
            "M8 commented declaration bypass",
            ReplaceSyntheticInvariantDeclaration(
                BuildSyntheticAdjustmentReferenceContract(),
                """
                BuildReference(document, out var documentReferenceId);

                // var documentReferenceId =
                //     document.Id.ToString(CultureInfo.InvariantCulture);
                """),
            new[]
            {
                "InvariantDeclarationCount",
                "InvariantConversionCount"
            }
        ];
        yield return
        [
            "M9 string-literal declaration bypass",
            ReplaceSyntheticInvariantDeclaration(
                BuildSyntheticAdjustmentReferenceContract(),
                """
                BuildReference(document, out var documentReferenceId);

                var fakeContract =
                    "var documentReferenceId = " +
                    "document.Id.ToString(CultureInfo.InvariantCulture);";
                """),
            new[]
            {
                "InvariantDeclarationCount",
                "InvariantConversionCount"
            }
        ];
        yield return
        [
            "M10 commented correct decrease with executable bypass",
            BuildSyntheticAdjustmentReferenceContract(
                sourceBeforeDecreaseCall:
                    BuildCommentedCorrectDecreaseCall(),
                decreaseReferenceArgument:
                    "document.Id.ToString(CultureInfo.InvariantCulture)"),
            new[]
            {
                "DecreaseReferenceArgument"
            }
        ];
    }

    [Theory]
    [MemberData(nameof(AdjustmentReferenceContractTriviaControls))]
    public void Adjustment_reference_contract_ignores_harmless_non_code_text(
        string control,
        string approveSource)
    {
        GetAdjustmentReferenceContractViolations(approveSource)
            .Should().BeEmpty(
                because: $"{control} is non-code trivia or literal content");
    }

    public static IEnumerable<object[]>
        AdjustmentReferenceContractTriviaControls()
    {
        yield return
        [
            "CurrentCulture in comment",
            BuildSyntheticAdjustmentReferenceContract(
                reassignment:
                "// CultureInfo.CurrentCulture")
        ];
        yield return
        [
            "parameterless conversion in string",
            BuildSyntheticAdjustmentReferenceContract(
                reassignment:
                "var harmlessText = \"document.Id.ToString()\";")
        ];
        yield return
        [
            "assignment in comment",
            BuildSyntheticAdjustmentReferenceContract(
                reassignment:
                "// documentReferenceId = alternateReferenceId;")
        ];
    }

    [Fact]
    public void Adjustment_reference_scanner_strips_non_code_preserving_layout()
    {
        var source =
            """
            var before = 1;
            // line_marker " quote
            var block = before /* block_marker
            " quote */ + 1;
            var regular =
                "regular_marker // not a comment \" escaped quote";
            var verbatim =
                @"verbatim_marker "" quoted /* not a comment */";
            var interpolated =
                $"interpolated_marker {document.Id}";
            var interpolatedVerbatim =
                $@"interpolated_verbatim_marker {document.Id}";
            var alternateInterpolated =
                @$"alternate_interpolated_marker {document.Id}";
            var quote = '\'';
            var slash = '\\';
            documentReferenceId /* trivia_marker */ = value;
            var after = 2;
            """;

        var sanitized =
            StripNonCodeTriviaPreservingLayout(source);

        sanitized.Length.Should().Be(source.Length);
        sanitized
            .Select((value, index) => (value, index))
            .Where(item => item.value == '\n')
            .Select(item => item.index)
            .Should().Equal(
                source
                    .Select((value, index) => (value, index))
                    .Where(item => item.value == '\n')
                    .Select(item => item.index));
        sanitized.Should().Contain("var before = 1;");
        sanitized.Should().Contain("var after = 2;");
        Regex.IsMatch(
                sanitized,
                @"\bdocumentReferenceId\s+=\s+value;",
                RegexOptions.CultureInvariant)
            .Should().BeTrue();

        foreach (var marker in new[]
                 {
                     "line_marker",
                     "block_marker",
                     "regular_marker",
                     "verbatim_marker",
                     "interpolated_marker",
                     "interpolated_verbatim_marker",
                     "alternate_interpolated_marker",
                     "trivia_marker"
                 })
        {
            sanitized.Should().NotContain(marker);
        }
    }

    [Fact]
    public async Task Adjustment_failure_rolls_back_outer_transaction_without_approving_document()
    {
        var events = new List<string>();
        var document = CreatePendingAdjustmentDocument();
        var repository = new FakeAdjustmentDocumentRepository(
            document,
            events);
        var movements = new FailingMovementService(
            events,
            failOnCreateCall: 2);
        var unitOfWork = new RecordingUnitOfWork(events);
        var service = CreateAdjustmentService(
            repository,
            movements,
            unitOfWork);

        var action = () => service.ApproveAsync(
            new ApproveInventoryAdjustmentDocumentRequest
            {
                Id = document.Id,
                ApprovalNote = "approved"
            });

        await action.Should()
            .ThrowAsync<InjectedAdjustmentFailureException>();
        events.Should().Equal(
            "begin",
            "prelock",
            "movement:501",
            "movement:502",
            "rollback");
        document.Status.Should().Be(
            InventoryAdjustmentDocumentStatus.PendingApproval);
        movements.Requests.Should().HaveCount(2);
        var expectedReferenceId =
            document.Id.ToString(CultureInfo.InvariantCulture);
        movements.Requests.Select(x => x.ReferenceId)
            .Should().OnlyContain(
                x => x == expectedReferenceId);
        movements.Requests.Select(x => x.ReferenceLineId)
            .Should().Equal(501, 502);
    }

    [Fact]
    public async Task Approved_adjustment_retry_returns_detail_without_new_transaction_or_movement()
    {
        var events = new List<string>();
        var document = CreatePendingAdjustmentDocument();
        document.Status =
            InventoryAdjustmentDocumentStatus.Approved;
        var repository = new FakeAdjustmentDocumentRepository(
            document,
            events);
        var movements = new FailingMovementService(
            events,
            failOnCreateCall: null);
        var unitOfWork = new RecordingUnitOfWork(events);
        var service = CreateAdjustmentService(
            repository,
            movements,
            unitOfWork);

        var result = await service.ApproveAsync(
            new ApproveInventoryAdjustmentDocumentRequest
            {
                Id = document.Id
            });

        result.Id.Should().Be(document.Id);
        events.Should().BeEmpty();
        movements.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Adjustment_document_second_movement_failure_should_rollback_first_real_posting()
    {
        using var timeoutCts =
            new CancellationTokenSource(RelationalTestTimeout);
        var ct = timeoutCts.Token;
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync(ct: ct);
        var seed = await database.SeedInventoryCatalogAsync(ct: ct);
        var fixture = await CreateRelationalAdjustmentFixtureAsync(
            database,
            seed,
            ct);
        await SeedInitialInventoryAsync(
            database,
            fixture,
            ct);
        var before = await CaptureRelationalSnapshotAsync(
            database,
            fixture,
            ct);
        RealMovementFailureDecorator? movements = null;

        await using (var posting =
                     database.CreateTenantContext(seed.StoreId))
        {
            movements = new RealMovementFailureDecorator(
                CreateRealMovementService(posting),
                failOnCreateCall: 2);
            var service = CreateRealAdjustmentService(
                posting,
                movements);

            var action = () => service.ApproveAsync(
                new ApproveInventoryAdjustmentDocumentRequest
                {
                    Id = fixture.DocumentId,
                    ApprovalNote = "must roll back"
                },
                ct);

            await action.Should()
                .ThrowExactlyAsync<
                    InjectedRelationalAdjustmentFailureException>();
            posting.Database.CurrentTransaction.Should().BeNull(
                "the real outer UnitOfWork must complete its rollback");
        }

        movements.Should().NotBeNull();
        movements!.PreLockCallCount.Should().Be(1);
        movements.CreateCallCount.Should().Be(2);
        movements.DelegatedCreateCallCount.Should().Be(1);
        movements.FirstDelegatedResult.Should().NotBeNull();
        movements.FirstDelegatedResult!.IsCreated.Should().BeTrue();
        movements.FirstDelegatedResult.InventoryTransactionId
            .Should().BeGreaterThan(0);
        movements.FirstDelegatedResult.InventoryValuationEntryIds
            .Should().NotBeEmpty();
        movements.Requests.Should().HaveCount(2);
        movements.Requests.Select(x => x.ReferenceType)
            .Should().OnlyContain(
                x => x == InventoryReferenceType.Adjustment);
        movements.Requests.Select(x => x.ReferenceId)
            .Should().OnlyContain(
                x => x == fixture.InvariantDocumentId);
        movements.Requests.Select(x => x.ReferenceLineId)
            .Should().Equal(fixture.LineIds.Cast<int?>());
        movements.Requests.Select(x => x.SkipIfExists)
            .Should().OnlyContain(x => x);
        movements.PreLockedKeys.Should().Equal(
            fixture.VariantIds
                .OrderBy(x => x)
                .Select(x => new InventoryPostingLockKey(
                    fixture.StoreId,
                    fixture.WarehouseId,
                    x)));

        var after = await CaptureRelationalSnapshotAsync(
            database,
            fixture,
            ct);

        AssertRelationalSnapshotUnchanged(before, after);
        after.Document.Status.Should().Be(
            InventoryAdjustmentDocumentStatus.PendingApproval);
        after.Document.ApprovedAtUtc.Should().BeNull();
        after.Document.ApprovedByUserId.Should().BeNull();
        after.Document.ApprovalNote.Should().BeNull();
        after.DocumentTransactionIds.Should().BeEmpty();
        after.DocumentValuationIds.Should().BeEmpty();
        after.DocumentLayerIds.Should().BeEmpty();
        after.DocumentAllocationIds.Should().BeEmpty();
        after.Balances.Should().HaveCount(2);
        after.Layers.Should().HaveCount(2);
        after.AllocationIds.Should().BeEmpty(
            "the first real outbound allocation must be rolled back");
    }

    [Fact]
    public async Task Approved_adjustment_document_retry_should_not_create_new_postings()
    {
        using var timeoutCts =
            new CancellationTokenSource(RelationalTestTimeout);
        var ct = timeoutCts.Token;
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync(ct: ct);
        var seed = await database.SeedInventoryCatalogAsync(ct: ct);
        var fixture = await CreateRelationalAdjustmentFixtureAsync(
            database,
            seed,
            ct);
        await SeedInitialInventoryAsync(
            database,
            fixture,
            ct);

        await using (var firstApproval =
                     database.CreateTenantContext(seed.StoreId))
        {
            var service = CreateRealAdjustmentService(
                firstApproval,
                CreateRealMovementService(firstApproval));

            var result = await service.ApproveAsync(
                new ApproveInventoryAdjustmentDocumentRequest
                {
                    Id = fixture.DocumentId,
                    ApprovalNote = "approved once"
                },
                ct);

            result.Id.Should().Be(fixture.DocumentId);
            result.Status.Should().Be(
                InventoryAdjustmentDocumentStatus.Approved);
            firstApproval.Database.CurrentTransaction.Should().BeNull();
        }

        var afterFirstApproval =
            await CaptureRelationalSnapshotAsync(
                database,
                fixture,
                ct);
        afterFirstApproval.Document.Status.Should().Be(
            InventoryAdjustmentDocumentStatus.Approved);
        afterFirstApproval.Document.ApprovedAtUtc
            .Should().NotBeNull();
        afterFirstApproval.Document.ApprovalNote
            .Should().Be("approved once");
        afterFirstApproval.DocumentTransactionIds
            .Should().HaveCount(2);
        afterFirstApproval.DocumentValuationIds
            .Should().HaveCount(2);
        afterFirstApproval.DocumentLayerIds
            .Should().BeEmpty(
                "outbound adjustments consume existing FIFO layers");
        afterFirstApproval.DocumentAllocationIds
            .Should().HaveCount(2);
        afterFirstApproval.Balances.Should().HaveCount(2);
        afterFirstApproval.Layers.Should().HaveCount(2);
        afterFirstApproval.AllocationIds.Should().HaveCount(2);
        await AssertExactlyOneDocumentMovementPerLineAsync(
            database,
            fixture,
            ct);

        RealMovementFailureDecorator? retryMovements = null;
        await using (var retry =
                     database.CreateTenantContext(seed.StoreId))
        {
            retryMovements = new RealMovementFailureDecorator(
                CreateRealMovementService(retry),
                failOnCreateCall: null);
            var service = CreateRealAdjustmentService(
                retry,
                retryMovements);

            var result = await service.ApproveAsync(
                new ApproveInventoryAdjustmentDocumentRequest
                {
                    Id = fixture.DocumentId,
                    ApprovalNote = "must be ignored"
                },
                ct);

            result.Id.Should().Be(fixture.DocumentId);
            result.Status.Should().Be(
                InventoryAdjustmentDocumentStatus.Approved);
            retry.Database.CurrentTransaction.Should().BeNull(
                "the approved-document short circuit must not open an outer transaction");
        }

        retryMovements.Should().NotBeNull();
        retryMovements!.PreLockCallCount.Should().Be(0);
        retryMovements.CreateCallCount.Should().Be(0);
        retryMovements.DelegatedCreateCallCount.Should().Be(0);
        retryMovements.Requests.Should().BeEmpty();

        var afterRetry = await CaptureRelationalSnapshotAsync(
            database,
            fixture,
            ct);
        AssertRelationalSnapshotUnchanged(
            afterFirstApproval,
            afterRetry);
        afterRetry.Document.Status.Should().Be(
            InventoryAdjustmentDocumentStatus.Approved);
        await AssertExactlyOneDocumentMovementPerLineAsync(
            database,
            fixture,
            ct);
    }

    [Theory]
    [InlineData(
        "GaoApp.Application/Services/Inventory/StockDocumentService.cs",
        "_stockDocumentRepository.BeginTransactionAsync(ct)",
        "_inventoryMovementService.CreateAsync(movementRequest, ct)")]
    [InlineData(
        "GaoApp.Application/Services/Inventory/StockCountService.cs",
        "_stockCountRepository.BeginTransactionAsync(ct)",
        "_inventoryMovementService.CreateAsync(movementRequest, ct)")]
    [InlineData(
        "GaoApp.Application/Services/Inventory/StockTransferService.cs",
        "_stockTransferRepository.BeginTransactionAsync(ct)",
        "_inventoryMovementService.CreateAsync(transferOutRequest, ct)")]
    public void Non_pos_caller_prelocks_after_begin_and_before_first_movement(
        string relativePath,
        string beginCall,
        string firstMovementCall)
    {
        var source = ReadRepositoryFile(relativePath);
        var beginIndex = source.IndexOf(
            beginCall,
            StringComparison.Ordinal);
        var preLockIndex = source.IndexOf(
            "_inventoryMovementService.PreLockBalancesAsync(",
            beginIndex,
            StringComparison.Ordinal);
        var movementIndex = source.IndexOf(
            firstMovementCall,
            preLockIndex,
            StringComparison.Ordinal);

        beginIndex.Should().BeGreaterThanOrEqualTo(0);
        preLockIndex.Should().BeGreaterThan(beginIndex);
        movementIndex.Should().BeGreaterThan(preLockIndex);
    }

    [Fact]
    public void Transfer_preserves_out_before_in_business_order()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Application/Services/Inventory/StockTransferService.cs");
        var outFactory = source.IndexOf(
            "CreateTransferOutRequest(",
            StringComparison.Ordinal);
        var outMovement = source.IndexOf(
            "_inventoryMovementService.CreateAsync(transferOutRequest, ct)",
            outFactory,
            StringComparison.Ordinal);
        var inFactory = source.IndexOf(
            "CreateTransferInRequest(",
            outMovement,
            StringComparison.Ordinal);
        var inMovement = source.IndexOf(
            "_inventoryMovementService.CreateAsync(transferInRequest, ct)",
            inFactory,
            StringComparison.Ordinal);

        outFactory.Should().BeGreaterThanOrEqualTo(0);
        outMovement.Should().BeGreaterThan(outFactory);
        inFactory.Should().BeGreaterThan(outMovement);
        inMovement.Should().BeGreaterThan(inFactory);
    }

    [Fact]
    public void Adjustment_approval_wraps_prelock_movements_and_status_save_in_outer_transaction()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Application/Services/Inventory/InventoryAdjustmentDocumentService.cs");
        var approveStart = source.IndexOf(
            "ApproveAsync(",
            StringComparison.Ordinal);
        var beginIndex = source.IndexOf(
            "_unitOfWork.BeginTransactionAsync(ct)",
            approveStart,
            StringComparison.Ordinal);
        var preLockIndex = source.IndexOf(
            "_inventoryMovementService.PreLockBalancesAsync(",
            beginIndex,
            StringComparison.Ordinal);
        var movementIndex = source.IndexOf(
            "_inventoryMovementService.CreateAsync(",
            preLockIndex,
            StringComparison.Ordinal);
        var approvedIndex = source.IndexOf(
            "document.Status = InventoryAdjustmentDocumentStatus.Approved",
            movementIndex,
            StringComparison.Ordinal);
        var saveIndex = source.IndexOf(
            "_unitOfWork.SaveChangesAsync(ct)",
            approvedIndex,
            StringComparison.Ordinal);
        var commitIndex = source.IndexOf(
            "_unitOfWork.CommitTransactionAsync(ct)",
            saveIndex,
            StringComparison.Ordinal);
        var rollbackIndex = source.IndexOf(
            "_unitOfWork.RollbackTransactionAsync(",
            commitIndex,
            StringComparison.Ordinal);

        beginIndex.Should().BeGreaterThan(approveStart);
        preLockIndex.Should().BeGreaterThan(beginIndex);
        movementIndex.Should().BeGreaterThan(preLockIndex);
        approvedIndex.Should().BeGreaterThan(movementIndex);
        saveIndex.Should().BeGreaterThan(approvedIndex);
        commitIndex.Should().BeGreaterThan(saveIndex);
        rollbackIndex.Should().BeGreaterThan(commitIndex);
    }

    private static async Task<RelationalAdjustmentFixture>
        CreateRelationalAdjustmentFixtureAsync(
            InventoryPostingLocalDb database,
            InventoryPostingSeed seed,
            CancellationToken ct)
    {
        await using var db =
            database.CreateTenantContext(seed.StoreId);
        var firstVariant = await db.ProductVariants
            .AsNoTracking()
            .SingleAsync(
                x => x.Id == seed.ProductVariantId,
                ct);
        var baseUnitId = await db.Products
            .AsNoTracking()
            .Where(x => x.Id == firstVariant.ProductId)
            .Select(x => x.BaseUnitId)
            .SingleAsync(ct);
        var secondVariant = new ProductVariant
        {
            StoreId = seed.StoreId,
            ProductId = firstVariant.ProductId,
            Sku = $"R2-ADJ-{Guid.NewGuid():N}",
            CostPrice = 12m,
            Price = 24m,
            IsActive = true
        };
        db.ProductVariants.Add(secondVariant);
        await db.SaveChangesAsync(ct);

        var document = new InventoryAdjustmentDocument
        {
            StoreId = seed.StoreId,
            DocumentNo = $"R2-ADJ-{Guid.NewGuid():N}"[..20],
            DocumentDate = DateTime.UtcNow,
            WarehouseId = seed.WarehouseId,
            AdjustmentType =
                InventoryTransactionType.AdjustmentDecrease,
            Status =
                InventoryAdjustmentDocumentStatus.PendingApproval,
            SubmittedAtUtc = DateTime.UtcNow,
            Note = "relational adjustment atomicity evidence",
            Lines =
            [
                new InventoryAdjustmentLine
                {
                    StoreId = seed.StoreId,
                    ProductVariantId = seed.ProductVariantId,
                    UnitId = baseUnitId,
                    Quantity = 2m,
                    Factor = 1m,
                    BaseQuantity = 2m,
                    ProvisionalUnitCost = 10m,
                    Note = "first real outbound movement"
                },
                new InventoryAdjustmentLine
                {
                    StoreId = seed.StoreId,
                    ProductVariantId = secondVariant.Id,
                    UnitId = baseUnitId,
                    Quantity = 3m,
                    Factor = 1m,
                    BaseQuantity = 3m,
                    ProvisionalUnitCost = 12m,
                    Note = "second real outbound movement"
                }
            ]
        };
        db.InventoryAdjustmentDocuments.Add(document);
        await db.SaveChangesAsync(ct);

        return new RelationalAdjustmentFixture(
            document.Id,
            seed.StoreId,
            seed.WarehouseId,
            document.Id.ToString(CultureInfo.InvariantCulture),
            document.Lines
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArray(),
            document.Lines
                .OrderBy(x => x.Id)
                .Select(x => x.ProductVariantId)
                .ToArray());
    }

    private static async Task SeedInitialInventoryAsync(
        InventoryPostingLocalDb database,
        RelationalAdjustmentFixture fixture,
        CancellationToken ct)
    {
        await using var db =
            database.CreateTenantContext(fixture.StoreId);
        var movement = CreateRealMovementService(db);
        var quantities = new[] { 10m, 20m };
        var costs = new[] { 10m, 12m };

        for (var index = 0;
             index < fixture.VariantIds.Count;
             index++)
        {
            var result = await movement.CreateAsync(
                new CreateInventoryMovementRequest
                {
                    WarehouseId = fixture.WarehouseId,
                    ProductVariantId =
                        fixture.VariantIds[index],
                    QuantityChange = quantities[index],
                    UnitCost = costs[index],
                    TransactionType =
                        InventoryTransactionType.PurchaseReceipt,
                    ReferenceType =
                        InventoryReferenceType.PurchaseReceipt,
                    ReferenceId =
                        $"R2-ADJ-BASE-{fixture.DocumentId}-{index + 1}",
                    ReferenceLineId = index + 1,
                    OccurredAtUtc = DateTime.UtcNow,
                    SkipIfExists = true
                },
                ct);

            result.IsCreated.Should().BeTrue();
            result.InventoryTransactionId
                .Should().BeGreaterThan(0);
            result.InventoryValuationEntryIds
                .Should().ContainSingle();
        }
    }

    private static async Task<RelationalInventorySnapshot>
        CaptureRelationalSnapshotAsync(
            InventoryPostingLocalDb database,
            RelationalAdjustmentFixture fixture,
            CancellationToken ct)
    {
        await using var db = database.CreateHostContext();
        var document = await db.InventoryAdjustmentDocuments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.StoreId == fixture.StoreId
                && x.Id == fixture.DocumentId)
            .Select(x => new RelationalDocumentSnapshot(
                x.Status,
                x.ApprovalNote,
                x.SubmittedAtUtc,
                x.SubmittedByUserId,
                x.ApprovedAtUtc,
                x.ApprovedByUserId,
                x.RejectedAtUtc,
                x.RejectedByUserId,
                x.CancelledAtUtc,
                x.CancelledByUserId))
            .SingleAsync(ct);

        var transactionIds = await db.InventoryTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.StoreId == fixture.StoreId)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToArrayAsync(ct);
        var valuationIds = await db.InventoryValuationEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.StoreId == fixture.StoreId)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToArrayAsync(ct);
        var allocationIds = await db.InventoryCostLayerAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.StoreId == fixture.StoreId)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToArrayAsync(ct);
        var balances = await db.InventoryBalances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.StoreId == fixture.StoreId
                && x.WarehouseId == fixture.WarehouseId
                && fixture.VariantIds.Contains(
                    x.ProductVariantId))
            .OrderBy(x => x.ProductVariantId)
            .Select(x => new RelationalBalanceSnapshot(
                x.Id,
                x.WarehouseId,
                x.ProductVariantId,
                x.OnHandQty,
                x.ReservedQty,
                x.InventoryValue,
                x.AverageUnitCost,
                x.LastInboundUnitCost,
                x.LastInboundAtUtc,
                x.LastValuationAtUtc,
                x.IsDeleted))
            .ToArrayAsync(ct);
        var layers = await db.InventoryCostLayers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.StoreId == fixture.StoreId
                && x.WarehouseId == fixture.WarehouseId
                && fixture.VariantIds.Contains(
                    x.ProductVariantId))
            .OrderBy(x => x.Id)
            .Select(x => new RelationalLayerSnapshot(
                x.Id,
                x.WarehouseId,
                x.ProductVariantId,
                x.InventoryTransactionId,
                x.InventoryValuationEntryId,
                x.ReferenceType,
                x.ReferenceId,
                x.ReferenceLineId,
                x.OriginalQuantity,
                x.RemainingQuantity,
                x.ResolvedProvisionalQty,
                x.RemainingOpenProvisionalQty,
                x.UnitCost,
                x.IsDeleted))
            .ToArrayAsync(ct);

        var documentTransactionIds =
            await db.InventoryTransactions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x =>
                    x.StoreId == fixture.StoreId
                    && x.ReferenceType
                        == InventoryReferenceType.Adjustment
                    && x.ReferenceId
                        == fixture.InvariantDocumentId)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArrayAsync(ct);
        var documentValuationIds =
            await db.InventoryValuationEntries
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x =>
                    x.StoreId == fixture.StoreId
                    && x.ReferenceType
                        == InventoryReferenceType.Adjustment
                    && x.ReferenceId
                        == fixture.InvariantDocumentId)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArrayAsync(ct);
        var documentLayerIds =
            await db.InventoryCostLayers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x =>
                    x.StoreId == fixture.StoreId
                    && x.ReferenceType
                        == InventoryReferenceType.Adjustment
                    && x.ReferenceId
                        == fixture.InvariantDocumentId)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArrayAsync(ct);
        var documentAllocationIds =
            documentValuationIds.Length == 0
                ? []
                : await db.InventoryCostLayerAllocations
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x =>
                        x.StoreId == fixture.StoreId
                        && documentValuationIds.Contains(
                            x.InventoryValuationEntryId))
                    .OrderBy(x => x.Id)
                    .Select(x => x.Id)
                    .ToArrayAsync(ct);

        return new RelationalInventorySnapshot(
            document,
            transactionIds,
            valuationIds,
            layers,
            allocationIds,
            balances,
            documentTransactionIds,
            documentValuationIds,
            documentLayerIds,
            documentAllocationIds);
    }

    private static void AssertRelationalSnapshotUnchanged(
        RelationalInventorySnapshot expected,
        RelationalInventorySnapshot actual)
    {
        actual.Should().BeEquivalentTo(
            expected,
            options => options.WithStrictOrdering());
    }

    private static async Task
        AssertExactlyOneDocumentMovementPerLineAsync(
            InventoryPostingLocalDb database,
            RelationalAdjustmentFixture fixture,
            CancellationToken ct)
    {
        await using var db = database.CreateHostContext();
        var lineIds = await db.InventoryTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.StoreId == fixture.StoreId
                && x.ReferenceType
                    == InventoryReferenceType.Adjustment
                && x.ReferenceId
                    == fixture.InvariantDocumentId)
            .OrderBy(x => x.ReferenceLineId)
            .Select(x => x.ReferenceLineId)
            .ToArrayAsync(ct);

        lineIds.Should().BeEquivalentTo(
            fixture.LineIds.Cast<int?>());
        lineIds.GroupBy(x => x)
            .Should().OnlyContain(x => x.Count() == 1);
    }

    private static InventoryMovementService
        CreateRealMovementService(AppDbContext db)
        => new(
            new InventoryBalanceRepository(db),
            new InventoryTransactionRepository(db),
            new InventoryValuationEntryRepository(db),
            new InventoryCostLayerRepository(db),
            new InventoryCostLayerAllocationRepository(db),
            new WarehouseRepository(db),
            new InventoryPostingTransactionCoordinator(db));

    private static InventoryAdjustmentDocumentService
        CreateRealAdjustmentService(
            AppDbContext db,
            IInventoryMovementService movements)
        => new(
            new InventoryAdjustmentDocumentRepository(db),
            new WarehouseRepository(db),
            null!,
            null!,
            null!,
            null!,
            movements,
            new InventoryMovementFactory(),
            new InventoryMovementNoteBuilder(),
            new UnitOfWork(db),
            null!);

    private static InventoryMovementService CreateService(
        IInventoryBalanceRepository balances,
        IWarehouseRepository warehouses,
        bool hasActiveTransaction)
        => new(
            balances,
            null!,
            null!,
            null!,
            null!,
            warehouses,
            new FakePostingCoordinator(hasActiveTransaction));

    private static InventoryAdjustmentDocumentService
        CreateAdjustmentService(
            IInventoryAdjustmentDocumentRepository repository,
            IInventoryMovementService movements,
            IUnitOfWork unitOfWork)
        => new(
            repository,
            null!,
            null!,
            null!,
            null!,
            null!,
            movements,
            new InventoryMovementFactory(),
            new InventoryMovementNoteBuilder(),
            unitOfWork,
            null!);

    private static InventoryAdjustmentDocument
        CreatePendingAdjustmentDocument()
        => new()
        {
            Id = 42,
            StoreId = 3,
            WarehouseId = 10,
            DocumentNo = "ADJ-42",
            Status =
                InventoryAdjustmentDocumentStatus.PendingApproval,
            AdjustmentType =
                InventoryTransactionType.AdjustmentDecrease,
            Lines =
            [
                new InventoryAdjustmentLine
                {
                    Id = 501,
                    StoreId = 3,
                    ProductVariantId = 100,
                    Quantity = 1m,
                    Factor = 1m,
                    BaseQuantity = 1m,
                    ProvisionalUnitCost = 5m
                },
                new InventoryAdjustmentLine
                {
                    Id = 502,
                    StoreId = 3,
                    ProductVariantId = 101,
                    Quantity = 2m,
                    Factor = 1m,
                    BaseQuantity = 2m,
                    ProvisionalUnitCost = 6m
                }
            ]
        };

    private static string ReadAdjustmentApprovalSource()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Application/Services/Inventory/InventoryAdjustmentDocumentService.cs");
        var sanitizedSource =
            StripNonCodeTriviaPreservingLayout(source);
        const string approveBoundaryPattern =
            """
            \bpublic\s+async\s+Task\s*<
            \s*InventoryAdjustmentDocumentDetailDto\s*>
            \s+ApproveAsync\s*\(
            """;
        const string rejectBoundaryPattern =
            """
            \bpublic\s+async\s+Task\s*<
            \s*InventoryAdjustmentDocumentDetailDto\s*>
            \s+RejectAsync\s*\(
            """;
        const RegexOptions options =
            RegexOptions.IgnorePatternWhitespace
            | RegexOptions.CultureInvariant;

        var approveBoundaries = Regex.Matches(
            sanitizedSource,
            approveBoundaryPattern,
            options);
        var rejectBoundaries = Regex.Matches(
            sanitizedSource,
            rejectBoundaryPattern,
            options);

        approveBoundaries.Count.Should().Be(
            1,
            "ApproveAsync must have one unambiguous executable boundary");
        rejectBoundaries.Count.Should().Be(
            1,
            "RejectAsync must have one unambiguous executable boundary");

        var approveStart = approveBoundaries[0].Index;
        var rejectStart = rejectBoundaries[0].Index;
        rejectStart.Should().BeGreaterThan(
            approveStart,
            "RejectAsync must follow ApproveAsync");

        return source[approveStart..rejectStart];
    }

    private static string StripNonCodeTriviaPreservingLayout(
        string source)
    {
        var sanitized = source.ToCharArray();
        var index = 0;

        while (index < source.Length)
        {
            if (!TryBlankNonCodeToken(
                    source,
                    sanitized,
                    ref index))
            {
                index++;
            }
        }

        return new string(sanitized);
    }

    private static bool TryBlankNonCodeToken(
        string source,
        char[] sanitized,
        ref int index)
    {
        if (StartsWithAt(source, index, "//"))
        {
            index = BlankLineComment(
                source,
                sanitized,
                index);
            return true;
        }

        if (StartsWithAt(source, index, "/*"))
        {
            index = BlankBlockComment(
                source,
                sanitized,
                index);
            return true;
        }

        if (StartsWithAt(source, index, "$@\"")
            || StartsWithAt(source, index, "@$\""))
        {
            index = BlankInterpolatedString(
                source,
                sanitized,
                index,
                prefixLength: 3,
                verbatim: true);
            return true;
        }

        if (StartsWithAt(source, index, "$\""))
        {
            index = BlankInterpolatedString(
                source,
                sanitized,
                index,
                prefixLength: 2,
                verbatim: false);
            return true;
        }

        if (StartsWithAt(source, index, "@\""))
        {
            index = BlankVerbatimString(
                source,
                sanitized,
                index,
                prefixLength: 2);
            return true;
        }

        if (source[index] == '"')
        {
            index = BlankEscapedLiteral(
                source,
                sanitized,
                index,
                prefixLength: 1,
                delimiter: '"');
            return true;
        }

        if (source[index] == '\'')
        {
            index = BlankEscapedLiteral(
                source,
                sanitized,
                index,
                prefixLength: 1,
                delimiter: '\'');
            return true;
        }

        return false;
    }

    private static int BlankLineComment(
        string source,
        char[] sanitized,
        int start)
    {
        var index = start;
        while (index < source.Length
               && source[index] is not '\r' and not '\n')
        {
            BlankNonNewline(sanitized, index);
            index++;
        }

        return index;
    }

    private static int BlankBlockComment(
        string source,
        char[] sanitized,
        int start)
    {
        var index = start;
        while (index < source.Length)
        {
            if (StartsWithAt(source, index, "*/"))
            {
                BlankNonNewline(sanitized, index);
                BlankNonNewline(sanitized, index + 1);
                return index + 2;
            }

            BlankNonNewline(sanitized, index);
            index++;
        }

        return index;
    }

    private static int BlankEscapedLiteral(
        string source,
        char[] sanitized,
        int start,
        int prefixLength,
        char delimiter)
    {
        BlankRangePreservingNewlines(
            sanitized,
            start,
            prefixLength);
        var index = start + prefixLength;

        while (index < source.Length)
        {
            if (source[index] == '\\')
            {
                BlankNonNewline(sanitized, index);
                index++;
                if (index < source.Length)
                {
                    BlankNonNewline(sanitized, index);
                    index++;
                }

                continue;
            }

            var current = source[index];
            BlankNonNewline(sanitized, index);
            index++;
            if (current == delimiter)
            {
                return index;
            }
        }

        return index;
    }

    private static int BlankVerbatimString(
        string source,
        char[] sanitized,
        int start,
        int prefixLength)
    {
        BlankRangePreservingNewlines(
            sanitized,
            start,
            prefixLength);
        var index = start + prefixLength;

        while (index < source.Length)
        {
            if (source[index] == '"')
            {
                BlankNonNewline(sanitized, index);
                if (index + 1 < source.Length
                    && source[index + 1] == '"')
                {
                    BlankNonNewline(sanitized, index + 1);
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            BlankNonNewline(sanitized, index);
            index++;
        }

        return index;
    }

    private static int BlankInterpolatedString(
        string source,
        char[] sanitized,
        int start,
        int prefixLength,
        bool verbatim)
    {
        BlankRangePreservingNewlines(
            sanitized,
            start,
            prefixLength);
        var index = start + prefixLength;
        var interpolationDepth = 0;

        while (index < source.Length)
        {
            if (interpolationDepth == 0)
            {
                if (!verbatim && source[index] == '\\')
                {
                    BlankNonNewline(sanitized, index);
                    index++;
                    if (index < source.Length)
                    {
                        BlankNonNewline(sanitized, index);
                        index++;
                    }

                    continue;
                }

                if (source[index] == '"')
                {
                    BlankNonNewline(sanitized, index);
                    if (verbatim
                        && index + 1 < source.Length
                        && source[index + 1] == '"')
                    {
                        BlankNonNewline(sanitized, index + 1);
                        index += 2;
                        continue;
                    }

                    return index + 1;
                }

                if (source[index] == '{')
                {
                    BlankNonNewline(sanitized, index);
                    if (index + 1 < source.Length
                        && source[index + 1] == '{')
                    {
                        BlankNonNewline(sanitized, index + 1);
                        index += 2;
                        continue;
                    }

                    interpolationDepth = 1;
                    index++;
                    continue;
                }

                if (source[index] == '}'
                    && index + 1 < source.Length
                    && source[index + 1] == '}')
                {
                    BlankNonNewline(sanitized, index);
                    BlankNonNewline(sanitized, index + 1);
                    index += 2;
                    continue;
                }

                BlankNonNewline(sanitized, index);
                index++;
                continue;
            }

            if (TryBlankNonCodeToken(
                    source,
                    sanitized,
                    ref index))
            {
                continue;
            }

            var current = source[index];
            BlankNonNewline(sanitized, index);
            index++;
            if (current == '{')
            {
                interpolationDepth++;
            }
            else if (current == '}')
            {
                interpolationDepth--;
            }
        }

        return index;
    }

    private static void BlankRangePreservingNewlines(
        char[] sanitized,
        int start,
        int length)
    {
        var end = Math.Min(
            sanitized.Length,
            start + length);
        for (var index = start; index < end; index++)
        {
            BlankNonNewline(sanitized, index);
        }
    }

    private static void BlankNonNewline(
        char[] sanitized,
        int index)
    {
        if (index >= 0
            && index < sanitized.Length
            && sanitized[index] is not '\r' and not '\n')
        {
            sanitized[index] = ' ';
        }
    }

    private static bool StartsWithAt(
        string source,
        int index,
        string value)
        => index >= 0
            && index + value.Length <= source.Length
            && string.CompareOrdinal(
                source,
                index,
                value,
                0,
                value.Length) == 0;

    private static IReadOnlyList<string>
        GetAdjustmentReferenceContractViolations(
            string approveSource)
    {
        const string invariantConversionPattern =
            """
            document\s*\.\s*Id\s*\.\s*ToString\s*\(
            \s*CultureInfo\s*\.\s*InvariantCulture\s*\)
            """;
        const string invariantDeclarationPattern =
            """
            \bvar\s+documentReferenceId\s*=\s*
            document\s*\.\s*Id\s*\.\s*ToString\s*\(
            \s*CultureInfo\s*\.\s*InvariantCulture\s*\)\s*;
            """;
        const string assignmentPattern =
            """
            \bdocumentReferenceId\s*
            (?:\?\?=|<<=|>>=|\+=|-=|\*=|/=|%=|&=|\|=|\^=|=(?!=|>))
            """;
        const RegexOptions options =
            RegexOptions.IgnorePatternWhitespace
            | RegexOptions.CultureInvariant;

        var source = StripNonCodeTriviaPreservingLayout(
                approveSource)
            .ReplaceLineEndings("\n");
        var violations = new List<string>();

        if (Regex.Matches(
                source,
                invariantDeclarationPattern,
                options).Count != 1)
        {
            violations.Add("InvariantDeclarationCount");
        }

        if (Regex.Matches(
                source,
                invariantConversionPattern,
                options).Count != 1)
        {
            violations.Add("InvariantConversionCount");
        }

        if (Regex.Matches(
                source,
                assignmentPattern,
                options).Count != 1)
        {
            violations.Add("DocumentReferenceAssignmentCount");
        }

        if (Regex.IsMatch(
                source,
                @"CultureInfo\s*\.\s*CurrentCulture\b",
                options))
        {
            violations.Add("CurrentCulture");
        }

        if (Regex.IsMatch(
                source,
                @"CultureInfo\s*\.\s*CurrentUICulture\b",
                options))
        {
            violations.Add("CurrentUICulture");
        }

        if (Regex.IsMatch(
                source,
                """
                document\s*\.\s*Id\s*\.\s*ToString\s*\(\s*\)
                """,
                options))
        {
            violations.Add("ParameterlessDocumentIdConversion");
        }

        AddFactoryReferenceViolations(
            violations,
            source,
            "_inventoryMovementFactory.CreateAdjustmentIncrease",
            "IncreaseCallCount",
            "IncreaseReferenceArgument");
        AddFactoryReferenceViolations(
            violations,
            source,
            "_inventoryMovementFactory.CreateAdjustmentDecrease",
            "DecreaseCallCount",
            "DecreaseReferenceArgument");

        return violations;
    }

    private static void AddFactoryReferenceViolations(
        List<string> violations,
        string source,
        string invocationName,
        string callCountViolation,
        string referenceArgumentViolation)
    {
        var calls = GetInvocationArguments(source, invocationName);
        if (calls.Count != 1)
        {
            violations.Add(callCountViolation);
            return;
        }

        var arguments = calls[0];
        if (arguments.Count != 8
            || !string.Equals(
                arguments[4].Trim(),
                "documentReferenceId",
                StringComparison.Ordinal))
        {
            violations.Add(referenceArgumentViolation);
        }
    }

    private static IReadOnlyList<IReadOnlyList<string>>
        GetInvocationArguments(
            string source,
            string invocationName)
    {
        var calls = new List<IReadOnlyList<string>>();
        var searchStart = 0;

        while (searchStart < source.Length)
        {
            var invocationStart = source.IndexOf(
                invocationName,
                searchStart,
                StringComparison.Ordinal);
            if (invocationStart < 0)
            {
                break;
            }

            var openParenthesis = source.IndexOf(
                '(',
                invocationStart + invocationName.Length);
            if (openParenthesis < 0)
            {
                break;
            }

            var closeParenthesis =
                FindMatchingCloseParenthesis(
                    source,
                    openParenthesis);
            if (closeParenthesis < 0)
            {
                calls.Add([]);
                break;
            }

            calls.Add(SplitTopLevelArguments(
                source[(openParenthesis + 1)..closeParenthesis]));
            searchStart = closeParenthesis + 1;
        }

        return calls;
    }

    private static int FindMatchingCloseParenthesis(
        string source,
        int openParenthesis)
    {
        var depth = 0;
        for (var index = openParenthesis;
             index < source.Length;
             index++)
        {
            if (source[index] == '(')
            {
                depth++;
            }
            else if (source[index] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static IReadOnlyList<string>
        SplitTopLevelArguments(string argumentsSource)
    {
        var arguments = new List<string>();
        var depth = 0;
        var argumentStart = 0;

        for (var index = 0;
             index < argumentsSource.Length;
             index++)
        {
            switch (argumentsSource[index])
            {
                case '(':
                case '[':
                case '{':
                    depth++;
                    break;
                case ')':
                case ']':
                case '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arguments.Add(
                        argumentsSource[argumentStart..index]);
                    argumentStart = index + 1;
                    break;
            }
        }

        arguments.Add(argumentsSource[argumentStart..]);
        return arguments;
    }

    private static string BuildSyntheticAdjustmentReferenceContract(
        string? reassignment = null,
        string? additionalIdentityDeclaration = null,
        string? sourceBeforeDecreaseCall = null,
        string decreaseReferenceArgument = "documentReferenceId")
        => $$"""
             var documentReferenceId =
                 document.Id.ToString(CultureInfo.InvariantCulture);
             {{reassignment}}
             {{additionalIdentityDeclaration}}

             movementRequest =
                 _inventoryMovementFactory.CreateAdjustmentIncrease(
                     warehouseId,
                     productVariantId,
                     quantity,
                     unitCost,
                     documentReferenceId,
                     lineId,
                     note,
                     occurredAtUtc);

             {{sourceBeforeDecreaseCall}}

             movementRequest =
                 _inventoryMovementFactory.CreateAdjustmentDecrease(
                     warehouseId,
                     productVariantId,
                     quantity,
                     provisionalUnitCost,
                     {{decreaseReferenceArgument}},
                     lineId,
                     note,
                     occurredAtUtc);
             """;

    private static string ReplaceSyntheticInvariantDeclaration(
        string source,
        string replacement)
    {
        const string invariantDeclaration =
            """
            var documentReferenceId =
                document.Id.ToString(CultureInfo.InvariantCulture);
            """;
        var mutated = source.Replace(
            invariantDeclaration,
            replacement,
            StringComparison.Ordinal);

        if (string.Equals(
                mutated,
                source,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Synthetic invariant declaration was not found.");
        }

        return mutated;
    }

    private static string BuildCommentedCorrectDecreaseCall()
        =>
            """
            /*
            movementRequest =
                _inventoryMovementFactory.CreateAdjustmentDecrease(
                    warehouseId,
                    productVariantId,
                    quantity,
                    provisionalUnitCost,
                    documentReferenceId,
                    lineId,
                    note,
                    occurredAtUtc);
            */
            """;

    private static string ReadRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null
               && !File.Exists(Path.Combine(current.FullName, "GaoApp.sln")))
        {
            current = current.Parent;
        }

        current.Should().NotBeNull(
            "the test must run under a GaoApp repository checkout");
        return File.ReadAllText(
            Path.Combine(
                current!.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private sealed class FakePostingCoordinator(
        bool hasActiveTransaction)
        : IInventoryPostingTransactionCoordinator
    {
        public bool HasActiveTransaction { get; } =
            hasActiveTransaction;

        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
            => operation(ct);
    }

    private sealed class RecordingWarehouseRepository(
        params Warehouse[] warehouses)
        : IWarehouseRepository
    {
        private readonly Dictionary<int, Warehouse> _warehouses =
            warehouses.ToDictionary(x => x.Id);

        public List<int> RequestedIds { get; } = [];

        public Task<Warehouse?> GetByIdAsync(
            int id,
            CancellationToken ct = default)
        {
            RequestedIds.Add(id);
            return Task.FromResult(
                _warehouses.GetValueOrDefault(id));
        }

        public Task AddAsync(
            Warehouse warehouse,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Warehouse?> GetDefaultAsync(
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<List<Warehouse>> GetAllAsync(
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> ExistsCodeAsync(
            string code,
            int? excludeId = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> ExistsNameAsync(
            string name,
            int? excludeId = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SaveChangesAsync(
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task ClearDefaultAsync(
            int? exceptWarehouseId = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> GenerateNextCodeAsync(
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingBalanceRepository
        : IInventoryBalanceRepository
    {
        public List<InventoryPostingLockKey> LockedKeys { get; } = [];

        public Task<InventoryBalance> LockAndGetOrCreateAsync(
            int storeId,
            int warehouseId,
            int productVariantId,
            CancellationToken ct = default)
        {
            LockedKeys.Add(new InventoryPostingLockKey(
                storeId,
                warehouseId,
                productVariantId));
            return Task.FromResult(new InventoryBalance
            {
                StoreId = storeId,
                WarehouseId = warehouseId,
                ProductVariantId = productVariantId
            });
        }

        public Task<InventoryBalance?>
            GetByWarehouseAndVariantAsync(
                int warehouseId,
                int productVariantId,
                CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<InventoryBalance> GetOrCreateAsync(
            int warehouseId,
            int productVariantId,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task AddAsync(
            InventoryBalance balance,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<List<InventoryBalance>> GetByVariantAsync(
            int productVariantId,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SaveChangesAsync(
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<List<InventoryBalance>>
            GetNegativeBalancesAsync(
                CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<InventoryBalance?>
            GetDetailByWarehouseAndVariantAsync(
                int warehouseId,
                int productVariantId,
                CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<(List<InventoryBalance> Items, int TotalItems)>
            QueryCurrentBalancesAsync(
                InventoryBalanceQueryRequest request,
                CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Dictionary<int, decimal>>
            GetAvailableQtyMapByVariantIdsAsync(
                int storeId,
                int warehouseId,
                IReadOnlyCollection<int> variantIds,
                CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed record RelationalAdjustmentFixture(
        int DocumentId,
        int StoreId,
        int WarehouseId,
        string InvariantDocumentId,
        IReadOnlyList<int> LineIds,
        IReadOnlyList<int> VariantIds);

    private sealed record RelationalDocumentSnapshot(
        InventoryAdjustmentDocumentStatus Status,
        string? ApprovalNote,
        DateTime? SubmittedAtUtc,
        int? SubmittedByUserId,
        DateTime? ApprovedAtUtc,
        int? ApprovedByUserId,
        DateTime? RejectedAtUtc,
        int? RejectedByUserId,
        DateTime? CancelledAtUtc,
        int? CancelledByUserId);

    private sealed record RelationalBalanceSnapshot(
        int Id,
        int WarehouseId,
        int ProductVariantId,
        decimal OnHandQty,
        decimal ReservedQty,
        decimal InventoryValue,
        decimal AverageUnitCost,
        decimal? LastInboundUnitCost,
        DateTime? LastInboundAtUtc,
        DateTime? LastValuationAtUtc,
        bool IsDeleted);

    private sealed record RelationalLayerSnapshot(
        int Id,
        int WarehouseId,
        int ProductVariantId,
        int InventoryTransactionId,
        int InventoryValuationEntryId,
        InventoryReferenceType ReferenceType,
        string ReferenceId,
        int? ReferenceLineId,
        decimal OriginalQuantity,
        decimal RemainingQuantity,
        decimal ResolvedProvisionalQty,
        decimal RemainingOpenProvisionalQty,
        decimal UnitCost,
        bool IsDeleted);

    private sealed record RelationalInventorySnapshot(
        RelationalDocumentSnapshot Document,
        IReadOnlyList<int> TransactionIds,
        IReadOnlyList<int> ValuationIds,
        IReadOnlyList<RelationalLayerSnapshot> Layers,
        IReadOnlyList<int> AllocationIds,
        IReadOnlyList<RelationalBalanceSnapshot> Balances,
        IReadOnlyList<int> DocumentTransactionIds,
        IReadOnlyList<int> DocumentValuationIds,
        IReadOnlyList<int> DocumentLayerIds,
        IReadOnlyList<int> DocumentAllocationIds);

    private sealed class FakeAdjustmentDocumentRepository(
        InventoryAdjustmentDocument document,
        List<string> events)
        : IInventoryAdjustmentDocumentRepository
    {
        public Task<InventoryAdjustmentDocument?> GetDetailEntityAsync(
            int id,
            CancellationToken ct = default)
            => Task.FromResult<InventoryAdjustmentDocument?>(
                id == document.Id ? document : null);

        public Task<InventoryAdjustmentDocumentDetailDto?>
            GetDetailDtoAsync(
                int id,
                CancellationToken ct = default)
            => Task.FromResult<InventoryAdjustmentDocumentDetailDto?>(
                id == document.Id
                    ? new InventoryAdjustmentDocumentDetailDto
                    {
                        Id = document.Id,
                        DocumentNo = document.DocumentNo,
                        Status = document.Status
                    }
                    : null);

        public void Update(InventoryAdjustmentDocument entity)
            => events.Add("update");

        public Task<InventoryAdjustmentDocument?> GetByIdAsync(
            int id,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<PagedResult<InventoryAdjustmentDocumentListItemDto>>
            GetPagedAsync(
                InventoryAdjustmentDocumentFilterDto filter,
                CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> ExistsDocumentNoAsync(
            string documentNo,
            int? excludeId = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task AddAsync(
            InventoryAdjustmentDocument entity,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public void Remove(InventoryAdjustmentDocument entity)
            => throw new NotSupportedException();
    }

    private sealed class RealMovementFailureDecorator(
        IInventoryMovementService inner,
        int? failOnCreateCall)
        : IInventoryMovementService
    {
        private int _preLockCalls;
        private int _createCalls;
        private int _delegatedCreateCalls;

        public int PreLockCallCount =>
            Volatile.Read(ref _preLockCalls);

        public int CreateCallCount =>
            Volatile.Read(ref _createCalls);

        public int DelegatedCreateCallCount =>
            Volatile.Read(ref _delegatedCreateCalls);

        public List<InventoryPostingLockKey> PreLockedKeys { get; } =
            [];

        public List<CreateInventoryMovementRequest> Requests { get; } =
            [];

        public InventoryMovementResultDto? FirstDelegatedResult
        {
            get;
            private set;
        }

        public async Task PreLockBalancesAsync(
            IEnumerable<InventoryPostingLockKey> keys,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _preLockCalls);
            var capturedKeys = keys.ToList();
            PreLockedKeys.AddRange(capturedKeys);
            await inner.PreLockBalancesAsync(capturedKeys, ct);
        }

        public async Task<InventoryMovementResultDto> CreateAsync(
            CreateInventoryMovementRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            var call = Interlocked.Increment(ref _createCalls);
            if (call == failOnCreateCall)
            {
                throw new
                    InjectedRelationalAdjustmentFailureException();
            }

            Interlocked.Increment(ref _delegatedCreateCalls);
            var result = await inner.CreateAsync(request, ct);
            if (call == 1)
            {
                FirstDelegatedResult = result;
            }

            return result;
        }

        public Task<decimal> PeekOutboundUnitCostAsync(
            int warehouseId,
            int productVariantId,
            decimal quantity,
            CancellationToken ct = default)
            => inner.PeekOutboundUnitCostAsync(
                warehouseId,
                productVariantId,
                quantity,
                ct);
    }

    private sealed class FailingMovementService(
        List<string> events,
        int? failOnCreateCall)
        : IInventoryMovementService
    {
        private int _createCalls;

        public List<CreateInventoryMovementRequest> Requests { get; } =
            [];

        public Task PreLockBalancesAsync(
            IEnumerable<InventoryPostingLockKey> keys,
            CancellationToken ct = default)
        {
            events.Add("prelock");
            return Task.CompletedTask;
        }

        public Task<InventoryMovementResultDto> CreateAsync(
            CreateInventoryMovementRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            events.Add($"movement:{request.ReferenceLineId}");
            if (Interlocked.Increment(ref _createCalls)
                == failOnCreateCall)
            {
                throw new InjectedAdjustmentFailureException();
            }

            return Task.FromResult(new InventoryMovementResultDto
            {
                IsCreated = true,
                WarehouseId = request.WarehouseId,
                ProductVariantId = request.ProductVariantId
            });
        }

        public Task<decimal> PeekOutboundUnitCostAsync(
            int warehouseId,
            int productVariantId,
            decimal quantity,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingUnitOfWork(List<string> events)
        : IUnitOfWork
    {
        public Task BeginTransactionAsync(
            CancellationToken ct = default)
        {
            events.Add("begin");
            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync(
            CancellationToken ct = default)
        {
            events.Add("commit");
            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(
            CancellationToken ct = default)
        {
            events.Add("rollback");
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(
            CancellationToken ct = default)
        {
            events.Add("save");
            return Task.FromResult(1);
        }
    }

    private sealed class InjectedAdjustmentFailureException
        : Exception;

    private sealed class
        InjectedRelationalAdjustmentFailureException
        : Exception;
}
