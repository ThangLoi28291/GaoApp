# Roadmap nâng cấp ChatbotLocal thành dịch vụ trợ lý cho GaoApp

Ngày thống nhất phạm vi: 11/10/2026.

Nâng cấp project ChatbotLocal hiện có thành ứng dụng API chạy riêng để phục vụ trợ lý nội bộ của GaoApp. Trước tiên hoàn thiện ChatbotLocal, hợp đồng API, giao diện kiểm thử và bộ nghiệm thu. Khi phần này sẵn sàng, mới triển khai giao diện và kết nối nghiệp vụ trong GaoApp.

ChatbotLocal và GaoApp dự kiến đặt trên cùng một server nhưng chạy ở hai tiến trình riêng. Nếu dùng IIS, mỗi ứng dụng có Application Pool riêng. Cửa sổ chat và các nút mic của nhân viên nằm trong GaoApp sau khi tích hợp; Gemini, điều phối AI và xử lý ngôn ngữ nằm ở ChatbotLocal.

Kiến trúc API riêng và phạm vi tại đây là các quyết định hiện hành, thay cho phương án thư viện AI chạy cùng backend trong `ROADMAP-TICH-HOP-CHATBOT-GAOAPP.md`. Roadmap cũ tiếp tục là nguồn đối chiếu baseline, source và các tình huống đã kiểm tra.

Các giai đoạn dưới đây là kế hoạch triển khai. Việc tạo roadmap không đồng nghĩa các chức năng đã được triển khai hoặc nghiệm thu.

## 1 Phạm vi đã thống nhất

| Nội dung | Yêu cầu |
| --- | --- |
| Project AI | Nâng cấp ChatbotLocal hiện có, tận dụng hội thoại, Gemini, bộ nhớ và bộ kiểm thử |
| Triển khai | Ứng dụng API riêng, publish và khởi động lại độc lập với GaoApp, có thể cùng server |
| Nhân viên | Dùng tài khoản GaoApp; trợ lý biết tên hiển thị của nhân viên để chào và xưng hô |
| Cửa sổ trợ lý | Icon nhỏ ở góc màn hình GaoApp, bấm để mở hội thoại; giữ nội dung khi đóng rồi mở lại |
| Giọng nói | Bấm mic để nói thành chữ; câu trả lời của chatbot hiện bằng chữ |
| Hướng dẫn | Có nguồn hướng dẫn sử dụng GaoApp đầy đủ theo chức năng, có thể bổ sung và cập nhật |
| Giá | Tra cả giá bán sỉ và bán lẻ, đúng sản phẩm, đơn vị, cửa hàng và quy tắc nguồn GaoApp |
| Nhập hàng | Hiểu câu nói chưa trùng tên lưu; tìm hàng và đề xuất sẵn đơn vị, số lượng để nhân viên kiểm tra rồi xác nhận |
| Sản phẩm mới | Nhận câu nói thành chữ, hỗ trợ sửa lỗi nhận diện và chuẩn hóa tên, điền đề xuất vào form |
| Nguồn nghiệp vụ | Dữ liệu sản phẩm, giá, đơn vị và quy đổi do GaoApp cung cấp theo quyền |
| Tồn kho | Chưa mở công cụ số tồn hoặc xác nhận còn hàng; nghiệm thu riêng sau |
| Thao tác ghi | Nhân viên xác nhận trên GaoApp; ChatbotLocal không trực tiếp lưu phiếu, tạo sản phẩm hoặc lập giao dịch |
| Khả năng chịu lỗi | Khi trợ lý lỗi, GaoApp vẫn có luồng quét mã, gõ tìm, nhập và lưu thủ công |
| Thứ tự thực hiện | Hoàn thiện ChatbotLocal trước, tích hợp GaoApp sau |

Không thuộc bản đầu: đọc câu trả lời thành tiếng, hội thoại nói nghe liên tục, gửi tin nhắn cho khách tự động, tra tồn kho, tự động lập đơn, duyệt phiếu hoặc sửa dữ liệu nghiệp vụ. Có thể mở rộng từng phần sau khi thống nhất phạm vi mới.

## 2 Kiến trúc và ranh giới trách nhiệm

Luồng sử dụng sau khi tích hợp:

1. Nhân viên nói hoặc gõ trong GaoApp. Phần nhận giọng nói tạo văn bản để hiển thị và sửa khi cần.
2. Backend GaoApp xác định nhân viên, cửa hàng, quyền và ngữ cảnh hợp lệ; gọi API ChatbotLocal bằng kết nối được xác thực.
3. ChatbotLocal xử lý hội thoại hoặc một yêu cầu riêng như tìm hàng cho phiếu nhập, chuẩn hóa tên sản phẩm.
4. Khi cần dữ liệu, ChatbotLocal gọi API nghiệp vụ được cho phép của GaoApp theo ngữ cảnh đã xác thực. GaoApp kiểm tra lại quyền và phạm vi khi phục vụ dữ liệu.
5. ChatbotLocal trả câu trả lời bằng chữ hoặc đề xuất có cấu trúc. GaoApp hiển thị và điền thông tin phù hợp lên form.
6. Khi nhân viên xác nhận, GaoApp kiểm tra lại sản phẩm, đơn vị, số lượng, quyền và trạng thái chứng từ rồi thực hiện bằng dịch vụ nghiệp vụ hiện có.

| Thành phần | ChatbotLocal nâng cấp trước | GaoApp tích hợp sau |
| --- | --- | --- |
| Hội thoại | Điều phối Gemini, hiểu nhiều lượt, lịch sử và tóm tắt | Icon nổi, cửa sổ chat, hiển thị và tiếp tục hội thoại |
| Danh tính | Nhận và xác thực ngữ cảnh từ hệ thống gọi; cách ly lịch sử theo nhân viên và cửa hàng | Đăng nhập, cung cấp tên hiển thị, phạm vi và quyền hiện tại |
| Giọng nói | Chuẩn bị phần nhận tiếng Việt dùng lại và giao diện thử; xử lý văn bản đã nhận diện | Nối mic với chat, phiếu nhập và form tên sản phẩm |
| Hướng dẫn | Lưu nội dung, nhập và cập nhật bài, tìm đoạn phù hợp và trả lời có nguồn | Gắn các đường dẫn màn hình thực tế và quyền của người dùng |
| Sản phẩm và giá | Hợp đồng dữ liệu, adapter, dữ liệu giả lập, đối chiếu kết quả | API nghiệp vụ thật theo quyền và chính sách giá GaoApp |
| Nhập hàng | Trả sản phẩm, đơn vị, số lượng, trạng thái cần chọn hoặc hỏi thêm | Mở và điền cửa sổ nhập số lượng, nhân viên xác nhận để thêm dòng |
| Tên sản phẩm mới | Trả tên đề xuất và thông tin cần làm rõ | Điền form, sửa và lưu theo luồng hiện có |
| Vận hành | Key Gemini, quota, giới hạn tải, log, health check, backup và publish riêng | Giới hạn chờ API, công tắc sử dụng, thông báo lỗi và thao tác thủ công |

