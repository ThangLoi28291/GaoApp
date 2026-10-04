using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Tests.Configuration;

/// <summary>
/// Pure normalization/manifest tests: no database is created or opened.
/// Live SQL Server coverage remains in the existing schema/preflight suite.
/// </summary>
public sealed class CheckConstraintNormalizationRegressionTests
{
    private const string ExpectedMenuTarget =
        "([RoleId] IS NOT NULL AND [UserInStoreId] IS NULL) OR ([RoleId] IS NULL AND [UserInStoreId] IS NOT NULL)";
    private const string ActualMenuTarget =
        "([RoleId] IS NOT NULL AND [UserInStoreId] IS NULL OR [RoleId] IS NULL AND [UserInStoreId] IS NOT NULL)";

    [Fact]
    public void Server_menu_target_definition_matches_migration_without_redundant_and_parentheses()
    {
        Assert.Equal(Normalize(ExpectedMenuTarget), Normalize(ActualMenuTarget));
        Assert.True(DatabaseSchemaComparer.Compare(Manifest(ExpectedMenuTarget), Manifest(ActualMenuTarget)).IsMatch);
    }

    [Theory]
    [InlineData("((A IS NULL) AND (B IS NOT NULL)) OR ((C IS NULL))", "A IS NULL AND B IS NOT NULL OR C IS NULL")]
    [InlineData("((A IS NULL AND B IS NULL))", "A IS NULL AND B IS NULL")]
    [InlineData("[OR] IS NULL AND [AND] IS NOT NULL", "([OR] IS NULL) AND ([AND] IS NOT NULL)")]
    public void Null_only_groups_remove_only_redundant_parentheses(string expected, string actual)
    {
        Assert.Equal(Normalize(expected), Normalize(actual));
        Assert.Equal(Normalize(actual), Normalize(Normalize(actual)));
    }

    [Theory]
    [InlineData("A IS NULL AND (B IS NULL OR C IS NULL)", "A IS NULL AND B IS NULL OR C IS NULL")]
    [InlineData("(A IS NULL OR B IS NULL) AND C IS NULL", "A IS NULL OR B IS NULL AND C IS NULL")]
    [InlineData("A IS NULL AND B IS NULL", "A IS NULL OR B IS NULL")]
    [InlineData("A IS NULL", "A IS NOT NULL")]
    [InlineData("A IS NULL", "B IS NULL")]
    [InlineData("NOT (A IS NULL AND B IS NULL)", "A IS NULL AND B IS NULL")]
    public void Null_predicate_normalization_preserves_meaning(string expected, string changed)
        => Assert.NotEqual(Normalize(expected), Normalize(changed));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Menu_target_disabled_or_untrusted_still_rejects(bool disabled, bool untrusted)
        => Assert.False(DatabaseSchemaComparer.Compare(
            Manifest(ExpectedMenuTarget), Manifest(ActualMenuTarget, disabled, untrusted)).IsMatch);

    private const string ExpectedReceiving =
        "[ReceiptAllocationKind] = 0 AND [OutsidePoDecisionStatus] = 0 OR " +
        "[ReceiptAllocationKind] = 1 AND [PurchaseOrderLineId] IS NOT NULL AND [OutsidePoDecisionStatus] = 0 OR " +
        "[ReceiptAllocationKind] = 2 AND [PurchaseOrderLineId] IS NULL AND " +
        "([OutsidePoDecisionStatus] = 1 OR [OutsidePoDecisionStatus] = 2 OR [OutsidePoDecisionStatus] = 3)";

    private const string ActualReceiving =
        "([ReceiptAllocationKind]=(0) AND [OutsidePoDecisionStatus]=(0) OR " +
        "[ReceiptAllocationKind]=(1) AND [PurchaseOrderLineId] IS NOT NULL AND [OutsidePoDecisionStatus]=(0) OR " +
        "[ReceiptAllocationKind]=(2) AND [PurchaseOrderLineId] IS NULL AND " +
        "([OutsidePoDecisionStatus]=(3) OR [OutsidePoDecisionStatus]=(2) OR [OutsidePoDecisionStatus]=(1)))";

