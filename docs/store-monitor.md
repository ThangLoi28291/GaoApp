# Giám sát trực tiếp tại văn phòng

## Mô hình 3D — 09/10/2026

`/admin/store-monitor` mặc định mở cảnh 3D; nút **Chuyển sang 2D / Mở mô hình 3D** giữ lựa chọn trên trình duyệt. Hai chế độ dùng cùng một kết nối SSE và cùng quyền `admin.storemonitor.view`. Bundle 3D chỉ tải khi chọn 3D. Không cần React hoặc Node trên server.

Khu vực tính tiền chỉ hiển thị **3 quầy đầu tiên** còn hoạt động, sắp theo mã quầy (sau đó theo ID nếu trùng mã), ở cả 3D và 2D.

Bố trí: nhận hàng phía sau bên trái, kho/kiểm kho phía sau giữa, văn phòng duyệt bên phải; hai hàng với 10 kệ ngoài tiệm; in tem bên trái, soạn/giao bên phải; quầy tính tiền phía trước và đường vòng giao hàng bên ngoài. Đã bỏ khu nhận B theo phản hồi của chủ. Mã và số quầy lấy từ terminal thật, tối thiểu ba vị trí mô hình. Camera có toàn cảnh, từng khu, phóng to/thu nhỏ và toàn màn hình.

Mỗi nhân viên online có một nhân vật mang tên. Đăng nhập tạo presence ban đầu; các trang quản trị đã đăng nhập gửi heartbeat `/admin/api/store-activity/online` mỗi 10 giây, có antiforgery. User/store lấy từ principal, client không tự khai người khác. Không cần quyền xem giám sát để xuất hiện trong tiệm. Đăng xuất xóa presence; đóng trình duyệt/mất tín hiệu quá 60 giây hết hạn. Rời trang nghiệp vụ hoặc mở trang khác chưa tương tác không đồng nghĩa đăng xuất. Tab chỉ heartbeat không nâng giờ thao tác cũ vượt lên việc mới.

Cảnh 3D chọn thao tác/lần lưu mới nhất của từng người: bắt đầu việc mới thì chuyển từ vị trí đang đứng, không tạo bản sao và không sửa chứng từ cũ. Chưa làm việc/ngừng thao tác thì đi trong tiệm. Dữ liệu nhóm công việc của 2D vẫn được giữ để tương thích.

Khách đi lựa hàng không hiện tên, số lượng nền khoảng 5–7. Giỏ hiện tại có hàng gọi một khách đến đúng quầy. Chọn khách hàng thật trên đơn mới hiện tên tại quầy. Sản phẩm, đơn vị, lượng, đơn giá, thành tiền, tổng tiền, đã trả, còn phải trả và tiền thối đọc từ giỏ đã lưu theo ca/terminal, không lấy giá giả của demo. Giữ đơn cho khách đi lựa hàng; mở lại gọi đúng nhân vật của đơn trong phiên mô phỏng. Chốt đơn mới nhận túi rồi ra cửa; thanh toán một phần không cho khách ra. Hủy đơn ra cửa không nhận túi. Khách ẩn của đơn giữ được lưu trong bộ nhớ mô phỏng có giới hạn; tải lại/restart dựng trạng thái hiện tại, không phát lại lịch sử.

Gửi `LabelPrinting.Print` thành công: đến máy in, chạy in đủ động tác, lấy tem rồi đến kệ ngoài tiệm dán; không chờ xác nhận tem vật lý. Gửi `StockDocuments.SubmitApproval` thành công: lấy hàng khu nhận rồi mang lên kệ ngoài tiệm; animation không tự duyệt hoặc ghi tồn kho. Kiểm kho đi vào lối giữa các kệ trong kho. Duyệt phiếu ngồi ở máy tính văn phòng. Mọi chuỗi có thể bị công việc mới ưu tiên thay thế.

Giao hàng giữ projection SQL và thời hạn thẻ 60 giây sau hoàn tất. Nhân viên giao được ghép bằng UserId, không ghép bằng tên có thể trùng. Người đi ra cửa bên tiệm, đến xe, lên xe trong 2 giây rồi xe mới chạy. Người và xe cùng một chuyển động; xe chạy liên tục trên đường vòng khoảng 30 giây/lượt trong lúc chuyến chưa xong. Nếu người giao không online, xe dùng nhân vật minh họa người giao đã lưu, không thêm người đó vào danh sách online.

