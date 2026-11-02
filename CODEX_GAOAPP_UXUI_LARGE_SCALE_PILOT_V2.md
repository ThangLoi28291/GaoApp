# CODEX TASK CONTRACT — GAOAPP UX/UI LARGE-SCALE PILOT V2

**Mã nhiệm vụ:** GAOAPP-UXUI-LARGE-SCALE-PILOT-V2  
**Ngày lập:** 02/11/2026  
**Đầu vào từ người dùng:** Pilot RoomRental Mobile đã chạy ổn; chuyển sang kiểm chứng trên GaoApp có nhiều phân hệ, bảng dữ liệu lớn và luồng nghiệp vụ phức tạp hơn.  
**Người thực hiện:** Codex trên máy Windows của chủ dự án, ưu tiên chạy trong ứng dụng Codex/VS Code, **không yêu cầu người dùng mở Codex bằng PowerShell**.  
**Trạng thái ban đầu:** CHƯA ĐỌC SOURCE GAOAPP TRÊN MÁY / CHƯA BUILD / CHƯA CHẠY / CHƯA KIỂM THỬ.  
**Tính chất:** Hợp đồng thử nghiệm có Gate, **không phải lệnh triển khai sản xuất**.

> **CHỈ THỊ CHÍNH CHO CODEX**: Đọc toàn bộ tài liệu, đọc `AGENTS.md` và các quyết định/quy trình hiện có trong repo trước khi làm. Thực hiện Gate A0–A6 theo đúng điều kiện an toàn. **DỪNG XIN PHÊ DUYỆT** trước khi tạo dữ liệu lớn, sửa giao diện ứng dụng hoặc chạy load test nhiều người dùng. Gate A7–A8 chỉ được thực hiện sau khi người dùng phê duyệt phạm vi cụ thể. Không tự ý bỏ qua STOP để đạt cho đủ Gate.

---

## 0. Mục tiêu và phạm vi

### 0.1. Mục tiêu thực tế

1. **Chạy đúng source GaoApp đang có** trên môi trường local/staging an toàn; không tự tạo một dự án GaoApp khác.
2. Xác nhận bộ skill UX/UI đã thử ở RoomRental có thể áp dụng hiệu quả cho hệ thống lớn hơn, không phá vỡ kiến trúc .NET/Razor/MVC hiện tại.
3. Kiểm kê **nhiều phân hệ/màn hình** thay vì chỉ một màn hình: POS, danh mục sản phẩm, kho, nhập hàng, đơn hàng, hóa đơn điện tử, khách hàng, dashboard/báo cáo, quản trị/phân quyền… **chỉ đưa vào kết quả những phân hệ thực sự tìm thấy trong source**.
4. Đo UX/UI và hiệu năng với **khối lượng dữ liệu kiểm thử đáng kể**, đặc biệt tìm sản phẩm, danh sách nhiều bản ghi, lọc/sắp xếp, giỏ hàng nhiều dòng, phản hồi khi bấm nút, tải màn hình mobile/desktop.
5. Tạo baseline BEFORE, đề xuất Design System thống nhất và tối đa **02 màn hình pilot theo thứ tự**: (1) màn hình bảng dữ liệu lớn ít rủi ro; (2) một vùng UI POS có tương tác dày đặc. **Không tự triển khai cả hai khi chưa được duyệt.**
6. Nếu được duyệt, thay đổi giới hạn ở UI, chụp AFTER, build/test thật, so sánh dữ liệu và hiệu năng BEFORE/AFTER, có rollback rõ ràng.

### 0.2. Đường dẫn dự kiến — bắt buộc xác minh

```text
E:\GaoApp\GAOAPP-SA-20260819-01
```

Đây là đường dẫn từ thông tin bàn giao trước, không bảo đảm hiện vẫn chính xác. Nếu không tìm thấy hoặc phát hiện repo khác, **STOP** và báo đường dẫn đã kiểm tra. Không `git init`, không checkout nhầm, không tự giải nén/ghi đè dự án.

### 0.3. Phạm vi mặc định được phép ngay

