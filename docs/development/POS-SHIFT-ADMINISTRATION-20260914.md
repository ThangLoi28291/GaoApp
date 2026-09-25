# Quản lý phiếu nhận ca và nhận tiền cuối ca

## Sử dụng

- Admin mở `/admin/pos-shift/handover-slips`, chọn **Tạo phiếu**, chọn kho và quầy nhận ca, nhập tiền theo mệnh giá rồi tạo/in mã vạch. Không chọn trước nhân viên; người đang đăng nhập tại quầy được ghi nhận khi xác nhận nhận ca.
- Phiếu **Mới tạo** hoặc **Đã in** có nút **Sửa** để đổi kho, quầy, tiền theo mệnh giá và ghi chú. Lưu thay đổi giữ nguyên mã phiếu nhưng đổi mã vạch, đưa về trạng thái cần in lại. Phải sử dụng bản in mới; mã vạch cũ và form nhận ca đã nạp từ mã cũ đều bị từ chối.
- Nhân viên đăng nhập tại quầy được cấp, quét mã trong form nhận ca và xác nhận. Kho và tiền đầu ca lấy từ phiếu. Phiếu chỉ được nhận một lần; phiếu đã nhận hoặc đã hủy không được sửa. Phiếu cũ có người được chỉ định cũng theo quy tắc mới: ghi nhận người thực sự nhận ca, không giới hạn theo người đã chọn trước đó. Phiếu cũ chưa chỉ định quầy vẫn được giữ tương thích.
- Nhân viên chốt ca, ghi tiền thực đếm như trước. Đóng ca không đồng nghĩa admin đã nhận tiền.
- Admin mở `/admin/pos-shift/manager-dashboard`, chọn **Chờ admin nhận tiền**, mở **Nhận tiền / Duyệt**, nhập tiền thực nhận và ghi chú rồi xác nhận. Cần lý do nếu tiền thực nhận khác tiền nhân viên khai hoặc khác tiền dự kiến trong sổ quỹ.
- Chi tiết và Excel lưu số tiền admin thực nhận, người xác nhận, thời điểm và ghi chú. Số tiền nhân viên khai và phiếu chốt ca ban đầu giữ nguyên. Ca đã xác nhận không được ghi đè; gửi lại cùng dữ liệu chỉ trả thành công.

## Phân quyền

Chỉ thành viên đang hoạt động thuộc vai trò hệ thống `ADMIN` của cửa hàng hiện tại được quản lý phiếu, xem dashboard quản lý và xác nhận nhận tiền. Cấp riêng quyền mở ca hoặc toàn bộ quyền POS cho một vai trò nhân viên không vượt qua giới hạn này. Kiểm tra thành viên/vai trò được đọc lại từ cơ sở dữ liệu.

Nhân viên giữ quyền quét phiếu và mở/chốt ca; máy chủ kiểm tra quầy, cửa hàng, mã vạch hiện tại và trạng thái phiếu. Người nhận lấy từ phiên đăng nhập. Việc mở ca và đánh dấu phiếu đã dùng được lưu trong cùng một lần SaveChanges, có kiểm soát cạnh tranh bằng rowversion. Sửa phiếu cũng kiểm tra rowversion từ form: hai người cùng sửa, hoặc sửa đúng lúc nhận ca, chỉ một thao tác thành công; thao tác còn lại trả 409 và không lưu dở dang. Các dòng mệnh giá cũ được đánh dấu xóa mềm khi thay thế.

## Cập nhật dữ liệu và publish

Migration: `20260914154923_AddPOSShiftCashReceipt`. Thêm bốn cột nullable và một chỉ mục vào `POSShifts`; ca cũ không được tự đánh dấu đã nhận tiền.

Chạy migration bằng quy trình GaoApp.Migrator của dự án trước khi chạy bản Web mới. Nếu host cập nhật bằng SQL, dùng `docs/pos-shift-administration-upgrade.sql` sau khi đã có các migration trước đó, bao gồm `20260914073514_AddOrderRewardEligibilitySnapshots`. File SQL chỉ thực hiện migration nhận tiền này và có kiểm tra lịch sử để không chạy lại.

Các thay đổi đã nằm trong source, bao gồm migration, giao diện, JavaScript và CSS. Publish phải kèm các tệp tĩnh mới; sau khi cập nhật Web, tải lại trình duyệt để lấy phiên bản JavaScript mới.

Riêng thay đổi sửa phiếu và ghi nhận nhân viên lúc nhận ca sử dụng các cột đã có, không cần migration mới.

## Kiểm tra

- `PosShiftAdministrationSqlServerTests`: ứng dụng HTTP thật và SQL thử nghiệm riêng, kiểm tra quyền admin, cách ly cửa hàng, quầy nhận, ghi nhận người thực nhận, dùng phiếu một lần, số tiền nguồn, xác nhận và chống ghi đè.
- `PosShiftHandoverEditSqlServerTests`: sửa phiếu mới/đã in, đổi mã vạch, từ chối bản cũ, khóa sửa phiếu đã nhận/đã hủy và tranh chấp sửa/nhận ca.
- `GaoApp.Tests.Browser/pos-shift-admin.browser.cjs`: tạo/in/sửa/in lại phiếu, từ chối form đã quét mã cũ, nhân viên quét mã mới để nhận, chốt ca, admin nhận tiền, tải lại dữ liệu, form desktop/điện thoại.
- Lệnh probe trình duyệt: `PosOffline.Browser.dll --shift-admin`.