Khi có mốc trở về đã lưu, hoặc tất cả đơn trong chuyến đã có kết quả (`AwaitingReconciliation`, `ReadyToSettle`, `Settled`, `Cancelled`), xe đi theo đường về điểm đỗ; nhân viên xuống xe trong 2 giây rồi đi vào tiệm. Một đơn xong trong chuyến nhiều đơn chưa làm xe về. Công việc mới của nhân viên vẫn ưu tiên và không bị chuyến cũ giành lại. Thời gian chạy chỉ minh họa, không tự xác nhận giao hàng và không phải GPS. Mất thẻ nguồn không tự tạo xác nhận trở về; tải lại không phát lại chuyến đã hoàn tất.

### Sản phẩm vừa quét và phiếu giao hàng

Thông tin sản phẩm quét/thêm gần nhất chỉ hiển thị trong bảng bên phải: tên hàng, số lượng vừa thêm, đơn giá đã lưu và tổng giỏ. Mô hình không có ô sản phẩm nổi che quầy; nhãn tên nhân viên và khách vẫn hiển thị. Nút **Tính tiền** mở chi tiết quầy đang xử lý, gồm ô sản phẩm mới và dòng được đánh dấu trong bảng. Quét lại cùng mặt hàng vẫn cập nhật lần quét mới; không suy ra mặt hàng từ dòng cuối của giỏ. Quét lỗi không tạo thông báo thành công. Đổi/giữ/hủy/chốt giỏ bỏ thông tin của đơn cũ. Khi chưa có sự kiện quét trong phiên, màn hình ghi rõ chưa ghi nhận thay vì đoán mặt hàng.

Thông tin quét là snapshot nhỏ của dòng `OrderLine` trong lần SaveChanges của đúng request; chỉ công bố sau khi action và transaction POS thành công. Không thêm truy vấn từng lần quét hoặc gửi toàn bộ DTO/khách hàng/barcode. ID dòng dùng để ghép với giỏ hiện tại; số lượng thêm lấy từ chênh lệch đã lưu, kể cả khi POS chuẩn hóa số lượng yêu cầu. Nhân viên đã thực sự thao tác POS tiếp tục tại quầy khi còn giỏ Draft có hàng, đến khi giỏ kết thúc hoặc có việc mới hơn; tab POS cũ không lấy lại ưu tiên sau việc mới.

Tổng quan tách **Quầy tính tiền** và **Đơn giao hàng đã lưu**. Phiếu giao hiện người nhận và trạng thái (chờ soạn, đang giao, chờ chốt…), không hiển thị lẫn như khách đang tính tiền. Phiếu giao chưa kết thúc không bị ẩn chỉ vì mã phiếu mang ngày cũ; chủ chọn phiếu để xem mốc cập nhật thật. Các phiếu đã chốt/hủy vẫn áp dụng thời hạn 60 giây hiện hữu.

Mất mạng giữ số liệu cuối, báo dữ liệu cũ và dừng mô phỏng; thu hồi quyền/hết phiên xóa cả chi tiết, tên, khách, nhân vật và chuyến. Dữ liệu text được escape. Projection mới không gửi số điện thoại/địa chỉ/ghi chú, ảnh, công nợ hoặc toàn bộ DTO POS.

### Quyền xem do chủ cửa hàng tự cấp

Dùng quyền **Giám sát hoạt động trực tiếp của cửa hàng** (`admin.storemonitor.view`) trong **Phân quyền nhân viên**. Quyền được cấp theo vai trò: dùng vai trò chỉ chứa tài khoản chủ, hoặc bỏ quyền này khỏi vai trò đang dùng chung với người khác. Page, snapshot và SSE đều kiểm tra quyền; không chỉ ẩn menu. Quyền này yêu cầu cấp trực tiếp, không nhận alias `admin.storemonitor.manage`.

Seed mặc định không tự thêm quyền giám sát vào ADMIN. Các quyền đã cấp trước nâng cấp vẫn giữ để chủ tự điều chỉnh; bản nâng cấp không sửa RolePermissions của cửa hàng đang chạy. Muốn chỉ chủ được mở, cần tự bỏ grant giám sát khỏi những vai trò khác đã có quyền.

