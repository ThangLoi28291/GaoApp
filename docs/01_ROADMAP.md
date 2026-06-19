# GaoApp Roadmap

## Đã có trong source hiện tại

### Nền tảng

* Clean Architecture 4 project
* Multi-store tenant theo host/subdomain
* Cookie authentication
* Role / Permission
* Audit log
* Health check
* Serilog
* Upload / storage local

### Catalog / Product

* Category
* Brand
* Supplier
* Tax
* Unit
* Product
* ProductVariant
* ProductImage / MediaAsset
* ProductUnitConversion
* ProductVariantUnitBarcode
* Barcode history
* Barcode verification / governance

### POS / Order

* POS screen
* Current cart / draft order
* Hold/resume order
* Barcode scan
* Customer search/create/set/clear
* Payment
* Finalize
* Receipt
* Void
* Refund / SalesReturn
* POS realtime hub
* POS JS tách nhiều file

### Promotion / Reward

* Promotion admin
* Promotion engine
* Product discount
* Combo fixed price
* Buy X Get Y nền tảng entity/service
* Reward settings
* Customer reward ledger
* Customer reward voucher
* Apply voucher to cart
* Print / lock / unlock / cancel voucher

### Shift

* Open/close shift
* Cash in/out
* Ownership / takeover / force close
* Handover slip
* Closing slip
* Manager dashboard
* Print pages

### Inventory

* Warehouse
* Inventory balance
* Inventory transaction
* Inventory movement
* Stock document
* Stock count
* Stock transfer
* Inventory adjustment
* Inventory reservation
* Cost layer / valuation
* Negative inventory log
* Order inventory issue
* Input invoice mapping

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

\## Roadmap cập nhật - Module hóa đơn điện tử Viettel SInvoice



\### Đã hoàn thành



```text

Phase 1  - Nền dữ liệu tích hợp Viettel: PASS

Phase 2  - Cấu hình Viettel Basic Auth: PASS

Phase 3  - Build JSON Viettel: PASS

Phase 4  - Preview PDF nháp: PASS

Phase 5  - Lưu log preview + iframe PDF: PASS

Phase 6  - Tạo hóa đơn nháp Viettel: BỎ QUA

Phase 7  - Phát hành hóa đơn thật: PASS

Phase 8  - Tải PDF/XML chính thức: PASS

Phase 9  - Tra cứu UUID / đồng bộ sau timeout: PASS

Phase 10 - Gửi email hóa đơn cho khách: PASS

Phase 13 - Màn quản trị log tích hợp: PASS

Phase 15 - Chống bấm nhầm / phát hành trùng / UI an toàn: PASS

```



Module hiện đã đủ luồng vận hành nội bộ:



```text

POS bán hàng

→ Tạo InvoiceHead

→ Sinh dòng hóa đơn

→ Xem JSON Viettel

→ Preview PDF nháp

→ Phát hành hóa đơn thật

→ Tra cứu UUID nếu lỗi/timeout

→ Tải PDF/XML chính thức

→ Gửi email hóa đơn cho khách

→ Theo dõi log tích hợp

```



\### Tạm bỏ qua



```text

Phase 6  - Tạo hóa đơn nháp Viettel

Phase 11 - Cập nhật / hủy trạng thái thanh toán Viettel

```



Lý do:



\* Phase 6 chưa cần thiết vì luồng hiện tại dùng preview nháp và phát hành thật.

\* Phase 11 chưa cần thiết ngay vì POS đã quản lý thanh toán nội bộ.



\### Làm sau



\#### Phase 14 - Dọn log định kỳ



Mục tiêu:



\* Giảm dung lượng bảng InvoiceIntegrationLogs.

\* Giữ log quan trọng lâu dài.

\* Dọn log preview không cần thiết sau một khoảng thời gian.



Dự kiến:



