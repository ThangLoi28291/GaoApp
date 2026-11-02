# A5 — Skill inventory và ba phương án thiết kế

Ngày 02/11/2026. **SOURCE VERIFIED** cho inventory/source; **ASSUMPTION/PROPOSAL** cho thiết kế; **NOT TESTED** cho visual/performance runtime. Gate **BLOCKED — SKILL CHƯA CÀI**. Chưa có hai output thật từ skill UX/UI; không gọi tài liệu đề xuất dưới đây là kết quả chạy hai skill.

## Skill/MCP thực sự có gì

[Inventory đã ghi](evidence/skills-tools-inventory.txt). Repo không có `.agents/skills` hoặc `.codex/skills`. Đã kiểm tra ba tên ở user roots `C:/Users/wind/.agents/skills` và `C:/Users/wind/.codex/skills`; không thấy. Catalog của phiên không có ba skill này.

| Thành phần | Có/đã chạy trong pilot? | Đề xuất, chưa thực hiện |
|---|---|---|
| UI UX Pro Max | Chưa cài, chưa chạy | Review upstream/pin revision; nếu duyệt, chỉ tạo `.agents/skills/ui-ux-pro-max/SKILL.md` cùng scripts/data/resources của đúng skill. Không dùng global CLI installer. |
| Web Design Guidelines | Chưa cài, chưa chạy | Ưu tiên đầu tiên cho audit Razor/HTML/CSS/JS; `.agents/skills/web-design-guidelines/SKILL.md` và tài nguyên tham chiếu cần thiết. |
| Frontend Design | Chưa cài, chưa chạy | Tùy chọn sau audit; `.agents/skills/frontend-design/SKILL.md`, `agents/openai.yaml` nếu upstream có. Khóa stack Razor/Bootstrap và giới hạn scope để tránh chuyển framework. |
| Playwright MCP | Không có tool được expose; không thấy server trong scan config liên quan; chưa mở browser test | Cấu hình một server phiên bản đã review, profile isolated và localhost-only sau duyệt. Nếu cấu hình user-level, file dự kiến là `%USERPROFILE%/.codex/config.toml`; chưa sửa. Không dùng `npx ...@latest` trong lượt này. |
| Playwright Node library | Repo có bản 1.63.0 trong `GaoApp.Tests.Browser/node_modules`; không chạy browser | Thư viện đã có không đồng nghĩa MCP đã kết nối. Có thể tái sử dụng sau khi có endpoint test an toàn, không cài lại. |
| CUA/Computer Use | Tool browser/UI có sẵn; skill chỉ được đọc để kiểm kê | Không kích hoạt UI vì không có target test chạy được. Tool khả dụng không phải bằng chứng BROWSER VERIFIED. |
| OpenAI Docs | Đã dùng để đối chiếu hướng dẫn nhận diện skill local | Output thật là inventory + kế hoạch vị trí file này; không thay thế một skill UX/UI. |

Không cài/nâng cấp skill/MCP/package/.NET/Node/Bootstrap. Restore tool preview chỉ dùng nguồn local, project không có PackageReference; không cài dependency bên thứ ba.

