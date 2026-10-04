# Chỉnh giá bán tại phiếu nhập

Mở phiếu **Chờ duyệt** hoặc **Đã duyệt**, tab **Hàng hóa**, chọn
**Giá bán & lợi nhuận** dưới giá nhập của sản phẩm.

- Cần quyền duyệt phiếu tương ứng (phiếu trực tiếp / phiếu PO) và `Catalog.Product.Update`.
- Mặc định lợi nhuận 10% trên giá bán: `giá vốn / (1 - tỷ lệ)`.
  Có thể đổi sang cộng trên giá vốn: `giá vốn * (1 + tỷ lệ)`.
- Giá vốn dự kiến quy về đơn vị gốc từ giá nhập đang nhập, cộng VAT/cước được
  chọn đưa vào vốn; sau đó nhân hệ số của từng đơn vị bán. Đây là phép tính
  tham khảo, chưa bao gồm chiết khấu bán hoặc chi phí vận hành.
- Tỷ lệ và giá đề xuất có thể chỉnh từng đơn vị. Giá sửa tay được giữ khi thay
  đổi tham số; bấm đề xuất để dùng lại kết quả tính tự động. Các tỷ lệ được ghi
  cùng lịch sử lần lưu; mở bảng giá mới bắt đầu lại với mặc định 10%.
- Chỉnh giá lẻ và giá sỉ cho từng đơn vị. Giá sỉ để trống dùng giá lẻ;
  có mục tiêu và gợi ý riêng cho bán sỉ, mặc định 10%. Chỉ các đơn vị được
  tích và có giá mới được lưu. Lưu cập nhật giá bán ngay vào danh mục,
  không chờ duyệt phiếu và không sửa giá nhập,
  tồn kho hoặc công nợ. Giá mới được dùng khi POS lấy lại giá; không thay giá
  các dòng đã có trong giỏ. POS ngoại tuyến cần đồng bộ lại danh mục.
- Giá Product (`BasePrice`) đồng bộ với giá lẻ của đơn vị được chọn chia hệ
  số quy đổi, làm tròn 2 chữ số. Mặc định chọn đơn vị gốc nếu có, nếu không
  chọn đơn vị có hệ số nhỏ nhất. UI hiển thị giá Product trước/sau. Đây là
  giá mặc định chung của Product; biến thể không có giá riêng sẽ kế thừa.
- Ngoài hộp sửa giá có mục **Đánh giá giá bán** và nhãn từng dòng: chưa đánh
  giá, đã cập nhật, cần xem lại. Vàng là cần xem lại, đỏ là có giá dưới vốn,
  xanh là đã đánh giá. **Đã kiểm tra, giữ giá** ghi nhận đánh giá mà không
  sửa giá lẻ, sỉ hay Product. Nếu vẫn bán dưới vốn thì vẫn hiện cảnh báo đỏ.
- Đánh giá lưu riêng theo phiếu/dòng trong `AuditLogs` (`ReceiptPriceReview`),
  kèm người, thời gian, giá vốn tham khảo và phiên bản danh mục. Giá vốn hoặc
  danh mục thay đổi làm đánh giá cũ cần xem lại. Người nhập chỉ có quyền xem
  phiếu vẫn thấy trạng thái nhưng không nhận dữ liệu giá vốn từ API nếu
  chưa được cấp quyền xem giá vốn.
- Sản phẩm không có đơn vị quy đổi hoạt động dùng giá bán của biến thể với
  đơn vị gốc. Không tự tạo đơn vị hay barcode mới.
- Lịch sử lưu vào `AuditLogs`, gồm người sửa, giá cũ/mới, đơn vị, phiếu nguồn,
  giá vốn tính tham khảo và tỷ lệ. Hiển thị 30 thay đổi gần nhất của sản phẩm
  phát sinh qua chức năng này.

API kiểm tra cửa hàng, dòng phiếu, quyền, CSRF và RowVersion. Toàn bộ đơn vị
được chọn cùng lịch sử được lưu trong một giao dịch SQL. Nếu dữ liệu đã đổi
hoặc mất kết nối khi lưu, đóng và mở lại bảng giá để kiểm tra trước khi thử lại.

## Triển khai

Publish lại **GaoApp.Web**, triển khai theo quy trình Web hiện tại rồi tải lại
trình duyệt bằng Ctrl+F5. Không có migration mới hoặc thay đổi worker cho
chức năng này.

## Kiểm thử

- `node --test GaoApp.Tests/Ui/receipt-selling-prices.test.cjs`
- `dotnet test GaoApp.Tests/GaoApp.Tests.csproj --filter FullyQualifiedName~ReceiptSellingPriceSqlServerTests`
- `dotnet run --project GaoApp.Tests.Browser/PosOffline.Browser.csproj -- --receipt-prices`

SQL và browser fixture chỉ dùng cơ sở dữ liệu LocalDB dùng một lần.
