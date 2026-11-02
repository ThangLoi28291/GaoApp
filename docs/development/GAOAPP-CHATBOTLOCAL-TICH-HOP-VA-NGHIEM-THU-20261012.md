# GaoApp tích hợp ChatbotLocal — triển khai và nghiệm thu ngày 12/10/2026

Đã triển khai phần tích hợp GaoApp và kiểm tra với ChatbotLocal, Gemini thật, danh mục/giá GaoApp thật được phép đọc. Bản hiện tại dành cho kiểm thử; chưa bật trên hệ thống vận hành. Các phần còn cần nghiệm thu tại cửa hàng được ghi riêng bên dưới.

Hợp đồng tham chiếu: `docs/BAN-GIAO-GAOAPP-TICH-HOP-CHATBOTLOCAL-API.md` trong checkout ChatbotLocal. Phiên bản dữ liệu trên wire là `contractVersion: "1.0"`.

## 1. Phạm vi đã làm

- Nút hội thoại nhỏ trong layout quản trị; tên xưng hô lấy từ nhân viên đang đăng nhập. Mở lại tiếp tục hội thoại theo nhân viên/cửa hàng/phạm vi; có chọn, tạo, đổi tên, xóa hội thoại và tải lịch sử cũ.
- Gõ câu hỏi hoặc nói tiếng Việt để điền câu hỏi, sửa trước khi gửi; trả lời bằng chữ. Đoạn nhận dạng tạm thời của mic trợ lý chỉ hiển thị trạng thái, chỉ đoạn final được điền vào ô nhập.
- Giá đọc từ GaoApp theo đúng sản phẩm/biến thể/đơn vị. Mặc định giá lẻ; giá sỉ và cả hai theo câu hỏi, quyền hiện tại. Giá đơn vị lấy từ dữ liệu lưu, không nhân giá hộp để tự suy giá lốc/thùng.
- Trong cửa sổ nhận hàng, câu như “4 lốc TH có đường 180ml” được gửi tới API đề xuất. Chỉ kết quả `matched` mới có nút điền; sau đó xác minh quy cách qua API nghiệp vụ đang có và mở popup số lượng để nhân viên kiểm tra, xác nhận.
- Form sản phẩm mới có mic và nút đề xuất tên. Hiển thị câu cần làm rõ, tên đề xuất và hàng có thể trùng; nhân viên chọn dùng tên rồi lưu qua chức năng hiện có.
- Không tự lưu phiếu/sản phẩm từ response trợ lý, không dùng giá bán làm giá nhập. Không thêm menu, seeder menu, migration hay cấp quyền vào dữ liệu vận hành.

Hướng dẫn phần mềm và tồn kho hiện trả capability chưa bật từ ChatbotLocal. Nút chat đã có, nhưng cần hoàn tất kho hướng dẫn và bật capability ở ChatbotLocal trước khi nghiệm thu trả lời các hướng dẫn GaoApp.

## 2. Cách hai dự án kết nối

Trình duyệt → GaoApp backend → ChatbotLocal backend. ChatbotLocal gọi lại GaoApp backend để kiểm tra quyền và đọc danh mục/giá.

GaoApp dùng cookie đăng nhập và CSRF; nhân viên, cửa hàng, kho/pháp nhân được lấy ở server. Trình duyệt không được tự khai báo danh tính hoặc quyền. Mỗi lượt backend ký cặp token RS256 riêng, thời hạn tối đa 120 giây; hai token có loại, audience và vai trò khác nhau. Khóa riêng ở GaoApp; ChatbotLocal dùng khóa công khai tương ứng. Callback nguồn kiểm tra quyền đang còn hiệu lực và giao với quyền đã ký.

| GaoApp route | Chức năng |
| --- | --- |
| `GET /admin/api/assistant/context` | Ngữ cảnh phiên hiện tại; tùy chọn `documentId` để lấy phạm vi phiếu nhập |
| `POST /admin/api/assistant/call/{operation}` | Trung gian cho các thao tác chat, hội thoại, form, recovery và hủy |
| `POST /api/v1/assistant/context/authorize` | Callback kiểm tra quyền hiện tại |
| `POST /api/v1/assistant/catalog/search` | Tìm và làm rõ hàng, có phân trang |
| `GET /api/v1/assistant/catalog/variants/{variantId}` | Chi tiết hàng và quy cách hợp lệ |
| `POST /api/v1/assistant/quotes/lookup` | Giá theo lựa chọn xác định, tối đa 20 mục/lượt |

Callback không dùng cookie trình duyệt, chỉ nhận delegation JWT đúng hợp đồng. Catalog không trả giá sỉ, giá vốn hoặc tồn kho. Dữ liệu có ID, số lượng, hệ số và số tiền dạng chuỗi theo hợp đồng. Quyền bị thu hồi, scope sai hoặc lựa chọn sai bị từ chối.

Nhận 202 hoặc mất response: kiểm tra bằng UUID gốc. Chỉ gửi lại cùng UUID/body sau khi server xác nhận chưa có request; thao tác mới dùng UUID mới. Đổi nội dung hoặc phạm vi làm cũ generation/epoch và bỏ kết quả trễ; nút hủy gọi API hủy thực sự. Client không truy cập tùy ý URL trong Location.

## 3. Cấu hình để bật ở môi trường kiểm thử

Mặc định `Assistant.Enabled = false`. Thêm cấu hình riêng của môi trường, ví dụ:

```json
{
  "Assistant": {
    "Enabled": true,
    "BaseUrl": "https://chatbot.example.internal",
    "Issuer": "https://gaoapp.example.internal",
    "KeyId": "gaoapp-assistant-v1",
    "PrivateKeyFile": "../../private-config/gaoapp-assistant-private.pem",
    "TimeoutSeconds": 100,
    "TokenLifetimeSeconds": 120,
    "ReceivingEnabled": true,
    "NormalizeEnabled": true
  }
}
```

URL/đường dẫn trên là ví dụ. Dùng RSA ít nhất 2048 bit, giữ khóa riêng ngoài thư mục public và repository. Khai báo issuer, key ID, khóa công khai tương ứng và URL callback nguồn GaoApp trong cấu hình ChatbotLocal theo runbook của dự án đó. URL HTTP loopback chỉ được client chấp nhận trong Development.

