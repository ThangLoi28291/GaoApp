-- SQLCMD parameters: StoreId, LegalEntityId, SettingId, ApplyRepair (0 = preview, 1 = apply).
-- Restore Viettel API tenant MST 8341099579-001. HKD/seller display MST stays 070091008655.
-- Only safe, unissued drafts accidentally snapshotted with the display MST are corrected.
-- Run against the intended database. IDs must identify the same HKD and its active configuration.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
DECLARE @StoreId int = $(StoreId), @LegalEntityId int = $(LegalEntityId), @SettingId int = $(SettingId);
DECLARE @Apply bit = $(ApplyRepair);
DECLARE @ApiTax nvarchar(20) = N'8341099579-001', @DisplayTax nvarchar(20) = N'070091008655';
DECLARE @Login nvarchar(150) = @ApiTax;
DECLARE @Password nvarchar(500), @Username nvarchar(150), @Template nvarchar(20), @Series nvarchar(25), @Now datetime2 = SYSUTCDATETIME();
DECLARE @Drafts TABLE (Id int PRIMARY KEY);

BEGIN TRY
    BEGIN TRANSACTION;
    -- Same stable lock used by the application's invoice issuance and draft creation.
    IF NOT EXISTS (SELECT 1 FROM dbo.Stores WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE Id=@StoreId AND IsDeleted=0)
        THROW 51040, 'Store not found.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.LegalEntities WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
        WHERE Id=@LegalEntityId AND StoreId=@StoreId AND IsDeleted=0 AND IsActive=1 AND TaxCode=@DisplayTax)
        THROW 51040, 'The HKD must already have the confirmed current seller display tax code.', 1;
    IF EXISTS (SELECT 1 FROM dbo.LegalEntities WHERE StoreId=@StoreId AND Id<>@LegalEntityId AND IsDeleted=0
        AND InvoiceProviderSettingId=@SettingId)
        THROW 51040, 'The selected configuration belongs to another HKD.', 1;

    SELECT @Password=[Password], @Username=Username, @Template=TemplateCode, @Series=InvoiceSeries
    FROM dbo.InvoiceProviderSettings WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
    WHERE Id=@SettingId AND StoreId=@StoreId AND IsDeleted=0 AND IsActive=1 AND ProviderCode=N'VIETTEL'
        AND Username=@Login AND SupplierTaxCode IN (@ApiTax,@DisplayTax);
    IF @Username IS NULL OR NULLIF(@Template,N'') IS NULL OR NULLIF(@Series,N'') IS NULL
        THROW 51040, 'Active Viettel configuration does not match the confirmed connection and seller identities.', 1;
    IF EXISTS (SELECT 1 FROM dbo.InvoiceProviderSettings WHERE StoreId=@StoreId AND IsDeleted=0 AND Id<>@SettingId
        AND ProviderCode=N'VIETTEL' AND SupplierTaxCode=@ApiTax AND TemplateCode=@Template AND InvoiceSeries=@Series)
        THROW 51040, 'Another configuration already has the target API tenant, template and series.', 1;

    INSERT @Drafts(Id)
    SELECT h.Id FROM dbo.InvoiceHeads h WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
    WHERE h.StoreId=@StoreId AND h.InvoiceProviderSettingId=@SettingId AND h.SupplierTaxCode=@DisplayTax
        AND h.IsDeleted=0 AND h.ProviderStatus=0 AND h.IsLocked=0 AND h.LegacyReadOnly=0 AND h.IsAutoInvoiceGroup=0
        AND h.OriginalInvoiceHeadId IS NULL AND h.CorrectionType IS NULL
        AND h.IssuedAtUtc IS NULL AND NULLIF(h.ProviderInvoiceNo,N'') IS NULL
        AND NULLIF(h.ProviderTransactionId,N'') IS NULL AND NULLIF(h.ReservationCode,N'') IS NULL
        AND h.LastSyncedAtUtc IS NULL AND NULLIF(h.PdfFilePath,N'') IS NULL AND NULLIF(h.ZipFilePath,N'') IS NULL
        AND NOT EXISTS (SELECT 1 FROM dbo.AutoInvoiceOperationSources s
            WHERE s.StoreId=@StoreId AND s.InvoiceHeadId=h.Id AND s.IsDeleted=0
                AND (s.IsActive=1 OR s.Status IN (1,2,3,5)));

    IF @Apply=0
    BEGIN
        SELECT @StoreId AS StoreId, @LegalEntityId AS LegalEntityId, @SettingId AS SettingId,
            @Username AS LoginUnchanged, @ApiTax AS TargetApiTaxCode, @DisplayTax AS SellerTaxCodeUnchanged,
            COUNT(*) AS EligibleLocalDrafts FROM @Drafts;
        SELECT Id AS EligibleInvoiceHeadId FROM @Drafts ORDER BY Id;
        ROLLBACK TRANSACTION;
        RETURN;
    END;

    UPDATE dbo.InvoiceProviderSettings SET SupplierTaxCode=@ApiTax, UpdatedAtUtc=@Now, UpdatedBy=NULL
    WHERE StoreId=@StoreId AND Id=@SettingId AND SupplierTaxCode<>@ApiTax;
    UPDATE dbo.LegalEntities SET InvoiceProviderSettingId=@SettingId, UpdatedAtUtc=@Now, UpdatedBy=NULL
    WHERE StoreId=@StoreId AND Id=@LegalEntityId AND (InvoiceProviderSettingId IS NULL OR InvoiceProviderSettingId<>@SettingId);
    UPDATE h SET SupplierTaxCode=@ApiTax, LastIssuanceRelevantChangeAtUtc=@Now, UpdatedAtUtc=@Now, UpdatedBy=NULL
    FROM dbo.InvoiceHeads h JOIN @Drafts d ON d.Id=h.Id WHERE h.StoreId=@StoreId;
    DECLARE @UpdatedDrafts int = @@ROWCOUNT;

    -- Compare credentials in memory, without printing them or changing their encryption.
    IF NOT EXISTS (SELECT 1 FROM dbo.InvoiceProviderSettings WHERE Id=@SettingId AND StoreId=@StoreId AND SupplierTaxCode=@ApiTax
        AND CONVERT(varbinary(max),Username)=CONVERT(varbinary(max),@Username)
        AND CONVERT(varbinary(max),[Password])=CONVERT(varbinary(max),@Password)
        AND TemplateCode=@Template AND InvoiceSeries=@Series)
        THROW 51040, 'Configuration verification failed; repair rolled back.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.LegalEntities WHERE StoreId=@StoreId AND Id=@LegalEntityId AND TaxCode=@DisplayTax
        AND InvoiceProviderSettingId=@SettingId)
        THROW 51040, 'HKD verification failed; repair rolled back.', 1;
    IF EXISTS (SELECT 1 FROM @Drafts d JOIN dbo.InvoiceHeads h ON h.Id=d.Id WHERE h.SupplierTaxCode<>@ApiTax OR h.ProviderStatus<>0)
        THROW 51040, 'Draft verification failed; repair rolled back.', 1;
    COMMIT TRANSACTION;
    SELECT @StoreId AS StoreId, @LegalEntityId AS LegalEntityId, @SettingId AS SettingId,
        @Username AS LoginUnchanged, @ApiTax AS ApiTaxCode, @DisplayTax AS SellerTaxCodeUnchanged,
        @UpdatedDrafts AS UpdatedLocalDrafts, CAST(1 AS bit) AS CredentialsUnchanged;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
