# UI-04a · Danh sách đơn POS trên điện thoại

Ngày 25/10/2026. **Đã triển khai và chạy kiểm tra theo yêu cầu bổ sung của người dùng:** build Release 0 lỗi/cảnh báo, 10 test SQL/quyền/CSP và một test lọc sản phẩm qua; probe Chrome qua 10 kích thước. Đã xem ảnh, chỉnh mật độ phone và chạy lại. Xem [báo cáo chạy test](MOBILE-UI-04A-RUNTIME-REVIEW-20261025.md); điện thoại thật và một số ca nghiệm thu còn riêng. Không đổi database hoặc Chrome người dùng đang dùng.

## Bố cục đã áp dụng

Route hiện có: **`/admin/pos/orders-page`**. Dưới 768 px, màn danh sách dùng navy/lam v2, thẻ T01, vùng chạm tối thiểu 44 px, ô nhập 16 px/cao 50 px và nút chính tối thiểu 48 px. Giữ nguyên CSS và cách render desktop/tablet từ 768 px.

| Vùng | Hiển thị điện thoại và nguồn |
|---|---|
| Đầu trang | Dùng tiêu đề app bar, H1 nội dung ẩn trực quan; các link header cố định từ trước chỉ ẩn trên phone. Không thêm link chức năng thay thế. |
| Lối tắt POS | Cùng nav đã render từ `_POSNavTabs`/AdminNavigationProvider, mở trong disclosure “Lối tắt POS của cửa hàng”; thiếu nav thì không dựng vùng này. Không thêm/sao chép mục menu. |
| Tổng quan | Bốn chỉ số cũ trong native disclosure “Thống kê theo bộ lọc”; mở ra là lưới hai cột, giữ nhãn phạm vi toàn bộ lọc so với các đơn trên trang. |
| Tìm và lọc | Cùng form/field/nút; tìm và ngày nhanh luôn có; nút mở phần ngày/trạng thái/sản phẩm/nhân viên/khách/máy/thanh toán, chỉ đơn giao và xóa bộ lọc. Date input ở 320 px xếp một cột khi mở. |
| Thẻ đơn | Cùng nút mã đơn, khách/khách lẻ, mốc thời gian, badge trạng thái, tổng/giá trị dự kiến cạnh khách/thời gian, đã trả và còn thiếu theo renderer hiện có. Cùng nút In/Thao tác ở đầu thẻ. |
| Thông tin bổ sung | Native details chứa điện thoại, mã hóa đơn liên kết, thu ngân/máy/trạng thái giao, phương thức thanh toán/công nợ, cọc/voucher/sau bán; không cần request để mở vùng này. |
| Xem nhanh | Cùng native dialog và nội dung cũ, chỉ đổi phone thành bảng từ đáy với header/footer cố định trong dialog, nội dung cuộn, safe area và giới hạn viewport. |
| Thao tác | Cùng native popover và handler; phone trình bày gần đáy, có giới hạn chiều cao và cuộn. Không thay điều kiện hủy/hoàn hoặc cơ chế thu tiền. |
| Phân trang/trạng thái | Giữ số dòng 10/20/50, trang trước/sau, thông tin phạm vi và skeleton/rỗng/lỗi/thử lại của loader cũ. |

Các chức năng xem/in/ACB/sau bán vẫn dùng quyền, đường dẫn và luồng cũ. Việc rà quyền/điểm mở thao tác chi tiết thuộc UI-04c; UI-04a không mở rộng quyền hoặc thêm lối thao tác nghiệp vụ.

## Giữ một bộ dữ liệu và các phần tử đang có

View chỉ thêm marker `data-mobile-orders` và hai asset mới. Không đổi ID, field, action, nội dung, thứ tự import cũ hoặc form. **`pos.orders.page.js`, `pos.orders.filters.js`, CSS desktop, receipt templates và ACB launcher không sửa.**

Adapter `pos.orders.mobile.js` chỉ hoạt động ở media query dưới 768 px. Nó lấy sáu ô của từng hàng đã render, giữ reference các child node và sắp cùng nút/text/badge vào article trong ô đầu. Năm ô còn lại chỉ ẩn trong chế độ phone. Không clone nút, form hoặc menu; không tự dựng dữ liệu đơn từ response thứ hai.

Observer chỉ theo dõi việc thay child của `tbody`, không theo dõi subtree. Vì vậy các thay đổi trình bày bên trong ô không tự kích hoạt vòng quan sát. Khi loader thay trang/dữ liệu, adapter sắp các hàng mới và bỏ reference hàng đã tháo. Desktop không bật observer. Khi trở về từ 768 px, adapter trả **cùng node về đúng ô/thứ tự và colspan cũ**, tháo wrapper, đưa nav POS về đúng vị trí ban đầu và trả focus khi cần. Nếu native dialog đang mở thì không giành focus.

Disclosure là native details/summary. Summary của hàng có role button để guard double-click cũ không mở xem nhanh khi thao tác với disclosure. `pagehide` ngắt observer, `pageshow` đồng bộ theo breakpoint hiện tại, kể cả bfcache. Không lưu trạng thái đơn/bộ lọc vào storage.

Adapter không gọi mạng, không đọc giá trị form, không tính tiền, không thay history/URL, không tự submit/retry hoặc điều khiển mở dialog/popover. Nguồn trạng thái và dữ liệu tiếp tục thuộc loader cũ; các script chung giữ quyền sở hữu menu, ghim, viewport, tài khoản và quyền.

## Hợp đồng dữ liệu giữ nguyên

