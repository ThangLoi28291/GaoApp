# Nhận hàng từ mã mới và khai báo quy đổi

## Luồng đã thống nhất

- Dùng chung popup trên trang nhân viên nhận hàng và trang quản lý phiếu nhập kho.
- Quét mã chưa biết rồi nhấn Enter mở popup với hai tab **Sản phẩm có sẵn** / **Sản phẩm mới**. F4 mở tab sản phẩm mới; khi tìm không có kết quả cũng có đường vào popup.
- Sản phẩm có sẵn: chọn đúng đơn vị của mã vừa quét hoặc **Thêm quy cách**. Ô đơn vị có autocomplete, tìm được khi gõ không dấu; chọn đơn vị có sẵn hoặc gõ tên mới ngay trong cùng ô. Nếu chọn đơn vị đã thuộc sản phẩm thì dùng quy đổi hiện hành và nhận hàng trực tiếp.
- Sản phẩm mới: khai báo tên, đơn vị gốc, đơn vị nhận, quy đổi và số lượng. Danh mục có thể bổ sung lúc quản lý duyệt; phiếu cần nhà cung cấp trước khi tạo sản phẩm.
- Nhân viên bắt buộc khai báo quy đổi, ví dụ 1 Thùng = 24 Hộp; popup hiển thị tổng số lượng gốc tương ứng. Không có trường hợp lưu quy đổi chưa biết. Số lượng và quy đổi phải dương, tối đa ba số lẻ và nằm trong giới hạn decimal(18,3).
- Nhấn **Lưu và nhận hàng** đưa sản phẩm/quy cách mới ngay vào bảng hàng nhập, có ô sửa số lượng và nút xóa. Tổng dòng và tổng số lượng bao gồm các dòng này. Nhân viên tiếp tục quét/nhận hàng ngay; quản lý đối chiếu quy đổi ở **Cần xử lý** khi duyệt phiếu. Chưa tạo danh mục khi chỉ lưu khai báo. Quản lý đủ quyền có thể chọn duyệt và tạo ngay trong phiếu nháp.
- Mã mới của một đơn vị đã có: lưu đề xuất barcode và dòng nhận hàng trong cùng giao dịch; giữ mã nội bộ. Đề xuất chỉ thành mã dùng chung sau khi quản lý duyệt.
- Quét lại một khai báo chưa duyệt sẽ điền lại quy cách đã khai báo để cộng số lượng. Không cho cùng mã mang quy đổi khác trong một phiếu.
- Duyệt khai báo tạo/liên kết sản phẩm, đơn vị, barcode và dòng phiếu; vẫn phải duyệt phiếu để ghi nhận tồn kho. Sản phẩm tạo nhanh chưa được mở bán cho đến khi hoàn thiện danh mục.
- Các khối và nút lớn trước đây được gộp vào mục thu gọn; mục này ẩn khi không có việc cần xử lý. Giữ khả năng xử lý bản ghi cũ.
- Số lượng trên cả hai trang và các popup nhập hàng tăng/giảm theo bước **1**, áp dụng cho hàng có sẵn, quy cách mới và sản phẩm mới. Nhấp dòng để chọn; dùng phím **+ / −** (kể cả bàn phím số) hoặc hai nút trên bảng để tự lưu số lượng. Giữ dòng được chọn sau khi tải lại, lưu lần lượt khi bấm liên tiếp, không giảm xuống dưới 1 bằng các nút này. Phím tắt không can thiệp ô tìm kiếm, ghi chú hoặc popup khác.
- Bảng của trang quản lý mang theo phiên bản phiếu tương ứng; sau sửa số lượng, phiên bản được cập nhật cùng bảng để có thể thao tác tiếp trên hàng mới. Nếu lưu lỗi, dừng các lần tăng/giảm đang đợi và yêu cầu lưu lại trước khi gửi duyệt.

## API

Đường dẫn gốc: `/admin/api/stock-documents/{documentId}/intake`.

