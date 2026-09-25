# API quản lý hình ảnh

API dùng cùng domain/subdomain cửa hàng và phiên đăng nhập bằng cookie của trang quản trị. Không truyền `storeId`; cửa hàng được xác định từ tenant của request. Chưa bổ sung xác thực Bearer token cho các endpoint này.

## Endpoint và quyền

| Method | URL | Chức năng | Quyền |
|---|---|---|---|
| GET | `/admin/media-library/data` | Danh sách, thống kê, phân trang, token CSRF | `catalog.product.view` |
| GET | `/admin/media-library/{id}` | Chi tiết một MediaAsset | `catalog.product.view` |
| GET | `/admin/media-library/{id}/preview` | Nội dung ảnh | `catalog.product.view` |
| GET | `/admin/media-library/policy` | Chính sách dọn và lượt xử lý gần nhất | `catalog.product.view` |
| POST | `/admin/media-library/cleanup?afterId=0` | Kiểm tra/dọn một lô của cửa hàng | View và `catalog.product.delete` |
| POST | `/admin/media-library/{id}/cleanup` | Kiểm tra/lên lịch/dọn một ảnh | View và `catalog.product.delete` |
| POST | `/admin/media-library/{id}/cancel-temp` | Hủy ảnh tải tạm theo ID | View và `catalog.product.delete` |
| POST | `/admin/media/temp` | Tải một ảnh tạm | Một trong `catalog.product.create`, `catalog.product.update`, `catalog.productvariant.update` |
| DELETE | `/admin/media/temp` | Hủy ảnh tải tạm theo token upload | Cùng quyền upload |

`id` là ID MediaAsset, không phải ID ProductImage. `/admin/media-library` là trang HTML; dùng `/data` để lấy JSON.

Mọi POST/DELETE cần cookie phiên đăng nhập, cookie antiforgery và header `RequestVerificationToken`. Client có quyền View lấy giá trị header từ `requestVerificationToken` của GET `/data`; response này đồng thời cấp cookie antiforgery khi cần. Client chỉ có quyền upload sử dụng token từ biểu mẫu quản trị được phép truy cập. API đọc dữ liệu quản lý không trả đường dẫn lưu trữ hoặc token upload.

## Danh sách và chi tiết

Ví dụ: `GET /admin/media-library/data?search=gao&statusFilter=unused&page=1`.

- `search`: tên file chứa từ khóa; trim và giới hạn 150 ký tự.
- `statusFilter`: `all` (mặc định), `used`, `temp`, `unused`, `waiting`, `ready`, `deleted`. Giá trị không hợp lệ được xử lý như `all`.
- `page`: mặc định 1, tự giới hạn trong phạm vi trang hiện có; mỗi trang 24 ảnh, mới nhất trước. Khi rỗng vẫn trả `page=1`, `pages=1`.
- `summary` thống kê toàn bộ cửa hàng, không bị ảnh hưởng bởi tìm kiếm/lọc. Dung lượng lấy từ metadata, đơn vị byte; không phải phép đo ổ đĩa.

Ví dụ response (ID, thời gian và token minh họa):

```json
{
  "items": [
    {
      "id": 123,
      "name": "gao.png",
      "sizeBytes": 20480,
      "createdAtUtc": "2026-09-11T01:00:00Z",
      "expireAtUtc": null,
      "isTemp": false,
      "isDeleted": false,
      "used": false,
      "status": "unused",
      "previewUrl": "/admin/media-library/123/preview",
      "products": []
    }
  ],
  "summary": {
    "total": 1, "used": 0, "temporary": 0, "waiting": 1,
    "ready": 0, "bytes": 20480, "readyBytes": 0
  },
  "search": "gao",
  "status": "unused",
  "page": 1,
  "pages": 1,
  "pageSize": 24,
  "filteredCount": 1,
  "canManage": true,
  "requestVerificationToken": "TOKEN_FROM_SERVER"
}
```

GET `/{id}` trả trực tiếp object cùng cấu trúc một phần tử `items`. `products` chứa `{ id, name, url }` của sản phẩm trong cửa hàng đang liên kết qua ảnh/biến thể. Tham chiếu URL trong nội dung hoặc quảng cáo vẫn được bảo vệ dù không có phần tử trong `products`. Các trường thời gian `...Utc` dùng UTC.

| Trạng thái | Ý nghĩa |
|---|---|
| `used` | Có tham chiếu đang sử dụng; được giữ lại |
| `temp` | Ảnh tạm còn trong thời gian chờ |
| `unused` | Ảnh đã lưu nhưng không còn dùng, chưa bắt đầu thời gian giữ |
| `waiting` | Đã bắt đầu thời gian giữ ảnh không dùng |
| `ready` | Đến hạn kiểm tra để dọn, bao gồm yêu cầu xóa đang chờ thử lại |
| `deleted` | Đã hoàn tất dọn file; giữ metadata lịch sử, `previewUrl=null` |

`all` không gồm `deleted`. `summary.waiting` cộng cả `unused` và `waiting`; `summary.total`/`bytes` không gồm ảnh đã dọn. Dùng `status`, không chỉ `isDeleted`, để xác định việc dọn file đã hoàn tất: dấu xóa được ghi trước thao tác file.

## Dọn ảnh và hủy upload

POST `/{id}/cleanup` không cần body, trả một trong:

```json
{ "outcome": "Scheduled" }
```