Hai ứng dụng có thể chạy cùng server dưới hai tiến trình/site riêng. Cấu hình giới hạn tài nguyên, timeout, giám sát từng tiến trình; cùng server vẫn có thể ảnh hưởng nhau khi thiếu CPU/RAM. Không đưa ChatbotLocal vào startup hoặc luồng lưu nghiệp vụ của GaoApp. Khi ChatbotLocal mất kết nối, UI báo lỗi và nhân viên tiếp tục gõ/tìm/quét mã theo luồng hiện có. Lỗi tải launcher được xử lý để không làm hỏng layout.

Các permission được khai báo độc lập với menu:

| Permission | Điều kiện bổ sung |
| --- | --- |
| `assistant.use` | Bật quyền sử dụng trợ lý |
| `assistant.chat` | Chat và quản lý hội thoại |
| `assistant.receiving.resolve` | Đồng thời có quyền tạo chứng từ kho hiện tại |
| `assistant.products.normalize_name` | Đồng thời có quyền tạo sản phẩm |
| `quotes.retail.read` | Tra giá lẻ |
| `quotes.wholesale.read` | Tra giá sỉ; tra giá hiện cần quyền lẻ nữa |
| `catalog.read` | Suy ra từ quyền xem sản phẩm hoặc tạo chứng từ kho; không tạo quyền menu mới |

Quản trị viên dùng chức năng quản lý quyền đang có để gán cho vai trò cần dùng. Không có script gán quyền hàng loạt trong bản này. Nút nổi xuất hiện khi tính năng bật và phiên có quyền sử dụng.

Gemini key vẫn nằm ở backend ChatbotLocal. Bản này chưa làm màn hình nhập/quản lý Gemini key tại GaoApp. Ba project được gắn metadata vào runtime kiểm thử theo thứ tự người dùng gửi:

1. Chính: `advance-honor-455815-n9`.
2. Dự phòng 1: `noble-nation-319018`.
3. Dự phòng 2: `gen-lang-client-0013763006`.

Giữ model cấu hình sẵn `gemini-3.5-flash-lite`. Các call thật của phiên kiểm thử dùng project chính; chưa ép gọi riêng hai project dự phòng hoặc thử quota thật. Project ID không thay thế API key. Không sửa nội dung các file key đang có, không đưa key vào source hoặc trình duyệt. Runtime thử dùng DB riêng, không phải cấu hình deployment vận hành.

## 4. Kết quả kiểm thử và bằng chứng

Chạy bằng .NET SDK 8.0.420, SQL Express local, Node, Edge headless trên máy phát triển. Build GaoApp và ChatbotLocal thành công. NuGet báo NU1900 do không truy cập được nguồn dữ liệu kiểm tra lỗ hổng trong môi trường này; đây không phải kết quả xác nhận dependency không có lỗ hổng.

| Nhóm | Kết quả | Giới hạn |
| --- | --- | --- |
| GaoApp unit/integration | 8/8 đạt | JWT, strict JSON, quyền/scope, giá, đủ 23 đơn vị, lỗi launcher; fixture riêng |
| GaoApp HTTP/SQL | 3/3 đạt | BFF với 2 nhân viên/2 cửa hàng, CSRF, đổi quyền, sai scope, tắt trợ lý; lưu phiếu qua nghiệp vụ trên DB TEST |
| JavaScript client | 7/7 đạt | UUID/body, 202, recovery, hủy, stale scope/generation, hết hạn; transport mô phỏng |
| UI Edge headless | 8/8 đạt | Nút nổi, resume, chat, form, stale response, mobile 390px; transport/speech events mô phỏng |
| ChatbotLocal hồi quy | 1.095/1.095 đạt, 0 bỏ qua | Unit và HTTP offline trong bộ lọc của phiên; không gọi Gemini |
| API với nguồn GaoApp thật | 21/21 đạt | Nguồn chỉ đọc, Gemini thật cho chat; không lưu phiếu/sản phẩm vận hành |
| Hội thoại tra giá liên tiếp | 9/9 lượt đạt | Danh mục và giá thật; có kiểm tra nội dung trả lời, không chỉ HTTP 200 |

Nguồn GaoApp thật hiện có một cửa hàng hoạt động trong tập đọc được. Hai nhân viên/hai cửa hàng và đổi quyền được thử trên DB SQL fixture; không tính là hai cửa hàng thật. Các response nguồn dùng `source.kind = gaoapp`, `isTest = false` để chỉ nguồn dữ liệu, không có nghĩa đã deploy vận hành.

Ví dụ đã đối chiếu: “Sữa tươi TH có đường hộp 180ml”, product/variant `156272`:

| Đơn vị | Unit/conversion | Hệ số | Giá lẻ tại lần thử | Giá sỉ tại lần thử |
| --- | --- | --- | --- | --- |
| Hộp | `14` / `51659` | 1 | 10.000 đ | 10.000 đ |
| Lốc | `16` / `67419` | 4 | 38.000 đ | 38.000 đ |
| Thùng | `85` / `67420` | 48 | 448.000 đ | 448.000 đ |

Giá sỉ trùng giá lẻ là dữ liệu hiện tại, không phải fallback. “4 lốc TH có đường 180ml” khớp đúng quy cách và tương đương 16 hộp. “0.75 Hộp …” giữ nguyên số thập phân. Tên sửa thành “sữa tươi của TH có đường loại 180ml” được đề xuất “Sữa tươi TH có đường 180ml”, có một hàng gần trùng; câu thiếu đơn vị “180” yêu cầu làm rõ.

Hội thoại đã thử: `giá sữa` → `TH` → `có đường` → `180ml` → `1` → `giá lốc` → `báo đủ luôn` → `giá sỉ` → `cả giá lẻ và sỉ`. “Giá lốc” chỉ trả lốc; “báo đủ luôn” trả lại đủ hộp/lốc/thùng.

Tổng metrics provider thật trong DB kiểm thử, gồm cả lượt tìm lỗi trước khi sửa: 27 call, 27 call provider thành công; 6.570 input token, 782 output token, 7.352 total token. Thời gian provider 664–6.628 ms, trung vị 870 ms. Đây không phải thời gian toàn request và không có nghĩa mọi lượt trước sửa đều đúng ngữ nghĩa. Không trộn số này với kiểm tra offline; không suy ra tiền hoặc quota còn lại.

Bằng chứng cục bộ trong `Logs/assistant-integration-20261012/`: TRX `assistant-final-unit`, `assistant-final-http`, `chatbot-offline-valid-root`; `browser-results.json`, `live-results.json`, `live-chat-prices.json`, `live-product.json`, `live-quotes.json`, `live-receiving.json`, `live-normalize.json`, `real-ai-metrics.json`. Thư mục này được loại khỏi Git vì chứa build, runtime khóa/DB và dữ liệu đọc thử. Bản test trước sửa giữ riêng ở `live-chat-prices-before-fix.json`.

