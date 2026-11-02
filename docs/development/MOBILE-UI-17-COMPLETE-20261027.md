# UI-17 — Ngân hàng và đối soát trên điện thoại

Ngày 27/10/2026. Đã triển khai UI-17a/b/c/d/e và qua tự động trên paired snapshot + nguồn UI-17. Chưa xác minh toàn bộ workspace/P3 đồng thời; còn thiết bị thật. Tiếp UI-18a — bộ lọc kỳ, so sánh và phạm vi dùng chung.

## Giao diện đã triển khai

- **UI-17a:** tra cứu ACB theo đơn ở trang riêng và embedded. Đơn, số phải thu, số đã ghi nhận, giao dịch và nguồn xác nhận dùng dữ liệu owner gốc. Lịch sử QR và bằng chứng vẫn mở bằng details native; adapter không đổi trạng thái mở. Nút quay về đơn và dialog embedded giữ đường dẫn gốc.
- **UI-17b:** đối soát ACB theo kỳ/nguồn. Bộ lọc ngày và callback chia nhóm, chỉ số và bảng có nhãn trên điện thoại. Phân biệt callback đã nhận với kết quả tra cứu ngân hàng; trạng thái khớp, chờ, lệch và chưa ghi tiền giữ nghĩa nguồn. Dòng có rowspan tiếp tục được gán đúng cột, không sao chép QR hoặc số tiền.
- **UI-17c:** thông báo ngân hàng trực tiếp. Bảng chuyển thành thẻ bằng CSS; giữ nguyên node/key/thuộc tính dòng live để owner gốc so sánh `isEqualNode` và giữ focus, lựa chọn, trạng thái đang xem. Modal xác nhận dùng các node gốc, có thân cuộn và footer trong khung; quay về desktop/in trả lại đúng thứ tự child nodes.
- **UI-17d:** tài khoản cửa hàng, cấu hình ACB, định tuyến callback và kết quả kiểm thử native. Biểu mẫu có trường 16px, vùng chạm từ 44px, nhóm cấu hình và điều hướng nội trang trên điện thoại. Mật khẩu/key, masked value, mặc định và phạm vi cửa hàng do owner gốc quyết định.
- **UI-17e:** trạng thái rỗng, tải, lỗi/chậm, trùng nguồn và thiếu quyền tiếp tục do native handler hiển thị. Adapter không gọi lại ngân hàng, retry, xác nhận tiền hoặc chuyển tiếp nút; quay về đơn giữ link/ngữ cảnh hiện có.

Chín view cha nạp ba cặp `bank-workspace.mobile`, `bank-notifications.mobile`, `bank-settings.mobile`. Chỉ thêm import, không sửa thân Razor gốc. Bốn partial trong UI-17 và partial `_AcbCallbackHealth` giữ từng byte/BOM. CSS mới chỉ áp dụng `screen` dưới 768px; adapter khôi phục thuộc tính/DOM trình bày trước in và ở desktop, có printing guard. Embedded trong iframe của host desktop còn kiểm viewport host để không áp mẫu điện thoại chỉ vì iframe hẹp.

Các màn đối soát HKD và đối soát ca đã thuộc UI-13/UI-07; UI-17 không thay lại các owner này.

## Quyền và cấu hình

Menu tiếp tục lấy từ `AdminMenuItems` theo quyền, trạng thái và thứ tự cửa hàng. UI-17 không thêm menu/ghim, seeder, migration, schema, endpoint hoặc quyền mặc định. Controller/service/repository/DTO ngân hàng, CSS/JS nghiệp vụ gốc và cấu hình ngân hàng được bảo vệ theo snapshot. Có thay đổi đồng thời ngoài phần việc UI-17 trong nhánh InvoiceNotifications; hash/diff của chúng chỉ là quan sát tại thời điểm ghi, không phải cam kết ổn định hay kết quả UI-17. Không ghi đè phần việc này; paired build dùng bảy bản trước UI-17 bất biến (bao gồm Program.cs trước khi đăng ký feature đồng thời) và loại toàn bộ compile input mới không thuộc UI-17.

