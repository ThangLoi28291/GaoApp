using GaoApp.Infrastructure.Data.Migrations;

namespace GaoApp.Tests.Storefront;

// Independent frozen APPV2 actual SQL capture: WEB07/tests/schema/
// 9417fee644324fdabc9e94ea63e4e86c/exact-check-source-persisted-pairs.json.
// These are captured model signatures and raw sys.check_constraints, not
// production normalization dictionary entries. No SQL/fixture during discovery.
[Trait("Category", "StorefrontWeb07")]
public sealed class Web07SchemaExpressionTests
{
    [Theory]
    [InlineData("decision")] [InlineData("payout")] [InlineData("funding")]
    public void Actual_whole_predicates_match_idempotently_but_quoted_copies_are_not_aliased(string name)
    {
        var pair = Pair(name); var source = N(pair.Source); var actual = N(pair.Persisted);
        Assert.Equal(source, actual); Assert.Equal(source, N(source)); Assert.Equal(actual, N(actual));
        Assert.NotEqual(DatabaseSchemaNormalization.NormalizeSqlExpression(pair.Source), DatabaseSchemaNormalization.NormalizeSqlExpression(pair.Persisted));
        var result = DatabaseSchemaComparer.Compare(Manifest(pair, pair.Source), Manifest(pair, pair.Persisted));
        Assert.True(result.IsMatch); Assert.Equal(0, result.Mismatches.Total);
        var literal = "[Note]=N'" + pair.Persisted.Replace("'", "''", StringComparison.Ordinal) + "'";
        Assert.Equal(DatabaseSchemaNormalization.NormalizeSqlExpression(literal), N(literal));
    }

    [Theory]
    [InlineData("decision", true, false)] [InlineData("decision", false, true)] [InlineData("decision", true, true)]
    [InlineData("payout", true, false)] [InlineData("payout", false, true)] [InlineData("payout", true, true)]
    [InlineData("funding", true, false)] [InlineData("funding", false, true)] [InlineData("funding", true, true)]
    public void Canonical_equivalence_cannot_hide_disabled_untrusted_or_both_CHECK_flags(string name, bool disabled, bool untrusted)
    {
        var pair = Pair(name);
        var result = DatabaseSchemaComparer.Compare(Manifest(pair, pair.Source), Manifest(pair, pair.Persisted, disabled, untrusted));
        Assert.False(result.IsMatch); Assert.Equal(2, result.Mismatches.CheckConstraints);
    }

    [Theory]
    [InlineData("decision", "kind")] [InlineData("decision", "required-return")] [InlineData("decision", "and-or")]
    [InlineData("payout", "object")] [InlineData("payout", "required-owner")] [InlineData("payout", "and-or")]
    [InlineData("payout", "literal")] [InlineData("payout", "character-code")]
    [InlineData("funding", "kind")] [InlineData("funding", "required-owner")] [InlineData("funding", "and-or")]
    public void Semantic_neighbors_of_both_observed_forms_remain_distinct_and_comparer_rejected(string name, string defect)
    {
        var pair = Pair(name);
        foreach (var persisted in new[] { false, true })
        {
            var original = persisted ? pair.Persisted : pair.Source;
            var changed = Change(original, name, defect, persisted);
            Assert.NotEqual(original, changed); Assert.NotEqual(N(original), N(changed));
            Assert.False(DatabaseSchemaComparer.Compare(Manifest(pair, original), Manifest(pair, changed)).IsMatch);
        }
    }

    [Theory]
    [InlineData("N'{'", "N'['")] [InlineData("'cash'", "'Cash'")]
    [InlineData("'bank'", "'bank '")] [InlineData("N'[FundingId] IS NOT NULL'", "N'[FundingId] IS NULL'")]
    public void Quoted_object_marker_case_trailing_bytes_and_SQL_looking_literals_stay_distinct(string original, string changed)
        => Assert.NotEqual(N("[Note]=" + original), N("[Note]=" + changed));