- Kiểm tra thư mục, Git, solution, project, tài liệu kiến trúc và các quyết định đã chốt.
- Phân tích code theo hướng **đọc có trọng tâm**; lập sơ đồ route → controller → view → JS/CSS → truy vấn/dữ liệu cho màn hình mục tiêu.
- Kiểm tra bộ skill `.agents/skills/`, dùng lại skill đã có; không tự cài đè.
- Build/run local và xem browser **chỉ khi đã xác minh app, DB, background service và tích hợp ngoài đều an toàn**.
- Đo đọc dữ liệu/hiển thị trên **DB test đã được người dùng cho phép**; ghi báo cáo Markdown, ảnh demo không chứa dữ liệu thật.
- Đề xuất 3 phương án UI, 2 màn hình pilot và phương pháp benchmark.

### 0.4. Những việc BẮT BUỘC xin phê duyệt riêng

- Tạo/sao chép DB, seed dữ liệu số lượng lớn, truncate/delete/update/insert vào bất kỳ database nào; chạy migration hoặc chỉnh schema/index/query.
- Load test nhiều người dùng, gửi request dồn dập, stress test SQL Server/IIS hoặc làm tăng tải hệ thống ngoài local test.
- Sửa `.cs`, `.cshtml`, `.razor`, `.js`, `.css`, `.json`, package, cấu hình run hay đổi framework; kể cả sửa UI "nhẹ".
- Bật/tắt Windows Service, IIS production, lịch chạy job, tích hợp thanh toán ACB, Viettel SInvoice, Zalo, gửi email/SMS, webhook/callback thật.
- Cài skill/MCP/package mới hoặc nâng cấp .NET/Node/npm/Codex nếu gây thay đổi cấu hình ngoài repo; khi đã có, không cài lại.
- Commit, push, merge, reset, clean, rebase, xóa/sửa nhánh hiện có, ghi đè tài liệu authority hoặc file người khác đang sửa.

### 0.5. Nguyên tắc bất biến nghiệp vụ và bảo mật

- **KHÔNG ĐỔI** quy tắc giá theo đơn vị/lốc/thùng, khuyến mãi, điểm thưởng, thanh toán, xuất hóa đơn, nhập hàng, tồn kho, multi-store/tenant/terminal, kiểm soát quyền, đơn hàng.
- Không tự thay đổi hành vi cho phép tồn âm, cách tính VAT, khóa/sửa hóa đơn, dữ liệu pháp lý, chính sách giá.
- Không chuyển sang React/Next/Vue/Tailwind hay thay Bootstrap/UI library chỉ vì skill gợi ý. Ưu tiên ngôn ngữ và frontend thực tế đã xác minh.
- Không gọi API phát hành hóa đơn, xác nhận/thu tiền thật, gửi Zalo khách thật hoặc bấm hoàn tất đơn hàng trong lúc kiểm tra UX.
- Không dùng DB production làm sân chơi. **Không clone data thật để sinh ảnh, log, seed, prompt hoặc báo cáo**. Chỉ sử dụng dữ liệu giả và danh tính test có quyền phù hợp.
- Không in connection string, token, cookie, mật khẩu, MST/điện thoại/tên khách thật vào output. Khi cần kiểm tra cấu hình, chỉ báo loại môi trường và trạng thái kết nối đã xác minh, không lộ giá trị bí mật.
- Không để Codex và Visual Studio đồng thời chỉnh cùng file; build bằng VS nếu người dùng chọn, nhưng phải tách thao tác ghi.
- Mọi kết luận phải gắn nhãn **SOURCE VERIFIED / BROWSER VERIFIED / MEASURED / ASSUMPTION / NOT TESTED**. Chỉ dùng PASS khi có log/bằng chứng thật.

---

## A0 — Source & governance discovery (READ-ONLY)

**Việc cần làm**

1. Xác nhận repo root, branch/commit, Git status trước mọi thao tác; kiểm tra có uncommitted changes hay không.
2. Đọc `AGENTS.md`, hướng dẫn phát triển, source map, decision log, backlog và chỉ dẫn trong các thư mục bị ảnh hưởng nếu có. Văn bản quyết định mới nhất/authority trong repo có ưu tiên cao hơn giả định của hợp đồng này; nếu mâu thuẫn, **STOP để báo**, không tự chọn.
3. Xác định solution `.sln`/`.slnx`, `.csproj`, `TargetFramework`, web startup project, cấu trúc Controller/View, assets, test project, cấu hình IIS/Service/DB. Không khẳng định file tồn tại nếu chưa thấy.
4. Kiểm tra nhánh Git, cây file đang sửa, skill hiện có, MCP có sẵn. Nếu skill đã tồn tại, chỉ kiểm tra và sử dụng, không cài chồng.
5. Tạo danh sách nguồn thực sự đã đọc và mapping màn hình theo mức ưu tiên. Với repo lớn, **không đọc toàn bộ bằng một prompt**, không đưa file khổng lồ vào context; dùng tìm kiếm có mục tiêu, bỏ `bin/obj/.vs/node_modules` và artifact build.
6. Chỉ xác minh thông tin kích hoạt từ source; không tự ghi một biến cấu hình production vào log.

