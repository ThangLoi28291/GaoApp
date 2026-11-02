using GaoApp.Infrastructure.Data.Migrations;

namespace GaoApp.Tests.Storefront;

// Independent fixtures from frozen APPV2 source and the actual trusted/enabled
// sys.check_constraints capture: tests/schema/533d2fc6effb4b4ea4288039237af308.
// No production SQL constants or normalization-map patterns are used here.
[Trait("Category", "StorefrontWeb05")]
public sealed class Web05SchemaExpressionTests
{
    [Theory]
    [InlineData("registry")][InlineData("consumption")][InlineData("time")]
    public void Observed_whole_CHECK_source_and_SQL_Server_signature_match_idempotently(string name)
    {
        var pair = Pair(name); var source = N(pair.Source); var persisted = N(pair.Persisted);
        Assert.Equal(source, persisted); Assert.Equal(source, N(source)); Assert.Equal(persisted, N(persisted));
        var comparison = DatabaseSchemaComparer.Compare(Manifest(pair.Source), Manifest(pair.Persisted)); Assert.True(comparison.IsMatch); Assert.Equal(0, comparison.Mismatches.Total);
        Assert.NotEqual(DatabaseSchemaNormalization.NormalizeSqlExpression(pair.Source), DatabaseSchemaNormalization.NormalizeSqlExpression(pair.Persisted));
    }

    [Theory]
    [InlineData("registry-channel0")][InlineData("registry-channel3")][InlineData("registry-missing-pos")][InlineData("registry-extra-web")][InlineData("registry-both-arms")]
    [InlineData("consumption-channel2")][InlineData("consumption-channel3")][InlineData("consumption-missing-pos")][InlineData("consumption-extra-web")][InlineData("consumption-weak-arm")]
    [InlineData("time-zero1")][InlineData("time-precision3")][InlineData("time-unknown-has-instant")][InlineData("time-known-no-day")][InlineData("time-both-arms")]
    public void Neighboring_channel_nullability_time_precision_and_boolean_meanings_remain_different(string name)
    {
        var (original, changed) = Mutation(name); Assert.NotEqual(original, changed); Assert.NotEqual(N(original), N(changed));
        Assert.False(DatabaseSchemaComparer.Compare(Manifest(original), Manifest(changed)).IsMatch);
    }

    [Theory]
    [InlineData("registry", true, false)][InlineData("registry", false, true)][InlineData("registry", true, true)]
    [InlineData("consumption", true, false)][InlineData("consumption", false, true)][InlineData("consumption", true, true)]
    [InlineData("time", true, false)][InlineData("time", false, true)][InlineData("time", true, true)]
    public void Equivalent_text_never_hides_disabled_or_untrusted_CHECK(string name, bool disabled, bool untrusted)
    {
        var pair = Pair(name); var comparison = DatabaseSchemaComparer.Compare(Manifest(pair.Source), Manifest(pair.Persisted, disabled, untrusted));
        Assert.False(comparison.IsMatch); Assert.Equal(2, comparison.Mismatches.CheckConstraints);
    }

    [Theory]
    [InlineData("'BankVerifiedReceipts'", "'bankverifiedreceipts'")]
    [InlineData("N'[Channel]=1 AND [PosQrRequestId] IS NOT NULL'", "N'[Channel]=1 AND [PosQrRequestId] IS NOT NULL '")]
    [InlineData("'O''Brien'", "'O''brien'")]
    [InlineData("N'[TimePrecision] IN (1,2)'", "N'([TimePrecision]=2 OR [TimePrecision]=1)'")]
    public void SQL_looking_literal_case_escaped_quotes_and_trailing_bytes_remain_distinct(string source, string changed)
        => Assert.NotEqual(N("[Note]=" + source), N("[Note]=" + changed));

    [Fact]
    public void Whole_known_predicate_inside_a_quoted_value_is_not_rewritten()
    {
        var quoted = "[Note]=N'" + SourceRegistry + "'";
        Assert.Equal(DatabaseSchemaNormalization.NormalizeSqlExpression(quoted), N(quoted)); Assert.Contains(SourceRegistry, N(quoted));
    }

