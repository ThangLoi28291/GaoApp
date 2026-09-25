using FluentAssertions;
using GaoApp.Infrastructure.Data.Migrations;

namespace GaoApp.Tests.Configuration;

public sealed class DatabaseSchemaNormalizationTests
{
    private const string DepositFilter = "[OrderId] IS NOT NULL AND [Kind] IN ('Apply','Void')";

    [Theory]
    [InlineData("[OrderId] IS NOT NULL AND ([Kind] IN ('Apply','Void'))")]
    [InlineData("([OrderId] IS NOT NULL AND ([Kind] IN ('Apply','Void')))")]
    [InlineData("(([OrderId] IS NOT NULL AND (([Kind] IN ('Apply','Void')))))")]
    [InlineData(" OrderId is not null and ( Kind in ( 'Apply', 'Void' ) ) ")]
    [InlineData(" ( ( OrderId is not null and ( ( Kind in ('Apply','Void') ) ) ) ) ")]
    [InlineData("[OrderId] IS NOT NULL AND\n([Kind] IN (\n'Apply',\n'Void'))")]
    public void Sql_server_redundant_in_parentheses_match_the_migration(string actual)
    {
        var expected = DatabaseSchemaNormalization.NormalizeSqlExpression(DepositFilter);
        expected.Should().Be("orderidisnotnullandkindin('apply','void')");
        DatabaseSchemaNormalization.NormalizeSqlExpression(actual).Should().Be(expected);
    }

    [Theory]
    [InlineData("[OrderId] IS NOT NULL AND ([Kind] IN ('Apply'))")]
    [InlineData("[OrderId] IS NOT NULL AND ([Kind] IN ('Apply','Void','Restore'))")]
    [InlineData("[OrderId] IS NOT NULL AND ([Kind] NOT IN ('Apply','Void'))")]
    [InlineData("[OrderId] IS NULL AND ([Kind] IN ('Apply','Void'))")]
    [InlineData("[OrderId] IS NOT NULL OR ([Kind] IN ('Apply','Void'))")]
    [InlineData("[CustomerId] IS NOT NULL AND ([Kind] IN ('Apply','Void'))")]
    [InlineData("[OrderId] IS NOT NULL AND ([OtherKind] IN ('Apply','Void'))")]
    [InlineData("[OrderId] IS NOT NULL AND ([Kind] IN ('Apply','Void') OR [Kind] = 'Restore')")]
    public void Changed_predicate_semantics_remain_different(string changed)
        => DatabaseSchemaNormalization.NormalizeSqlExpression(changed).Should()
            .NotBe(DatabaseSchemaNormalization.NormalizeSqlExpression(DepositFilter));

    [Theory]
    [InlineData("[OrderId] IS NOT NULL AND ([Kind] IN ('Apply','Void') OR [Kind] = 'Restore')",
        "[OrderId] IS NOT NULL AND [Kind] IN ('Apply','Void') OR [Kind] = 'Restore'")]
    [InlineData("[OrderId] IS NOT NULL AND ([Kind] IN ('Apply','Void') AND [Active] = 1) OR [Override] = 1",
        "[OrderId] IS NOT NULL AND ([Kind] IN ('Apply','Void') AND ([Active] = 1 OR [Override] = 1))")]
    public void Logical_grouping_is_not_discarded(string first, string second)
        => DatabaseSchemaNormalization.NormalizeSqlExpression(first).Should()
            .NotBe(DatabaseSchemaNormalization.NormalizeSqlExpression(second));

    [Theory]
    [InlineData("'Apply)','Vo''id('")]
    [InlineData("N'Apply',N'Void'")]
    [InlineData("'text AND (x IN (''y''))','Void'")]
    public void Literal_contents_do_not_determine_parenthesis_boundaries(string literals)
    {
        var bare = $"[OrderId] IS NOT NULL AND [Kind] IN ({literals})";
        var wrapped = $"([OrderId] IS NOT NULL AND ([Kind] IN ({literals})))";
        DatabaseSchemaNormalization.NormalizeSqlExpression(wrapped).Should()
            .Be(DatabaseSchemaNormalization.NormalizeSqlExpression(bare));
    }

    [Fact]
    public void Sql_like_text_inside_a_literal_is_not_rewritten()
        => DatabaseSchemaNormalization.NormalizeSqlExpression(
                "'[OrderId] IS NOT NULL AND ([Kind] IN (''Apply'',''Void''))'")
            .Should().Be("'orderidisnotnulland(kindin(''apply'',''void''))'");
}
