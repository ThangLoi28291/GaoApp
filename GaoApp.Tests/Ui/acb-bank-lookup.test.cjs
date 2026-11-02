const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/acb.reconciliation-bank.js'), 'utf8');
const flush = () => new Promise(setImmediate);

function element() {
    const listeners = new Map();
    return { disabled: false, textContent: '', dataset: {}, children: [], attributes: {}, innerHTML: '',
        addEventListener(type, fn) { listeners.set(type, fn); },
        fire(type, event = {}) { return listeners.get(type)?.(event); },
        click() { if (!this.disabled) return this.fire('click'); },
        setAttribute(name, value) { this.attributes[name] = value; },
        replaceChildren() { this.children = []; this.innerHTML = ''; }, append(child) { this.children.push(child); },
        contains() { return true; }
    };
}

function harness(fetch) {
    const from = element(); from.value = '2026-10-01';
    const to = element(); to.value = '2026-10-12';
    const filters = element(); filters.elements = { from, to, source: { value: 'TRANSACTION_HISTORY' } }; filters.valid = true;
    filters.reportValidity = () => filters.valid;
    const form = element(); form.action = 'https://store.test/admin/acb/reconciliation/bank-lookup';
    form.querySelector = () => ({ value: 'fixture-csrf-token' });
    const button = element();
    const results = element(); results.pageInput = { value: '8', reportValidity: () => true };
    results.querySelector = () => results.pageInput;
    const callback = element(); callback.innerHTML = 'real-callback-count-0';
    const elements = { 'acb-report-filter': filters, 'acb-bank-lookup-form': form,
        'acb-bank-lookup': button, 'acb-bank-results': results, 'acb-callback-health': callback };
    const requests = [];
    const timers = new Map(); let nextTimer = 0;
    vm.runInNewContext(source, { document: { getElementById: id => elements[id], createElement: element },
        URLSearchParams, AbortController,
        fetch: (url, options) => { requests.push({ url, options }); return fetch(url, options); },
        setTimeout: fn => { timers.set(++nextTimer, fn); return nextTimer; }, clearTimeout: id => timers.delete(id) });
    return { filters, from, to, button, results, callback, requests, timers,
        message: () => results.children.map(x => x.textContent).join('') };
}
const success = html => ({ ok: true, status: 200, redirected: false,
    headers: { get: name => name === 'X-Acb-Data-Source' ? 'bank-lookup' : null }, text: async () => html });
const pageTarget = (page, jump = false) => ({ dataset: jump ? {} : { bankPage: String(page) },
    hasAttribute: () => jump, closest() { return this; } });

test('bank query sends selected dates, zero callback filters, protected POST and leaves callback data intact', async () => {
    const h = harness(async () => success('<p>bank-page-1</p>'));
    h.button.click(); assert.equal(h.button.disabled, true);
    await flush();
    const { url, options } = h.requests[0];
    assert.equal(url, 'https://store.test/admin/acb/reconciliation/bank-lookup');
    assert.equal(options.method, 'POST'); assert.equal(options.cache, 'no-store');
    assert.equal(options.credentials, 'same-origin');
    assert.equal(options.headers.RequestVerificationToken, 'fixture-csrf-token');
    assert.equal(options.body.toString(), 'from=2026-10-01&to=2026-10-12&page=1');
    assert.equal(h.results.innerHTML, '<p>bank-page-1</p>');
    assert.equal(h.callback.innerHTML, 'real-callback-count-0');
    assert.equal(h.button.disabled, false); assert.equal(h.timers.size, 0);
});

test('jumping to page 8 makes one bank page request and preserves the queried date range', async () => {
    const h = harness(async () => success('<p>bank-page</p>'));
    h.button.click(); await flush();
    h.results.fire('click', { target: pageTarget(8, true) }); await flush();
    assert.equal(h.requests.length, 2);
    assert.equal(h.requests[1].options.body.toString(), 'from=2026-10-01&to=2026-10-12&page=8');
    h.results.pageInput.reportValidity = () => false;
    h.results.fire('click', { target: pageTarget(9999, true) }); await flush();
    assert.equal(h.requests.length, 2);
});

test('changing dates cancels stale responses and cannot overwrite newer bank data', async () => {
    const deferred = [];
    const h = harness(() => new Promise(resolve => deferred.push(resolve)));
    h.button.click();
    h.from.value = '2026-10-10'; h.from.fire('change');
    assert.equal(h.requests[0].options.signal.aborted, true);
    assert.match(h.message(), /Đã đổi khoảng ngày/);
    h.button.click();
    deferred[0](success('<p>old-private-results</p>')); await flush();
    assert.equal(h.button.disabled, true);
    assert.doesNotMatch(h.results.innerHTML, /old-private-results/);
    deferred[1](success('<p>new-results</p>')); await flush();
    assert.equal(h.results.innerHTML, '<p>new-results</p>');
    assert.equal(h.button.disabled, false);
});

test('bank errors are plain text, clear prior results and keep callback evidence unchanged', async () => {
    const h = harness(async () => ({ ok: false, status: 502, json: async () => ({ detail: '<img src=x onerror=alert(1)>' }) }));
    h.button.click(); await flush();
    assert.equal(h.results.innerHTML, '');
    assert.equal(h.message(), '<img src=x onerror=alert(1)>');
    assert.equal(h.results.children[0].attributes.role, 'alert');
    assert.equal(h.callback.innerHTML, 'real-callback-count-0');
});

for (const kind of ['redirect', '403', 'unexpected-source']) {
    test(`bank query handles ${kind} without inserting a login or unrelated page`, async () => {
        const response = success('<script>private-login-page</script>');
        if (kind === 'redirect') response.redirected = true;
        if (kind === '403') response.status = 403;
        if (kind === 'unexpected-source') response.headers.get = () => null;
        const h = harness(async () => response); h.button.click(); await flush();
        assert.equal(h.results.innerHTML, ''); assert.equal(h.button.disabled, false);
        assert.doesNotMatch(h.message(), /private-login-page/);
    });
}

test('invalid dates prevent a bank request', () => {
    const h = harness(async () => success('unused'));
    h.filters.valid = false; h.button.click(); assert.equal(h.requests.length, 0);
});