- `Kept`: giữ lại vì đang dùng, còn thời gian chờ, đã dọn hoặc chưa đủ điều kiện.
- `Scheduled`: bắt đầu thời gian giữ ảnh không dùng.
- `Deleted`: hoàn tất xóa file, giữ metadata lịch sử.
- `Failed`: chưa xóa được file; yêu cầu được giữ để thử lại. HTTP vẫn là 200, client phải kiểm tra `outcome`.

Mặc định ảnh tạm có hạn 6 giờ; ảnh đã lưu được giữ thêm 7 ngày **kể từ lúc phát hiện không dùng**. Ảnh đang dùng luôn được kiểm tra lại trước khi dọn. API không có thao tác bỏ qua thời gian chờ hoặc cưỡng chế xóa ảnh đang dùng.

POST `/cleanup?afterId=0` không cần body, trả:

```json
{
  "result": { "scanned": 100, "scheduled": 5, "deleted": 20, "failed": 0, "kept": 75, "lastId": 456 },
  "hasMore": true
}
```

Khi `hasMore=true`, gọi tiếp với `afterId=result.lastId`. Dừng khi `hasMore=false`; có thể có một lô rỗng cuối cùng. Cộng các số liệu `result` để có tổng cả đợt; `failed>0` cần hiển thị cho người dùng. Mỗi đợt mới bắt đầu lại `afterId=0`. Chỉ dọn các file đã đăng ký trong MediaAssets thuộc vùng ảnh sản phẩm/ảnh tạm, không quét file rời trên ổ đĩa.

POST `/{id}/cancel-temp` trả 200 body rỗng: thu hồi token và đặt hạn dọn về hiện tại, file sẽ được dọn ở lượt quét tiếp theo. Chỉ áp dụng cho upload tạm còn token, chưa bị xóa, chưa được dùng. Gọi lại hoặc dùng ID không phù hợp trả 404.

POST `/admin/media/temp`: `multipart/form-data`, trường `file`; ảnh JPG/JPEG/PNG/GIF/WebP, tối đa 10 MiB, kiểm tra định dạng thực. Thành công trả **chuỗi token dạng text**, không phải JSON. Lưu token này vào luồng tạo/sửa sản phẩm hiện có để gắn ảnh. DELETE cùng URL gửi token dưới dạng body `text/plain`, trả 200/404 và lên lịch dọn như thao tác hủy theo ID.

## Chính sách và lỗi

GET `/policy` trả `{ options, lastRun }`. `options` gồm `enabled`, `tempLifetimeHours`, `unusedRetentionDays`, `intervalMinutes`, `batchSize` (mặc định true, 6, 7, 60, 100). `lastRun` có thể null; khi có gồm `atUtc` và `result` của lượt xử lý được ghi gần nhất trong bộ nhớ tiến trình. Khởi động lại làm mất thống kê này, không mất deadline trong DB. Đây là API đọc; thay đổi chính sách qua cấu hình `MediaCleanup`, không có endpoint ghi cấu hình.

Worker chạy khi ứng dụng đang hoạt động, bắt đầu sau một phút rồi theo chu kỳ; `enabled=false` chỉ tắt worker, không tắt API dọn thủ công.

- Gửi `Accept: application/json` để dùng API theo phiên đăng nhập; phiên hết hạn cần đăng nhập lại.
- 400: không xác định được cửa hàng, thiếu/sai antiforgery, hoặc dữ liệu upload không hợp lệ.
- 403: thiếu quyền.
- 404: ID không thuộc cửa hàng/không tồn tại, preview đã mất, hoặc không đủ điều kiện hủy ảnh tạm.
- Lỗi ngoài các trường hợp trên có thể trả 5xx. Client kiểm tra HTTP status trước khi parse JSON; các response lỗi không đảm bảo là JSON.

## Ví dụ JavaScript cùng domain đã đăng nhập

```javascript
async function readJson(response) {
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response.json();
}

const library = await readJson(await fetch('/admin/media-library/data?statusFilter=ready', {
  credentials: 'same-origin',
  headers: { Accept: 'application/json' }
}));

// Gọi hàm này khi người quản lý yêu cầu dọn.
async function cleanupImage(id) {
  if (!library.canManage) throw new Error('Không có quyền dọn ảnh');
  const result = await readJson(await fetch(`/admin/media-library/${id}/cleanup`, {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      RequestVerificationToken: library.requestVerificationToken
    }
  }));
  if (result.outcome === 'Failed') throw new Error('Chưa dọn được ảnh; sẽ thử lại');
  return result.outcome;
}
```

Các thay đổi cần được build/publish và khởi động bản ứng dụng mới để endpoint có hiệu lực; không cần migration schema.

Kiểm tra local ngày 11/09/2026: build Release thành công, 28/28 kiểm thử nhóm Media và HostStorage đạt. Kiểm thử HTTP chạy ứng dụng/SQL Server thật với dữ liệu fixture: danh sách/chi tiết/chính sách JSON, UTC, không lộ đường dẫn/token upload, phạm vi cửa hàng, quyền, CSRF lấy từ API và luồng hủy/dọn. Kết quả: `Logs/media-library-results/media-api.trx`. Chưa deploy bản thay đổi này.

Bản sửa timeout tiếp theo: 29/29 kiểm thử đạt, bổ sung fixture hơn 16.000 ảnh/32.000 sản phẩm. Truy vấn danh sách trên dữ liệu local 16.216 ảnh giảm từ timeout 30 giây xuống 351 ms (thời gian service, không gồm HTTP/tải ảnh). Hợp đồng API giữ nguyên. Chi tiết tại [ghi chú sửa timeout](development/MEDIA-LIBRARY-20260911.md#sửa-timeout-trên-dữ-liệu-cửa-hàng-lớn); bản local đã được build và khởi động lại.
