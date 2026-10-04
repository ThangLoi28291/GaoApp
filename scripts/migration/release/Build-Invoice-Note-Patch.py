from pathlib import Path
import json, hashlib, shutil, datetime

repo = Path(__file__).resolve().parents[3]
release = repo / 'scripts/migration/release'
source = (release / 'Install-Inventory-Negative-Patch.ps1').read_text(encoding='utf-8-sig')
changes = {
    "'EMPLOYEE-MAPPING-20260929-V1'": "'INVENTORY-NEGATIVE-20260929-V1'",
    "AppliedPatch 'INVENTORY-NEGATIVE-20260929-V1' -Force": "AppliedPatch 'INVOICE-NOTES-20260929-V1' -Force",
    "Patch='INVENTORY-NEGATIVE-20260929-V1'": "Patch='INVOICE-NOTES-20260929-V1'",
    'with the employee patch, before the inventory exception patch.': 'with the inventory exception patch, before the invoice note patch.',
    'employee-patched baseline': 'inventory-patched baseline',
    '^(04-inventory|05-invoice-stock|06-invoices)': '^(06-invoices)',
    'Inventory or a later step already committed.': 'Invoice migration already committed.',
    "@('01-products','02-customers','03-sales','03-return-archive')": "@('01-products','02-customers','03-sales','03-return-archive','04-inventory')",
    "@{Payload='Migration.sql';Relative='scripts/migration/initial-import/04-inventory/Migration.sql'}": "@{Payload='InvoiceMigration.sql';Relative='scripts/migration/invoices/InvoiceMigration.sql'},\n @{Payload='Build-Sql.py';Relative='scripts/migration/invoices/Build-Sql.py'}",
    'Inventory-negative-patch-': 'Invoice-note-patch-',
    'ApprovedNegativeOrders=@(1825885,1832994);ExpectedMerchandiseLines=4;ExpectedQuantityOut=11': "ApprovedLegacyInvoiceIds=@(1828849,1828927);ExpectedOriginalNoteLength=519;RawNotesPreserved=$true",
    '# Keep all four completed package receipts and all reset/backup evidence.': '# Keep every completed import and all reset/backup evidence.',
    "@('04-inventory-PREVIEW','04-inventory-DRYRUN','FinalChainVerified')": "@('06-invoices-PREVIEW','06-invoices-DRYRUN','FinalChainVerified')",
    'InventoryNegativePatchEvidence': 'InvoiceNotePatchEvidence',
    'InventoryNegativePatch/FILES_ONLY': 'InvoiceNotePatch/FILES_ONLY',
    'INVENTORY_NEGATIVE_PATCH_PASS': 'INVOICE_NOTE_PATCH_PASS',
    'Run 04-inventory PREVIEW, then DRYRUN.': 'Run 06-invoices PREVIEW, then DRYRUN.'
}
for old,new in changes.items():
    if old not in source: raise Exception('Installer template changed: '+old)
    source=source.replace(old,new)
source=source.replace("PreviousAppliedPatch 'INVOICE-NOTES-20260929-V1'", "PreviousAppliedPatch 'INVENTORY-NEGATIVE-20260929-V1'")
stock_check=r'''
# The invoice-stock runner has Reports/CSV receipts rather than Package/Status fields.
$stockSqlEntry=@($manifest.Files|Where-Object {$_.Path.Replace('\','/') -ceq 'scripts/migration/invoice-input-stock/InvoiceInputStockMigration.sql'})[0]
foreach($mode in @('COMMIT','VERIFY')){
 $key='05-invoice-stock-'+$mode
 if($state.PSObject.Properties.Name -notcontains $key){throw "Missing successful $key."}
 $report=Get-Content -LiteralPath (Join-Path $state.$key 'manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
 if($report.Mode -cne $mode -or $report.Server -ine $config.Server -or
  $report.SourceDatabase -cne $config.SourceDatabase -or $report.TargetDatabase -cne $config.TargetDatabase -or
  $report.StoreId -ne 1 -or $report.WarehouseId -ne 1 -or $report.SqlSha256 -cne $stockSqlEntry.Sha256){
  throw "Invoice-stock receipt contract differs for $mode. No files changed."
 }
 $pass=@($report.Reports|Where-Object {$_.Report -ceq ($mode+'_PASS')})
 if($pass.Count -ne 1 -or $pass[0].Rows -ne 1 -or $pass[0].File -cnotmatch ('^\d+-'+$mode+'_PASS\.csv$')){
  throw "Invoice-stock $mode does not have exactly one success report."
 }
 $passRows=@(Import-Csv -LiteralPath (Join-Path $state.$key $pass[0].File) -Encoding UTF8)
 if($passRows.Count -ne 1 -or $passRows[0].Report -cne ($mode+'_PASS')){throw 'Invoice-stock success CSV does not match its manifest.'}
}
'''
source=source.replace('$replacements=@(',stock_check+'\n$replacements=@(',1)
installer=release/'Install-Invoice-Note-Patch.ps1'
installer.write_text(source,encoding='utf-8-sig')
dest=repo/'.artifacts/invoice-note-patch-20260929'
dest.mkdir(exist_ok=False)
for src,name in [(installer,installer.name),(release/'Verify-Package.ps1','Verify-Package.ps1'),
 (repo/'scripts/migration/invoices/InvoiceMigration.sql','InvoiceMigration.sql'),
 (repo/'scripts/migration/invoices/Build-Sql.py','Build-Sql.py')]:
    shutil.copyfile(src,dest/name)
