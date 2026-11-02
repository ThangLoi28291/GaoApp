using GaoApp.Infrastructure.Data.Migrations;

namespace GaoApp.Tests.Storefront;

// Independent APPv1 SQL capture: schema/03545cec9a7d4372b79c5836073ee6f3.
// Source signatures are captured expected model signatures; persisted text is
// verbatim sys.check_constraints. No production map constants are referenced.
// Fifteen named constraints share twelve forms; identical Amount forms are
// grouped, while comparer/trust assertions still cover every captured alias.
[Trait("Category", "StorefrontWeb06")]
public sealed class Web06SchemaExpressionTests
{
    private sealed record Alias(string Table, string Name);
    private sealed record CapturedPair(string Name, string Source, string Persisted, Alias[] Aliases);
    public static IEnumerable<object[]> ObservedForms => Pairs.Select(pair => new object[] { pair.Name });
    [Theory, MemberData(nameof(ObservedForms))]
    public void Observed_whole_predicates_match_idempotently_without_rewriting_quoted_copies(string name)
    {
        var pair = Pair(name); var source = N(pair.Source); var persisted = N(pair.Persisted);
        Assert.Equal(source, persisted); Assert.Equal(source, N(source)); Assert.Equal(persisted, N(persisted));
        Assert.NotEqual(DatabaseSchemaNormalization.NormalizeSqlExpression(pair.Source), DatabaseSchemaNormalization.NormalizeSqlExpression(pair.Persisted));
        foreach (var alias in pair.Aliases)
        {
            var result = DatabaseSchemaComparer.Compare(Manifest(alias, pair.Source), Manifest(alias, pair.Persisted));
            Assert.True(result.IsMatch); Assert.Equal(0, result.Mismatches.Total);
        }
        var quoted = "[Note]=N'" + pair.Persisted.Replace("'", "''", StringComparison.Ordinal) + "'";
        Assert.Equal(DatabaseSchemaNormalization.NormalizeSqlExpression(quoted), N(quoted));
    }
    [Theory, MemberData(nameof(ObservedForms))]
    public void Equivalent_text_never_hides_disabled_untrusted_or_both_flags(string name)
    {
        var pair = Pair(name);
        foreach (var alias in pair.Aliases)
            foreach (var (disabled, untrusted) in new[] { (true, false), (false, true), (true, true) })
            {
                var result = DatabaseSchemaComparer.Compare(Manifest(alias, pair.Source), Manifest(alias, pair.Persisted, disabled, untrusted));
                Assert.False(result.IsMatch); Assert.Equal(2, result.Mismatches.CheckConstraints);
            }
    }
    [Theory, MemberData(nameof(SemanticNeighbors))]
    public void Real_source_and_persisted_semantic_neighbors_remain_distinct(string name, string original, string changed)
    {
        Assert.False(string.IsNullOrWhiteSpace(name)); Assert.NotEqual(original, changed); Assert.NotEqual(N(original), N(changed));
        var alias = new Alias("Web06ObservedFixture", "CK_Observed");
        Assert.False(DatabaseSchemaComparer.Compare(Manifest(alias, original), Manifest(alias, changed)).IsMatch);
    }
    [Theory]
    [InlineData("'Automatic'", "'automatic'")]
    [InlineData("'CodReceipt'", "'CodReceipt '")]
    [InlineData("'O''Brien'", "'O''brien'")]
    [InlineData("N'[InvoiceRoute] IN (1,2)'", "N'([InvoiceRoute]=(2) OR [InvoiceRoute]=(1))'")]
    public void Quoted_case_trailing_bytes_escapes_and_SQL_looking_values_stay_distinct(string original, string changed)
        => Assert.NotEqual(N("[Note]=" + original), N("[Note]=" + changed));
    public static IEnumerable<object[]> SemanticNeighbors()
    {
        foreach (var pair in Pairs)
        {
            var (source, persisted) = Mutate(pair);
            yield return new object[] { pair.Name + "-source", pair.Source, source };
            yield return new object[] { pair.Name + "-persisted", pair.Persisted, persisted };
        }
    }
    private static (string Source, string Persisted) Mutate(CapturedPair p) => p.Name switch
    {
        "ck_bankverifiedreceipts_codprovenance" => (p.Source.Replace("in(0,1,2)", "in(0,1,3)", StringComparison.Ordinal), p.Persisted.Replace("[NativeCollectionAuthorityProofJson] IS NULL OR", "[NativeCollectionAuthorityProofJson] IS NOT NULL OR", StringComparison.Ordinal)),
        "ck_deliveryorderlines_identity" => (p.Source.Replace("sourcecartidisnulland", "sourcecartidisnotnulland", StringComparison.Ordinal), p.Persisted.Replace("[StorefrontOrderId] IS NULL AND", "[StorefrontOrderId] IS NOT NULL AND", StringComparison.Ordinal)),
        "ck_deliveryorders_nativeorigin" => (p.Source.Replace("sourcekind=1and", "sourcekind=2and", StringComparison.Ordinal), p.Persisted.Replace("[CreatedShiftId] IS NULL", "[CreatedShiftId] IS NOT NULL", StringComparison.Ordinal)),
        "ck_deliveryorders_total" => (p.Source.Replace("sourcekind=0andquotedtotal>0", "sourcekind=0andquotedtotal>=0", StringComparison.Ordinal), p.Persisted.Replace("[SourceKind]=(1) AND [QuotedTotal]>=(0)", "[SourceKind]=(1) AND [QuotedTotal]>(0)", StringComparison.Ordinal)),
        "ck_deliverypickingworks_approval" => (p.Source.Replace("approvedtotal>=0", "approvedtotal>0", StringComparison.Ordinal), p.Persisted.Replace("round([ApprovedTotal],(0))", "round([ApprovedTotal],(2))", StringComparison.Ordinal)),
        "ck_invoiceheads_legacyorder" => (p.Source.Replace("nativeserviceinvoiceroutein(1,2)", "nativeserviceinvoiceroutein(0,2)", StringComparison.Ordinal), "(" + p.Persisted + ") AND [OrderId] IS NULL"),
        "ck_storefrontcarrierwithholdings_amount" => (p.Source.Replace("amount>0", "amount>=0", StringComparison.Ordinal), p.Persisted.Replace("(10000000000000000.)", "(9999999999999999.)", StringComparison.Ordinal)),
        "ck_storefrontfulfillmentworks_stage" => (p.Source.Replace("and6", "and7", StringComparison.Ordinal), p.Persisted.Replace("[Stage]>=(0)", "[Stage]>(0)", StringComparison.Ordinal)),
        "ck_storefrontservicefundings_source" => (p.Source.Replace(")=1", ")=2", StringComparison.Ordinal), p.Persisted.Replace("[CodReceiptId] IS NOT NULL", "[CodReceiptId] IS NULL", StringComparison.Ordinal)),
        "ck_storefrontservicerevenues_arithmetic" => (p.Source.Replace("netamount+taxamount", "netamount-taxamount", StringComparison.Ordinal), p.Persisted.Replace("[TaxRate]<=(100)", "[TaxRate]<=(99)", StringComparison.Ordinal)),
        "ck_storefrontservicerevenues_grossamount" => (p.Source.Replace("grossamount>0", "grossamount>=0", StringComparison.Ordinal), p.Persisted.Replace("(10000000000000000.)", "(9999999999999999.)", StringComparison.Ordinal)),
        "ck_storefrontservicerevenuesettings_route" => (p.Source.Replace("in(1,2)", "in(0,2)", StringComparison.Ordinal), p.Persisted.Replace("[InvoiceRoute]=(1)", "[InvoiceRoute]=(0)", StringComparison.Ordinal)),
        _ => throw new ArgumentOutOfRangeException(nameof(p))
    };
    private static CapturedPair Pair(string name) => Pairs.Single(pair => pair.Name == name);
    private static string N(string text) => DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(text)!;
    private static DatabaseSchemaManifest Manifest(Alias alias, string expression, bool disabled = false, bool untrusted = false)
        => DatabaseSchemaCanonicalizer.WithFingerprint([], [new DatabaseTableSchema(new("dbo", alias.Table), [], null, [], [], [new(alias.Name, N(expression), disabled, untrusted)])], []);
    private static readonly CapturedPair[] Pairs =
    [
        new("ck_deliveryorders_total", "((sourcekind=0andquotedtotal>0)or(sourcekind=1andquotedtotal>=0))andquotedtotal=round(quotedtotal,0)", "(([SourceKind]=(0) AND [QuotedTotal]>(0) OR [SourceKind]=(1) AND [QuotedTotal]>=(0)) AND [QuotedTotal]=round([QuotedTotal],(0)))", [new("deliveryorders","ck_deliveryorders_total")]),
        new("ck_deliverypickingworks_approval", "(approvedrevisionisnullandapprovedbyuseridisnullandapprovedatutcisnullandapprovedtotalisnull)or(approvedrevisionisnotnullandapprovedrevision>0andapprovedbyuseridisnotnullandapprovedatutcisnotnullandapprovedtotalisnotnullandapprovedtotal>=0andapprovedtotal=round(approvedtotal,0))", "([ApprovedRevision] IS NULL AND [ApprovedByUserId] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovedTotal] IS NULL OR [ApprovedRevision] IS NOT NULL AND [ApprovedRevision]>(0) AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [ApprovedTotal] IS NOT NULL AND [ApprovedTotal]>=(0) AND [ApprovedTotal]=round([ApprovedTotal],(0)))", [new("deliverypickingworks","ck_deliverypickingworks_approval")]),
        new("ck_storefrontservicefundings_source", "(casewhenglobalbankreceiptidisnotnullthen1else0end+casewhencodreceiptidisnotnullthen1else0end+casewhencarrierwithholdingidisnotnullthen1else0end)=1", "(((case when [GlobalBankReceiptId] IS NOT NULL then (1) else (0) end+case when [CodReceiptId] IS NOT NULL then (1) else (0) end)+case when [CarrierWithholdingId] IS NOT NULL then (1) else (0) end)=(1))", [new("storefrontservicefundings","ck_storefrontservicefundings_source")]),
        new("ck_deliveryorders_nativeorigin", "(sourcekind=0andsourcecartidisnotnullandcreatedterminalidisnotnullandcreatedshiftidisnotnullandstorefrontorderidisnullandstorefrontfulfillmentsourceidisnullandstorefrontallownegativeinventoryisnull)or(sourcekind=1andsourcecartidisnullandcreatedterminalidisnullandcreatedshiftidisnullandstorefrontorderidisnotnullandstorefrontfulfillmentsourceidisnotnullandstorefrontallownegativeinventoryisnotnull)", "([SourceKind]=(0) AND [SourceCartId] IS NOT NULL AND [CreatedTerminalId] IS NOT NULL AND [CreatedShiftId] IS NOT NULL AND [StorefrontOrderId] IS NULL AND [StorefrontFulfillmentSourceId] IS NULL AND [StorefrontAllowNegativeInventory] IS NULL OR [SourceKind]=(1) AND [SourceCartId] IS NULL AND [CreatedTerminalId] IS NULL AND [CreatedShiftId] IS NULL AND [StorefrontOrderId] IS NOT NULL AND [StorefrontFulfillmentSourceId] IS NOT NULL AND [StorefrontAllowNegativeInventory] IS NOT NULL)", [new("deliveryorders","ck_deliveryorders_nativeorigin")]),
        new("ck_deliveryorderlines_identity", "(storefrontorderidisnullandstorefrontorderlineidisnullandsourcecartidisnotnulland((sourceorderlineidisnotnullandoriginalrootlineidisnullandproductunitconversionidisnullandsellingunitidisnullandbaseunitidisnull)or(sourceorderlineidisnullandoriginalrootlineidisnotnullandsellingunitidisnotnullandbaseunitidisnotnullandlinediscount=0andallocatedorderdiscount=0)))or(storefrontorderidisnotnullandstorefrontorderlineidisnotnullandsourcecartidisnullandsourceorderlineidisnullandoriginalrootlineidisnullandsellingunitidisnotnullandbaseunitidisnotnull)", "([StorefrontOrderId] IS NULL AND [StorefrontOrderLineId] IS NULL AND [SourceCartId] IS NOT NULL AND ([SourceOrderLineId] IS NOT NULL AND [OriginalRootLineId] IS NULL AND [ProductUnitConversionId] IS NULL AND [SellingUnitId] IS NULL AND [BaseUnitId] IS NULL OR [SourceOrderLineId] IS NULL AND [OriginalRootLineId] IS NOT NULL AND [SellingUnitId] IS NOT NULL AND [BaseUnitId] IS NOT NULL AND [LineDiscount]=(0) AND [AllocatedOrderDiscount]=(0)) OR [StorefrontOrderId] IS NOT NULL AND [StorefrontOrderLineId] IS NOT NULL AND [SourceCartId] IS NULL AND [SourceOrderLineId] IS NULL AND [OriginalRootLineId] IS NULL AND [SellingUnitId] IS NOT NULL AND [BaseUnitId] IS NOT NULL)", [new("deliveryorderlines","ck_deliveryorderlines_identity")]),
        new("ck_storefrontcarrierwithholdings_amount", "amount>0andamount<10000000000000000andamount=round(amount,0)", "([Amount]>(0) AND [Amount]<(10000000000000000.) AND [Amount]=round([Amount],(0)))", [new("storefrontcarrierwithholdings","ck_storefrontcarrierwithholdings_amount"),new("storefrontcodcollections","ck_storefrontcodcollections_amount"),new("storefrontcodreceipts","ck_storefrontcodreceipts_amount"),new("storefrontservicefundings","ck_storefrontservicefundings_amount")]),
        new("ck_storefrontservicerevenues_arithmetic", "grossamount=netamount+taxamountandnetamount>=0andtaxamount>=0andtaxrate>=0andtaxrate<=100andinvoiceroutein(1,2)", "([GrossAmount]=([NetAmount]+[TaxAmount]) AND [NetAmount]>=(0) AND [TaxAmount]>=(0) AND [TaxRate]>=(0) AND [TaxRate]<=(100) AND ([InvoiceRoute]=(2) OR [InvoiceRoute]=(1)))", [new("storefrontservicerevenues","ck_storefrontservicerevenues_arithmetic")]),
        new("ck_storefrontservicerevenues_grossamount", "grossamount>0andgrossamount<10000000000000000andgrossamount=round(grossamount,0)", "([GrossAmount]>(0) AND [GrossAmount]<(10000000000000000.) AND [GrossAmount]=round([GrossAmount],(0)))", [new("storefrontservicerevenues","ck_storefrontservicerevenues_grossamount")]),
        new("ck_storefrontservicerevenuesettings_route", "invoiceroutein(1,2)", "([InvoiceRoute]=(2) OR [InvoiceRoute]=(1))", [new("storefrontservicerevenuesettings","ck_storefrontservicerevenuesettings_route")]),
        new("ck_invoiceheads_legacyorder", "orderidisnotnullor(legacysourceidisnotnullandlegacyreadonly=1)orisautoinvoicegroup=1or(storefrontservicerevenueidisnotnullandnativeserviceinvoiceroutein(1,2)andnativeservicerecognizedatutcisnotnullandnativeserviceactoruseridisnotnullandlegacyreadonly=0andlegacysourceidisnull)", "([OrderId] IS NOT NULL OR [LegacySourceId] IS NOT NULL AND [LegacyReadOnly]=(1) OR [IsAutoInvoiceGroup]=(1) OR [StorefrontServiceRevenueId] IS NOT NULL AND ([NativeServiceInvoiceRoute]=(2) OR [NativeServiceInvoiceRoute]=(1)) AND [NativeServiceRecognizedAtUtc] IS NOT NULL AND [NativeServiceActorUserId] IS NOT NULL AND [LegacyReadOnly]=(0) AND [LegacySourceId] IS NULL)", [new("invoiceheads","ck_invoiceheads_legacyorder")]),
        new("ck_bankverifiedreceipts_codprovenance", "routingprovenancein(0,1,2)and(nativecollectionauthorityproofjsonisnullorisjson(nativecollectionauthorityproofjson)=1)", "(([RoutingProvenance]=(2) OR [RoutingProvenance]=(1) OR [RoutingProvenance]=(0)) AND ([NativeCollectionAuthorityProofJson] IS NULL OR isjson([NativeCollectionAuthorityProofJson])=(1)))", [new("bankverifiedreceipts","ck_bankverifiedreceipts_codprovenance")]),
        new("ck_storefrontfulfillmentworks_stage", "stagebetween0and6", "([Stage]>=(0) AND [Stage]<=(6))", [new("storefrontfulfillmentworks","ck_storefrontfulfillmentworks_stage")]),
    ];
}