```text

\- PreviewDraft: giữ 30 ngày

\- SendEmail: giữ 90 ngày

\- DownloadPdf / DownloadZip: giữ 180 ngày

\- IssueInvoice / SearchByTransactionUuid: giữ dài hạn

\- Log lỗi: giữ dài hạn hoặc theo chính sách riêng

```



\---



\#### Phase 16 - Hủy / thay thế / điều chỉnh hóa đơn



Mục tiêu:



\* Xử lý hóa đơn đã phát hành nhưng bị sai.

\* Không sửa trực tiếp dữ liệu hóa đơn đã phát hành.

\* Làm đúng theo nghiệp vụ hóa đơn điện tử.



Dự kiến chức năng:



```text

\- Hủy hóa đơn

\- Lập hóa đơn thay thế

\- Lập hóa đơn điều chỉnh

\- Lưu quan hệ hóa đơn gốc và hóa đơn xử lý

\- Ghi log toàn bộ thao tác

```



\---



\#### Phase 17 - Đồng bộ danh sách hóa đơn từ Viettel



Mục tiêu:



\* Đối soát hóa đơn giữa GaoApp và Viettel.

\* Tìm hóa đơn GaoApp thiếu trạng thái hoặc thiếu số hóa đơn.

\* Phục hồi dữ liệu nếu có lỗi mạng hoặc timeout.



Dự kiến chức năng:



```text

\- Lọc theo ngày

\- Gọi getInvoices từ Viettel

\- So sánh theo transactionUuid / invoiceNo

\- Cập nhật ProviderStatus

\- Cập nhật ProviderInvoiceNo

\- Cập nhật IssuedAtUtc

\- Ghi log đồng bộ

```



\---



\#### Phase 18 - Dashboard hóa đơn điện tử



Mục tiêu:



\* Theo dõi tình hình hóa đơn nhanh trên màn quản trị.



Dự kiến chỉ số:



```text

\- Tổng hóa đơn đã phát hành hôm nay

\- Tổng hóa đơn phát hành lỗi

\- Tổng hóa đơn chưa tải PDF/XML

\- Tổng hóa đơn đã gửi email

\- Tổng hóa đơn cần tra cứu UUID

\- Thống kê theo ngày / tháng

```



\---



\#### Phase 20 - Bảo mật production



Mục tiêu:



\* Tăng an toàn khi dùng thật.



Dự kiến:



```text

\- Mã hóa password cấu hình Viettel

\- Không log password

\- Phân quyền riêng cho phát hành hóa đơn

\- Phân quyền riêng cho xem log request/response

\- Chống bấm phát hành nhiều lần

\- Giới hạn quyền mở khóa hóa đơn đã tạo

```
---

# Invoice / E-invoice Roadmap

## Đã hoàn thành

### Phase 1 - Nền dữ liệu hóa đơn

* Tạo `InvoiceHead`.
* Tạo `InvoiceDetail`.
* Sinh hóa đơn bán ra từ đơn POS.
* Map hóa đơn với đơn hàng.

Trạng thái: PASS

### Phase 2 - Cấu hình Viettel SInvoice

* Tạo cấu hình nhà cung cấp hóa đơn điện tử.
* Hỗ trợ Basic Auth.
* Lưu thông tin mẫu số, ký hiệu, MST nhà cung cấp.
* Chuẩn bị bảo mật cấu hình production.

Trạng thái: PASS

### Phase 3 - Build payload Viettel

* Build JSON hóa đơn gốc.
* Build thông tin người mua.
* Build dòng hàng.
* Build tổng tiền.
* Build thuế.

Trạng thái: PASS

### Phase 4 - Preview nháp

* Tạo preview PDF nháp từ Viettel.
* Hiển thị trong màn quản trị.

Trạng thái: PASS

### Phase 5 - Log tích hợp

* Lưu log request/response.
* Theo dõi thời gian gọi API.
* Lưu lỗi phát hành/preview/tra cứu.

Trạng thái: PASS

