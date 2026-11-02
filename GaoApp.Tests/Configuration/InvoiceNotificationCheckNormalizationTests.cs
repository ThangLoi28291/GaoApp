using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Tests.Configuration;

public sealed class InvoiceNotificationCheckNormalizationTests
{
    public static IEnumerable<object[]> PersistedChecks()
        => InvoiceNotificationCheckSamples.Names.Select(name => new object[] { name, InvoiceNotificationCheckSamples.Persisted(name) });

    [Theory]
    [MemberData(nameof(PersistedChecks))]
    public void Exact_P3_migration_and_captured_SQL_Server_predicate_are_equivalent(string name, string persisted)
    {
        var migration = InvoiceNotificationCheckSamples.Migration(name);
        Assert.Equal(DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(migration),
            DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(persisted));
        // Index/default expressions retain their existing normalizer. The CHECK
        // exception must not silently become a general SQL rewriting rule.
        Assert.NotEqual(DatabaseSchemaNormalization.NormalizeSqlExpression(migration),
            DatabaseSchemaNormalization.NormalizeSqlExpression(persisted));
    }

    [Fact]
    public void Prior_generic_expression_path_reports_two_changed_CHECKs_as_four_records()
    {
        var expected = InvoiceNotificationCheckSamples.Names.Select(name => name + "|" +
            DatabaseSchemaNormalization.NormalizeSqlExpression(InvoiceNotificationCheckSamples.Migration(name))).ToHashSet();
        var actual = InvoiceNotificationCheckSamples.Names.Select(name => name + "|" +
            DatabaseSchemaNormalization.NormalizeSqlExpression(InvoiceNotificationCheckSamples.Persisted(name))).ToHashSet();
        Assert.Equal(2, expected.Except(actual).Count());
        Assert.Equal(2, actual.Except(expected).Count());
        Assert.Equal(4, expected.Except(actual).Count() + actual.Except(expected).Count());
    }

    public static IEnumerable<object[]> ChangedPredicates()
    {
        var source = InvoiceNotificationCheckSamples.PersistedSource;
        var state = InvoiceNotificationCheckSamples.PersistedState;
        yield return [InvoiceNotificationCheckSamples.SourceName, source.Replace("[SourceType]='Invoice'", "[SourceType]='InputInvoice' OR [SourceType]='Invoice'", StringComparison.Ordinal)];
        yield return [InvoiceNotificationCheckSamples.SourceName, source.Replace("[SourceType]='Invoice' OR ", "", StringComparison.Ordinal)];
        yield return [InvoiceNotificationCheckSamples.SourceName, source.Replace("[SourceId]", "[CustomerId]", StringComparison.Ordinal)];
        yield return [InvoiceNotificationCheckSamples.SourceName, source.Replace("[StoreId]>(0)", "[StoreId]>=(0)", StringComparison.Ordinal)];
        yield return [InvoiceNotificationCheckSamples.SourceName, source.Replace("AND ([SourceType]", "OR ([SourceType]", StringComparison.Ordinal)];
        yield return [InvoiceNotificationCheckSamples.SourceName, source.Replace("AND ([Kind]", "AND [Kind]", StringComparison.Ordinal).Replace("'Receipt'))", "'Receipt')", StringComparison.Ordinal)];
        yield return [InvoiceNotificationCheckSamples.SourceName, source.Replace("[Kind]='Receipt'", "[Kind]='RetailReceipt'", StringComparison.Ordinal)];
        yield return [InvoiceNotificationCheckSamples.StateName, state.Replace("[State]='Cancelled'", "[State]='Queued' OR [State]='Cancelled'", StringComparison.Ordinal)];
        yield return [InvoiceNotificationCheckSamples.StateName, state.Replace("[State]='Cancelled' OR ", "", StringComparison.Ordinal)];
        yield return [InvoiceNotificationCheckSamples.StateName, state.Replace("[State]", "[Kind]", StringComparison.Ordinal)];
        yield return [InvoiceNotificationCheckSamples.StateName, state.Replace(" OR ", " AND ", StringComparison.Ordinal)];
    }

    [Theory]
    [MemberData(nameof(ChangedPredicates))]
    public void A_changed_literal_column_operator_or_logical_group_is_not_equivalent(string name, string changed)
    {
        Assert.NotEqual(InvoiceNotificationCheckSamples.Persisted(name), changed);
        Assert.NotEqual(DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(InvoiceNotificationCheckSamples.Migration(name)),
            DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(changed));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void Equivalent_text_never_hides_disabled_or_untrusted_CHECK_flags(bool disabled, bool untrusted, bool matches)
    {
        DatabaseSchemaManifest Manifest(string text, bool isDisabled, bool isNotTrusted)
            => DatabaseSchemaCanonicalizer.WithFingerprint([], [new DatabaseTableSchema(
                new("dbo", "InvoiceNotificationOutbox"), [], null, [], [],
                [new(InvoiceNotificationCheckSamples.SourceName,
                    DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(text)!, isDisabled, isNotTrusted)])], []);
        var comparison = DatabaseSchemaComparer.Compare(
            Manifest(InvoiceNotificationCheckSamples.Migration(InvoiceNotificationCheckSamples.SourceName), false, false),
            Manifest(InvoiceNotificationCheckSamples.PersistedSource, disabled, untrusted));
        Assert.Equal(matches, comparison.IsMatch);
        Assert.Equal(matches ? 0 : 2, comparison.Mismatches.CheckConstraints);
        Assert.Equal(comparison.Mismatches.CheckConstraints, comparison.Mismatches.Total);
    }
}

internal static class InvoiceNotificationCheckSamples
{
    internal const string SourceName = "CK_InvoiceNotificationOutbox_Source";
    internal const string StateName = "CK_InvoiceNotificationOutbox_State";
    internal static readonly string[] Names = [SourceName, StateName];
    // Captured from SQL Server sys.check_constraints, not a hand-expanded parser output.
    internal const string PersistedSource = "([StoreId]>(0) AND [SourceId]>(0) AND ([SourceType]='Invoice' OR [SourceType]='Order') AND ([Kind]='DraftPreview' OR [Kind]='CorrectionLink' OR [Kind]='CorrectionXml' OR [Kind]='CorrectionZipXml' OR [Kind]='CorrectionPdf' OR [Kind]='OfficialLink' OR [Kind]='OfficialXml' OR [Kind]='OfficialZipXml' OR [Kind]='OfficialPdf' OR [Kind]='Receipt'))";
    internal const string PersistedState = "([State]='Cancelled' OR [State]='Blocked' OR [State]='Failed' OR [State]='NeedsReview' OR [State]='PartiallySent' OR [State]='Sent' OR [State]='Accepted' OR [State]='Dispatching' OR [State]='Pending')";
    internal static string Persisted(string name) => name == SourceName ? PersistedSource : PersistedState;
    internal static string Migration(string name) => new AddInvoiceNotificationsP3().UpOperations
        .OfType<AddCheckConstraintOperation>().Single(operation => operation.Name == name).Sql;
}