### Hiệu năng, kích thước và vận hành

Cảnh gộp vật tĩnh theo material/khu, giảm hình học nhân vật, không render bóng thời gian thực, giới hạn 30 FPS và độ phân giải render. Ẩn tab/chuyển 2D dừng vòng vẽ; tắt hiệu ứng hoặc mất kết nối dừng di chuyển. Bản đóng gói khoảng 1,6 MB chưa nén, nằm cùng static assets trên server.

Bố cục chuẩn 1366×768 phục vụ màn 17 inch; cửa sổ nhỏ hơn co đều mô hình, chữ, nút và cột thông tin, không tràn cả trang. Kích thước lớn hơn tận dụng diện tích thêm. Chi tiết dài cuộn trong cột riêng.

`StoreMonitorReadCache` gom projection giỏ/giao hàng: mỗi cửa hàng dùng chung một lần đọc đã commit trong một giây, tối đa 512 mục cache. Không cache quyền, session hoặc DbContext; mỗi kết nối vẫn được kiểm tra quyền riêng trước khi phát dữ liệu. Presence/cache này thuộc một Web instance như monitor trước đây; nhiều instance cần kho presence/pub-sub chung.

Sau khi build/publish Web mới, khởi động lại ứng dụng và Ctrl+F5 trang giám sát. Bundle đã có trong wwwroot, không cần build npm trên server. Hướng dẫn build scene: [tools/store-monitor-3d/README.md](../tools/store-monitor-3d/README.md). Kiểm tra tích hợp: [STORE-MONITOR-3D-20261009.md](development/STORE-MONITOR-3D-20261009.md).

## Chế độ 2D và lịch sử tích hợp


Trang `/admin/store-monitor` dành cho màn hình admin của **cửa hàng đang đăng nhập**. Quyền riêng `admin.storemonitor.view` cho phép xem hoạt động của cả cửa hàng; cần cấp quyền này cho người quản lý được phép theo dõi nhân viên. Menu hệ thống có mục “Giám sát trực tiếp”. Trang hỗ trợ toàn màn hình, nền sáng/tối, tắt hiệu ứng và tùy chọn giảm chuyển động của hệ điều hành.

Mặc định dùng nền sáng và **Vừa màn hình** trên cửa sổ từ 1100×620 px. Cả sáu khu vực và dòng thời gian nằm trong chiều cao cửa sổ ở zoom 100%. POS nằm trên cùng; bốn khu vực nhập/kho/tem/duyệt ở dưới bên trái, giao hàng bên phải. Ô tóm tắt giữ nhân viên/quầy, thao tác cụ thể, trạng thái và giờ lưu thật; nội dung dài được rút gọn trên tổng quan. Cảnh 2D co theo cả chiều rộng và chiều cao, không thu nhỏ toàn bộ trang hoặc dùng animation để suy ra tiến độ.

Khu vực có nhiều công việc chia trang theo số ô vừa chiều rộng thực tế: tổng số công việc luôn giữ đầy đủ, `1/3` là trang đang xem/tổng số trang. Nút `‹ ›` chuyển trang; **Luân phiên** tự chuyển mỗi 10 giây, giữ nguyên số lượng, trạng thái và mốc giờ nghiệp vụ. Rê chuột/đặt focus vào khu vực giữ trang đó; mở chi tiết, ẩn tab hoặc mất kết nối dừng luân phiên. Hệ điều hành giảm chuyển động mặc định tắt luân phiên; người dùng vẫn có thể chủ động bật. Bấm hoặc Enter/Space trên ô mở chi tiết trực tiếp với mã/tên phiếu, tên hàng, số lượng và các mốc đầy đủ. Escape hoặc “Đóng ×” trở về tổng quan. Chi tiết tiếp tục nhận cập nhật và bị xóa khi thu hồi quyền.