## 5. Đối chiếu 20 tình huống bàn giao

“Fixture” là dữ liệu kiểm thử riêng; “thật” là danh mục/provider được phép. Không coi mic mô phỏng là mic thật hoặc lưu fixture là ghi chứng từ vận hành.

| # | Tình huống | Bằng chứng hiện có / còn cần thử |
| --- | --- | --- |
| 1 | Resume, mở/đóng, chuyển cửa hàng | UI và HTTP fixture đạt; cần thử chuyển/tải lại trên thiết bị sử dụng thật |
| 2 | Nhân viên khác, ID/scope khác | HTTP hai nhân viên/hai cửa hàng và bộ ChatbotLocal đạt với fixture; chưa thử hai cửa hàng thật |
| 3 | JWT hết hạn/sai, thu hồi quyền | Unit, HTTP và callback nguồn thật đạt |
| 4 | Làm rõ giá sữa → TH → có đường → 180ml | Hội thoại nguồn thật đạt sau sửa |
| 5 | Đủ đơn vị và giá từng đơn vị | Hộp/lốc/thùng thật đạt; giá đối chiếu API nguồn |
| 6 | Giá lốc → báo đủ luôn | Hội thoại thật đạt |
| 7 | Lẻ/sỉ/cả hai, quyền sỉ | Thật đạt; token giảm quyền sỉ bị từ chối, fixture kiểm tra quyền nhân viên |
| 8 | Missing/error/0, thiếu cả hai | Fixture GaoApp và ChatbotLocal đạt; chưa có ca thật cho mọi nhánh |
| 9 | Chỉ nhận hàng, >20 đơn vị bán | Fixture 23 đơn vị và hồi quy ChatbotLocal; chưa có ca thật dạng này |
| 10 | Thiếu loại đường, chọn hoặc bổ sung | Hồi quy form/UI ChatbotLocal đạt bằng fixture; cần thử lại trên màn hình nhập hàng cửa hàng |
| 11 | 4 lốc, thập phân, dung tích | API nguồn thật đạt; input speech ở đây là text, không phải âm thanh thật |
| 12 | Nhiều mặt hàng, đơn vị sai | API thật yêu cầu tách hai mặt hàng; đơn vị sai kiểm tra fixture và lựa chọn nguồn |
| 13 | Tên thiếu đơn vị rồi sửa text hiện tại | API thật và UI fixture đạt |
| 14 | Hàng gần trùng khi chuẩn hóa | API thật có duplicate, UI fixture hiển thị; nhân viên chưa nghiệm thu thao tác trên thiết bị thật |
| 15 | Kết quả trễ, sửa form, đổi scope | Client và UI fixture đạt |
| 16 | 202/lost response/retry UUID/body | Client và HTTP hồi quy đạt; chưa tạo mất mạng trên thiết bị cửa hàng |
| 17 | Mic thật Edge/thiết bị, từ chối quyền | Chưa thử mic thật. Chỉ đã thử speech final/interim mô phỏng; cần nghiệm thu tại cửa hàng |
| 18 | Chat AI thật, guides/tồn kho chưa bật | Chat Gemini thật đạt; capability chưa bật được giữ đúng. Kho hướng dẫn GaoApp còn cần hoàn tất ở ChatbotLocal |
| 19 | Lỗi nguồn/provider/quota, tắt trợ lý | Hồi quy lỗi/cooldown và fixture tắt trợ lý đạt; không ép quota Google thật, chưa đo tải khi hai app chạy cùng server |
| 20 | Kiểm tra rồi xác nhận, lưu TEST | UI fixture chỉ điền sau click; HTTP/SQL fixture lưu qua nghiệp vụ đạt. Còn một lượt đầy đủ từ mic → popup thật → lưu phiếu TEST trên thiết bị |

## 6. Các sửa phía ChatbotLocal cần mang cùng bản GaoApp

Kiểm thử danh mục thật phát hiện lỗi mà fixture nhỏ không thể hiện. Đã sửa trực tiếp trong project ChatbotLocal:

- `AssistantDataModels.cs`: lưu cờ `HasMoreMatches` của trang catalog còn phân trang.
- `ProtectedChatService.cs`: giữ ý định hỏi giá và query khi danh mục hơn một trang; câu hỏi giá/đơn vị thuần túy tiếp tục sản phẩm duy nhất đã xác định, rồi kiểm tra nguồn/giá mới.
- `AssistantCatalogClarification.cs`: cho phép làm rõ hãng chưa nằm trên trang đầu; nhận câu nối giá sỉ, giá lốc, cả lẻ và sỉ; không thay thế routing cho câu hỏi chung hoặc tên hàng mới.
- `AssistantCatalogClarificationTests.cs`: thêm 10 ca hồi quy cho danh mục phân trang và câu nối giá.

Phía GaoApp cũng sửa tìm kiếm để `contextVariantIds` không bỏ qua query mới, nhận dung tích/đường từ câu đã cung cấp và dùng ranh giới từ cho hãng TH, tránh nhầm “Ông Thọ”. Không thay đổi key/provider model hay dữ liệu hàng hóa.

Không dùng lại build ChatbotLocal trước các sửa này khi test tích hợp. Báo cáo 1.176 kiểm tra mock cũ của dự án là bằng chứng lịch sử, không được tự cộng thành số kiểm tra thật của bản này.

## 7. Các bước nghiệm thu tiếp

1. Đưa hai bản build tương ứng lên môi trường test riêng; khai báo URL, cặp khóa tích hợp và quyền vai trò. Để tắt tính năng trên môi trường vận hành đến khi nghiệm thu.
2. Nhân viên thử mic thật trên Edge và điện thoại/thiết bị cửa hàng, cả khi từ chối mic; kiểm tra chỉ final được gửi, sửa chữ trước gửi, câu trả lời bằng chữ.
3. Dùng phiếu TEST, nói 4 lốc TH, kiểm tra popup đúng tên/quy cách/số lượng rồi bấm xác nhận; thử lưu, mất kết nối và tránh lưu lặp qua luồng đang có.
4. Thử resume qua tải lại trang, đổi nhân viên/cửa hàng/kho/pháp nhân thực tế; thử tắt hoặc dừng ChatbotLocal và tiếp tục tìm tay/quét mã.
5. Nạp, nghiệm thu kho hướng dẫn ở ChatbotLocal rồi bật capability guides. Chưa bật tồn kho nếu chưa có API/quyền được thống nhất.