**PASS A0:** xác minh đúng repo + trạng thái Git + file authority + startup/DB integration nguy cơ + danh sách module có đường dẫn thật. Nếu không có source, sai repo, dirty files có nguy cơ trùng phạm vi: `BLOCKED` phần thay đổi; vẫn có thể báo cáo read-only.

---

## A1 — Xác thực môi trường chạy GaoApp (chỉ local/test an toàn)

1. Đọc launch profile, `Program.cs`/startup, hosted services, scheduler, dependencies, provider integrations, connection mapping **trước khi run**.
2. Phân loại chính xác `DEV/TEST/STAGING/PRODUCTION/UNKNOWN`. Nếu **UNKNOWN**, ngừng start ứng dụng để tránh kích hoạt job/API thật.
3. Chỉ chọn DB test có thể xác minh **không thuộc production**, connection string được che; không tự sửa appsettings/live secrets để "chạy được".
4. Kiểm tra khả năng tự phát hành hóa đơn, đẩy đơn, gửi Zalo, callback ACB, cron lịch; xác nhận môi trường test không gọi ra hệ thống thật. Nếu không chứng minh được, **BLOCKED RUN** cho đến khi người dùng xử lý.
5. Để người dùng mở app bằng Visual Studio/VS Code nếu muốn; Codex được quyền gợi ý hoặc thực hiện `dotnet restore/build/run/test` trong terminal riêng sau khi xác minh giải pháp, không cần yêu cầu họ mở PowerShell để khởi chạy Codex.
6. Ghi lệnh build/test và kết quả, số test thực tế. `dotnet test` kết thúc thành công nhưng **0 test** không được ghi là đã test nghiệp vụ.
7. Ghi URL localhost thực tế từ console (không đoán port), login qua tài khoản demo, thử route an toàn không phát sinh giao dịch. Nếu DB test chưa có, vẫn bàn giao hướng dẫn để người dùng cấu hình, không tự dùng DB kinh doanh.

**PASS A1:** build thực sự thành công + local app mở được trên browser + môi trường và dữ liệu test an toàn được chứng minh; báo riêng `BUILD PASS / RUN BLOCKED` nếu chỉ build đạt.

---

## A2 — Thiết kế dữ liệu test quy mô lớn (KẾ HOẠCH, KHÔNG TỰ SEED)

**Không tự tạo dữ liệu.** Trước tiên kiểm kê dữ liệu test đã có và chốt giới hạn dung lượng DB, ổ đĩa, SQL Server edition, CPU/RAM, concurrency an toàn; không chạm production.

**Gợi ý hồ sơ tải thử — chỉ là đề xuất, điều chỉnh theo schema và tài nguyên thực tế:**

| Tier | Sản phẩm giả | Dòng đơn hàng giả | Dòng POS trong một giỏ | Mục đích |
|---|---:|---:|---:|---|
| T0 – Smoke | 200–1.000 | 2.000–10.000 | 10–30 | Kiểm tra UI đủ chạy |
| T1 – Medium | 5.000–10.000 | 50.000–100.000 | 50–100 | Bảng lớn, tìm kiếm, lọc, pagination |
| T2 – Large | 20.000–50.000 | 200.000–500.000 | 150–300 | Phát hiện trễ UI/render/quy trình phức tạp |

- **Đây không phải kích cỡ dữ liệu GaoApp hiện có**, không được viết "đã tạo/đã kiểm tra" nếu chưa làm.
- `Dòng đơn hàng` không đồng nghĩa `số hóa đơn`; Codex phải xác minh cardinality và ràng buộc dữ liệu từ schema trước khi ước tính.
- Khi chưa được duyệt **chỉ đo với dataset test hiện có** (nếu được phép) và nêu rằng benchmark quy mô lớn chưa thực hiện.
- Ưu tiên sinh dữ liệu tổng hợp bằng cơ chế seed test đã tồn tại; nếu không có thì **đề xuất script/test fixture, trình trước**, không thực hiện.
- Mọi mã sản phẩm, khách hàng, đối tác, mã hóa đơn, số điện thoại/email dùng giá trị giả rõ ràng; không dùng dữ liệu khách thật hoặc thông tin định danh có thể gọi người thật.
- Nếu dung lượng SQL Express/ổ cứng hoặc tài nguyên bị giới hạn, phải tính dung lượng dự kiến và đề nghị tier nhỏ hơn; tránh làm đầy ổ hệ điều hành.
- Không tạo trạng thái hóa đơn hợp lệ giả gắn với provider thật, không gọi hệ thống thuế hay tạo order/payment thật.