    private static string N(string text) => DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(text)!;
    private static DatabaseSchemaManifest Manifest(string text, bool disabled = false, bool untrusted = false)
        => DatabaseSchemaCanonicalizer.WithFingerprint([], [new DatabaseTableSchema(new("dbo", "Web05ObservedFixture"), [], null, [], [], [new("CK_Observed", N(text), disabled, untrusted)])], []);
    private static (string Source, string Persisted) Pair(string name) => name switch
    { "registry" => (SourceRegistry, PersistedRegistry), "consumption" => (SourceConsumption, PersistedConsumption), "time" => (SourceTime, PersistedTime), _ => throw new ArgumentOutOfRangeException(nameof(name)) };
    private static (string Original, string Changed) Mutation(string name) => name switch
    {
        "registry-channel0" => (SourceRegistry, SourceRegistry.Replace("[Channel]=1", "[Channel]=0", StringComparison.Ordinal)),
        "registry-channel3" => (SourceRegistry, SourceRegistry.Replace("[Channel]=2", "[Channel]=3", StringComparison.Ordinal)),
        "registry-missing-pos" => (SourceRegistry, SourceRegistry.Replace("[PosQrRequestId] IS NOT NULL", "[PosQrRequestId] IS NULL", StringComparison.Ordinal)),
        "registry-extra-web" => (SourceRegistry, SourceRegistry.Replace("[StorefrontPaymentIntentId] IS NULL", "[StorefrontPaymentIntentId] IS NOT NULL", StringComparison.Ordinal)),
        "registry-both-arms" => (SourceRegistry, SourceRegistry.Replace(" OR ", " AND ", StringComparison.Ordinal)),
        "consumption-channel2" => (SourceConsumption, SourceConsumption.Replace("[Channel]=1", "[Channel]=2", StringComparison.Ordinal)),
        "consumption-channel3" => (SourceConsumption, SourceConsumption.Replace("[Channel]=2", "[Channel]=3", StringComparison.Ordinal)),
        "consumption-missing-pos" => (SourceConsumption, SourceConsumption.Replace("[PosPaymentId] IS NOT NULL", "[PosPaymentId] IS NULL", StringComparison.Ordinal)),
        "consumption-extra-web" => (SourceConsumption, SourceConsumption.Replace("[StorefrontAllocationId] IS NULL", "[StorefrontAllocationId] IS NOT NULL", StringComparison.Ordinal)),
        "consumption-weak-arm" => (SourceConsumption, SourceConsumption.Replace("[Channel]=1 AND", "[Channel]=1 OR", StringComparison.Ordinal)),
        "time-zero1" => (SourceTime, SourceTime.Replace("[TimePrecision]=0", "[TimePrecision]=1", StringComparison.Ordinal)),
        "time-precision3" => (SourceTime, SourceTime.Replace("IN (1,2)", "IN (1,3)", StringComparison.Ordinal)),
        "time-unknown-has-instant" => (SourceTime, SourceTime.Replace("[OccurredAtUtc] IS NULL", "[OccurredAtUtc] IS NOT NULL", StringComparison.Ordinal)),
        "time-known-no-day" => (SourceTime, SourceTime.Replace("[BankBusinessDate] IS NOT NULL", "[BankBusinessDate] IS NULL", StringComparison.Ordinal)),
        "time-both-arms" => (SourceTime, SourceTime.Replace(" OR ", " AND ", StringComparison.Ordinal)),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };
    private const string SourceRegistry = "([Channel]=1 AND [PosQrRequestId] IS NOT NULL AND [StorefrontPaymentIntentId] IS NULL) OR ([Channel]=2 AND [PosQrRequestId] IS NULL AND [StorefrontPaymentIntentId] IS NOT NULL)";
    private const string SourceConsumption = "([Channel]=1 AND [PosPaymentId] IS NOT NULL AND [StorefrontAllocationId] IS NULL) OR ([Channel]=2 AND [PosPaymentId] IS NULL AND [StorefrontAllocationId] IS NOT NULL)";
    private const string SourceTime = "([TimePrecision]=0 AND [OccurredAtUtc] IS NULL AND [BankBusinessDate] IS NULL) OR ([TimePrecision] IN (1,2) AND [OccurredAtUtc] IS NOT NULL AND [BankBusinessDate] IS NOT NULL)";
    private const string PersistedRegistry = "([Channel]=(1) AND [PosQrRequestId] IS NOT NULL AND [StorefrontPaymentIntentId] IS NULL OR [Channel]=(2) AND [PosQrRequestId] IS NULL AND [StorefrontPaymentIntentId] IS NOT NULL)";
    private const string PersistedConsumption = "([Channel]=(1) AND [PosPaymentId] IS NOT NULL AND [StorefrontAllocationId] IS NULL OR [Channel]=(2) AND [PosPaymentId] IS NULL AND [StorefrontAllocationId] IS NOT NULL)";
    private const string PersistedTime = "([TimePrecision]=(0) AND [OccurredAtUtc] IS NULL AND [BankBusinessDate] IS NULL OR ([TimePrecision]=(2) OR [TimePrecision]=(1)) AND [OccurredAtUtc] IS NOT NULL AND [BankBusinessDate] IS NOT NULL)";
}
