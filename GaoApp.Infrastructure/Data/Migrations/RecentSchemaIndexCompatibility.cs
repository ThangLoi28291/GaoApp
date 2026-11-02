using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Infrastructure.Data.Migrations;

internal static class RecentSchemaIndexCompatibility
{
    // EF's SQL Server generator persisted null filters on these historical
    // unique index operations even though the operations omitted Filter.
    internal static string? Filter(CreateIndexOperation operation)
    {
        if (!operation.IsUnique || operation.Filter is not null) return operation.Filter;
        var table = DatabaseSchemaNormalization.NormalizeIdentifier(operation.Table);
        var name = DatabaseSchemaNormalization.NormalizeIdentifier(operation.Name);
        var columns = operation.Columns.Select(DatabaseSchemaNormalization.NormalizeIdentifier).ToArray();
        if (table == "customermarketingcampaigns" && name == "ix_customermarketingcampaigns_campaignkey" && columns.SequenceEqual(new[] { "campaignkey" }))
            return "[CampaignKey] IS NOT NULL";
        if (table == "customermarketingcampaigns" && name == "ix_customermarketingcampaigns_storeid_previewrequestid" && columns.SequenceEqual(new[] { "storeid", "previewrequestid" }))
            return "[StoreId] IS NOT NULL AND [PreviewRequestId] IS NOT NULL";
        if (table == "customermarketingcampaignrecipients" && name == "ix_customermarketingcampaignrecipients_campaignid_customerid" && columns.SequenceEqual(new[] { "campaignid", "customerid" }))
            return "[CampaignId] IS NOT NULL AND [CustomerId] IS NOT NULL";
        return null;
    }
}
