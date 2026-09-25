"""Read-only audit output -> reviewable table plan. Does not connect to SQL."""
from pathlib import Path
import json

root=Path(__file__).resolve().parents[3]
audit=root/'.artifacts/migration-review/discussion'
out=root/'scripts/migration/initial-import'
tables=json.loads((audit/'report-01.json').read_text(encoding='utf-8-sig'))
fks=json.loads((audit/'report-02.json').read_text(encoding='utf-8-sig'))
tables.append(dict(TableName='LegacyReturnArchives',RowCount=0))  # New schema, not yet applied to reference TEST.

keep={
 '__EFMigrationsHistory':'Lịch sử schema; không xóa hoặc reseed.',
 'Stores':'Cửa hàng 1 và cấu hình vận hành hiện có.',
 'LegalEntities':'Chủ thể 1 và liên kết cấu hình/kho.',
 'Warehouses':'Kho 1 và liên kết chủ thể.',
 'LegalEntityActivationEvents':'Lịch sử kích hoạt chế độ chủ thể; giữ cùng cấu hình để tránh lệch trạng thái.',
 'Users':'Tài khoản, nhân viên, mật khẩu và trạng thái; không chép đè từ GaoStore.',
 'UserInStores':'Phân công nhân viên vào cửa hàng và role.',
 'Roles':'Vai trò hiện có.',
 'Permissions':'Danh mục quyền hệ thống.',
 'RolePermissions':'Phân quyền theo vai trò.',
 'AdminMenuItems':'Menu và cấu trúc menu cha/con.',
 'RewardSettings':'Cấu hình tích điểm; không bao gồm số dư khách TEST.',
 'InvoiceProviderSettings':'Cấu hình nhà cung cấp hóa đơn; chỉ giữ, không phát hành khi chuyển.',
 'StoreBankAccounts':'Tài khoản ngân hàng cấu hình.',
 'StoreAcbSettings':'Cấu hình kết nối ngân hàng.',
 'AcbCallbackRoutes':'Cấu hình định tuyến callback ngân hàng.',
 'AcbCallbackRouteChanges':'Lịch sử thay đổi cấu hình định tuyến; không phải giao dịch thu tiền.',
 'POSTerminals':'Máy/quầy POS, gồm mã LEGACY-KET và LEGACY-UNKNOWN đang có.',
 'POSTerminalDevices':'Liên kết thiết bị với quầy POS.',
 'PosReceiptTemplates':'Mẫu in hóa đơn bán lẻ.',
 'ProductLabelPrinters':'Cấu hình máy in tem.',
 'ProductLabelTemplates':'Mẫu tem.',
 'Brands':'Danh mục tham chiếu; gói sản phẩm hiện không chép vào bảng này.',
 'Taxes':'Danh mục thuế suất tham chiếu.',
 'Attribute':'Định nghĩa thuộc tính sản phẩm.',
 'AttributeValue':'Giá trị thuộc tính; liên kết với variant TEST sẽ xóa riêng.',
}
receive={
 'Category':'01 sản phẩm', 'Suppliers':'01 sản phẩm', 'Unit':'01 sản phẩm',
 'Products':'01 sản phẩm','ProductVariant':'01 sản phẩm','ProductUnitConversion':'01 sản phẩm',
 'ProductVariantUnitBarcode':'01 sản phẩm','ProductImages':'01 ảnh sản phẩm',
 'Customers':'02 khách hàng','CustomerRewardLedgers':'02 tích lũy',
 'CustomerRewardVouchers':'02 voucher',
 'POSShifts':'03 ca legacy và ca thay thế có ghi nguồn',
 'Orders':'03 đơn bán','OrderLines':'03 chi tiết đơn bán','OrderPayments':'03 thanh toán',
 'SalesReturns':'03 trả hàng có liên kết','SalesReturnLines':'03 trả hàng có liên kết',
 'SalesReturnPayments':'03 hoàn tiền theo cách đã chuyển TEST',
 'LegacyReturnArchives':'03 lưu nguyên bản phiếu trả thiếu liên kết để tra cứu (bảng mới)',
 'StockDocument':'04 phiếu nhập','StockDocumentLine':'04 chi tiết phiếu nhập',
 'InventoryTransactions':'04 lịch sử kho','InventoryValuationEntries':'04 giá vốn',
 'InventoryCostLayers':'04 lớp giá vốn','InventoryCostLayerAllocations':'04 phân bổ giá vốn',
 'InventoryBalances':'04 tồn kho',
 'InvoiceInputStockSupplementalMovements':'05 tồn hóa đơn',
 'InvoiceHeads':'06 toàn bộ hóa đơn','InvoiceDetails':'06 chi tiết hóa đơn',
}
known={r['TableName'] for r in tables}
assert set(keep)<=known and set(receive)<=known
plan=[]
for row in tables:
 name=row['TableName']
 if name in keep: action='KEEP';reason=keep[name];step='—'
 elif name=='MediaAssets':
  action='SELECTIVE';reason='Bảng dùng chung: giữ tài nguyên cấu hình/logo; chỉ dọn metadata ảnh sản phẩm TEST sau khi kiểm tra tham chiếu. Không xóa toàn bộ file uploads.';step='01 ảnh sản phẩm'
 else:
  action='CLEAR';step=receive.get(name,'Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt')
  reason='Dữ liệu nghiệp vụ TEST hoặc bảng phụ thuộc; làm rỗng theo thứ tự khóa ngoại.'
  if name in ('Promotions','PromotionItems','PromotionComboRule','DisplayPromotions'):
   reason='Chương trình khuyến mãi TEST gắn với danh mục cũ; không giữ liên kết Product/Variant TEST. Quy đổi và giá từ Promotion nguồn được xử lý ở bước 01.'
  if name in ('CustomerDebtReceipts','CustomerReceivableEntries','CustomerDeposits','CustomerDepositEntries'):
   step='Không chuyển công nợ cũ (đã chốt); số dư đặt cọc cũ chưa nằm trong phạm vi các gói hiện tại'
  if name in ('OrderNumberSequences','DocumentNumberSequences'):
   reason='Bộ đếm chứng từ TEST; khởi tạo lại phù hợp sau import, kiểm tra không trùng số mới.'
 plan.append(dict(Table=name,Action=action,TestRows=row['RowCount'],Reason=reason,ReceiveStep=step))

