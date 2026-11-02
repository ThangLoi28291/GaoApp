# UI-15 — Khách hàng, nhà cung cấp, công nợ, cọc và ưu đãi trên điện thoại

Ngày 26/10/2026. Đã triển khai và hoàn tất kiểm tra tự động UI-15a/b/c/d/e theo hợp đồng chức năng hiện có. Còn nghiệm thu thiết bị thật; voucher chưa có trường ngày hết hạn trong entity/DTO danh sách/xem nhanh. Bước tiếp theo: UI-16a — hóa đơn.

## Giao diện đã triển khai

- **UI-15a:** danh sách, biểu mẫu và hồ sơ khách hàng; tab Tổng quan/Mua hàng/Điểm/Voucher dùng dữ liệu và quyền gốc. Tài khoản không được xem đơn không có tab hoặc liên kết đơn trong hồ sơ.
- **UI-15b:** danh sách/biểu mẫu nhà cung cấp; thông tin liên hệ, ngân hàng, mã khóa, validation và RowVersion giữ nguyên. Create GET và POST lỗi dùng Edit; file Create cũ được kiểm tra và giữ nguyên.
- **UI-15c:** công nợ, sổ phát sinh, phiếu thu, chi hoàn tiền và báo cáo thu nợ; bảng vật lý thành thẻ có nhãn cột. Số tiền/chứng từ/liên kết đơn vẫn từ máy chủ, lỗi không xóa bản nháp.
- **UI-15d:** danh sách phiếu cọc và các tab Nhận cọc/Phiếu còn dư/Lịch sử/Hoàn cọc theo quyền; giữ owner QR, xác nhận nhận/chi tiền, liên kết đơn và chống ghi lặp.
- **UI-15e:** cấu hình thưởng, danh sách/xem nhanh/lịch sử voucher; giữ điều kiện, tỷ lệ, giá trị, ngày phát hành/sử dụng, trạng thái và các xác nhận gốc.

Mười view nạp ba cặp customer-directory.mobile, customer-finance.mobile và customer-rewards.mobile. Chín view chỉ thêm import; riêng RewardSettings/Index sửa thêm đúng ba data-attribute tỷ lệ sang InvariantCulture. CSS chỉ áp dụng cho screen dưới 768px; adapter khôi phục thuộc tính/vị trí node khi về desktop hoặc trước in. Trường nhập 16px, vùng chạm từ 44px, màu navy/lam, safe-area và reduced motion theo mẫu đã duyệt. Không đổi controller/service/repository/DTO, lệnh ghi, JavaScript/CSS nghiệp vụ hoặc các partial gốc. Rà ảnh phát hiện thuộc tính data-* vi-VN dùng dấu phẩy khiến JavaScript Number đọc NaN và hiển thị tỷ lệ cũ bằng 0 trong xác nhận. Ba biểu thức serialize bất biến sửa đúng số nguồn 1.000 đ/điểm, 30 điểm/voucher và 30.000 đ/voucher; trường nhập, bố cục desktop và cấu hình lưu không đổi.

## Quyền, dữ liệu và giới hạn nguồn

Menu tiếp tục lấy từ AdminMenuItems đúng cửa hàng, quyền, trạng thái và thứ tự. Không thêm menu/ghim, seeder/migration/schema, quyền mặc định, endpoint hoặc cấu hình cửa hàng. Adapter không fetch/storage, clone form, ghi giá trị trường, relay submit/click hay tính lại số tiền/điểm.

Supplier/Create GET và hai nhánh POST lỗi trả View("Edit", vm); màn tạo mới đang hoạt động dùng Supplier/Edit.cshtml. Supplier/Create.cshtml cũ được giữ nguyên. **Voucher có trạng thái Expired, nhưng entity và DTO danh sách/xem nhanh không có ngày hết hạn.** DTO in riêng có ExpiredAtUtc nullable và service hiện trả null; không dựng ngày hết hạn hoặc quy tắc từ ngày phát hành.

Trước in, tài chính trả các bộ lọc/node về vị trí gốc và bỏ điều hướng điện thoại; thưởng bỏ điều hướng nội trang. Cờ printing ngăn dựng lại khi media đổi, sau in mới phục hồi. Các view/chủ in Receipt/Print UI-22 không chỉnh sửa. Điểm R05 BarcodeManager độc lập của UI-14 vẫn giữ nguyên trong lịch sử, không được công bố đã sửa.

## Kiểm tra tự động

