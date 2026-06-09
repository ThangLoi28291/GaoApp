# GaoApp Roadmap

## Đã có trong source hiện tại

### Nền tảng

- Clean Architecture 4 project
- Multi-store tenant theo host/subdomain
- Cookie authentication
- Role / Permission
- Audit log
- Health check
- Serilog
- Upload / storage local

### Catalog / Product

- Category
- Brand
- Supplier
- Tax
- Unit
- Product
- ProductVariant
- ProductImage / MediaAsset
- ProductUnitConversion
- ProductVariantUnitBarcode
- Barcode history
- Barcode verification / governance

### POS / Order

- POS screen
- Current cart / draft order
- Hold/resume order
- Barcode scan
- Customer search/create/set/clear
- Payment
- Finalize
- Receipt
- Void
- Refund / SalesReturn
- POS realtime hub
- POS JS tách nhiều file

### Promotion / Reward

- Promotion admin
- Promotion engine
- Product discount
- Combo fixed price
- Buy X Get Y nền tảng entity/service
- Reward settings
- Customer reward ledger
- Customer reward voucher
- Apply voucher to cart
- Print / lock / unlock / cancel voucher

### Shift

- Open/close shift
- Cash in/out
- Ownership / takeover / force close
- Handover slip
- Closing slip
- Manager dashboard
- Print pages

### Inventory

- Warehouse
- Inventory balance
- Inventory transaction
- Inventory movement
- Stock document
- Stock count
- Stock transfer
- Inventory adjustment
- Inventory reservation
- Cost layer / valuation
- Negative inventory log
- Order inventory issue
- Input invoice mapping

## Nên ưu tiên tiếp theo

### Phase A - Chốt POS + Promotion

1. Hoàn thiện Buy X Get Y theo dòng tặng riêng.
2. Test đổi số lượng / xóa dòng chính / thanh toán / in bill.
3. Tối ưu query POS khi quét mã.
4. Chốt rule reward không tính hàng tặng.
5. Chốt rule tồn kho với hàng tặng.

### Phase B - Kiểm thử kho và giá vốn

1. Test nhập kho theo đơn vị quy đổi.
2. Test xuất kho từ POS theo base quantity.
3. Test void/refund trả tồn.
4. Kiểm tra FIFO / provisional cost.
5. Kiểm tra báo cáo tồn kho.

### Phase C - Quản trị và báo cáo

1. Báo cáo doanh thu theo ca/ngày.
2. Báo cáo lợi nhuận theo giá vốn snapshot.
3. Báo cáo tồn âm / tồn lỗi.
4. Xuất Excel chuẩn.
5. In phiếu quản lý.

### Phase D - Ổn định vận hành IIS

1. Tách môi trường Development/Production rõ.
2. Không auto migrate ở Production.
3. Cấu hình logging theo ngày.
4. Backup database.
5. Kiểm tra tenant/domain/proxy.