Không cần tạo menu trợ lý mới. Nếu sau này có trang cấu hình riêng, cung cấp đường dẫn để người dùng tự thêm qua Quản lý menu.

## 8. Đã bật để người dùng thử trực tiếp trên GaoApp local

Theo yêu cầu tiếp theo của người dùng ngày 12/10/2026, đã bật riêng `Assistant` trong `GaoApp.Web/appsettings.Development.json`. GaoApp đang chạy ở `http://localhost:7051` đã nhận cấu hình mới mà không cần dừng tiến trình. Cấu hình Production không thay đổi.

ChatbotLocal chạy nền tại `http://127.0.0.1:5219`, callback trực tiếp vào GaoApp `http://localhost:7051`, không dùng source harness 7066. Runtime ở `Logs/assistant-integration-20261012/local-ui/runtime.json`; history/operations/legacy DB local riêng với lượt nghiệm thu trước. Dùng cặp khóa tích hợp local đã tạo và các Gemini key/project hiện có; không đưa key ra trình duyệt.

Đã xác minh: GaoApp live 200, ChatbotLocal live/ready 200, callback ký hợp lệ được cho phép, capability chat/receiving/normalizeName đều available. Chưa gọi mic hoặc lưu nghiệp vụ thay người dùng trong lần bật này.

