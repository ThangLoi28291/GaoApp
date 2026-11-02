# A4 — Baseline hiệu năng: BLOCKED

Ngày 02/11/2026. **NOT TESTED** cho toàn bộ runtime UX/SQL/browser. DB test an toàn chưa xác minh; không có browser session hoặc dataset thật được dùng cho benchmark. **0 lượt đo runtime**. Không có p50/p95, FPS, Core Web Vitals, SQL duration hoặc phần trăm cải thiện. A4 không PASS.

## Bảng BEFORE thực tế

| Kịch bản | Dataset/tier | Viewport | n | Thời gian/p50/p95 | Trạng thái |
|---|---|---|---:|---|---|
| Mở `/Admin/Product` | UNKNOWN / chưa có tier thực chạy | Chưa mở | 0 | N/A | BLOCKED A1 |
| Search một SKU/tên giả qua Product Search | UNKNOWN | Chưa mở | 0 | N/A | BLOCKED A1 |
| Lọc rồi chuyển page của Product | UNKNOWN | Chưa mở | 0 | N/A | BLOCKED A1 |
| POS đọc/tìm sản phẩm | UNKNOWN | Chưa mở | 0 | N/A | BLOCKED A1 |
| POS cập nhật giỏ 50–300 dòng | Không có sandbox/fixture đã được duyệt | Chưa mở | 0 | N/A | BLOCKED riêng cả quyền mutation/fixture |
| SQL CPU/IO/query plan, DOM count, scroll/long tasks, browser memory/errors | UNKNOWN | Chưa mở | 0 | N/A | NOT TESTED |

N/A không phải 0 ms. Không lấy số đo 1.076 ms ở báo cáo invoice-picker 29/10 hoặc các run cũ làm baseline mới. Thời gian build/Node runner trong A1 không được xem là tốc độ UI.

## Đo tĩnh đã làm — MEASURED, không phải tốc độ tải trang

Đọc chiều dài file source bằng filesystem và SHA-256; [asset-sizes-source.csv](evidence/asset-sizes-source.csv). Đây là bytes chưa nén của từng file, không phải tổng bundle/request, transfer size, gzip/brotli hoặc cache footprint runtime.

| Asset | Bytes source |
|---|---:|
| `Admin/js/products.index.js` | 29.489 |
| `Admin/js/products.filters.js` | 8.596 |
| `Admin/js/products.mobile.js` | 5.850 |
| `Admin/css/products.index.css` | 3.520 |
| `Admin/css/products.mobile.css` | 22.772 |
| `Admin/css/gaoapp.design-system.css` | 29.891 |
| `Admin/js/pos/pos.barcode.js` | 85.467 |
| `Admin/js/pos/pos.render.js` | 106.261 |

**SOURCE VERIFIED:** Product list dùng server paging tối đa 200 record; hai biểu diễn row/card cùng page. Không có chứng cứ request đang tải 10k+ sản phẩm một lần. Source có lazy image, cancellation và debounce 300 ms. Đây là cơ sở chọn kịch bản đo, không phải kết luận performance đạt/không đạt.

## Protocol sau khi có test environment được duyệt — chưa chạy

Điều kiện cố định: cùng checkout/hash, Debug hoặc Release được ghi rõ, cùng máy/browser/version, một profile test mới, một account/role/Store/terminal, cùng DB fingerprint/count và pageSize. Cùng URL/query/viewport; ghi server uptime/network/cache. Không đóng phiên người dùng, không xóa cache/server SQL hoặc bật cấu hình server-wide để tạo cold giả.

1. **Navigation:** mở Product Index pageSize20, chờ font/asset và kết quả sẵn sàng. Ghi navigation start→DOMContentLoaded/load, TTFB và thời điểm list sẵn sàng riêng. First navigation gọi là first-in-session; không gọi SQL cold nếu chưa chứng minh cache SQL cold. Thêm 5 lượt warm ở nhịp bình thường, một lượt mỗi lần, không concurrent.
2. **Search:** SKU giả kết quả một và tên tiếng Việt không dấu nhiều kết quả; đo từ thao tác nhập cuối đến response và list hoàn tất. Ghi riêng debounce 300 ms để không nhầm SQL duration. Dùng cùng từ khóa/dataset/pageSize; 5 lượt warm cho từng trường hợp, ghi cache/hủy request. Không in payload hoặc tên khách.
3. **Filter/paging:** đổi lifecycle/category hợp lệ rồi page1→2→1, pageSize20 và100/200 chỉ khi an toàn. Ghi interaction→UI ready, query URL, item count/page và total. Không thêm sort mới: source Product đang OrderByDescending(Id); sort của màn khác cần mapping riêng.

Nhịp tối thiểu đề xuất giữa các thao tác là vài giây, chờ UI/server kết thúc; dừng nếu lỗi/saturation hoặc outbound ngoài test. Không stress/soak, không vòng lặp request không nghỉ. Năm mẫu mỗi case là khảo sát nhỏ, chưa đủ mô tả tail latency ổn định. Báo danh sách mẫu và median; nếu tính empirical p95 (nearest rank) phải ghi rõ với n=5 nó chính là max quan sát và không phải p95 đáng tin. Tăng số mẫu chỉ khi thực sự cần và vẫn trong phạm vi đã duyệt.

Thu thêm HTML/response/transfer bytes, số record thực nhận, DOM node count cho cả node ẩn, ảnh decode, long tasks và lỗi browser trong cùng cửa sổ quan sát. Chỉ báo memory/CPU khi công cụ đo hỗ trợ và ghi phương pháp/đơn vị. Scroll dùng timeline/frame sampling thực; không suy 60fps từ cảm giác mượt. Các phép đo SQL chỉ dùng quyền đọc đã cấp trên test DB, không tạo index, bật Query Store/trace hay chỉnh query.

Với POS, trước hết chỉ đo GET `products/search`/hiển thị khi app bootstrap đã được chứng minh an toàn. Giỏ dài yêu cầu fixture/đơn nháp sandbox và phạm vi ghi test được duyệt riêng; không bấm finalize/payment/invoice/print/provider. Không thay test source hoặc mock API rồi gọi đó là benchmark SQL/app thật.

AFTER chỉ có sau A7. So sánh cặp cùng dữ liệu, role, viewport, source baseline và cache; lưu mẫu/log lỗi cùng số đo. Nếu đổi điều kiện, ghi không so sánh trực tiếp. Không đặt trước mục tiêu tăng % hoặc cam kết SQL nhanh lên từ thay CSS.
