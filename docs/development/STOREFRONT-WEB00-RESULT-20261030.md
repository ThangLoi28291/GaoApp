# WEB00 — Kết quả thực hiện và kiểm tra

Ngày: 30/10/2026. Yêu cầu: “Làm web00, chạy test độc lập khi hoàn thành, chạy review độc lập xong báo kết quả”.

## Trạng thái

**WEB00 hoàn thành.** Đã lập [hợp đồng WEB00](STOREFRONT-WEB00-CONTRACT-20261030.md), policy thuần và hoàn tất W00T01–W00T05. Kiểm thử độc lập và review kỹ thuật đạt sau sửa. Chủ cửa hàng đã trả lời “Làm theo cách 1”: `StoreServiceFee` cho cả giao gần và toàn quốc, chi phí trả shipper/hãng ghi riêng. Hợp đồng revision 3 ghi quyết định này; không đổi policy hoặc harness đã kiểm thử. WEB01 chưa bắt đầu.

Không có endpoint, schema/migration, cấu hình vận hành hay menu mới. Không chạy Web, truy cập SQL, ngân hàng, ghi tiền/kho hoặc triển khai. Những thay đổi có sẵn ngoài phạm vi này được giữ nguyên. Không commit/push/merge, review dựa trên bytes working tree thay vì khẳng định một SHA commit đã được kiểm tra.

## Đầu ra

- [Hợp đồng kiến trúc, dữ liệu, trạng thái, tiền/kho, giao diện và cổng tích hợp](STOREFRONT-WEB00-CONTRACT-20261030.md).
- [Quy tắc Domain](../../GaoApp.Domain/Storefront/StorefrontContracts.cs): sáu file, chỉ quyết định và trả hiệu ứng; không có I/O.
- [Bộ test độc lập](../../tools/validation/storefront-web00/README.md): console .NET 10, không package/project reference, không startup ứng dụng.
- [Kế hoạch đã cập nhật trạng thái](STOREFRONT-IMPLEMENTATION-PLAN-20261030.md).

## Kiểm thử độc lập

Người kiểm thử: agent `web00_tests`, tách khỏi người triển khai policy. Oracle chuẩn bị từ yêu cầu trước khi đọc API. Chỉ người kiểm thử sửa harness; người triển khai không sửa kỳ vọng để ép đạt.

Lệnh từ thư mục gốc repository:

```powershell
dotnet run --project tools/validation/storefront-web00/Storefront.Web00.Validation.csproj -- --results .artifacts/storefront-web00
```

SDK 10.0.403; runtime thực chạy 10.0.14. `UseAppHost=false`, NuGet sources rỗng và output riêng chỉ áp dụng cho harness. Lượt cuối sau các bản sửa review đạt **378/378, 0 lỗi**. Các kết quả từng case ở `.artifacts/storefront-web00/results.json`; [bản tóm tắt lưu trong repository](storefront-web00/validation-summary.json) giữ lệnh, số lượng, môi trường và SHA256 của sáu policy cùng năm file harness. Đã đối chiếu khớp hash sau chạy.

| Nhóm | Đạt / đã chạy |
|---|---:|
| Workflow, pha, quyền, guest và phiên bản | 191/191 |
| QR và phân bổ khoản thu | 54/54 |
| Phí ship và trọng lượng | 49/49 |
| Kho âm, giữ, xuất và trả | 36/36 |
| Phân loại phí, COD, hoàn tiền | 33/33 |
| Giới hạn tiền và số lượng | 15/15 |

Phạm vi test: ma trận pha/lệnh/quyền/guest/phiên bản, QR đúng/sai số/trùng/muộn, kg và ngưỡng miễn ship, giữ/xuất/nhận trả với kho âm, phân loại phí, COD tiền còn ở người giao và net remittance, ngân sách hoàn thực trả, biên số lượng/số tiền.

Không đại diện cho SQL transaction/idempotency/lock thật, chữ ký ACB, HTTP authorization, ghi sổ tiền/kho, POS regression, migration, hóa đơn, giá/promotion/publication hoặc UI điện thoại. Các cổng này đã phân việc WEB01–WEB09 trong hợp đồng.

## Review độc lập

Reviewer: agent `web00_review`, độc lập với người triển khai và `web00_tests`. Reviewer đọc mã policy, harness và hợp đồng, đối chiếu nguồn POS/ACB/giao hàng/kho/báo cáo/giá hiện tại. Hai phát hiện tiền đã được sửa và bổ sung test:

1. Receipt sai số trước đó chưa xử lý có thể bị receipt mới đúng số làm mất nghĩa Review trong policy. `Evaluate` nay bắt buộc nhận cờ review còn treo, không tự mở khóa; chuỗi thiếu/thừa rồi nhận đúng được kiểm thử.
2. Output thu hộ hãng còn mang tên/giá trị chi phí cửa hàng. Nay tách phần thanh toán nghĩa vụ hãng và chênh lệch; chi phí cửa hàng bằng 0 trong chế độ thu hộ; lệch hai chiều yêu cầu review.

Đã làm rõ thêm dispatch đa nguồn nguyên tử và projection OR/AND, định danh ngân hàng đăng ký riêng ngoài POS cùng giới hạn transport. **Kết luận review revision 2: đạt kỹ thuật, không còn phát hiện chặn trong phạm vi đã rà.** Reviewer tự chạy lại toàn bộ **378/378, 0 lỗi, exit 0**, output riêng `.artifacts/storefront-web00/reviewer-run/results.json`. Hash sáu policy khớp, nguồn và test không đổi trước/sau lượt reviewer; [tóm tắt review](storefront-web00/review-summary.json) lưu bằng chứng. Không cộng hai lượt thành 756 tình huống khác nhau.

Kết quả review giữ nguyên trạng thái tại thời điểm chạy (khi quyết định phí còn chờ). [Quyết định đóng WEB00](storefront-web00/business-decision.json) ghi xác nhận sau đó của chủ cửa hàng. Lần cập nhật revision 3 chỉ sửa tài liệu/trạng thái theo lựa chọn đã có test; đối chiếu hash policy/harness để dùng lại bằng chứng test, không tuyên bố đã chạy một lượt test mới.

## Việc chuyển sang bước sau

Không còn quyết định chặn WEB00. WEB01 cần lưu StoreServiceFee đã chốt trong cấu hình. Bản banner giữa là baseline để tiếp tục UI, chưa có xác nhận cuối sau sửa. Các giá trị thật của vùng giao/phí, hạn QR, nhận diện và ánh xạ tài khoản/chủ thể sẽ nhập trong cấu hình và phải đủ trước mở bán; số test không trở thành dữ liệu cửa hàng.
