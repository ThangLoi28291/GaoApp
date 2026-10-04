const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(require.resolve('../../GaoApp.Web/wwwroot/Admin/js/pos/pos.barcode.js'), 'utf8');
const deferred = () => { let resolve; const promise = new Promise(r => resolve = r); return { promise, resolve }; };
function fixture(search = async () => [], post = async () => ({ orderId: 1, lines: [] })) {
    const listeners = {}, requests = [], timers = new Set(), state = {};
    const element = () => ({ value: '', dataset: {}, style: {}, innerHTML: '',
        addEventListener(type, fn) { listeners[type] = fn; },
        querySelectorAll() { return [{ classList: { add() {}, remove() {} }, setAttribute() {}, scrollIntoView() {} }]; },
        querySelector() { return state.flatItems?.length ? { getAttribute: () => state.flatItems[Math.max(0, state.activeIndex || 0)].key } : null; } });
    const input = element(), popup = element(); popup.addEventListener = () => {};
    const badge = { style: {} };
    const context = { AbortController, console, window: {
        PosCommon: {
            debounce(fn) { let work; const run = value => { run.cancel(); work = () => fn(value); timers.add(work); }; run.cancel = () => timers.delete(work); return run; },
            createTtlCache() { const values = new Map(); return { get: key => values.get(key), set: (key, value) => values.set(key, value) }; },
            createActionLocker: () => ({ lock: () => true, unlock() {} }), isAbortError: e => e.name === 'AbortError', registerUiLock() {}, isPosOnline: () => true
        }, PosRender: { buildVariantDisplayName: (name, variant) => variant || name, renderBarcodeAutocompleteLoading() {}, renderBarcodeAutocompleteEmpty() {} }
    }, document: { getElementById: () => badge, addEventListener() {} } };
    vm.runInNewContext(source, context);
    const api = context.window.PosBarcode.create({ posState: { business: { currentOrderId: 1 } }, barcodeSearchState: state,
        elements: { txtBarcode: input, barcodeAutocomplete: popup }, helpers: {
            formatMoney: String, escapeHtml: value => String(value ?? ''), fetchJson: search,
            postJson: async (url, body) => { requests.push({ url, body }); return post(url, body); },
            runPosAction: async (_, key, work, options) => {
                try { const result = await work(); options.onSuccess?.(result); return { ok: true, data: result }; }
                catch (error) { options.onError?.(error); return { ok: false, error }; }
            },
            focusBarcodeInput() { input.focused = true; }, syncDraftToUi() {}, showSuccess() {}, showError() {},
            applyDraftActionSuccess: options => options.afterSync?.()
        } });
    api.bindEvents();
    return { api, input, popup, state, requests, timers,
        type(value) { input.value = value; listeners.input(); },
        enter() { return listeners.keydown({ key: 'Enter', preventDefault() {} }); },
        flush() { for (const work of [...timers]) { timers.delete(work); work(); } }
    };
}
const product = { variantId: 11, productName: 'Bia', displayName: 'Bia', barcode: '111111', price: 20, unitOptions: [] };

test('invalid scan clears the old code and quantity and focuses for the next scan', async () => {
    const f = fixture(undefined, async () => { throw new Error('Không tìm thấy sản phẩm'); });
    f.type('4+'); f.type('999999'); await f.enter(); f.flush();
    assert.equal(f.requests[0].body.quantity, 4);
    assert.equal(f.input.value, '');
    assert.equal(f.input.focused, true);
    assert.equal(f.state.requestedQty, 1);
    assert.equal(f.state.hasQtyPrefix, false);
    assert.equal(f.popup.style.display, 'none');
});

test('failed addition from a name search clears the submitted name', async () => {
    const f = fixture(async () => [product], async () => { throw new Error('Không thể thêm sản phẩm'); });
    f.type('Bia'); await f.enter();
    assert.equal(f.requests.length, 1);
    assert.match(f.requests[0].url, /items\?variantId=11/);
    assert.equal(f.input.value, '');
    assert.equal(f.popup.style.display, 'none');
});

for (const fails of [false, true]) {
    test(`a ${fails ? 'failed' : 'successful'} scan preserves the next input and quantity`, async () => {
        const pending = deferred();
        const f = fixture(undefined, async () => { await pending.promise; if (fails) throw new Error('Lỗi'); return { orderId: 1, lines: [] }; });
        f.type('111111'); const scanning = f.enter();
        f.type('3+'); f.type('222222');
        pending.resolve(); await scanning;
        assert.equal(f.input.value, '222222');
        assert.equal(f.state.requestedQty, 3);
    });
}

test('editing and retyping the same code while a request is pending is preserved', async () => {
    const pending = deferred(), f = fixture(undefined, () => pending.promise);
    f.type('111111'); const scanning = f.enter();
    f.type(''); f.type('111111');
    pending.resolve({ orderId: 1, lines: [] }); await scanning;
    assert.equal(f.input.value, '111111');
});

test('scanner Enter cancels the pending autocomplete instead of showing the old product after success', async () => {
    let searches = 0;
    const f = fixture(async () => { searches++; return [product]; });
    f.type('111111');
    await f.enter(); f.flush();
    assert.equal(searches, 0);
    assert.equal(f.input.value, '');
    assert.equal(f.popup.style.display, 'none');
    assert.equal(f.requests[0].body.barcode, '111111');
});

test('a late search reply cannot reopen suggestions after scanning', async () => {
    const late = deferred(), f = fixture(() => late.promise);
    f.input.value = '111111';
    const searching = f.api.searchBarcodeAutocomplete('111111');
    await f.enter(); late.resolve([product]); await searching;
    assert.equal(f.popup.style.display, 'none');
    assert.equal(f.state.flatItems.length, 0);
});

test('scanning a second barcode while old suggestions exist adds the new barcode exactly once', async () => {
    const f = fixture(async () => [product]);
    f.input.value = 'Bia'; await f.api.searchBarcodeAutocomplete('Bia');
    f.type('222222'); await f.enter();
    assert.equal(f.requests.length, 1);
    assert.equal(f.requests[0].url, '/admin/pos/cart/current/scan');
    assert.equal(f.requests[0].body.barcode, '222222');
});

test('an exact numeric scan never accepts a fuzzy autocomplete match', async () => {
    const f = fixture(async () => [product]);
    f.input.value = '222222'; await f.api.searchBarcodeAutocomplete('222222');
    await f.enter();
    assert.equal(f.requests[0].body.barcode, '222222');
});

test('typing a product name still selects its current suggestion and quantity prefix', async () => {
    const f = fixture(async () => [product]);
    f.type('4+'); f.type('Bia'); await f.api.searchBarcodeAutocomplete('Bia'); await f.enter();
    assert.equal(f.requests[0].url, '/admin/pos/1/items?variantId=11&qty=4');
});

test('an earlier reply cannot overwrite a newer cached product search', async () => {
    const late = deferred(), f = fixture(() => late.promise);
    f.input.value = 'old'; const searching = f.api.searchBarcodeAutocomplete('old');
    f.state.cache.set('bia', [product]); f.type('Bia'); await f.api.searchBarcodeAutocomplete('Bia');
    late.resolve([{ ...product, variantId: 99 }]); await searching;
    await f.enter();
    assert.equal(f.requests[0].url, '/admin/pos/1/items?variantId=11&qty=1');
});
