const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/acb.callback-monitor.js'), 'utf8');
function fixture(fetch) {
    const elements = {};
    for (const id of ['acb-callback-health', 'acb-refresh-status', 'acb-live-status', 'acb-monitor-message'])
        elements[id] = { events: {}, addEventListener(name, handler) { this.events[name] = handler; } };
    elements['acb-callback-health'].dataset = { statusUrl: '/admin/acb/reconciliation/callback-status' };
    elements['acb-callback-health'].querySelector = () => ({ open: true });
    const document = { hidden: false, getElementById: id => elements[id] };
    let interval;
    vm.runInNewContext(source, { document, fetch, AbortController, setTimeout, clearTimeout,
        setInterval(fn, ms) { assert.equal(ms, 10000); interval = fn; } });
    return { elements, document, tick: () => interval(), click: () => elements['acb-refresh-status'].events.click() };
}
test('Monitoring refresh reads the protected local status endpoint and preserves expanded diagnostics', async () => {
    const f = fixture(async (url, options) => {
        assert.equal(url, '/admin/acb/reconciliation/callback-status');
        assert.equal(options.credentials, 'same-origin');
        assert.equal(options.cache, 'no-store');
        assert.equal(options.method, undefined); // GET, never a bank callback or payment command.
        return { ok: true, redirected: false, text: async () => '<section>receipt</section>' };
    });
    await f.click();
    assert.equal(f.elements['acb-callback-health'].innerHTML, '<section>receipt</section>');
    assert.equal(f.elements['acb-refresh-status'].disabled, false);
});
test('Monitoring does not overlap requests or run in a hidden tab', async () => {
    let calls = 0, resolve;
    const f = fixture(() => { calls++; return new Promise(r => { resolve = r; }); });
    f.elements['acb-live-status'].checked = true;
    const running = f.click();
    f.tick();
    assert.equal(calls, 1);
    resolve({ ok: true, text: async () => 'ok' });
    await running;
    f.document.hidden = true;
    f.tick();
    assert.equal(calls, 1);
});
test('Monitoring keeps the last evidence on session redirects or failed responses', async () => {
    const f = fixture(async () => ({ ok: true, redirected: true, text: async () => 'login' }));
    f.elements['acb-callback-health'].innerHTML = 'saved evidence';
    await f.click();
    assert.equal(f.elements['acb-callback-health'].innerHTML, 'saved evidence');
    assert.match(f.elements['acb-monitor-message'].textContent, /phiên đăng nhập/);
});