Nút **Vừa màn hình / Xem đầy đủ** đổi sang danh sách đầy đủ có thể cuộn; lựa chọn được lưu trên trình duyệt. Màn hình nhỏ tự dùng dạng cuộn, không che bớt công việc. Trong dạng đầy đủ, POS có tối thiểu ba vị trí quầy, ô trống chỉ là vị trí chờ mã quầy thật; bấm ô nghiệp vụ hiện hữu vẫn lọc dòng thời gian theo nhân viên. Các khu vực hiện hữu không dùng thanh tiến độ; giao hàng chỉ đếm dòng thực sự đã ghi nhận. Quyền/menu và nguồn SSE giữ nguyên.

## Dữ liệu đang kết nối

- Nhập hàng: tạo phiếu và cập nhật mặt hàng/dòng nhập.
- POS: tạo đơn, cập nhật giỏ hàng, thanh toán, chốt/giữ/mở lại/hủy/hoàn đơn.
- Kho: cập nhật kiểm kê, chuyển kho và xác nhận chứng từ.
- Tem: gửi lệnh in, xác nhận/hủy và cập nhật kế hoạch in.
- Duyệt phiếu: gửi duyệt, duyệt/từ chối phiếu nhập, kiểm kê và cập nhật giá nhập.

“Đang thao tác” là tín hiệu tương tác với màn hình nghiệp vụ, không phải bằng chứng nhân viên đang thực hiện công việc vật lý. Không thu nội dung gõ, ảnh màn hình hay vị trí. Mở trang mà không tương tác chỉ hiện “Đang mở màn hình”. Các lần lưu thành công được máy chủ ghi vào luồng sự kiện; lỗi, yêu cầu bị từ chối, đọc/tra cứu và xem trước không tạo sự kiện. Trạng thái thao tác hết hạn sau 15 giây không có tín hiệu thao tác mới; kết nối nhân viên hết hạn sau 60 giây không có heartbeat. Tab ẩn/đóng gửi tín hiệu rời trang.

Presence được nhóm theo **nhân viên + khu vực + mã quầy + phiếu**, chỉ gộp các tab cùng nhóm. Nhiều nhân viên nhập hàng cùng lúc có nhiều ô; một người mở hai phiếu cũng có hai ô. Kiểm kê và chuyển kho có cùng số ID vẫn là hai công việc khác nhau. Một tài khoản mở ba quầy vẫn hiện ba quầy; cùng người mở nhập hàng và POS sẽ có ở cả hai khu vực. POS phản ánh quầy/giỏ hiện tại theo terminal, các nghiệp vụ chứng từ phản ánh từng người/phiếu. Vị trí quầy giữ theo thứ tự mã khi rời trang. Tổng quan đếm số nhóm công việc đang mở và số tài khoản riêng biệt; dùng chung tài khoản không xác định được số người thực tế.

Mã công việc trong presence được máy chủ kiểm tra cùng Store và ký vào ticket. Client không được tự khai ID/tên phiếu. Trang danh sách không có phiếu chỉ hiện ngữ cảnh màn hình chung. Với in tem, chuyển phiếu bằng XHR nhận ticket từ response được cấp quyền; đóng phiếu hoặc đổi sang khu vực in khác bỏ context phiếu đó. Đổi phiếu reset giờ mở; đóng một tab không xóa công việc ở tab khác. Các lần lưu có work key của chứng từ cha; dòng kiểm kê/chuyển kho được tra parent read-only trước khi sửa/xóa. Không thêm migration hoặc thay đổi business posting.

Mỗi ô công việc có tên nhân viên/quầy, thao tác cụ thể, mã chứng từ và chi tiết từ lần lưu thành công. Ngoài POS, máy chủ tra dữ liệu đã lưu trong đúng cửa hàng để lấy tên hàng, đơn vị và phiếu cha; trước sửa/xóa giữ snapshot nhỏ để thể hiện lượng trước/sau hoặc hàng vừa bỏ. Không dùng tên/giá do client tự khai để thay thế dữ liệu đã lưu. Dòng có đơn vị snapshot trống được tra đơn vị đã liên kết; dòng nhập dùng đơn vị gốc khi hệ số là 1 và chưa có đơn vị riêng. Không đoán đơn vị khi chưa xác định được. Khi không tra được dữ liệu, dùng thông báo nền và ghi log; giám sát không làm lỗi thao tác nghiệp vụ.