**PASS A2 (PLAN):** có tài liệu schema đã xác minh, an toàn DB, tier + ước lượng tài nguyên, scripts/luồng tạo demo đề xuất (nếu cần) và điểm duyệt. **PASS A2 (EXECUTED)** chỉ khi người dùng cấp quyền seed, dữ liệu đã tạo thực tế trên DB test và có báo cáo số bản ghi đo được.

---

## A3 — Audit UX/UI nhiều phân hệ, ghi baseline BEFORE

**Không mặc định tất cả phân hệ đều tồn tại.** Điền vào bảng sau chỉ sau khi xác minh route, màn hình, role test và quyền truy cập:

| Nhóm | Kịch bản cần quan sát | Rủi ro chính |
|---|---|---|
| Đăng nhập / Terminal | Đăng nhập, chọn quầy nếu có, chuyển role test | Sai terminal, mất phiên/điểm bán |
| POS | Quét/tìm mã, thêm nhiều dòng, đổi số lượng, tìm SKU | Lag khi giỏ lớn, focus máy quét, nhầm giá |
| Giá / khuyến mãi | Xem trạng thái áp giá, combo/điểm thưởng (không hoàn tất giao dịch) | Chạm sai quy tắc giá/khuyến mãi |
| Danh mục sản phẩm | Search/filter/sort, danh sách dài, chi tiết sản phẩm | Query chậm, DOM quá lớn, mobile overflow |
| Kho / tồn kho | Bảng nhiều SKU, lọc theo kho nếu có | Trộn dữ liệu kho/store, tồn không nhất quán |
| Nhập hàng | Danh sách phiếu, thông tin hàng, trạng thái duyệt (read-only) | Thay đổi/duyệt nhầm dữ liệu |
| Đơn hàng / lịch sử | Tìm đơn, lọc theo ngày, pagination | Mất filter, thiếu trạng thái, API nặng |
| Hóa đơn điện tử | Danh sách, xem chi tiết mẫu an toàn, trạng thái | Không được bấm phát hành/điều chỉnh thật |
| Khách hàng / công nợ | Tìm, lọc, biểu mẫu test nếu có | Lộ PII, phân quyền |
| Dashboard / báo cáo | Dashboard và bộ lọc nhiều dữ liệu | Chậm render, biểu đồ, tổng hợp nặng |
| Quản trị / phân quyền | Menu, role, phân tách legal entity/store | Lộ hoặc đổi sai quyền |

**Với mỗi màn hình đã xác minh**, thu thập:

- Route và file chính (`Controller/Action → View → CSS/JS → API/Query`) có đường dẫn và vị trí/line nếu được.
- Ảnh BEFORE trên viewport `390×844` (mobile), `768×1024` (tablet), `1440×900` (desktop); POS bổ sung `1920×1080` nếu đang dùng màn hình quầy.
- Kích cỡ dữ liệu demo tại thời điểm chụp, tài khoản/role test **chỉ ghi nhãn role**, không ghi user/password/token.
- Các dấu hiệu: tràn ngang, mất focus, khó đọc, số lần click thao tác phổ biến, trạng thái loading/empty/error, chi phí bảng dài, keyboard/scan, thao tác cảm ứng, accessibility cơ bản.
- Phân biệt phát hiện **qua source** hay **thực tế browser**; ảnh chỉ giữ nếu bảo đảm không chứa dữ liệu khách thật.

**PASS A3:** audit >= 5 nhóm màn hình **nếu có đủ nhóm thực tế**, mỗi nhóm có bằng chứng route và nhận xét; ít nhất 2 màn hình quan trọng có BEFORE ở mobile/desktop. Nếu app có dưới 5 nhóm, phải ghi đúng số thực và không bịa thêm.

---

## A4 — Benchmark hiệu năng an toàn (BASELINE)

### 4.1. Chỉ chạy trên test local đã xác minh