# A retained table must not lose an FK parent when business rows are cleared.
cross=[]
actions={r['Table']:r['Action'] for r in plan}
for fk in fks:
 if actions[fk['ChildTable']]=='KEEP' and actions[fk['ParentTable']]=='CLEAR': cross.append(fk)
(out/'TABLE-PLAN.json').write_text(json.dumps(dict(Status='DISCUSSION_ONLY_NOT_AN_EXECUTION_SCRIPT',Tables=plan,RetainedToClearedForeignKeys=cross,OptionalMigrationMetadataTables=['GaoStoreMigrationRunsV2'],BlockCleanupWhenMigrationReceiptsExist=True),ensure_ascii=False,indent=2),encoding='utf-8')

lines=['# Kế hoạch bảng GaoApp trước lần chuyển đầu tiên','',
'Trạng thái: **bản đối chiếu để thảo luận; chưa chạy dọn dữ liệu**. Kiểm kê trên `GaoAppDb` TEST ngày 23/09/2026. Số dòng chỉ là bằng chứng TEST, không dùng làm điều kiện cứng cho dữ liệu thật.','',
'Người dùng đã chốt: giữ cửa hàng/chủ thể/kho ID 1, menu, tài khoản/nhân viên và phân quyền; giữ cấu hình tích điểm, hóa đơn, ngân hàng/QR, POS và mẫu in. Làm rỗng dữ liệu nghiệp vụ TEST cùng các bảng liên quan trước lần chuyển đầu tiên.','',
'## Nhóm giữ nguyên','',
'| Bảng | Dòng TEST | Nội dung giữ |','|---|---:|---|']
for r in plan:
 if r['Action']=='KEEP': lines.append(f"| `{r['Table']}` | {r['TestRows']:,} | {r['Reason']} |")
lines+=['','## Nhóm làm rỗng','',
'Danh sách tường minh; không dùng quy tắc “xóa mọi bảng trừ nhóm giữ”. Nếu schema có bảng mới chưa phân loại, công cụ phải dừng để kiểm tra. Làm rỗng cả bảng không được chép lại để chứng từ, số dư và liên kết TEST không ảnh hưởng dữ liệu mới.','',
'| Bảng | Dòng TEST | Bước nhận dữ liệu cũ |','|---|---:|---|']
for r in plan:
 if r['Action']=='CLEAR':lines.append(f"| `{r['Table']}` | {r['TestRows']:,} | {r['ReceiveStep']} |")
