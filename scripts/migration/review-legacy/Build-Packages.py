"""Generate reviewed initial-import packages; originals are audit inputs only."""
from pathlib import Path
import re,json,hashlib
ROOT=Path(__file__).resolve().parent
OUT=ROOT.parent/'initial-import'
OUT.mkdir(exist_ok=True)
orig=ROOT/'original'
product=next(orig.rglob('03-PRODUCT-RESET-IMPORT-v4.sql')).read_text(encoding='utf-8-sig')
customer=next(orig.rglob('02-CUSTOMER-RESET-IMPORT-FINAL.sql')).read_text(encoding='utf-8-sig')
stock=next(orig.rglob('*REAL-COMMIT-v1.0.sql')).read_text(encoding='utf-8-sig')

def replace_once(s,old,new):
    assert s.count(old)==1,(old[:90],s.count(old))
    return s.replace(old,new,1)

def qualify(s):
    # Preserve context from USE without actually changing the connection database.
    context='__TARGET__'; result=[]
    for line in s.splitlines():
        m=re.fullmatch(r'USE\s+\[?(DataGaoStore|GaoAppDb)\]?;',line.strip(),re.I)
        if m:
            context='__SOURCE__' if m[1]=='DataGaoStore' else '__TARGET__'
            continue
        line=line.replace('DataGaoStore.dbo.','[__SOURCE__].dbo.')
        if context=='__SOURCE__':
            line=re.sub(r'(?<!\.)\bdbo\.', '[__SOURCE__].dbo.',line)
        result.append(line)
    return '\n'.join(result)

def output(name,sql,source,target):
    folder=OUT/name; folder.mkdir(exist_ok=True)
    sql=qualify(sql)
    assert not re.search(r'(?im)^\s*(DELETE\s+(?:FROM\s+)?dbo\.|TRUNCATE|.*NOCHECK CONSTRAINT|COMMIT TRAN|ROLLBACK TRAN|BEGIN TRAN)',sql)
    sql="-- Reviewed initial import. Run only through Invoke-Migration.ps1.\nIF @@TRANCOUNT<>1 OR ISNULL(TRY_CONVERT(int,SESSION_CONTEXT(N'GSTORE_INITIAL_IMPORT')),0)<>1\n    THROW 55100,'Run through the transactional package runner.',1;\n"+sql
    (folder/'Migration.sql').write_text(sql,encoding='utf-8-sig')
    (folder/'package.json').write_text(json.dumps(dict(PackageId='GSTORE-'+name+'-V2',SourceTables=source,TargetTables=target),indent=2),encoding='utf-8')

# Product: retain transformations, eliminate recursive reset and constraint disabling.
product=product[product.index('SET NOCOUNT ON;'):]
product=re.sub(r'DECLARE @ApprovalToken.*?/\* ===', 'DECLARE @StoreId int=1;\n/* ===',product,count=1,flags=re.S)
start=product.index('/* ============================================================\n   3. BUILD FK-AWARE')
end=product.index('    /* ---------- Category ---------- */',start)
product=product[:start]+"IF @Mode='PREVIEW'\nBEGIN\n SELECT N'PRODUCT_PLAN' Report,(SELECT COUNT_BIG(*) FROM #FinalProduct) Products,(SELECT COUNT_BIG(*) FROM #AltConv) AlternateConversions,(SELECT COUNT_BIG(*) FROM #UnitManifest) Units;\n RETURN;\nEND;\n"+product[end:]
start=product.index('    /* Reseed explicit-ID masters')
end=product.index('    /* Core in-transaction invariants',start)
product=product[:start]+product[end:]
product=product[:product.index('    IF @CommitChanges = 1')]+"\nSELECT N'PRODUCT_STAGED_AND_VERIFIED' Report,COUNT_BIG(*) Products FROM dbo.Products;\n"
# Fix fallback UTC being shifted twice for unknown source dates.
product=product.replace('DATEADD(hour,-7,COALESCE(s.CreatedDate,s.ModifiedDate,SYSUTCDATETIME()))','COALESCE(DATEADD(hour,-7,s.CreatedDate),DATEADD(hour,-7,s.ModifiedDate),SYSUTCDATETIME())')
product=product.replace('DATEADD(hour,-7,COALESCE(fp.CreatedDate,SYSUTCDATETIME()))','COALESCE(DATEADD(hour,-7,fp.CreatedDate),SYSUTCDATETIME())')
product=product.replace('DATEADD(hour, -7, COALESCE(n.FirstCreatedDate, SYSUTCDATETIME()))','COALESCE(DATEADD(hour,-7,n.FirstCreatedDate),SYSUTCDATETIME())')
# Existing approved supplier substitutions apply only while the original reference is invalid.
product=product.replace('WHEN pd.Id = 143415 THEN 40042','WHEN pd.Id = 143415 AND NOT EXISTS(SELECT 1 FROM dbo.Supplier s WHERE s.ID=pd.SupplierID) THEN 40042')
product=product.replace('WHEN pd.Id = 291459 THEN 11','WHEN pd.Id = 291459 AND NOT EXISTS(SELECT 1 FROM dbo.Supplier s WHERE s.ID=pd.SupplierID) THEN 11')
product+='\n'+(OUT/'01-products/TechnicalCatalog.sql').read_text(encoding='utf-8-sig')
output('01-products',product,['ProductDetail','Product','Promotion','Supplier','Order','OrderDetail'],['Category','Suppliers','Unit','Products','ProductVariant','ProductUnitConversion','ProductVariantUnitBarcode'])