- Chọn kịch bản **chỉ đọc** trước: mở danh sách, tìm 1 mã sản phẩm, lọc, sort, mở chi tiết, chuyển trang.
- Với POS, thử nhiều dòng bằng **fixture/sandbox đã xác minh** và phải chắc chắn không ghi nhận đơn hàng/thanh toán thật. Nếu không có cách an toàn, chỉ benchmark đến khâu đọc/tìm/hiển thị, còn giỏ lớn ghi `BLOCKED`.
- Không gửi tải đồng thời / stress / soak nếu chưa được người dùng duyệt bằng Gate riêng. Mặc định chỉ **1 phiên browser**, nhịp thao tác bình thường.
- Trên SQL, ưu tiên công cụ đo read-only (nếu được cấp quyền) và thống kê tổng hợp; không tự bật cấu hình server-wide, không đổi index, no SQL tuning thật trong pilot UI.

### 4.2. Bảng chỉ số và phương pháp

| Chỉ số | Cách ghi nhận | Ghi chú |
|---|---|---|
| Thời gian mở màn hình | Navigation timing/Network hoặc đo browser thực tế | Ghi điều kiện cold/warm, đơn vị ms |
| Tìm kiếm mã/tên | Từ thao tác tìm đến danh sách hiển thị | Nêu cache và số kết quả |
| Lọc/sort/pagination | Từ tương tác đến UI sẵn sàng | Cùng filter và tier |
| POS update khi giỏ dài | UI phản hồi sau đổi số lượng | Không hoàn tất đơn/đẩy hóa đơn |
| Kích thước DOM / lag cuộn | Browser tools nếu có, hoặc quan sát có mô tả | Không tự khẳng định 60 fps nếu chưa đo |
| Số bản ghi tải một lần | Request/response có thể xác nhận | Kiểm tra server pagination/virtualization |
| Memory/CPU/browser errors | Chỉ số nếu có công cụ và quyền phù hợp | Không lộ payload nhạy cảm |
| `p50`, `p95` | Từ >= 5 lượt đo/kịch bản nếu khả thi | Nếu ít mẫu, báo số lượt và không gắn nhãn p95 đáng tin |

Đo BEFORE và AFTER **cùng dataset, cùng thiết bị, cùng tài khoản test, cùng kịch bản và viewport**. Nêu mọi khác biệt về cache/server/network. Không tự đặt hoặc bịa mức tăng `%`. Không coi tác vụ thao tác UI nhanh lên là bằng chứng SQL nhanh lên nếu không đo SQL.

**PASS A4:** có tối thiểu 3 kịch bản được đo thật trên test local, dữ liệu/tier/viewport/số mẫu được mô tả; nếu không có DB test an toàn thì A4 `BLOCKED` và không bịa chỉ số.

---

## A5 — Kiểm tra bộ UX/UI skill & Design System toàn hệ thống

**Nguồn công cụ đã dùng trong pilot trước:**

- UI UX Pro Max: https://github.com/nextlevelbuilder/ui-ux-pro-max-skill
- Web Design Guidelines: https://github.com/vercel-labs/agent-skills
- Frontend Design community port: https://github.com/dobromirdikov/codex-frontend-design-skill
- Playwright MCP: https://github.com/microsoft/playwright-mcp
- Tabler (tham khảo bố cục, **không tự cài**): https://github.com/tabler/tabler

**Quy trình:**

1. Kiểm tra `.agents/skills/{ui-ux-pro-max,web-design-guidelines,frontend-design}/SKILL.md` hoặc path skill thực tế đã cấu hình. Không giả định được cài sẵn vì pilot RoomRental ở repo khác.
2. Nếu thiếu, **chỉ đề xuất cài local**, dẫn link/trình bày file dự kiến bị ghi; không thực hiện `npx` tải code bên thứ ba khi chưa có phê duyệt.
3. Nếu đã có, gọi skill theo tên mà Codex nhận diện, ghi đầu ra cụ thể; đừng coi có folder là đã chạy skill thành công.
4. Nếu Playwright MCP có, kiểm tra nó mở browser thật qua localhost test. Nếu chưa có, đề xuất cấu hình sau, không tự sửa `%USERPROFILE%\.codex\config.toml`.
5. Dùng skill đề xuất bộ design token cho cả POS và ERP nhưng vẫn hỗ trợ **mật độ dữ liệu cao**. Cần hướng dẫn rõ về:
   - Điều hướng desktop/mobile, phân cấp rõ status/khối nghiệp vụ.
   - Màu sắc/trạng thái (error/disabled/loading/locked), typography tiếng Việt, spacing/density phù hợp quầy bán lẻ.
   - Input hỗ trợ barcode/focus/keyboard, modal/drawer, tìm kiếm, bảng 10k+ dòng theo phân trang hoặc virtualization **chỉ là đề xuất, chưa code**.
   - Hiệu ứng nhẹ, hạn chế animation/rerender, tránh JS bundle mới nặng.
   - Component thống nhất giữa POS, danh sách hóa đơn/kho/đơn hàng nhưng không gộp những nghiệp vụ phải tách.
