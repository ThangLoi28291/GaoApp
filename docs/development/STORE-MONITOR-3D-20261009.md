# Store monitor 3D — 09/10/2026

Đã tích hợp cảnh 3D vào /admin/store-monitor theo bố trí và luồng đã thống nhất. Cảnh dùng Babylon.js 9.0.0 đóng gói nội bộ; nhân vật và xe từ source R05, với trạng thái live riêng. Không dùng simulation nghiệp vụ giả, React, giá/SKU demo hoặc server API demo.

## Thay đổi

- Cảnh 10 kệ, nhận hàng, kho/kiểm kho, in tem, văn phòng, quầy POS thật và đường vòng giao hàng bên ngoài.
- Một nhân vật/UserId, chuyển việc khi có tương tác/lưu mới; không chuyển vì mở trang hoặc heartbeat của tab cũ.
- Login/heartbeat tạo roster online, logout ra cửa, mất heartbeat quá 60 giây hết hạn.
- Khách nền khoảng 5–7, không hiện tên lúc lựa hàng. Giỏ có hàng gọi khách đúng quầy; tên khách chỉ lấy từ khách đã gắn trong đơn. Giữ/mở lại/chốt/hủy có luồng tương ứng; thanh toán một phần không làm khách ra.
- In thành công chạy máy rồi lấy tem/dán kệ; gửi duyệt nhập hàng mang hàng lên kệ ngoài tiệm. Việc mới có thể ngắt chuỗi.
- Duyệt phiếu có tư thế ngồi thao tác máy tính; kiểm kho đi vào trong kho.
- Xe/chuyến và nhân viên giao dựa vào UserId và trạng thái đã lưu. Nhân viên lên xe xong mới chạy; đường vòng là minh họa, không GPS hoặc xác nhận giao tự động.
- Bảng quầy đọc tên hàng, lượng, giá/thành tiền và tổng tiền từ SQL đã commit; không gửi liên hệ, địa chỉ, ghi chú, debt, ảnh hay toàn DTO.
- Mặc định 3D, chuyển 2D/WebGL fallback; dùng một SSE chung. Ẩn tab/2D dừng render; offline/effects-off dừng chuyển động; thu hồi quyền xóa dữ liệu và hình đã vẽ.
- Bố cục 1366×768 co đồng đều tới cửa sổ nhỏ hơn, giữ toàn cảnh/đường giao hàng và chi tiết trong cột riêng.
- Một lần đọc projection/cửa hàng/giây dùng chung các viewer. Quyền/session kiểm tra riêng; cache không giữ DbContext.
- Quyền admin.storemonitor.view cấp trực tiếp, không nhận alias manage và không tự cấp mới vào ADMIN qua seed. Grant đã có giữ nguyên để chủ tự sửa trong màn hình phân quyền.

## Kiểm tra

- Build Web/.NET thành công.
- 57 kiểm tra hồi quy C#: StoreActivityTests, StoreActivityDetailsTests, StoreActivityEnricherTests, StoreMonitorHttpTests, AccountControllerObservabilityTests.
- 72 kiểm tra C# trong lượt mới: các kiểm tra PermissionTests cùng 5 unit test/1 SQL+HTTP test của 3D. Kiểm tra SQL chạy Web thật, tạo giỏ POS thật, gắn khách, xác nhận tổng/lượng, tách cửa hàng, chặn không quyền và logout; database do fixture tạo riêng.
- 14 kiểm tra world state: ưu tiên việc mới, không chuyển do mở trang, giữ/resume đúng khách, chốt/hủy/thanh toán một phần, in/dán, mang hàng lên kệ, ra/vào, restart, tránh vật cản, courier bị việc mới thay thế, giới hạn khách của đơn giữ trạng thái khi tắt hiệu ứng, đổi người giao và dọn chuyến hết hạn.
- 3 kiểm tra Chrome/WebGL: render thật với SSE fixture, XSS escaping, 1920×1080/1366×768/1280×650/1024×768/800×600/390×844, dừng offline/2D, clear quyền, lazy-load và không WebGL.
- 19 kiểm tra renderer/cảnh 2D và giao hàng vẫn qua.

Chrome headless dùng SwiftShader để kiểm tra hành vi/hiển thị; FPS này không phải phép đo GPU máy tại tiệm. Chưa deploy production hoặc nghiệm thu trên các client thực tế. Không sửa dữ liệu tiền/kho/chứng từ hay quyền production.

Bằng chứng C#: GaoApp.Tests/TestResults/store-monitor-3d-regression.trx, store-monitor-3d-new.trx, store-monitor-3d-final.trx. Ảnh kiểm tra và preview: Logs/store-monitor-3d-20261009/. Test report chỉ xác nhận các suite nêu trên, không đại diện toàn bộ CI.

## Mở sau nâng cấp

1. Build/publish Web chứa bundle mới rồi khởi động lại ứng dụng.
2. Chủ tự cấp quyền **Giám sát hoạt động trực tiếp của cửa hàng** cho vai trò chỉ chứa mình; bỏ grant khỏi các vai trò khác đang có nếu cần.
3. Mở /admin/store-monitor, Ctrl+F5, chọn 3D nếu trình duyệt đã lưu chế độ 2D.
4. Thử thao tác POS/in tem/nhập hàng/duyệt/giao ở trình duyệt khác để đối chiếu với chứng từ thật. Monitor chỉ đọc.