- `metricCount` đọc `totalItems` của toàn bộ lọc. `metricTotal`, `metricPaid`, `metricDue` vẫn do hàm `updateMetrics` hiện có tổng hợp các đơn **trên trang đang hiển thị**; phần thanh toán của đơn giao nguồn vẫn được loại như trước. Không đổi tên thành doanh thu.
- Tổng, đã trả, còn thiếu, khoản cọc, giảm voucher, hoàn tiền và số phiếu trả không được adapter diễn giải hoặc tính lại. Giữ số/đơn vị/escaping do renderer cũ đã tạo.
- Đơn giao nguồn giữ caption giá trị dự kiến khi lập đơn/chưa thu tiền; hóa đơn giao đã chốt giữ mốc hoàn tất và dữ liệu hàng thực giao. Các badge lấy nguyên DOM từ `displayStatus`/mapping cũ. Không biến đơn giao nguồn thành giao dịch thanh toán.
- Giữ cách định dạng UTC+7, khoảng ngày và nhánh ngày tạo/ngày hoàn tất trong renderer. CustomerName rỗng vẫn là Khách lẻ.
- Query, applied filters, lookup selected IDs, phân trang, phục hồi bookmark và `history.replaceState/popstate` do script cũ xử lý. Không thêm `storeId`, endpoint hoặc phạm vi quầy mới.
- Timeout/AbortController/sequence, giữ trang cũ khi đang tải thay thế, lỗi 401/403/rỗng và thử lại vẫn thuộc owner cũ. Không có polling hoặc lần tải do đổi breakpoint.

Controller trang và API `POSController` vẫn yêu cầu `Pos.Order.View`; in vẫn kiểm tra `Pos.Order.Reprint`, sau bán/thu tiền tiếp tục được máy chủ kiểm tra. Controller/service/repository, DTO/validator và tenant/terminal không thay đổi. Không tạo menu, ghim, migration hoặc ghi dữ liệu.

## Đối chiếu source tại lượt triển khai đầu

[static-review.json](mobile-ui-20261025/evidence/ui-04a/static-review.json) đối chiếu snapshot `.artifacts/ui-04a/before/`:

- **30 file bảo vệ giữ hash**, gồm renderer/filter/CSS cũ, receipt/ACB, chi tiết đơn, controller, repository/DTO/validator, menu/layout/viewport, tenant/terminal, CSP và Chrome launcher/profile/Program.
- **Hai đối chiếu file cũ**: view chỉ thêm hai import/marker và giữ toàn bộ ID; browser fixture chỉ bổ sung các ca responsive/disclosure/khôi phục node. Chuẩn hóa BOM/CRLF/newline cuối file khi đối chiếu.
- Tất cả CSS mới nằm trong media phone, có nhánh giảm chuyển động/forced colors. Tại lượt triển khai đầu chỉ kiểm tra cú pháp; sau yêu cầu bổ sung đã chạy browser theo báo cáo runtime bên dưới.
- [Baseline delta](mobile-ui-20261025/evidence/ui-04a/baseline-delta.json): 560 file gốc, **525 không đổi, 35 đổi, 0 mất**, cộng dồn các bước; hai asset mới ghi riêng, baseline không sửa.

`pos-orders-page.browser.cjs` đã chạy qua 10 kích thước gồm 320/360/390/430/767/768 px, disclosure bàn phím, một form/một nút gốc, identity/thứ tự child node sau vòng phone → desktop, không refetch do resize và tiền đơn đầu nằm trên dock. Các ca nghiệp vụ/escaping/race/preview/print/ACB cũ tiếp tục qua. [runtime-review.json](mobile-ui-20261025/evidence/ui-04a/runtime-review.json) ghi hash hiện tại, 29 file bảo vệ còn nguyên và các kết quả build/test; static-review cũ giữ làm lịch sử trước kiểm thử. Chưa có kiểm tra điện thoại vật lý hoặc pixel diff với bản phát hành trước.

## Các điểm có thể đối chiếu thêm trong Visual Studio

1. F5 profile `http` hoặc `https`, trên host cửa hàng mở **`/admin/pos/orders-page`**. Thử 320/390/430 px: tìm/lọc, khoảng ngày nhanh, phân trang và số dòng; mở “Thông tin bổ sung” để đối chiếu đủ dữ liệu của đơn.
2. Kiểm tra một đơn khách lẻ, đơn công nợ/thanh toán một phần/cọc/voucher, đơn giao nguồn và hóa đơn giao đã chốt. So số tiền/trạng thái với bản desktop; số đơn toàn bộ lọc và tiền trên trang phải giữ đúng phạm vi cũ.
3. Mở xem nhanh bằng mã đơn, đóng/Back/Escape theo hành vi native đang có, thử menu thao tác và lỗi tải trong môi trường kiểm tra. Không phát sinh thao tác thu tiền/hủy/hoàn mới ở bước này.
4. Đổi qua 767 → 768 → 1366 px và ngược lại, kể cả khi preview đang mở: desktop phải trở lại bố cục cũ, không mất/trùng nút hoặc form; đổi kích thước không tải lại danh sách. Thử phóng chữ, bàn phím, reduced motion và quay lại bằng bfcache.
5. Dùng tài khoản kiểm tra ít quyền/menu trống và cửa hàng khác để đối chiếu menu/ghim/quyền; không có shortcut mẫu tự sinh. F5/Stop tiếp tục giữ Chrome đang mở như UI-03e.

[20 ca nghiệm thu](mobile-ui-20261025/evidence/ui-04a/acceptance-matrix.json) đã cập nhật bằng chứng tự động và tách rõ các phần còn lại; không đánh dấu các bước trước đã nghiệm thu. Không cần migration database cửa hàng; fixture test tự tạo/dọn database tạm. Bước tiếp theo: **UI-04b — chi tiết đơn POS (`POSOrderDetail/Index`)**.