6. Đề xuất **03 hướng thiết kế**, so sánh theo hiệu năng, mức thay đổi và rủi ro:
   - **A — Conservative:** giữ layout hiện hữu, tối ưu density/contrast/focus/bảng.
   - **B — Balanced (khuyến nghị thử):** Design System + layout cải thiện có giới hạn, tái sử dụng component đang có.
   - **C — Visual refresh:** đổi cấu trúc hiển thị sâu hơn, chi phí regression lớn, chưa nên áp dụng hàng loạt.

**PASS A5:** Codex xác minh skill có/không, ít nhất 2 skill tạo output thật nếu cài sẵn, có 3 phương án thiết kế được gắn với công nghệ và màn hình đã xác minh. Nếu chưa cài, ghi rõ `SKILL CHƯA CÀI` thay vì PASS.

---

## A6 — Đề xuất 2 pilot nhỏ và STOP xin phê duyệt

**KHÔNG CÓ QUYỀN SỬA UI TẠI A6.** Chọn màn hình dựa vào audit thực tế, ưu tiên:

- **Pilot P1 — Data-heavy / read-mostly:** danh sách sản phẩm / kho / đơn hàng / hóa đơn, chọn **một màn hình** có nhiều dòng và nhiều filter. Mục tiêu: typography, table responsive, pagination, search UX, loading feedback. Không đổi truy vấn/SQL nếu chưa duyệt riêng.
- **Pilot P2 — POS interaction:** vùng tìm/scan sản phẩm và danh sách giỏ nhiều dòng, chọn **một vùng UI cụ thể**; mọi event/binding/giá/khuyến mãi/phím tắt phải giữ nguyên. Chỉ bắt đầu sau khi P1 được review và được phê duyệt riêng.

Trước khi xin duyệt, phải xuất đủ:

1. Màn hình P1, P2 có route/file có thật, ảnh BEFORE, role test.
2. Danh sách file **dự kiến sửa** (đường dẫn chính xác), lý do, test hồi quy, rủi ro và rollback. Phân biệt view-scoped và shared layout; nếu shared ảnh hưởng toàn app, nêu rõ.
3. Dữ liệu/tier thực sự đã benchmark, những phép thử chưa được chạy vì an toàn.
4. 3 phương án UI, ảnh hưởng tới kết quả UX/performance, đề xuất chọn 1 phương án cho P1.
5. Những lựa chọn người dùng phải phê duyệt riêng: **(i) dùng/seed DB test nào, (ii) được thay đổi file UI nào, (iii) có cho phép benchmark đồng thời hay không**.

**Điểm dừng bắt buộc:** `STOP — CHỜ DUYỆT PILOT P1`. Không lập luận rằng người dùng đã duyệt pilot RoomRental tức là tự động duyệt sửa GaoApp.

---

## A7 — Thực thi P1/P2 sau duyệt (KHÔNG CHẠY TRƯỚC)

**Gate này chỉ kích hoạt khi người dùng ghi rõ `DUYỆT GAOAPP P1` hoặc phê duyệt tương đương với phạm vi/file. P2 cần duyệt riêng.**

- Trước sửa: bảo đảm backup/commit điểm xuất phát do người dùng thực hiện và Git sạch trong phạm vi cần sửa; lập nhánh độc lập `uxui/gaoapp-large-scale-p1` nếu được phép, không force nếu branch trùng tên.
- Chỉ chỉnh đúng các file đã được duyệt, ưu tiên `.cshtml/.css/.js` scope nhỏ nếu xác minh thực tế có. Không sửa backend, DB, API hoặc validation để phục vụ giao diện.
- Không xóa/đổi các `name/id/asp-*`, `data-*` quan trọng, form binding, antiforgery, script selector, barcode keyboard handling, permission, filter/sort model, query params, paging state.
- P1: cải thiện đọc bảng + trạng thái loading/empty/error + mobile; P2: mật độ quầy và focus, chống re-render thừa; không tự tối ưu truy vấn hoặc thay behavior trong quá trình UX.
- Không tạo thư viện frontend mới hoặc chèn remote CDN không được duyệt. Không tự biến UI thành SPA.
- Thực hiện review UI + kỹ thuật độc lập theo khả năng công cụ, chỉ ghi review độc lập nếu thực sự có một lượt review tách biệt với người sửa.