- Nhập hàng: “vừa quét nhập Gạo ST25”, số lượng và đơn vị; sửa “5 kg → 8 kg”; bỏ hàng vẫn hiện tên/lượng đã xóa. Intake ghép đúng CommandId với lần nhận hàng và itemId với hàng sửa/duyệt/xóa; lưu thông tin chờ duyệt được phân biệt với duyệt hàng.
- Kiểm kê: tên hàng, lượng đã đếm kể cả 0, lượng trước/sau và chênh lệch theo đơn vị gốc. Chuyển kho: tên hàng/lượng, kho nguồn → kho đích; gửi duyệt/từ chối cũng có nội dung phiếu.
- In tem: tên hàng và số tem từng hàng. Gửi in hiện số đã gửi; xác nhận hiện số thực nhận/tổng gửi, kể cả thiếu hoặc 0. Kế hoạch hiện số cần in mỗi hàng; hoàn tất phiếu khác với xác nhận một lệnh in. Tên hàng lấy từ nội dung lệnh đã lưu, có thể khác tên trên phiếu nhập cũ.
- Duyệt/lưu giá: loại phiếu, số phiếu, số mặt hàng và tối đa hai tên đại diện. Giá nháp chỉ đếm các dòng server xác nhận đã lưu; không phát request prices, ảnh, thông tin khách/nhà cung cấp hoặc nội dung ghi chú. Header có đổi kho/ngày phiếu thì hiện trước/sau; cước vận chuyển chỉ ghi khi trang trả thông báo lưu thành công, lỗi chuyển hướng không tạo sự kiện.

Giờ lưu dạng `HH:mm:ss` và số giây/phút đã trôi qua cập nhật liên tục theo giờ máy chủ. Giờ mở màn hình và thời gian màn hình đã mở được ghi riêng; bộ đếm này không phải thời gian làm một công việc cụ thể. Tên đại diện dài được rút gọn, tổng số mặt hàng vẫn được giữ; nội dung registry tối đa 240 ký tự. Mỗi người/phiếu giữ giờ và hiệu ứng riêng như trước.

Các cảnh 2D gồm máy POS, gói hàng và máy in bill; pallet, máy quét, máy tính và phiếu nhập; kệ hàng với thiết bị kiểm kê; kiện đi giữa kho nguồn/kho đích; máy in tem và màn hình chuẩn bị mẫu; bàn giấy và con dấu duyệt. Máy POS hiển thị chi tiết đã lưu, tên hàng/tổng tiền đầy đủ nằm dưới mô hình. Phiếu nhập hiển thị cả mã và tên phiếu (`DocumentTitle`) ở khu Nhập hàng, Duyệt phiếu và dòng thời gian; tên đầy đủ được xuống dòng dưới mã, đồng thời xuất hiện trên thiết bị. Tên lấy từ dữ liệu đã lưu cùng cửa hàng hoặc ticket trang đã ký, giữ riêng tối đa 255 ký tự; phiếu chưa đặt tên chỉ hiện mã. Màn hình thiết bị khác hiển thị mã phiếu/trạng thái; không đoán số lượng hoặc tỷ lệ hoàn thành từ chuỗi thông báo.

Hiệu ứng vật dụng chạy **một lần** khi có sự kiện lưu mới hoặc tín hiệu tương tác mới. Thêm/quét hàng POS chạy tia quét; chỉ chốt đơn thành công mới nhả bill, thanh toán một phần chỉ cập nhật màn hình. Gửi lệnh in tem mới chạy dải tem; kế hoạch, xác nhận hoặc hủy lệnh không giả lập in thêm. Duyệt/xác nhận thành công mới đóng dấu; gửi chờ duyệt, từ chối và lưu giá chỉ mô tả nhập/đọc phiếu. Duyệt hàng intake dựa trên thông báo thành công đã được server làm rõ, không đóng dấu cho lưu nháp chờ duyệt. Chuyển kho và kiểm kê có hình/hiệu ứng riêng ngay cả khi trùng ID số.