### Phase 7 - Phát hành hóa đơn thật

* Gửi hóa đơn thật lên Viettel.
* Lưu số hóa đơn.
* Lưu mã tra cứu, mã CQT, transaction.
* Khóa hóa đơn sau phát hành.

Trạng thái: PASS

### Phase 8 - Tải PDF/XML chính thức

* Tải PDF.
* Tải ZIP/XML.
* Lưu đường dẫn file vào `InvoiceHead`.

Trạng thái: PASS

### Phase 9 - Tra cứu UUID

* Tra cứu hóa đơn theo `TransactionUuid`.
* Dùng khi phát hành timeout hoặc chưa rõ kết quả.
* Đồng bộ lại trạng thái phát hành.

Trạng thái: PASS

### Phase 10 - Gửi email hóa đơn

* Gửi hóa đơn cho khách qua API Viettel.
* Lưu trạng thái gửi email.

Trạng thái: PASS

### Phase 13 - Màn quản trị log tích hợp

* Xem log theo hóa đơn.
* Xem request/response.
* Xem lỗi tích hợp.

Trạng thái: PASS

### Phase 14 - Dọn log định kỳ

* Có quy trình dọn log cũ.
* Giữ log quan trọng cho đối soát.

Trạng thái: PASS

### Phase 15 - Chống bấm nhầm / phát hành trùng

* Chặn phát hành hóa đơn đã có số.
* Chặn phát hành lại khi cần tra cứu UUID.
* Cảnh báo trạng thái nguy hiểm.
* UI an toàn hơn.

Trạng thái: PASS

### Phase 16 - Xử lý sai sót hóa đơn

* 16.1 Dữ liệu nền thay thế / điều chỉnh: PASS
* 16.2 Tạo hóa đơn thay thế / điều chỉnh: PASS
* 16.3 Điều chỉnh tiền tăng/giảm: PASS
* 16.4 Phát hành hóa đơn thay thế / điều chỉnh: PASS
* 16.5 Điều chỉnh thông tin không copy dòng sản phẩm gốc: PASS
* 16.6 Tự động cập nhật trạng thái hồ sơ xử lý sai sót: PASS
* 16.7 Hiển thị lịch sử xử lý sai sót: PASS
* 16.8 Chặn tạo sai quy tắc nghiệp vụ: PASS
* 16.9 Cập nhật tài liệu module và changelog: PASS

Trạng thái tổng: PASS

### Phase 17 - Đồng bộ danh sách hóa đơn từ Viettel

* Đồng bộ danh sách hóa đơn phát hành từ Viettel.
* Hỗ trợ đối soát trạng thái.

Trạng thái: PASS

### Phase 18 - Dashboard hóa đơn điện tử

* Tổng quan hóa đơn phát hành.
* Theo dõi lỗi.
* Theo dõi hóa đơn chưa tải PDF/XML.

Trạng thái: PASS

### Phase 20 - Bảo mật production

* Không hiển thị mật khẩu API.
* Không log thông tin nhạy cảm.
* Chuẩn bị mã hóa mật khẩu bằng DataProtection.
* Phân quyền ADMIN cho cấu hình.

Trạng thái: PASS

## Việc nên làm sau

### Phase 19 - Đối soát nâng cao

* Đối soát hóa đơn GaoApp với hóa đơn Viettel.
* Cảnh báo hóa đơn local thiếu số Viettel.
* Cảnh báo hóa đơn Viettel không có trong GaoApp.
* Cảnh báo lệch mẫu số/ký hiệu/ngày/tổng tiền.

Trạng thái: TODO

### Phase 21 - Báo cáo kế toán hóa đơn

* Báo cáo hóa đơn bán ra theo ngày/tháng.
* Báo cáo hóa đơn điều chỉnh/thay thế.
* Xuất Excel.
* Lọc theo khách hàng/MST/trạng thái.

Trạng thái: TODO



