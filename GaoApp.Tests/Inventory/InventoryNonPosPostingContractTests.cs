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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Globalization;

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
        var result = AnalyzeAdjustmentReferenceAstContract(
            ReadAdjustmentServiceSource(),
            ReadInventoryMovementFactoryInterfaceSource());

        result.Violations.Should().BeEmpty();
        result.ServiceParseErrorCount.Should().Be(0);
        result.FactoryInterfaceParseErrorCount.Should().Be(0);
        result.ServiceClassCount.Should().Be(1);
        result.ApproveMethodCount.Should().Be(1);
        result.DocumentReferenceDeclarationCount.Should().Be(1);
        result.DocumentReferenceDeclarationIsDirectVar.Should().BeTrue();
        result.DocumentReferenceInitializerIsInvariant.Should().BeTrue();
        result.DocumentReferenceConversionCount.Should().Be(1);
        result.DocumentReferenceWriteCount.Should().Be(0);
        result.DocumentReferenceReadCount.Should().Be(2);
        result.IncreaseOverloadCount.Should().Be(1);
        result.DecreaseOverloadCount.Should().Be(1);
        result.IncreaseReferenceIndex.Should().Be(4);
        result.DecreaseReferenceIndex.Should().Be(4);
        result.IncreaseCallCount.Should().Be(1);
        result.DecreaseCallCount.Should().Be(1);
        result.IncreaseReferenceArgumentIsDocumentReference.Should().BeTrue();
        result.DecreaseReferenceArgumentIsDocumentReference.Should().BeTrue();
        result.ConstructionLoopCount.Should().Be(1);
        result.MovementRequestAddCount.Should().Be(1);
        result.PreLockCallCount.Should().Be(1);
        result.PostingLoopCount.Should().Be(1);
        result.PostingCallCount.Should().Be(1);
        result.TransactionTryCount.Should().Be(1);
        result.ConstructionLoopIsDirectStatement.Should().BeTrue();
        result.MovementRequestAddIsDirectStatement.Should().BeTrue();
        result.PreLockIsDirectStatement.Should().BeTrue();
        result.PreLockHasAwaitShape.Should().BeTrue();
        result.PostingLoopIsDirectStatement.Should().BeTrue();
        result.PostingCreateIsDirectStatement.Should().BeTrue();
        result.PostingCreateHasAwaitShape.Should().BeTrue();
        result.ProtectedOrderingIsValid.Should().BeTrue();
        result.ConstructionFlowIsValid.Should().BeTrue();
        result.PostingFlowIsValid.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AdjustmentReferenceContractMutants))]
    public void Adjustment_reference_contract_rejects_mutant(
        string mutant,
        string serviceSource,
        string factoryInterfaceSource,
        string[] expectedViolations)
    {
        var baseline = AnalyzeAdjustmentReferenceAstContract(
            ReadAdjustmentServiceSource(),
            ReadInventoryMovementFactoryInterfaceSource());
        var result = AnalyzeAdjustmentReferenceAstContract(
            serviceSource,
            factoryInterfaceSource);

        baseline.Violations.Should().BeEmpty(
            because: $"{mutant} must start from the valid contract baseline");
        result.Violations.Should().NotBeEmpty(
            because: $"{mutant} must violate the durable identity contract");
        foreach (var expectedViolation in expectedViolations)
        {
            result.Violations.Should().Contain(
                expectedViolation,
                because: $"{mutant} must be rejected for this violation");
        }
    }

    public static IEnumerable<object[]>
        AdjustmentReferenceContractMutants()
    {
        var serviceSource = ReadAdjustmentServiceSource();
        var factoryInterfaceSource =
            ReadInventoryMovementFactoryInterfaceSource();

        yield return
        [
            "M1 CurrentCulture reassignment",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "documentReferenceId = " +
                "document.Id.ToString(CultureInfo.CurrentCulture);"),
            factoryInterfaceSource,
            new[]
            {
                "DocumentReferenceConversionCount",
                "DocumentReferenceWrite"
            }
        ];
        yield return
        [
            "M2 parameterless reassignment",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "documentReferenceId = document.Id.ToString();"),
            factoryInterfaceSource,
            new[]
            {
                "DocumentReferenceConversionCount",
                "DocumentReferenceWrite"
            }
        ];
        yield return
        [
            "M3 interpolation reassignment",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "documentReferenceId = $\"{document.Id}\";"),
            factoryInterfaceSource,
            new[]
            {
                "DocumentReferenceWrite"
            }
        ];
        yield return
        [
            "M4 decrease direct conversion",
            ReplaceFactoryReferenceArgument(
                serviceSource,
                "CreateAdjustmentDecrease",
                "document.Id.ToString(CultureInfo.InvariantCulture)"),
            factoryInterfaceSource,
            new[]
            {
                "DocumentReferenceConversionCount",
                "DocumentReferenceUseCount",
                "DecreaseReferenceArgument",
            }
        ];
        yield return
        [
            "M5 separate decrease variable",
            ReplaceFactoryReferenceArgument(
                InsertAfterDocumentReferenceDeclaration(
                    serviceSource,
                    "var decreaseReferenceId = " +
                    "document.Id.ToString(CultureInfo.InvariantCulture);"),
                "CreateAdjustmentDecrease",
                "decreaseReferenceId"),
            factoryInterfaceSource,
            new[]
            {
                "DocumentReferenceConversionCount",
                "DocumentReferenceUseCount",
                "DecreaseReferenceArgument",
            }
        ];
        yield return
        [
            "M6 CurrentUICulture compound reassignment",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "documentReferenceId ??= " +
                "document.Id.ToString(CultureInfo.CurrentUICulture);"),
            factoryInterfaceSource,
            new[]
            {
                "DocumentReferenceConversionCount",
                "DocumentReferenceWrite"
            }
        ];
        yield return
        [
            "M7 legal comment-trivia reassignment",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "documentReferenceId /* legal C# trivia */ = " +
                "$\"{document.Id}\";"),
            factoryInterfaceSource,
            new[]
            {
                "DocumentReferenceWrite"
            }
        ];
        yield return
        [
            "M8 commented declaration bypass",
            ReplaceDocumentReferenceDeclaration(
                serviceSource,
                """
                BuildReference(document, out var documentReferenceId);

                // var documentReferenceId =
                //     document.Id.ToString(CultureInfo.InvariantCulture);
                """),
            factoryInterfaceSource,
            new[]
            {
                "DocumentReferenceDeclarationShape",
                "DocumentReferenceInitializer",
                "DocumentReferenceConversionCount",
                "DocumentReferenceWrite"
            }
        ];
        yield return
        [
            "M9 string-literal declaration bypass",
            ReplaceDocumentReferenceDeclaration(
                serviceSource,
                """
                BuildReference(document, out var documentReferenceId);

                var fakeContract =
                    "var documentReferenceId = " +
                    "document.Id.ToString(CultureInfo.InvariantCulture);";
                """),
            factoryInterfaceSource,
            new[]
            {
                "DocumentReferenceDeclarationShape",
                "DocumentReferenceInitializer",
                "DocumentReferenceConversionCount",
                "DocumentReferenceWrite"
            }
        ];
        yield return
        [
            "M10 commented correct decrease with executable bypass",
            ReplaceFactoryReferenceArgument(
                InsertBeforeFactoryAssignment(
                    serviceSource,
                    "CreateAdjustmentDecrease",
                    BuildCommentedCorrectDecreaseCall()),
                "CreateAdjustmentDecrease",
                    "document.Id.ToString(CultureInfo.InvariantCulture)"),
            factoryInterfaceSource,
            new[]
            {
                "DocumentReferenceConversionCount",
                "DocumentReferenceUseCount",
                "DecreaseReferenceArgument"
            }
        ];
        yield return
        [
            "M11 raw-string declaration bypass",
            ReplaceDocumentReferenceDeclaration(
                serviceSource,
                BuildRawStringDeclarationBypass(
                    dollarCount: 0)),
            factoryInterfaceSource,
            new[]
            {
                "DocumentReferenceDeclarationShape",
                "DocumentReferenceInitializer",
                "DocumentReferenceConversionCount",
                "DocumentReferenceWrite"
            }
        ];
        yield return
        [
            "M12 interpolated raw-string declaration bypass",
            ReplaceDocumentReferenceDeclaration(
                serviceSource,
                BuildRawStringDeclarationBypass(
                    dollarCount: 2)),
            factoryInterfaceSource,
            new[]
            {
                "DocumentReferenceDeclarationShape",
                "DocumentReferenceInitializer",
                "DocumentReferenceConversionCount",
                "DocumentReferenceWrite"
            }
        ];
        yield return
        [
            "M13 raw-string fake factory call",
            ReplaceFactoryReferenceArgument(
                InsertBeforeFactoryAssignment(
                    serviceSource,
                    "CreateAdjustmentDecrease",
                    BuildRawStringContainingCorrectDecreaseCall()),
                "CreateAdjustmentDecrease",
                    "document.Id.ToString(CultureInfo.InvariantCulture)"),
            factoryInterfaceSource,
            new[]
            {
                "DocumentReferenceConversionCount",
                "DocumentReferenceUseCount",
                "DecreaseReferenceArgument"
            }
        ];
        yield return
        [
            "M15 nested raw interpolation declaration bypass",
            ReplaceDocumentReferenceDeclaration(
                serviceSource,
                "BuildReference(document, out var documentReferenceId);\n\n"
                + BuildNestedInterpolatedRawContractStatement()),
            factoryInterfaceSource,
            new[]
            {
                "DocumentReferenceDeclarationShape",
                "DocumentReferenceInitializer",
                "DocumentReferenceConversionCount",
                "DocumentReferenceWrite"
            }
        ];
    }

    [Theory]
    [MemberData(nameof(AdjustmentTwoStageFlowMutants))]
    public void Adjustment_two_stage_contract_rejects_mutant(
        string mutant,
        string serviceSource,
        string[] expectedViolations)
    {
        var factoryInterfaceSource =
            ReadInventoryMovementFactoryInterfaceSource();
        var baseline = AnalyzeAdjustmentReferenceAstContract(
            ReadAdjustmentServiceSource(),
            factoryInterfaceSource);
        var result = AnalyzeAdjustmentReferenceAstContract(
            serviceSource,
            factoryInterfaceSource);

        baseline.Violations.Should().BeEmpty(
            because: $"{mutant} must start from the production baseline");
        result.ServiceParseErrorCount.Should().Be(
            0,
            because: $"{mutant} must remain valid C# 12 syntax");
        result.FactoryInterfaceParseErrorCount.Should().Be(0);
        result.Violations.Should().NotBeEmpty();
        foreach (var expectedViolation in expectedViolations)
        {
            result.Violations.Should().Contain(
                expectedViolation,
                because: $"{mutant} must fail for this exact contract rule");
        }
    }

    public static IEnumerable<object[]>
        AdjustmentTwoStageFlowMutants()
    {
        var source = ReadAdjustmentServiceSource();

        yield return
        [
            "M16 alias standalone roots with durable lambdas",
            BuildAliasStandaloneWithHiddenFactoryCalls(
                source,
                useLocalFunction: false),
            new[]
            {
                "IncreaseCallCount",
                "DecreaseCallCount",
                "FactoryCallNestedExecutableScope",
                "IncreaseFactoryReceiver",
                "DecreaseFactoryReceiver",
                "IncreaseFactoryArgumentCount",
                "DecreaseFactoryArgumentCount",
                "IncreaseFactoryFlowShape",
                "DecreaseFactoryFlowShape",
                "DocumentReferenceNestedUse",
                "DocumentReferenceUseCount"
            }
        ];
        yield return
        [
            "M17 alias standalone roots with durable local function",
            BuildAliasStandaloneWithHiddenFactoryCalls(
                source,
                useLocalFunction: true),
            new[]
            {
                "IncreaseCallCount",
                "DecreaseCallCount",
                "FactoryCallNestedExecutableScope",
                "IncreaseFactoryReceiver",
                "DecreaseFactoryReceiver",
                "IncreaseFactoryArgumentCount",
                "DecreaseFactoryArgumentCount",
                "IncreaseFactoryFlowShape",
                "DecreaseFactoryFlowShape"
            }
        ];
        yield return
        [
            "M18 exact receiver standalone root with durable lambda",
            BuildExactReceiverStandaloneWithHiddenDurableCall(
                source),
            new[]
            {
                "IncreaseCallCount",
                "FactoryCallNestedExecutableScope",
                "IncreaseFactoryArgumentCount",
                "IncreaseFactoryFlowShape",
                "DocumentReferenceNestedUse"
            }
        ];
        yield return
        [
            "M19 alias durable construction call",
            BuildAliasDurableFactoryCall(source),
            new[]
            {
                "IncreaseFactoryReceiver",
                "IncreaseFactoryFlowShape"
            }
        ];
        yield return
        [
            "M20 hidden document reference read",
            InsertAfterDocumentReferenceDeclaration(
                source,
                "Func<string> hiddenReferenceRead = "
                + "() => documentReferenceId;"),
            new[]
            {
                "DocumentReferenceNestedUse",
                "DocumentReferenceUseCount"
            }
        ];
        yield return
        [
            "M21 wrong request added",
            ReplaceMovementRequestAddArgument(
                source,
                "alternateMovementRequest"),
            new[]
            {
                "MovementRequestAddArgument",
                "MovementRequestProvenance"
            }
        ];
        yield return
        [
            "M22 wrong posting collection",
            ReplacePostingCollection(
                source,
                "alternateMovementRequests"),
            new[]
            {
                "PostingCollection",
                "MovementRequestsUse"
            }
        ];
        yield return
        [
            "M23 posting through movement-service alias",
            BuildPostingServiceAlias(source),
            new[]
            {
                "PostingReceiver"
            }
        ];
        yield return
        [
            "M24 Add hidden inside lambda",
            HideMovementRequestAddInLambda(source),
            new[]
            {
                "MovementRequestAddCount",
                "MovementRequestAddNestedScope",
                "MovementRequestAddFlow",
                "MovementRequestProvenance"
            }
        ];
        yield return
        [
            "M25 posting call gated by impossible condition",
            WrapPostingCreateStatement(
                source,
                "if (document.Id < 0)"),
            new[]
            {
                "PostingCreateDirectStatement"
            }
        ];
        yield return
        [
            "M26 Add gated by impossible condition",
            WrapMovementRequestAddStatement(
                source,
                "if (line.Id < 0)"),
            new[]
            {
                "MovementRequestAddDirectStatement",
                "MovementRequestAddFlow"
            }
        ];
        yield return
        [
            "M27 prelock gated by impossible condition",
            WrapPreLockStatement(
                source,
                "if (document.Id < 0)"),
            new[]
            {
                "PreLockDirectStatement"
            }
        ];
        yield return
        [
            "M28 dropped await from posting",
            DropAwaitFromPostingCreate(source),
            new[]
            {
                "PostingCreateAwaitShape"
            }
        ];
        yield return
        [
            "M29 dropped await from prelock",
            DropAwaitFromPreLock(source),
            new[]
            {
                "PreLockAwaitShape"
            }
        ];
        yield return
        [
            "M30 posting foreach gated",
            WrapPostingLoop(
                source,
                "if (document.Id < 0)"),
            new[]
            {
                "PostingLoopDirectStatement"
            }
        ];
        yield return
        [
            "M31 construction foreach gated",
            WrapConstructionLoop(
                source,
                "if (document.Id < 0)"),
            new[]
            {
                "ConstructionLoopDirectStatement"
            }
        ];
        yield return
        [
            "M32 posting call inside nested alternate loop",
            WrapPostingCreateStatement(
                source,
                "for (var attempt = 0; attempt < 1; attempt++)"),
            new[]
            {
                "PostingCreateDirectStatement"
            }
        ];
        yield return
        [
            "M33 prelock inside nested switch",
            WrapPreLockStatementInSwitch(source),
            new[]
            {
                "PreLockDirectStatement"
            }
        ];
    }

    [Fact]
    public void Adjustment_reference_scope_classifier_distinguishes_root_and_nested_executable_nodes()
    {
        var productionRoot = ParseRequiredCompilationUnit(
            ReadAdjustmentServiceSource());
        var approveMethod = productionRoot
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(x => x.Identifier.ValueText == "ApproveAsync");
        var rootForeachStatements = approveMethod
            .DescendantNodes()
            .OfType<ForEachStatementSyntax>()
            .Where(x => IsInRootApproveExecutableScope(
                x,
                approveMethod))
            .ToList();
        var constructionLoop = rootForeachStatements.Single(
            x => IsDocumentLinesExpression(x.Expression));
        var postingLoop = rootForeachStatements.Single(
            x => x.Identifier.ValueText == "movementRequest");
        var scopeRoot = ParseRequiredCompilationUnit(
            """
            class ScopeFixture
            {
                void ApproveAsync()
                {
                    Func<int> lambda = () => 1;
                    Func<int, int> simple = value => value + 1;
                    Func<int> anonymous = delegate { return 2; };
                    int Local()
                    {
                        Func<int> nested = () => 3;
                        return nested();
                    }
                }
            }
            """);
        var scopeMethod = scopeRoot
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();
        var lambda = scopeMethod
            .DescendantNodes()
            .OfType<ParenthesizedLambdaExpressionSyntax>()
            .First();
        var simpleLambda = scopeMethod
            .DescendantNodes()
            .OfType<SimpleLambdaExpressionSyntax>()
            .Single();
        var anonymousMethod = scopeMethod
            .DescendantNodes()
            .OfType<AnonymousMethodExpressionSyntax>()
            .Single();
        var localFunction = scopeMethod
            .DescendantNodes()
            .OfType<LocalFunctionStatementSyntax>()
            .Single();
        var nestedLambda = localFunction
            .DescendantNodes()
            .OfType<ParenthesizedLambdaExpressionSyntax>()
            .Single();

        IsInRootApproveExecutableScope(
                constructionLoop,
                approveMethod)
            .Should().BeTrue();
        IsInRootApproveExecutableScope(
                postingLoop,
                approveMethod)
            .Should().BeTrue();
        IsInRootApproveExecutableScope(
                lambda.ExpressionBody!,
                scopeMethod)
            .Should().BeFalse();
        IsInRootApproveExecutableScope(
                simpleLambda.ExpressionBody!,
                scopeMethod)
            .Should().BeFalse();
        IsInRootApproveExecutableScope(
                anonymousMethod.Block!,
                scopeMethod)
            .Should().BeFalse();
        IsInRootApproveExecutableScope(
                localFunction.Body!,
                scopeMethod)
            .Should().BeFalse();
        IsInRootApproveExecutableScope(
                nestedLambda.ExpressionBody!,
                scopeMethod)
            .Should().BeFalse();
    }

    [Fact]
    public void Adjustment_direct_statement_classifier_rejects_intermediate_control_flow()
    {
        var root = ParseRequiredCompilationUnit(
            """
            class DirectStatementFixture
            {
                async Task CheckAsync(bool condition)
                {
                    await DirectAwait();
                    DroppedAwait();
                    _ = AssignedAwait();
                    await (condition
                        ? ConditionalAwait()
                        : Task.CompletedTask);

                    if (condition)
                    {
                        await InIf();
                    }

                    switch (condition)
                    {
                        case true:
                            await InSwitch();
                            break;
                    }

                    for (var index = 0; index < 1; index++)
                    {
                        await InFor();
                    }

                    foreach (var item in items)
                    {
                        await InForeach();
                    }

                    try
                    {
                        await InTry();
                    }
                    finally
                    {
                    }

                    Func<Task> lambda = async () =>
                    {
                        await InLambda();
                    };

                    async Task LocalAsync()
                    {
                        await InLocal();
                    }
                }
            }
            """);
        var method = root
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(x => x.Identifier.ValueText == "CheckAsync");
        var expectedBlock = method.Body!;
        var invocations = method
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .ToDictionary(
                x => GetInvokedMethodName(x)!,
                StringComparer.Ordinal);
        ExpressionStatementSyntax GetStatement(string methodName)
            => invocations[methodName]
                .Ancestors()
                .OfType<ExpressionStatementSyntax>()
                .First();

        IsDirectStatementInBlock(
                GetStatement("DirectAwait"),
                expectedBlock)
            .Should().BeTrue();
        IsDirectStatementInBlock(
                GetStatement("InIf"),
                expectedBlock)
            .Should().BeFalse();
        IsDirectStatementInBlock(
                GetStatement("InSwitch"),
                expectedBlock)
            .Should().BeFalse();
        IsDirectStatementInBlock(
                GetStatement("InFor"),
                expectedBlock)
            .Should().BeFalse();
        IsDirectStatementInBlock(
                GetStatement("InForeach"),
                expectedBlock)
            .Should().BeFalse();
        IsDirectStatementInBlock(
                GetStatement("InTry"),
                expectedBlock)
            .Should().BeFalse();
        IsDirectStatementInBlock(
                GetStatement("InLambda"),
                expectedBlock)
            .Should().BeFalse();
        IsDirectStatementInBlock(
                GetStatement("InLocal"),
                expectedBlock)
            .Should().BeFalse();
        HasAwaitExpressionStatementShape(
                invocations["DirectAwait"])
            .Should().BeTrue();
        HasAwaitExpressionStatementShape(
                invocations["DroppedAwait"])
            .Should().BeFalse();
        HasAwaitExpressionStatementShape(
                invocations["AssignedAwait"])
            .Should().BeFalse();
        HasAwaitExpressionStatementShape(
                invocations["ConditionalAwait"])
            .Should().BeFalse();
    }

    [Fact]
    public void Adjustment_direct_statement_contract_allows_unrelated_control_flow()
    {
        var result = AnalyzeAdjustmentReferenceAstContract(
            InsertAfterDocumentReferenceDeclaration(
                ReadAdjustmentServiceSource(),
                """
                if (request.Id == int.MinValue)
                {
                    _ = request.ApprovalNote;
                }
                """),
            ReadInventoryMovementFactoryInterfaceSource());

        result.Violations.Should().BeEmpty();
        result.PreLockIsDirectStatement.Should().BeTrue();
        result.PreLockHasAwaitShape.Should().BeTrue();
        result.PostingLoopIsDirectStatement.Should().BeTrue();
        result.PostingCreateIsDirectStatement.Should().BeTrue();
        result.PostingCreateHasAwaitShape.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AdjustmentReferenceWriteMutants))]
    public void Adjustment_reference_contract_rejects_every_later_write_shape(
        string writeShape,
        string serviceSource,
        string[] expectedViolations)
    {
        var factoryInterfaceSource =
            ReadInventoryMovementFactoryInterfaceSource();
        var baseline = AnalyzeAdjustmentReferenceAstContract(
            ReadAdjustmentServiceSource(),
            factoryInterfaceSource);
        var result = AnalyzeAdjustmentReferenceAstContract(
            serviceSource,
            factoryInterfaceSource);

        baseline.Violations.Should().BeEmpty();
        result.Violations.Should().NotBeEmpty(
            because: $"{writeShape} must fail closed");
        foreach (var expectedViolation in expectedViolations)
        {
            result.Violations.Should().Contain(expectedViolation);
        }
    }

    public static IEnumerable<object[]>
        AdjustmentReferenceWriteMutants()
    {
        var source = ReadAdjustmentServiceSource();

        yield return
        [
            "prefix increment",
            InsertAfterDocumentReferenceDeclaration(
                source,
                "++documentReferenceId;"),
            new[] { "DocumentReferenceWrite" }
        ];
        yield return
        [
            "postfix decrement",
            InsertAfterDocumentReferenceDeclaration(
                source,
                "documentReferenceId--;"),
            new[] { "DocumentReferenceWrite" }
        ];
        yield return
        [
            "ref argument",
            InsertAfterDocumentReferenceDeclaration(
                source,
                "BuildReference(ref documentReferenceId);"),
            new[] { "DocumentReferenceWrite" }
        ];
        yield return
        [
            "out argument",
            InsertAfterDocumentReferenceDeclaration(
                source,
                "BuildReference(document, out documentReferenceId);"),
            new[] { "DocumentReferenceWrite" }
        ];
        yield return
        [
            "duplicate local declaration",
            InsertAfterDocumentReferenceDeclaration(
                source,
                "var documentReferenceId = alternateReferenceId;"),
            new[]
            {
                "DocumentReferenceDeclarationCount",
                "DocumentReferenceDeclarationShape"
            }
        ];
        yield return
        [
            "pattern declaration",
            InsertAfterDocumentReferenceDeclaration(
                source,
                "if (document is { Id: var documentReferenceId }) { }"),
            new[]
            {
                "DocumentReferenceDeclarationCount",
                "DocumentReferenceDeclarationShape",
                "DocumentReferenceWrite"
            }
        ];
        yield return
        [
            "CurrentCulture in nested function",
            InsertAfterDocumentReferenceDeclaration(
                source,
                "Func<CultureInfo> hiddenCulture = "
                + "() => CultureInfo.CurrentCulture;"),
            new[] { "CurrentCulture" }
        ];
        yield return
        [
            "CurrentUICulture in local function",
            InsertAfterDocumentReferenceDeclaration(
                source,
                "CultureInfo HiddenUiCulture() "
                + "{ return CultureInfo.CurrentUICulture; }"),
            new[] { "CurrentUICulture" }
        ];
    }

    [Fact]
    public void Adjustment_reference_contract_M14_ignores_raw_fake_method_boundaries()
    {
        var serviceSource = ReadAdjustmentServiceSource();
        var factoryInterfaceSource =
            ReadInventoryMovementFactoryInterfaceSource();
        var baseline = AnalyzeAdjustmentReferenceAstContract(
            serviceSource,
            factoryInterfaceSource);
        var mutated = AnalyzeAdjustmentReferenceAstContract(
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                BuildRawFakeMethodBoundariesStatement()),
            factoryInterfaceSource);

        baseline.Violations.Should().BeEmpty();
        mutated.Violations.Should().BeEmpty(
            "M14 method-like raw-string content is not executable syntax");
    }

    [Theory]
    [MemberData(nameof(AdjustmentReferenceContractTriviaControls))]
    public void Adjustment_reference_contract_ignores_harmless_non_code_text(
        string control,
        string serviceSource)
    {
        var factoryInterfaceSource =
            ReadInventoryMovementFactoryInterfaceSource();
        var baseline = AnalyzeAdjustmentReferenceAstContract(
            ReadAdjustmentServiceSource(),
            factoryInterfaceSource);
        var result = AnalyzeAdjustmentReferenceAstContract(
            serviceSource,
            factoryInterfaceSource);

        baseline.Violations.Should().BeEmpty();
        result.Violations.Should().BeEmpty(
                because: $"{control} is non-code trivia or literal content");
    }

    public static IEnumerable<object[]>
        AdjustmentReferenceContractTriviaControls()
    {
        var serviceSource = ReadAdjustmentServiceSource();

        yield return
        [
            "CurrentCulture in comment",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "// CultureInfo.CurrentCulture")
        ];
        yield return
        [
            "parameterless conversion in string",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "var harmlessText = \"document.Id.ToString()\";")
        ];
        yield return
        [
            "assignment in comment",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "// documentReferenceId = alternateReferenceId;")
        ];
        yield return
        [
            "block comment",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "/* documentReferenceId = document.Id.ToString(); */")
        ];
        yield return
        [
            "verbatim string",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "var harmlessVerbatim = @\"documentReferenceId = "
                + "document.Id.ToString(CultureInfo.CurrentCulture);\";")
        ];
        yield return
        [
            "regular interpolated string",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "var harmlessInterpolated = "
                + "$\"documentReferenceId = document.Id.ToString()\";")
        ];
        yield return
        [
            "dollar-at interpolated verbatim string",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "var harmlessDollarAt = "
                + "$@\"documentReferenceId = document.Id.ToString()\";")
        ];
        yield return
        [
            "at-dollar interpolated verbatim string",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "var harmlessAtDollar = "
                + "@$\"documentReferenceId = document.Id.ToString()\";")
        ];
        yield return
        [
            "plain raw string",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                BuildHarmlessRawStringStatement(
                    dollarCount: 0,
                    quoteCount: 3))
        ];
        yield return
        [
            "one-dollar interpolated raw string",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                BuildHarmlessRawStringStatement(
                    dollarCount: 1,
                    quoteCount: 3))
        ];
        yield return
        [
            "two-dollar interpolated raw string",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                BuildHarmlessRawStringStatement(
                    dollarCount: 2,
                    quoteCount: 3))
        ];
        yield return
        [
            "four-quote raw string containing triple quotes",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                    BuildHarmlessRawStringStatement(
                        dollarCount: 0,
                        quoteCount: 4,
                        additionalContent:
                            "short quote run: \"\"\""))
        ];
        yield return
        [
            "multiline CRLF raw string",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                    BuildHarmlessRawStringStatement(
                        dollarCount: 0,
                        quoteCount: 3,
                        lineEnding: "\r\n"))
        ];
        yield return
        [
            "nested interpolated raw string",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                BuildNestedInterpolatedRawContractStatement())
        ];
        yield return
        [
            "harmless lambda",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "Func<int> harmlessLambda = () => 1 + 2;")
        ];
        yield return
        [
            "harmless anonymous method",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "Func<int> harmlessAnonymous = "
                + "delegate { return 3; };")
        ];
        yield return
        [
            "harmless local function",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "int HarmlessLocal() { return 4; }")
        ];
        yield return
        [
            "harmless nested lambda inside local function",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "int HarmlessNested() { "
                + "Func<int> nested = () => 5; "
                + "return nested(); }")
        ];
        yield return
        [
            "quote and backslash character literals",
            InsertAfterDocumentReferenceDeclaration(
                serviceSource,
                "var harmlessQuote = '\\'';\n"
                + "var harmlessSlash = '\\\\';")
        ];
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Adjustment_reference_contract_fails_closed_on_parse_errors(
        bool malformedService)
    {
        var serviceSource = ReadAdjustmentServiceSource();
        var factoryInterfaceSource =
            ReadInventoryMovementFactoryInterfaceSource();
        var result = AnalyzeAdjustmentReferenceAstContract(
            malformedService
                ? serviceSource[..serviceSource.LastIndexOf(
                    '}')]
                : serviceSource,
            malformedService
                ? factoryInterfaceSource
                : factoryInterfaceSource[..factoryInterfaceSource.LastIndexOf(
                    '}')]);

        result.Violations.Should().Contain(
            malformedService
                ? "ServiceParseError"
                : "FactoryInterfaceParseError");
        if (malformedService)
        {
            result.ServiceParseErrorCount.Should().BeGreaterThan(0);
        }
        else
        {
            result.FactoryInterfaceParseErrorCount.Should().BeGreaterThan(0);
        }
    }

    [Theory]
    [MemberData(nameof(AdjustmentReferenceInterfaceDrifts))]
    public void Adjustment_reference_contract_fails_closed_on_interface_drift(
        string drift,
        string factoryInterfaceSource,
        string[] expectedViolations)
    {
        var serviceSource = ReadAdjustmentServiceSource();
        var baseline = AnalyzeAdjustmentReferenceAstContract(
            serviceSource,
            ReadInventoryMovementFactoryInterfaceSource());
        var result = AnalyzeAdjustmentReferenceAstContract(
            serviceSource,
            factoryInterfaceSource);

        baseline.Violations.Should().BeEmpty(
            because: $"{drift} must start from the valid interface baseline");
        result.Violations.Should().NotBeEmpty(
            because: $"{drift} must fail closed");
        foreach (var expectedViolation in expectedViolations)
        {
            result.Violations.Should().Contain(expectedViolation);
        }
    }

    public static IEnumerable<object[]>
        AdjustmentReferenceInterfaceDrifts()
    {
        var source = ReadInventoryMovementFactoryInterfaceSource();

        yield return
        [
            "missing durable overload",
            RemoveDurableFactoryMethod(
                source,
                "CreateAdjustmentIncrease"),
            new[] { "IncreaseOverloadCount" }
        ];
        yield return
        [
            "duplicate durable overload",
            DuplicateDurableFactoryMethod(
                source,
                "CreateAdjustmentIncrease"),
            new[] { "IncreaseOverloadCount" }
        ];
        yield return
        [
            "missing documentId parameter",
            RenameDurableFactoryParameter(
                source,
                "CreateAdjustmentIncrease",
                "documentId",
                "referenceId"),
            new[] { "IncreaseOverloadCount" }
        ];
        yield return
        [
            "changed documentId argument position",
            MoveDurableFactoryParameter(
                MoveDurableFactoryParameter(
                    source,
                    "CreateAdjustmentIncrease",
                    "documentId",
                    3),
                "CreateAdjustmentDecrease",
                "documentId",
                3),
            new[] { "ReferenceParameterIndex" }
        ];
        yield return
        [
            "wrong parameter count",
            RemoveDurableFactoryParameter(
                source,
                "CreateAdjustmentIncrease",
                "occurredAtUtc"),
            new[] { "IncreaseOverloadCount" }
        ];
        yield return
        [
            "increase decrease index mismatch",
            MoveDurableFactoryParameter(
                source,
                "CreateAdjustmentDecrease",
                "documentId",
                3),
            new[] { "ReferenceParameterIndex" }
        ];
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

    private static string ReadAdjustmentServiceSource()
        => ReadRepositoryFile(
            "GaoApp.Application/Services/Inventory/InventoryAdjustmentDocumentService.cs");

    private static string ReadInventoryMovementFactoryInterfaceSource()
        => ReadRepositoryFile(
            "GaoApp.Application/Interfaces/Services/Inventory/IInventoryMovementFactory.cs");

    private static AdjustmentReferenceAstContractResult
        AnalyzeAdjustmentReferenceAstContract(
            string serviceSource,
            string factoryInterfaceSource)
    {
        var parseOptions =
            new CSharpParseOptions(LanguageVersion.CSharp12);
        var serviceTree = CSharpSyntaxTree.ParseText(
            serviceSource,
            parseOptions);
        var factoryInterfaceTree = CSharpSyntaxTree.ParseText(
            factoryInterfaceSource,
            parseOptions);
        var serviceParseErrorCount = serviceTree
            .GetDiagnostics()
            .Count(x => x.Severity == DiagnosticSeverity.Error);
        var factoryInterfaceParseErrorCount = factoryInterfaceTree
            .GetDiagnostics()
            .Count(x => x.Severity == DiagnosticSeverity.Error);
        var violations = new List<string>();

        if (serviceParseErrorCount != 0)
        {
            AddViolation(violations, "ServiceParseError");
        }

        if (factoryInterfaceParseErrorCount != 0)
        {
            AddViolation(violations, "FactoryInterfaceParseError");
        }

        var serviceRoot = serviceTree.GetCompilationUnitRoot();
        var serviceClasses = serviceRoot
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(x => x.Identifier.ValueText
                == "InventoryAdjustmentDocumentService")
            .ToList();
        if (serviceClasses.Count != 1)
        {
            AddViolation(violations, "ServiceClassCount");
        }

        var approveMethods = serviceClasses.Count == 1
            ? serviceClasses[0]
                .Members
                .OfType<MethodDeclarationSyntax>()
                .Where(x => x.Identifier.ValueText == "ApproveAsync")
                .ToList()
            : [];
        if (approveMethods.Count != 1)
        {
            AddViolation(violations, "ApproveMethodCount");
        }

        var factoryInterfaceRoot =
            factoryInterfaceTree.GetCompilationUnitRoot();
        var factoryInterfaces = factoryInterfaceRoot
            .DescendantNodes()
            .OfType<InterfaceDeclarationSyntax>()
            .Where(x => x.Identifier.ValueText
                == "IInventoryMovementFactory")
            .ToList();
        if (factoryInterfaces.Count != 1)
        {
            AddViolation(violations, "FactoryInterfaceCount");
        }

        var factoryInterface = factoryInterfaces.Count == 1
            ? factoryInterfaces[0]
            : null;
        var increaseOverloads = FindDurableFactoryMethods(
            factoryInterface,
            "CreateAdjustmentIncrease");
        var decreaseOverloads = FindDurableFactoryMethods(
            factoryInterface,
            "CreateAdjustmentDecrease");
        if (increaseOverloads.Count != 1)
        {
            AddViolation(violations, "IncreaseOverloadCount");
        }

        if (decreaseOverloads.Count != 1)
        {
            AddViolation(violations, "DecreaseOverloadCount");
        }

        var increaseReferenceIndex = increaseOverloads.Count == 1
            ? GetParameterIndex(
                increaseOverloads[0],
                "documentId")
            : null;
        var decreaseReferenceIndex = decreaseOverloads.Count == 1
            ? GetParameterIndex(
                decreaseOverloads[0],
                "documentId")
            : null;
        if (increaseReferenceIndex is null
            || decreaseReferenceIndex is null
            || increaseReferenceIndex != decreaseReferenceIndex
            || increaseReferenceIndex != 4)
        {
            AddViolation(violations, "ReferenceParameterIndex");
        }

        var documentReferenceDeclarationCount = 0;
        var documentReferenceConversionCount = 0;
        var documentReferenceWriteCount = 0;
        var documentReferenceReadCount = 0;
        var documentReferenceDeclarationIsDirectVar = false;
        var documentReferenceInitializerIsInvariant = false;
        var increaseReferenceArgumentIsDocumentReference = false;
        var decreaseReferenceArgumentIsDocumentReference = false;
        var increaseCalls = new List<InvocationExpressionSyntax>();
        var decreaseCalls = new List<InvocationExpressionSyntax>();
        var twoStageFlow =
            TwoStageFlowContractResult.Empty;

        if (approveMethods.Count == 1
            && approveMethods[0].Body is { } approveBody)
        {
            var approveMethod = approveMethods[0];
            var declarators = approveMethod
                .DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Where(x => x.Identifier.ValueText
                    == "documentReferenceId")
                .ToList();
            var designations = approveMethod
                .DescendantNodes()
                .OfType<SingleVariableDesignationSyntax>()
                .Where(x => x.Identifier.ValueText
                    == "documentReferenceId")
                .ToList();
            documentReferenceDeclarationCount =
                declarators.Count + designations.Count;
            documentReferenceWriteCount = designations.Count;
            if (documentReferenceDeclarationCount != 1)
            {
                AddViolation(
                    violations,
                    "DocumentReferenceDeclarationCount");
            }

            LocalDeclarationStatementSyntax? referenceStatement = null;
            InvocationExpressionSyntax? approvedInitializer = null;
            if (declarators.Count == 1
                && designations.Count == 0
                && declarators[0].Parent
                    is VariableDeclarationSyntax declaration
                && declaration.Type
                    is IdentifierNameSyntax
                    {
                        Identifier.ValueText: "var"
                    }
                && declaration.Variables.Count == 1
                && declaration.Parent
                    is LocalDeclarationStatementSyntax localStatement
                && localStatement.Parent == approveBody)
            {
                referenceStatement = localStatement;
                documentReferenceDeclarationIsDirectVar = true;
                approvedInitializer =
                    declarators[0].Initializer?.Value
                    as InvocationExpressionSyntax;
            }
            else
            {
                AddViolation(
                    violations,
                    "DocumentReferenceDeclarationShape");
            }

            documentReferenceInitializerIsInvariant =
                approvedInitializer is not null
                && IsDocumentIdToStringInvocation(
                    approvedInitializer)
                && approvedInitializer.ArgumentList.Arguments.Count == 1
                && approvedInitializer.ArgumentList.Arguments[0]
                    .NameColon is null
                && IsInvariantCultureExpression(
                    approvedInitializer.ArgumentList.Arguments[0]
                        .Expression);
            if (!documentReferenceInitializerIsInvariant)
            {
                AddViolation(
                    violations,
                    "DocumentReferenceInitializer");
            }

            var documentIdConversions = approveMethod
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(IsDocumentIdToStringInvocation)
                .ToList();
            documentReferenceConversionCount =
                documentIdConversions.Count;
            if (documentIdConversions.Count != 1
                || approvedInitializer is null
                || documentIdConversions[0].Span
                    != approvedInitializer.Span)
            {
                AddViolation(
                    violations,
                    "DocumentReferenceConversionCount");
            }

            if (approveMethod
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Any(x => IsCultureInfoMember(
                    x,
                    "CurrentCulture")))
            {
                AddViolation(
                    violations,
                    "CurrentCulture");
            }

            if (approveMethod
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Any(x => IsCultureInfoMember(
                    x,
                    "CurrentUICulture")))
            {
                AddViolation(
                    violations,
                    "CurrentUICulture");
            }

            var referenceIdentifiers = approveMethod
                .DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(x => x.Identifier.ValueText
                    == "documentReferenceId")
                .ToList();
            var writeIdentifiers = referenceIdentifiers
                .Where(IsWriteIdentifier)
                .ToList();
            documentReferenceWriteCount +=
                writeIdentifiers.Count;
            if (documentReferenceWriteCount != 0)
            {
                AddViolation(
                    violations,
                    "DocumentReferenceWrite");
            }

            var readIdentifiers = referenceIdentifiers
                .Except(writeIdentifiers)
                .ToList();
            documentReferenceReadCount =
                readIdentifiers.Count;
            var nestedReadIdentifiers = readIdentifiers
                .Where(x => !IsInRootApproveExecutableScope(
                    x,
                    approveMethod))
                .ToList();
            if (nestedReadIdentifiers.Count != 0)
            {
                AddViolation(
                    violations,
                    "DocumentReferenceNestedUse");
                AddViolation(
                    violations,
                    "DocumentReferenceUseCount");
            }

            increaseCalls = FindFactoryInvocations(
                approveMethod,
                "CreateAdjustmentIncrease");
            decreaseCalls = FindFactoryInvocations(
                approveMethod,
                "CreateAdjustmentDecrease");
            if (increaseCalls.Count != 1)
            {
                AddViolation(violations, "IncreaseCallCount");
            }

            if (decreaseCalls.Count != 1)
            {
                AddViolation(violations, "DecreaseCallCount");
            }

            var validatedReferenceArguments =
                new List<IdentifierNameSyntax>();
            increaseReferenceArgumentIsDocumentReference =
                ValidateFactoryInvocation(
                violations,
                increaseCalls,
                increaseOverloads,
                increaseReferenceIndex,
                approveMethod,
                "Increase",
                "IncreaseReferenceArgument",
                validatedReferenceArguments);
            decreaseReferenceArgumentIsDocumentReference =
                ValidateFactoryInvocation(
                violations,
                decreaseCalls,
                decreaseOverloads,
                decreaseReferenceIndex,
                approveMethod,
                "Decrease",
                "DecreaseReferenceArgument",
                validatedReferenceArguments);

            var readSpans = readIdentifiers
                .Where(x => IsInRootApproveExecutableScope(
                    x,
                    approveMethod))
                .Select(x => x.Span)
                .OrderBy(x => x.Start)
                .ToList();
            var validatedSpans = validatedReferenceArguments
                .Select(x => x.Span)
                .OrderBy(x => x.Start)
                .ToList();
            if (referenceStatement is null
                || readSpans.Count != 2
                || validatedSpans.Count != 2
                || !readSpans.SequenceEqual(validatedSpans))
            {
                AddViolation(
                    violations,
                    "DocumentReferenceUseCount");
            }

            twoStageFlow = ValidateTwoStagePostingFlow(
                approveMethod,
                increaseCalls,
                decreaseCalls,
                violations);
        }
        else
        {
            AddViolation(
                violations,
                "DocumentReferenceDeclarationCount");
            AddViolation(
                violations,
                "DocumentReferenceDeclarationShape");
            AddViolation(
                violations,
                "DocumentReferenceInitializer");
            AddViolation(
                violations,
                "DocumentReferenceConversionCount");
            AddViolation(
                violations,
                "DocumentReferenceUseCount");
            AddViolation(violations, "IncreaseCallCount");
            AddViolation(violations, "DecreaseCallCount");
        }

        return new AdjustmentReferenceAstContractResult(
            violations,
            serviceParseErrorCount,
            factoryInterfaceParseErrorCount,
            serviceClasses.Count,
            approveMethods.Count,
            documentReferenceDeclarationCount,
            documentReferenceDeclarationIsDirectVar,
            documentReferenceInitializerIsInvariant,
            documentReferenceConversionCount,
            documentReferenceWriteCount,
            documentReferenceReadCount,
            increaseOverloads.Count,
            decreaseOverloads.Count,
            increaseReferenceIndex,
            decreaseReferenceIndex,
            increaseCalls.Count,
            decreaseCalls.Count,
            increaseReferenceArgumentIsDocumentReference,
            decreaseReferenceArgumentIsDocumentReference,
            twoStageFlow.ConstructionLoopCount,
            twoStageFlow.MovementRequestAddCount,
            twoStageFlow.PreLockCallCount,
            twoStageFlow.PostingLoopCount,
            twoStageFlow.PostingCallCount,
            twoStageFlow.TransactionTryCount,
            twoStageFlow.ConstructionLoopIsDirectStatement,
            twoStageFlow.MovementRequestAddIsDirectStatement,
            twoStageFlow.PreLockIsDirectStatement,
            twoStageFlow.PreLockHasAwaitShape,
            twoStageFlow.PostingLoopIsDirectStatement,
            twoStageFlow.PostingCreateIsDirectStatement,
            twoStageFlow.PostingCreateHasAwaitShape,
            twoStageFlow.ProtectedOrderingIsValid,
            twoStageFlow.ConstructionFlowIsValid,
            twoStageFlow.PostingFlowIsValid);
    }

    private static IReadOnlyList<MethodDeclarationSyntax>
        FindDurableFactoryMethods(
            InterfaceDeclarationSyntax? factoryInterface,
            string methodName)
        => factoryInterface?
            .Members
            .OfType<MethodDeclarationSyntax>()
            .Where(x => x.Identifier.ValueText == methodName)
            .Where(x => x.ParameterList.Parameters.Count == 8)
            .Where(x => GetParameterIndex(x, "documentId") is not null)
            .Where(x => GetParameterIndex(x, "lineId") is not null)
            .ToList()
            ?? [];

    private static int? GetParameterIndex(
        MethodDeclarationSyntax method,
        string parameterName)
    {
        for (var index = 0;
             index < method.ParameterList.Parameters.Count;
             index++)
        {
            if (method.ParameterList.Parameters[index]
                    .Identifier.ValueText == parameterName)
            {
                return index;
            }
        }

        return null;
    }

    private static bool IsDocumentIdToStringInvocation(
        InvocationExpressionSyntax invocation)
        => invocation.Expression
            is MemberAccessExpressionSyntax
            {
                Expression:
                    MemberAccessExpressionSyntax
                    {
                        Expression:
                            IdentifierNameSyntax
                            {
                                Identifier.ValueText: "document"
                            },
                        Name:
                            IdentifierNameSyntax
                            {
                                Identifier.ValueText: "Id"
                            }
                    },
                Name:
                    IdentifierNameSyntax
                    {
                        Identifier.ValueText: "ToString"
                    }
            };

    private static bool IsInvariantCultureExpression(
        ExpressionSyntax expression)
        => expression
            is MemberAccessExpressionSyntax
            {
                Expression:
                    IdentifierNameSyntax
                    {
                        Identifier.ValueText: "CultureInfo"
                    },
                Name:
                    IdentifierNameSyntax
                    {
                        Identifier.ValueText: "InvariantCulture"
                    }
            };

    private static bool IsCultureInfoMember(
        MemberAccessExpressionSyntax expression,
        string memberName)
        => expression.Expression
            is IdentifierNameSyntax
            {
                Identifier.ValueText: "CultureInfo"
            }
            && expression.Name.Identifier.ValueText == memberName;

    private static bool IsWriteIdentifier(
        IdentifierNameSyntax identifier)
    {
        foreach (var ancestor in identifier.Ancestors())
        {
            if (ancestor is AssignmentExpressionSyntax assignment
                && assignment.Left.Span.Contains(identifier.Span))
            {
                return true;
            }

            if (ancestor is PrefixUnaryExpressionSyntax prefix
                && (prefix.IsKind(
                        SyntaxKind.PreIncrementExpression)
                    || prefix.IsKind(
                        SyntaxKind.PreDecrementExpression))
                && prefix.Operand.Span.Contains(identifier.Span))
            {
                return true;
            }

            if (ancestor is PostfixUnaryExpressionSyntax postfix
                && (postfix.IsKind(
                        SyntaxKind.PostIncrementExpression)
                    || postfix.IsKind(
                        SyntaxKind.PostDecrementExpression))
                && postfix.Operand.Span.Contains(identifier.Span))
            {
                return true;
            }

            if (ancestor is ArgumentSyntax argument
                && (argument.RefKindKeyword.IsKind(
                        SyntaxKind.RefKeyword)
                    || argument.RefKindKeyword.IsKind(
                        SyntaxKind.OutKeyword))
                && argument.Expression.Span.Contains(identifier.Span))
            {
                return true;
            }
        }

        return false;
    }

    private static List<InvocationExpressionSyntax>
        FindFactoryInvocations(
            MethodDeclarationSyntax approveMethod,
            string methodName)
        => approveMethod
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(x => GetInvokedMethodName(x) == methodName)
            .ToList();

    private static bool ValidateFactoryInvocation(
        List<string> violations,
        IReadOnlyList<InvocationExpressionSyntax> calls,
        IReadOnlyList<MethodDeclarationSyntax> overloads,
        int? referenceIndex,
        MethodDeclarationSyntax approveMethod,
        string callPrefix,
        string violation,
        ICollection<IdentifierNameSyntax> validatedReferenceArguments)
    {
        var nestedCalls = calls
            .Where(x => !IsInRootApproveExecutableScope(
                x,
                approveMethod))
            .ToList();
        if (nestedCalls.Count != 0)
        {
            AddViolation(
                violations,
                "FactoryCallNestedExecutableScope");
        }

        if (calls.Any(x => !HasExactReceiver(
                x,
                "_inventoryMovementFactory")))
        {
            AddViolation(
                violations,
                $"{callPrefix}FactoryReceiver");
        }

        var expectedArgumentCount =
            overloads.Count == 1
                ? overloads[0].ParameterList.Parameters.Count
                : 8;
        if (calls.Any(x =>
                x.ArgumentList.Arguments.Count
                    != expectedArgumentCount
                || x.ArgumentList.Arguments.Any(
                    argument => argument.NameColon is not null)))
        {
            AddViolation(
                violations,
                $"{callPrefix}FactoryArgumentCount");
        }

        var rootCalls = calls
            .Where(x => IsInRootApproveExecutableScope(
                x,
                approveMethod))
            .ToList();
        if (calls.Count != 1
            || rootCalls.Count != 1
            || overloads.Count != 1
            || referenceIndex is null)
        {
            AddViolation(violations, violation);
            AddViolation(
                violations,
                $"{callPrefix}FactoryFlowShape");
            return false;
        }

        var call = rootCalls[0];
        var arguments = call.ArgumentList.Arguments;
        var assignment = call.Parent
            as AssignmentExpressionSyntax;
        var hasExpectedReceiver =
            HasExactReceiver(
                call,
                "_inventoryMovementFactory");
        var hasExpectedAssignmentShape =
            assignment is not null
            && assignment.IsKind(
                SyntaxKind.SimpleAssignmentExpression)
            && assignment.Right == call
            && assignment.Left
                is IdentifierNameSyntax
                {
                    Identifier.ValueText: "movementRequest"
                }
            && assignment.Parent is ExpressionStatementSyntax;
        var hasExpectedArgumentShape =
            arguments.Count == expectedArgumentCount
            && arguments.All(x => x.NameColon is null)
            && referenceIndex.Value < arguments.Count
            && arguments[referenceIndex.Value].Expression
                is IdentifierNameSyntax
                {
                    Identifier.ValueText: "documentReferenceId"
                };

        if (!hasExpectedReceiver
            || !hasExpectedAssignmentShape
            || !hasExpectedArgumentShape)
        {
            AddViolation(violations, violation);
            AddViolation(
                violations,
                $"{callPrefix}FactoryFlowShape");
            return false;
        }

        validatedReferenceArguments.Add(
            (IdentifierNameSyntax)
            arguments[referenceIndex.Value].Expression);
        return true;
    }

    private static string? GetInvokedMethodName(
        InvocationExpressionSyntax invocation)
        => invocation.Expression switch
        {
            IdentifierNameSyntax identifier =>
                identifier.Identifier.ValueText,
            GenericNameSyntax generic =>
                generic.Identifier.ValueText,
            MemberAccessExpressionSyntax member =>
                member.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding =>
                binding.Name.Identifier.ValueText,
            _ => null
        };

    private static bool HasExactReceiver(
        InvocationExpressionSyntax invocation,
        string receiverName)
        => invocation.Expression
            is MemberAccessExpressionSyntax
            {
                Expression:
                    IdentifierNameSyntax receiver
            }
            && receiver.Identifier.ValueText == receiverName;

    private static bool IsInRootApproveExecutableScope(
        SyntaxNode node,
        MethodDeclarationSyntax approveMethod)
    {
        for (SyntaxNode? current = node;
             current is not null && current != approveMethod;
             current = current.Parent)
        {
            if (current is AnonymousFunctionExpressionSyntax
                or LocalFunctionStatementSyntax)
            {
                return false;
            }
        }

        return node.AncestorsAndSelf().Contains(approveMethod);
    }

    private static bool IsDirectStatementInBlock(
        StatementSyntax statement,
        BlockSyntax expectedBlock)
        => statement.Parent == expectedBlock;

    private static ExpressionStatementSyntax?
        GetContainingExpressionStatement(
            InvocationExpressionSyntax? invocation)
        => invocation?
            .Ancestors()
            .OfType<ExpressionStatementSyntax>()
            .FirstOrDefault();

    private static bool HasAwaitExpressionStatementShape(
        InvocationExpressionSyntax? invocation)
        => invocation?.Parent
            is AwaitExpressionSyntax awaitExpression
            && awaitExpression.Expression == invocation
            && awaitExpression.Parent
                is ExpressionStatementSyntax statement
            && statement.Expression == awaitExpression;

    private static TwoStageFlowContractResult
        ValidateTwoStagePostingFlow(
            MethodDeclarationSyntax approveMethod,
            IReadOnlyList<InvocationExpressionSyntax> increaseCalls,
            IReadOnlyList<InvocationExpressionSyntax> decreaseCalls,
            List<string> violations)
    {
        var approveBody = approveMethod.Body
            ?? throw new InvalidOperationException(
                "ApproveAsync must have a block body.");
        var rootForeachStatements = approveMethod
            .DescendantNodes()
            .OfType<ForEachStatementSyntax>()
            .Where(x => IsInRootApproveExecutableScope(
                x,
                approveMethod))
            .ToList();
        var constructionLoops = rootForeachStatements
            .Where(x => x.Identifier.ValueText == "line")
            .Where(x => IsDocumentLinesExpression(x.Expression))
            .ToList();
        if (constructionLoops.Count != 1)
        {
            AddViolation(violations, "ConstructionLoopCount");
        }

        var constructionLoop = constructionLoops.Count == 1
            ? constructionLoops[0]
            : null;
        var constructionLoopIsDirectStatement =
            constructionLoop is not null
            && IsDirectStatementInBlock(
                constructionLoop,
                approveBody);
        if (!constructionLoopIsDirectStatement)
        {
            AddViolation(
                violations,
                "ConstructionLoopDirectStatement");
        }

        var postingCandidates = rootForeachStatements
            .Where(x => x.Identifier.ValueText
                == "movementRequest")
            .ToList();
        if (postingCandidates.Count != 1)
        {
            AddViolation(violations, "PostingLoopCount");
        }

        var postingLoop = postingCandidates.Count == 1
            ? postingCandidates[0]
            : null;
        var postingCollectionIsValid =
            postingLoop?.Expression
                is IdentifierNameSyntax
                {
                    Identifier.ValueText: "movementRequests"
                };
        if (!postingCollectionIsValid)
        {
            AddViolation(violations, "PostingCollection");
        }

        var movementRequestsDeclarators = approveMethod
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(x => x.Identifier.ValueText
                == "movementRequests")
            .ToList();
        var movementRequestsDesignations = approveMethod
            .DescendantNodes()
            .OfType<SingleVariableDesignationSyntax>()
            .Where(x => x.Identifier.ValueText
                == "movementRequests")
            .ToList();
        var movementRequestsDeclarationIsValid =
            movementRequestsDeclarators.Count == 1
            && movementRequestsDesignations.Count == 0
            && movementRequestsDeclarators[0].Parent
                is VariableDeclarationSyntax
                {
                    Type:
                        IdentifierNameSyntax
                        {
                            Identifier.ValueText: "var"
                        }
                } collectionDeclaration
            && collectionDeclaration.Variables.Count == 1
            && collectionDeclaration.Parent
                is LocalDeclarationStatementSyntax
                {
                    Parent: BlockSyntax collectionBlock
                }
            && collectionBlock == approveMethod.Body
            && IsExpectedMovementRequestsCreation(
                movementRequestsDeclarators[0]
                    .Initializer?.Value);
        if (!movementRequestsDeclarationIsValid)
        {
            AddViolation(
                violations,
                "MovementRequestsDeclaration");
        }

        var rootIncreaseCalls = increaseCalls
            .Where(x => IsInRootApproveExecutableScope(
                x,
                approveMethod))
            .ToList();
        var rootDecreaseCalls = decreaseCalls
            .Where(x => IsInRootApproveExecutableScope(
                x,
                approveMethod))
            .ToList();
        var increaseAssignment = rootIncreaseCalls.Count == 1
            ? rootIncreaseCalls[0].Parent
                as AssignmentExpressionSyntax
            : null;
        var decreaseAssignment = rootDecreaseCalls.Count == 1
            ? rootDecreaseCalls[0].Parent
                as AssignmentExpressionSyntax
            : null;
        var increaseAssignmentIsValid =
            IsMovementRequestFactoryAssignment(
                increaseAssignment,
                rootIncreaseCalls.SingleOrDefault());
        var decreaseAssignmentIsValid =
            IsMovementRequestFactoryAssignment(
                decreaseAssignment,
                rootDecreaseCalls.SingleOrDefault());
        if (!increaseAssignmentIsValid)
        {
            AddViolation(
                violations,
                "IncreaseFactoryFlowShape");
        }

        if (!decreaseAssignmentIsValid)
        {
            AddViolation(
                violations,
                "DecreaseFactoryFlowShape");
        }

        var sharedSelectionIfs =
            constructionLoop is null
                || rootIncreaseCalls.Count != 1
                || rootDecreaseCalls.Count != 1
                ? []
                : constructionLoop
                    .DescendantNodes()
                    .OfType<IfStatementSyntax>()
                    .Where(x => IsInRootApproveExecutableScope(
                        x,
                        approveMethod))
                    .Where(x => x.Else is not null)
                    .Where(x => x.Statement.Span.Contains(
                        rootIncreaseCalls[0].Span))
                    .Where(x => x.Else!.Statement.Span.Contains(
                        rootDecreaseCalls[0].Span))
                    .ToList();
        if (sharedSelectionIfs.Count != 1)
        {
            AddViolation(
                violations,
                "FactorySelectionIfElse");
        }

        var sharedSelectionIf =
            sharedSelectionIfs.Count == 1
                ? sharedSelectionIfs[0]
                : null;
        var movementRequestDeclarators =
            constructionLoop?.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Where(x => x.Identifier.ValueText
                    == "movementRequest")
                .Where(x => IsInRootApproveExecutableScope(
                    x,
                    approveMethod))
                .ToList()
            ?? [];
        var movementRequestDeclarationIsValid =
            movementRequestDeclarators.Count == 1
            && movementRequestDeclarators[0].Initializer is null
            && movementRequestDeclarators[0].Parent
                is VariableDeclarationSyntax
                {
                    Type:
                        IdentifierNameSyntax
                        {
                            Identifier.ValueText:
                                "CreateInventoryMovementRequest"
                        }
                } requestDeclaration
            && requestDeclaration.Variables.Count == 1
            && requestDeclaration.Parent
                is LocalDeclarationStatementSyntax
                {
                    Parent: BlockSyntax requestBlock
                }
            && requestBlock == constructionLoop?.Statement;
        if (!movementRequestDeclarationIsValid)
        {
            AddViolation(
                violations,
                "MovementRequestDeclaration");
        }

        var allInvocations = approveMethod
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .ToList();
        var relevantAddCalls = allInvocations
            .Where(x => GetInvokedMethodName(x) == "Add")
            .Where(x => HasExactReceiver(
                    x,
                    "movementRequests")
                || x.ArgumentList.Arguments.Any(
                    argument => IsIdentifier(
                        argument.Expression,
                        "movementRequest")))
            .ToList();
        var rootConstructionAddCalls = relevantAddCalls
            .Where(x => IsInRootApproveExecutableScope(
                x,
                approveMethod))
            .Where(x => constructionLoop is not null
                && x.AncestorsAndSelf().Contains(
                    constructionLoop))
            .ToList();
        if (relevantAddCalls.Count != 1
            || rootConstructionAddCalls.Count != 1)
        {
            AddViolation(
                violations,
                "MovementRequestAddCount");
        }

        if (relevantAddCalls.Any(x =>
                !IsInRootApproveExecutableScope(
                    x,
                    approveMethod)))
        {
            AddViolation(
                violations,
                "MovementRequestAddNestedScope");
        }

        var approvedAdd = rootConstructionAddCalls.Count == 1
            ? rootConstructionAddCalls[0]
            : null;
        var addReceiver = approvedAdd?.Expression
            is MemberAccessExpressionSyntax
            {
                Expression:
                    IdentifierNameSyntax receiver
            }
            ? receiver
            : null;
        var addReceiverIsValid =
            addReceiver?.Identifier.ValueText
                == "movementRequests";
        if (!addReceiverIsValid)
        {
            AddViolation(
                violations,
                "MovementRequestAddReceiver");
        }

        var addArgument =
            approvedAdd?.ArgumentList.Arguments.Count == 1
            && approvedAdd.ArgumentList.Arguments[0]
                .NameColon is null
            && approvedAdd.ArgumentList.Arguments[0]
                .Expression
                is IdentifierNameSyntax addArgumentIdentifier
            ? addArgumentIdentifier
            : null;
        var addArgumentIsValid =
            addArgument?.Identifier.ValueText
                == "movementRequest";
        if (!addArgumentIsValid)
        {
            AddViolation(
                violations,
                "MovementRequestAddArgument");
        }

        var constructionBody =
            constructionLoop?.Statement as BlockSyntax;
        var addStatement = GetContainingExpressionStatement(
            approvedAdd);
        var movementRequestAddIsDirectStatement =
            constructionBody is not null
            && addStatement is not null
            && IsDirectStatementInBlock(
                addStatement,
                constructionBody);
        if (!movementRequestAddIsDirectStatement)
        {
            AddViolation(
                violations,
                "MovementRequestAddDirectStatement");
        }

        var addFlowIsValid =
            approvedAdd is not null
            && sharedSelectionIf is not null
            && movementRequestAddIsDirectStatement
            && approvedAdd.SpanStart
                > sharedSelectionIf.Span.End;
        if (!addFlowIsValid)
        {
            AddViolation(
                violations,
                "MovementRequestAddFlow");
        }

        var constructionRequestIdentifiers =
            constructionLoop?.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(x => x.Identifier.ValueText
                    == "movementRequest")
                .ToList()
            ?? [];
        var nestedRequestIdentifiers =
            constructionRequestIdentifiers
                .Where(x => !IsInRootApproveExecutableScope(
                    x,
                    approveMethod))
                .ToList();
        if (nestedRequestIdentifiers.Count != 0)
        {
            AddViolation(
                violations,
                "MovementRequestNestedUse");
        }

        var requestWriteIdentifiers =
            constructionRequestIdentifiers
                .Where(IsWriteIdentifier)
                .Where(x => IsInRootApproveExecutableScope(
                    x,
                    approveMethod))
                .ToList();
        var expectedRequestWrites =
            new[]
            {
                increaseAssignment?.Left
                    as IdentifierNameSyntax,
                decreaseAssignment?.Left
                    as IdentifierNameSyntax
            }
            .Where(x => x is not null)
            .Cast<IdentifierNameSyntax>()
            .Select(x => x.Span)
            .OrderBy(x => x.Start)
            .ToList();
        var actualRequestWrites =
            requestWriteIdentifiers
                .Select(x => x.Span)
                .OrderBy(x => x.Start)
                .ToList();
        var requestReadIdentifiers =
            constructionRequestIdentifiers
                .Except(requestWriteIdentifiers)
                .Where(x => IsInRootApproveExecutableScope(
                    x,
                    approveMethod))
                .ToList();
        var expectedRequestReads =
            addArgumentIsValid
                ? new[] { addArgument!.Span }
                : [];
        if (!addArgumentIsValid
            || expectedRequestWrites.Count != 2
            || !actualRequestWrites.SequenceEqual(
                expectedRequestWrites)
            || requestReadIdentifiers.Count
                != expectedRequestReads.Length
            || !requestReadIdentifiers
                .Select(x => x.Span)
                .SequenceEqual(expectedRequestReads))
        {
            AddViolation(
                violations,
                "MovementRequestProvenance");
        }

        var preLockCalls = allInvocations
            .Where(x => GetInvokedMethodName(x)
                == "PreLockBalancesAsync")
            .ToList();
        var rootPreLockCalls = preLockCalls
            .Where(x => IsInRootApproveExecutableScope(
                x,
                approveMethod))
            .ToList();
        if (preLockCalls.Count != 1
            || rootPreLockCalls.Count != 1)
        {
            AddViolation(violations, "PreLockCallCount");
        }

        var approvedPreLock =
            rootPreLockCalls.Count == 1
                ? rootPreLockCalls[0]
                : null;
        var preLockCollectionIdentifier =
            GetExpectedPreLockCollectionIdentifier(
                approvedPreLock);
        var preLockShapeIsValid =
            approvedPreLock is not null
            && HasExactReceiver(
                approvedPreLock,
                "_inventoryMovementService")
            && preLockCollectionIdentifier is not null;
        if (!preLockShapeIsValid)
        {
            AddViolation(violations, "PreLockFlow");
        }

        var transactionTryCandidates =
            FindTransactionTryCandidates(
                approveMethod,
                approvedPreLock,
                postingLoop);
        if (transactionTryCandidates.Count != 1)
        {
            AddViolation(
                violations,
                "TransactionTrySelection");
        }

        var transactionTry =
            transactionTryCandidates.Count == 1
                ? transactionTryCandidates[0]
                : null;
        var transactionTryIsDirectStatement =
            transactionTry is not null
            && IsDirectStatementInBlock(
                transactionTry,
                approveBody);
        if (!transactionTryIsDirectStatement)
        {
            AddViolation(
                violations,
                "TransactionTryDirectStatement");
        }

        var preLockStatement =
            GetContainingExpressionStatement(
                approvedPreLock);
        var preLockIsDirectStatement =
            transactionTry is not null
            && preLockStatement is not null
            && IsDirectStatementInBlock(
                preLockStatement,
                transactionTry.Block);
        if (!preLockIsDirectStatement)
        {
            AddViolation(
                violations,
                "PreLockDirectStatement");
        }

        var preLockHasAwaitShape =
            HasAwaitExpressionStatementShape(
                approvedPreLock);
        if (!preLockHasAwaitShape)
        {
            AddViolation(
                violations,
                "PreLockAwaitShape");
        }

        var postingLoopIsDirectStatement =
            transactionTry is not null
            && postingLoop is not null
            && IsDirectStatementInBlock(
                postingLoop,
                transactionTry.Block);
        if (!postingLoopIsDirectStatement)
        {
            AddViolation(
                violations,
                "PostingLoopDirectStatement");
        }

        var relevantPostingCalls =
            allInvocations
                .Where(x => GetInvokedMethodName(x)
                    == "CreateAsync")
                .Where(x => x.ArgumentList.Arguments.Count > 0)
                .Where(x => IsIdentifier(
                    x.ArgumentList.Arguments[0].Expression,
                    "movementRequest"))
                .ToList();
        var postingCallsInLoop =
            postingLoop is null
                ? []
                : relevantPostingCalls
                    .Where(x => x.AncestorsAndSelf()
                        .Contains(postingLoop))
                    .ToList();
        if (relevantPostingCalls.Count != 1
            || postingCallsInLoop.Count != 1)
        {
            AddViolation(violations, "PostingCallCount");
        }

        if (relevantPostingCalls.Any(x =>
                !IsInRootApproveExecutableScope(
                    x,
                    approveMethod)))
        {
            AddViolation(
                violations,
                "PostingCallNestedScope");
        }

        var approvedPostingCall =
            postingCallsInLoop.Count == 1
                ? postingCallsInLoop[0]
                : null;
        var postingReceiverIsValid =
            approvedPostingCall is not null
            && HasExactReceiver(
                approvedPostingCall,
                "_inventoryMovementService");
        if (!postingReceiverIsValid)
        {
            AddViolation(violations, "PostingReceiver");
        }

        var postingArgumentIsValid =
            approvedPostingCall?.ArgumentList.Arguments
                is { Count: 2 } postingArguments
            && postingArguments.All(
                argument => argument.NameColon is null)
            && IsIdentifier(
                postingArguments[0].Expression,
                "movementRequest")
            && IsIdentifier(
                postingArguments[1].Expression,
                "ct");
        if (!postingArgumentIsValid)
        {
            AddViolation(violations, "PostingArgument");
        }

        var postingBody =
            postingLoop?.Statement as BlockSyntax;
        var postingCreateStatement =
            GetContainingExpressionStatement(
                approvedPostingCall);
        var postingCreateIsDirectStatement =
            postingBody is not null
            && postingCreateStatement is not null
            && IsDirectStatementInBlock(
                postingCreateStatement,
                postingBody);
        if (!postingCreateIsDirectStatement)
        {
            AddViolation(
                violations,
                "PostingCreateDirectStatement");
        }

        var postingCreateHasAwaitShape =
            HasAwaitExpressionStatementShape(
                approvedPostingCall);
        if (!postingCreateHasAwaitShape)
        {
            AddViolation(
                violations,
                "PostingCreateAwaitShape");
        }

        var protectedOrderingIsValid =
            constructionLoopIsDirectStatement
            && constructionLoop is not null
            && preLockIsDirectStatement
            && preLockStatement is not null
            && postingLoopIsDirectStatement
            && postingCreateIsDirectStatement
            && postingCreateStatement is not null
            && constructionLoop.Span.End
                < preLockStatement.SpanStart
            && preLockStatement.Span.End
                < postingLoop!.SpanStart
            && postingLoop.SpanStart
                < postingCreateStatement.SpanStart;
        if (!protectedOrderingIsValid)
        {
            AddViolation(violations, "PostingFlow");
        }

        var movementRequestsIdentifiers = approveMethod
            .DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(x => x.Identifier.ValueText
                == "movementRequests")
            .ToList();
        var movementRequestsWrites =
            movementRequestsIdentifiers
                .Where(IsWriteIdentifier)
                .ToList();
        if (movementRequestsWrites.Count != 0
            || movementRequestsDesignations.Count != 0)
        {
            AddViolation(
                violations,
                "MovementRequestsWrite");
        }

        if (movementRequestsIdentifiers.Any(x =>
                !IsInRootApproveExecutableScope(
                    x,
                    approveMethod)))
        {
            AddViolation(
                violations,
                "MovementRequestsNestedUse");
        }

        var unexpectedCollectionMutations =
            allInvocations
                .Where(x => HasExactReceiver(
                    x,
                    "movementRequests"))
                .Where(x => GetInvokedMethodName(x)
                    is not "Add" and not "Select")
                .ToList();
        if (unexpectedCollectionMutations.Count != 0)
        {
            AddViolation(
                violations,
                "MovementRequestsMutation");
        }

        var postingCollectionIdentifier =
            postingCollectionIsValid
                ? (IdentifierNameSyntax)
                    postingLoop!.Expression
                : null;
        var expectedCollectionReads =
            new[]
            {
                addReceiverIsValid
                    ? addReceiver
                    : null,
                preLockCollectionIdentifier,
                postingCollectionIdentifier
            }
            .Where(x => x is not null)
            .Cast<IdentifierNameSyntax>()
            .Select(x => x.Span)
            .OrderBy(x => x.Start)
            .ToList();
        var actualCollectionReads =
            movementRequestsIdentifiers
                .Except(movementRequestsWrites)
                .Where(x => IsInRootApproveExecutableScope(
                    x,
                    approveMethod))
                .Select(x => x.Span)
                .OrderBy(x => x.Start)
                .ToList();
        if (expectedCollectionReads.Count != 3
            || !actualCollectionReads.SequenceEqual(
                expectedCollectionReads))
        {
            AddViolation(
                violations,
                "MovementRequestsUse");
        }

        var constructionFlowIsValid =
            constructionLoop is not null
            && constructionLoopIsDirectStatement
            && movementRequestsDeclarationIsValid
            && movementRequestDeclarationIsValid
            && increaseAssignmentIsValid
            && decreaseAssignmentIsValid
            && sharedSelectionIf is not null
            && approvedAdd is not null
            && addReceiverIsValid
            && addArgumentIsValid
            && movementRequestAddIsDirectStatement
            && addFlowIsValid
            && actualRequestWrites.SequenceEqual(
                expectedRequestWrites)
            && requestReadIdentifiers.Count == 1;
        var postingFlowIsValid =
            postingLoop is not null
            && postingCollectionIsValid
            && preLockShapeIsValid
            && transactionTryCandidates.Count == 1
            && transactionTryIsDirectStatement
            && preLockIsDirectStatement
            && preLockHasAwaitShape
            && postingLoopIsDirectStatement
            && approvedPostingCall is not null
            && postingReceiverIsValid
            && postingArgumentIsValid
            && postingCreateIsDirectStatement
            && postingCreateHasAwaitShape
            && protectedOrderingIsValid;

        return new TwoStageFlowContractResult(
            constructionLoops.Count,
            rootConstructionAddCalls.Count,
            rootPreLockCalls.Count,
            postingCandidates.Count,
            postingCallsInLoop.Count,
            transactionTryCandidates.Count,
            constructionLoopIsDirectStatement,
            movementRequestAddIsDirectStatement,
            preLockIsDirectStatement,
            preLockHasAwaitShape,
            postingLoopIsDirectStatement,
            postingCreateIsDirectStatement,
            postingCreateHasAwaitShape,
            protectedOrderingIsValid,
            constructionFlowIsValid,
            postingFlowIsValid);
    }

    private static List<TryStatementSyntax>
        FindTransactionTryCandidates(
            MethodDeclarationSyntax approveMethod,
            InvocationExpressionSyntax? preLockCall,
            ForEachStatementSyntax? postingLoop)
    {
        if (preLockCall is null || postingLoop is null)
        {
            return [];
        }

        var approvedStatusAssignments = approveMethod
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(x => IsInRootApproveExecutableScope(
                x,
                approveMethod))
            .Where(IsApprovedStatusAssignment)
            .ToList();
        var saveCalls = approveMethod
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(x => IsInRootApproveExecutableScope(
                x,
                approveMethod))
            .Where(x => IsExactUnitOfWorkCall(
                x,
                "SaveChangesAsync"))
            .ToList();
        var commitCalls = approveMethod
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(x => IsInRootApproveExecutableScope(
                x,
                approveMethod))
            .Where(x => IsExactUnitOfWorkCall(
                x,
                "CommitTransactionAsync"))
            .ToList();
        if (approvedStatusAssignments.Count != 1
            || saveCalls.Count != 1
            || commitCalls.Count != 1)
        {
            return [];
        }

        return approveMethod
            .DescendantNodes()
            .OfType<TryStatementSyntax>()
            .Where(x => IsInRootApproveExecutableScope(
                x,
                approveMethod))
            .Where(x => x.Block.Span.Contains(
                preLockCall.Span))
            .Where(x => x.Block.Span.Contains(
                postingLoop.Span))
            .Where(x => x.Block.Span.Contains(
                approvedStatusAssignments[0].Span))
            .Where(x => x.Block.Span.Contains(
                saveCalls[0].Span))
            .Where(x => x.Block.Span.Contains(
                commitCalls[0].Span))
            .ToList();
    }

    private static bool IsApprovedStatusAssignment(
        AssignmentExpressionSyntax assignment)
        => assignment.IsKind(
                SyntaxKind.SimpleAssignmentExpression)
            && IsMemberAccess(
                assignment.Left,
                "document",
                "Status")
            && IsMemberAccess(
                assignment.Right,
                "InventoryAdjustmentDocumentStatus",
                "Approved");

    private static bool IsExactUnitOfWorkCall(
        InvocationExpressionSyntax invocation,
        string methodName)
        => GetInvokedMethodName(invocation) == methodName
            && HasExactReceiver(
                invocation,
                "_unitOfWork")
            && invocation.ArgumentList.Arguments.Count == 1
            && invocation.ArgumentList.Arguments[0]
                .NameColon is null
            && IsIdentifier(
                invocation.ArgumentList.Arguments[0].Expression,
                "ct");

    private static bool IsDocumentLinesExpression(
        ExpressionSyntax expression)
        => expression
            is MemberAccessExpressionSyntax
            {
                Expression:
                    IdentifierNameSyntax
                    {
                        Identifier.ValueText: "document"
                    },
                Name:
                    IdentifierNameSyntax
                    {
                        Identifier.ValueText: "Lines"
                    }
            };

    private static bool IsExpectedMovementRequestsCreation(
        ExpressionSyntax? expression)
        => expression
            is ObjectCreationExpressionSyntax
            {
                Type:
                    GenericNameSyntax
                    {
                        Identifier.ValueText: "List",
                        TypeArgumentList.Arguments.Count: 1
                    } generic,
                ArgumentList.Arguments.Count: 0,
                Initializer: null
            }
            && generic.TypeArgumentList.Arguments[0]
                is IdentifierNameSyntax
                {
                    Identifier.ValueText:
                        "CreateInventoryMovementRequest"
                };

    private static bool IsMovementRequestFactoryAssignment(
        AssignmentExpressionSyntax? assignment,
        InvocationExpressionSyntax? invocation)
        => assignment is not null
            && invocation is not null
            && assignment.IsKind(
                SyntaxKind.SimpleAssignmentExpression)
            && assignment.Right == invocation
            && assignment.Left
                is IdentifierNameSyntax
                {
                    Identifier.ValueText: "movementRequest"
                }
            && assignment.Parent
                is ExpressionStatementSyntax;

    private static bool IsIdentifier(
        ExpressionSyntax expression,
        string identifier)
        => expression
            is IdentifierNameSyntax name
            && name.Identifier.ValueText == identifier;

    private static IdentifierNameSyntax?
        GetExpectedPreLockCollectionIdentifier(
            InvocationExpressionSyntax? preLockCall)
    {
        if (preLockCall is null
            || preLockCall.ArgumentList.Arguments.Count != 2
            || preLockCall.ArgumentList.Arguments.Any(
                x => x.NameColon is not null)
            || !IsIdentifier(
                preLockCall.ArgumentList.Arguments[1].Expression,
                "ct")
            || preLockCall.ArgumentList.Arguments[0].Expression
                is not InvocationExpressionSyntax
                {
                    Expression:
                        MemberAccessExpressionSyntax
                        {
                            Expression:
                                IdentifierNameSyntax collection,
                            Name:
                                IdentifierNameSyntax
                                {
                                    Identifier.ValueText: "Select"
                                }
                        },
                    ArgumentList.Arguments.Count: 1
                } selectCall
            || collection.Identifier.ValueText
                != "movementRequests"
            || selectCall.ArgumentList.Arguments[0].Expression
                is not SimpleLambdaExpressionSyntax
                {
                    Parameter.Identifier.ValueText: "movement",
                    ExpressionBody:
                        ObjectCreationExpressionSyntax
                        {
                            Type:
                                IdentifierNameSyntax
                                {
                                    Identifier.ValueText:
                                        "InventoryPostingLockKey"
                                },
                            ArgumentList.Arguments.Count: 3
                        } lockKey
                }
            || !IsMemberAccess(
                lockKey.ArgumentList.Arguments[0].Expression,
                "document",
                "StoreId")
            || !IsMemberAccess(
                lockKey.ArgumentList.Arguments[1].Expression,
                "movement",
                "WarehouseId")
            || !IsMemberAccess(
                lockKey.ArgumentList.Arguments[2].Expression,
                "movement",
                "ProductVariantId"))
        {
            return null;
        }

        return collection;
    }

    private static bool IsMemberAccess(
        ExpressionSyntax expression,
        string receiver,
        string member)
        => expression
            is MemberAccessExpressionSyntax
            {
                Expression:
                    IdentifierNameSyntax receiverIdentifier,
                Name:
                    IdentifierNameSyntax memberIdentifier
            }
            && receiverIdentifier.Identifier.ValueText == receiver
            && memberIdentifier.Identifier.ValueText == member;

    private static void AddViolation(
        ICollection<string> violations,
        string violation)
    {
        if (!violations.Contains(violation))
        {
            violations.Add(violation);
        }
    }

    private static string ReplaceDocumentReferenceDeclaration(
        string source,
        string replacement)
    {
        var statement = GetDocumentReferenceDeclarationStatement(
            source);
        return ReplaceSourceSpan(
            source,
            statement.Span,
            replacement);
    }

    private static string InsertAfterDocumentReferenceDeclaration(
        string source,
        string statement)
    {
        var declaration = GetDocumentReferenceDeclarationStatement(
            source);
        return source.Insert(
            declaration.Span.End,
            "\n" + statement);
    }

    private static LocalDeclarationStatementSyntax
        GetDocumentReferenceDeclarationStatement(
            string source)
    {
        var root = ParseRequiredCompilationUnit(source);
        var declarations = root
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(x => x.Identifier.ValueText
                == "documentReferenceId")
            .Select(x => x.Parent?.Parent)
            .OfType<LocalDeclarationStatementSyntax>()
            .ToList();

        return declarations.Count == 1
            ? declarations[0]
            : throw new InvalidOperationException(
                "Expected one documentReferenceId local declaration.");
    }

    private static string ReplaceFactoryReferenceArgument(
        string source,
        string methodName,
        string replacement)
    {
        var invocation = GetRequiredFactoryInvocation(
            source,
            methodName);
        var referenceArguments = invocation
            .ArgumentList
            .Arguments
            .Where(x => x.Expression
                is IdentifierNameSyntax
                {
                    Identifier.ValueText: "documentReferenceId"
                })
            .ToList();
        if (referenceArguments.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one {methodName} reference argument.");
        }

        return ReplaceSourceSpan(
            source,
            referenceArguments[0].Expression.Span,
            replacement);
    }

    private static string InsertBeforeFactoryAssignment(
        string source,
        string methodName,
        string statement)
    {
        var invocation = GetRequiredFactoryInvocation(
            source,
            methodName);
        var expressionStatement = invocation
            .Ancestors()
            .OfType<ExpressionStatementSyntax>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Expected {methodName} expression statement.");

        return source.Insert(
            expressionStatement.SpanStart,
            statement + "\n");
    }

    private static InvocationExpressionSyntax
        GetRequiredFactoryInvocation(
            string source,
            string methodName)
    {
        var root = ParseRequiredCompilationUnit(source);
        var calls = root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(x => x.Expression
                is MemberAccessExpressionSyntax
                {
                    Expression:
                        IdentifierNameSyntax
                        {
                            Identifier.ValueText:
                                "_inventoryMovementFactory"
                        },
                    Name:
                        IdentifierNameSyntax name
                }
                && name.Identifier.ValueText == methodName)
            .ToList();

        return calls.Count == 1
            ? calls[0]
            : throw new InvalidOperationException(
                $"Expected one {methodName} invocation.");
    }

    private static string
        BuildAliasStandaloneWithHiddenFactoryCalls(
            string source,
            bool useLocalFunction)
    {
        var mutated = ReplaceFactoryInvocationArgumentList(
            source,
            "CreateAdjustmentIncrease",
            """
            (
                document.WarehouseId,
                line.ProductVariantId,
                line.BaseQuantity,
                finalUnitCost.Value,
                note,
                occurredAtUtc)
            """);
        mutated = ReplaceFactoryInvocationArgumentList(
            mutated,
            "CreateAdjustmentDecrease",
            """
            (
                document.WarehouseId,
                line.ProductVariantId,
                line.BaseQuantity,
                line.ProvisionalUnitCost,
                note,
                occurredAtUtc)
            """);
        mutated = ReplaceFactoryReceiver(
            mutated,
            "CreateAdjustmentIncrease",
            "movementFactoryAlias");
        mutated = ReplaceFactoryReceiver(
            mutated,
            "CreateAdjustmentDecrease",
            "movementFactoryAlias");
        var hiddenCalls = useLocalFunction
            ? BuildHiddenDurableLocalFunction()
            : BuildHiddenDurableLambdas(
                includeDecrease: true);

        return InsertBeforeConstructionSelection(
            mutated,
            "var movementFactoryAlias = "
            + "_inventoryMovementFactory;\n"
            + hiddenCalls);
    }

    private static string
        BuildExactReceiverStandaloneWithHiddenDurableCall(
            string source)
    {
        var mutated = ReplaceFactoryInvocationArgumentList(
            source,
            "CreateAdjustmentIncrease",
            """
            (
                document.WarehouseId,
                line.ProductVariantId,
                line.BaseQuantity,
                finalUnitCost.Value,
                note,
                occurredAtUtc)
            """);

        return InsertBeforeConstructionSelection(
            mutated,
            BuildHiddenDurableLambdas(
                includeDecrease: false));
    }

    private static string BuildAliasDurableFactoryCall(
        string source)
    {
        var mutated = ReplaceFactoryReceiver(
            source,
            "CreateAdjustmentIncrease",
            "movementFactoryAlias");

        return InsertBeforeConstructionSelection(
            mutated,
            "var movementFactoryAlias = "
            + "_inventoryMovementFactory;");
    }

    private static string BuildPostingServiceAlias(
        string source)
    {
        var postingCall =
            GetRequiredPostingCreateInvocation(source);
        if (postingCall.Expression
            is not MemberAccessExpressionSyntax member)
        {
            throw new InvalidOperationException(
                "Expected member-access posting call.");
        }

        var mutated = ReplaceSourceSpan(
            source,
            member.Expression.Span,
            "movementServiceAlias");
        return InsertAfterDocumentReferenceDeclaration(
            mutated,
            "var movementServiceAlias = "
            + "_inventoryMovementService;");
    }

    private static string HideMovementRequestAddInLambda(
        string source)
    {
        var addCall = GetRequiredMovementRequestAddInvocation(
            source);
        var statement = addCall.Ancestors()
            .OfType<ExpressionStatementSyntax>()
            .First();

        return ReplaceSourceSpan(
            source,
            statement.Span,
            "Action hiddenAdd = () => "
            + "movementRequests.Add(movementRequest);");
    }

    private static string WrapPostingCreateStatement(
        string source,
        string controlHeader)
        => WrapStatement(
            source,
            GetContainingExpressionStatement(
                GetRequiredPostingCreateInvocation(source))
                ?? throw new InvalidOperationException(
                    "Expected posting expression statement."),
            controlHeader);

    private static string WrapMovementRequestAddStatement(
        string source,
        string controlHeader)
        => WrapStatement(
            source,
            GetContainingExpressionStatement(
                GetRequiredMovementRequestAddInvocation(source))
                ?? throw new InvalidOperationException(
                    "Expected Add expression statement."),
            controlHeader);

    private static string WrapPreLockStatement(
        string source,
        string controlHeader)
        => WrapStatement(
            source,
            GetContainingExpressionStatement(
                GetRequiredPreLockInvocation(source))
                ?? throw new InvalidOperationException(
                    "Expected prelock expression statement."),
            controlHeader);

    private static string DropAwaitFromPostingCreate(
        string source)
        => DropAwaitFromInvocation(
            source,
            GetRequiredPostingCreateInvocation(source));

    private static string DropAwaitFromPreLock(
        string source)
        => DropAwaitFromInvocation(
            source,
            GetRequiredPreLockInvocation(source));

    private static string WrapPostingLoop(
        string source,
        string controlHeader)
        => WrapStatement(
            source,
            GetRequiredPostingLoop(source),
            controlHeader);

    private static string WrapConstructionLoop(
        string source,
        string controlHeader)
        => WrapStatement(
            source,
            GetRequiredConstructionLoop(source),
            controlHeader);

    private static string WrapPreLockStatementInSwitch(
        string source)
    {
        var statement = GetContainingExpressionStatement(
            GetRequiredPreLockInvocation(source))
            ?? throw new InvalidOperationException(
                "Expected prelock expression statement.");
        var statementText = source[
            statement.Span.Start..statement.Span.End];

        return ReplaceSourceSpan(
            source,
            statement.Span,
            "switch (document.Id)\n"
            + "{\n"
            + "    default:\n"
            + statementText
            + "\n        break;\n"
            + "}");
    }

    private static string WrapStatement(
        string source,
        StatementSyntax statement,
        string controlHeader)
    {
        var statementText = source[
            statement.Span.Start..statement.Span.End];
        return ReplaceSourceSpan(
            source,
            statement.Span,
            controlHeader
            + "\n{\n"
            + statementText
            + "\n}");
    }

    private static string DropAwaitFromInvocation(
        string source,
        InvocationExpressionSyntax invocation)
    {
        if (invocation.Parent
            is not AwaitExpressionSyntax awaitExpression
            || awaitExpression.Expression != invocation)
        {
            throw new InvalidOperationException(
                "Expected directly awaited invocation.");
        }

        return ReplaceSourceSpan(
            source,
            awaitExpression.Span,
            source[invocation.Span.Start..invocation.Span.End]);
    }

    private static string ReplaceMovementRequestAddArgument(
        string source,
        string replacement)
    {
        var addCall = GetRequiredMovementRequestAddInvocation(
            source);
        var argument = addCall.ArgumentList.Arguments.Single();
        var statement = addCall.Ancestors()
            .OfType<ExpressionStatementSyntax>()
            .First();
        var mutated = ReplaceSourceSpan(
            source,
            argument.Expression.Span,
            replacement);

        return mutated.Insert(
            statement.SpanStart,
            $"var {replacement} = movementRequest;\n");
    }

    private static string ReplacePostingCollection(
        string source,
        string replacement)
    {
        var postingLoop = GetRequiredApproveMethod(source)
            .DescendantNodes()
            .OfType<ForEachStatementSyntax>()
            .Single(x => x.Identifier.ValueText
                == "movementRequest");
        var mutated = ReplaceSourceSpan(
            source,
            postingLoop.Expression.Span,
            replacement);

        return mutated.Insert(
            postingLoop.SpanStart,
            $"var {replacement} = movementRequests;\n");
    }

    private static string ReplaceFactoryInvocationArgumentList(
        string source,
        string methodName,
        string replacement)
    {
        var invocation = GetRequiredFactoryInvocation(
            source,
            methodName);
        return ReplaceSourceSpan(
            source,
            invocation.ArgumentList.Span,
            replacement);
    }

    private static string ReplaceFactoryReceiver(
        string source,
        string methodName,
        string replacement)
    {
        var invocation = GetRequiredFactoryInvocation(
            source,
            methodName);
        if (invocation.Expression
            is not MemberAccessExpressionSyntax member)
        {
            throw new InvalidOperationException(
                $"Expected {methodName} member access.");
        }

        return ReplaceSourceSpan(
            source,
            member.Expression.Span,
            replacement);
    }

    private static string InsertBeforeConstructionSelection(
        string source,
        string statement)
    {
        var selection = GetRequiredConstructionSelection(
            source);
        return source.Insert(
            selection.SpanStart,
            statement + "\n");
    }

    private static IfStatementSyntax
        GetRequiredConstructionSelection(
            string source)
    {
        var constructionLoop = GetRequiredApproveMethod(source)
            .DescendantNodes()
            .OfType<ForEachStatementSyntax>()
            .Single(x => x.Identifier.ValueText == "line"
                && IsDocumentLinesExpression(x.Expression));
        return constructionLoop
            .DescendantNodes()
            .OfType<IfStatementSyntax>()
            .Single(x => x.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(call => GetInvokedMethodName(call)
                    == "CreateAdjustmentIncrease")
                && x.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(call => GetInvokedMethodName(call)
                        == "CreateAdjustmentDecrease"));
    }

    private static MethodDeclarationSyntax
        GetRequiredApproveMethod(
            string source)
        => ParseRequiredCompilationUnit(source)
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single(x => x.Identifier.ValueText
                == "InventoryAdjustmentDocumentService")
            .Members
            .OfType<MethodDeclarationSyntax>()
            .Single(x => x.Identifier.ValueText
                == "ApproveAsync");

    private static ForEachStatementSyntax
        GetRequiredConstructionLoop(
            string source)
        => GetRequiredApproveMethod(source)
            .DescendantNodes()
            .OfType<ForEachStatementSyntax>()
            .Single(x => x.Identifier.ValueText == "line"
                && IsDocumentLinesExpression(x.Expression));

    private static ForEachStatementSyntax
        GetRequiredPostingLoop(
            string source)
        => GetRequiredApproveMethod(source)
            .DescendantNodes()
            .OfType<ForEachStatementSyntax>()
            .Single(x => x.Identifier.ValueText
                == "movementRequest"
                && IsIdentifier(
                    x.Expression,
                    "movementRequests"));

    private static InvocationExpressionSyntax
        GetRequiredMovementRequestAddInvocation(
            string source)
        => GetRequiredApproveMethod(source)
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(x => GetInvokedMethodName(x) == "Add"
                && HasExactReceiver(
                    x,
                    "movementRequests")
                && x.ArgumentList.Arguments.Count == 1
                && IsIdentifier(
                    x.ArgumentList.Arguments[0].Expression,
                    "movementRequest"));

    private static InvocationExpressionSyntax
        GetRequiredPreLockInvocation(
            string source)
        => GetRequiredApproveMethod(source)
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(x => GetInvokedMethodName(x)
                    == "PreLockBalancesAsync"
                && HasExactReceiver(
                    x,
                    "_inventoryMovementService"));

    private static InvocationExpressionSyntax
        GetRequiredPostingCreateInvocation(
            string source)
        => GetRequiredApproveMethod(source)
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(x => GetInvokedMethodName(x) == "CreateAsync"
                && HasExactReceiver(
                    x,
                    "_inventoryMovementService")
                && x.ArgumentList.Arguments.Count == 2
                && IsIdentifier(
                    x.ArgumentList.Arguments[0].Expression,
                    "movementRequest"));

    private static string BuildHiddenDurableLambdas(
        bool includeDecrease)
    {
        const string increase =
            """
            Func<CreateInventoryMovementRequest> hiddenIncrease = () =>
                _inventoryMovementFactory.CreateAdjustmentIncrease(
                    document.WarehouseId,
                    line.ProductVariantId,
                    line.BaseQuantity,
                    line.UnitCost ?? 1m,
                    documentReferenceId,
                    line.Id,
                    note,
                    occurredAtUtc);
            """;
        const string decrease =
            """
            Func<CreateInventoryMovementRequest> hiddenDecrease = () =>
                _inventoryMovementFactory.CreateAdjustmentDecrease(
                    document.WarehouseId,
                    line.ProductVariantId,
                    line.BaseQuantity,
                    line.ProvisionalUnitCost,
                    documentReferenceId,
                    line.Id,
                    note,
                    occurredAtUtc);
            """;

        return includeDecrease
            ? increase + "\n" + decrease
            : increase;
    }

    private static string BuildHiddenDurableLocalFunction()
        =>
            """
            void HiddenDurableRequests()
            {
                _ = _inventoryMovementFactory.CreateAdjustmentIncrease(
                    document.WarehouseId,
                    line.ProductVariantId,
                    line.BaseQuantity,
                    line.UnitCost ?? 1m,
                    documentReferenceId,
                    line.Id,
                    note,
                    occurredAtUtc);
                _ = _inventoryMovementFactory.CreateAdjustmentDecrease(
                    document.WarehouseId,
                    line.ProductVariantId,
                    line.BaseQuantity,
                    line.ProvisionalUnitCost,
                    documentReferenceId,
                    line.Id,
                    note,
                    occurredAtUtc);
            }
            """;

    private static string RemoveDurableFactoryMethod(
        string source,
        string methodName)
    {
        var (root, factoryInterface, method) =
            GetRequiredDurableFactoryMethod(
                source,
                methodName);
        var updatedInterface = factoryInterface.RemoveNode(
            method,
            SyntaxRemoveOptions.KeepExteriorTrivia)
            ?? throw new InvalidOperationException(
                $"Could not remove {methodName}.");

        return root.ReplaceNode(
                factoryInterface,
                updatedInterface)
            .ToFullString();
    }

    private static string DuplicateDurableFactoryMethod(
        string source,
        string methodName)
    {
        var (root, factoryInterface, method) =
            GetRequiredDurableFactoryMethod(
                source,
                methodName);
        var index = factoryInterface.Members.IndexOf(method);
        var updatedInterface = factoryInterface.WithMembers(
            factoryInterface.Members.Insert(
                index + 1,
                method));

        return root.ReplaceNode(
                factoryInterface,
                updatedInterface)
            .ToFullString();
    }

    private static string RenameDurableFactoryParameter(
        string source,
        string methodName,
        string oldName,
        string newName)
    {
        var (root, _, method) =
            GetRequiredDurableFactoryMethod(
                source,
                methodName);
        var parameter = method.ParameterList.Parameters
            .Single(x => x.Identifier.ValueText == oldName);
        var updatedParameter = parameter.WithIdentifier(
            SyntaxFactory.Identifier(newName)
                .WithTriviaFrom(parameter.Identifier));

        return root.ReplaceNode(
                parameter,
                updatedParameter)
            .ToFullString();
    }

    private static string MoveDurableFactoryParameter(
        string source,
        string methodName,
        string parameterName,
        int destinationIndex)
    {
        var (root, _, method) =
            GetRequiredDurableFactoryMethod(
                source,
                methodName);
        var parameters =
            method.ParameterList.Parameters.ToList();
        var parameter = parameters.Single(
            x => x.Identifier.ValueText == parameterName);
        parameters.Remove(parameter);
        parameters.Insert(destinationIndex, parameter);
        var updatedMethod = method.WithParameterList(
            method.ParameterList.WithParameters(
                SyntaxFactory.SeparatedList(parameters)));

        return root.ReplaceNode(
                method,
                updatedMethod)
            .ToFullString();
    }

    private static string RemoveDurableFactoryParameter(
        string source,
        string methodName,
        string parameterName)
    {
        var (root, _, method) =
            GetRequiredDurableFactoryMethod(
                source,
                methodName);
        var parameter = method.ParameterList.Parameters
            .Single(x => x.Identifier.ValueText
                == parameterName);
        var updatedMethod = method.WithParameterList(
            method.ParameterList.WithParameters(
                method.ParameterList.Parameters.Remove(
                    parameter)));

        return root.ReplaceNode(
                method,
                updatedMethod)
            .ToFullString();
    }

    private static (
        CompilationUnitSyntax Root,
        InterfaceDeclarationSyntax FactoryInterface,
        MethodDeclarationSyntax Method)
        GetRequiredDurableFactoryMethod(
            string source,
            string methodName)
    {
        var root = ParseRequiredCompilationUnit(source);
        var factoryInterface = root
            .DescendantNodes()
            .OfType<InterfaceDeclarationSyntax>()
            .Single(x => x.Identifier.ValueText
                == "IInventoryMovementFactory");
        var methods = FindDurableFactoryMethods(
            factoryInterface,
            methodName);

        return methods.Count == 1
            ? (root, factoryInterface, methods[0])
            : throw new InvalidOperationException(
                $"Expected one durable {methodName} overload.");
    }

    private static CompilationUnitSyntax ParseRequiredCompilationUnit(
        string source)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var errors = tree.GetDiagnostics()
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(
                "Mutation baseline must parse without errors: "
                + string.Join("; ", errors));
        }

        return tree.GetCompilationUnitRoot();
    }

    private static string ReplaceSourceSpan(
        string source,
        Microsoft.CodeAnalysis.Text.TextSpan span,
        string replacement)
        => source[..span.Start]
            + replacement
            + source[span.End..];

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

    private static string BuildRawStringDeclarationBypass(
        int dollarCount)
        =>
            "BuildReference(document, out var documentReferenceId);\n\n"
            + "var fakeContract = "
            + BuildRawStringLiteral(
                """
                var documentReferenceId =
                    document.Id.ToString(CultureInfo.InvariantCulture);
                """,
                dollarCount,
                quoteCount: 3)
            + ";";

    private static string
        BuildRawStringContainingCorrectDecreaseCall()
        =>
            "var fakeDecreaseCall = "
            + BuildRawStringLiteral(
                """
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
                """,
                dollarCount: 0,
                quoteCount: 3)
            + ";";

    private static string BuildRawFakeMethodBoundariesStatement()
        =>
            "var fakeMethodBoundaries = "
            + BuildRawStringLiteral(
                """
                class InventoryAdjustmentDocumentService
                {
                    public Task ApproveAsync() => Task.CompletedTask;
                    public Task RejectAsync() => Task.CompletedTask;
                }
                """,
                dollarCount: 2,
                quoteCount: 3)
            + ";";

    private static string
        BuildNestedInterpolatedRawContractStatement()
        =>
            """"
            var fakeContract = $"""
                {
                    """
                    var documentReferenceId =
                        document.Id.ToString(CultureInfo.InvariantCulture);
                    """
                }
                """;
            """";

    private static string BuildHarmlessRawStringStatement(
        int dollarCount,
        int quoteCount,
        string? additionalContent = null,
        string lineEnding = "\n")
    {
        var content =
            BuildDangerousRawStringContent()
                .ReplaceLineEndings(lineEnding);
        if (!string.IsNullOrEmpty(additionalContent))
        {
            content +=
                lineEnding + additionalContent;
        }

        return "var harmlessRaw = "
               + BuildRawStringLiteral(
                   content,
                   dollarCount,
                   quoteCount,
                   lineEnding)
               + ";";
    }

    private static string BuildDangerousRawStringContent()
        =>
            """
            CultureInfo.CurrentCulture;
            CultureInfo.CurrentUICulture;
            document.Id.ToString();
            documentReferenceId = alternateReferenceId;
            _inventoryMovementFactory.CreateAdjustmentIncrease(
                warehouseId,
                productVariantId,
                quantity,
                unitCost,
                wrongReferenceId,
                lineId,
                note,
                occurredAtUtc);
            _inventoryMovementFactory.CreateAdjustmentDecrease(
                warehouseId,
                productVariantId,
                quantity,
                provisionalUnitCost,
                wrongReferenceId,
                lineId,
                note,
                occurredAtUtc);
            ApproveAsync(fake);
            RejectAsync(fake);
            """;

    private static string BuildRawStringLiteral(
        string content,
        int dollarCount,
        int quoteCount,
        string lineEnding = "\n")
    {
        if (dollarCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dollarCount));
        }

        if (quoteCount < 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quoteCount));
        }

        var delimiter = new string(
            '"',
            quoteCount);
        return new string('$', dollarCount)
               + delimiter
               + lineEnding
               + content.ReplaceLineEndings(lineEnding)
               + lineEnding
               + delimiter;
    }

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

    private sealed record AdjustmentReferenceAstContractResult(
        IReadOnlyList<string> Violations,
        int ServiceParseErrorCount,
        int FactoryInterfaceParseErrorCount,
        int ServiceClassCount,
        int ApproveMethodCount,
        int DocumentReferenceDeclarationCount,
        bool DocumentReferenceDeclarationIsDirectVar,
        bool DocumentReferenceInitializerIsInvariant,
        int DocumentReferenceConversionCount,
        int DocumentReferenceWriteCount,
        int DocumentReferenceReadCount,
        int IncreaseOverloadCount,
        int DecreaseOverloadCount,
        int? IncreaseReferenceIndex,
        int? DecreaseReferenceIndex,
        int IncreaseCallCount,
        int DecreaseCallCount,
        bool IncreaseReferenceArgumentIsDocumentReference,
        bool DecreaseReferenceArgumentIsDocumentReference,
        int ConstructionLoopCount,
        int MovementRequestAddCount,
        int PreLockCallCount,
        int PostingLoopCount,
        int PostingCallCount,
        int TransactionTryCount,
        bool ConstructionLoopIsDirectStatement,
        bool MovementRequestAddIsDirectStatement,
        bool PreLockIsDirectStatement,
        bool PreLockHasAwaitShape,
        bool PostingLoopIsDirectStatement,
        bool PostingCreateIsDirectStatement,
        bool PostingCreateHasAwaitShape,
        bool ProtectedOrderingIsValid,
        bool ConstructionFlowIsValid,
        bool PostingFlowIsValid);

    private sealed record TwoStageFlowContractResult(
        int ConstructionLoopCount,
        int MovementRequestAddCount,
        int PreLockCallCount,
        int PostingLoopCount,
        int PostingCallCount,
        int TransactionTryCount,
        bool ConstructionLoopIsDirectStatement,
        bool MovementRequestAddIsDirectStatement,
        bool PreLockIsDirectStatement,
        bool PreLockHasAwaitShape,
        bool PostingLoopIsDirectStatement,
        bool PostingCreateIsDirectStatement,
        bool PostingCreateHasAwaitShape,
        bool ProtectedOrderingIsValid,
        bool ConstructionFlowIsValid,
        bool PostingFlowIsValid)
    {
        public static TwoStageFlowContractResult Empty { get; } =
            new(
                0,
                0,
                0,
                0,
                0,
                0,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false);
    }

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