Sau lưu hiện “Vừa lưu xong” tối đa 12 giây, kể cả khi nhân viên đã rời trang. Lần lưu mới hơn tín hiệu thao tác được ưu tiên; tương tác mới sau lần lưu trở lại hiệu ứng nhập liệu. Một lần lưu tiếp theo chỉ phát lại hình động của ô đó. Snapshot trùng và cập nhật ô khác giữ nguyên node/animation. Khi POS đổi sang đơn mới, không lấy bill/chi tiết của đơn trước làm trạng thái đơn mới. Khi chờ, mất kết nối hoặc tab bị ẩn, mô hình dừng. Hiệu ứng mặc định bật, tôn trọng tùy chọn giảm chuyển động của hệ điều hành; người dùng có thể chủ động bấm “Hiệu ứng: tắt” để bật trong phiên đang xem. Nút luôn phản ánh trạng thái hiệu ứng thực tế. Animation mô tả thao tác trên phần mềm, không xác nhận máy in vật lý đã chạy hoặc hàng đã di chuyển thực tế. Không có WebGL, thư viện 3D hay vòng render liên tục.

Khu vực giao hàng đã kết nối tại D10 (08/10/2026). Mỗi đơn có thẻ riêng với mã đơn, người nhận, người soạn/người giao và người thực hiện lần lưu cuối. Khi soạn, hiện tên từng mặt hàng, lượng đã ghi nhận/kế hoạch và dòng thiếu; thanh tiến độ đếm số dòng đã ghi nhận, không suy ra phần trăm công việc vật lý. Xếp chuyến hiện người giao dự kiến; bàn giao thực tế mới hiện đã nhận hàng. Khi xuất phát, hiện chuyến, số đơn, giờ đi, thời gian đã đi và lần cập nhật cuối. Sau xác nhận ở quầy, hiện người chốt và giá trị hàng thực giao đã chốt, không coi số này là tiền mặt đã thu. Nếu có mốc trở về từ luồng cũ, hiển thị chờ đối chiếu và dừng đồng hồ đang đi; không yêu cầu người dùng nhập thêm bước trở về trong luồng đã gộp D07/D08.

Thẻ giao hàng dùng lưới riêng, hiệu ứng ngắn theo mốc đã lưu: quét/thùng hàng khi soạn, tín hiệu khi xuất phát, chứng từ tại quầy và xác nhận chốt. Trong chế độ vừa màn hình, chỉ hiển thị tóm tắt và số dòng đã ghi nhận/thiếu; bấm thẻ để xem đầy đủ từng sản phẩm và các mốc. Nhiều đơn tự chia trang cùng cơ chế các khu vực khác. Dạng đầy đủ vẫn hiển thị mọi thẻ và có thể cuộn. Chờ quản lý xử lý chưa được đánh dấu sẵn sàng; đồng hồ không tự chuyển trạng thái hoặc tạo vị trí. Mất kết nối đánh dấu dữ liệu cũ, dừng hiệu ứng và giữ lần ghi nhận cuối; kết nối lại đọc SQL. Thu hồi quyền/hết phiên xóa dữ liệu đang hiển thị, kể cả trong cửa sổ chi tiết hoặc khi kiểm tra lại bằng HTTP. Các nội dung được escape trước khi đưa vào HTML.

D10 đọc hồ sơ giao, kế hoạch soạn, phân công/custody, settlement và hai SQL outbox đã có; chỉ phát dữ liệu đã commit, không phụ thuộc worker đã đánh dấu nhận event. Định danh event ổn định qua restart/reconnect; snapshot trùng giữ một thẻ cho mỗi đơn. Chỉ quyền `admin.storemonitor.view` trong cùng cửa hàng được đọc; gửi tên khách cần cho việc nhận diện, không gửi điện thoại, địa chỉ, ghi chú, token tra cứu hay toàn bộ payload. D09 theo dõi/giao bù được tạm hoãn theo yêu cầu, không tự tạo hồ sơ theo dõi thiếu khi đọc giám sát.

## Vận hành

SSE `/admin/api/store-activity/stream` chuyển cập nhật tối đa mỗi giây. Máy chủ kiểm tra lại phiên đăng nhập, cửa hàng và quyền hiện tại trước khi gửi dữ liệu; heartbeat tối đa 5 giây. Khi mất mạng, màn hình dừng hiệu ứng, đánh dấu tín hiệu cũ và tự nối lại. Thu hồi quyền đóng luồng. `/snapshot` phục vụ kiểm tra trạng thái. Presence chỉ chấp nhận ticket bảo vệ bằng Data Protection do trang được cấp quyền phát hành, gắn với nhân viên, cửa hàng và module; POST cần antiforgery.