Build lại scene chỉ khi sửa nguồn: xem [README](../../tools/store-monitor-3d/README.md). Server production không cần npm/Node.

## Bổ sung sau phản hồi ảnh thực tế

- Hai thẻ mã GH là phiếu giao hàng đã lưu, không phải giỏ đang tính tiền. Tổng quan tách nhóm quầy POS và đơn giao; đơn giao ghi rõ người nhận và trạng thái, giữ quy tắc chưa kết thúc thì không hết hạn theo ngày mã phiếu.
- Thông tin sản phẩm quét/thêm gần nhất chỉ nằm trong bảng bên phải: tên, lượng vừa thêm, đơn giá đã lưu và tổng giỏ. Theo phản hồi tiếp theo của chủ, đã bỏ ô sản phẩm nổi trên mô hình để không che quầy/nhân vật; giữ nhãn tên nhân viên và khách. Bấm Tính tiền mở chi tiết quầy có hàng; bảng đánh dấu dòng vừa thêm. Feed cũng nêu sản phẩm cụ thể.
- Thông tin lấy bằng StoreCheckoutScanCapture của chính request, theo các dòng OrderLine đã SaveChanges; không suy ra bằng dòng cuối giỏ hoặc đọc lại dữ liệu có thể bị request khác đổi. Sau thành công của action/transaction, sự kiện chứa allowlist nhỏ cùng LineId. Không thêm query vào từng lượt quét. Delta lượng phản ánh chuẩn hóa của POS, không tin số lượng client khai; không lấy dòng quà tặng hoặc dòng chỉ đổi giá làm sản phẩm vừa quét.
- Lần quét trước chỉ ghép với đúng current OrderId/Terminal/LineId. Quét lặp lại có sự kiện mới, đổi/giữ/chốt/hủy giỏ xóa ô cũ; lỗi không công bố thành công. Snapshot chưa có sự kiện scan hợp lệ hiện thông báo chưa ghi nhận.
- Nhân viên đã bắt đầu POS giữ vị trí khi còn giỏ Draft có hàng, chuyển khi có việc mới hoặc giỏ kết thúc. Ưu tiên thao tác mới trước khi xét hết hạn, nên tab POS cũ không giành lại người sau việc khác.

Kiểm tra bổ sung: build Web và Tests trong thư mục artifacts riêng thành công, 0 lỗi/0 cảnh báo; 26 C# (SQL/HTTP thật cho thêm thủ công, barcode, current-scan, lượng 0 được chuẩn hóa, scan thất bại, tenant, quyền và logout); 16 world state; 4 Chrome/WebGL; 9 hồi quy 2D/giao hàng. TRX: GaoApp.Tests/TestResults/store-monitor-3d-scan-final.trx. Bằng chứng và ảnh dữ liệu fixture: Logs/store-monitor-3d-scan-20261009/checkout-scan.png. Kiểm tra không sửa dữ liệu cửa hàng thật. Source đã cập nhật; ứng dụng đang chạy cần build/publish và khởi động lại để nhận trường PosScan mới, rồi Ctrl+F5 trên client.

## Thay khu B bằng đường giao hàng

- Bỏ nhà nhận B và đổi nút camera thành **Giao hàng**. Đường vòng bo góc bên tiệm chạy khoảng 30 giây/lượt, có nhánh ra/về điểm đỗ; camera toàn cảnh bao gồm tiệm và đường.
- Trước đây xe và người chạy riêng. Bây giờ người đi đến xe, lên xe 2 giây, sau đó xe mới xuất phát. Khi lên/chạy/xuống, mesh nhân viên gắn vào xe; vị trí và hướng cập nhật cùng một nhịp. Giữ tư thế ngồi đúng cả khi tắt hiệu ứng/mất kết nối.
- Khi còn đơn chưa có kết quả, xe tiếp tục chạy. Mốc trở về đã lưu hoặc kết quả của tất cả đơn trong chuyến mới cho xe theo đường về điểm đỗ, xuống xe 2 giây rồi đi vào tiệm. Không tạo xác nhận giao/trở về, không dùng thời gian để suy ra kết quả và không dùng đường tắt qua phần cỏ.
- Người giao không online dùng một nhân vật minh họa theo chuyến; không thêm presence. Khi online lại, nhân vật thật thay minh họa trên cùng xe. Xóa xe nguồn không xóa mesh nhân viên đang ngồi trên đó. Công việc mới ưu tiên, kể cả tải lại sau khi công việc mới đã hết thời gian hoạt động.
- Kiểm tra: build Web/Tests thành công, 0 lỗi/0 cảnh báo; 20 world state và 6 Chrome/WebGL. Hai kiểm tra WebGL mới bao gồm lên/chạy/về/xuống/vào tiệm, tư thế ngồi khi dừng hiệu ứng, người giao offline/online và giữ nhân vật sau khi xóa xe nguồn. Các kiểm tra bố cục 1920→390 px, chi tiết sản phẩm bên phải, 2D/lazy-load/offline/thu hồi quyền tiếp tục qua.

Bằng chứng: `Logs/store-monitor-3d-road-20261009/verification.json`, `world-tests.txt`, `browser-tests.txt`; ảnh `boarding.png`, `on-road.png`, `rider.png`, `returning.png`, `dismounted.png`, `back-in-store.png`. Dữ liệu trình duyệt là fixture, không thao tác chứng từ thật. Chưa triển khai production; bản Web mới cần build/publish và khởi động lại, sau đó Ctrl+F5 ở client.