Tra cứu và đối soát dùng `Pos.Payment.View`. Hủy QR cần thêm `Pos.Payment.Create`, chốt cần `Pos.Order.Finalize`; retry callback cần `System.Integration.Manage`. Thông báo ngân hàng và tài khoản dùng `System.BankAccount.View`; quản trị thiết bị/tài khoản thêm `System.BankAccount.Manage`. Xác nhận tiền còn yêu cầu `Pos.Payment.Create` và `Pos.Order.Finalize`. Cài đặt/định tuyến dùng `System.Integration.Manage`; định tuyến vẫn qua `CanManageAsync` gốc.

GET lịch sử theo đơn dùng `LookupAsync(refresh:false)`, chỉ đọc SQL và khóa đơn theo owner. Tra cứu mới, bank-lookup, hủy/status/complete, test connection/sandbox/production, retry, cấp/thu hồi/gia hạn thiết bị và lưu cấu hình vẫn là các lệnh gốc, không được gửi chỉ vì kiểm tra layout.

## Phạm vi bằng chứng

<!-- UI17_SOURCE_RESULTS_START -->
Source review qua: snapshot 3.582 đường dẫn; scope 3.570 gồm 3.563 file current trùng byte và 7 snapshot compile copies bất biến. Mười chín nguồn UI-17, 9 cha imports-only/BOM, 4 partial UI-17 + shared callback nguyên vẹn. Ba CSS chỉ screen dưới 768px; 3 adapter +2 cjs test qua syntax. Baseline 560:416 không đổi /144 đã đổi /0 mất; 9 view mới khác baseline từ lượt UI-17. Giữ nguyên 41 verification và view/asset/report/evidence đã giới thiệu trước đó.

Paired isolated build 0 cảnh báo/0 lỗi; sáu gzip khớp byte nguồn. UI-17 chỉ xác minh paired snapshot + 19 nguồn UI-17. Workspace chung và InvoiceNotifications P3 đang thay đổi không được nhận là đã build/test qua. Bảy file gốc được lấy từ snapshot bất biến; Category.CS bổ sung có byte trùng Git HEAD sạch trước tác vụ; SDK/package/generated inputs giữ nguyên và actual Compile TSV chứng minh đủ 7 shadow + supplement + Test SDK Main, không lọt source mới ngoài UI-17. Không sửa hoặc suppress EF migration warning.

Manifest/diff các file hiện tại ngoài UI-17 chỉ là quan sát tại thời điểm ghi, không khóa nguồn đang thay đổi và không được tính vào phần việc UI-17. Mọi protected file cũ ngoài 7 đã ghi nhận vẫn phải trùng snapshot. Bằng chứng actual compile/content/SDK được lưu riêng trong isolated-build-proof.json.
<!-- UI17_SOURCE_RESULTS_END -->

## Kiểm thử và đánh giá

<!-- UI17_RUNTIME_RESULTS_START -->
- **461/461 test .NET duy nhất qua**, 0 lỗi/0 bỏ qua ở kết quả cuối; 464 lượt thực thi. Lưu 2 lượt lỗi trước, đúng testcase có lượt cuối qua; không cộng trùng rerun.
- **50/50 Node unit**, ba probe gốc qua: live7ca/10snapshots, admin review, POS recovery10ca. Các probe tài chính gốc dùng synthetic API; một approval giả được gửi có chủ ý để kiểm luồng xác nhận.
- **24 trạng thái /168 lượt điện thoại /48 desktop /24 in /12 lượt hộp thoại (4 loại)**. Không tràn trang, ID trùng hoặc thay field/form/value/disabled vì trình bày; desktop 768/1440 và in khớp computed CSS/hình học khi tắt CSS mới.
- Live owner giữ các dòng equal-to-incoming bằng node identity; dòng đổi antiforgery có thể thay theo owner gốc. Tên cấu hình đang soạn, evidence mở, lựa chọn QR thứ hai và focus không bị snapshot đọc làm mất; modal giữ target và được hủy, không xác nhận.
- SQL fingerprint nghiêm giữ tiền đơn, QR, giao dịch/bằng chứng, settings/secrets, định tuyến, menu, role/grants/user-store/host-rights và raw payload. Khoản thu 10.000, tồn vật lý100, callback attempts0; biên nhận chưa áp dụng vẫn chưa áp dụng.
- Trong 24 trạng thái có 17 native GET/read states, 4 body Razor native đã capture và 3 phản hồi GET tổng hợp/ràng chờ có provenance. SandboxResult không được mở qua bank test POST; routing thật của host fixture rỗng. Callback key của fixture không giải mã được trong ephemeral key ring: chỉ kiểm tra trình bày Unreadable, không nhận là kết nối hay giải mã thành công. Các legacy probe chỉ dùng synthetic APIs, gồm một xác nhận tiền giả có chủ ý, không viết cửa hàng/ngân hàng thật.
- Các lỗi trước UI assertions do concurrent PendingModelChangesWarning và cart fixture được lưu trong TRX/log riêng. Paired build không suppress EF; fixture cart được chuẩn bị bằng native hold trước fingerprint, không sửa guard sản phẩm. Các lần build lỗi P3 CS0535, Category.CS và SDK entrypoint giữ artifact; kết quả build qua chỉ thuộc paired boundary.
- SQL/Web GUID và Chrome headless/profile mới thuộc fixture; giữ cửa sổ Chrome hiện có. Không gọi ngân hàng thật hoặc gửi approval/refresh/cancel/complete/retry/save/provisioning từ native acceptance browser.
<!-- UI17_RUNTIME_RESULTS_END -->