**Trạng thái sau khi sửa nhưng chưa test:** `ĐÃ SỬA, CHƯA TEST`, không ghi PASS.

---

## A8 — AFTER, hồi quy, báo cáo và nghiệm thu (chỉ sau A7)

1. Build/test theo lệnh/solution đã xác minh ở A1; báo project, configuration, số test thật.
2. Run trên DB test đã chốt; chụp AFTER cùng URL, role test, dataset, viewport và trạng thái trước. Không chạy nếu phát sinh integration thật.
3. Test UI smoke: bàn phím/barcode focus nếu P2, tìm/lọc/sort/phân trang nếu P1, tương tác mobile; kiểm tra màn hình lân cận để phát hiện shared CSS regression.
4. Đo lại các kịch bản trong A4 với cùng điều kiện; báo chênh lệch thực tế và độ không chắc chắn.
5. Kiểm tra `git diff --check`, danh sách file changed, test record, ảnh trước/sau; không tự commit/merge/push.
6. Đề xuất cho người dùng nghiệm thu P1 và chỉ sau đó xin duyệt P2 hoặc mở rộng theo batch; **không nâng cấp hàng loạt**.

**PASS A8:** build thật đạt, UI chạy thật, kiểm thử các chức năng liên quan trên DB test đạt, before/after cùng điều kiện, không thấy regression trong phạm vi test. Nếu phần nào chưa thử phải ghi `CHƯA TEST` rõ, không quy toàn dự án là PASS.

---

## Bộ artifact phải bàn giao

Tạo file Markdown báo cáo trong thư mục riêng, chỉ sau khi xác nhận không có file khác đang quản lý cùng tên:

```text
E:\GaoApp\GAOAPP-SA-20260819-01\docs\uxui\large-scale-pilot\
    00-SOURCE-GOVERNANCE-MAP.md
    01-LOCAL-RUN-SAFETY-BASELINE.md
    02-LARGE-DATA-TEST-PLAN.md
    03-MULTI-MODULE-UXUI-AUDIT.md
    04-PERFORMANCE-BEFORE.md
    05-SKILLS-DESIGN-SYSTEM-OPTIONS.md
    06-P1-P2-APPROVAL-PLAN.md
    07-PILOT-AFTER-RESULT.md       # chỉ tạo nếu A7/A8 thực sự chạy
    08-FILES-DIFF-ROLLBACK.md      # chỉ tạo nếu có thay đổi source
    evidence\before\             # chỉ ảnh demo an toàn
    evidence\after\              # chỉ ảnh demo sau duyệt
```

**Quy ước:** Không tạo file giả với chỉ số ước lượng. Nếu Gate BLOCKED thì trong report ghi nguyên nhân, bằng chứng, phần đã làm và đề xuất mở chặn. Nếu repo không cho phép tạo report theo governance thì nộp nội dung trong phản hồi, không tự thay đổi đường dẫn authority.

### Form báo cáo cuối bắt buộc

1. **KẾT LUẬN HIỆN TẠI:** Repo path, branch, đã build/run thật chưa, môi trường local/test hay BLOCKED.
2. **SCOPE:** Phân hệ tìm thấy thực tế, các file chính/route, nguồn evidence.
3. **GATES:** A0–A8 với trạng thái `PASS / FAIL / BLOCKED / CHƯA TEST / SKIPPED` và bằng chứng cụ thể.
4. **SQL/DATA:** DB test được xác minh thế nào, tier thật đã chạy, giới hạn tài nguyên, những gì CHƯA seed/test.
5. **UX/UI:** Danh sách lỗi ưu tiên cao, 3 phương án design, P1/P2 được chọn.
6. **PERFORMANCE:** Bảng BEFORE (và AFTER nếu có) với số đo thật, dataset, viewport, số lần đo, p50/p95 nếu đủ mẫu.
7. **SKILLS/MCP:** Có sẵn/cài mới/chưa có; có thật sự chạy và tạo output không.
8. **CHANGES:** File tạo/sửa thực tế, `git status`, diff, lệnh build/test, kết quả; không lộ secret.
9. **ROLLBACK:** Cách hoàn tác chỉ thay đổi do pilot tạo, giữ nguyên thay đổi có sẵn; tuyệt đối không `git reset --hard` / `git clean -fd`.
10. **PHÊ DUYỆT CẦN THIẾT:** Đúng một yêu cầu rõ ràng tiếp theo, ưu tiên duyệt P1 và DB test riêng; sau đó STOP.