<!-- UI15_RUNTIME_RESULTS_START -->
- **124/124 test .NET duy nhất qua**, 0 lỗi/0 bỏ qua; 144 lượt thực thi. Gộp bằng testId và tên đầy đủ ổn định; các lượt chạy lại không cộng trùng. 1 lượt thất bại ban đầu được lưu cùng TRX/log và đã có lượt cuối cùng qua cho đúng case.
- **29 trạng thái, 203 lượt điện thoại, 58 đối chiếu desktop, 29 đối chiếu in và 27 lượt hộp thoại.** Phone 320×568,360×780,390×844,430×932,600×900,767×900,390×440; desktop 768/1440px. CSS/hình học desktop và in khớp khi tắt stylesheet mới; không tràn ngang, ID trùng, field drift hoặc ghi nghiệp vụ do resize/in.
- Native browser kiểm tra tìm/lọc khách và NCC, validation, hai ghi chú được lưu đúng; tab hồ sơ, phân trang 23 phát sinh điểm, dialog đơn/voucher và quyền riêng tư đơn hàng. Ba data-attribute thưởng đọc thành số 1000/30/30000; xác nhận hiển thị đúng tỷ lệ cũ và bản nháp 1001 đ/điểm rồi hủy, không lưu chính sách.
- Công nợ còn 50.000; cọc 250.000 nhận −40.000 hoàn −50.000 dùng =160.000; reward ledger 11.500/23 phát sinh/3 voucher. Sau thao tác xem/bản nháp/hủy, các số này cùng dữ liệu cửa hàng, menu, settings, quyền, tồn và cửa hàng khác giữ snapshot.
- Thu nợ 10.000, nhận cọc 250.000 và hoàn cọc 40.000 trong bước chuẩn bị fixture đi qua API native, replay cùng request trả đúng ID không ghi lặp. **Dòng Apply 50.000 gắn đơn là lịch sử đọc seed trực tiếp**, không tuyên bố đã ghi qua browser/API. Native dùng cọc được đối chiếu bằng kiểm thử nghiệp vụ và CLI cọc gốc. Browser chỉ lưu đúng hai ghi chú khách/NCC; không gửi lệnh tiền, QR, voucher hoặc cấu hình thưởng.
- Ba CLI live gốc công nợ/cọc/hồ sơ qua. Probe reward-settings gốc qua ở 320/390/1440px, dùng HTML Razor thật được test PosRewardEligibility SQL chụp cùng JavaScript/tài nguyên gốc; lượt probe này không phải request settings live. Luồng settings live đã có trong acceptance mới.
- Release build 0 cảnh báo/0 lỗi; sáu asset gzip khớp byte nguồn. Snapshot 3.550 đường dẫn, 3.537 file bảo vệ, 19 nguồn nghiệm thu; 9 view imports-only và một view thêm đúng ba sửa serialize số. Baseline 560 file giữ nguyên: 440 không đổi/120 đã đổi/0 mất.
<!-- UI15_RUNTIME_RESULTS_END -->

## Rà ảnh và môi trường

<!-- UI15_VISUAL_RESULTS_START -->
Root đã xem 13 ảnh cuối. Đã xem 13 ảnh cuối của khách hàng/nhà cung cấp, hồ sơ bị giới hạn quyền, đơn nguồn, công nợ/báo cáo thu, sổ cọc, cấu hình và voucher. Thẻ, bộ lọc, biểu mẫu và hộp thoại đọc được; nút báo cáo đã thu gọn, footer và nút đóng nằm trong khung. Hộp xác nhận thưởng hiện đúng chính sách cũ 1.000 đ/điểm, 30 điểm/voucher, voucher 30.000 đ và thay đổi mới 1.001 đ/điểm. Giá trị đơn chỉ hiện với quyền tương ứng; số dư 50.000/160.000 và trạng thái voucher giữ nguồn. Ảnh full-page có thể ghi thanh Menu cố định ở vị trí giữa ảnh dài; bố cục viewport và khoảng cuộn được kiểm tra riêng. Không thay thế kiểm tra Android/iOS, bàn phím, máy quét hoặc máy in thật.
<!-- UI15_VISUAL_RESULTS_END -->

Kiểm thử dùng Web/LocalDB GUID và Chrome headless/profile mới thuộc fixture; tắt launcher trình duyệt mặc định. Acceptance mới chặn nguồn ngoài và thay thao tác in/mở cửa sổ. Các cửa sổ/tab Chrome đang mở và database/cấu hình thật được giữ nguyên.

Android/iOS thật, bàn phím, safe-area, zoom, xoay màn hình, một tay, camera/máy quét và in phần cứng vẫn chờ nghiệm thu. Mô phỏng viewport/print media không thay nghiệm thu thiết bị. Các điểm chờ lịch sử tiếp tục được giữ.

## Bằng chứng

Snapshot/log tại .artifacts/ui-15; TRX/ảnh/SQL tại TestResults/mobile-ui-15-20261026. [Source](mobile-ui-20261025/evidence/ui-15/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-15/baseline-delta.json), [asset nén](mobile-ui-20261025/evidence/ui-15/asset-bundle.json), [runtime](mobile-ui-20261025/evidence/ui-15/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-15/acceptance-matrix.json), [rà ảnh](mobile-ui-20261025/evidence/ui-15/visual-inspection.json). Tiếp **UI-16a — danh sách và chi tiết hóa đơn**.