# Customer: insert only; caller owns transaction and controls commit.
customer=customer[customer.index('USE [GaoAppDb];'):]
customer=re.sub(r'IF @@TRANCOUNT <> 0\s+THROW[^;]+;','',customer,count=1)
start=customer.index('BEGIN TRY\n    BEGIN TRANSACTION;')
end=customer.index('    SET IDENTITY_INSERT dbo.Customers ON;',start)
customer=customer[:start]+"IF @Mode='PREVIEW' RETURN;\n"+customer[end:]
customer=customer[:customer.index('    COMMIT TRANSACTION;')]
# Detect accidental truncation before explicit CONVERT can lose source text.
guard="""
IF EXISTS(SELECT 1 FROM DataGaoStore.dbo.[User] u WHERE GroupID IN('MEMBER','WHOLESALE') AND
 (LEN(COALESCE(NULLIF(LTRIM(RTRIM(Name)),N''),LTRIM(RTRIM(Code))))>200
 OR LEN(LTRIM(RTRIM(Address)))>300 OR LEN(LTRIM(RTRIM(Code)))>50
 OR LEN(LTRIM(RTRIM(Email)))>100 OR LEN(LTRIM(RTRIM(MST)))>50))
 THROW 55101,'Customer source text exceeds target lengths; review before import.',1;
IF EXISTS(SELECT 1 FROM DataGaoStore.dbo.UserPoint WHERE LEN(Description)>1000)
 THROW 55102,'Voucher description exceeds target length; review before import.',1;
"""
customer=customer.replace('DROP TABLE IF EXISTS #CustomerStage;',guard+'\nDROP TABLE IF EXISTS #CustomerStage;',1)
output('02-customers',customer,['User','UserPoint'],['Customers','CustomerRewardLedgers','CustomerRewardVouchers'])

# Inventory: preserve the approved replay algorithm, but no clearing or reset.
stock=stock[stock.index('DECLARE @MigrationExecutionUtc'):]
start=stock.index('IF EXISTS(SELECT 1 FROM dbo.StockDocument WHERE Type<>1)')
end=stock.index('IF NOT EXISTS\n(',start)
stock=stock[:start]+stock[end:]
start=stock.index('IF\n(\n       @BeforeStockDocuments')
end=stock.index('-- Dynamic positive ID bases.',start)
stock=stock[:start]+stock[end:]
start=stock.index('IF (SELECT COUNT_BIG(*) FROM #EventRaw WHERE EventKind=3 AND IsNegativeHeaderException=1)<>13')
end=stock.index('CREATE TABLE #Event\n',start)
stock=stock[:start]+"DECLARE @ExpectedNegativeHeaderLines bigint=(SELECT COUNT_BIG(*) FROM #EventRaw WHERE EventKind=3 AND IsNegativeHeaderException=1);\n\n"+stock[end:]
stock=stock.replace("ReferenceId=N'LEGACY-SALE-EXC')<>13","ReferenceId=N'LEGACY-SALE-EXC')<>@ExpectedNegativeHeaderLines")
# Staging is read-only; eliminate old delete-impact/side-effect capture section.
start=stock.index('CREATE TABLE #OldDoc')
end=stock.index('-- Recheck source + target immediately before persistent DML.',start)
stock=stock[:start]+"IF @Mode='PREVIEW' RETURN;\n\n"+stock[end:]
start=stock.index('BEGIN TRY\n    BEGIN TRANSACTION;')
end=stock.index('    SET IDENTITY_INSERT dbo.StockDocument ON;',start)
stock=stock[:start]+stock[end:]
# Original post-commit checks are run BEFORE the runner commits; no postcommit false success.
start=stock.index('    COMMIT TRANSACTION;')
end=stock.index('-- J. POST-COMMIT VERIFICATION',start)
stock=stock[:start]+stock[end:]
stock=stock.replace('!=13.','differs from staged approved exceptions.')
stock=stock.replace('REAL_COMMIT','INITIAL_IMPORT').replace('post-COMMIT','before-runner-COMMIT')
stock=stock.replace('REAL COMMIT PASS: Step4 v2 full clear + reload committed. Run REAL POSTVERIFY v1.0 next.','STAGED_VERIFIED: transaction is still pending; runner decides COMMIT or ROLLBACK.')
stock=stock.replace('canonical Step4 v2 committed.','Step4 staged; runner controls final transaction.')
output('04-inventory',stock,['ProductDetail','Product','Order','OrderDetail','HoaDonNhap','ChiTietNhapKho'],['StockDocument','StockDocumentLine','InventoryTransactions','InventoryValuationEntries','InventoryCostLayers','InventoryCostLayerAllocations','InventoryBalances'])
print('Generated',OUT)
