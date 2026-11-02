using GaoApp.Infrastructure.Data.Migrations;

namespace GaoApp.Tests.Storefront;

// Independent observed fixtures: source bytes archived at targeted-v3/source-evidence
// and SQL Server sys.check_constraints captured before the failing fixture disposed.
// These do not call production SQL constants or reuse a compatibility-map regex.
[Trait("Category", "StorefrontWeb04")]
public sealed class Web04SchemaExpressionTests
{
    [Theory]
    [InlineData("delivery")][InlineData("reservation")][InlineData("price")][InlineData("rounding")]
    public void Observed_source_and_trusted_SQL_Server_whole_predicate_match_and_normalization_is_idempotent(string name)
    {
        var (source, persisted) = Pair(name); var normalized = N(source);
        Assert.Equal(normalized, N(persisted)); Assert.Equal(normalized, N(normalized)); Assert.Equal(N(persisted), N(N(persisted)));
        var compared = DatabaseSchemaComparer.Compare(Manifest(source), Manifest(persisted)); Assert.True(compared.IsMatch); Assert.Equal(0, compared.Mismatches.Total);
        // CHECK-only compatibility must leave the existing generic path distinct.
        Assert.NotEqual(DatabaseSchemaNormalization.NormalizeSqlExpression(source), DatabaseSchemaNormalization.NormalizeSqlExpression(persisted));
    }
    public static IEnumerable<object[]> ChangedNames() => Changes().Select(x => new object[] { x.Name });
    [Theory, MemberData(nameof(ChangedNames))]
    public void Changed_status_scope_budget_rounding_discount_binary_or_boolean_meaning_remains_different(string name)
    {
        var mutation = Changes().Single(x => x.Name == name); Assert.NotEqual(mutation.Original, mutation.Changed);
        Assert.NotEqual(N(mutation.Original), N(mutation.Changed)); Assert.False(DatabaseSchemaComparer.Compare(Manifest(mutation.Original), Manifest(mutation.Changed)).IsMatch);
    }
    public static IEnumerable<object[]> FlagChanges()
    {
        foreach (var name in new[] { "delivery", "reservation", "price", "rounding" })
            foreach (var flags in new[] { (true, false), (false, true), (true, true) }) yield return [name, flags.Item1, flags.Item2];
    }
    [Theory, MemberData(nameof(FlagChanges))]
    public void Exact_text_compatibility_never_hides_disabled_or_untrusted_CHECK(string name, bool disabled, bool untrusted)
    {
        var pair = Pair(name); var compared = DatabaseSchemaComparer.Compare(Manifest(pair.Source), Manifest(pair.Persisted, disabled, untrusted));
        Assert.False(compared.IsMatch); Assert.Equal(2, compared.Mismatches.CheckConstraints);
    }
    [Theory]
    [InlineData("'OrderCreated'", "'ordercreated'")][InlineData("'OrderCreated'", "'OrderCreated '")]
    [InlineData("N'AND (A=1 OR B=2)'", "N'AND (A=1 OR B=2) '")][InlineData("'O''Brien'", "'O''brien'")]
    public void Quoted_case_escaped_quotes_SQL_like_text_and_trailing_bytes_remain_significant(string first, string changed)
        => Assert.NotEqual(N("[Kind]=" + first), N("[Kind]=" + changed));
    [Fact]
    public void Known_constraint_words_inside_a_literal_are_not_rewritten_as_an_arithmetic_predicate()
    {
        const string literal = "[Note]=N'[RoundingAdjustment] BETWEEN -201 AND 201 AND [RoundingAdjustment] = ROUND([RoundingAdjustment],0)'";
        Assert.Equal(DatabaseSchemaNormalization.NormalizeSqlExpression(literal), N(literal)); Assert.Contains("BETWEEN -201 AND 201", N(literal));
    }
    private static string N(string value) => DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(value)!;
    private static DatabaseSchemaManifest Manifest(string text, bool disabled = false, bool untrusted = false)
        => DatabaseSchemaCanonicalizer.WithFingerprint([], [new DatabaseTableSchema(new("dbo", "Web04ObservedFixture"), [], null, [], [], [new("CK_Observed", N(text), disabled, untrusted)])], []);
    private static (string Source, string Persisted) Pair(string name) => name switch
    { "delivery" => (SourceDelivery, PersistedDelivery), "reservation" => (SourceReservation, PersistedReservation), "price" => (SourcePrice, PersistedPrice), "rounding" => (SourceRounding, PersistedRounding), _ => throw new ArgumentOutOfRangeException(nameof(name)) };
    private sealed record Mutation(string Name, string Original, string Changed);
    private static IEnumerable<Mutation> Changes()
    {
        yield return new("delivery-wrong-web-reference", PersistedDelivery, PersistedDelivery.Replace("[ReferenceType]=(12)", "[ReferenceType]=(13)", StringComparison.Ordinal));
        yield return new("delivery-status4", PersistedDelivery, PersistedDelivery.Replace("[Status]=(3)", "[Status]=(4)", StringComparison.Ordinal));
        yield return new("delivery-null-branch-OR", PersistedDelivery, PersistedDelivery.Replace("AND [DeliveryOrderLineId] IS NULL", "OR [DeliveryOrderLineId] IS NULL", StringComparison.Ordinal));
        yield return new("reservation-wrong-reference", PersistedReservation, PersistedReservation.Replace("[ReferenceType]<>(12)", "[ReferenceType]<>(11)", StringComparison.Ordinal));
        yield return new("reservation-status4", PersistedReservation, PersistedReservation.Replace("[Status]=(3)", "[Status]=(4)", StringComparison.Ordinal));
        yield return new("reservation-zero-quantity", PersistedReservation, PersistedReservation.Replace("[ReservedQty]>(0)", "[ReservedQty]>=(0)", StringComparison.Ordinal));
        yield return new("reservation-no-binary-reference", PersistedReservation, PersistedReservation.Replace("CONVERT([varbinary](max),[ReferenceId])", "[ReferenceId]", StringComparison.Ordinal));
        yield return new("reservation-no-reference-length", PersistedReservation, PersistedReservation.Replace(" AND datalength([ReferenceId])=datalength(CONVERT([nvarchar](50),[StorefrontOrderId]))", "", StringComparison.Ordinal));
        yield return new("price-negative-adjustment-sign", PersistedPrice, PersistedPrice.Replace("+[RoundingAdjustment]", "-[RoundingAdjustment]", StringComparison.Ordinal));
        yield return new("price-positive-discount-sign", PersistedPrice, PersistedPrice.Replace("-[Discount]", "+[Discount]", StringComparison.Ordinal));
        yield return new("price-reversed-discount-bound", PersistedPrice, PersistedPrice.Replace("[Discount]<=", "[Discount]>=", StringComparison.Ordinal));
        yield return new("price-two-decimal-rounding", PersistedPrice, PersistedPrice.Replace("round([Quantity]*[UnitPrice],(0))", "round([Quantity]*[UnitPrice],(2))", StringComparison.Ordinal));
        yield return new("price-OR-instead-AND", PersistedPrice, PersistedPrice.Replace(" AND ", " OR ", StringComparison.Ordinal));
        yield return new("rounding-lower-budget202", PersistedRounding, PersistedRounding.Replace("(-201)", "(-202)", StringComparison.Ordinal));
        yield return new("rounding-upper-budget200", PersistedRounding, PersistedRounding.Replace("<=(201)", "<=(200)", StringComparison.Ordinal));
        yield return new("rounding-fractional-policy", PersistedRounding, PersistedRounding.Replace("[RoundingAdjustment]=round", "[RoundingAdjustment]>=round", StringComparison.Ordinal));
    }
    private const string SourceDelivery = """([ReferenceType]<>11 AND [DeliveryOrderId] IS NULL AND [DeliveryOrderLineId] IS NULL AND ([SourceLegalEntityId] IS NULL OR [ReferenceType]=12) AND [DeliveryRevision] IS NULL AND [ReportedByUserId] IS NULL AND [ReportedAtUtc] IS NULL) OR ([ReferenceType]=11 AND [IsDeleted]=0 AND [DeliveryOrderId] IS NOT NULL AND [DeliveryOrderLineId] IS NOT NULL AND [SourceLegalEntityId] IS NOT NULL AND [DeliveryRevision] IS NOT NULL AND [DeliveryRevision]>0 AND [ReportedByUserId] IS NOT NULL AND [ReportedAtUtc] IS NOT NULL AND [ReservedQty]>0 AND [ReferenceLineId] IS NOT NULL AND [ReferenceLineId]=[DeliveryOrderLineId] AND [ReferenceId]=CONVERT(nvarchar(50),[DeliveryOrderId]) AND ([Status]=1 OR [Status]=2 OR [Status]=3))""";
    private const string SourceReservation = """([ReferenceType]<>12 AND [StorefrontOrderId] IS NULL AND [StorefrontOrderLineId] IS NULL AND [StorefrontFulfillmentSourceId] IS NULL) OR ([ReferenceType]=12 AND [IsDeleted]=0 AND [StorefrontOrderId] IS NOT NULL AND [StorefrontOrderLineId] IS NOT NULL AND [StorefrontFulfillmentSourceId] IS NOT NULL AND [SourceLegalEntityId] IS NOT NULL AND [ReservedQty]>0 AND [ReferenceLineId]=[StorefrontOrderLineId] AND [ReferenceLineId] IS NOT NULL AND CONVERT(varbinary(max),[ReferenceId])=CONVERT(varbinary(max),CONVERT(nvarchar(50),[StorefrontOrderId])) AND DATALENGTH([ReferenceId])=DATALENGTH(CONVERT(nvarchar(50),[StorefrontOrderId])) AND [Status] IN (1,2,3))""";
    private const string SourcePrice = """ROUND([Quantity] * [UnitPrice],0)+[RoundingAdjustment]>=0 AND [Discount]<=ROUND([Quantity] * [UnitPrice],0)+[RoundingAdjustment] AND [NetAmount]=ROUND([Quantity] * [UnitPrice],0)+[RoundingAdjustment]-[Discount]""";
    private const string SourceRounding = """[RoundingAdjustment] BETWEEN -201 AND 201 AND [RoundingAdjustment] = ROUND([RoundingAdjustment],0)""";
    private const string PersistedDelivery = """([ReferenceType]<>(11) AND [DeliveryOrderId] IS NULL AND [DeliveryOrderLineId] IS NULL AND ([SourceLegalEntityId] IS NULL OR [ReferenceType]=(12)) AND [DeliveryRevision] IS NULL AND [ReportedByUserId] IS NULL AND [ReportedAtUtc] IS NULL OR [ReferenceType]=(11) AND [IsDeleted]=(0) AND [DeliveryOrderId] IS NOT NULL AND [DeliveryOrderLineId] IS NOT NULL AND [SourceLegalEntityId] IS NOT NULL AND [DeliveryRevision] IS NOT NULL AND [DeliveryRevision]>(0) AND [ReportedByUserId] IS NOT NULL AND [ReportedAtUtc] IS NOT NULL AND [ReservedQty]>(0) AND [ReferenceLineId] IS NOT NULL AND [ReferenceLineId]=[DeliveryOrderLineId] AND [ReferenceId]=CONVERT([nvarchar](50),[DeliveryOrderId]) AND ([Status]=(1) OR [Status]=(2) OR [Status]=(3)))""";
    private const string PersistedReservation = """([ReferenceType]<>(12) AND [StorefrontOrderId] IS NULL AND [StorefrontOrderLineId] IS NULL AND [StorefrontFulfillmentSourceId] IS NULL OR [ReferenceType]=(12) AND [IsDeleted]=(0) AND [StorefrontOrderId] IS NOT NULL AND [StorefrontOrderLineId] IS NOT NULL AND [StorefrontFulfillmentSourceId] IS NOT NULL AND [SourceLegalEntityId] IS NOT NULL AND [ReservedQty]>(0) AND [ReferenceLineId]=[StorefrontOrderLineId] AND [ReferenceLineId] IS NOT NULL AND CONVERT([varbinary](max),[ReferenceId])=CONVERT([varbinary](max),CONVERT([nvarchar](50),[StorefrontOrderId])) AND datalength([ReferenceId])=datalength(CONVERT([nvarchar](50),[StorefrontOrderId])) AND ([Status]=(3) OR [Status]=(2) OR [Status]=(1)))""";
    private const string PersistedPrice = """((round([Quantity]*[UnitPrice],(0))+[RoundingAdjustment])>=(0) AND [Discount]<=(round([Quantity]*[UnitPrice],(0))+[RoundingAdjustment]) AND [NetAmount]=((round([Quantity]*[UnitPrice],(0))+[RoundingAdjustment])-[Discount]))""";
    private const string PersistedRounding = """([RoundingAdjustment]>=(-201) AND [RoundingAdjustment]<=(201) AND [RoundingAdjustment]=round([RoundingAdjustment],(0)))""";
}
