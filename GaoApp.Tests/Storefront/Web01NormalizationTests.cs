using GaoApp.Infrastructure.Data.Migrations;

namespace GaoApp.Tests.Storefront;

[Trait("Category", "StorefrontWeb01")]
public sealed class Web01NormalizationTests
{
    [Theory]
    [InlineData("[Kind] IN (0,1)", "([Kind]=(0) OR [Kind]=(1))")]
    [InlineData("[Phase] BETWEEN 0 AND 6", "([Phase]>=(0) AND [Phase]<=(6))")]
    [InlineData("[TotalDue]=[GoodsAmount]+[ShippingCharge]", "[TotalDue]=([GoodsAmount]+[ShippingCharge])")]
    [InlineData("[QrEnabled]=0 OR ([BankAccountId] IS NOT NULL AND [ReceivingLegalEntityId] IS NOT NULL AND [QrHoldMinutes] IS NOT NULL AND LEN(LTRIM(RTRIM([BankTerminalCode]))) > 0 AND LEN(LTRIM(RTRIM([BankUserCode]))) > 0)", "[QrEnabled]=(0) OR [BankAccountId] IS NOT NULL AND [ReceivingLegalEntityId] IS NOT NULL AND [QrHoldMinutes] IS NOT NULL AND LEN(LTRIM(RTRIM([BankTerminalCode])))>(0) AND LEN(LTRIM(RTRIM([BankUserCode])))>(0)")]
    public void Equivalent_source_and_SQL_Server_forms_have_same_canonical_check(string source, string stored)
    {
        var canonical = DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(source);
        Assert.Equal(canonical, DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(stored));
        Assert.Equal(canonical, DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(canonical));
        Assert.Equal(canonical, DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(DatabaseSchemaNormalization.NormalizeSqlExpression(source)));
        Assert.Equal(canonical, DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(DatabaseSchemaNormalization.NormalizeSqlExpression(stored)));
    }

    [Theory]
    [InlineData("[Kind] IN (0,1)", "([Kind]=0 OR [Kind]=2)")]
    [InlineData("[Phase] BETWEEN 0 AND 6", "[Phase]>=0 AND [Phase]<=7")]
    [InlineData("[Amount]>0", "[Amount]>=0")]
    [InlineData("[A]=1 AND ([B]=1 OR [C]=1)", "([A]=1 AND [B]=1) OR [C]=1")]
    [InlineData("[A]=1 OR [B]=1 AND [C]=1", "([A]=1 OR [B]=1) AND [C]=1")]
    [InlineData("[TotalDue]=[GoodsAmount]+[ShippingCharge]", "[TotalDue]=[GoodsAmount]-[ShippingCharge]")]
    [InlineData("[Amount]=([Goods]+[Ship])+[Extra]", "[Amount]=[Goods]+([Ship]-[Extra])")]
    [InlineData("[Status] IN ('Pending','Review')", "[Status] IN ('Pending','Paid')")]
    [InlineData("[Code] LIKE '[A-F]%'", "[Code] LIKE 'A-F%'")]
    [InlineData("[Code] LIKE '[A-F]%' COLLATE Latin1_General_100_BIN2", "[Code] LIKE '[a-f]%' COLLATE Latin1_General_100_BIN2")]
    public void Changed_bounds_enum_arithmetic_or_grouping_remain_detectable(string expected, string corrupt)
        => Assert.NotEqual(DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(expected), DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(corrupt));

    [Theory]
    [InlineData("[IsCurrent]=1", "[IsCurrent]=0")]
    [InlineData("[IsCurrent]=1", null)]
    [InlineData("[StoreId] IS NOT NULL AND [DeletedAtUtc] IS NULL", "[StoreId] IS NOT NULL")]
    public void Index_filter_corruption_remains_a_manifest_difference(string filter, string? corrupt)
    {
        var expected = Manifest(filter); var actual = Manifest(corrupt);
        Assert.False(DatabaseSchemaComparer.Compare(expected, actual).IsMatch);
    }
    [Theory]
    [InlineData("Pending")]
    [InlineData("APPLY")]
    [InlineData("ACB")]
    [InlineData("O'Brien")]
    public void String_defaults_keep_case_and_escape_quotes_while_unicode_prefix_is_canonical(string value)
    {
        var literal = "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        Assert.Equal(literal, DatabaseSchemaNormalization.NormalizeDefaultValue(value));
        Assert.Equal(literal, DatabaseSchemaNormalization.NormalizeDefaultExpression("(N" + literal + ")"));
        Assert.Equal(literal, DatabaseSchemaNormalization.NormalizeDefaultExpression("(" + literal + ")"));
        Assert.NotEqual(DatabaseSchemaNormalization.NormalizeDefaultExpression("'Pending'"), DatabaseSchemaNormalization.NormalizeDefaultExpression("'pending'"));
    }
    private static DatabaseSchemaManifest Manifest(string? filter)
        => DatabaseSchemaCanonicalizer.WithFingerprint(["synthetic"],
            [new DatabaseTableSchema(new("dbo", "StorefrontFilterProbe"), [], null, [],
                [new DatabaseIndexSchema("ix_probe", [new("id", false)], [], true, false, false, false,
                    DatabaseSchemaNormalization.NormalizeSqlExpression(filter))], [])], []);
}