| Phương thức | Đường dẫn thêm | Nội dung |
| --- | --- | --- |
| GET | — | Trạng thái, quyền thao tác, danh mục và đơn vị để chọn |
| POST | — | Ghi nhận sản phẩm mới hoặc quy cách mới |
| POST | `/known` | Ghi nhận đơn vị có sẵn, đồng thời đề xuất barcode nếu cần |
| POST | `/{itemId}/review` | Quản lý duyệt hoặc loại khai báo |
| POST | `/{itemId}/quantity` | Sửa số lượng hàng đã nhận theo quy cách khai báo; không cần chờ duyệt danh mục |
| POST | `/{itemId}/remove` | Người sửa phiếu loại khai báo khi phiếu còn cho phép sửa |

Tất cả yêu cầu cần tài khoản đăng nhập và đúng cửa hàng. Yêu cầu ghi cần header `RequestVerificationToken`, `commandId` UUID, `documentRowVersion`; xử lý một khai báo cần thêm `itemRowVersion`. Phiếu từ đơn mua giữ kiểm tra người nhận và `leaseToken` hiện hành. Quyền phiếu theo nguồn: `Inventory.StockDocument.*` hoặc `Purchase.Receipt.*`. Tạo danh mục cần các quyền Product/Unit/Barcode tương ứng, ngoài quyền duyệt phiếu.

Ví dụ POST khai báo một quy cách mới của sản phẩm có sẵn (các ID/phiên bản lấy từ API):

```json
{
  "commandId": "<UUID của thao tác>",
  "documentRowVersion": "<Base64 từ trạng thái phiếu>",
  "productVariantId": 123,
  "name": "Tên sản phẩm",
  "barcode": "8931234567890",
  "unitName": "Thùng",
  "factor": 24,
  "quantity": 2,
  "note": "Quy cách in trên bao bì",
  "approveNow": false
}
```

Nếu chọn đơn vị có sẵn trong danh mục chung, gửi `unitId`. Khi tạo sản phẩm mới, bỏ `productVariantId` và gửi `baseUnitId` hoặc `baseUnitName`. API lấy đơn vị gốc thực tế cho sản phẩm đã có, không chấp nhận ghi đè quy đổi hiện hành.

Gửi lại cùng command và cùng nội dung không cộng hai lần. Xung đột phiên bản/quy cách trả lỗi để tải lại, không ghi đè thay đổi của người khác. Sau khi duyệt, giao diện tải lại danh sách hàng và giữ giá/chi phí người quản lý đang nhập.

Yêu cầu sửa số lượng gồm `quantity`, `commandId`, `documentRowVersion`, `itemRowVersion`, và `leaseToken` nếu là phiếu từ đơn mua. Giữ nguyên sản phẩm/quy đổi đã khai báo; chỉ cho sửa khi phiếu còn được nhập. Chặn gửi duyệt trong lúc số lượng đang lưu hoặc còn giá trị chưa lưu thành công. Khi duyệt khai báo, dòng danh mục thay thế dòng khai báo trên giao diện, không đếm hai lần.

## Database và chuyển máy chủ

Migration: `20260911053655_AddReceiptIntakePacking`.

Thêm năm cột nullable trên `StockDocumentProvisionalItems`: `ProposedProductVariantId`, `ProposedBaseUnitId`, `ProposedBaseUnitName`, `ProposedFactor`, `ProposedCategoryId`; thêm khóa ngoại và chỉ mục cho sản phẩm/đơn vị gốc. Không sửa số lượng, tồn kho hay dữ liệu phiếu cũ.

Đã áp dụng migration này vào database phát triển `GaoAppDb` ngày 11/09/2026 và kiểm tra lại đủ năm cột cùng lịch sử migration. Kết nối máy phát triển lưu bằng User Secrets của `GaoApp.Web`, không đưa tên SQL Server vào mã nguồn.

