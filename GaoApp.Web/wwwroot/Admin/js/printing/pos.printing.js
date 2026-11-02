(function () {
    'use strict';
    let connecting;
    const helperUrl = 'http://127.0.0.1:17880';
    const helperFormat = 'gao-raster-v1', helperWidth = 576, helperMaxHeight = 16000;
    const helperUnavailable = 'Không kết nối được Print Helper trên máy này.';
    const helperJobs = new WeakMap();
    let helperMutationTail = Promise.resolve();
    const paymentPrintChannelName = 'gao-pos-payment-print-v1', paymentPrintLifetime = 120000;
    const paymentPrintIntents = new Map();
    const paymentPrintResults = new Map();
    const paymentPrintStorageKey = token => paymentPrintChannelName + ':' + token;
    let paymentPrintChannel;
    let activePaymentPrint;
    function cashDrawerFor(receipt, postPayment) {
        if (postPayment !== true || !['Completed', 2].includes(receipt?.status) || !Array.isArray(receipt.payments)) return false;
        // Canonical payment DTOs / finalized offline state, never rendered receipt text or input fields.
        const positive = value => typeof value === 'number' && Number.isFinite(value) && value > 0;
        return receipt.payments.some(payment => payment.method === 'Cash' && positive(payment.amount)) ||
            (positive(receipt.changeDue) && receipt.payments.some(payment => payment.method === 'BankTransfer' && positive(payment.amount)));
    }
    function claimPaymentPrint(token, orderId, sourceWindow = null) {
        const ticket = paymentPrintIntents.get(token);
        if (!ticket || ticket.orderId !== orderId || ticket.expiresAt <= Date.now() ||
            (ticket.printWindow && sourceWindow && ticket.printWindow !== sourceWindow)) return false;
        paymentPrintIntents.delete(token);
        ticket.claimed = true;
        paymentPrintResults.set(token, ticket);
        return true;
    }
    function completePaymentPrint(token, orderId, message, sourceWindow) {
        const ticket = paymentPrintResults.get(token);
        if (!ticket?.claimed || ticket.orderId !== orderId ||
            (ticket.printWindow && ticket.printWindow !== sourceWindow)) return false;
        finishPaymentPrint(token, orderId, message);
        return true;
    }
    function paymentPrintOwner() {
        try { return window.parent && window.parent !== window ? window.parent : window.opener; }
        catch { return null; }
    }
    function finishPaymentPrint(token, orderId, message) {
        const result = paymentPrintResults.get(token);
        if (!result || result.orderId !== orderId || result.expiresAt <= Date.now()) return;
        paymentPrintResults.delete(token);
        const popup = result.printWindow;
        if (popup) {
            try {
                popup.sessionStorage.removeItem(paymentPrintStorageKey(token) + ':result');
                // The POS also owns closing the tab it opened. Do not depend on
                // the receipt's own close callback or the current user's role.
                if (!popup.closed && popup.location.origin === window.location.origin &&
                    popup.location.pathname === `/admin/pos/receipt/${orderId}`) {
                    if (window.GaoPrintLifecycle) window.GaoPrintLifecycle.close(popup);
                    else popup.close();
                }
            } catch { /* The receipt may already be closed or have navigated away. */ }
        }
        if (typeof message === 'string' && message)
            window.dispatchEvent(new CustomEvent('pos:print-error', { detail: { orderId, message: message.slice(0, 500) } }));
        window.dispatchEvent(new CustomEvent('pos:print-closed'));
    }
    function watchPaymentPrint(token, ticket) {
        const inspect = () => {
            if (paymentPrintResults.get(token) !== ticket || ticket.expiresAt <= Date.now()) return;
            try {
                if (ticket.printWindow.closed) return;
                const result = JSON.parse(ticket.printWindow.sessionStorage.getItem(paymentPrintStorageKey(token) + ':result') || 'null');
                if (result?.finished === true && result.orderId === ticket.orderId) {
                    finishPaymentPrint(token, result.orderId, result.message);
                    return;
                }
            } catch { /* Navigation can temporarily make the new tab unavailable. */ }
            window.setTimeout(inspect, 500);
        };
        window.setTimeout(inspect, 500);
    }
    function postPaymentUrl(url, orderId, printWindow = null) {
        // Keep the one-use checkout intent separate from saved printer preferences.
        // Stage it in the actual tab; the channel remains for older print callers.
        try {
            const target = new URL(url, window.location.href);
            if (!Number.isInteger(orderId) || orderId <= 0 || target.origin !== window.location.origin ||
                ![`/admin/pos/receipt/${orderId}`, `/admin/pos/orders/${orderId}/print`].includes(target.pathname)) return url;
            if (!paymentPrintChannel) {
                paymentPrintChannel = new window.BroadcastChannel(paymentPrintChannelName);
                paymentPrintChannel.onmessage = event => {
                    const request = event.data;
                    if (request?.kind === 'finished') {
                        finishPaymentPrint(request.token, request.orderId, request.message);
                        return;
                    }
                    if (typeof request?.requestId !== 'string' || typeof request.token !== 'string' ||
                        !paymentPrintIntents.has(request.token)) return;
                    const granted = claimPaymentPrint(request.token, request.orderId);
                    paymentPrintChannel.postMessage({ requestId: request.requestId, granted });
                };
            }
            const token = window.crypto.randomUUID();
            const ticket = { orderId, expiresAt: Date.now() + paymentPrintLifetime };
            let staged = false;
            if (printWindow && !printWindow.closed) {
                try {
                    // Stage a one-use ticket in the actual print tab before
                    // navigating it. A busy/background POS cannot time it out.
                    printWindow.sessionStorage.setItem(paymentPrintStorageKey(token), JSON.stringify({ ...ticket, version: 2 }));
                    staged = true;
                } catch { return url; } // The live POS uses its direct transport instead.
            }
            if (staged) {
                const result = { ...ticket, printWindow };
                paymentPrintIntents.set(token, result);
                paymentPrintResults.set(token, result);
                watchPaymentPrint(token, result);
            } else paymentPrintIntents.set(token, ticket);
            window.setTimeout(() => {
                paymentPrintIntents.delete(token); paymentPrintResults.delete(token);
                try {
                    printWindow?.sessionStorage.removeItem(paymentPrintStorageKey(token));
                    printWindow?.sessionStorage.removeItem(paymentPrintStorageKey(token) + ':result');
                } catch { /* Closed print tab. */ }
            }, paymentPrintLifetime);
            target.hash = 'pos-payment-print=' + token;
            return target.pathname + target.search + target.hash;
        } catch { return url; } // No verified handoff means no drawer pulse.
    }
    async function consumeCashDrawer(receipt) {
        try {
            const match = /^#pos-payment-print=([0-9a-f-]{36})$/i.exec(window.location.hash);
            if (!match) return false;
            window.history.replaceState(null, '', window.location.pathname + window.location.search);
            const owner = paymentPrintOwner();
            if (typeof owner?.PosPrinting?.claimPaymentPrint === 'function') {
                // A synchronous claim on the live POS survives a missing tab
                // storage snapshot and a throttled background BroadcastChannel.
                const granted = owner.PosPrinting.claimPaymentPrint(match[1], receipt?.orderId, window);
                try { window.sessionStorage?.removeItem(paymentPrintStorageKey(match[1])); } catch { /* No storage required. */ }
                if (granted === true) activePaymentPrint = { token: match[1], orderId: receipt?.orderId };
                return cashDrawerFor(receipt, granted);
            }
            let stored;
            try { stored = window.sessionStorage?.getItem(paymentPrintStorageKey(match[1])); } catch { /* Use the live channel below. */ }
            if (stored) {
                window.sessionStorage.removeItem(paymentPrintStorageKey(match[1]));
                const ticket = JSON.parse(stored);
                // Compatibility with a receipt opened by the previous POS
                // bundle. New grants are consumed only by the live POS above.
                if (ticket.version !== 2) {
                    if (ticket.orderId !== receipt?.orderId || !Number.isFinite(ticket.expiresAt) || ticket.expiresAt <= Date.now()) return false;
                    activePaymentPrint = { token: match[1], orderId: ticket.orderId };
                    return cashDrawerFor(receipt, true);
                }
            }
            return await new Promise(resolve => {
                const channel = new window.BroadcastChannel(paymentPrintChannelName), requestId = window.crypto.randomUUID();
                const finish = granted => {
                    window.clearTimeout(timeout); channel.close();
                    if (granted === true) activePaymentPrint = { token: match[1], orderId: receipt?.orderId };
                    resolve(cashDrawerFor(receipt, granted));
                };
                const timeout = window.setTimeout(() => finish(false), 2000);
                channel.onmessage = event => { if (event.data?.requestId === requestId) finish(event.data.granted === true); };
                channel.postMessage({ token: match[1], requestId, orderId: receipt?.orderId });
            });
        } catch { return false; }
    }
    function reportPaymentPrint(receipt, message = '') {
        const ticket = activePaymentPrint;
        activePaymentPrint = null;
        if (!ticket || ticket.orderId !== receipt?.orderId) return;
        try {
            const owner = paymentPrintOwner();
            if (owner?.PosPrinting?.completePaymentPrint?.(ticket.token, ticket.orderId, message, window) === true) return;
        } catch { /* Older POS bundles still receive the channel/storage result. */ }
        try {
            window.sessionStorage?.setItem(paymentPrintStorageKey(ticket.token) + ':result',
                JSON.stringify({ orderId: ticket.orderId, finished: true, message: String(message || '').slice(0, 500) }));
        } catch { /* BroadcastChannel still reports when storage is unavailable. */ }
        if (window.parent && window.parent !== window) return;
        try {
            const channel = new window.BroadcastChannel(paymentPrintChannelName);
            channel.postMessage({ ...ticket, kind: 'finished', message: String(message || '').slice(0, 500) });
            channel.close();
        } catch { /* The dedicated receipt can still close when the POS has gone away. */ }
    }
    const key = context => {
        if (!(Number(context.storeId) > 0)) throw new Error('Chưa xác định cửa hàng để chọn máy in.');
        return `gao-pos-print-v1:${context.storeId}:${context.terminalId || 'admin'}`;
    };
    function preferences(context) {
        try {
            const saved = localStorage.getItem(key(context));
            return saved ? { ...JSON.parse(saved), color: false } : { mode: 'browser', printer: '', copies: 1, color: false, templateKey: 'modern-80' };
        } catch { throw new Error('Không đọc được cấu hình máy in trên máy này. Mở Máy in của quầy để kiểm tra lại.'); }
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
    function withHelperMutation(action) {
        const pending = helperMutationTail.then(() => {
            // One physical Helper serves POS, history and cash slips. Coordinate
            // same-origin tabs as well as distinct paper frames in this page.
            const locks = window.navigator?.locks;
            return locks?.request ? locks.request('gao-pos-helper:' + helperUrl, { mode: 'exclusive' }, action) : action();
        });
        helperMutationTail = pending.catch(() => {}); // A failed job must release the next one.
        return pending;
    }
    async function helperRequest(path, payload) {
        const started = Date.now();
        const busyTimeout = error => {
            error.message = path === '/drawer'
                ? 'Máy in đang bận. Đã hết thời gian chờ; lệnh mở két chưa được thực hiện.'
                : 'Máy in đang bận. Đã hết thời gian chờ; lệnh in bill này chưa được thực hiện.';
            return error;
        };
        for (let attempt = 0; ; attempt++) {
            try { return await helperRequestOnce(path, payload); }
            catch (error) {
                if (!error.helperBusyBeforeWrite) throw error;
                if (error.printDiagnostic) error.printDiagnostic.busyRetries = attempt;
                // The installed Helper explicitly rejects this request BEFORE
                // touching USB. Only this exact contract permits a bounded retry.
                const remaining = 30000 - (Date.now() - started);
                if (remaining <= 0 || attempt >= 31) throw busyTimeout(error);
                await new Promise(resolve => window.setTimeout(resolve, Math.min(remaining, 250 * (attempt + 1), 1000)));
                // Background Chrome tabs can delay timers well past their due
                // time. Do not send a late print after the waiting limit.
                if (Date.now() - started >= 30000) throw busyTimeout(error);
            }
        }
    }
    async function helperRequestOnce(path, payload) {
        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), payload ? 45000 : 5000);
        let stage = 'network', httpStatus = null, responseOk = null, data;
        try {
            const response = await fetch(helperUrl + path, { method: payload ? 'POST' : 'GET',
                mode: 'cors', credentials: 'omit', cache: 'no-store', redirect: 'error', signal: controller.signal,
                ...(payload ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) } : {}) });
            httpStatus = response.status ?? null;
            responseOk = response.ok;
            stage = 'json'; data = await response.json();
            stage = 'http'; if (!response.ok) throw new Error('helper-response');
            stage = 'ok'; if (data?.ok !== true) throw new Error('helper-response');
            stage = 'protocol'; if (data.protocol !== helperFormat) throw new Error('helper-response');
            if (payload) {
                stage = path === '/drawer' ? 'drawer-ack' : 'print-ack';
                if (path === '/drawer' ? data.drawerOpened !== true : data.printed !== true) throw new Error('helper-response');
                if (path !== '/drawer') {
                    stage = 'copies-ack'; if (data.copies !== payload.copies) throw new Error('helper-response');
                }
            }
            return data;
        } catch (cause) {
            // A lost response may follow a successful device write. Never retry or route to browser print.
            const diagnostic = { endpoint: path, method: payload ? 'POST' : 'GET', stage, httpStatus,
                errorName: String(cause?.name || 'Error'),
                errorMessage: String(cause?.message || '').slice(0, 320),
                expected: { protocol: helperFormat, ...(payload && path !== '/drawer' ? { printed: true, copies: payload.copies } : {}) },
                responseKeys: data && typeof data === 'object' ? Object.keys(data).slice(0, 30) : [],
                received: Object.fromEntries(['ok', 'protocol', 'printed', 'copies', 'drawerOpened', 'width', 'error', 'message']
                    .filter(field => data && Object.prototype.hasOwnProperty.call(data, field))
                    .map(field => [field, typeof data[field] === 'string' ? data[field].slice(0, 512) : data[field]])) };
            const value = field => JSON.stringify(data?.[field])?.slice(0, 160) ?? 'thiếu';
            const detail = stage === 'copies-ack' ? `Helper xác nhận số bản: ${value('copies')}; yêu cầu: ${payload.copies}.`
                : stage === 'print-ack' ? `Helper chưa xác nhận đã in: printed=${value('printed')}.`
                : stage === 'drawer-ack' ? `Helper chưa xác nhận mở két: drawerOpened=${value('drawerOpened')}.`
                : stage === 'protocol' ? `Giao thức Helper trả về: ${value('protocol')}.`
                : stage === 'json' ? `Phản hồi không đọc được dạng JSON (HTTP ${httpStatus ?? '?'}).`
                : stage === 'http' || stage === 'ok' ? `Helper trả HTTP ${httpStatus ?? '?'}; ok=${value('ok')}.`
                : cause?.name === 'AbortError' ? 'Hết thời gian chờ phản hồi từ Helper.'
                : 'Trình duyệt không đọc được phản hồi HTTP từ Helper.';
            const message = path === '/drawer' ? 'Chưa xác nhận được kết quả mở két. Kiểm tra két trước khi thao tác lại.' : payload
                ? 'Chưa xác nhận phản hồi từ Print Helper. Chưa xác nhận kết quả in. Kiểm tra giấy đã ra trước khi bấm in lại.'
                : helperUnavailable + ' Kiểm tra helper và quyền truy cập mạng cục bộ của trang.';
            const error = new Error(message + '\n' + detail);
            error.printDiagnostic = diagnostic;
            error.helperBusyBeforeWrite = !!payload && responseOk === false && httpStatus === 409 &&
                data?.ok === false && data.protocol === helperFormat &&
                data.error === 'Printer is busy; request was not printed.' &&
                data.printed === undefined && data.drawerOpened === undefined && data.copies === undefined && data.outcome === undefined;
            throw error;
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
        await withHelperMutation(async () => {
            await drawerReady(context);
            await helperRequest('/drawer', {protocol:helperFormat,cashDrawer:true});
        });
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
            await withHelperMutation(() => helperRequest('/print', payload));
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
        if (!available.includes(pref.printer)) throw new Error('Máy in đã chọn không còn trên máy này. Mở Máy in của quầy để chọn lại.');
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
    function sendReceipt(rendered, context, printWindow, onComplete, cashDrawer = false) {
        // Match POS Shift and the working published Web: the Helper appends
        // the pulse after all copies/cut inside ONE locked print job. A second
        // /drawer request can race the first job even after its HTTP ACK.
        return send(rendered, context, printWindow, onComplete, cashDrawer === true);
    }
    function openLocal(receipt, context, catalog, offline = false, postPayment = false, preopenedWindow = null) {
        const options = { offline, invoiceBuyerQrExpiresAtUtc: context.invoiceBuyerSelfServiceExpiresAtUtc };
        if (context.invoiceBuyerSelfServiceUrl) {
            if (!window.qrcodegen?.QrCode) throw new Error('Chưa tải được bộ tạo QR hóa đơn.');
            const matrix = window.qrcodegen.QrCode.encodeText(context.invoiceBuyerSelfServiceUrl, window.qrcodegen.QrCode.Ecc.QUARTILE);
            const canvas = document.createElement('canvas'), scale = 6, border = 4;
            canvas.width = canvas.height = (matrix.size + border * 2) * scale;
            const ink = canvas.getContext('2d'); ink.fillStyle = '#fff'; ink.fillRect(0, 0, canvas.width, canvas.height); ink.fillStyle = '#000';
            for (let y = 0; y < matrix.size; y++) for (let x = 0; x < matrix.size; x++)
                if (matrix.getModule(x, y)) ink.fillRect((x + border) * scale, (y + border) * scale, scale, scale);
            options.invoiceBuyerQrDataUrl = canvas.toDataURL('image/png');
        }
        const rendered = ReceiptTemplates.render(receipt, selected(context, catalog), options);
        // Reuse the window opened by the cashier's click. Opening another one after
        // the asynchronous IndexedDB save can be blocked by the browser.
        let popup = preopenedWindow && !preopenedWindow.closed ? preopenedWindow : null;
        if (!popup && postPayment === true) {
            // Automatic checkout has no remaining click gesture. Keep a real layout
            // viewport for rasterization without depending on popup permission.
            const frame = document.createElement('iframe');
            frame.dataset.posLocalPrint = 'true';
            frame.title = 'In bill sau thanh toán'; frame.setAttribute('aria-hidden', 'true');
            frame.style.cssText = 'position:fixed;left:-10000px;top:0;width:850px;height:850px;border:0';
            document.body.appendChild(frame); popup = frame.contentWindow;
        }
        if (!popup) popup = window.open('', '_blank', 'width=850,height=850');
        if (!popup) throw new Error('Cho phép mở cửa sổ in hóa đơn trên máy client.');
        // Preserve the opener of a dedicated checkout tab for Linux close rules.
        // History/template previews keep their existing isolated popup behavior.
        if (postPayment !== true) popup.opener = null;
        popup.document.write(rendered.html); popup.document.close();
        const bar = popup.document.createElement('div'); bar.className = 'local-print-tools';
        const style = popup.document.createElement('style');
        style.textContent = '.local-print-tools{padding:12px;background:#edf2ef;font:14px Arial}.local-print-tools button{padding:8px 16px;margin-right:10px}@media print{.local-print-tools{display:none}}';
        popup.document.head.appendChild(style);
        const button = popup.document.createElement('button'); button.textContent = 'In hóa đơn';
        const status = popup.document.createElement('span');
        let printing = false, helperCompleted = false;
        let printClosed = false;
        let cashDrawer = cashDrawerFor(receipt, postPayment);
        const closePrint = () => {
            if (printClosed) return;
            printClosed = true;
            const embedded = !!popup.frameElement;
            GaoPrintLifecycle.close(popup);
            if (!embedded) window.dispatchEvent(new CustomEvent('pos:print-closed'));
        };
        const print = async () => {
            if (printing || helperCompleted) return;
            printing = true;
            button.disabled = true; status.textContent = 'Đang gửi lệnh in…';
            const openDrawer = cashDrawer; cashDrawer = false;
            try {
                const result = await sendReceipt(rendered, context, popup, closePrint, openDrawer);
                helperCompleted = result.mode === 'helper';
                status.textContent = result.mode === 'helper' ? 'Print Helper đã nhận và ghi lệnh in.' : result.mode === 'qz' ? 'Đã gửi tới ' + result.printer : 'Đã mở hộp thoại in.';
                if (result.mode === 'helper' || result.mode === 'qz') closePrint();
            }
            catch (error) {
                status.textContent = error.message;
                if (postPayment === true) {
                    window.dispatchEvent(new CustomEvent('pos:print-error', { detail: { orderId: receipt.orderId, message: error.message } }));
                    closePrint();
                }
            }
            finally { printing = false; button.disabled = helperCompleted; }
        };
        let autoTimer;
        const close = popup.document.createElement('button'); close.textContent = 'Đóng';
        close.addEventListener('click', closePrint);
        button.addEventListener('click', () => { popup.clearTimeout(autoTimer); print(); }); bar.append(button, close, status); popup.document.body.prepend(bar);
        autoTimer = popup.setTimeout(print, 150);
        return popup;
    }
    window.PosPrinting = { preferences, savePreferences, selected, printers, send, sendReceipt, measure, pageSize, openLocal, health,
        cashDrawerFor, postPaymentUrl, consumeCashDrawer, reportPaymentPrint, claimPaymentPrint, completePaymentPrint, drawerReady, openDrawer };
})();