Theo [hướng dẫn OpenAI về skill local](https://learn.chatgpt.com/docs/build-skills), Codex phát hiện skill từ các thư mục đã quy định và mỗi skill có manifest. Do đó tồn tại folder, được expose trong catalog và tạo output thực là ba điều cần ghi riêng. Chưa có chứng cứ bộ skill RoomRental được dùng ở checkout này.

Các nguồn upstream đã mở read-only: [UI UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill), [Web Design Guidelines](https://github.com/vercel-labs/agent-skills), [Frontend Design port](https://github.com/dobromirdikov/codex-frontend-design-skill). Guidelines phù hợp audit focus/form/accessibility/animation/navigation; Frontend Design là community port, cần review nội dung và license trước khi chọn. Chưa chạy code từ các nguồn đó.

[Playwright MCP](https://github.com/microsoft/playwright-mcp) có cơ chế profile isolated, là hướng cấu hình đề xuất cho một phiên demo riêng. [Tabler](https://github.com/tabler/tabler) chỉ tham khảo bố cục admin Bootstrap; không lấy package/theme của nó vào GaoApp.

## Nền thiết kế thực tế đã có

**SOURCE VERIFIED:** `wwwroot/Admin/css/gaoapp.design-system.css` cung cấp `gds` opt-in, type/spacing/color/radius/motion; `gaoapp.commerce.css` có bộ Commerce v1; `gaoapp.commerce-tokens.css` ghi approved mobile v2 palette. `_Styles.cshtml` nạp Public Sans từ `wwwroot/lib/fonts`, Bootstrap/vendor/core đang có. Product đã có desktop table/mobile cards và các adapter mobile; không cần đổi stack.

`docs/development/MOBILE-UI-MOBILE-V2-20261025.md` ghi navy/lam là hướng người dùng đã duyệt. Không lấy xanh lá ở demo UI-01 cũ làm chuẩn mới. Các quyết định menu và status/price owner giữ nguyên. Những khác biệt CSS theo scope chưa được kết luận là lỗi nhìn bằng browser.

## Token/component đề xuất cho P1 và hướng toàn hệ thống

**PROPOSAL:** dùng ý nghĩa token thống nhất, áp vào P1 trước; chưa sửa shared CSS/token. Giá trị navy/lam dưới đây lấy từ palette mobile v2 hiện có để tránh thêm một palette; semantic status là đề xuất cần QA tương phản trên nền thực. Chưa tuyên bố đạt chuẩn accessibility.

| Nhóm | Hướng đề xuất | Scope/guard |
|---|---|---|
| Màu nền/chữ | Ink `#19253b`, muted `#647188`, navy `#192c4a`, primary `#3858df`, canvas `#f4f6fa`, surface trắng, line `#e5eaf1`, focus `#315bd7` | Dùng token scope; không override Bootstrap toàn cục hoặc tự đổi desktop POS đã duyệt. |
| Status | success xanh + nhãn/icon; warning hổ phách + nhãn; error đỏ + nội dung; disabled/locked có giải thích | Không suy trạng thái giá/đơn từ màu, không che giá trị thật hoặc tháo native disabled/quyền. |
| Typography tiếng Việt | Giữ Public Sans local; body 14–15px desktop dense, secondary 12–13px có tương phản; input mobile16px; line-height khoảng1,45–1,55 | Mã/SKU/barcode giữ leading zero; tên xuống dòng có giới hạn nhưng có cách đọc đủ; số tiền căn phải/tabular-nums, giữ formatter server. |
| Density/spacing | Thang4/8/12/16/24/32px; desktop row dự kiến40–44px, thao tác chạm44–48px; mobile giữ card đọc được | Không lấy hàng cao32px cho touch. Thử page20/100/200 rồi mới chốt, không thêm setting density vào DB. |
| Navigation | Desktop sidebar/menu gốc; mobile dùng shell và cấu hình đã có | Chỉ AdminMenuItems theo quyền/Store; không thêm shortcut/sidebar/menu giả. Route được cung cấp trong A3/A6. |
| Search/barcode | Có nhãn/focus rõ/loading/empty/error; preserve Enter/Escape/quantity prefix và scan owner | P1 giữ AbortController/sequence/debounce/history. P2 chỉ presentation; không đổi keyboard handler hoặc chuyển focus bằng animation mới. |
| Filter/pagination | Nhìn rõ điều kiện áp dụng, reset, total/page và busy; vị trí thao tác ổn định | Không thêm client sort khác server, không reset filter/paging khi resize hoặc tạo trường nhập clone. |
| Modal/drawer | Dùng owner Bootstrap/native hiện có, focus-visible, Escape và trả focus; footer không che input khi bàn phím mở | Test focus trap/scroll/viewport thật trước khi đổi; không nhúng modal framework mới. |
| Table lớn | Dùng server paging đã có, header và nhóm cột dễ đối chiếu, scroller có nhãn khi cần | 10k+ tổng bản ghi không có nghĩa render10k DOM. Virtualization chỉ là phương án tương lai sau đo, không code ở P1. |
| Motion/loading | Trạng thái rõ ngay khi bắt đầu tác vụ; màu/opacity nhẹ120–160ms nếu cần; reduced-motion tắt chuyển động không thiết yếu | Không animate layout/filter/table từng dòng, không tạo timer/render loop hay bundle nặng. |

Component cần thống nhất dần: page header/filter/status/amount/paging/read-only detail/loading-empty-error. POS và ERP có thể dùng cùng ngôn ngữ hiển thị nhưng vẫn giữ payment/invoice/inventory/promotion workflow và permission riêng.

## Ba phương án gắn với source

| Phương án | Thay đổi P1/Product | Hiệu năng dự kiến — ASSUMPTION | Chi phí/rủi ro |
|---|---|---|---|
| A — Conservative | Giữ nguyên cấu trúc, chỉnh contrast/focus/font/spacing và độ rộng nhóm cột bằng CSS scope | Ít nguy cơ tăng JS; không cam kết giảm TTFB/SQL | Nhỏ nhất; phù hợp fallback khi BEFORE cho thấy bố cục hiện tại đủ tốt. |
| **B — Balanced, khuyến nghị có điều kiện** | Tinh chỉnh hierarchy header/filter/table trên bốn file view/CSS P1; giữ node/binding/owner/query và mobile v2; tái dùng gds | Không thêm JS/bundle/requests; có thể dễ đọc và ít cuộn hơn, chưa có số đo chứng minh | Vừa và có giới hạn; phải có BEFORE/role/demo/Git baseline trước khi sửa. Shared layout/token không nằm trong scope. |
| C — Visual refresh | Cấu trúc danh sách/bộ lọc sâu hơn, thay cách trình bày nhiều vùng; chỉ minh họa hướng, chưa code | Có rủi ro thêm DOM/render/reflow; chưa có bằng chứng lợi ích | Lớn nhất; cần audit và regression rộng, không đề xuất triển khai hàng loạt hoặc chuyển React/Next/Vue/Tailwind. |

B phù hợp mục tiêu P1 sau khi mở chặn; nếu phép đo chứng minh bottleneck là query/server, việc đó thành task backend riêng, không mở rộng pilot UI. P2 chỉ bắt đầu sau nghiệm thu P1 và một phê duyệt riêng. Chưa tạo mockup/ảnh thiết kế và chưa nhận phương án nào đã được user duyệt cho GaoApp.

## Đề xuất nâng cấp bộ skill sau này

Ưu tiên review/cài local Web Design Guidelines và UI UX Pro Max để tạo hai đầu ra độc lập về audit và token/density; Frontend Design là bổ sung tùy chọn. Chọn revision cụ thể và kiểm tra allowlist tài nguyên trước cài, không chạy installer global. Kịch bản nghiệm thu skill: mỗi skill nhận đúng file Razor/CSS P1, xuất finding file/line + đề xuất trong stack thật, không tạo menu/seed/query hoặc code ngoài scope. Đầu ra phải có thể đối chiếu source và browser; không dùng nhận xét thẩm mỹ để thay benchmark.

Ưu tiên sửa môi trường test và baseline trước công việc cài tool. Việc cài skill/MCP, sửa UiPreview và thay đổi config là các scope duyệt riêng, không được gộp ngầm vào `DUYỆT GAOAPP P1`.
