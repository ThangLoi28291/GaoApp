# UI-01a–b · Bộ nền GaoApp Commerce

Ngày thực hiện: 25/10/2026. Hoàn thành UI-01a và UI-01b theo [kế hoạch tổng](MOBILE-UI-UPGRADE-PLAN-20261025.md). Đã bổ sung bộ thiết kế dùng chung và trang Razor tương tác để kiểm tra trước khi chuyển các màn nghiệp vụ.

Bổ sung theo yêu cầu tiếp theo: [trang so sánh máy tính/điện thoại và thanh menu dưới cùng](MOBILE-UI-DEVICE-COMPARISON-20261025.md). Ảnh và hash trong báo cáo UI-01 ban đầu dưới đây là snapshot trước phần bổ sung; ảnh và kết quả mới nằm ở báo cáo liên kết này.

**Phương án điện thoại mới nhất:** [Mobile v2](MOBILE-UI-MOBILE-V2-20261025.md), được thiết kế lại sau phản hồi về bản đầu. Palette xanh lá và ảnh trong tài liệu này là lịch sử của v1; chưa dùng làm quyết định cuối cho mobile.

## Hướng thiết kế được chọn

GaoApp Commerce: giao diện tác nghiệp thương mại với nền sáng, xanh lá trầm, chữ rõ và các nhóm thông tin gọn. Tham khảo cách Shopify Polaris tổ chức trang tổng quan, danh sách, chi tiết, biểu mẫu và trạng thái cho người vận hành cửa hàng. Shopify mô tả các mẫu trang và các nhóm thành phần nhỏ dùng lại cho những tác vụ này trong [App Home patterns](https://shopify.dev/docs/apps/build/app-home#patterns).

Đây là thiết kế của GaoApp, được hiện thực bằng Razor, Bootstrap và `gds` đang có. Giữ Public Sans tự lưu trong dự án; tài nguyên giao diện được phục vụ nội bộ. Các trạng thái vừa có màu vừa có nhãn. Mục tiêu vùng chạm 44–48 CSS px tham khảo [W3C Target Size Enhanced](https://www.w3.org/WAI/WCAG22/Understanding/target-size-enhanced.html); đây là lựa chọn thiết kế, chưa phải tuyên bố đạt toàn bộ WCAG.

## Xem và thao tác

- Bản đang chạy trên máy phát triển: `http://127.0.0.1:5206/admin/ui-foundation`.
- Đường dẫn tích hợp vào ứng dụng: `/admin/ui-foundation`, controller `UiFoundation`, action `Index`, area `Admin`.
- Route ứng dụng kế thừa yêu cầu đăng nhập từ `BaseAdminController` và dùng quyền hiện có `security.role.permissions`. Không bổ sung mã quyền hay tạo mục menu. Khi cần, người dùng tự thêm đường dẫn qua Quản lý menu.
- Trang xem thử dùng bốn đơn và số liệu cố định. Tìm theo mã hoặc tên có/không dấu; lọc nhanh hoặc mở bảng lọc; mở chi tiết; thử biểu mẫu trống/hợp lệ; thử trạng thái tải, trống, lỗi và hộp xác nhận.
- Công cụ `tools/GaoApp.UiPreview` render chính các Razor và model này trên loopback, độc lập với startup, database và worker của ứng dụng. Route xem thử của công cụ chỉ cung cấp dữ liệu minh họa; nó không kiểm tra đăng nhập tài khoản thật.

## Token bản sáng v1 – tham chiếu lịch sử

| Nhóm | Giá trị |
|---|---|
| Hành động chính / hover | `#176B45` / `#105334` |
| Nền chính / bề mặt / nền nhẹ | `#F5F7F5` / `#FFFFFF` / `#F1F4F1` |
| Chữ chính / nội dung / phụ | `#192B23` / `#34483E` / `#62736A` |
| Viền phân nhóm / viền control | `#DFE5DF` / `#84958B` |
| Focus bàn phím | `#245FCE`, viền ngoài 3 px |
| Thành công / thông tin / cảnh báo / lỗi | `#176B45` / `#245F9E` / `#805500` / `#B42332` |
| Font | Public Sans; system fallback |
| Chữ | Tiêu đề 24–32 px; phần 18 px; nội dung 15 px; nhãn 14 px; phụ 13 px; input 16 px |
| Khoảng cách | 4, 8, 12, 16, 24, 32, 48 px |
| Bo góc | Control 12 px; card 16 px; panel 20 px; chip dạng viên |
| Vùng chạm | 44 px; 48 px khi dưới 768 px hoặc pointer coarse |
| Lớp nổi | Sticky 10; thông báo nổi 20; native dialog dùng top layer |
| Chuyển động | 140/200 ms; dừng animation trang trí trong reduced motion |

Các tỷ lệ tương phản tính từ mã màu: trắng trên xanh chính 6,51:1; chữ phụ trên nền canvas 4,67:1; viền control trên trắng 3,16:1. Nhãn trạng thái thông tin, cảnh báo và lỗi trên nền tương ứng đều trên 5,8:1. Đây là phép tính palette, cần kiểm tra tiếp khi ghép vào các màn thực tế.

## Phạm vi CSS và cách dùng

Điều kiện áp dụng là root có **cả hai** thuộc tính:

```html
<div data-gao-ui="modern" data-gao-theme="commerce">
    <!-- Nội dung dùng bộ thành phần mới -->
</div>
```

Thứ tự tải: Bootstrap/core và CSS cũ → `gaoapp.design-system.css` → `gaoapp.commerce.css` → CSS riêng của trang nếu có. `ui-foundation.preview.css` và `ui-foundation.preview.js` thuộc riêng trang xem thử. Chỉ tải CSS/JS mới một lần ở layout hoặc section của trang được chuyển. Layout Admin hiện hành chưa nạp bộ mới.

`gaoapp.design-system.css` tiếp tục cung cấp tên `gds`, cấu trúc card/header/status. `gaoapp.commerce.css` đổi token và bổ sung thành phần trong phạm vi opt-in. `gaoapp.ui.css` vẫn sở hữu submit guard và trạng thái dùng chung hiện có; script Commerce chỉ phụ trách native dialog. `site.css` hiện là placeholder; `demo.css` vẫn có các quy tắc template/menu cũ, nên chưa được thay trong bước này.

Các class `.gds-button`, `.gds-input`, `.gds-record`, `.gds-dialog`, `.gds-action-dock`, `.gds-state` chỉ có hiệu lực trong root Commerce. Không dùng đồng thời `.modal`/`.offcanvas` của Bootstrap cho cùng native dialog. Đặt dialog trong root để kế thừa token; đóng dialog trước khi thay hoặc tháo partial chứa nó.

Razor dùng lại:

```cshtml
@using GaoApp.Web.Areas.Admin.ViewModels.Commerce
@await Html.PartialAsync("Commerce/_Header", new CommerceHeader("Đơn hàng", "CỬA HÀNG"))
@await Html.PartialAsync("Commerce/_Status", new CommerceStatus("Đang giao", CommerceTone.Info))
@await Html.PartialAsync("Commerce/_State", new CommerceState(CommerceStateKind.Empty, "Chưa có đơn", "Các đơn sẽ xuất hiện tại đây."))
```

Các mẫu markup hoạt động cho bộ lọc, record, form, dialog và dock nằm trong `UiFoundation/Index.cshtml`. Chúng là thành phần nền để ghép T01–T13; chưa phải 13 màn nghiệp vụ hoàn chỉnh. Controller/service của từng nghiệp vụ tiếp tục quyết định quyền, trạng thái hợp lệ và kết quả lưu.

## Hợp đồng thao tác

- Nút mở: `data-gao-dialog-open="id-dialog"`; nút đóng: `data-gao-dialog-close`; dialog có `aria-labelledby` trỏ đến tiêu đề duy nhất.
- `window.GaoCommerce.openDialog(dialog, opener)` trả về boolean. Chỉ mở trong root Commerce, không xếp chồng khi đã có native/Bootstrap dialog hoặc offcanvas đang mở.
- Listener khai báo một lần, dùng delegation để nhận nút từ partial nạp sau. Tab/Shift+Tab vòng trong dialog; Escape đóng; khi đóng trả focus về điểm mở còn trong DOM và khôi phục scroll lock.
- Form mẫu có nhãn, input mode, required, `aria-invalid`, lỗi tại trường, phần tóm tắt và `aria-busy`. Logic kiểm tra mẫu chỉ phục vụ xem UI; không thay cho kiểm tra server hoặc bộ kiểm tra biểu mẫu nghiệp vụ.
- Danh sách có một DOM cho mỗi record. CSS đổi vị trí các vùng khi hẹp; không nhân đôi input, ID hay handler cho desktop và điện thoại.
- Điện thoại dưới 768 px: form một cột, record dạng card, sheet sát đáy, nút lớn. Từ 768 px: record dạng dòng, form hai cột. Mẫu workspace tách cột phụ từ 1200 px, tối đa nội dung 1280 px. Các mốc giữ tương thích breakpoint Bootstrap.
- Nội dung dialog cuộn độc lập, header/footer giữ trong khung `dvh`; padding xét safe area. Tổng tiền dùng số tabular và cho phép xuống dòng khi cần; tên dài không đẩy nút ra ngoài.

## Kết quả kiểm tra

| Kiểm tra | Kết quả và phạm vi |
|---|---|
| Razor, model và controller mới | Build qua công cụ preview, 0 warning, 0 error |
| Hợp đồng route/quyền | Controller thật biên dịch; kiểm tra GET, route, kế thừa Authorize và policy hiện có đạt |
| JavaScript | `node --check` hai file đạt; console trình duyệt không có error/warn |
| 320 / 390 / 768 / 1440 px | Không tràn ngang; button đang hiển thị và khả dụng đạt ít nhất 44 × 44 px |
| DOM | Không trùng ID; 4 record ở mọi kích thước; asset mới nạp một lần |
| Lọc | Tên Việt không dấu tìm được; lọc trạng thái, không kết quả và đặt lại hoạt động |
| Dialog | Sheet, chi tiết tên dài, xác nhận; Tab/Shift+Tab, Escape và trả focus đạt |
| Biểu mẫu | Lỗi tại trường, focus lỗi đầu, đang kiểm tra, kết quả mẫu, giữ bản nhập khi đóng mở đạt |
| Chiều cao thấp | 390 × 420 px: dialog 404 px, footer trong khung; đây là mô phỏng viewport khi nhập liệu |
| CSS cũ | 9 phần tử Bootstrap/modern giữ cùng màu, nền, viền, font, shadow và kích thước trước/sau nạp Commerce |
| Baseline | SHA256 của cả 560 file giao diện/tài nguyên trong baseline giữ nguyên |

Ảnh: [320 px](mobile-ui-20261025/evidence/ui-01/layout-320.jpg), [390 px](mobile-ui-20261025/evidence/ui-01/layout-390.jpg), [768 px](mobile-ui-20261025/evidence/ui-01/layout-768.jpg), [1440 px](mobile-ui-20261025/evidence/ui-01/layout-1440.jpg), [lỗi biểu mẫu](mobile-ui-20261025/evidence/ui-01/form-errors-390.jpg), [chi tiết dạng sheet](mobile-ui-20261025/evidence/ui-01/detail-sheet-390.jpg).

Dữ liệu đo: [viewport matrix](mobile-ui-20261025/evidence/ui-01/viewport-matrix.json), [CSS isolation](mobile-ui-20261025/evidence/ui-01/css-isolation.json), [DOM/assets](mobile-ui-20261025/evidence/ui-01/dom-assets.json), [tổng kết và hash](mobile-ui-20261025/evidence/ui-01/result.json).

## Giới hạn kiểm tra và bước tiếp theo

Máy có SDK 10.0.401/runtime 10.0.12, trong khi repo pin SDK 10.0.402/runtime 10.0.13. Bản UI được build bằng SDK/runtime đang cài trong công cụ độc lập, từ thư mục ngoài phạm vi `global.json` và truyền `-p:RuntimeFrameworkVersion=10.0.12` cho công cụ. Các file baseline SDK/runtime của repo giữ nguyên. **Chưa xác nhận build toàn ứng dụng hoặc chạy luồng nghiệp vụ trên baseline .NET yêu cầu.**

UI-01c còn phải đối chiếu cơ chế thích ứng với workspace thật. UI-01d đã có ảnh và kiểm tra bàn phím ở trình duyệt desktop; còn kiểm tra reduced motion/forced colors khi đang bật, zoom và thiết bị iOS/Android thật. UI-01e còn kiểm kê selector/event của ba màn POS orders, receiving và POS trước khi chuyển. Các việc này được giữ mở trong kế hoạch tổng.

Tại lần bàn giao UI-01a–b ban đầu, phần bổ sung vào source gồm 5 Razor, 4 CSS/JS, 1 model và 1 controller. Baseline 260 Razor/300 asset được giữ để đối chiếu; phần bổ sung tiếp theo được theo dõi ở báo cáo so sánh thiết bị bên trên. Hai partial nền `_Styles`/`_Variables` trong baseline UI-01 chưa cần chỉnh. Menu, migration, seeder và dữ liệu cửa hàng giữ nguyên.