Trên Windows Server, cấu hình `ConnectionStrings:DefaultConnection` theo máy chủ thực tế (hoặc biến môi trường `ConnectionStrings__DefaultConnection`) và áp dụng migration qua quy trình triển khai hiện có. User Secrets của máy phát triển không được mang sang máy chủ. Script chỉ nâng từ `20260910040620_AddProductLabelPrinting` lên migration này được tạo tại `.artifacts/receipt-intake-upgrade.sql` để đối chiếu.

Kiểm tra cấu trúc database còn phát hiện hai sai khác metadata trong migration đã có: kiểu SQL suy ra của bảng nhật ký POS chưa được khai báo rõ, và dấu ngoặc của bộ lọc chỉ mục in tem khác cách SQL Server trả về. Đã làm rõ kiểu/dấu ngoặc tương đương trong nguồn migration để kiểm tra schema nhận diện đúng; không đổi dữ liệu hay ý nghĩa của các cấu trúc này. Danh sách migration mong đợi trong kiểm thử cũng được cập nhật.

## Kiểm chứng

- Kiểm thử SQL/HTTP: khai báo, quy đổi bắt buộc và giới hạn, gửi lại command, barcode và số lượng cùng giao dịch, phân quyền/cửa hàng/CSRF, duyệt ngay, loại khai báo, duyệt hàng mới; không ghi nhận tồn kho trước duyệt phiếu.
- Kiểm thử hồi quy nghiệp vụ barcode, hàng chưa có danh mục, hợp đồng API, migration/snapshot và bộ kiểm tra schema: **54/54 đạt, không bỏ qua**, kết quả cuối trong `TestResults/receipt-intake/receipt-intake-verified.trx`.
- Chrome thực: nhân viên quét mã mới, hai tab, chọn đơn vị, thêm Kiện quy đổi 48 Hộp, nhập hàng mới, quét lại khai báo, màn hình 390 px, gửi phiếu và quản lý duyệt; giữ giá đang nhập sau duyệt khai báo.
- Chrome hồi quy: quét lại barcode chờ duyệt, cộng số lượng, duyệt barcode dùng chung, đồng bộ rowversion khi thêm/sửa/xóa, chờ tự lưu trước gửi và chặn gửi khi lưu lỗi.
- Ảnh giao diện: `TestResults/receipt-intake/browser/`; log trình duyệt: `Logs/receipt-intake-browser-final.log`, `Logs/receipt-barcode-browser-regression2.log`.
- Đợt điều chỉnh autocomplete và nhận hàng ngay: **18/18 kiểm thử SQL/HTTP đạt** (`receipt-intake-autocomplete.trx`); Chrome kiểm tra tìm không dấu, nhập tên đơn vị mới, sửa số lượng ngay trên bảng, tổng phiếu và duyệt sau. Ảnh `new-unit-received-immediately.png`; log `Logs/receipt-intake-autocomplete-browser-final.log`. Không có migration mới trong đợt điều chỉnh này.
- Đợt điều chỉnh bước số lượng và chọn dòng: build Web thành công; bài kiểm tra Chrome mở cả hai màn hình, thử mũi tên, phím +/−, nút bấm, bấm liên tiếp trên hàng có sẵn/quy cách mới/sản phẩm mới, giữ số lượng khi gõ dấu trong ô tìm kiếm, gửi phiếu và duyệt. Log `Logs/receipt-quantity-controls-verified.log`; ảnh `quantity-step-warehouse.png`, `quantity-step-management.png`. Phiên bản phiếu được lấy từ chính partial bảng quản lý vừa hiển thị, giúp tránh lỗi phiên bản cũ khi chuyển từ sửa hàng có sẵn sang hàng mới.

Build/test được thực hiện trong `.artifacts/employee-account` để tránh chiếm DLL đầu ra Visual Studio. Các thao tác kiểm thử dùng SQL database tạm; database phát triển chỉ được cập nhật cấu trúc nêu trên.
