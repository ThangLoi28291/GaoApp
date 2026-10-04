(function () {
    'use strict';
    let connecting;
    const helperUrl = 'http://127.0.0.1:17880';
    const helperFormat = 'gao-raster-v1', helperWidth = 576, helperMaxHeight = 16000;
    const helperUnavailable = 'Không kết nối được Print Helper trên máy này.';
    const helperJobs = new WeakMap();
    const paymentPrintChannelName = 'gao-pos-payment-print-v1', paymentPrintLifetime = 120000;
    const paymentPrintIntents = new Map();
    let paymentPrintChannel;
    function cashDrawerFor(receipt, postPayment) {
        if (postPayment !== true || !['Completed', 2].includes(receipt?.status) || !Array.isArray(receipt.payments)) return false;
        // Canonical payment DTOs / finalized offline state, never rendered receipt text or input fields.
        const positive = value => typeof value === 'number' && Number.isFinite(value) && value > 0;
        return receipt.payments.some(payment => payment.method === 'Cash' && positive(payment.amount)) ||
            (positive(receipt.changeDue) && receipt.payments.some(payment => payment.method === 'BankTransfer' && positive(payment.amount)));
    }
    function postPaymentUrl(url, orderId) {
        // The live POS owns this one-use intent. No persistent flag survives a POS reload.
        // BroadcastChannel preserves the existing noopener popup and QR iframe boundaries.
        try {
            const target = new URL(url, window.location.href);
            if (!Number.isInteger(orderId) || orderId <= 0 || target.origin !== window.location.origin ||
                ![`/admin/pos/receipt/${orderId}`, `/admin/pos/orders/${orderId}/print`].includes(target.pathname)) return url;
            if (!paymentPrintChannel) {
                paymentPrintChannel = new window.BroadcastChannel(paymentPrintChannelName);
                paymentPrintChannel.onmessage = event => {
                    const request = event.data, ticket = paymentPrintIntents.get(request?.token);
                    if (!ticket || typeof request.requestId !== 'string') return;
                    paymentPrintIntents.delete(request.token); // This POS event loop grants at most one claimant.
                    paymentPrintChannel.postMessage({ requestId: request.requestId,
                        granted: ticket.orderId === request.orderId && ticket.expiresAt > Date.now() });
                };
            }
            const token = window.crypto.randomUUID();
            paymentPrintIntents.set(token, { orderId, expiresAt: Date.now() + paymentPrintLifetime });
            window.setTimeout(() => paymentPrintIntents.delete(token), paymentPrintLifetime);
            target.hash = 'pos-payment-print=' + token;
            return target.pathname + target.search + target.hash;
        } catch { return url; } // No verified handoff means no drawer pulse.
    }
    async function consumeCashDrawer(receipt) {
        try {
            const match = /^#pos-payment-print=([0-9a-f-]{36})$/i.exec(window.location.hash);
            if (!match) return false;
            window.history.replaceState(null, '', window.location.pathname + window.location.search);
            return await new Promise(resolve => {
                const channel = new window.BroadcastChannel(paymentPrintChannelName), requestId = window.crypto.randomUUID();
                const finish = granted => { window.clearTimeout(timeout); channel.close(); resolve(cashDrawerFor(receipt, granted)); };
                const timeout = window.setTimeout(() => finish(false), 2000);
                channel.onmessage = event => { if (event.data?.requestId === requestId) finish(event.data.granted === true); };
                channel.postMessage({ token: match[1], requestId, orderId: receipt?.orderId });
            });
        } catch { return false; }
    }
    const key = context => {
        if (!(Number(context.storeId) > 0)) throw new Error('Chưa xác định cửa hàng để chọn máy in.');
        return `gao-pos-print-v1:${context.storeId}:${context.terminalId || 'admin'}`;
    };
    function preferences(context) {
        try {
            const saved = localStorage.getItem(key(context));
            return saved ? { ...JSON.parse(saved), color: false } : { mode: 'browser', printer: '', copies: 1, color: false, templateKey: 'modern-80' };
        } catch { throw new Error('Không đọc được cấu hình máy in trên máy này. Nhờ admin kiểm tra cấu hình máy in tại quầy.'); }
    }
    function savePreferences(context, value) {
        const saved = { mode: ['qz', 'helper'].includes(value.mode) ? value.mode : 'browser', printer: String(value.printer || '').slice(0, 250),
            copies: Math.max(1, Math.min(5, Math.trunc(Number(value.copies) || 1))), color: false,
            templateKey: String(value.templateKey || 'modern-80'), template: value.template ? ReceiptTemplates.normalize(value.template) : undefined };
        if (saved.mode === 'qz' && !saved.printer) throw new Error('Chọn máy in đã cài trên client.');
        try { localStorage.setItem(key(context), JSON.stringify(saved)); }
        catch { throw new Error('Không lưu được cấu hình máy in. Kiểm tra bộ nhớ trình duyệt.'); }
        return saved;
    }
    function selected(context, catalog) {
        // The store's synchronized default is authoritative; old per-browser template snapshots must not override it.
        return ReceiptTemplates.normalize(context.receiptDefault?.template?.design ||
            ReceiptTemplates.builtIns().find(x => x.key === 'modern-80').design);
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
    async function helperRequest(path, payload) {
        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), payload ? 45000 : 5000);
        try {
            const response = await fetch(helperUrl + path, { method: payload ? 'POST' : 'GET',
                mode: 'cors', credentials: 'omit', cache: 'no-store', redirect: 'error', signal: controller.signal,
                ...(payload ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) } : {}) });
            const data = await response.json();
            if (!response.ok || data.ok !== true || data.protocol !== helperFormat)
                throw new Error('helper-response');
            if (payload && (path === '/drawer' ? data.drawerOpened !== true : (data.printed !== true || data.copies !== payload.copies))) throw new Error('helper-response');
            return data;
        } catch {
            // A lost response may follow a successful device write. Never retry or route to browser print.
            if (path === '/drawer') throw new Error('Chưa xác nhận được kết quả mở két. Kiểm tra két trước khi thao tác lại.');
            throw new Error(payload
                ? helperUnavailable + ' Chưa xác nhận kết quả in. Kiểm tra giấy đã ra trước khi bấm in lại.'
                : helperUnavailable + ' Kiểm tra helper và quyền truy cập mạng cục bộ của trang.');
        } finally { window.clearTimeout(timeout); }
    }
    async function health() {
        const data = await helperRequest('/health');
        if (data.width !== helperWidth) throw new Error('Print Helper chưa hỗ trợ bitmap 576 px của GaoApp.');
        return data;
    }
    async function drawerReady(context) {
        if (preferences(context).mode !== 'helper') throw new Error('Chọn Linux Print Helper trong Mẫu hóa đơn & máy in để mở két trên quầy này.');
        const ready = await health();
        if (ready.cashDrawerOnly !== true) throw new Error('Cần cập nhật Print Helper trên máy Linux để dùng nút chỉ mở két.');
    }
    async function openDrawer(context) {
        await drawerReady(context);
        await helperRequest('/drawer', {protocol:helperFormat,cashDrawer:true});
    }
    async function rasterize(rendered, printWindow) {
        if (rendered.size.width !== 80 || rendered.size.height)
            throw new Error('Linux Print Helper hiện hỗ trợ mẫu giấy 80 mm. Chọn mẫu 80 mm hoặc cách in khác.');
        const doc = printWindow.document;
        await doc.fonts?.ready;
        const paper = doc.querySelector('.receipt');
        if (!paper) throw new Error('Chưa tải xong mẫu hóa đơn để in.');
        await Promise.all(Array.from(paper.querySelectorAll('img'), img => img.decode()));
        const bounds = paper.getBoundingClientRect(), scale = helperWidth / bounds.width;
        const height = Math.ceil(bounds.height * scale);
        if (!(height > 0 && height <= helperMaxHeight)) throw new Error('Hóa đơn quá dài hoặc chưa tải xong để in qua Print Helper.');
        // Serialize the exact canonical HTML; this transport has no receipt fields or layout rules.
        const root = new DOMParser().parseFromString(rendered.html, 'text/html').documentElement;
        if (Array.from(root.querySelectorAll('img')).some(img => !img.getAttribute('src')?.startsWith('data:image/png;base64,')))
            throw new Error('Ảnh hóa đơn chưa được nhúng để in qua Print Helper.');
        const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${helperWidth}" height="${height}" viewBox="0 0 ${bounds.width} ${height / scale}"><foreignObject width="100%" height="100%">${new XMLSerializer().serializeToString(root)}</foreignObject></svg>`;
        const image = new Image();
        image.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg);
        await image.decode();
        const canvas = doc.createElement('canvas'); canvas.width = helperWidth; canvas.height = height;
        const drawing = canvas.getContext('2d');
        drawing.fillStyle = '#fff'; drawing.fillRect(0, 0, helperWidth, height); drawing.drawImage(image, 0, 0);
        const pixels = drawing.getImageData(0, 0, helperWidth, height).data;
        const bytes = new Uint8Array(helperWidth / 8 * height);
        let ink = false;
        for (let pixel = 0; pixel < helperWidth * height; pixel++) {
            if (pixels[pixel * 4] < 128) { bytes[pixel >> 3] |= 128 >> (pixel & 7); ink = true; }
        }
        if (!ink) throw new Error('Không tạo được ảnh hóa đơn. Chưa gửi lệnh in.');
        let binary = '';
        for (let offset = 0; offset < bytes.length; offset += 8192)
            binary += String.fromCharCode(...bytes.subarray(offset, offset + 8192));
        return { protocol: helperFormat, width: helperWidth, height, rasterBase64: btoa(binary), cut: true, cashDrawer: false };
    }
    function sendHelper(rendered, pref, printWindow, onComplete, cashDrawer) {
        if (helperJobs.has(printWindow)) return helperJobs.get(printWindow);
        const job = (async () => {
            await health(); // Fail before rasterization/POST if the installed helper has the old receipt/text contract.
            const payload = await rasterize(rendered, printWindow);
            payload.copies = Math.max(1, Math.min(5, Math.trunc(Number(pref.copies) || 1)));
            payload.cashDrawer = cashDrawer === true;
            await helperRequest('/print', payload);
            if (onComplete) window.setTimeout(onComplete, 0);
            return { mode: 'helper' };
        })();
        helperJobs.set(printWindow, job);
        job.catch(() => helperJobs.delete(printWindow));
        // Keep successful jobs until this paper window is discarded: one page cannot submit twice while closing.
        return job;
    }
    async function send(rendered, context, printWindow, onComplete, cashDrawer = false) {
        const pref = preferences(context);
        if (pref.mode === 'helper') return sendHelper(rendered, pref, printWindow, onComplete, cashDrawer);
        if (pref.mode !== 'qz') {
            pageSize(printWindow.document, rendered);
            const cancel = onComplete ? GaoPrintLifecycle.watch(printWindow, onComplete) : null;
            try { printWindow.focus(); printWindow.print(); }
            catch (error) { cancel?.(); throw error; }
            return { mode: 'browser' };
        }
        await connect();
        const available = await qz.printers.find();
        if (!available.includes(pref.printer)) throw new Error('Máy in đã chọn không còn trên máy này. Nhờ admin chọn lại máy in tại quầy.');
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
    function openLocal(receipt, context, catalog, offline = false, postPayment = false, preopenedWindow = null) {
        const rendered = ReceiptTemplates.render(receipt, selected(context, catalog), { offline });
        // Reuse the window opened by the cashier's click. Opening another one after
        // the asynchronous IndexedDB save can be blocked by the browser.
        const popup = preopenedWindow && !preopenedWindow.closed ? preopenedWindow : window.open('', '_blank', 'width=850,height=850');
        if (!popup) throw new Error('Cho phép mở cửa sổ in hóa đơn trên máy client.');
        popup.opener = null; popup.document.write(rendered.html); popup.document.close();
        const bar = popup.document.createElement('div'); bar.className = 'local-print-tools';
        const style = popup.document.createElement('style');
        style.textContent = '.local-print-tools{padding:12px;background:#edf2ef;font:14px Arial}.local-print-tools button{padding:8px 16px;margin-right:10px}@media print{.local-print-tools{display:none}}';
        popup.document.head.appendChild(style);
        const button = popup.document.createElement('button'); button.textContent = 'In hóa đơn';
        const status = popup.document.createElement('span');
        let printing = false, helperCompleted = false;
        let cashDrawer = cashDrawerFor(receipt, postPayment);
        const print = async () => {
            if (printing || helperCompleted) return;
            printing = true;
            button.disabled = true; status.textContent = 'Đang gửi lệnh in…';
            const openDrawer = cashDrawer; cashDrawer = false;
            try { const result = await send(rendered, context, popup, () => GaoPrintLifecycle.close(popup), openDrawer); helperCompleted = result.mode === 'helper'; status.textContent = result.mode === 'helper' ? 'Print Helper đã nhận và ghi lệnh in.' : result.mode === 'qz' ? 'Đã gửi tới ' + result.printer : 'Đã mở hộp thoại in.'; }
            catch (error) { status.textContent = error.message; }
            finally { printing = false; button.disabled = helperCompleted; }
        };
        let autoTimer;
        button.addEventListener('click', () => { popup.clearTimeout(autoTimer); print(); }); bar.append(button, status); popup.document.body.prepend(bar);
        autoTimer = popup.setTimeout(print, 150);
        return popup;
    }
    window.PosPrinting = { preferences, savePreferences, selected, printers, send, measure, pageSize, openLocal, health,
        cashDrawerFor, postPaymentUrl, consumeCashDrawer, drawerReady, openDrawer };
})();
