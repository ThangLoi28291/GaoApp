(function () {
    'use strict';

    const groups = {
        receipt: ['Thông tin phiếu', 'bx-file'], goods: ['Hàng nhập', 'bx-package'],
        approval: ['Duyệt phiếu', 'bx-check-shield'], invoice: ['Hóa đơn', 'bx-receipt']
    };
    // Values follow PurchaseReceiptAuditEventType. Unknown future events retain their evidence.
    const events = [null,
        ['Tạo phiếu nhập', 'receipt'], ['Cập nhật thông tin phiếu', 'receipt'],
        ['Thêm hàng vào phiếu', 'goods'], ['Điều chỉnh hàng nhập', 'goods'], ['Xóa hàng khỏi phiếu', 'goods'],
        ['Gửi phiếu chờ duyệt', 'approval'], ['Đề nghị sửa phiếu', 'approval'],
        ['Trả phiếu về chỉnh sửa', 'approval'], ['Bỏ qua đề nghị sửa', 'approval'], ['Từ chối phiếu nhập', 'approval'],
        ['Chốt giá và xác nhận nhập kho', 'approval'], ['Xác nhận nhập kho', 'approval'],
        ['Gắn hóa đơn đầu vào', 'invoice'], ['Gỡ hóa đơn đầu vào', 'invoice'],
        ['Tách phiếu nhập', 'receipt'], ['Tạo phiếu từ thao tác tách', 'receipt'],
        ['Không thể gắn hóa đơn: sai chủ thể', 'invoice'], ['Không thể xác nhận: chủ thể hóa đơn chưa hợp lệ', 'invoice'],
        ['Xác nhận sản phẩm tương ứng trên hóa đơn', 'invoice'], ['Tự động ghép sản phẩm trên hóa đơn', 'invoice'],
        ['Chấp nhận chênh lệch đối chiếu hóa đơn', 'invoice'], ['Hủy hiệu lực chấp nhận chênh lệch', 'invoice'],
        ['Bỏ qua dòng hóa đơn khi đối chiếu', 'invoice'], ['Đưa dòng hóa đơn vào đối chiếu lại', 'invoice'],
        ['Loại dòng hàng khỏi đối chiếu', 'invoice'], ['Đưa dòng hàng vào đối chiếu lại', 'invoice'],
        ['Cập nhật trạng thái đối chiếu hóa đơn', 'invoice'], ['Gắn lại hóa đơn đầu vào', 'invoice'],
        ['Bắt đầu nhận hàng', 'goods'], ['Tiếp tục nhận hàng', 'goods'], ['Tiếp quản phiên nhận hàng', 'goods'],
        ['Ghi nhận thao tác nhận hàng', 'goods'], ['Hoàn tác nhận hàng', 'goods'], ['Kết thúc nhận hàng', 'goods'],
        ['Chấp nhận hàng ngoài đơn đặt', 'approval'], ['Từ chối hàng ngoài đơn đặt', 'approval'],
        ['Ghi nhận hàng mới / quy cách mới', 'goods'], ['Cập nhật hàng chờ duyệt', 'goods'],
        ['Gỡ hàng chờ duyệt khỏi phiếu', 'goods'], ['Duyệt hàng vào sản phẩm có sẵn', 'approval'],
        ['Duyệt tạo sản phẩm mới', 'approval'], ['Chưa thể xác nhận: còn hàng chưa xử lý', 'approval'],
        ['Lưu mã vạch vào danh mục', 'goods'], ['Không lưu mã vạch vào danh mục', 'goods'],
        ['Khôi phục hàng chờ duyệt', 'goods'],
        ['Đổi tên phiếu nhập', 'receipt'], ['Yêu cầu đổi tên phiếu nhập', 'approval'],
        ['Duyệt đổi tên phiếu nhập', 'approval'], ['Từ chối đổi tên phiếu nhập', 'approval'],
        ['Xóa phiếu nháp chưa gửi duyệt', 'receipt'],
        ['Kết thúc chờ hóa đơn', 'invoice'], ['Xác nhận đã kiểm tra hóa đơn', 'invoice']
    ];
    const labels = {
        Quantity: 'Số lượng nhập', BaseQuantity: 'Số lượng quy đổi', Factor: 'Hệ số quy đổi',
        ProposedFactor: 'Hệ số quy đổi đề xuất', NameSnapshot: 'Tên hàng ghi nhận', UnitNameSnapshot: 'Đơn vị nhận',
        ProposedBaseUnitName: 'Đơn vị gốc đề xuất', RawBarcodeSnapshot: 'Mã vạch ghi nhận', Barcode: 'Mã vạch',
        Status: 'Trạng thái', Note: 'Ghi chú', ApprovalNote: 'Ghi chú duyệt / trả về',
        WaitForInputInvoice: 'Chờ nhà cung cấp gửi hóa đơn',
        InvoiceFollowUp: 'Theo dõi hóa đơn', MapId: 'Liên kết hóa đơn', EvidenceFingerprint: 'Dấu kiểm tra số liệu',
        WarehouseId: 'Kho nhập', SupplierId: 'Nhà cung cấp', ReceiptSource: 'Nguồn nhập', PurchaseOrderId: 'Đơn đặt hàng',
        DocumentDate: 'Ngày phiếu', DocumentTitle: 'Tên phiếu', RequestId: 'Mã yêu cầu', IsDeleted: 'Đã xóa', DirectReceiptReason: 'Lý do nhập trực tiếp',
        HasVat: 'Có VAT', SubtotalBeforeVat: 'Tiền hàng trước VAT', VatAmount: 'Tiền VAT',
        IncludeVatInInventoryCost: 'Tính VAT vào giá vốn', HasFreight: 'Có phí vận chuyển',
        CapitalizeFreightInInventoryCost: 'Tính vận chuyển vào giá vốn', FreightTotal: 'Phí vận chuyển',
        FreightPayeeName: 'Người nhận tiền vận chuyển', FreightNote: 'Ghi chú vận chuyển', IsFreightPaid: 'Đã trả phí vận chuyển',
        IsMerchandisePaid: 'Đã thanh toán tiền hàng', MerchandisePayeeName: 'Người nhận tiền hàng', TotalAmount: 'Tổng tiền',
        HasRevisionRequest: 'Có đề nghị sửa phiếu', RevisionRequestNote: 'Nội dung đề nghị sửa',
        RevisionRequestedAtUtc: 'Thời điểm đề nghị sửa', RevisionRequestedByUserId: 'Người đề nghị sửa',
        RevisionResolvedAtUtc: 'Thời điểm xử lý đề nghị', RevisionResolvedByUserId: 'Người xử lý đề nghị',
        SubmittedAtUtc: 'Thời điểm gửi duyệt', SubmittedByUserId: 'Người gửi duyệt',
        ApprovedAtUtc: 'Thời điểm duyệt', ApprovedByUserId: 'Người duyệt',
        ConfirmedAtUtc: 'Thời điểm xác nhận', ConfirmedByUserId: 'Người xác nhận', ConfirmedLegalEntityId: 'Chủ thể xác nhận',
        ProductVariantId: 'Sản phẩm', PurchaseOrderLineId: 'Dòng đơn đặt hàng', ProductUnitConversionId: 'Quy cách sản phẩm',
        UnitId: 'Đơn vị', UnitPriceBeforeVat: 'Đơn giá trước VAT', TaxId: 'Loại thuế', TaxRate: 'Thuế suất',
        UnitPriceAfterVat: 'Đơn giá sau VAT', UnitCost: 'Giá nhập', LineTotal: 'Thành tiền', FreightAllocation: 'Vận chuyển phân bổ',
        ShortageDisposition: 'Cách xử lý hàng thiếu', ShortageReason: 'Lý do thiếu hàng',
        ResolvedStockDocumentLineId: 'Dòng hàng sau khi duyệt', ProposedProductVariantId: 'Sản phẩm đề xuất',
        ProposedBaseUnitId: 'Đơn vị gốc đề xuất', ProposedCategoryId: 'Danh mục đề xuất',
        InputInvoiceHeadId: 'Hóa đơn đầu vào', InputInvoiceId: 'Hóa đơn đầu vào', InputInvoiceDetailId: 'Dòng hóa đơn',
        ConfirmedFactor: 'Hệ số đã xác nhận', ConfirmedBaseUnitId: 'Đơn vị gốc đã xác nhận',
        BuyerTaxCode: 'Mã số thuế bên mua', NormalizedBuyerTaxCode: 'Mã số thuế bên mua đã chuẩn hóa',
        NormalizedSellerTaxCode: 'Mã số thuế bên bán đã chuẩn hóa', ResolutionStatus: 'Trạng thái xác định chủ thể',
        SourceReceiptId: 'Phiếu nhập nguồn', SourceDocumentNo: 'Số phiếu nguồn', ResultReceiptId: 'Phiếu nhập tạo ra',
        ResultDocumentNo: 'Số phiếu tạo ra', ResultCount: 'Số phiếu sau khi tách',
        ReceivingSessionState: 'Trạng thái phiên nhận hàng', ReceiptAllocationKind: 'Nguồn dòng hàng',
        OutsidePoDecisionStatus: 'Duyệt hàng ngoài đơn', ReconciliationState: 'Trạng thái đối chiếu',
        ExclusionReason: 'Lý do loại khỏi đối chiếu', Reason: 'Lý do', QuantityDifference: 'Chênh lệch số lượng',
        AmountDifference: 'Chênh lệch tiền', ProductName: 'Tên sản phẩm', UnitName: 'Đơn vị', InvoiceNumber: 'Số hóa đơn'
    };
    const preferred = ['NameSnapshot', 'Quantity', 'UnitNameSnapshot', 'Factor', 'ProposedFactor', 'BaseQuantity',
        'Status', 'UnitCost', 'UnitPriceBeforeVat', 'TotalAmount', 'Note', 'ApprovalNote', 'DirectReceiptReason'];
    const documentStates = {1:'Nháp', 2:'Chờ duyệt', 3:'Đã xác nhận nhập kho', 4:'Trả về chỉnh sửa', 5:'Đã hủy',
        Draft:'Nháp', PendingApproval:'Chờ duyệt', Confirmed:'Đã xác nhận nhập kho', Rejected:'Trả về chỉnh sửa', Cancelled:'Đã hủy'};
    const provisionalStates = {0:'Chờ xử lý', 1:'Đã xử lý', 2:'Đã gỡ khỏi phiếu', Unresolved:'Chờ xử lý', Resolved:'Đã xử lý', Removed:'Đã gỡ khỏi phiếu'};
    const sourceStates = {0:'Nhập trực tiếp (cũ)', 1:'Nhập trực tiếp', 2:'Theo đơn đặt hàng',
        LegacyDirect:'Nhập trực tiếp (cũ)', Direct:'Nhập trực tiếp', PurchaseOrder:'Theo đơn đặt hàng'};
    const fieldStates = {
        InvoiceFollowUp: { NeedsReview: 'Cần kiểm tra hóa đơn', Reviewed: 'Đã kiểm tra hóa đơn' },
        ReceivingSessionState: {0:'Chưa bắt đầu', 1:'Đang nhận hàng', 2:'Đã khóa nhận hàng', 3:'Đã kết thúc',
            None:'Chưa bắt đầu', Active:'Đang nhận hàng', Frozen:'Đã khóa nhận hàng', Closed:'Đã kết thúc'},
        ReceiptAllocationKind: {0:'Nhập trực tiếp', 1:'Theo đơn đặt hàng', 2:'Ngoài đơn đặt hàng',
            Direct:'Nhập trực tiếp', PurchaseOrder:'Theo đơn đặt hàng', OutsidePo:'Ngoài đơn đặt hàng'},
        OutsidePoDecisionStatus: {0:'Không áp dụng', 1:'Chờ duyệt', 2:'Đã chấp nhận', 3:'Đã từ chối',
            NotApplicable:'Không áp dụng', Pending:'Chờ duyệt', Accepted:'Đã chấp nhận', Rejected:'Đã từ chối'},
        ReconciliationState: {0:'Không áp dụng', 1:'Chưa đầy đủ', 2:'Khớp', 3:'Có chênh lệch', 4:'Đã chấp nhận chênh lệch',
            NotApplicable:'Không áp dụng', Incomplete:'Chưa đầy đủ', Matched:'Khớp', Mismatch:'Có chênh lệch', AcceptedMismatch:'Đã chấp nhận chênh lệch'}
    };
    const number = new Intl.NumberFormat('vi-VN', {maximumFractionDigits: 6});
    const own = (object, key) => Object.prototype.hasOwnProperty.call(object, key);
    const text = (tag, className, value) => {
        const element = document.createElement(tag);
        element.className = className || '';
        if (value !== undefined) element.textContent = value;
        return element;
    };
    const normalize = value => String(value ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase();
    function parseObject(raw) {
        try { const value = JSON.parse(raw || '{}'); return value && !Array.isArray(value) && typeof value === 'object' ? value : {}; }
        catch { return {}; }
    }
    function date(value, utc = true) {
        if (!value) return null;
        const raw = String(value);
        // SQL datetime2 is serialized without a zone; OccurredAtUtc still represents UTC.
        const result = new Date(utc && !/(Z|[+-]\d{2}:?\d{2})$/i.test(raw) ? raw + 'Z' : raw);
        return Number.isNaN(result.getTime()) ? null : result;
    }
    function format(key, value, model) {
        if (value === null || value === '') return 'Chưa có';
        if (typeof value === 'boolean') return value ? 'Có' : 'Không';
        if (key === 'Status') return (model.type >= 37 && model.type <= 45 ? provisionalStates : documentStates)[value] ?? `Trạng thái ${value}`;
        if (key === 'ReceiptSource') return sourceStates[value] ?? String(value);
        if (own(fieldStates, key)) return fieldStates[key][value] ?? String(value);
        if (/AtUtc$/.test(key) || key === 'DocumentDate') return date(value, key !== 'DocumentDate')?.toLocaleString('vi-VN') ?? String(value);
        if (typeof value === 'object') return 'Xem dữ liệu gốc trong chi tiết kỹ thuật';
        if (/Id$/.test(key)) return `Mã #${value}`;
        return typeof value === 'number' ? number.format(value) : String(value);
    }
    function model(item) {
        const type = Number(item.eventType), definition = events[type] || ['Thao tác khác', 'receipt'];
        const before = parseObject(item.oldValuesJson), after = parseObject(item.newValuesJson);
        const keys = [...new Set([...Object.keys(before), ...Object.keys(after)])];
        const fields = keys.filter(key => own(labels, key) && (!own(before, key) || !own(after, key) || JSON.stringify(before[key]) !== JSON.stringify(after[key])))
            .sort((a, b) => (preferred.includes(a) ? preferred.indexOf(a) : 100) - (preferred.includes(b) ? preferred.indexOf(b) : 100));
        const result = {item, type, title: definition[0], group: definition[1], before, after, fields,
            occurred: date(item.occurredAtUtc), actor: item.actorUserName || (item.actorUserId ? `Nhân viên #${item.actorUserId}` : 'Hệ thống')};
        result.search = normalize([result.title, groups[result.group][0], result.actor, item.reason, item.note,
            item.stockDocumentLineId ? `dòng ${item.stockDocumentLineId}` : '',
            ...keys.flatMap(key => [labels[key] || key, format(key, before[key] ?? null, result), format(key, after[key] ?? null, result)])].join(' '));
        return result;
    }
    function valueCell(model, key, side) {
        return own(model[side], key) ? format(key, model[side][key], model) : 'Không được lưu';
    }
    function change(model, key) {
        const row = text('div', 'rh-change');
        row.append(text('span', 'rh-label', labels[key]));
        const values = text('span', 'rh-values');
        if (own(model.before, key) && own(model.after, key)) {
            values.append(text('span', 'rh-before', valueCell(model, key, 'before')),
                text('span', 'rh-arrow', '→'), text('strong', 'rh-after', valueCell(model, key, 'after')));
        } else {
            values.append(text('span', 'rh-value-prefix', own(model.after, key) ? 'Ghi nhận: ' : 'Trước thao tác: '),
                text('strong', 'rh-after', valueCell(model, key, own(model.after, key) ? 'after' : 'before')));
        }
        row.append(values);
        return row;
    }
    function buildItem(model) {
        const {item} = model;
        const article = text('article', `rh-event rh-${model.group}`);
        article.dataset.eventId = item.id;
        const icon = text('span', 'rh-icon'); icon.setAttribute('aria-hidden', 'true');
        icon.append(text('i', `bx ${groups[model.group][1]}`));
        const body = text('div', 'rh-event-body');
        const head = text('div', 'rh-event-head');
        head.append(text('h6', 'rh-title', model.title));
        const time = text('time', 'rh-time', model.occurred?.toLocaleTimeString('vi-VN', {hour:'2-digit', minute:'2-digit', second:'2-digit'}) || 'Chưa rõ giờ');
        if (model.occurred) {time.dateTime = model.occurred.toISOString(); time.title = model.occurred.toLocaleString('vi-VN');}
        head.append(time);
        const meta = text('div', 'rh-meta');
        meta.append(text('span', 'rh-actor', model.actor), text('span', 'rh-badge', groups[model.group][0]));
        if (item.isSuccess === false) meta.append(text('span', 'rh-failed', 'Không thành công'));
        if (item.stockDocumentLineId) meta.append(text('span', '', `Mã dòng hàng #${item.stockDocumentLineId}`));
        body.append(head, meta);
        const mainFields = model.fields.filter(key => {
            if (/Id$|AtUtc$/.test(key) || ['NameSnapshot', 'UnitNameSnapshot', 'ProposedBaseUnitName'].includes(key)) return false;
            if (key === 'ProposedFactor' && !own(model.before, key) && model.after.UnitNameSnapshot && model.after.ProposedBaseUnitName) return false;
            if (own(model.before, key) && own(model.after, key)) return true;
            const value = own(model.after, key) ? model.after[key] : model.before[key];
            return value !== null && value !== '' && value !== false && (value !== 0 || key === 'Quantity' || key === 'Status');
        }).slice(0, 3);
        const name = model.after.NameSnapshot || model.before.NameSnapshot;
        if (name) body.append(text('div', 'rh-product', name));
        if (mainFields.length) {
            const changes = text('div', 'rh-changes');
            for (const key of mainFields) changes.append(change(model, key));
            const snapshot = own(model.after, 'Quantity') ? model.after : model.before;
            if (snapshot.UnitNameSnapshot) changes.append(text('div', 'rh-packing',
                `Đơn vị: ${snapshot.UnitNameSnapshot}` + (snapshot.ProposedFactor && snapshot.ProposedBaseUnitName
                    ? ` · 1 ${snapshot.UnitNameSnapshot} = ${number.format(snapshot.ProposedFactor)} ${snapshot.ProposedBaseUnitName}` : '')));
            body.append(changes);
        }
        if (item.reason) body.append(text('p', 'rh-note', `Lý do: ${item.reason}`));
        if (item.note && item.note !== item.reason) body.append(text('p', 'rh-note', `Ghi chú: ${item.note}`));
        const details = text('details', 'rh-details');
        details.append(text('summary', '', model.fields.length ? `Xem chi tiết (${model.fields.length} mục)` : 'Xem chi tiết thao tác'));
        if (model.fields.length) {
            const oldSaved = Object.keys(model.before).length > 0, newSaved = Object.keys(model.after).length > 0;
            if (!oldSaved || !newSaved) details.append(text('p', 'rh-evidence-note', !oldSaved
                ? 'Bản ghi này chỉ lưu thông tin sau thao tác; không có giá trị trước để so sánh.'
                : 'Bản ghi này chỉ lưu thông tin trước thao tác.'));
            const wrap = text('div', 'rh-table-wrap'), table = text('table', 'rh-table');
            table.append(text('caption', 'visually-hidden', `Chi tiết: ${model.title}`));
            const header = text('thead'), headerRow = text('tr');
            for (const label of ['Nội dung', 'Trước thao tác', 'Sau thao tác']) {
                const th = text('th', '', label); th.scope = 'col'; headerRow.append(th);
            }
            header.append(headerRow); table.append(header);
            const rows = text('tbody');
            for (const key of model.fields) {
                const row = text('tr'), th = text('th', '', labels[key]); th.scope = 'row';
                row.append(th, text('td', 'rh-before', valueCell(model, key, 'before')), text('td', '', valueCell(model, key, 'after')));
                rows.append(row);
            }
            table.append(rows); wrap.append(table); details.append(wrap);
        } else details.append(text('p', 'rh-evidence-note', 'Xem lý do, ghi chú hoặc dữ liệu gốc để đối chiếu thao tác này.'));
        const raw = text('details', 'rh-technical'); raw.append(text('summary', '', 'Chi tiết kỹ thuật · dữ liệu gốc'));
        raw.append(text('p', '', `Mã sự kiện #${item.id} · Loại ${item.eventType}${item.traceId ? ` · Mã theo dõi: ${item.traceId}` : ''}`));
        // Preserve malformed/legacy JSON verbatim as well. Never interpret stored audit text as markup.
        for (const [label, value] of [['Trường thay đổi', item.changedFieldsJson], ['Giá trị trước', item.oldValuesJson], ['Giá trị sau', item.newValuesJson]]) {
            raw.append(text('div', 'fw-semibold', label));
            let formatted = value || '(Không có dữ liệu)';
            try { if (value) formatted = JSON.stringify(JSON.parse(value), null, 2); } catch { /* Keep original evidence. */ }
            raw.append(text('pre', '', formatted));
        }
        details.append(raw); body.append(details); article.append(icon, body);
        return article;
    }

    let reload = null;
    window.PurchaseReceiptHistory = {load: () => reload?.()};
    document.addEventListener('DOMContentLoaded', () => {
        const card = document.getElementById('purchaseReceiptAuditCard');
        if (!card || !window.stockDocumentPage?.canViewAudit) return;
        const timeline = card.querySelector('#purchaseReceiptAuditTimeline'), status = card.querySelector('#purchaseReceiptAuditStatus');
        const search = card.querySelector('#receiptHistorySearch'), filter = card.querySelector('#receiptHistoryFilter'),
            sort = card.querySelector('#receiptHistorySort'), refresh = card.querySelector('#receiptHistoryRefresh'),
            more = card.querySelector('#receiptHistoryMore'), stats = card.querySelector('#receiptHistoryStats');
        let all = [], limit = 12, generation = 0;
        function render() {
            const query = normalize(search.value).trim();
            const filtered = all.filter(model => (!filter.value || model.group === filter.value) && (!query || model.search.includes(query)))
                .sort((a, b) => ((a.occurred?.getTime() || 0) - (b.occurred?.getTime() || 0) || Number(a.item.id) - Number(b.item.id)) * (sort.value === 'oldest' ? 1 : -1));
            const shown = filtered.slice(0, limit), fragment = document.createDocumentFragment();
            let lastDay = null;
            for (const model of shown) {
                const day = model.occurred?.toLocaleDateString('vi-VN', {weekday:'long', day:'2-digit', month:'2-digit', year:'numeric'}) || 'Chưa rõ ngày';
                if (day !== lastDay) {fragment.append(text('h6', 'rh-day', day)); lastDay = day;}
                fragment.append(buildItem(model));
            }
            timeline.replaceChildren(fragment);
            status.textContent = all.length === 0 ? 'Phiếu này chưa có lịch sử thao tác.' : filtered.length === 0
                ? 'Không tìm thấy thao tác phù hợp. Thử đổi từ khóa hoặc bộ lọc.'
                : `Đang hiển thị ${shown.length} / ${filtered.length} thao tác${filtered.length !== all.length ? ` (tổng ${all.length})` : ''} · ${sort.value === 'oldest' ? 'Cũ nhất trước' : 'Mới nhất trước'}`;
            more.hidden = shown.length >= filtered.length;
            more.textContent = `Xem thêm ${Math.min(12, filtered.length - shown.length)} thao tác`;
        }
        reload = async () => {
            const request = ++generation;
            refresh.disabled = true; card.setAttribute('aria-busy', 'true'); status.textContent = 'Đang tải lịch sử...';
            try {
                const response = await fetch(`/admin/api/stock-documents/${window.stockDocumentPage.documentId}/audit-events`,
                    {headers:{Accept:'application/json'}, cache:'no-store'});
                const api = await readApiResponse(response);
                if (!api.ok || !Array.isArray(api.data?.events)) throw new Error('Không thể tải lịch sử. Bấm “Tải lại” để thử lại.');
                if (request !== generation) return;
                all = api.data.events.map(model);
                stats.textContent = `${all.length} thao tác · ${new Set(all.map(x => x.item.actorUserId || x.actor)).size} người thực hiện`;
                render();
            } catch (error) {
                if (request === generation) status.textContent = `${error.message}${all.length ? ' Nội dung bên dưới là lần tải trước.' : ''}`;
            } finally {
                if (request === generation) {refresh.disabled = false; card.setAttribute('aria-busy', 'false');}
            }
        };
        for (const control of [search, filter, sort]) control.addEventListener(control === search ? 'input' : 'change', () => {limit = 12; render();});
        refresh.addEventListener('click', reload);
        more.addEventListener('click', () => {limit += 12; render();});
        reload();
    });
})();