### Ma trận nghiệm thu

| Gate | PASS chỉ khi | STOP/BLOCKED khi |
|---|---|---|
| A0 | Repo/authority/Git/module thật đã xác minh | Không thấy source hoặc va chạm governance |
| A1 | Build + browser local trên môi trường an toàn | DB production/unknown, integration không kiểm soát |
| A2-PLAN | Có kế hoạch test data với schema/dung lượng/tier | Muốn seed chưa được duyệt |
| A3 | Audit nhiều màn hình thật + ảnh BEFORE hợp lệ | Chưa mở app hoặc ảnh chứa PII |
| A4 | Baseline đo bằng số liệu thực tế | Không có DB test/an toàn hoặc muốn stress chưa duyệt |
| A5 | Skill được xác minh và phương án UI có evidence | Chưa có skill, không tự báo PASS |
| A6 | Đề xuất P1/P2 + file/rủi ro/test plan | Chưa được duyệt sửa |
| A7 | Sửa đúng phạm vi đã duyệt | Không có phê duyệt P1/P2 cụ thể |
| A8 | Build/test/UI/benchmark sau sửa thực sự đạt | Có regression hoặc chưa test |

---

## Lưu ý tối ưu ngữ cảnh Codex và chi phí thực thi

- Dự án GaoApp lớn: **không gọi đọc toàn repo một lần**. Đầu tiên mapping docs/route, sau đó mới mở file liên quan.
- Chạy **A0 → A1 trước**, nếu an toàn và có test DB mới đo. Khi bị chặn, báo ngay ngắn gọn blocker, không tự thử hàng chục cách gây rủi ro.
- Đo một tập kịch bản đại diện, không cần chụp hàng trăm ảnh. Cần chọn nhóm màn hình ưu tiên và giải thích lựa chọn.
- Không làm benchmark nặng trước khi có dữ liệu tổng hợp và giới hạn tài nguyên rõ ràng.
- Phân biệt UI skill review và benchmark backend: hai loại bằng chứng không thay thế nhau.

---

## PROMPT KÍCH HOẠT — DÁN VÀO CODEX (GUI)

```text
Tôi đã chạy pilot RoomRental Mobile ổn. Bây giờ hãy chuyển sang GaoApp,
repo dự kiến E:\GaoApp\GAOAPP-SA-20260819-01.

Đọc TOÀN BỘ file CODEX_GAOAPP_UXUI_LARGE_SCALE_PILOT_V2.md,
đọc AGENTS.md và authority/docs hiện có trong repo.

Mục tiêu: kiểm chứng UX/UI trên dự án nhiều phân hệ và dữ liệu lớn,
ưu tiên hiệu năng POS, bảng dữ liệu lớn và tương tác mobile/desktop.

Thực hiện A0 → A6 với bằng chứng thật. Chỉ chạy local khi chắc chắn
không kết nối sản xuất và không gọi hóa đơn/ACB/Zalo/webhook thật.
Không tự tạo dữ liệu lớn, không stress test, không sửa UI/backend/DB,
không tự cài đè skill, không commit/push/deploy. Nếu thiếu điều kiện,
đánh dấu BLOCKED và nêu phương án an toàn.

Lập audit nhiều phân hệ, kế hoạch tier dữ liệu tổng hợp, baseline
hiệu năng nếu có DB test an toàn, 3 phương án UI và đề xuất P1/P2.

Sau A6 STOP để tôi duyệt P1 và kế hoạch DB test. Chỉ khi tôi phê duyệt
rõ ràng mới tiến hành A7/A8 theo phạm vi đã duyệt.

Trả báo cáo Gate PASS/FAIL/BLOCKED/CHƯA TEST, file/diff,
build/run/test thật, rủi ro/rollback và câu hỏi duyệt tiếp theo.
```

**END TASK CONTRACT — Mặc định chỉ A0–A6. Không có quyền thay đổi source hay dữ liệu trước phê duyệt riêng.**
