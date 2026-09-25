(function () {
    'use strict';
    let connecting;
    const key = context => {
        if (!(Number(context.storeId) > 0)) throw new Error('Chưa xác định cửa hàng để chọn máy in.');
        return `gao-pos-print-v1:${context.storeId}:${context.terminalId || 'admin'}`;
    };
    function preferences(context) {
        try {
            const saved = localStorage.getItem(key(context));
            return saved ? { ...JSON.parse(saved), color: false } : { mode: 'browser', printer: '', copies: 1, color: false, templateKey: 'modern-80' };
        } catch { throw new Error('Không đọc được cấu hình máy in trên máy này. Mở Mẫu hóa đơn & máy in để kiểm tra.'); }
    }
    function savePreferences(context, value) {
        const saved = { mode: value.mode === 'qz' ? 'qz' : 'browser', printer: String(value.printer || '').slice(0, 250),
            copies: Math.max(1, Math.min(5, Math.trunc(Number(value.copies) || 1))), color: false,
            templateKey: String(value.templateKey || 'modern-80'), template: value.template ? ReceiptTemplates.normalize(value.template) : undefined };
        if (saved.mode === 'qz' && !saved.printer) throw new Error('Chọn máy in đã cài trên client.');
        try { localStorage.setItem(key(context), JSON.stringify(saved)); }
        catch { throw new Error('Không lưu được cấu hình máy in. Kiểm tra bộ nhớ trình duyệt.'); }
        return saved;
    }
    function selected(context, catalog) {
        const pref = preferences(context), options = catalog?.length ? catalog : ReceiptTemplates.builtIns();
        return pref.template || options.find(x => x.key === pref.templateKey)?.design || options.find(x => x.key === 'modern-80')?.design || ReceiptTemplates.normalize();
    }
    async function connect() {
        if (!window.qz) throw new Error('Chưa tải được thư viện kết nối máy in.');
        if (qz.websocket.isActive()) return;
        qz.websocket.setUsingSurf(false);
        if (!connecting) connecting = qz.websocket.connect({ host: ['localhost'], usingSecure: true, retries: 0 })
            .catch(() => { throw new Error('Chưa kết nối QZ Tray. Cài và mở QZ Tray trên chính máy client, rồi bấm Kết nối máy in.'); })
            .finally(() => { connecting = null; });
        return connecting;
    }
    async function printers() { await connect(); return (await qz.printers.find()).sort((a, b) => a.localeCompare(b)); }
    function measure(document, rendered) {
        const mm = document.querySelector('.receipt').getBoundingClientRect().height * 25.4 / 96;
        return rendered.size.height || Math.max(30, Math.ceil(mm + 1));
    }
    function pageSize(document, rendered) {
        let style = document.getElementById('receipt-page-size');
        if (!style) { style = document.createElement('style'); style.id = 'receipt-page-size'; document.head.appendChild(style); }
        style.textContent = `@page{size:${rendered.size.width}mm ${measure(document, rendered)}mm;margin:0}`;
    }
    async function send(rendered, context, printWindow, onComplete) {
        const pref = preferences(context);
        if (pref.mode !== 'qz') {
            pageSize(printWindow.document, rendered);
            const cancel = onComplete ? GaoPrintLifecycle.watch(printWindow, onComplete) : null;
            try { printWindow.focus(); printWindow.print(); }
            catch (error) { cancel?.(); throw error; }
            return { mode: 'browser' };
        }
        await connect();
        const available = await qz.printers.find();
        if (!available.includes(pref.printer)) throw new Error('Máy in đã chọn không còn trên client. Vào Mẫu hóa đơn & máy in để chọn lại.');
        const height = measure(printWindow.document, rendered);
        const config = qz.configs.create(pref.printer, { units: 'mm', size: { width: rendered.size.width, height },
            margins: 0, copies: pref.copies, colorType: 'grayscale', scaleContent: false,
            rasterize: true, jobName: 'GaoApp · Phiếu bán hàng' });
        const html = rendered.html.replace('</head>', `<style>@page{size:${rendered.size.width}mm ${height}mm;margin:0}</style></head>`);
        await qz.print(config, [{ type: 'pixel', format: 'html', flavor: 'plain', data: html,
            options: { pageWidth: rendered.size.width / 25.4 } }]);
        if (onComplete) window.setTimeout(onComplete, 0);
        return { mode: 'qz', printer: pref.printer };
    }
    function openLocal(receipt, context, catalog, offline = false) {
        const rendered = ReceiptTemplates.render(receipt, selected(context, catalog), { offline });
        const popup = window.open('', '_blank', 'width=850,height=850');
        if (!popup) throw new Error('Cho phép mở cửa sổ in hóa đơn trên máy client.');
        popup.opener = null; popup.document.write(rendered.html); popup.document.close();
        const bar = popup.document.createElement('div'); bar.className = 'local-print-tools';
        const style = popup.document.createElement('style');
        style.textContent = '.local-print-tools{padding:12px;background:#edf2ef;font:14px Arial}.local-print-tools button{padding:8px 16px;margin-right:10px}@media print{.local-print-tools{display:none}}';
        popup.document.head.appendChild(style);
        const button = popup.document.createElement('button'); button.textContent = 'In hóa đơn';
        const status = popup.document.createElement('span');
        const print = async () => {
            button.disabled = true; status.textContent = 'Đang gửi lệnh in…';
            try { const result = await send(rendered, context, popup, () => GaoPrintLifecycle.close(popup)); status.textContent = result.mode === 'qz' ? 'Đã gửi tới ' + result.printer : 'Đã mở hộp thoại in.'; }
            catch (error) { status.textContent = error.message; }
            finally { button.disabled = false; }
        };
        button.addEventListener('click', print); bar.append(button, status); popup.document.body.prepend(bar);
        popup.setTimeout(print, 150);
        return popup;
    }
    window.PosPrinting = { preferences, savePreferences, selected, printers, send, measure, pageSize, openLocal };
})();