<!-- UI17_VISUAL_RESULTS_START -->
Root đã xem 18 ảnh cuối. Đã xem 18 ảnh cuối bao đủ chín view cha, bản nhúng, thẻ giao dịch/rowspan, bốn hộp thoại, bộ lọc, biểu mẫu và lỗi đọc. Màu navy/lam, số tiền và trạng thái đọc rõ; ngày giao dịch dùng cả hàng nên chuỗi dài không bị chia cột hẹp, KPI tài khoản tách nhãn/số 26px với màu tương phản. Nút đóng/footer ở trong khung; bản nháp, lựa chọn QR thứ hai và nguồn xác nhận giữ nguyên. Ảnh cấu hình callback populated, SandboxResult và bảng ngân hàng là body Razor biên dịch với model tổng hợp trong khung GET gốc; không phải kết quả gọi ACB. Lỗi đọc dùng GET kiểm soát, không ghi tiền. Thanh Menu chỉ dùng nguồn cấu hình fixture; ảnh full-page có thể ghi thanh cố định giữa ảnh dài, viewport và cuộn được kiểm tra riêng. Kiểm thử này dùng paired snapshot + UI-17; workspace có P3 đồng thời chưa được xác minh. Nghiệm thu Android/iOS, bàn phím, thông báo từ điện thoại/ACB thật và thiết bị thực tế còn chờ.

Còn nghiệm thu Android/iOS thật, bàn phím/safe-area/zoom/xoay/một tay, camera/máy quét, thiết bị ngân hàng và in phần cứng. UI-17 chỉ xác minh paired snapshot + 19 nguồn UI-17. Workspace chung và InvoiceNotifications P3 đang thay đổi không được nhận là đã build/test qua. Bảy file gốc được lấy từ snapshot bất biến; Category.CS bổ sung có byte trùng Git HEAD sạch trước tác vụ; SDK/package/generated inputs giữ nguyên và actual Compile TSV chứng minh đủ 7 shadow + supplement + Test SDK Main, không lọt source mới ngoài UI-17. Không sửa hoặc suppress EF migration warning.
<!-- UI17_VISUAL_RESULTS_END -->

## Bằng chứng

Snapshot/script tại `.artifacts/ui-17`. Sau lần chạy cuối: [nguồn](mobile-ui-20261025/evidence/ui-17/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-17/baseline-delta.json), [asset nén](mobile-ui-20261025/evidence/ui-17/asset-bundle.json). [runtime](mobile-ui-20261025/evidence/ui-17/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-17/acceptance-matrix.json), [paired build](mobile-ui-20261025/evidence/ui-17/isolated-build-proof.json), [ảnh](mobile-ui-20261025/evidence/ui-17/visual-inspection.json). TRX/ảnh/SQL tại TestResults/mobile-ui-17-20261027. Tiếp **UI-18a — bộ lọc kỳ, so sánh và phạm vi dùng chung**.
