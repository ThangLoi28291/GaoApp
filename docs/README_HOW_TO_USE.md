# Cách dùng bộ tài liệu AI cho GaoApp

Bộ file này được tạo từ source zip `GaoApp(14).zip` ngày 2026-06-09.

Mục đích: giúp mỗi lần mở chat mới, bạn không cần nhắc lại toàn bộ dự án. Bạn chỉ gửi vài file `.md` + file code đang sửa.

## Cách chép vào project

Chép nguyên thư mục `docs` vào ngang hàng với các project:

```txt
GaoApp/
├── GaoApp.Domain/
├── GaoApp.Application/
├── GaoApp.Infrastructure/
├── GaoApp.Web/
└── docs/
```

Sau đó commit Git:

```bash
git add docs
git commit -m "Add AI documentation for GaoApp"
```

## Mỗi lần mở chat mới gửi gì?

Trường hợp sửa POS / Promotion:

```txt
00_AI_CONTEXT.md
modules/POS.md
modules/PROMOTION.md
CHANGELOG_AI.md
OrderService.cs hoặc POSService.cs hoặc file JS/View liên quan
```

Trường hợp sửa Kho:

```txt
00_AI_CONTEXT.md
modules/INVENTORY.md
CHANGELOG_AI.md
StockDocumentService.cs hoặc InventoryMovementService.cs
```

Trường hợp sửa sản phẩm / barcode:

```txt
00_AI_CONTEXT.md
modules/PRODUCT.md
CHANGELOG_AI.md
ProductService.cs hoặc BarcodeLookupService.cs
```

## Sau khi làm xong một chức năng

Không tạo file `.md` mới. Chỉ cập nhật:

1. `CHANGELOG_AI.md`: ghi đã làm gì, file nào đã sửa, còn cần test gì.
2. File module liên quan, ví dụ `modules/PROMOTION.md`: cập nhật trạng thái hoặc lưu ý nghiệp vụ mới.

## Lưu ý

- File `.md` không thay thế code.
- File `.md` là bản tóm tắt để AI hiểu nhanh dự án.
- Khi lỗi nằm trong hàm cụ thể, vẫn phải gửi file code hiện tại.
