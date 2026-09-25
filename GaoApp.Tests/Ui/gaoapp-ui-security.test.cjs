const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

function boot() {
    const calls = [], sent = [];
    const window = {
        location: { href: 'https://shop.test/admin', origin: 'https://shop.test' },
        addEventListener() {},
        toastr: { options: {}, error: (...args) => calls.push(args) },
        fetch: (url, init) => { sent.push({ url, init }); return Promise.resolve({ ok: true }); }
    };
    const document = {
        querySelector: () => ({ getAttribute: () => 'session-csrf' }),
        querySelectorAll: () => [], addEventListener() {}
    };
    vm.runInNewContext(fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/gaoapp.ui.js'), 'utf8'),
        { window, document, URL, Headers, Request });
    return { window, calls, sent };
}

test('untrusted toast content is always passed to toastr with HTML escaping enabled', () => {
    const { window, calls } = boot();
    assert.equal(window.toastr.options.escapeHtml, true);
    const payload = '<img src=x onerror=alert(document.cookie)>';
    window.GaoAppNotify.error(payload, '<svg onload=alert(1)>', { escapeHtml: false, timeOut: 900 });
    assert.equal(calls[0][0], payload);
    assert.equal(calls[0][2].escapeHtml, true);
    assert.equal(calls[0][2].timeOut, 900);
});

test('same-origin writes receive CSRF token and cross-origin requests do not disclose it', async () => {
    const { window, sent } = boot();
    await window.fetch('/admin/brand/create', { method: 'POST' });
    await window.fetch('https://external.test/upload', { method: 'POST' });
    await window.fetch('/admin/brand/index', { method: 'GET' });
    assert.equal(sent[0].init.headers.get('RequestVerificationToken'), 'session-csrf');
    assert.equal(new Headers(sent[1].init.headers).has('RequestVerificationToken'), false);
    assert.equal(new Headers(sent[2].init.headers).has('RequestVerificationToken'), false);
});
