(function () {
    'use strict';
    const uuid = value => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value || '');
    const positive = value => Number.isFinite(Number(value)) && Number(value) > 0;
    function commandAllowed(spec, documentId) {
        if (!spec || typeof spec.body !== 'string' || spec.body.length > 450000) return false;
        try {
            const body = JSON.parse(spec.body), base = `/admin/api/stock-documents/${documentId}`;
            if (!positive(body.quantity)) return false;
            if (spec.method === 'POST' && uuid(body.commandId))
                return spec.url === base + '/intake' || spec.url === base + '/intake/known' ||
                    new RegExp('^' + base + '/intake/[1-9]\\d*/quantity$').test(spec.url);
            return spec.method === 'PUT' && body.unitCost === null &&
                new RegExp('^' + base + '/lines/[1-9]\\d*$').test(spec.url);
        } catch { return false; }
    }
    function createStore(storage, scope) {
        if (![scope.user, scope.store, scope.document].every(x => Number.isSafeInteger(Number(x)) && Number(x) > 0))
            throw new Error('Thiếu thông tin chủ bản nháp.');
        const prefix = `gaoapp:receiving-draft:v1:${scope.store}:${scope.user}:${scope.document}:`;
        function get(key) {
            try {
                const value = JSON.parse(storage.getItem(prefix + key));
                return value?.schema === 1 && value.user === String(scope.user) && value.store === String(scope.store) &&
                    value.document === String(scope.document) && value.key === key ? value : null;
            } catch { return null; }
        }
        return {
            get,
            put(key, kind, data, label) {
                const previous=get(key);
                if(previous?.kind===kind&&JSON.stringify(previous.data)===JSON.stringify(data))return previous;
                const value = {schema: 1, user: String(scope.user), store: String(scope.store), document: String(scope.document),
                    key, kind, data, generation: Date.now().toString(36)+'-'+Math.random().toString(36).slice(2),
                    label: String(label || 'Hàng đang nhập').slice(0, 240), updated: Date.now()};
                const json = JSON.stringify(value);
                if (json.length > 460000) throw new Error('Bản nháp quá lớn. Hãy lưu bớt hàng hoặc thu nhỏ ảnh.');
                storage.setItem(prefix + key, json);
                return value;
            },
            remove: key => storage.removeItem(prefix + key),
            list() {
                const result = [];
                for (let i = 0; i < storage.length; i++) {
                    const name = storage.key(i);
                    if (name?.startsWith(prefix)) {
                        const value = get(name.slice(prefix.length));
                        if (value && (value.kind !== 'command' || commandAllowed(value.data, scope.document))) result.push(value);
                    }
                }
                return result.sort((a, b) => a.updated - b.updated);
            }
        };
    }
    if (typeof module === 'object' && module.exports) module.exports = {createStore, commandAllowed};
    if (typeof document === 'undefined') return;
    const root = document.getElementById('receiptIntake');
    if (!root) return;
    const scope = {user: root.dataset.draftUser, store: root.dataset.draftStore, document: root.dataset.documentId};
    let store, storageError = '', busy = false, expired = false, restoring = false;
    try { store = createStore(window.localStorage, scope); } catch {
        const warning=document.createElement('p');warning.className='alert alert-warning';
        warning.textContent='Trình duyệt đang chặn lưu nháp. Hãy giữ phiếu mở và chờ xác nhận lưu từng món trước khi rời trang.';root.prepend(warning);return;
    }
    const id = Number(scope.document), base = `/admin/api/stock-documents/${id}/intake`;
    const rawFetch = window.fetch.bind(window), responses = new WeakMap();
    const token = () => root.querySelector('[name="__RequestVerificationToken"]')?.value || '';
    const panel = document.createElement('section'); panel.id = 'receivingRecovery'; panel.className = 'alert alert-warning text-body'; panel.hidden = true;
    panel.setAttribute('aria-label', 'Khôi phục hàng đang nhập');
    const title = document.createElement('strong'), message = document.createElement('p'), list = document.createElement('ul');
    message.setAttribute('role', 'status'); message.className = 'my-2 text-break'; list.className = 'small mb-2 text-break';
    const restore = document.createElement('button'); restore.type = 'button'; restore.className = 'btn btn-primary'; restore.textContent = 'Khôi phục phần chưa lưu';
    const login = document.createElement('a'); login.className = 'btn btn-primary ms-2'; login.textContent = 'Đăng nhập lại để lưu tiếp';
    login.dataset.receivingLogin='1';
    login.href = '/admin/account/login?returnUrl=' + encodeURIComponent(location.pathname + location.search);
    panel.append(title, message, list, restore, login); root.prepend(panel);
    function entries() { try { return store.list(); } catch { return []; } }
    function render(text) {
        const pending = entries(); panel.hidden = !pending.length && !storageError && !expired;
        title.textContent = pending.length ? `Có ${pending.length} phần nhập chưa xác nhận lưu` : 'Trạng thái lưu phiếu';
        if (text) message.textContent = text;
        else message.textContent = storageError || (expired ? 'Phiên đăng nhập đã kết thúc. Phần đang nhập được giữ trên thiết bị này; đăng nhập lại cùng tài khoản để tiếp tục.' :
            'Đã giữ bản nháp trên thiết bị này. Kiểm tra phiếu rồi khôi phục; các món đã lưu sẽ không bị cộng lại.');
        list.replaceChildren();
        for (const entry of pending) {
            const item = document.createElement('li'); item.textContent = entry.label + ' ';
            const discard=document.createElement('button');discard.type='button';discard.className='btn btn-sm btn-link';discard.textContent='Bỏ mục nháp';discard.disabled=busy;
            discard.addEventListener('click',()=>{if(confirmDiscard(entry)){if(entry.kind==='command')confirm(entry.key);else remove(entry.key);}});item.append(discard);list.append(item);
        }
        restore.disabled = busy || expired || !pending.length || !!window.receivingIsActivelySaving?.() || !!window.ReceiptIntake?.isActivelySaving?.(); login.hidden = !expired;
        if(expired)for(const modal of document.querySelectorAll('#quickAddProductModal.show,#receiptIntakeModal.show,#wraQuantityModal.show')){
            if(modal.querySelector('[data-receiving-login]'))continue;
            const notice=document.createElement('div');notice.className='alert alert-warning';notice.setAttribute('role','status');
            notice.append(document.createTextNode('Phiên đã hết hạn. Phần đang nhập được giữ trên thiết bị. '),login.cloneNode(true));
            modal.querySelector('.modal-body').append(notice);
        }
        window.updateReceivingSubmitState?.();
        document.dispatchEvent(new Event('receiving:recovery'));
    }
    function confirmDiscard(entry){return window.confirm('Bỏ phần đang giữ trên thiết bị: '+entry.label+'? Hàng đã lưu trên phiếu vẫn được giữ.');}
    function put(key, kind, data, label, required = false) {
        try { const value = store.put(key, kind, data, label); storageError = ''; render(); return value; }
        catch {
            storageError = 'Thiết bị chưa giữ được bản nháp. Kiểm tra dung lượng hoặc quyền lưu dữ liệu của trình duyệt trước khi rời phiếu.'; render();
            if (required) throw new Error(storageError);
            return null;
        }
    }
    function remove(key) { store.remove(key); render(); }
    function quantityKey(input) { return input.dataset.intakeQuantity ? 'intake-' + input.dataset.intakeQuantity : 'line-' + input.dataset.lineId; }
    function saveQuantityDraft(input, value) {
        if (!input || input.readOnly || restoring) return;
        const key = 'quantity:' + quantityKey(input), row = input.closest('tr');
        if (Number(value) === Number(input.defaultValue)) { if (store.get(key)) remove(key); return; }
        put(key, 'quantity', {rowKey: quantityKey(input), value: String(value), baseline: input.defaultValue,
            lineVersion: row?.dataset.rowVersion || '', unitId: input.dataset.unitId || '', note: input.dataset.note || ''},
            (row?.querySelector('.wrd-line-name,strong')?.textContent.trim() || 'Sản phẩm') + ' · số lượng ' + value);
    }
    function confirmQuantity(input, value) {
        if (!input) return;
        const key = 'quantity:' + quantityKey(input), draft = store.get(key);
        if (draft && Number(draft.data.value) === Number(value)) remove(key);
    }
    async function request(url, options = {}, label) {
        const spec = {url, method: (options.method || 'GET').toUpperCase(), body: options.body};
        if (!commandAllowed(spec, id)) {
            const response=await rawFetch(url, options);
            if(response.status===401){expired=true;render();}
            return response;
        }
        const body = JSON.parse(spec.body), key = 'command:' + (body.commandId || 'line-' + url.split('/').pop());
        const formKey=url===base+'/known'?'form:known':url===base?'form:intake':null;
        if(formKey){spec.formKey=formKey;spec.formGeneration=store.get(formKey)?.generation||null;}
        const previous = store.get(key);
        const record = previous || put(key, 'command', spec, label || `${body.name || 'Hàng nhận'} · ${body.quantity}`, true);
        // Freeze the original intent and tokens before starting the network request.
        const response = await rawFetch(record.data.url, {...options, method: record.data.method, body: record.data.body,
            credentials: 'same-origin', cache: 'no-store', redirect: 'error',
            headers: {...options.headers, 'Content-Type': 'application/json', RequestVerificationToken: token()}});
        responses.set(response, key);
        if (response.status === 401) { expired = true; render(); }
        return response;
    }
    function confirm(value) {
        const key = typeof value === 'string' ? value : responses.get(value);
        if (!key) return;
        const record = store.get(key); remove(key);
        if(!record)return;
        if(record.data.formGeneration){
            if(store.get(record.data.formKey)?.generation===record.data.formGeneration)remove(record.data.formKey);
            return;
        }
        const body=JSON.parse(record.data.body);
        const known=store.get('form:known')?.data;
        if(record.data.url===base+'/known'&&known&&Number(known.quantity)===Number(body.quantity)&&
            Number(known.selected?.productUnitConversionId)===Number(body.productUnitConversionId))remove('form:known');
        const intake=store.get('form:intake')?.data.value;
        if(record.data.url===base&&intake&&Number(intake.fields?.riQuantity)===Number(body.quantity)&&
            Number(intake.fields?.riFactor)===Number(body.factor)&&String(intake.fields?.riBarcode||'')===String(body.barcode||'')&&
            String(intake.fields?.riNote||'')===String(body.note||'')&&(intake.product?.productVariantId||null)===(body.productVariantId||null)&&
            (intake.tab!=='new'||intake.fields?.riNewName===body.name))remove('form:intake');
    }
    function captureForms() {
        if (restoring) return;
        const knownModal = document.getElementById('quickAddProductModal');
        if (knownModal?.classList.contains('show')) {
            const selected = window.jQuery?.('#quickLookupInput').data('selected-item');
            if (selected && positive(selected.productVariantId)) put('form:known', 'form',
                {type: 'known', selected, quantity: document.getElementById('popupQuickQty').value},
                `${selected.productName || selected.text || 'Sản phẩm'} · đang nhập ${document.getElementById('popupQuickQty').value}`);
        }
        const intake = window.ReceiptIntake?.draft?.();
        if (intake) put('form:intake', 'form', {type: 'intake', value: intake}, (intake.fields.riNewName || intake.product?.productName || 'Hàng mới') + ' · đang nhập');
    }
    async function snapshot() {
        const response = await rawFetch(base, {credentials: 'same-origin', cache: 'no-store', redirect: 'error', headers: {Accept: 'application/json'}});
        if (response.status === 401) { expired = true; throw new Error('Đăng nhập lại cùng tài khoản để khôi phục hàng đang nhập.'); }
        if (!response.ok) throw new Error('Chưa đọc được phiếu. Bản nháp vẫn được giữ trên thiết bị.');
        const result = await response.json();
        if (result.state?.stockDocumentId !== id || !Array.isArray(result.state.items)) throw new Error('Phiếu không khớp bản nháp.');
        return result;
    }
    async function recover() {
        if (busy) return;
        busy = true; restoring = true; render('Đang đối chiếu phần đã lưu trên phiếu…');
        try {
            let current = await snapshot();
            for (const entry of entries().filter(x => x.kind === 'command')) {
                const spec = entry.data, body = JSON.parse(spec.body);
                if (current.recentReceipts?.some(x => x.commandId.toLowerCase() === String(body.commandId).toLowerCase())) { confirm(entry.key); continue; }
                if (!current.canCapture) throw new Error('Phiếu không còn cho nhập hàng hoặc bạn không còn quyền sửa. Bản nháp vẫn được giữ để đối chiếu.');
                if (spec.method === 'PUT') {
                    const lineId = spec.url.split('/').pop();
                    if (typeof window.refreshReceivingLines !== 'function') throw new Error('Mở màn hình nhận hàng để đối chiếu số lượng đang chờ.');
                    if (await window.refreshReceivingLines({confirmQuantity: {lineId, payload: body}})) { confirm(entry.key); continue; }
                } else if (spec.url === base || spec.url === base + '/known') {
                    // Explicit recovery adds the preserved item to the freshly read
                    // draft. Its command ID still protects against a lost acknowledgement.
                    body.documentRowVersion = current.state.documentRowVersion;
                }
                const response = await rawFetch(spec.url, {method: spec.method, credentials: 'same-origin', cache: 'no-store', redirect: 'error',
                    headers: {'Content-Type': 'application/json', RequestVerificationToken: token()}, body: JSON.stringify(body)});
                if (response.status === 401) { expired = true; throw new Error('Phiên đã hết hạn. Đăng nhập lại để lưu tiếp.'); }
                const result = await response.json().catch(() => ({}));
                if (!response.ok) throw new Error(result.message || 'Chưa khôi phục được mục này. Bản nháp vẫn được giữ; kiểm tra lại phiếu và quyền nhận hàng.');
                if (spec.method === 'POST' && (result.stockDocumentId !== id || !result.documentRowVersion)) throw new Error('Chưa nhận đủ xác nhận lưu. Bản nháp vẫn được giữ.');
                if (spec.method === 'PUT' && !await window.refreshReceivingLines({confirmQuantity: {lineId: spec.url.split('/').pop(), payload: body}}))
                    throw new Error('Chưa xác nhận được số lượng đã lưu.');
                confirm(entry.key); current = await snapshot();
            }
            await window.ReceiptIntake?.reload({requireConfirmation: true});
            if (window.refreshReceivingLines) await window.refreshReceivingLines();
            for (const entry of entries().filter(x => x.kind === 'quantity')) {
                const input = [...document.querySelectorAll('.js-receiving-qty,[data-intake-quantity]')].find(x => quantityKey(x) === entry.data.rowKey);
                if (!input || input.readOnly) throw new Error('Dòng hàng trong bản nháp đã thay đổi. Giữ bản nháp để quản lý đối chiếu.');
                if (Number(input.defaultValue) === Number(entry.data.value)) { remove(entry.key); continue; }
                if (!positive(entry.data.value) || input.defaultValue !== entry.data.baseline ||
                    (entry.data.lineVersion && input.closest('tr')?.dataset.rowVersion !== entry.data.lineVersion))
                    throw new Error('Số lượng hoặc dòng hàng đã được sửa ở nơi khác. Bản nháp được giữ để đối chiếu, chưa ghi đè phiếu.');
                input.value = entry.data.value;
                const saved = input.dataset.intakeQuantity ? await window.ReceiptIntake.saveQuantity(input) : await window.saveReceivingQty(input);
                if (saved === false) throw new Error('Chưa lưu được số lượng trong bản nháp.');
                remove(entry.key);
            }
            window.finishReceivingRecovery?.();window.ReceiptIntake?.finishRecovery?.();window.WarehouseReceivingQuantity?.finishRecovery?.();
            // A form that was never submitted is reopened for review, not posted.
            const form = entries().find(x => x.kind === 'form');
            if (form?.data.type === 'known' && typeof window.openQtyPopup === 'function') {
                window.openQtyPopup(form.data.selected); document.getElementById('popupQuickQty').value = form.data.quantity;
                window.updatePopupBaseQty?.();
            } else if (form?.data.type === 'intake') await window.ReceiptIntake.restoreDraft(form.data.value);
            expired = false; render(entries().length ? 'Đã mở lại phần đang nhập. Kiểm tra và bấm lưu để hoàn tất.' : 'Đã đối chiếu và khôi phục đủ phần chưa lưu.');
            window.receivingSaveEvent?.('saved', 'Đã khôi phục phần đang nhập');
        } catch (error) { render(error.message || 'Chưa khôi phục được. Bản nháp vẫn được giữ.'); }
        finally { busy = false; restoring = false; render(message.textContent); }
    }
    window.ReceivingDraftRecovery = {request, confirm, key: response => responses.get(response), saveQuantityDraft, confirmQuantity,
        confirmLine: lineId => confirm('command:line-' + lineId),
        hasPending: () => !restoring && entries().length > 0, isRecovering: () => busy,
        captureForms, recover, forgetForm: type => remove('form:' + type)};
    restore.addEventListener('click', () => void recover());
    document.addEventListener('input', event => {
        if (event.target.matches('.js-receiving-qty,[data-intake-quantity]')) saveQuantityDraft(event.target, event.target.value);
        captureForms();
    });
    document.addEventListener('change', captureForms);
    document.addEventListener('receiving:save',()=>render(message.textContent));
    document.addEventListener('shown.bs.modal', captureForms);
    document.addEventListener('hidden.bs.modal', event => {
        if (event.target.id === 'quickAddProductModal') remove('form:known');
        if (event.target.id === 'receiptIntakeModal') remove('form:intake');
    });
    document.addEventListener('visibilitychange', () => { if (document.hidden) captureForms(); });
    window.addEventListener('pagehide', captureForms);
    window.addEventListener('storage', () => render());
    render();
})();