Mở [GaoApp local](http://localhost:7051/admin), đăng nhập tài khoản quản trị có quyền trợ lý và tải lại trang để hiện nút 💬. Trong phiếu TEST, dùng “Nói tên hàng”, kiểm tra/sửa text, bấm “Hiểu câu nhập hàng”, sau đó “Điền để kiểm tra”; xác nhận thêm dòng/lưu bằng luồng hiện có.

Nếu ChatbotLocal đã dừng, khởi động lại từ thư mục repository:

```powershell
.\scripts\assistant\start-local-chatbot.ps1
```

Script từ chối cổng đang bị chiếm, không tự dừng ứng dụng khác hoặc tạo phiếu/sản phẩm. PID và log được lưu trong thư mục `local-ui`. Để tắt trợ lý local, đổi `Assistant.Enabled` thành `false` trong `appsettings.Development.json` rồi tải lại trang.

### Chuyển source sang thư mục hoặc ổ đĩa khác

Giữ nguyên thư mục `Logs/assistant-integration-20261012` khi chuyển GaoApp để giữ khóa tích hợp, bản build và dữ liệu hội thoại local. `Assistant:PrivateKeyFile` và `ChatbotTestApi:ApiKeyFilePath` dạng tương đối được tính từ content root của `GaoApp.Web`, không phụ thuộc thư mục terminal. Mặc định khóa test nằm tại `../../../repo/ChatbotLocal/src/ChatbotLocal.Web/App_Data/gaoapp-test-key.txt`; bố cục này dùng checkout `repo/ChatbotLocal` ở gốc ổ đĩa chứa source hiện tại.

Các script trong `scripts/assistant` nhận `-ChatbotDirectory` hoặc biến môi trường `CHATBOTLOCAL_DIRECTORY`. Nếu không chỉ định, chúng ưu tiên checkout `ChatbotLocal` cùng cấp thư mục repository GaoApp, rồi đến `repo/ChatbotLocal` trên cùng ổ đĩa. Đường dẫn tương đối trong tham số này được tính từ repository GaoApp. Ví dụ khi ChatbotLocal nằm cạnh repository:

```powershell
$env:CHATBOTLOCAL_DIRECTORY = '../ChatbotLocal'
$env:ChatbotTestApi__ApiKeyFilePath = '../../ChatbotLocal/src/ChatbotLocal.Web/App_Data/gaoapp-test-key.txt'
.\scripts\assistant\start-local-chatbot.ps1
```

Biến `ChatbotTestApi__ApiKeyFilePath` cần có trong tiến trình khởi chạy GaoApp; có thể dùng cấu hình môi trường hoặc user secrets tương đương. Nếu giữ ChatbotLocal ở vị trí độc lập, cấu hình các đường dẫn trên theo vị trí đó.

Launcher tính lại đường dẫn runtime thông thường mỗi lần chạy, bao gồm các DB trong `local-ui`, thư mục secret và dữ liệu seed thuộc checkout ChatbotLocal. Đường dẫn tùy chỉnh ngoài bố cục đó được giữ nguyên. Script dừng và báo vị trí nếu DB cũ còn tồn tại nhưng bản tương ứng tại source mới bị thiếu, tránh tạo hội thoại mới thay cho dữ liệu chưa chuyển. Script không sao chép DB, tạo khóa hoặc thay đổi các runtime lịch sử. Kiểm tra đường dẫn bằng `.\scripts\assistant\test-assistant-paths.ps1`; các fixture giả được giữ trong `.artifacts/assistant-paths` để kiểm tra.

## 9. Cải thiện chat và quản lý bật/tắt AI

Theo phản hồi người dùng ngày 12/10/2026:

- Enter gửi câu hỏi; Shift+Enter xuống dòng; Enter khi đang gõ qua bộ nhập/composition không gửi. Khóa gửi được đặt trước bước lấy identity để tránh gửi lặp khi bấm nhanh.
- Báo giá trình bày thành thẻ và bảng theo đơn vị, tên hàng chỉ xuất hiện một lần. Giữ đúng giá 0, tình trạng thiếu/lỗi dữ liệu, nhãn TEST và thông báo giá sỉ dùng giá lẻ. Toàn bộ nội dung dùng DOM/textContent, không thực thi HTML từ trả lời.
- ChatbotLocal hỏi từng thuộc tính còn thiếu từ danh mục đã xác minh. Mặc định không liệt kê toàn bộ sản phẩm. Chỉ hiện danh sách khi người dùng yêu cầu; không chấp nhận số thứ tự từ danh sách chưa từng hiển thị. Danh sách dài trong lịch sử cũ được thu gọn để người dùng tự mở xem.
- Trang quản lý: `/admin/assistant/settings`, yêu cầu quyền `system.setting.update`. Bốn công tắc gồm toàn bộ AI, chat/tra giá, hỗ trợ nhập hàng, gợi ý tên sản phẩm. Áp dụng riêng cửa hàng hiện tại; storeId không lấy từ form. Không tạo menu: người dùng tự thêm đường dẫn này qua Quản lý menu.
- Công tắc được lưu ở `GaoApp.Web/App_Data/Assistant/store-{storeId}.json`, có phiên bản chống ghi đè cấu hình cũ. Có thể cấu hình `Assistant:StoreSettingsDirectory` sang thư mục dữ liệu bền vững của server. Cần giữ thư mục này khi triển khai lại. Đọc cấu hình lỗi sẽ chặn AI; nguồn dữ liệu và BFF đều kiểm tra công tắc hiện tại, kể cả người quản trị và lượt trả lời đang xử lý. `Assistant.Enabled` vẫn là công tắc kết nối ở cấu hình máy chủ.

Xác minh: 11 kiểm thử GaoApp (bao gồm HTTP/SQL, quyền, CSRF, bật lại khi AI đã tắt, cách ly cửa hàng, lưu qua khởi động lại), 11 ca trình duyệt Edge, 7 kiểm thử client và 176 ca ChatbotLocal liên quan đều qua. Bộ hồi quy ChatbotLocal trước sửa cuối có 1095 ca qua, 1 ca phát hiện câu hỏi loại đường dùng chung với nhập hàng; đã sửa và chạy lại toàn bộ 176 ca liên quan thành công. Không kiểm tra micro vật lý thay người dùng, không lưu phiếu nhập hay sản phẩm thực.

Test Gemini thật trên local: `giá sữa tươi th` → hỏi loại đường; `có đường` → hỏi dung tích; `180ml` → giá lẻ Hộp 10.000 đ, Lốc 38.000 đ, Thùng 448.000 đ. Kết quả ở `Logs/assistant-integration-20261012/live-ux-results.json`. GaoApp 7051 và ChatbotLocal 5219 đã được khởi động lại với bản mới; tải lại trang để dùng.

## 10. Sửa hội thoại chọn hàng và báo giá ngay

Theo chuỗi lỗi người dùng cung cấp (Pepsi 300ml/330ml rồi sữa tươi TH có đường 180ml):

- Sản phẩm xác định duy nhất được báo giá ngay khi người dùng có quyền tra giá và chức năng đang bật; không cần gõ thêm từ “giá”. Giữ việc đọc giá mới từ GaoApp, không dùng giá trong lịch sử.
- “Tất cả”, “hết luôn”, “tất cả nhé” trong ngữ cảnh một sản phẩm lấy các đơn vị bán của sản phẩm đó. Enter/mic/bảng báo giá không đổi.
- Sau dung tích không tìm thấy, giữ truy vấn có hãng và làm mới các sản phẩm gợi ý. Câu trả lời chỉ gợi ý tối đa hai quy cách lấy từ nguồn vừa xác minh; không tự đổi 330ml thành 320ml rồi báo giá. Có thể sửa 300ml → 330ml hoặc chai → lon mà không mất hãng.
- Tên khớp hoàn toàn với một sản phẩm được ưu tiên trước các tên dài hơn có thêm vị/loại đường. Danh sách có trang tiếp theo vẫn yêu cầu làm rõ, không tự chọn theo trang đầu.
- Truy vấn tên nước ngọt cụ thể bỏ tiền tố nhóm chung khi tên được lưu chỉ là “Pepsi...”. Câu hỏi nhóm chung hỏi hãng/loại, thay vì yêu cầu người dùng đọc lại nguyên tên trên bao bì.

Kiểm thử: 1.103/1.103 ca offline đạt, gồm báo giá tự động theo quyền/bật tắt, TH → có đường 180ml → giá lốc → tất cả, Pepsi → 300ml → 330ml → chai 390ml, không lẫn số lượng với số thứ tự và không tự báo giá quy cách thay thế. DLL đang chạy tại 5219 khớp bản đã kiểm thử; GaoApp 7051 vẫn chạy.

Đọc danh mục local xác minh có Pepsi lon 320ml, Pepsi chai 390ml và các biến thể khác; không có kết quả cho Pepsi 300ml/330ml. Bài thử chuỗi mới bằng Gemini thật bị duyệt tự động chặn vì dữ liệu danh mục Pepsi thực được xem là chưa nằm trong phạm vi mẫu đã duyệt; đang chờ người dùng cho phép. Chưa coi bài thử Gemini này là đã đạt.

## 11. Chọn sản phẩm từ danh sách gợi ý, không phải gõ nguyên tên

Theo phản hồi tiếp theo: nhập “Pepsi” phải cho nhân viên chọn quy cách có sẵn.

- Hiện từng nhóm 5 sản phẩm dưới dạng nút bấm, có “Xem thêm sản phẩm” để xem nhóm tiếp theo. Với tìm kiếm sữa rộng còn khác loại đường/hãng, vẫn hỏi ngắn để thu hẹp; nhân viên có thể yêu cầu danh sách khi cần.
- Chọn nút gửi số thứ tự cùng tên hiển thị; backend đối chiếu danh sách hiện tại, cửa sổ đang hiển thị, tên và sản phẩm đang hoạt động trước khi lấy giá mới. Chọn sản phẩm và xem thêm được xử lý trực tiếp, không cần gọi model để diễn giải lại lựa chọn.
- Sai dung tích hiện các sản phẩm thực để nhân viên chọn. Kể cả chỉ có một quy cách thay thế, vẫn cần xác nhận chọn; không tự chuyển 330ml sang 390ml rồi báo giá.
- Cho phép query do AI đề xuất sửa một ký tự tên dài, dùng tên rút gọn hoặc gộp những từ tách rời như “pep si”. Các query được chỉnh phải hiện lựa chọn để xác nhận, kể cả chỉ còn một sản phẩm. Dung tích và loại đường người dùng nói vẫn được giữ/chặn nếu model đổi khác.
- Danh sách từ lượt trước chỉ để đọc, không bấm chọn nhầm vào ngữ cảnh mới. Khi đang gửi/chờ kết quả, các nút chọn bị khóa. Mọi tên sản phẩm được hiển thị qua textContent.

Xác minh: 1.110/1.110 ca backend offline, 13/13 ca giao diện Edge, 7/7 ca client đạt. Các ca mới kiểm tra 5 lựa chọn → xem thêm → chọn giá mới, tên không trùng, sản phẩm ngừng hoạt động, danh sách cũ, dung tích sai và tên gõ gần đúng. Bản chạy 5219 khớp DLL đã kiểm thử; giao diện GaoApp nạp JS/CSS mới sau Ctrl+F5. Không gửi thêm danh mục thực tới Gemini khi chưa có xác nhận cho bài thử đã bị duyệt chặn ở mục 10. Kiểm thử tên gần đúng dùng model giả lập; người dùng thử thực tế trên local để nghiệm thu khả năng đề xuất của Gemini.

## 12. Nghiệm thu Gemini thật sau khi người dùng cho phép dữ liệu TH/Pepsi

Ngày 12/10/2026, người dùng xác nhận cho phép gửi tên/quy cách/đơn vị/giá TH và Pepsi local sang Google Gemini, sau đó yêu cầu chạy toàn bộ luồng thật.

Đã chạy:

- 21/21 ca API với GaoApp `http://localhost:7051` và ChatbotLocal `http://127.0.0.1:5219`: callback, kiểm tra token/quyền, danh mục và giá thực, đơn vị/quy đổi, phiếu nhập dạng đề xuất, số lượng 0.75, nhiều sản phẩm phải tách, chỉnh tên thiếu/đủ đơn vị dung tích, hội thoại, UUID idempotency, chat Gemini, chặn hướng dẫn/tồn kho chưa bật.
- 15/15 ca chat/chọn hàng với Gemini thật: TH → có đường 180ml → giá lốc → tất cả → lẻ/sỉ; Pepsi 5 lựa chọn → chọn chai → đọc giá thực; nước ngọt → Pepsi → xem thêm → chọn nhóm sau; 300ml/330ml → chọn quy cách đang có; peps/pep si/pepsy; đọc lại lịch sử.
- Giá trong câu trả lời được đối chiếu trực tiếp với API GaoApp cho đúng sản phẩm, đơn vị và tier, không chỉ kiểm tra chuỗi có từ “giá”. Kết quả: `live-results.json`, `live-gemini-all-results.json` dưới thư mục Logs nghiệm thu.
- Nhật ký AiCalls xác nhận provider Gemini, model `gemini-3.5-flash-lite`, 8 lần gọi model trong lượt chat nghiệm thu cuối đều thành công. Chọn nút, xem thêm, đọc giá và kiểm tra/quy đổi nhập hàng dùng logic backend theo dữ liệu nguồn; không ép model tính giá/quy đổi. Bằng chứng: `real-provider-verification.json`.

Test thật phát hiện sai khác với model giả lập ở tên “pepsy” và trường hợp model mở rộng “peps” thành tên công ty/nhóm sản phẩm. Đã bổ sung hướng dẫn sửa chính tả, một lần sửa query tên khi không có kết quả, ràng buộc giữ dung tích/loại đường, và fallback hẹp về nguyên từ nhân viên nhập khi model mở rộng một tên ngắn ngoài ý người dùng. Query sửa luôn yêu cầu chọn xác nhận trước báo giá. Có thêm kiểm thử chống sửa 330ml thành 390ml, đổi loại đường hoặc đổi hãng; 97 ca chat/dữ liệu/UI liên quan đều đạt sau thay đổi cuối (162 ca liên quan đạt ở bước sửa trước đó).

Bản sửa đã nạp vào ChatbotLocal 5219. Chưa lưu phiếu nhập hoặc sản phẩm thật. Lịch sử TEST Gemini lưu riêng ở DB local-ui. Mic vật lý và việc bấm/lưu phiếu TEST trên thiết bị cửa hàng vẫn do người dùng nghiệm thu; các ca speech ở API dùng văn bản nhận dạng đầu vào. Phê duyệt đã bị chặn ở mục 10 được giải quyết bằng xác nhận trực tiếp này; bài test thật đã hoàn tất.

## 13. Nối Gemini trực tiếp vào nói/tìm hàng trên phiếu nhập

Đã sửa điểm thiếu mà người dùng chỉ ra: trước đây `Nói tên hàng` mới chuyển chữ vào một khối “Hiểu câu nhập hàng” riêng; nút tìm vẫn gọi tra tên cũ. Nay kết quả giọng nói cuối cùng, Enter và nút tìm trong cửa sổ nhận hàng đều gọi API Receiving. Nút trợ lý riêng và mic thứ hai được ẩn để nhân viên dùng ngay thao tác hiện có.

ChatbotLocal gọi Gemini ở stage `receiving_routing` để hiểu/chỉnh tên hàng. Model chỉ đề xuất truy vấn tên; backend giữ số lượng và đơn vị đã nói, kiểm tra hãng/vị/loại đường/dung tích, tìm hàng trong đúng cửa hàng và đọc lại đơn vị/quy đổi hiện tại. Model không chọn ID, không tính giá/quy đổi, không ghi nghiệp vụ. Luồng chỉnh tên sản phẩm mới vẫn theo phần triển khai trước; nâng cấp này tập trung vào tìm hàng trên phiếu nhập.

- “Sữa tươi TH loại có đường”: hiện các sản phẩm TH có đường để chọn. Chọn hàng mở cửa sổ đơn vị/số lượng đang có. Nếu chưa nói số lượng thì để trống ô số lượng để nhân viên nhập.
- “4 lốc TH có đường 180ml”: xác minh đúng sản phẩm, Lốc ×4 và số lượng 4, mở cửa sổ xác nhận với tương đương 16 Hộp. Chỉ thêm dòng khi nhân viên xác nhận bằng chức năng hiện có.
- Sửa chữ, Nói lại hoặc đóng cửa sổ hủy hiệu lực kết quả cũ; Enter liên tiếp giữ một lượt đang chờ. Giá trị quy đổi thay đổi sau khi AI trả lời bị từ chối trước khi mở cửa sổ số lượng.
- AI tắt thì tìm thường vẫn hoạt động. Khi Gemini mất kết nối có nút “Tìm theo cách thường” và chức năng kiểm tra/gửi lại cùng lượt. Quét mã vẫn theo tra mã chính xác.

Kết quả: 1.125/1.125 ca backend offline, 11/11 ca cửa sổ nhập hàng dùng các script GaoApp thật với API mô phỏng, 13/13 ca giao diện chatbot/form và 7/7 ca client đạt. API với Gemini thật và danh mục GaoApp local đạt 9/9 ca: câu trong ảnh, tên đủ 180ml nhưng thiếu số lượng, 4 lốc, số lượng/dung tích đọc bằng chữ, pepsy, pep si, Pepsi 330ml không tự đổi quy cách, 0.75 lốc và yêu cầu nhiều mặt hàng phải tách. AiCalls xác nhận 8/8 lượt tên hàng gọi Gemini thật thành công; yêu cầu nhiều mặt hàng được chặn theo kiểm tra đầu vào.

Bằng chứng: `Logs/assistant-integration-20261012/receiving-ai-results/` gồm `receiving-backend-regression.trx`, `receiving-browser-results.json`, `live-gemini-receiving-results.json`, `real-receiving-provider.json` và ảnh cửa sổ lựa chọn/xác nhận. Bản đã nạp tại ChatbotLocal 5219; GaoApp 7051 phục vụ các script mới. Ctrl+F5 trước khi thử trên phiếu TEST. Chưa lưu phiếu hoặc sản phẩm thật; mic vật lý vẫn cần người dùng nghiệm thu trên thiết bị.

## 14. Thống nhất Gemini cho ba luồng và sửa đổi hãng trong hội thoại

Theo yêu cầu ngày 12/10/2026, hội thoại tìm/tra giá, tìm hàng trên phiếu nhập và đề xuất tên sản phẩm mới đều dùng Gemini để hiểu văn bản tự nhiên. Các lượt chat bổ sung thuộc tính, đổi hãng, hỏi giá lốc và “tất cả” cũng đi qua model; backend kiểm tra kết quả theo ngữ cảnh đúng cửa hàng và dữ liệu hiện tại. Nút chọn sản phẩm/xem thêm là thao tác lựa chọn đã xác minh, không dùng model tự đoán ID. Các kiểm tra giá, quyền, mã hàng, đơn vị, số lượng và quy đổi tiếp tục thực hiện ở backend.

Người dùng xác nhận “Sữa tươi TH nguyên chất hộp 180ml” trong danh mục là loại cửa hàng gọi “TH không đường”. GaoApp bổ sung alias thuộc tính cho tên sữa tươi TH nguyên chất dạng hộp/bịch với dung tích rõ ràng; không phân loại các dòng chocolate, hương vị hoặc thương hiệu khác chỉ vì có chữ “nguyên chất”. Tìm theo loại đường dùng thuộc tính đã xác minh thay vì bắt tên chứa đúng chữ “không đường”. Tên lưu, giá và dữ liệu sản phẩm vận hành không bị sửa.

Chatbot giữ hãng, dung tích và loại đường của sản phẩm vừa báo giá dưới dạng ngữ cảnh có thời hạn. “Có TH thì sao”, “còn Vinamilk”, “tôi hỏi sữa tươi TH không đường” thay hãng và giữ các thuộc tính đang nói; nêu dung tích/loại đường mới thì thay các thuộc tính đó. Đổi sản phẩm đọc lại đơn vị/giá; không mang ID quy đổi hoặc giá của hãng cũ sang hãng mới.

API normalize-name có stage `name_normalization` riêng, nhận văn bản gốc và schema chỉ chứa `proposedName`. Gemini sửa dấu, chính tả, viết hoa hãng và thứ tự tên. Backend giữ các số đo, số lượng, bao bì, hãng, loại đường và các từ người dùng cung cấp; đề xuất thêm/đổi thuộc tính bị chuyển sang yêu cầu kiểm tra, không cho bấm dùng tên. Mic ở form tên mới tự yêu cầu đề xuất sau kết quả nhận dạng cuối; nhân viên bấm “Dùng tên đề xuất” để điền, rồi xác nhận lưu theo chức năng hiện có. Sửa input/đóng modal vẫn bỏ kết quả cũ.

Nghiệm thu:

- 1.131 ca hồi quy backend ChatbotLocal đạt; bổ sung 8 ca đổi hãng/ngữ cảnh bằng parser và HTTP đạt, bao gồm trường hợp model đề xuất general nhưng backend vẫn giữ ngữ cảnh hàng hợp lệ.
- 19 ca tích hợp GaoApp đạt, gồm tra alias, đúng thuộc tính, giữ giá/quy đổi và kiểm tra quyền/CSRF.
- 15 ca giao diện chatbot/form, 11 ca cửa sổ nhập hàng và 7 ca client đạt. Ca mic tên mới xác nhận chỉ gửi một yêu cầu sau kết quả cuối và chưa điền tên trước khi nhân viên bấm dùng.
- 12/12 lượt với Gemini thật và nguồn GaoApp local đạt: Vinamilk → 180ml không đường → TH bằng cả ba câu người dùng gửi → đổi lại Vinamilk → TH ít đường 110ml → giá lốc → tất cả; đề xuất tên trong ảnh Kinh Đô 500g; giữ “180” khi chưa có ml; nhập TH không đường 4 lốc = 16 hộp.
- AiCalls xác nhận cả 12 lượt trên gọi Gemini thật thành công, đủ ba stage `routing`, `receiving_routing`, `name_normalization`. Kết quả tên thật: “bánh ngot kinh do 500g loại hộp” → “Bánh ngọt Kinh Đô hộp 500g”.

Bằng chứng ở `Logs/assistant-integration-20261012/natural-ai-results/`: `natural-backend-regression.trx`, `brand-continuation.trx`, `natural-gaoapp.trx`, `live-natural-language-results.json`, `real-natural-provider.json`. Bản đã nạp vào GaoApp 7051 và ChatbotLocal 5219, giữ DataProtection/phiên đăng nhập và tắt migrations lúc khởi động. Chỉ tạo lịch sử/đề xuất test, không lưu phiếu hay sản phẩm thật. Ctrl+F5 rồi dùng hội thoại mới hoặc form trên phiếu TEST để thử mic vật lý.

## 15. Tên có hương vị/số chưa rõ và thống nhất nói hoặc Enter

Sửa lỗi câu “sưa tuoi của th 180 vị bưởi”: normalize-name trước đây dùng parser nhận hàng nên “180 vị” có thể bị đọc thành số lượng 180 và đơn vị Vỉ. Khi Gemini sắp tên thành “Sữa tươi TH vị bưởi 180”, guard thấy số 180 xuất hiện lại và loại cả đề xuất, trả về tên cũ chưa sửa dấu.

Đã tách `AssistantNameText` khỏi parser số lượng. Tên giữ số, hãng và hương vị qua việc sửa dấu/thứ tự; “vị” và “hương” là cách diễn đạt tương đương, còn “vỉ” có dấu được phân biệt là bao bì. Nếu model thêm ml/g/kg cho một số chưa có đơn vị, backend bỏ riêng phần đơn vị chưa xác nhận, giữ phần tên AI đã sửa. Đổi bưởi sang dâu, TH sang hãng khác hoặc 180 sang 200 vẫn bị từ chối. Yêu cầu bổ sung dung tích/loại đường không làm mất tên đúng; khi chỉ thiếu thông tin, nhân viên được bấm dùng phần tên đã sửa, rồi kiểm tra trước khi lưu. Tên có hương vị không bị hỏi thêm loại đường một cách máy móc.

Ở ô tên sản phẩm mới, nói xong tự điền ô và gọi Gemini; gõ tay rồi Enter gọi cùng API đề xuất tên. IME đang ghép chữ không gửi. Ctrl+Enter vẫn dành cho lưu theo chức năng phiếu hiện có. Ở cửa sổ tìm hàng nhập, nói xong hoặc gõ Enter vẫn gọi Receiving; micro bị chặn không chặn cách gõ Enter.

Kiểm thử: 1.150 ca backend, 17 ca giao diện chatbot/tên và 12 ca cửa sổ nhập hàng đạt; có ca bảo toàn bưởi và số 180, đơn vị Vỉ riêng, đổi hương vị/hãng/số bị chặn, Enter/IME/Ctrl+Enter, micro từ chối và kết quả đến muộn. Thử Gemini thật ở nguồn/quyền TEST riêng (GaoApp do Visual Studio quản lý đang dừng) đạt 3/3, AiCalls xác minh cả ba stage `name_normalization` dùng Gemini thành công:

- “sưa tuoi của th 180 vị bưởi” → “Sữa tươi TH vị bưởi 180”, hỏi riêng đơn vị.
- “sưa tuoi của th 180ml vị bưởi” → “Sữa tươi TH vị bưởi 180ml”.
- “bánh ngot kinh do 500g loại hộp” → “Bánh ngọt Kinh Đô hộp 500g”.

Bằng chứng ở `Logs/assistant-integration-20261012/name-flavor-results/`, gồm `name-flavor-backend.trx` và `gemini-isolated/real-name-flavor-results.json`, `gemini-isolated/provider-verification.json`. Server TEST 5307 đã dừng; ChatbotLocal 5219 đã nạp bản mới. Không chạy thêm GaoApp trên 7051 để tránh xung đột Visual Studio; người dùng F5 GaoApp và Ctrl+F5 trang để thử. Chưa dùng mic vật lý hay lưu sản phẩm/phiếu thật thay người dùng.

## 16. Ảnh lựa chọn AI, tra giá bằng barcode và tải danh sách phiếu

Theo yêu cầu bổ sung ngày 12/10/2026, danh sách sản phẩm AI đề nghị trong cửa sổ nhận hàng có ảnh, tên và đơn vị nhập. Ảnh được đọc qua API catalog GaoApp theo phiếu/variant đã chọn; không dùng URL do model cung cấp. Giới hạn ba yêu cầu ảnh đồng thời, hủy khi đổi đề xuất/đóng cửa sổ. Hàng chưa có ảnh hoặc ảnh tải lỗi vẫn có tên và nút chọn. Luồng chọn hàng vẫn mở popup kiểm tra đơn vị/số lượng, không tự lưu dòng.

Chat có nút Quét mã dùng bộ đọc barcode camera đã có của GaoApp. Nhận mã đầu tiên thì dừng camera và gửi một lượt; đóng chat, đổi hội thoại hoặc gửi tay đều dừng camera. Máy quét cầm tay có thể nhập mã vào ô chat rồi Enter. Mã số 8–64 chữ số được nhận trực tiếp, giữ số 0 đầu; mã chữ hoặc mã ngắn có thể gửi bằng `Mã vạch: ABC-001` (camera tự thêm nhãn này khi cần).

Lượt barcode bỏ qua Gemini hoàn toàn. ChatbotLocal gọi catalog search với `query = barcode:<mã>`; GaoApp đọc đúng mã active trong ProductVariantUnitBarcode và kiểm tra trạng thái sản phẩm/quy đổi/đơn vị theo cửa hàng. Không tìm gần đúng tên/SKU, không dùng lịch sử barcode đã ngừng. Một sản phẩm hợp lệ thì đọc giá mới cho các đơn vị bán, trả giá lẻ và sỉ theo quyền; thiếu giá sỉ vẫn theo chính sách fallback hiện có. Mã chưa có hoặc khớp nhiều sản phẩm trả thông báo kiểm tra, không tự chọn hoặc sinh giá. Lịch sử và ngữ cảnh sản phẩm được giữ để hỏi tiếp; các câu tự nhiên tiếp theo vẫn qua Gemini.

Lỗi tải `/admin/warehouse-receiving/receipts`: stack trace thể hiện truy vấn EF bị hủy sau khoảng 16 giây, chưa đủ để khẳng định nguyên nhân hủy là SQL timeout hay kết nối trình duyệt. Trang trước đây đọc toàn bộ lịch sử phiếu cùng hóa đơn/audit rồi lọc ba trạng thái ở controller. Nay dùng GetReceivingReceiptListAsync, lọc Draft/PendingApproval/Rejected tại SQL, chỉ đọc thông tin cần hiển thị và tổng dòng/loại hàng trong hai truy vấn. GetReceiptListAsync đầy đủ vẫn phục vụ màn hình quản lý/đối soát. Hủy đúng do RequestAborted trả trạng thái 499, không biến thành 500; hủy bất thường hoặc lỗi DB vẫn được báo lỗi.

Giao diện tải phiếu chỉ giữ một yêu cầu đang chờ, giữ danh sách trước đó khi tải lại lỗi, có trạng thái/Thử lại và hủy đọc quá 30 giây. Không nhận HTML/trang đăng nhập làm dữ liệu phiếu. Không thay quyền hoặc tạo menu.

Nghiệm thu: build GaoApp đạt; 43 ca GaoApp về danh sách phiếu/tenant/cancellation/quyền/BFF/barcode đạt (gồm barcode chạy trên SQL Server, cùng mã ở hai cửa hàng vẫn đọc đúng sản phẩm/giá); 1.198 ca hồi quy ChatbotLocal đạt, gồm HTTP xác nhận barcode không gọi model, không tự báo giá mã thiếu/trùng và chặn khi mất quyền. Có 22 ca UI chat/form, 15 ca cửa sổ nhập hàng với ảnh và 6 ca tải danh sách phiếu đạt bằng API/camera/giọng nói mô phỏng. Với 1.001 phiếu, 20.000 dòng, 10.000 audit và 2.000 liên kết hóa đơn ở DB test, danh sách nhận hàng đọc 501 phiếu active bằng hai truy vấn khoảng 66 ms. Thời gian này không đại diện DB vận hành có thể đang bị khóa hoặc mất kết nối.

Bằng chứng: `Logs/receiving-list-20261012/`, `Logs/assistant-integration-20261012/barcode-results/`, `receiving-ai-results/` và `browser-results.json`. ChatbotLocal local 5219 đã nạp đúng DLL qua test và health/live trả 200. GaoApp local do Visual Studio quản lý: dừng/chạy lại bằng F5 rồi Ctrl+F5 trang để nạp C# và script mới. Chưa nghiệm thu camera/micro vật lý trên thiết bị của nhân viên; các test dùng DB/nguồn mô phỏng riêng, không ghi phiếu/sản phẩm thật.