inventory=repo/'.artifacts/inventory-negative-patch-20260929'
baseline=json.loads((inventory/'expected-package-files.json').read_text(encoding='utf-8-sig'))
for entry in baseline['Files']:
    if entry['Path']=='scripts/migration/initial-import/04-inventory/Migration.sql':
        data=(inventory/'Migration.sql').read_bytes()
        entry.update(Bytes=len(data),Sha256=hashlib.sha256(data).hexdigest().upper())
(dest/'expected-package-files.json').write_text(json.dumps(baseline,indent=2),encoding='utf-8-sig')
(dest/'Read-Original-Invoice-Notes.sql').write_text('''-- Read-only, run in SSMS after invoice COMMIT to see both complete original notes.
USE [GaoAppDb];
SELECT h.Id AS GaoAppInvoiceId,h.LegacySourceId,h.Note AS DisplayNote,
       LEN(raw.Note) AS OriginalLength,raw.Note AS OriginalNote
FROM dbo.InvoiceHeads h
OUTER APPLY OPENJSON(h.LegacySnapshotJson) WITH(Note nvarchar(max)) raw
WHERE h.StoreId=1 AND h.LegacySourceId IN(1828849,1828927)
ORDER BY h.LegacySourceId;
''',encoding='utf-8-sig')
(dest/'HUONG-DAN.md').write_text('''# Bản vá ghi chú hai hóa đơn — 29/09/2026

Chỉ áp dụng C:\\GaoMigration-20260929, WIN-HU6RO2EMIJF\\SQLEXPRESS,
DataGaoStore -> GaoAppDb. Yêu cầu mọi bước đến 05-invoice-stock đã COMMIT + VERIFY,
và chưa COMMIT 06-invoices. Giữ Web/worker và ghi nguồn dừng.

Hai hóa đơn nguồn 1828849, 1828927: nhóm 12, ngày tạo 12/09/2026,
chưa có số/ngày phát hành, Note dài 519 ký tự. Chỉ hai ID này được rút gọn hiển thị.
Note đích chứa phần đầu + “... [Xem ghi chú gốc]” (dấu ba chấm Unicode), tối đa 500 đơn vị UTF-16.
Toàn bộ ghi chú gốc giữ nguyên trong LegacySnapshotJson và CSV LONG_NOTE_ADJUSTMENTS.
Không sửa ngày, số tiền, trạng thái hoặc dữ liệu nguồn. Không gọi API phát hành.
Màn hình hiện chưa có nút mở ghi chú gốc: dấu nhắc là văn bản, không phải liên kết.
Muốn xem đầy đủ: mở file *-LONG_NOTE_ADJUSTMENTS.csv trong thư mục bằng chứng,
hoặc chạy Read-Original-Invoice-Notes.sql bằng SSMS sau khi COMMIT hóa đơn.

1. Giải nén vào C:\\GaoMigration-20260929\\invoice-note-patch.
2. Chạy:

```powershell
Set-Location 'C:\\GaoMigration-20260929'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\\invoice-note-patch\\Install-Invoice-Note-Patch.ps1
```

Kỳ vọng INVOICE_NOTE_PATCH_PASS. Installer chỉ thay InvoiceMigration.sql và Build-Sql.py,
cập nhật checksum/state và sao lưu bản cũ trong runs/real-01. Không kết nối SQL.
Không reset hoặc chạy lại bước đã COMMIT. Python không cần cài trên server.

3. Chạy lại PREVIEW:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\\Run-Step.ps1 -Step 06-invoices -Mode PREVIEW
```

Kiểm tra LONG_NOTE_ADJUSTMENTS: 2 dòng, OriginalLength=519, DisplayLength<=500,
OriginalNotePreserved=True. CSV chứa cả OriginalNote đầy đủ và DisplayNote.
Xem COUNTS/PLAN/QUALITY và chờ PREVIEW_ONLY trước khi tiến hành DRYRUN.
Bản vá kiểm tra offline; PREVIEW/DRYRUN trên server mới xác nhận dữ liệu thực tế.
''',encoding='utf-8-sig')
entries=[]
for f in sorted(dest.iterdir()):
    b=f.read_bytes();entries.append(dict(Path=f.name,Bytes=len(b),Sha256=hashlib.sha256(b).hexdigest().upper()))
(dest/'checksums.json').write_text(json.dumps(dict(CreatedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),Files=entries),indent=2),encoding='utf-8-sig')
print(dest)
