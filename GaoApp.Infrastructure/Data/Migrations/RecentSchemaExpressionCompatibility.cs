namespace GaoApp.Infrastructure.Data.Migrations;

public static partial class DatabaseSchemaNormalization
{
    // Whole, reviewed predicates only. Do not erase arbitrary parentheses or
    // reorder unrelated logic to hide a changed schema constraint.
    private static string? NormalizeRecentCheck(string value)
    {
        var normalized = NormalizeSqlExpression(value);
        foreach (var pair in RecentCheckForms)
            if (normalized == NormalizeSqlExpression(pair.Source) || normalized == NormalizeSqlExpression(pair.Persisted))
                return NormalizeSqlExpression(pair.Source);
        return null;
    }

    private static readonly (string Source, string Persisted)[] RecentCheckForms =
    [
        // WEB07: exact three whole predicates from independent owned SQL smoke capture.
        // AND-before-OR branches and numeric function parentheses only; literals,
        // object/null/source guards and disabled/untrusted flags remain significant.
        // storefrontcancellationdecisions.ck_storefrontcancellationdecisions_decision
        ("LEN([Reason])>0 AND (([Kind]=1 AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NULL) OR ([Kind]=2 AND [AuthorisesPhysicalReturn]=0 AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NULL) OR ([Kind]=3 AND [AuthorisesPhysicalReturn]=0 AND [ActualDeliveryEvidenceId] IS NOT NULL AND [ActualReturnEvidenceId] IS NULL) OR ([Kind]=4 AND [AuthorisesPhysicalReturn]=0 AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NOT NULL))",
         "(len([Reason])>(0) AND ([Kind]=(1) AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NULL OR [Kind]=(2) AND [AuthorisesPhysicalReturn]=(0) AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NULL OR [Kind]=(3) AND [AuthorisesPhysicalReturn]=(0) AND [ActualDeliveryEvidenceId] IS NOT NULL AND [ActualReturnEvidenceId] IS NULL OR [Kind]=(4) AND [AuthorisesPhysicalReturn]=(0) AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NOT NULL))"),
        // storefrontrefundevidence.ck_storefrontrefundevidence_nativepayout
        ("([FundingId] IS NULL AND [PayoutSourceKind] IS NULL AND [TreasuryEntryId] IS NULL AND [POSShiftCashTransactionId] IS NULL AND [PayerBankAccountId] IS NULL AND [PayerLegalEntityId] IS NULL AND [Fund] IS NULL AND [VerificationMethod] IS NULL AND [EvidenceReference] IS NULL AND [ProtectedPayoutEvidence] IS NULL AND [SourceSnapshotJson] IS NULL) OR ([FundingId] IS NOT NULL AND [PayerLegalEntityId] IS NOT NULL AND [Fund] IS NOT NULL AND ([Fund]=N'cash' OR [Fund]=N'bank') AND [VerificationMethod] IS NOT NULL AND [EvidenceReference] IS NOT NULL AND LEN([EvidenceReference])>0 AND [ProtectedPayoutEvidence] IS NOT NULL AND DATALENGTH([ProtectedPayoutEvidence])>0 AND [SourceSnapshotJson] IS NOT NULL AND ISJSON([SourceSnapshotJson])=1 AND LEFT(LTRIM(REPLACE(REPLACE(REPLACE([SourceSnapshotJson],NCHAR(9),N''),NCHAR(10),N''),NCHAR(13),N'')),1)=N'{' AND (([PayoutSourceKind]=1 AND [POSShiftCashTransactionId] IS NOT NULL) OR ([PayoutSourceKind]=2 AND [TreasuryEntryId] IS NOT NULL)))",
         "([FundingId] IS NULL AND [PayoutSourceKind] IS NULL AND [TreasuryEntryId] IS NULL AND [POSShiftCashTransactionId] IS NULL AND [PayerBankAccountId] IS NULL AND [PayerLegalEntityId] IS NULL AND [Fund] IS NULL AND [VerificationMethod] IS NULL AND [EvidenceReference] IS NULL AND [ProtectedPayoutEvidence] IS NULL AND [SourceSnapshotJson] IS NULL OR [FundingId] IS NOT NULL AND [PayerLegalEntityId] IS NOT NULL AND [Fund] IS NOT NULL AND ([Fund]=N'cash' OR [Fund]=N'bank') AND [VerificationMethod] IS NOT NULL AND [EvidenceReference] IS NOT NULL AND len([EvidenceReference])>(0) AND [ProtectedPayoutEvidence] IS NOT NULL AND datalength([ProtectedPayoutEvidence])>(0) AND [SourceSnapshotJson] IS NOT NULL AND isjson([SourceSnapshotJson])=(1) AND left(ltrim(replace(replace(replace([SourceSnapshotJson],nchar((9)),N''),nchar((10)),N''),nchar((13)),N'')),(1))=N'{' AND ([PayoutSourceKind]=(1) AND [POSShiftCashTransactionId] IS NOT NULL OR [PayoutSourceKind]=(2) AND [TreasuryEntryId] IS NOT NULL))"),
        // storefrontrefundfundings.ck_storefrontrefundfundings_source
        ("(([Kind]=1 AND [GlobalBankReceiptId] IS NOT NULL AND [CodReceiptId] IS NULL AND [ReceivingLegalEntityId] IS NOT NULL) OR ([Kind]=2 AND [CodReceiptId] IS NOT NULL)) AND ISJSON([SourceSnapshotJson])=1",
         "(([Kind]=(1) AND [GlobalBankReceiptId] IS NOT NULL AND [CodReceiptId] IS NULL AND [ReceivingLegalEntityId] IS NOT NULL OR [Kind]=(2) AND [CodReceiptId] IS NOT NULL) AND isjson([SourceSnapshotJson])=(1))"),
        // WEB06: exact whole predicates observed on the owned fresh SQL schema.
        // All fifteen captured constraints were enabled/trusted; twelve distinct pairs.
        // bankverifiedreceipts.ck_bankverifiedreceipts_codprovenance
        ("routingprovenancein(0,1,2)and(nativecollectionauthorityproofjsonisnullorisjson(nativecollectionauthorityproofjson)=1)",
         "(routingprovenance=0orroutingprovenance=1orroutingprovenance=2)and(nativecollectionauthorityproofjsonisnullorisjson(nativecollectionauthorityproofjson)=1)"),
        // deliveryorderlines.ck_deliveryorderlines_identity
        ("(storefrontorderidisnullandstorefrontorderlineidisnullandsourcecartidisnotnulland((sourceorderlineidisnotnullandoriginalrootlineidisnullandproductunitconversionidisnullandsellingunitidisnullandbaseunitidisnull)or(sourceorderlineidisnullandoriginalrootlineidisnotnullandsellingunitidisnotnullandbaseunitidisnotnullandlinediscount=0andallocatedorderdiscount=0)))or(storefrontorderidisnotnullandstorefrontorderlineidisnotnullandsourcecartidisnullandsourceorderlineidisnullandoriginalrootlineidisnullandsellingunitidisnotnullandbaseunitidisnotnull)",
         "storefrontorderidisnullandstorefrontorderlineidisnullandsourcecartidisnotnulland(sourceorderlineidisnotnullandoriginalrootlineidisnullandproductunitconversionidisnullandsellingunitidisnullandbaseunitidisnullorsourceorderlineidisnullandoriginalrootlineidisnotnullandsellingunitidisnotnullandbaseunitidisnotnullandlinediscount=0andallocatedorderdiscount=0)orstorefrontorderidisnotnullandstorefrontorderlineidisnotnullandsourcecartidisnullandsourceorderlineidisnullandoriginalrootlineidisnullandsellingunitidisnotnullandbaseunitidisnotnull"),
        // deliveryorders.ck_deliveryorders_nativeorigin
        ("(sourcekind=0andsourcecartidisnotnullandcreatedterminalidisnotnullandcreatedshiftidisnotnullandstorefrontorderidisnullandstorefrontfulfillmentsourceidisnullandstorefrontallownegativeinventoryisnull)or(sourcekind=1andsourcecartidisnullandcreatedterminalidisnullandcreatedshiftidisnullandstorefrontorderidisnotnullandstorefrontfulfillmentsourceidisnotnullandstorefrontallownegativeinventoryisnotnull)",
         "sourcekind=0andsourcecartidisnotnullandcreatedterminalidisnotnullandcreatedshiftidisnotnullandstorefrontorderidisnullandstorefrontfulfillmentsourceidisnullandstorefrontallownegativeinventoryisnullorsourcekind=1andsourcecartidisnullandcreatedterminalidisnullandcreatedshiftidisnullandstorefrontorderidisnotnullandstorefrontfulfillmentsourceidisnotnullandstorefrontallownegativeinventoryisnotnull"),
        // deliveryorders.ck_deliveryorders_total
        ("((sourcekind=0andquotedtotal>0)or(sourcekind=1andquotedtotal>=0))andquotedtotal=round(quotedtotal,0)",
         "(sourcekind=0andquotedtotal>0orsourcekind=1andquotedtotal>=0)andquotedtotal=round(quotedtotal,0)"),
        // deliverypickingworks.ck_deliverypickingworks_approval
        ("(approvedrevisionisnullandapprovedbyuseridisnullandapprovedatutcisnullandapprovedtotalisnull)or(approvedrevisionisnotnullandapprovedrevision>0andapprovedbyuseridisnotnullandapprovedatutcisnotnullandapprovedtotalisnotnullandapprovedtotal>=0andapprovedtotal=round(approvedtotal,0))",
         "approvedrevisionisnullandapprovedbyuseridisnullandapprovedatutcisnullandapprovedtotalisnullorapprovedrevisionisnotnullandapprovedrevision>0andapprovedbyuseridisnotnullandapprovedatutcisnotnullandapprovedtotalisnotnullandapprovedtotal>=0andapprovedtotal=round(approvedtotal,0)"),
        // invoiceheads.ck_invoiceheads_legacyorder
        ("orderidisnotnullor(legacysourceidisnotnullandlegacyreadonly=1)orisautoinvoicegroup=1or(storefrontservicerevenueidisnotnullandnativeserviceinvoiceroutein(1,2)andnativeservicerecognizedatutcisnotnullandnativeserviceactoruseridisnotnullandlegacyreadonly=0andlegacysourceidisnull)",
         "orderidisnotnullorlegacysourceidisnotnullandlegacyreadonly=1orisautoinvoicegroup=1orstorefrontservicerevenueidisnotnulland(nativeserviceinvoiceroute=1ornativeserviceinvoiceroute=2)andnativeservicerecognizedatutcisnotnullandnativeserviceactoruseridisnotnullandlegacyreadonly=0andlegacysourceidisnull"),
        // storefrontcarrierwithholdings.ck_storefrontcarrierwithholdings_amount, storefrontcodcollections.ck_storefrontcodcollections_amount, storefrontcodreceipts.ck_storefrontcodreceipts_amount, storefrontservicefundings.ck_storefrontservicefundings_amount
        ("amount>0andamount<10000000000000000andamount=round(amount,0)",
         "amount>0andamount<(10000000000000000.)andamount=round(amount,0)"),
        // storefrontfulfillmentworks.ck_storefrontfulfillmentworks_stage
        ("stagebetween0and6",
         "stage>=0andstage<=6"),
        // storefrontservicefundings.ck_storefrontservicefundings_source
        ("(casewhenglobalbankreceiptidisnotnullthen1else0end+casewhencodreceiptidisnotnullthen1else0end+casewhencarrierwithholdingidisnotnullthen1else0end)=1",
         "((casewhenglobalbankreceiptidisnotnullthen1else0end+casewhencodreceiptidisnotnullthen1else0end)+casewhencarrierwithholdingidisnotnullthen1else0end)=1"),
        // storefrontservicerevenues.ck_storefrontservicerevenues_arithmetic
        ("grossamount=netamount+taxamountandnetamount>=0andtaxamount>=0andtaxrate>=0andtaxrate<=100andinvoiceroutein(1,2)",
         "grossamount=(netamount+taxamount)andnetamount>=0andtaxamount>=0andtaxrate>=0andtaxrate<=100and(invoiceroute=2orinvoiceroute=1)"),
        // storefrontservicerevenues.ck_storefrontservicerevenues_grossamount
        ("grossamount>0andgrossamount<10000000000000000andgrossamount=round(grossamount,0)",
         "grossamount>0andgrossamount<(10000000000000000.)andgrossamount=round(grossamount,0)"),
        // storefrontservicerevenuesettings.ck_storefrontservicerevenuesettings_route
        ("invoiceroutein(1,2)",
         "invoiceroute=1orinvoiceroute=2"),
        // WEB05: three complete predicates captured from enabled, trusted SQL checks.
        // SQL persists AND-before-OR branches without outer parentheses and expands the numeric IN.
        ("([Channel]=1 AND [PosQrRequestId] IS NOT NULL AND [StorefrontPaymentIntentId] IS NULL) OR ([Channel]=2 AND [PosQrRequestId] IS NULL AND [StorefrontPaymentIntentId] IS NOT NULL)",
         "([Channel]=(1) AND [PosQrRequestId] IS NOT NULL AND [StorefrontPaymentIntentId] IS NULL OR [Channel]=(2) AND [PosQrRequestId] IS NULL AND [StorefrontPaymentIntentId] IS NOT NULL)"),
        ("([Channel]=1 AND [PosPaymentId] IS NOT NULL AND [StorefrontAllocationId] IS NULL) OR ([Channel]=2 AND [PosPaymentId] IS NULL AND [StorefrontAllocationId] IS NOT NULL)",
         "([Channel]=(1) AND [PosPaymentId] IS NOT NULL AND [StorefrontAllocationId] IS NULL OR [Channel]=(2) AND [PosPaymentId] IS NULL AND [StorefrontAllocationId] IS NOT NULL)"),
        ("([TimePrecision]=0 AND [OccurredAtUtc] IS NULL AND [BankBusinessDate] IS NULL) OR ([TimePrecision] IN (1,2) AND [OccurredAtUtc] IS NOT NULL AND [BankBusinessDate] IS NOT NULL)",
         "([TimePrecision]=(0) AND [OccurredAtUtc] IS NULL AND [BankBusinessDate] IS NULL OR ([TimePrecision]=(2) OR [TimePrecision]=(1)) AND [OccurredAtUtc] IS NOT NULL AND [BankBusinessDate] IS NOT NULL)"),
        // WEB04: exact whole predicates captured from trusted, enabled SQL Server checks.
        // Keep arithmetic/grouping/binary conversions significant for all other expressions.
        (StorefrontCheckoutSchemaSqlCatalog.DeliveryConstraint,
         "referencetype<>11anddeliveryorderidisnullanddeliveryorderlineidisnulland(sourcelegalentityidisnullorreferencetype=12)anddeliveryrevisionisnullandreportedbyuseridisnullandreportedatutcisnullorreferencetype=11andisdeleted=0anddeliveryorderidisnotnullanddeliveryorderlineidisnotnullandsourcelegalentityidisnotnullanddeliveryrevisionisnotnullanddeliveryrevision>0andreportedbyuseridisnotnullandreportedatutcisnotnullandreservedqty>0andreferencelineidisnotnullandreferencelineid=deliveryorderlineidandreferenceid=convert(nvarchar50,deliveryorderid)and(status=1orstatus=2orstatus=3)"),
        (StorefrontCheckoutSchemaSqlCatalog.ReservationConstraint,
         "referencetype<>12andstorefrontorderidisnullandstorefrontorderlineidisnullandstorefrontfulfillmentsourceidisnullorreferencetype=12andisdeleted=0andstorefrontorderidisnotnullandstorefrontorderlineidisnotnullandstorefrontfulfillmentsourceidisnotnullandsourcelegalentityidisnotnullandreservedqty>0andreferencelineid=storefrontorderlineidandreferencelineidisnotnullandconvert(varbinary(max),referenceid)=convert(varbinary(max),convert(nvarchar50,storefrontorderid))anddatalength(referenceid)=datalength(convert(nvarchar50,storefrontorderid))and(status=3orstatus=2orstatus=1)"),
        (StorefrontCheckoutSchemaSqlCatalog.LinePriceConstraint,
         "(round(quantity*unitprice,0)+roundingadjustment)>=0anddiscount<=(round(quantity*unitprice,0)+roundingadjustment)andnetamount=((round(quantity*unitprice,0)+roundingadjustment)-discount)"),
        ("[RoundingAdjustment] BETWEEN -201 AND 201 AND [RoundingAdjustment] = ROUND([RoundingAdjustment],0)",
         "roundingadjustment>=-201androundingadjustment<=201androundingadjustment=round(roundingadjustment,0)"),
        ("kindin(0,1)", "kind=0orkind=1"),
        ("phasebetween0and6", "phase>=0andphase<=6"),
        ("totaldue=goodsamount+shippingcharge", "totaldue=(goodsamount+shippingcharge)"),
        ("qrenabled=0or(bankaccountidisnotnullandqrholdminutesisnotnull)", "qrenabled=0orbankaccountidisnotnullandqrholdminutesisnotnull"),
        ("discount<=round(quantity*unitprice,0)andnetamount=round(quantity*unitprice,0)-discount", "discount<=round(quantity*unitprice,0)andnetamount=(round(quantity*unitprice,0)-discount)"),
        ("len(guesttokenhash)=64andguesttokenhashnotlike'%[^0-9A-Fa-f]%'collatelatin1_general_100_bin2", "len(guesttokenhash)=64andnotguesttokenhashlike('%[^0-9A-Fa-f]%')collatelatin1_general_100_bin2"),
        ("revision>0andphasebetween0and6andpaymentmethodin(0,1)andpaymentstatebetween0and3anddeliverystatebetween0and6andrefundstatebetween0and4anddeliverymethodin(0,1)", "revision>0and(phase>=0andphase<=6)and(paymentmethod=0orpaymentmethod=1)and(paymentstate>=0andpaymentstate<=3)and(deliverystate>=0anddeliverystate<=6)and(refundstate>=0andrefundstate<=4)and(deliverymethod=0ordeliverymethod=1)"),
        ("totaldue=goodsamount+shippingchargeandshippingfeetreatment=1", "totaldue=(goodsamount+shippingcharge)andshippingfeetreatment=1"),
        ("revision>0andsettingsrevision>0andtotaldue=goodsamount+shippingchargeandexpiresatutc>createdatutc", "revision>0andsettingsrevision>0andtotaldue=(goodsamount+shippingcharge)andexpiresatutc>createdatutc"),
        ("amount=goodsamount+shippingamount+unallocatedreceiptamountandstatebetween1and4", "amount=((goodsamount+shippingamount)+unallocatedreceiptamount)and(state>=1andstate<=4)"),
        ("qrenabled=0or(bankaccountidisnotnullandreceivinglegalentityidisnotnullandqrholdminutesisnotnullandlen(ltrim(rtrim(bankterminalcode)))>0andlen(ltrim(rtrim(bankusercode)))>0)", "qrenabled=0orbankaccountidisnotnullandreceivinglegalentityidisnotnullandqrholdminutesisnotnullandlen(ltrim(rtrim(bankterminalcode)))>0andlen(ltrim(rtrim(bankusercode)))>0"),
        ("qrholdminutesisnullorqrholdminutesbetween1and10080", "qrholdminutesisnullorqrholdminutes>=1andqrholdminutes<=10080"),
        ("freefromgoodsamountisnullor(freefromgoodsamount>0andfreefromgoodsamount=round(freefromgoodsamount,0))", "freefromgoodsamountisnullorfreefromgoodsamount>0andfreefromgoodsamount=round(freefromgoodsamount,0)"),
        ("revision>0anddeliverymethodin(0,1)andmindeliverydays>=0andmaxdeliverydays>=mindeliverydaysandmaxdeliverydays<=365", "revision>0and(deliverymethod=0ordeliverymethod=1)andmindeliverydays>=0andmaxdeliverydays>=mindeliverydaysandmaxdeliverydays<=365"),
        ("(revenuetaxpercentageisnullandrevenuetaxreductionamount=0)or(revenuetaxpercentageisnotnullandrevenuetaxpercentagein(1,2,3,5)andrevenuetaxreductionamount>=0)", "revenuetaxpercentageisnullandrevenuetaxreductionamount=0orrevenuetaxpercentageisnotnulland(revenuetaxpercentage=1orrevenuetaxpercentage=2orrevenuetaxpercentage=3orrevenuetaxpercentage=5)andrevenuetaxreductionamount>=0"),
        ("storeid>0andcustomerid>0andbindingversion>0andsenderprofileid<>'00000000-0000-0000-0000-000000000000'andstatusin('Pending','Dispatching','Accepted','Sent','Skipped','Failed','NeedsReview')", "storeid>0andcustomerid>0andbindingversion>0andsenderprofileid<>'00000000-0000-0000-0000-000000000000'and(status='NeedsReview'orstatus='Failed'orstatus='Skipped'orstatus='Sent'orstatus='Accepted'orstatus='Dispatching'orstatus='Pending')"),
        ("storeid>0andrecipientcount>0andcampaignkey<>'00000000-0000-0000-0000-000000000000'andpreviewrequestid<>'00000000-0000-0000-0000-000000000000'andsenderprofileid<>'00000000-0000-0000-0000-000000000000'andlen(messagetext)>0andlen(requestfingerprint)=64and((assetbytesisnullandassetkindisnullandassetfilenameisnullandassetcontenttypeisnullandassetlengthisnullandassetsha256isnull)or(assetbytesisnotnullandassetkindin('Image','File')andassetfilenameisnotnullandassetcontenttypeisnotnullandassetlength>0andassetsha256isnotnull))andstatusin('Draft','Scheduled','Dispatching','Completed','Cancelled','NeedsReview')", "storeid>0andrecipientcount>0andcampaignkey<>'00000000-0000-0000-0000-000000000000'andpreviewrequestid<>'00000000-0000-0000-0000-000000000000'andsenderprofileid<>'00000000-0000-0000-0000-000000000000'andlen(messagetext)>0andlen(requestfingerprint)=64and(assetbytesisnullandassetkindisnullandassetfilenameisnullandassetcontenttypeisnullandassetlengthisnullandassetsha256isnullorassetbytesisnotnulland(assetkind='File'orassetkind='Image')andassetfilenameisnotnullandassetcontenttypeisnotnullandassetlength>0andassetsha256isnotnull)and(status='NeedsReview'orstatus='Cancelled'orstatus='Completed'orstatus='Dispatching'orstatus='Scheduled'orstatus='Draft')")
    ];
}