Presence và các nghiệp vụ hiện hữu dùng bộ nhớ tạm trên **một Web instance**: tối đa 120 sự kiện trong 4 giờ, 200 tab/cửa hàng, 8 tab/nhân viên, 8 màn hình giám sát/cửa hàng và 512 cửa hàng trong bộ nhớ. Khởi động lại làm mới phần tạm này. Riêng giao hàng đọc lại từ SQL: tối đa 120 thẻ, ưu tiên đơn đang xử lý. **Đơn đã chốt thực giao hoặc đã hủy chỉ hiện thêm 60 giây từ mốc hoàn tất đã lưu**, sau đó tự ẩn cả ở tổng quan và dạng đầy đủ; cửa sổ chi tiết của đơn đó cũng đóng. Tải lại trang, snapshot trùng hay chỉnh chuyến của đơn khác không kéo dài thời gian này. Máy chủ lọc thẻ đã hết hạn; đồng hồ giao diện tự dọn trong nhịp tối đa một giây kể cả khi mất mạng hoặc chưa có snapshot mới. Đơn chưa chốt, đang giao/chờ đối chiếu vẫn được giữ. Đây là ẩn thẻ giám sát, không xóa hồ sơ; tra cứu vẫn dùng phần lịch sử giao hàng/hóa đơn riêng. Tổng số đơn đang xử lý vẫn tính đầy đủ, có thông báo nếu vượt số thẻ hiển thị. Luồng ghép tối đa 120 sự kiện, gồm tối đa 60 sự kiện giao hàng trong 4 giờ gần đây; dòng thời gian này tách biệt với thời hạn thẻ đã hoàn tất. Không dùng bộ đếm này làm báo cáo cả ngày. Khi triển khai nhiều instance, cần kho presence/pub-sub dùng chung trước khi bật trên nhiều máy chủ. D10 không thêm migration hoặc sửa nghiệp vụ tiền/kho.

## Test tại tiệm cho D10

Khởi động Web bằng code mới, mở `/admin/store-monitor` bằng tài khoản có quyền giám sát rồi Ctrl+F5. Dùng cửa sổ khác thao tác nghiệp vụ; không cần app điện thoại.

1. Tạo đơn giao tại POS: màn hình xuất hiện đúng mã, tên khách, mặt hàng; lượng chưa soạn để trống, chưa đánh dấu bàn giao/chốt.
2. Hai nhân viên nhận hai đơn khác nhau: thấy hai thẻ riêng. Ghi một mặt hàng thiếu: đúng tên hàng, lượng đã soạn/kế hoạch, dòng thiếu, nhân viên, giờ lưu và hiệu ứng của đơn đó.
3. Xử lý/chốt soạn rồi xếp chuyến: thấy người giao dự kiến. Xác nhận bàn giao và xuất phát: thấy đã nhận hàng, đúng chuyến/người giao/giờ đi. Chờ một phút: thời gian đã đi tăng, trạng thái và lượng giữ nguyên khi chưa có báo cáo mới.
4. Người giao về, quầy nhập kết quả và xác nhận theo màn gộp D07/D08: thẻ chuyển đã chốt, hiện đúng người chốt, mặt hàng/số lượng thực giao và giá trị hàng thực giao; đối chiếu với hóa đơn bán cuối. Lượng thực giao phải khác lượng đã soạn nếu mang hàng về; hàng không giao không được tính tiền. Sau khoảng 60 giây từ lúc chốt, thẻ tự biến mất và số trang được rút gọn; đơn khác chưa chốt trong chuyến vẫn còn. F5 không làm đơn đã hết hạn hiện lại. Tra lịch sử/hóa đơn vẫn thấy hồ sơ đã chốt và tiền đúng. Đơn đã hủy cũng tự ẩn sau 60 giây.
5. F5 và khởi động lại Web: các đơn đang soạn/đang giao vẫn có đúng trạng thái. Ngắt mạng màn hình: báo dữ liệu cũ, không phát hiệu ứng thành công; nối lại mạng: phục hồi đúng, không nhân đôi thẻ.
6. Bật toàn màn hình, thử nhiều/ít đơn, nền sáng/tối và tắt hiệu ứng; kiểm tra chữ/thiếu hàng đọc được từ chỗ ngồi thật. Thu hồi quyền trên tài khoản test: nội dung cũ phải bị xóa.