    private static string Change(string value, string name, string defect, bool persisted)
    {
        var (oldValue, newValue) = (name, defect, persisted) switch
        {
            ("decision", "kind", false) => ("kind=4", "kind=5"),
            ("decision", "kind", true) => ("[Kind]=(4)", "[Kind]=(5)"),
            ("decision", "required-return", false) => ("actualreturnevidenceidisnotnull", "actualreturnevidenceidisnull"),
            ("decision", "required-return", true) => ("[ActualReturnEvidenceId] IS NOT NULL", "[ActualReturnEvidenceId] IS NULL"),
            ("decision", "and-or", false) => ("len(reason)>0and(", "len(reason)>0or("),
            ("decision", "and-or", true) => ("len([Reason])>(0) AND", "len([Reason])>(0) OR"),
            ("payout", "object", false) => ("=n'{'", "=n'['"),
            ("payout", "object", true) => ("=N'{'", "=N'['"),
            ("payout", "required-owner", false) => ("payerlegalentityidisnotnull", "payerlegalentityidisnull"),
            ("payout", "required-owner", true) => ("[PayerLegalEntityId] IS NOT NULL", "[PayerLegalEntityId] IS NULL"),
            ("payout", "and-or", false) => (")or(fundingidisnotnull", ")and(fundingidisnotnull"),
            ("payout", "and-or", true) => ("[SourceSnapshotJson] IS NULL OR", "[SourceSnapshotJson] IS NULL AND"),
            ("payout", "literal", false) => ("n'cash'", "n'Cash'"),
            ("payout", "literal", true) => ("N'cash'", "N'Cash'"),
            ("payout", "character-code", false) => ("nchar9", "nchar8"),
            ("payout", "character-code", true) => ("nchar((9))", "nchar((8))"),
            ("funding", "kind", false) => ("kind=2", "kind=3"),
            ("funding", "kind", true) => ("[Kind]=(2)", "[Kind]=(3)"),
            ("funding", "required-owner", false) => ("receivinglegalentityidisnotnull", "receivinglegalentityidisnull"),
            ("funding", "required-owner", true) => ("[ReceivingLegalEntityId] IS NOT NULL", "[ReceivingLegalEntityId] IS NULL"),
            ("funding", "and-or", false) => (")andisjson", ")orisjson"),
            ("funding", "and-or", true) => (") AND isjson", ") OR isjson"),
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        return value.Replace(oldValue, newValue, StringComparison.Ordinal);
    }
    private sealed record CapturedPair(string Name, string Table, string Constraint, string Source, string Persisted);
    private static CapturedPair Pair(string name) => Pairs.Single(x => x.Name == name);
    private static string N(string text) => DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(text)!;
    private static DatabaseSchemaManifest Manifest(CapturedPair p, string expression, bool disabled = false, bool untrusted = false)
        => DatabaseSchemaCanonicalizer.WithFingerprint([], [new DatabaseTableSchema(new("dbo", p.Table), [], null, [], [], [new(p.Constraint, N(expression), disabled, untrusted)])], []);
    private static readonly CapturedPair[] Pairs =
    [
        new(@"decision",@"storefrontcancellationdecisions",@"ck_storefrontcancellationdecisions_decision",@"len(reason)>0and((kind=1andactualdeliveryevidenceidisnullandactualreturnevidenceidisnull)or(kind=2andauthorisesphysicalreturn=0andactualdeliveryevidenceidisnullandactualreturnevidenceidisnull)or(kind=3andauthorisesphysicalreturn=0andactualdeliveryevidenceidisnotnullandactualreturnevidenceidisnull)or(kind=4andauthorisesphysicalreturn=0andactualdeliveryevidenceidisnullandactualreturnevidenceidisnotnull))",@"(len([Reason])>(0) AND ([Kind]=(1) AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NULL OR [Kind]=(2) AND [AuthorisesPhysicalReturn]=(0) AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NULL OR [Kind]=(3) AND [AuthorisesPhysicalReturn]=(0) AND [ActualDeliveryEvidenceId] IS NOT NULL AND [ActualReturnEvidenceId] IS NULL OR [Kind]=(4) AND [AuthorisesPhysicalReturn]=(0) AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NOT NULL))"),
        new(@"payout",@"storefrontrefundevidence",@"ck_storefrontrefundevidence_nativepayout",@"(fundingidisnullandpayoutsourcekindisnullandtreasuryentryidisnullandposshiftcashtransactionidisnullandpayerbankaccountidisnullandpayerlegalentityidisnullandfundisnullandverificationmethodisnullandevidencereferenceisnullandprotectedpayoutevidenceisnullandsourcesnapshotjsonisnull)or(fundingidisnotnullandpayerlegalentityidisnotnullandfundisnotnulland(fund=n'cash'orfund=n'bank')andverificationmethodisnotnullandevidencereferenceisnotnullandlen(evidencereference)>0andprotectedpayoutevidenceisnotnullanddatalength(protectedpayoutevidence)>0andsourcesnapshotjsonisnotnullandisjson(sourcesnapshotjson)=1andleft(ltrim(replace(replace(replace(sourcesnapshotjson,nchar9,n''),nchar10,n''),nchar13,n'')),1)=n'{'and((payoutsourcekind=1andposshiftcashtransactionidisnotnull)or(payoutsourcekind=2andtreasuryentryidisnotnull)))",@"([FundingId] IS NULL AND [PayoutSourceKind] IS NULL AND [TreasuryEntryId] IS NULL AND [POSShiftCashTransactionId] IS NULL AND [PayerBankAccountId] IS NULL AND [PayerLegalEntityId] IS NULL AND [Fund] IS NULL AND [VerificationMethod] IS NULL AND [EvidenceReference] IS NULL AND [ProtectedPayoutEvidence] IS NULL AND [SourceSnapshotJson] IS NULL OR [FundingId] IS NOT NULL AND [PayerLegalEntityId] IS NOT NULL AND [Fund] IS NOT NULL AND ([Fund]=N'cash' OR [Fund]=N'bank') AND [VerificationMethod] IS NOT NULL AND [EvidenceReference] IS NOT NULL AND len([EvidenceReference])>(0) AND [ProtectedPayoutEvidence] IS NOT NULL AND datalength([ProtectedPayoutEvidence])>(0) AND [SourceSnapshotJson] IS NOT NULL AND isjson([SourceSnapshotJson])=(1) AND left(ltrim(replace(replace(replace([SourceSnapshotJson],nchar((9)),N''),nchar((10)),N''),nchar((13)),N'')),(1))=N'{' AND ([PayoutSourceKind]=(1) AND [POSShiftCashTransactionId] IS NOT NULL OR [PayoutSourceKind]=(2) AND [TreasuryEntryId] IS NOT NULL))"),
        new(@"funding",@"storefrontrefundfundings",@"ck_storefrontrefundfundings_source",@"((kind=1andglobalbankreceiptidisnotnullandcodreceiptidisnullandreceivinglegalentityidisnotnull)or(kind=2andcodreceiptidisnotnull))andisjson(sourcesnapshotjson)=1",@"(([Kind]=(1) AND [GlobalBankReceiptId] IS NOT NULL AND [CodReceiptId] IS NULL AND [ReceivingLegalEntityId] IS NOT NULL OR [Kind]=(2) AND [CodReceiptId] IS NOT NULL) AND isjson([SourceSnapshotJson])=(1))"),
    ];
}