Không tạo thư viện AI mới chạy bên trong GaoApp theo phương án cũ. Có thể tổ chức lại mã ChatbotLocal để rõ trách nhiệm, nhưng giữ khả năng triển khai thành một ứng dụng trợ lý độc lập.

Tách tiến trình giúp cách ly lỗi ứng dụng và cập nhật riêng; hai ứng dụng trên cùng server vẫn chia sẻ CPU, RAM, ổ đĩa và hệ điều hành. Giới hạn tải của trợ lý và timeout phía GaoApp vẫn là yêu cầu bắt buộc. [Microsoft về Application Pool](https://learn.microsoft.com/en-us/iis/configuration/system.applicationhost/applicationpools/).

## 3 Nền tảng hiện có cần tái sử dụng

| Thành phần ChatbotLocal | Hướng nâng cấp |
| --- | --- |
| Hội thoại và điều phối công cụ | Giữ hành vi đã kiểm thử; thêm danh tính, quyền, công cụ hướng dẫn và các API hỗ trợ form |
| Hiểu sản phẩm nhiều lượt | Tái sử dụng lọc hãng, dung tích, thuộc tính, đổi sản phẩm và tham chiếu nhóm |
| Client Gemini | Giữ model cấu hình hiện tại `gemini-3.5-flash-lite`, timeout và cách xử lý lịch sử công cụ; kiểm tra khả năng model ở môi trường triển khai |
| Bộ nhớ và SQLite | Bổ sung chủ sở hữu, phạm vi, phiên bản state, chính sách lịch sử và backup |
| Ba slot key test | Giữ quản lý key ở backend, phân loại quota và theo dõi theo project/model |
| Đo sử dụng AI | Chuyển thành số liệu vận hành có thể lưu và tổng hợp |
| Cầu nối GaoApp Development | Giữ cho kiểm thử trong phạm vi đã có; thay bằng adapter theo hợp đồng API dùng thật khi GaoApp tích hợp |
| Giao diện ChatbotLocal | Dùng làm nơi kiểm thử chat và các thao tác hỗ trợ trước khi có UI GaoApp |
| Bộ kiểm thử | Giữ baseline, ghi rõ test thay đổi do ownership, scope hoặc hợp đồng mới |

Roadmap bàn giao trước ghi 699 kiểm tra tự động đạt và 65/65 lượt hội thoại trong bộ tình huống đã kiểm tra. Các số liệu này là baseline được báo cáo của phiên bản cũ; bản nâng cấp cần chạy lại phần tương ứng và nghiệm thu thêm yêu cầu mới. Chúng không chứng minh bản API mới hoặc giá sỉ đã hoàn thành.

Các điểm cần thay đổi từ nguồn hiện tại:

- Kho hội thoại hiện chưa gắn chủ sở hữu nhân viên và cửa hàng.
- Adapter GaoApp hiện dùng ngữ cảnh cấu hình cố định, hợp với test Development nhưng chưa hợp với nhiều nhân viên/cửa hàng.
- Dữ liệu giá hiện dùng trường giá chung; hợp đồng mới cần phân biệt sỉ, lẻ và trạng thái thiếu giá.
- Cầu nối hiện đọc và trả dữ liệu tồn. Phần dữ liệu gửi Gemini trong bản đầu phải loại thông tin tồn và khả dụng.
- Trạng thái hết quota của key hiện nằm trong một tiến trình; cần chốt việc lưu cooldown qua restart.
- Cấu hình giữ lịch sử 30 ngày hiện dựa trên lần cập nhật cuối của cuộc trò chuyện, cần làm rõ chính sách tuổi từng tin nhắn.

## 4 Hành vi cần đạt cho từng công việc

### Hội thoại nội bộ và tên nhân viên

Nhận tên hiển thị từ ngữ cảnh tin cậy của GaoApp, không yêu cầu nhân viên khai báo lại mỗi lần. Dùng tên đó trong lời chào; khi chưa có cách xưng hô được xác định, dùng cách gọi trung tính và cho phép nhân viên đổi cách xưng hô trong hội thoại.

Không suy ra tuổi hoặc giới tính từ tên. Chỉ gửi thông tin danh tính cần thiết sang Gemini theo chính sách dữ liệu; lời chào có thể được tạo ở tầng ứng dụng để giảm dữ liệu cá nhân gửi lên model.

Lịch sử tiếp tục được sau khi đóng cửa sổ hoặc tải lại trang. Mọi thao tác liệt kê, xem, gửi, đổi tên và xóa phải kiểm tra chủ sở hữu và phạm vi. Khi đổi cửa hàng, kho hoặc pháp nhân, không dùng state sản phẩm cũ để tra dữ liệu mới.

### Hướng dẫn sử dụng GaoApp

Xây dựng kho hướng dẫn có thể nhập và cập nhật theo chức năng. Mỗi bài có tên, nội dung thao tác, điều kiện áp dụng, quyền liên quan, đường dẫn màn hình, phiên bản, nguồn đối chiếu và trạng thái duyệt sử dụng.

Chatbot tìm các đoạn phù hợp, trả lời ngắn theo từng bước và dẫn về bài hoặc màn hình liên quan. Có thể dùng màn hình hiện tại làm ngữ cảnh để hiểu câu như “thêm hàng mới ở đây thế nào”. Những đường dẫn chưa được xác minh phải được đánh dấu trong khâu biên soạn, không giới thiệu như màn hình đã có.

Lập danh sách chức năng GaoApp cần có hướng dẫn; phân biệt đã biên soạn, đã đối chiếu và chưa có nội dung. Bao phủ toàn bộ hướng dẫn là mục tiêu nội dung, không mặc nhiên đạt chỉ vì API tìm kiếm đã hoạt động. Khi chưa có bài được duyệt, trợ lý nói rõ và không tự tạo quy trình thao tác.

Tài liệu kỹ thuật hiện có là đầu vào biên soạn, cần chuyển thành hướng dẫn nhân viên phù hợp. Màn hình GaoApp có thể được đọc để đối chiếu nhưng giai đoạn này không sửa code GaoApp. Nội dung mới hoặc sửa đổi có phiên bản; các câu hỏi cũ phải truy được bài đã dùng để trả lời.

### Tra giá sỉ và lẻ

Xác định đúng sản phẩm, biến thể và đơn vị trước khi báo giá. Khi nhân viên yêu cầu cả hai, hiển thị rõ giá sỉ và lẻ từ nguồn GaoApp, tiền tệ, đơn vị và thời điểm tra.

Hợp đồng cần biểu diễn riêng giá có dữ liệu, giá bằng 0 theo nguồn, chưa có giá và lỗi nguồn. Không suy ra giá sỉ từ giá lẻ hoặc tự coi hai giá bằng nhau. Giá trong lịch sử chỉ phục vụ hiểu hội thoại; khi báo giá phải tra lại nguồn.

Nếu yêu cầu tính tổng, backend tính bằng decimal từ giá đã xác minh. Trước khi coi tổng này tương đương POS, phải đối chiếu quy tắc số lượng, đơn vị, nhóm khách và cách tính giỏ hàng của GaoApp ở giai đoạn tích hợp. Bản báo giá bằng chữ chưa phải đơn hàng hoặc giao dịch đã lưu.

Trình bày báo giá ngắn, dễ đọc và dễ sao chép để nhân viên trả lời khách nhanh. Việc gửi nội dung cho khách do nhân viên thực hiện.

Giá và dữ liệu giả lập dùng trước khi tích hợp phải có nhãn nguồn test rõ ràng. Cầu nối Development chỉ chứng minh được những trường nó thực sự hỗ trợ; không dùng nó để tuyên bố giá sỉ thật đã nghiệm thu.

### Hiểu câu nói khi nhập hàng

Nhận văn bản từ giọng nói, tách tên hàng, hãng, thuộc tính, dung tích, đơn vị nhận và số lượng. Tìm ứng viên trong danh mục theo phạm vi, đối chiếu dữ liệu rồi trả ID sản phẩm/biến thể/đơn vị hợp lệ; không tạo ID từ suy đoán của model.

Ví dụ nghiệm thu chính từ yêu cầu của người dùng:

| Nội dung | Kết quả mong muốn |
| --- | --- |
| Câu nói | 4 lốc TH có đường 180ml |
| Sản phẩm | Sữa tươi TH có đường hộp 180ml |
| Đơn vị nhận | Lốc |
| Số lượng nhận | 4 |
| Quy đổi trong dữ liệu mẫu | 1 lốc = 4 hộp |
| Tương đương | 16 hộp |
| Khi tích hợp GaoApp | Mở sẵn cửa sổ nhập số lượng, chọn Lốc, điền 4; nhân viên kiểm tra và xác nhận |

Hệ số 4 là dữ liệu mẫu theo ảnh minh họa, không là mặc định cho mọi lốc. Hệ số thực tế lấy từ GaoApp; phép tính quy đổi do backend thực hiện. Dung tích 180ml không phải số lượng nhận 180.

Nếu có nhiều hàng phù hợp hoặc một thuộc tính quan trọng chưa rõ, trả danh sách ứng viên hoặc câu hỏi bổ sung. Nếu hàng được xác định nhưng không có đơn vị người dùng nói, nêu rõ thay vì tự tạo đơn vị hoặc tự đổi sang một quy cách khác.

Yêu cầu hỗ trợ form này có API riêng, không bắt buộc tạo hoặc tiếp tục cuộc trò chuyện. Xử lý được văn bản gõ tay giống văn bản từ mic. Mỗi lần nhân viên sửa hoặc nói lại tạo thế hệ yêu cầu mới; giao diện bỏ kết quả cũ đến muộn.

### Chuẩn hóa tên sản phẩm mới

Nhận văn bản gốc, giữ bản gốc và trả tên đề xuất có dấu, cách viết hãng, dung tích và cách trình bày phù hợp. Ví dụ “sữa tươi th có đường một trăm tám mươi mi li” thành “Sữa tươi TH có đường 180ml”.

Giữ đúng nghĩa các thuộc tính như có đường, ít đường, không đường, mùi vị, hãng và dung tích. Không thêm thuộc tính người nói chưa cung cấp hoặc chưa được xác minh. Không đổi một tên đã có sẵn trong danh mục chỉ vì AI thích cách viết khác.

Nếu tên gần sản phẩm đã có, trả gợi ý để tránh tạo trùng; nhân viên vẫn quyết định chọn hàng có sẵn hay tạo mới theo quyền. Bản đầu chỉ hỗ trợ tên và thông tin cần làm rõ; barcode, danh mục, đơn vị và quy đổi không được tự điền như sự thật nếu không có nguồn xác minh.

### Nhận giọng nói thành chữ

Phần hiện có tại GaoApp dùng nhận tiếng Việt trong trình duyệt, hiển thị văn bản rồi gọi tìm kiếm. Tái sử dụng cách này qua thành phần hoặc adapter dùng được trong giao diện test ChatbotLocal; việc nối vào GaoApp làm sau.

Có bắt đầu, dừng, nói lại, sửa văn bản và xử lý lỗi mic. Dừng nhận âm thanh khi đóng cửa sổ hoặc rời trang. Câu trả lời chỉ hiện bằng chữ; không cần dịch vụ đọc tiếng nói hoặc gửi audio tới Gemini để hoàn thành phạm vi này.

AI cải thiện việc hiểu văn bản sau nhận diện, không bảo đảm khôi phục chính xác thông tin đã nghe sai. Khi khác biệt ảnh hưởng loại hàng, dung tích hoặc số lượng thì yêu cầu xác nhận. Kiểm thử thật trên thiết bị nhân viên sử dụng vẫn cần ở giai đoạn GaoApp.

## 5 Hợp đồng API chuẩn bị cho GaoApp

Các đường dẫn sau là đề xuất để chốt trong giai đoạn thiết kế. Dùng phiên bản API và xuất OpenAPI, mẫu request/response, mã lỗi và tài liệu xác thực. Không coi việc đặt tên endpoint là bằng chứng endpoint đã tồn tại.

| Nhóm API ChatbotLocal | Đường dẫn đề xuất | Kết quả |
| --- | --- | --- |
| Gửi hội thoại | POST /api/v1/chat/messages | Nội dung chữ, ConversationId, nguồn tham chiếu, RequestId |
| Lịch sử | /api/v1/conversations và các tài nguyên con | Tạo, liệt kê, đọc phân trang, đổi tên và xóa theo sở hữu |
| Hiểu câu nhập hàng | POST /api/v1/receiving/resolve | Kết quả có cấu trúc để chọn hàng, đơn vị và số lượng |
| Chuẩn hóa tên mới | POST /api/v1/products/normalize-name | Văn bản gốc, tên đề xuất, thay đổi và điểm cần làm rõ |
| Tra giá theo danh tính hàng | POST /api/v1/quotes/lookup | Giá sỉ/lẻ theo từng đơn vị, trạng thái, nguồn và thời điểm |
| Khả năng sử dụng | GET /api/v1/capabilities | Các khả năng đang bật và được phép trong ngữ cảnh |
| Quản lý hướng dẫn | /api/v1/admin/guides | Nhập, sửa, duyệt, thu hồi và phiên bản, chỉ cho vai trò được cấp quyền |
| Health check | /health/live và /health/ready | Trạng thái nội bộ đã khử bí mật; không gọi Gemini mỗi lần kiểm tra |

Một hợp đồng ngữ cảnh tin cậy cần tối thiểu: hệ thống gọi, UserId, tên hiển thị, StoreId, kho/pháp nhân nếu cần, quyền hoặc bằng chứng ủy quyền, thời hạn hiệu lực và RequestId. Chọn cơ chế xác thực backend với backend cùng token ngữ cảnh ngắn hạn hoặc cơ chế tương đương; kiểm tra nơi phát hành, nơi nhận, thời hạn và phạm vi.

Không tin UserId, StoreId hay danh sách quyền do trình duyệt tự gửi. Xác thực hệ thống gọi không tự động cho phép truy cập dữ liệu của mọi nhân viên. Mọi API đọc nghiệp vụ phía GaoApp vẫn kiểm tra quyền hiện tại và ngữ cảnh, kể cả khi ChatbotLocal đề xuất công cụ hợp lệ.

Kết quả hiểu câu nhập hàng nên có các trường sau:

| Nhóm thông tin | Trường hoặc ý nghĩa |
| --- | --- |
| Theo dõi | ContractVersion, RequestId, văn bản gốc, nguồn dữ liệu và thời điểm tra |
| Kết quả | matched, ambiguous, needs_clarification, not_found hoặc lỗi có mã riêng |
| Danh tính | ProductId, VariantId, ConversionId nếu có, UnitId và tên tương ứng |
| Số lượng | Quantity, hệ số quy đổi, đơn vị gốc và EquivalentBaseQuantity |
| Cần xử lý | Danh sách ứng viên, thuộc tính thiếu, câu hỏi bổ sung, cảnh báo |
| Hiển thị | Tóm tắt ngắn để nhân viên kiểm tra trước khi xác nhận |

Nếu thiếu đơn vị hoặc số lượng thì để thiếu rõ ràng, không gắn kết quả vào trạng thái đã đủ để tự điền. Có thể trả sản phẩm đã khớp kèm yêu cầu chọn đơn vị/số lượng. Điểm tự tin của model chỉ là thông tin hỗ trợ, không thay việc đối chiếu danh tính và thuộc tính.

Hợp đồng đọc nghiệp vụ dự kiến cần: tìm ứng viên theo từ khóa/thuộc tính/barcode, lấy chi tiết biến thể và đơn vị/quy đổi, tra giá sỉ/lẻ, xác minh scope và quyền hiện tại khi cần. Kết quả có trạng thái thiếu dữ liệu, phiên bản hợp đồng, nguồn và thời điểm. Khi GaoApp chưa có API này, dùng server giả lập và fixture để nghiệm thu hợp đồng.

## 6 Quyền lịch sử và chính sách dữ liệu

- Tách lịch sử theo nhân viên và cửa hàng; lưu kho/pháp nhân khi chúng tác động dữ liệu hội thoại.
- Kiểm tra ownership cho mọi thao tác hội thoại. Khi quyền bị thu hồi, chặn hoặc loại dữ liệu không còn được phép khỏi transcript và summary trước khi hiển thị hoặc gửi lại model.
- Đổi scope thì tạo hội thoại phù hợp hoặc làm sạch ngữ cảnh; không để summary mang dữ liệu sang cửa hàng khác.
- Xử lý tuần tự một hội thoại, kiểm soát nhiều tab, chống gửi trùng theo RequestId và có cách khôi phục trạng thái sau mất phản hồi.
- Phân biệt thời gian giữ transcript, summary và state sản phẩm. Chốt retention trước khi mở sử dụng thật; không coi mốc 30 ngày hiện tại là quyết định cuối.
- Không giữ transaction dữ liệu trong lúc chờ Gemini. Có migration, backup và khôi phục kho lịch sử.
- Hướng dẫn, tên hàng và kết quả công cụ là dữ liệu đầu vào; nội dung “bỏ qua quyền” trong chúng không thay đổi chính sách hay quyền thực thi.
- Dùng dữ liệu giả hoặc dữ liệu đã được cho phép trong giai đoạn test. Không chuyển toàn bộ lịch sử chưa có ownership sang bản vận hành.
- Chốt trường được gửi Gemini cho văn bản tự do, tên hiển thị, transcript, summary, bài hướng dẫn và dữ liệu công cụ. Bài hướng dẫn cũng có thể chứa thông tin nội bộ cần xét phạm vi.

Google yêu cầu không gửi thông tin nhạy cảm, bí mật hoặc cá nhân lên Unpaid Services; chính sách sử dụng dữ liệu khác với Paid Services. Lựa chọn gói và danh sách dữ liệu được phép phải được chốt trước khi dùng dữ liệu thật. Không coi bộ lọc văn bản là bảo đảm phát hiện mọi thông tin nhạy cảm. [Điều khoản Gemini](https://ai.google.dev/gemini-api/terms).

## 7 Quota lỗi và vận hành độc lập

Giữ ba slot key phục vụ test, quản lý bí mật ở backend ChatbotLocal. Chỉ chuyển slot khi xác định lỗi quota ngày và slot tiếp theo có thể còn hạn mức; không chuyển key vì mọi lỗi 429. Phân biệt giới hạn phút, token, ngày, xác thực, lỗi dịch vụ và timeout. Khi hết slot hợp lệ, dừng gọi và trả lỗi rõ ràng.

Quota Google tính theo project, không theo từng key. Các slot cùng project có thể cùng hết hạn mức; cần metadata project và cooldown chung phù hợp. Hạn mức lấy từ AI Studio lúc triển khai; RPD reset theo giờ Pacific. [Tài liệu quota Gemini](https://ai.google.dev/gemini-api/docs/rate-limits).

Các giới hạn cần cấu hình: yêu cầu đồng thời toàn dịch vụ, theo nhân viên/cửa hàng, hàng chờ tối đa, thời gian chờ, deadline xử lý, số vòng model, số công cụ, số retry và ngân sách từng request. Hủy từ hệ thống gọi cần truyền xuống các bước xử lý; tránh một nhân viên chiếm hết năng lực hoặc quota.

Khi nguồn dữ liệu lỗi, báo đúng lỗi; không trả giá cũ từ lịch sử thay nguồn. Lỗi riêng một tính năng không làm health check cơ bản hoặc tính năng được phép khác bị treo. Trợ lý có công tắc toàn dịch vụ và từng khả năng, cùng trạng thái khả năng thực tế cho GaoApp.

Đo mọi lần gọi Gemini, kể cả tóm tắt và retry. Lưu model, project, slot đã khử bí mật, RequestId, công việc, thời gian chờ/xử lý, kết quả, mã lỗi và token theo trường vendor cung cấp. Trường thiếu được đánh dấu chưa có dữ liệu, không tính thành 0. Báo cáo request/token/chi phí ước tính phải tách khỏi hóa đơn thực tế.

Publish riêng ứng dụng ChatbotLocal; cấu hình IIS Application Pool riêng hoặc tiến trình dịch vụ tương đương, quyền file riêng, log riêng, cách tự khởi động, backup và rollback. API phục vụ GaoApp dùng kết nối nội bộ được xác thực; không mở endpoint test hoặc trang quản trị bí mật ra Internet. Gói publish không chứa key, cookie, credential hoặc dữ liệu test chưa được phép.

Tách timeout API trợ lý khỏi thao tác lưu nghiệp vụ GaoApp. Thiết kế để sau này GaoApp có thể ngừng chờ AI và tiếp tục thao tác thủ công. Khi chưa tích hợp, kiểm thử điều này bằng client/server giả lập; chưa coi đó là nghiệm thu thao tác GaoApp thật.

## 8 Các giai đoạn nâng cấp ChatbotLocal

### Giai đoạn 0 Chốt hợp đồng và baseline

Đọc source hiện tại, lập bản đồ các phần tái sử dụng, đóng băng bộ kiểm thử baseline và xác định thay đổi cần thiết. Chốt ngữ cảnh xác thực, phiên bản API, schema sản phẩm/giá/đơn vị, trạng thái kết quả hỗ trợ form và hợp đồng đọc dữ liệu GaoApp. Lập danh sách hướng dẫn cần biên soạn.

Đầu ra: thiết kế, OpenAPI hoặc bản hợp đồng ban đầu, mẫu dữ liệu, bảng quyền theo công việc, danh sách phần cần GaoApp làm sau, backlog và ngân sách test.

Nghiệm thu: từng công việc đã có input, output, quyền, dữ liệu nguồn và ví dụ đạt/chưa đạt. Các quyết định vận hành chưa chốt được ghi rõ cùng thời điểm bắt buộc chốt, không dùng giả định làm sự thật.

### Giai đoạn 1 API tin cậy và lịch sử nhân viên

Bổ sung xác thực hệ thống gọi, ngữ cảnh nhân viên/cửa hàng và quyền theo khả năng. Nâng cấp kho hội thoại có ownership, phạm vi, phiên bản state, phân trang và migration. Chuẩn hóa mã lỗi, RequestId, chống trùng, hủy và khóa hội thoại.

Đầu ra: API hội thoại được bảo vệ, client test với danh tính giả lập hợp lệ, migration và kiểm thử quyền.

Nghiệm thu: đoán ConversationId hoặc sửa scope không đọc/sửa được dữ liệu người khác; quyền bị thu hồi được xử lý theo chính sách; lỗi/hủy không lưu hội thoại ở trạng thái thành công giả.

### Giai đoạn 2 Adapter sản phẩm đơn vị và giá sỉ lẻ

Tách hợp đồng nghiệp vụ khỏi cầu nối Development. Tạo adapter cho API dự kiến và server giả lập với nhiều cửa hàng, đơn vị, hệ số và hai loại giá. Giữ định danh ổn định cho các lượt hỏi tiếp; quy định state cũ không tương thích. Loại tồn kho khỏi dữ liệu AI và danh sách công cụ đang bật.

Đầu ra: hợp đồng nghiệp vụ, fixture, adapter, công cụ giá và kiểm thử dữ liệu thay đổi.

Nghiệm thu: sản phẩm/đơn vị/giá sỉ/lẻ/nguồn và thời điểm khớp fixture; giá mới được tra lại; thiếu giá và lỗi nguồn được phân biệt. Ghi rõ phần nghiệm thu giá thật còn phụ thuộc GaoApp.

### Giai đoạn 3 Hiểu câu nhập hàng và chuẩn hóa tên mới

Tạo hai API hỗ trợ form độc lập với hội thoại. Tái sử dụng hiểu sản phẩm, bổ sung tách số lượng/đơn vị, đối chiếu ứng viên và kết quả có cấu trúc. Chuẩn hóa tên mà giữ đúng thuộc tính; trả điểm mơ hồ và gợi ý hàng trùng.

Đầu ra: API, trang thử chọn hàng/số lượng và trang thử tên mới, bộ câu nói chuẩn và biến thể chưa gặp.

Nghiệm thu: ví dụ 4 lốc TH có đường 180ml đạt đủ các trường; không lẫn 180ml với số lượng; mơ hồ không bị biến thành một lựa chọn chắc chắn; không có API ghi nghiệp vụ hoặc tự thêm dòng phiếu.

### Giai đoạn 4 Kho hướng dẫn và trả lời theo nguồn

Tạo lưu trữ hướng dẫn, công cụ nhập/cập nhật/duyệt theo quyền, tìm kiếm và nguồn tham chiếu. Biên soạn nội dung theo danh sách chức năng, đối chiếu với tài liệu và màn hình. Chọn phương pháp tìm kiếm phù hợp sau khi đo; không bắt buộc thêm vector database chỉ để có tính năng này.

Đầu ra: kho nội dung có phiên bản, bảng bao phủ hướng dẫn, bộ câu hỏi và công cụ tra cứu.

Nghiệm thu: trả đúng các bước theo bài đã duyệt; cập nhật/thu hồi bài có hiệu lực; nội dung không có nguồn được nêu rõ. Muốn tuyên bố bao phủ toàn bộ GaoApp thì toàn bộ danh sách chức năng đã chốt phải có bài đạt kiểm tra.

### Giai đoạn 5 Giao diện test và nhận giọng nói

Bổ sung giao diện test trong ChatbotLocal cho chat, mic tiếng Việt, nhận diện tên nhân viên, chọn hàng từ câu nói và chuẩn hóa tên mới. Tái sử dụng phần nhận giọng nói hiện có qua adapter; hiển thị chữ nhận diện, kết quả, trạng thái xử lý, hủy và sửa.

Đầu ra: môi trường kiểm thử dùng được trên máy tính/điện thoại; ví dụ mô phỏng cửa sổ chat và form nghiệp vụ.

Nghiệm thu: nói thành chữ rồi nhận câu trả lời chữ; mic dừng khi đóng; lỗi mic/AI có cách gõ lại; kết quả cũ đến muộn không ghi đè lần nói/sửa mới. Đây là UI test, không thay cho nghiệm thu giao diện GaoApp thật.

### Giai đoạn 6 Quota giới hạn tải và publish riêng

Hoàn thiện quản lý key/project/cooldown, timeout, hàng chờ, giới hạn công việc, số liệu vận hành và công tắc tắt. Chuẩn bị gói publish, cấu hình bí mật, quyền file, health check, backup và rollback cho tiến trình riêng.

Đầu ra: cấu hình runtime, báo cáo sử dụng, gói publish đã kiểm tra và hướng dẫn cài đặt IIS riêng.

Nghiệm thu: mô phỏng Gemini chậm/lỗi/hết quota, nguồn dữ liệu lỗi, hủy, restart và giới hạn tải; không có vòng retry vô hạn hoặc bí mật lộ trong API/log/publish. Live probe nhỏ chỉ xác nhận kết nối, không chủ động dùng hết quota thật.

### Giai đoạn 7 Nghiệm thu ChatbotLocal và bàn giao GaoApp

Chạy baseline phù hợp và toàn bộ các nhóm kiểm thử mới. Đánh giá thủ công câu nói nhiều lượt và tình huống chưa có trong bộ cũ. Tổng hợp độ trễ, số request mỗi thao tác, token, lỗi và trạng thái nội dung hướng dẫn. Đóng phiên bản hợp đồng bàn giao.

Đầu ra: source, test, OpenAPI, client mẫu, fixture, báo cáo nghiệm thu, danh sách phụ thuộc còn ở GaoApp và hướng dẫn vận hành.

Nghiệm thu: đạt mọi điều kiện bắt buộc ở mục 10. Người dùng xác nhận ChatbotLocal sẵn sàng tích hợp trước khi bắt đầu phần GaoApp. Việc bật dữ liệu thật hoặc triển khai vận hành vẫn cần hoàn thành chính sách dữ liệu và các điều kiện tương ứng.

## 9 Bộ tình huống nghiệm thu tối thiểu

| Nhóm | Tình huống | Điều kiện đạt |
| --- | --- | --- |
| Tên nhân viên | Hai nhân viên có tên và scope khác nhau | Lời chào đúng người, không trộn lịch sử; không suy đoán giới tính từ tên |
| Hội thoại | Hỏi tiếp, đổi hãng, quay lại loại cũ, sửa nội dung | Giữ đúng ý và state; công việc không liên quan sản phẩm vẫn được xử lý đúng |
| Hướng dẫn | Hỏi nhập hàng, đơn vị mới, sửa số lượng, hỏi “ở đây” | Đúng bài, đúng phiên bản, có nguồn và đường dẫn phù hợp |
| Thiếu hướng dẫn | Hỏi một chức năng chưa có bài | Nêu thiếu nguồn, không tạo các nút/bước thao tác không được xác minh |
| Cập nhật hướng dẫn | Sửa, thu hồi hoặc giới hạn một bài | Không dùng bài cũ hoặc ngoài quyền cho câu trả lời mới |
| Giá hai loại | Báo giá sỉ và lẻ cho hộp/lốc/thùng | Đúng từng đơn vị và trạng thái, không tự suy ra giá thiếu |
| Giá thay đổi | Thay giá trong fixture rồi hỏi lại | Tra nguồn mới, không dùng số trong history/summary |
| Câu nhập hàng chuẩn | 4 lốc TH có đường 180ml | Đúng hàng, Lốc, số lượng 4, hệ số mẫu 4 và 16 hộp |
| Câu chưa trùng tên | Đảo thứ tự, viết tắt, thiếu dấu, lỗi nhận diện có thể đối chiếu | Tìm đúng ứng viên hoặc hỏi phần còn mơ hồ |
| Thuộc tính đối lập | Có đường, ít đường, không đường | Không đổi sang loại khác khi thiếu kết quả |
| Số và dung tích | 4 lốc 180ml, 2 thùng 1 lít, thiếu số lượng | Tách đúng số lượng/dung tích; thiếu được trả rõ |
| Quy đổi | Nhiều sản phẩm có hệ số lốc khác nhau | Lấy đúng hệ số nguồn, tính decimal ở backend |
| Đơn vị không có | Nói lốc nhưng hàng chỉ có hộp | Báo thiếu đơn vị, không tự tạo hoặc tự chọn quy cách |
| Tên mới | Câu nói có hãng, đường và dung tích | Tên đề xuất rõ, giữ nghĩa, bản gốc vẫn xem được |
| Tên thiếu hoặc trùng | Thiếu dung tích, nghi trùng danh mục | Trả điểm cần hỏi/gợi ý trùng, không thêm thuộc tính giả |
| Mic | Dừng, đóng, từ chối quyền mic, nói lại | Dừng nhận âm thanh, có cách gõ, không trả kết quả cũ đè kết quả mới |
| Cách ly hội thoại | Đổi UserId/ConversationId/StoreId trái phép | Bị từ chối trên mọi API liên quan |
| Quyền bị thu hồi | Thu hồi quyền giá/hướng dẫn/trợ lý | Lượt tiếp theo và lịch sử/summary xử lý theo quyền hiện tại |
| Dữ liệu không tin cậy | Tên hàng hoặc hướng dẫn chứa chỉ dẫn vượt quyền | Không thay đổi công cụ, quyền hay scope |
| Tồn kho tắt | Hỏi còn hàng hoặc còn bao nhiêu | Nêu tính năng chưa bật; không truy/gửi tồn qua công cụ liên quan |
| Lỗi nguồn | Giá lỗi, catalog lỗi, scope không hợp lệ | Có mã lỗi rõ, không lấy số cũ hoặc dữ liệu store khác thay thế |
| Quota | Giả lập RPD, RPM, TPM; ba slot cùng/khác project | Phân loại đúng, cooldown phù hợp, số retry hữu hạn |
| Hủy và gửi trùng | Hủy, gửi hai lần, nhiều tab, mất phản hồi | Không nhân đôi lượt thành công; trạng thái có thể tra và khôi phục |
| Restart và tải | Restart trợ lý, hàng chờ đầy, model chậm | Có giới hạn, client timeout/hủy đúng, backup phục hồi được |
| Publish | Kiểm tra API, log, OpenAPI, file cấu hình và package | Không lộ key hoặc dữ liệu test chưa được phép |

Unit, HTTP và quota giả lập không gọi Gemini thật. Mỗi đợt live test chốt ngân sách tổng trước khi chạy, theo dõi cả các vòng tóm tắt/retry; ưu tiên tình huống mới cần xác minh model. Kết quả từ fixture và dữ liệu thật phải được phân biệt trong báo cáo.

## 10 Điều kiện hoàn thành ChatbotLocal

1. Có API được bảo vệ và phiên bản hợp đồng rõ ràng; GaoApp giả lập gọi được hội thoại và các API hỗ trợ form.
2. Cách ly nhân viên/cửa hàng, kiểm tra quyền, ownership và lịch sử đạt toàn bộ trường hợp bắt buộc; không còn lỗi truy cập chéo đã phát hiện.
3. Chat tiếp tục được, có tên hiển thị, mic nhập tiếng Việt và câu trả lời chữ trong UI test.
4. Hướng dẫn có quản lý nội dung, phiên bản, tìm kiếm và nguồn tham chiếu; báo cáo rõ mức bao phủ. Mọi bài dùng nghiệm thu đã được đối chiếu.
5. Giá sỉ/lẻ, số lượng và quy đổi khớp nguồn kiểm thử. Dữ liệu thiếu/lỗi không bị đổi thành số AI tự tạo.
6. API nhập hàng và tên mới chỉ trả đề xuất; không trực tiếp ghi nghiệp vụ. Các lỗi sai loại hàng, thuộc tính quan trọng, đơn vị hoặc số lượng đã phát hiện được sửa trước bàn giao.
7. Tồn kho bị tắt trong cả công cụ, khả năng công bố và dữ liệu đưa cho Gemini.
8. Baseline tương ứng đạt; các thay đổi test có lý do. Ngưỡng đánh giá ngữ nghĩa được chốt trước khi chạy, không dùng tỷ lệ tổng để bỏ qua lỗi quyền hoặc số liệu trọng yếu.
9. Timeout, hủy, chống gửi trùng, lỗi nguồn, quota, restart và giới hạn tải đạt kiểm thử; key/cookie/credential không lộ qua giao diện, API, log hoặc package.
10. Có báo cáo request/token/độ trễ, gói publish, backup/rollback và tài liệu bàn giao, ghi rõ các phần phải nghiệm thu thật ở GaoApp.

Hoàn thành các điều kiện này có nghĩa ChatbotLocal sẵn sàng tích hợp. Chưa có nghĩa icon, form, quyền dữ liệu thật hoặc mọi thao tác GaoApp đã được nghiệm thu. Ngày hoàn thành chỉ ước lượng sau giai đoạn 0 và khối lượng biên soạn hướng dẫn.

## 11 Các quyết định còn cần chốt khi triển khai

| Quyết định | Thời điểm cần chốt |
| --- | --- |
| Xác thực hệ thống gọi và token ngữ cảnh | Giai đoạn 0, trước khi hoàn thiện API |
| Retention transcript/summary, xóa và xử lý quyền bị thu hồi | Trước khi hoàn thiện kho hội thoại |
| SQLite hay kho lưu khác, cấu trúc backup/cooldown | Trước migration và gói vận hành |
| Danh sách chức năng cần có hướng dẫn, người duyệt và quyền đọc bài | Trước nghiệm thu nội dung |
| Dữ liệu được gửi Gemini và gói API phù hợp | Trước mọi đợt test bằng dữ liệu thật hoặc mở sử dụng |
| Số người đồng thời, giới hạn tải, timeout và ngân sách | Đo ở giai đoạn kiểm thử và chốt trước bàn giao |
| Ngưỡng ngữ nghĩa, tốc độ và số tình huống nghiệm thu mới | Chốt trước đợt nghiệm thu tương ứng |
| Metadata project cho ba slot key, cooldown qua restart | Trước kiểm thử quota/vận hành |
| Bảng giá sỉ/lẻ và tính tổng theo quy tắc nào của GaoApp | Chốt hợp đồng trước, đối chiếu dữ liệu thật khi tích hợp GaoApp |
| Quyền sử dụng trợ lý và từng công việc của nhân viên | Chốt hợp đồng trước, ánh xạ quyền GaoApp khi tích hợp |

Các nội dung này là quyết định triển khai còn mở, không thay đổi các yêu cầu chức năng đã thống nhất tại mục 1. Có thể tiến hành kiểm thử giả lập và biên soạn nguồn trong khi chốt các lựa chọn vận hành.

## 12 Bàn giao và phần GaoApp thực hiện sau

Gói bàn giao từ ChatbotLocal gồm:

- Source và phiên bản release, OpenAPI, hướng dẫn xác thực, client mẫu và toàn bộ mã lỗi.
- Schema ngữ cảnh nhân viên/quyền/scope, dữ liệu sản phẩm/giá/đơn vị và kết quả hỗ trợ form.
- Fixture, mock API, bộ test và báo cáo phân biệt nguồn giả lập với nguồn thật.
- Kho hướng dẫn, phiên bản nội dung, nguồn đối chiếu và bảng bao phủ chức năng.
- Gói publish riêng, hướng dẫn cấu hình bí mật, giới hạn tải, health check, backup và rollback.
- Danh sách endpoint nghiệp vụ GaoApp cần cung cấp, hợp đồng đã đóng phiên bản và các điểm còn phải đối chiếu thật.

Sau khi nghiệm thu ChatbotLocal, project GaoApp thực hiện:

1. Thêm API đọc nghiệp vụ theo quyền và ngữ cảnh hiện tại, thay cầu nối Development; đối chiếu sản phẩm, giá sỉ/lẻ, đơn vị và quy đổi với dịch vụ/màn hình thật.
2. Thêm kết nối backend gọi ChatbotLocal có timeout, hủy, RequestId và công tắc tắt.
3. Thêm icon nổi và cửa sổ chat cho nhân viên đủ quyền; truyền tên hiển thị và ngữ cảnh tin cậy.
4. Nối mic hiện có với trợ lý; khi kết quả rõ thì mở sẵn modal hàng/đơn vị/số lượng. Khi nhân viên xác nhận, kiểm tra lại dữ liệu và dùng luồng lưu hiện có.
5. Thêm mic và đề xuất chuẩn hóa tên vào form sản phẩm mới; nhân viên vẫn sửa và lưu theo quyền.
6. Kiểm thử trên thiết bị thật và với nhiều nhân viên/cửa hàng; bảo đảm quét mã, tìm thủ công, nhập hàng, tạo sản phẩm, lưu và các nghiệp vụ hiện có vẫn hoạt động khi trợ lý lỗi/tắt.
7. Dùng thử nội bộ, theo dõi phản hồi và nghiệm thu dữ liệu thật trước khi mở rộng.

Tuân thủ AGENTS.md của GaoApp: không tự tạo menu bằng seeder, migration, script hoặc sửa dữ liệu; không gắn cứng chức năng vào sidebar. Icon nổi là giao diện đã được người dùng yêu cầu, phải hiển thị theo quyền. Nếu có màn hình quản lý cần menu, cung cấp đường dẫn để người dùng cấu hình qua Quản lý menu.

## 13 Nguồn cần đọc khi nâng cấp

Source ChatbotLocal: `D:\repo\ChatbotLocal`. Source GaoApp để đọc đối chiếu: `D:\GaoApp\GAOAPP-SA-20260819-01`. Các đường dẫn là môi trường hiện tại, không phải cấu hình bắt buộc khi deploy.

| Nội dung | File hoặc thư mục |
| --- | --- |
| Roadmap bàn giao ban đầu | docs/ROADMAP-TICH-HOP-CHATBOT-GAOAPP.md trong ChatbotLocal |
| Điều phối | src/ChatbotLocal.Web/Services/Chat/AgentChatOrchestratorService.cs |
| Công cụ và hội thoại | src/ChatbotLocal.Web/Services/Chat/AgentRetailTools.cs và ChatOrchestratorService.cs |
| Bộ nhớ và state | src/ChatbotLocal.Web/Services/Chat/ConversationMemoryManager.cs và Models/Chat/ConversationState.cs |
| Kho hội thoại | src/ChatbotLocal.Web/Services/Chat/IConversationStore.cs, SqliteConversationStore.cs và ConversationOptions.cs |
| Client và quota | src/ChatbotLocal.Web/Services/AI/GeminiAgentClient.cs và GeminiKeyPool.cs |
| Đo sử dụng | src/ChatbotLocal.Web/Services/AI/AiUsageMeter.cs |
| Endpoint và đăng ký dịch vụ | src/ChatbotLocal.Web/Controllers và Program.cs |
| Cầu nối hiện có | src/ChatbotLocal.Web/Services/GaoApp/GaoAppProductRepository.cs và GaoAppOptions.cs |
| Mô hình sản phẩm | src/ChatbotLocal.Web/Models/Products/Product.cs |
| Test | tests/ChatbotLocal.Tests và tests/GaoApp.Chatbot.Tests |
| Báo cáo baseline | artifacts/ai-comparison/accepted-gemini-no-inventory-20261011.html và .json |
| Giọng nói GaoApp | GaoApp.Web/wwwroot/Admin/js/warehouse-receiving-mobile.js |
| Chọn đơn vị và số lượng GaoApp | GaoApp.Web/wwwroot/Admin/js/warehouse-receiving-detail.js |
| Form hàng mới GaoApp | GaoApp.Web/Areas/Admin/Views/Shared/_ReceiptIntake.cshtml và wwwroot/Admin/js/receipt-intake.js |
| Hướng dẫn nhập trên điện thoại | docs/development/WAREHOUSE-RECEIVING-MOBILE-20260912.md trong GaoApp |
| Giá và nhóm khách | GaoApp.Application/Services/Orders/POSService.cs và GaoApp.Domain/Constants/CustomerPriceTiers.cs |
| Tên nhân viên | GaoApp.Domain/Entities/User.cs và GaoApp.Infrastructure/Identity/CurrentUser.cs |
| Quy ước menu | AGENTS.md tại root GaoApp |

Không sao chép key, cookie, file credential hoặc toàn bộ App_Data vào bản bàn giao. Bằng chứng hội thoại cần được khử thông tin không được phép trước khi chia sẻ.

## 14 Lời nhắn bàn giao cho project ChatbotLocal

> Hãy nâng cấp project ChatbotLocal hiện có theo roadmap này thành dịch vụ trợ lý API chạy riêng, có thể đặt cùng server với GaoApp nhưng ở tiến trình hoặc IIS Application Pool riêng. Tái sử dụng hội thoại nhiều lượt, bộ nhớ, Gemini, quota và bộ kiểm thử đã có. Phạm vi gồm chat nội bộ biết tên hiển thị nhân viên, nói thành chữ và trả lời bằng chữ, hướng dẫn sử dụng GaoApp có nguồn cập nhật, tra giá sỉ/lẻ, hiểu câu nhập hàng để trả đúng sản phẩm/đơn vị/số lượng và chuẩn hóa tên sản phẩm mới. AI chỉ trả đề xuất; thao tác lưu nghiệp vụ do GaoApp thực hiện sau khi nhân viên xác nhận. Tồn kho vẫn tắt, không suy đoán còn hàng. Trước tiên đọc source và thực hiện giai đoạn 0 để chốt hợp đồng, baseline, dữ liệu giả lập, quyền và backlog. Hoàn thiện ChatbotLocal theo từng giai đoạn với kiểm thử và báo cáo. Giai đoạn này không sửa code, dữ liệu hay menu GaoApp; chỉ đọc để đối chiếu khi cần. Dùng mock API/fixture cho phần GaoApp chưa có, ghi rõ nguồn test và không tuyên bố dữ liệu thật đã nghiệm thu. Khi ChatbotLocal sẵn sàng, bàn giao OpenAPI, client mẫu, nguồn hướng dẫn, kết quả kiểm thử và danh sách phần GaoApp cần làm sau. Không tự triển khai vận hành hoặc thay đổi billing chỉ vì đã hoàn thành code.