Kiểm tra tự động D10 gồm `DeliveryD10MonitorTests` (Web/SQL/SSE thật trên database riêng), phần SSE/phân quyền của `StoreMonitorHttpTests`, `StoreActivityTests`, 10 test mô hình hiện hữu và `GaoApp.Tests/Ui/store-monitor-delivery-renderer.test.cjs` (renderer thật với dữ liệu mô phỏng, 1920/1440/390 px). [Bằng chứng D10](../Logs/delivery-roadmap/D10/run-20261008-01/verification.json) giữ kết quả từng lần chạy. Nghiệm thu màn hình văn phòng thật và đo p95 tại mạng tiệm còn chờ; không suy ra full CI/UAT từ các kiểm tra local. Chưa commit theo yêu cầu.

## Kiểm tra bố cục vừa màn hình (09/10/2026)

Chỉ thay giao diện; không sửa SQL/tiền/kho. Renderer thật với dữ liệu mô phỏng **8 quầy, 7 phiếu nhập, 6 việc kho, 5 việc in tem, 4 việc duyệt và 12 đơn giao** được kiểm tra ở 1920×1080, 1600×900, 1366×768, 1280×650 và 1100×620. Kiểm tra không cuộn cả trang, không cắt mốc giờ/nội dung ô; mọi công việc đi tới được bằng phân trang, snapshot trùng giữ node, chi tiết nhận cập nhật, luân phiên chỉ đổi trình bày và dừng đúng điều kiện. Màn hình 390 px giữ dạng đầy đủ. [Bằng chứng bố cục](../Logs/store-monitor-fit-20261009/verification.json) và ảnh minh họa dùng fixture, chưa thay cho nghiệm thu màn hình tại tiệm.

Test thật khi mở bằng code mới:

1. Vào `/admin/store-monitor`, **Ctrl+0**, **Ctrl+F5**, chọn “Vừa màn hình” và “Toàn màn hình” nếu cần. Cả sáu khu vực phải nhìn thấy ở zoom 100%.
2. Cho nhiều nhân viên mở/lưu phiếu và nhiều quầy thao tác cùng lúc. Kiểm tra tên, hàng vừa thao tác, giờ lưu; số bên tiêu đề là tổng công việc. Khi có nhiều trang, chờ 10 giây hoặc bấm `‹ ›` để thấy các việc còn lại.
3. Rê chuột vào khu vực hoặc mở chi tiết: trang đang xem giữ nguyên. Nhân viên lưu thêm hàng trên phiếu đó: cửa sổ chi tiết cập nhật đúng hàng/lượng/giờ; Escape quay về tổng quan.
4. Thử nền tối, tắt luân phiên và “Xem đầy đủ”; bật lại “Vừa màn hình”. Ngắt mạng: dữ liệu giữ mốc cuối và dừng luân phiên; nối lại cập nhật bình thường. Không chốt hay thay đổi đơn bằng màn hình giám sát.

Kiểm tra: `StoreActivityTests`, `StoreActivityDetailsTests`, `StoreActivityEnricherTests`, `StoreMonitorHttpTests`, kiểm tra bảo mật endpoint, `node --test GaoApp.Tests.Browser/store-monitor.scenes.test.cjs` và browser probe `store-monitor.browser.cjs`. [Báo cáo tích hợp cảnh 2D](development/STORE-MONITOR-SCENES-20261008.md), [báo cáo nền sáng/animation](development/STORE-MONITOR-UPGRADE-20261006.md), [báo cáo nhiều công việc](development/STORE-MONITOR-MULTIWORK-20261006.md), [áp dụng animation cũ](development/STORE-MONITOR-ANIMATION-20261006.md) và [thông báo chi tiết](development/STORE-MONITOR-DETAILS-20261006.md) phân biệt kiểm tra dữ liệu/bảo mật với snapshot mô phỏng dùng kiểm tra bố cục/animation.