lines+=['','## Bảng dùng chung','',
'`MediaAssets` cần xử lý theo bản ghi: giữ logo/tài nguyên cấu hình, xác định ảnh sản phẩm TEST qua `ProductImages` và kiểm tra tham chiếu trước khi dọn metadata. Thư mục uploads không nằm trong lệnh xóa toàn bộ.','',
'## Điều kiện trước khi viết/chạy bước dọn','',
'- Báo cáo đúng server/database đích, số dòng từng bảng, tập bảng giữ và tập bảng dọn.',
'- Có backup đích kiểm chứng khôi phục được; dừng các tiến trình ghi GaoApp khi thực hiện.',
'- Chạy thử trên bản sao TEST riêng; không dùng TEST đang đối chiếu làm nơi thử xóa.',
'- Mọi thao tác dọn nằm trong transaction; lỗi thì rollback. Trước commit phải xác nhận nhóm dọn rỗng, nhóm giữ không thay đổi, khóa ngoại còn hợp lệ.',
'- Không dọn lại khi một gói đã được import. Lần chạy lại chỉ kiểm tra/chạy tiếp đúng bước, không tự reset.',
'- `GaoStoreMigrationRunsV2` là journal do bộ chuyển tạo khi COMMIT, không có trong kiểm kê TEST gốc. Không xóa journal để vượt kiểm tra chạy lại; nếu có receipt đã chuyển, bước dọn phải dừng.',
'- Giữ schema và lịch sử EF; không xóa database hoặc tự chạy seed làm thay đổi cấu hình đã giữ.',
f'- Kiểm kê hiện tại: {len(tables)} bảng; {len(keep)} bảng giữ nguyên; {sum(r["Action"]=="CLEAR" for r in plan)} bảng dọn toàn bộ; 1 bảng dọn có chọn lọc.',
f'- Khóa ngoại từ nhóm giữ sang nhóm dọn phát hiện trong schema hiện tại: {len(cross)}. Vẫn phải kiểm tra tham chiếu nằm trong JSON/chuỗi và tài nguyên dùng chung trước khi thực hiện.','',
'## Quyết định đã chốt ở bước 03','',
'Dữ liệu TEST hiện có 389.661 đơn `LEGACY-*`, tất cả Completed/Paid và BalanceDue=0; trong đó 3.633 đơn nguồn có HaveDebt=1. Bảng Debt nguồn có 355 khách có Total ở dòng ID cuối dương; đây là số dư theo bảng nguồn, chưa phải kết luận đối soát thực tế. Người dùng đã chốt: **không chuyển công nợ cũ**. Không tái tạo Debt nguồn thành số dư phải thu hay phiếu thu nợ trên GaoApp. Dọn công nợ TEST trước import theo phạm vi đã đồng ý.',
'',
'Nguồn có 3.685 đầu phiếu trả hàng Category=3; TEST có 117 `LEGACY-RETURN-*`. 3.568 phiếu còn lại thiếu OrderIDMuaHang. Người dùng đã chốt: **lưu nhóm thiếu liên kết riêng để tra cứu**. Giữ header/chi tiết nguồn, mã phiếu, ngày, khách hàng, nhân viên và trạng thái nguồn; không tạo đơn bán giả, không tự ghi nhập kho hay hoàn tiền từ nhóm lưu trữ. Số lượng thực tế được tính từ snapshot chạy, không hard-code 3.568.',
'',
'Nhân viên và thiết bị: TEST đang ánh xạ nhân viên theo ID cũ; hiện 389.661/389.661 đơn có CreatedBy trùng UserID nguồn và tồn tại trong Users. Dữ liệu thật phải kiểm tra thêm danh tính, không xem trùng số ID là đủ. Ca thực lấy ManagementJob, ca thiếu nguồn dùng ca legacy thay thế như TEST; không biến các ca lịch sử thành ca bán đang mở.',
'',
'Một gói trả hàng khác được tìm thấy tại `D:/GaoApp/Chuyển Dữ Liệu/GAOAPP-RETURN-FULL-DATA-FINAL-PACKAGE.zip`; chưa chạy. Gói này không ghi SalesReturnPayments, trong khi TEST hiện có 117 khoản hoàn tiền. Theo yêu cầu giữ cách TEST, không dùng gói tìm thấy để ghi đè quy tắc hiện tại.']
(out/'TABLE-PLAN.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(f'Table plan: {len(tables)} tables; keep={len(keep)}, clear={sum(r["Action"]=="CLEAR" for r in plan)}, selective=1, keep-to-clear FK={len(cross)}')
