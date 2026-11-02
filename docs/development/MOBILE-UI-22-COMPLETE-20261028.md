# Hoàn thành UI-22 — In ấn và màn hình chuyên dụng

Ngày 28/10/2026. Toàn UI-22a–f đã triển khai và qua kiểm thử tự động. Bước tiếp **UI-23a — trang công khai và dự phòng**.

- UI-22a: kiểm kê 16 view/partial và route thực tế. POS/POSReceipt dùng ReceiptTemplates/Print; POS/PrintReceipt và POSReceipt/Index giữ nguyên byte, không tạo thêm route hay menu cho bản cũ.
- UI-22b: nút thao tác dễ chạm, nhóm nội dung và vùng cuộn riêng cho hóa đơn, cọc, nợ, bill giao, đơn mua, phiếu cuối/đầu ca và tem. Giữ nội dung giấy, QR/barcode, kích thước, phông và dấu phân trang gốc.
- UI-22c: màn mẫu in và mẫu tem theo bố cục một cột, màu navy/blue trên điện thoại; vùng thông tin tiệm, thư viện, thiết kế và máy in có nhóm riêng. Preview mặc định 100%; người dùng chọn Vừa khung khi cần. Mức zoom chỉ đổi phần xem, không sửa dữ liệu thiết kế hoặc khổ in.
- UI-22d: vòng đời mở/in/đóng, QZ, helper, ACK, lỗi/retry và claim đồng thời qua probe gốc. LabelPrintClient/Server và renderer/transport giữ nguyên source.
- UI-22e: màn khách/kiosk giữ khung chuyên dụng, không thêm menu Admin. Native màn khách qua giỏ, QR local, mất/kết nối lại, xác nhận một phần và hóa đơn khớp DTO đã chốt. Kiosk qua kích hoạt, menu, tra cứu tên, quét mã Enter, giỏ, nháp số lượng, xác nhận checkout và mất mạng. Giữ giá tiền trên một dòng, cho nút số lượng/xóa tự xuống hàng. Bổ sung nút Đóng chỉ trên điện thoại khi hộp gốc không có thao tác hủy; giữ nút gốc và không gửi số lượng nháp khi đóng.
- UI-22f: StoreMonitor 3D/2D và nền tối có điều khiển phone; giữ cảnh và bố cục màn lớn. Không đổi TS, bundle hoặc build của mô hình. Quyền xem monitor vẫn cấp riêng; ADMIN không tự nhận quyền này.

Bao phủ 16 view/partial; 13 view chỉ thêm import/marker, ba file còn lại giữ nguyên. Sáu asset mới giới hạn screen <=767.98px hoặc quản lý DOM phone có dọn khi desktop/print. Controller/service/model/migration, renderer, handler native, fields/token và quy tắc menu/quyền/cấu hình giữ nguyên. Không seed menu hay cập nhật database cửa hàng đang dùng.

170/170 ca .NET duy nhất, 99 ca Node; 42 trạng thái/294 phone/84 desktop/42 print; 24 PDF đối chiếu. Có 14 lượt kiểm tra hộp thoại và 2 kiểm tra cuộn. Hai probe Chrome gốc print-lifecycle và pos-print-helper đều qua; chỉ mở profile headless riêng, giữ các cửa sổ Chrome đang mở. 182 lượt .NET gồm 9 lượt chưa qua được lưu lại; mỗi testcase chỉ tính lần cuối. Các lỗi đầu ở dữ liệu fixture nhiều cửa hàng, quyền riêng monitor, giả định menu cũ và thời điểm iframe được sửa trong harness; nút đóng hộp kiosk là sửa UI thực tế.

12 cặp/24 PDF gồm bảy chứng từ native và năm khổ renderer 45/80 mm, A6/A5/A4. Đối chiếu page box, số trang, văn bản và từng pixel Poppler 110 DPI trước/sau đều khớp; đã render 48 trang PNG và xem trực quan 27 ảnh. Ảnh QR bill giao và barcode ca được giữ; giấy dài và đơn mua 28 dòng kiểm tra đủ nội dung. Đơn mua gốc chỉ in tên/ĐVT/số lượng, không thêm giá ngoài mẫu hiện có.

Bộ .NET gồm mẫu hóa đơn, ưu tiên in theo khách, bảo mật dữ liệu thanh toán trên màn khách, kiosk, monitor 2D/3D, renderer/LabelPrintClient, mẫu/tem/mã vạch/hàng đợi, bill giao và migration. Hai ca SQL tạo mới/nâng cấp từ trước P3 và lặp migration qua; không pending model/migration. UI-22 không sửa migration. Fixture dùng MigrateAsync thật; 2074 C# production được ghi nhận qua input MSBuild, không shadow/rebind/EnsureCreated. Sáu asset HTTP và gzip SDK giải nén khớp source; không khẳng định HTTP đã phục vụ gzip. Build asset cuối không lỗi/cảnh báo; lượt biên dịch C# trước còn xUnit2031 có sẵn ở InvoiceNotificationTransportTests.

Kiểm tra SQL xác nhận menu/pins, store, role/grant, thanh toán, tồn kho, cọc/nợ và đơn mua hiện có không đổi qua ma trận hiển thị. Kiosk tạo giỏ nháp trong fixture; in phiếu ca được phép cập nhật metadata mark-printed gốc. Luồng màn khách dùng fixture riêng, chủ ý ghi hai khoản thanh toán và chốt một đơn; QR là local EMV, không có credentials/cuộc gọi ngân hàng. Giữ 46 verification lịch sử và trạng thái chờ cũ. Baseline 560 file: 358 không đổi/202 đã đổi/0 mất.

- Chưa nghiệm thu Android/iOS thật, bàn phím, safe area, camera/scanner và hộp thoại in trên thiết bị thật.
- Chưa dùng máy in USB, QZ Tray hoặc Linux helper trên thiết bị vật lý; pos-helper-busy-queue cần server.py do người dùng cung cấp trước đây, hiện không còn trong workspace. Probe helper loopback kiểm tra ACK, retry và 5 cặp claim đồng thời; không thay file Python bằng bản giả.
- QR native của màn khách dùng tài khoản test, tạo local EMV và xác nhận thủ công trong fixture. Kiosk dừng ở giỏ, sửa số lượng, xác nhận checkout và ngắt mạng; không tạo/thu tiền ACB thật.
- PDF là ảnh chụp DOM đã render bằng dữ liệu SQL fixture, loại script để không phát lệnh lặp. Các cặp so sánh bật/tắt CSS điện thoại; không dùng ảnh này làm bằng chứng máy in vật lý.

Đường dẫn để tự cấu hình menu khi cần: /admin/receipt-templates, /admin/label-printing, /admin/label-printing-settings, /admin/pos/customer-display, /admin/store-monitor. Màn /kiosk dùng khóa thiết bị riêng; các đường dẫn in có ID chứng từ nằm trong [kiểm kê route](mobile-ui-20261025/evidence/ui-22/route-inventory.json).

[Nguồn](mobile-ui-20261025/evidence/ui-22/static-review.json), [build](mobile-ui-20261025/evidence/ui-22/current-build-proof.json), [runtime](mobile-ui-20261025/evidence/ui-22/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-22/acceptance-matrix.json), [PDF](../../TestResults/mobile-ui-22-20261028/browser/pdf-review.json), [ảnh](mobile-ui-20261025/evidence/ui-22/visual-inspection.json).