    [Fact]
    public void Actual_receiving_constraint_matches_migration_after_or_reordering()
    {
        Assert.NotEqual(
            DatabaseSchemaNormalization.NormalizeSqlExpression(ExpectedReceiving),
            DatabaseSchemaNormalization.NormalizeSqlExpression(ActualReceiving));
        Assert.Equal(Normalize(ExpectedReceiving), Normalize(ActualReceiving));
    }

    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(1, 3, 2)]
    [InlineData(2, 1, 3)]
    [InlineData(2, 3, 1)]
    [InlineData(3, 1, 2)]
    [InlineData(3, 2, 1)]
    public void Every_permutation_of_the_same_numeric_disjunction_matches(int a, int b, int c)
    {
        var actual = $"([Kind]=(2) AND ([Status]=({a}) OR [Status]=({b}) OR [Status]=({c})))";
        Assert.Equal(Normalize("[Kind]=2 AND ([Status]=1 OR [Status]=2 OR [Status]=3)"), Normalize(actual));
        Assert.Equal(Normalize(actual), Normalize(Normalize(actual)));
    }

    [Theory]
    [InlineData("[Status]=3 OR [Status]=1 OR [Status]=2", true)]
    [InlineData("(([Status]=(3)) OR (([Status])=(1)) OR [Status]=2)", false)]
    [InlineData("(Status=(3))OR(Status=(2))OR(Status=(1))", true)]
    [InlineData(" STATUS = 2\nOR [Status] = (1)\tOR status = 3 ", true)]
    [InlineData("([Status]=2) OR (([Status]=3)) OR ([Status]=1)", true)]
    public void Supported_atomic_parentheses_and_token_boundaries_are_respected(string expression, bool supported)
    {
        if (supported)
        {
            Assert.Equal(Normalize("Status=1 OR Status=2 OR Status=3"), Normalize(expression));
        }
        else
        {
            // Parenthesized identifiers are deliberately outside this narrow grammar.
            Assert.Equal(DatabaseSchemaNormalization.NormalizeSqlExpression(expression), Normalize(expression));
        }
    }

    [Theory]
    [InlineData("[X]=-10 OR [X]=0 OR [X]=2.50", "[X]=(2.50) OR [X]=(-10) OR [X]=(0)")]
    [InlineData("[ORDER]=1 OR [ORDER]=2", "[ORDER]=2 OR [ORDER]=1")]
    [InlineData("[Color]=1 OR [Color]=2", "[Color]=2 OR [Color]=1")]
    [InlineData("[X]=123456789012345678901234567890 OR [X]=2", "[X]=2 OR [X]=123456789012345678901234567890")]
    public void Numbers_are_sorted_without_precision_loss_and_identifiers_are_not_split(string expected, string actual)
        => Assert.Equal(Normalize(expected), Normalize(actual));

    [Theory]
    [InlineData("X=1 OR X=2 OR X=4")]
    [InlineData("X=1 OR X=2")]
    [InlineData("X=1 AND X=2 AND X=3")]
    [InlineData("X=1 OR Y=2 OR X=3")]
    [InlineData("X=1 OR X<>2 OR X=3")]
    [InlineData("X=1 OR X>=2 OR X=3")]
    [InlineData("X=1 OR X=2 OR X=NULL")]
    [InlineData("X=1 OR X=2 OR X=3 OR X=3")]
    [InlineData("NOT (X=1 OR X=2 OR X=3)")]
    public void Changed_constants_operators_columns_nulls_and_not_remain_different(string changed)
        => Assert.NotEqual(Normalize("X=1 OR X=2 OR X=3"), Normalize(changed));

    [Theory]
    [InlineData("A=1 AND (X=2 OR X=1)", "A=1 AND X=2 OR X=1")]
    [InlineData("(A=1 AND X=2) OR X=1", "A=1 AND (X=2 OR X=1)")]
    [InlineData("(X=2 OR X=1) AND Y=3", "X=2 OR (X=1 AND Y=3)")]
    [InlineData("A=1 AND (X=2 OR X=1)", "(A=1 OR X=2) AND X=1")]
    public void Mixed_boolean_grouping_is_never_flattened(string first, string second)
        => Assert.NotEqual(Normalize(first), Normalize(second));

    [Theory]
    [InlineData("Text=N'X=2 OR X=1'")]
    [InlineData("Text='(X=2 OR X=1)' AND (X=2 OR X=1)")]
    [InlineData("X=2 /* keep comment */ OR X=1")]
    [InlineData("X=2 -- comment\n OR X=1")]
    [InlineData("[X Y]=2 OR [X Y]=1")]
    [InlineData("[X]]Y]=2 OR [X]]Y]=1")]
    [InlineData("\"X\"=2 OR \"X\"=1")]
    [InlineData("dbo.X=2 OR dbo.X=1")]
    [InlineData("ABS(X)=2 OR ABS(X)=1")]
    [InlineData("X=1e2 OR X=2e2")]
    public void Unsupported_shapes_do_not_get_an_additional_or_rewrite(string sql)
        => Assert.Equal(DatabaseSchemaNormalization.NormalizeSqlExpression(sql), Normalize(sql));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n\t ")]
    public void Empty_check_expression_stays_null(string? sql)
        => Assert.Null(DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(sql));

    [Fact]
    public void Normalization_is_idempotent()
    {
        var first = Normalize(ActualReceiving);
        Assert.Equal(first, Normalize(first));
        Assert.Contains("outsidepodecisionstatus=1oroutsidepodecisionstatus=2oroutsidepodecisionstatus=3", first);
    }

    [Fact]
    public void Generic_index_and_default_normalizers_are_not_changed_to_use_check_sorting()
    {
        const string index = "[OrderId] IS NOT NULL AND ([Kind] IN ('Apply','Void'))";
        Assert.Equal("orderidisnotnullandkindin('apply','void')",
            DatabaseSchemaNormalization.NormalizeSqlExpression(index));
        Assert.Equal("0", DatabaseSchemaNormalization.NormalizeDefaultExpression("((0))"));
        Assert.NotEqual(DatabaseSchemaNormalization.NormalizeSqlExpression("X=1 OR X=2"),
            DatabaseSchemaNormalization.NormalizeSqlExpression("X=2 OR X=1"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_create_table_and_add_check_catalog_paths_use_check_normalization(bool inline)
    {
        var harness = new OperationHarness();
        var table = new CreateTableOperation { Name = "FixtureTable", Schema = "dbo" };
        var check = new AddCheckConstraintOperation
        {
            Name = "CK_Fixture", Table = "FixtureTable", Schema = "dbo", Sql = ActualReceiving
        };
        if (inline)
        {
            table.CheckConstraints.Add(check);
        }

        harness.Apply(table);
        if (!inline)
        {
            harness.Apply(check);
        }

        var result = Assert.Single(harness.Table().CheckConstraints);
        Assert.Equal(Normalize(ExpectedReceiving), result.Expression);
        Assert.False(result.IsDisabled);
        Assert.False(result.IsNotTrusted);
    }

    [Fact]
    public void Equivalent_check_records_have_the_same_fingerprint_and_zero_mismatches()
    {
        var expected = Manifest(ExpectedReceiving);
        var actual = Manifest(ActualReceiving);
        var comparison = DatabaseSchemaComparer.Compare(expected, actual);
        Assert.True(comparison.IsMatch);
        Assert.Equal(0, comparison.Mismatches.Total);
        Assert.Equal(expected.Fingerprint, actual.Fingerprint);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Disabled_and_untrusted_flags_still_reject_the_same_expression(bool disabled, bool untrusted)
    {
        var result = DatabaseSchemaComparer.Compare(
            Manifest(ExpectedReceiving), Manifest(ActualReceiving, disabled, untrusted));
        Assert.False(result.IsMatch);
        Assert.Equal(2, result.Mismatches.CheckConstraints);
    }

    [Fact]
    public void Real_semantic_change_still_produces_two_different_check_records()
    {
        var changed = ActualReceiving.Replace("[OutsidePoDecisionStatus]=(3)", "[OutsidePoDecisionStatus]=(4)", StringComparison.Ordinal);
        var result = DatabaseSchemaComparer.Compare(Manifest(ExpectedReceiving), Manifest(changed));
        Assert.False(result.IsMatch);
        Assert.Equal(2, result.Mismatches.CheckConstraints);
    }

    private static string Normalize(string sql)
        => DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(sql)!;

    private static DatabaseSchemaManifest Manifest(string sql, bool disabled = false, bool untrusted = false)
        => DatabaseSchemaCanonicalizer.WithFingerprint(
            ["fixture"],
            [new DatabaseTableSchema(
                new DatabaseObjectIdentity("dbo", "FixtureTable"),
                [], null, [], [],
                [new DatabaseCheckConstraintSchema("ck_fixture", Normalize(sql), disabled, untrusted)])],
            []);

    // Exercise the existing dispatcher in memory; no synthetic application migration,
    // DbContext construction, SQL connection or database write is involved.
    private sealed class OperationHarness
    {
        private static readonly Type Catalog = typeof(EfCoreDatabaseSchemaManifestCatalog);
        private static readonly Type TableType = Catalog.GetNestedType("MutableTable", BindingFlags.NonPublic)!;
        private readonly IDictionary _tables = (IDictionary)Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(typeof(DatabaseObjectIdentity), TableType))!;

        public void Apply(MigrationOperation operation)
        {
            try
            {
                Catalog.GetMethod("ApplyOperation", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, new object[]
                    {
                        "normalization-fixture", operation, _tables,
                        new Dictionary<DatabaseObjectIdentity, DatabaseSequenceSchema>(), "dbo"
                    });
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
        }

        public DatabaseTableSchema Table()
            => (DatabaseTableSchema)TableType.GetMethod("ToSchema")!
                .Invoke(_tables.Values.Cast<object>().Single(), null)!;
    }
}
