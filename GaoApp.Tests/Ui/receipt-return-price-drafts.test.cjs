const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/stock-document-management.js'), 'utf8');

function fixture(kind, controls = {}) {
    const ids = kind === 'reject'
        ? { button: 'btnReject', note: 'rejectNote', message: 'rejectMessage' }
        : { button: 'btnConfirmReturnToEdit', note: 'returnToEditNote', message: 'returnToEditMessage' };
    const button = { disabled: false }, message = { textContent: '' };
    const elements = new Map([[ids.button, button], [ids.note, { value: 'Nhập bù hàng thiếu' }], [ids.message, message]]);
    const ignore = { disabled: false };
    if (kind === 'return') {
        elements.set('btnIgnoreRevisionRequest', ignore);
        elements.get(ids.note).focus = () => events.push('focus-reason');
    }
    const events = [], requests = [];
    const location = { href: '', reload() { events.push('reload'); } };
    const window = { location, stockDocumentPage: { documentId: 4460, rowVersion: 'before-save' } };
    if (!controls.noPriceDrafts) window.GaoReceiptPriceDrafts = {
        async hold() {
            events.push('hold');
            if (controls.gate) await controls.gate;
            if (controls.saveFails) throw new Error('Giá nhập chưa được lưu');
            window.stockDocumentPage.rowVersion = 'after-save';
            events.push('saved');
            return () => events.push('release');
        }
    };
    const context = vm.createContext({
        window, location, TypeError, console,
        document: { addEventListener() {}, getElementById: id => elements.get(id) || null },
        alert: text => events.push('alert:' + text),
        fetch: async (url, options) => {
            events.push('post');
            requests.push({ url, body: JSON.parse(options.body) });
            if (controls.networkFails) throw new TypeError('Failed to fetch');
            return new Response(JSON.stringify({ message: controls.requestFails ? 'Phiếu đã thay đổi' : 'Đã trả phiếu' }),
                { status: controls.requestFails ? 409 : 200 });
        }
    });
    vm.runInContext(source, context);
    vm.runInContext(kind === 'reject' ? 'bindReject();' : 'bindRevisionRequestActions();', context);
    return { button, ignore, elements, message, events, requests, location };
}

test('rejecting a staff revision request requires an explicit reason and retains the request on blank input', async () => {
    const f = fixture('return');
    f.ignore.onclick();
    await f.button.onclick();
    assert.equal(f.requests.length, 0);
    assert.match(f.message.textContent, /Nhập lý do/);
    assert.ok(f.events.includes('focus-reason'));
    assert.ok(!f.events.includes('reload'));
    f.elements.get('returnToEditNote').value = 'Đã kiểm đếm cùng nhân viên, số lượng hiện tại chính xác';
    await f.button.onclick();
    assert.equal(f.requests.length, 1);
    assert.equal(f.requests[0].body.returnToEdit, false);
    assert.equal(f.requests[0].body.rowVersion, 'after-save');
    assert.equal(f.requests[0].body.note, 'Đã kiểm đếm cùng nhân viên, số lượng hiện tại chính xác');
});

for (const kind of ['reject', 'return']) {
    test(`${kind}: finish pending prices before returning, use fresh version, and ignore repeated clicks`, async () => {
        let finish;
        const f = fixture(kind, { gate: new Promise(resolve => { finish = resolve; }) });
        const sending = f.button.onclick();
        assert.equal(f.button.disabled, true);
        await f.button.onclick();
        assert.equal(f.requests.length, 0, 'Do not unlock the receipt while price autosave is pending.');
        finish();
        await sending;
        assert.deepEqual(f.events.slice(0, 3), ['hold', 'saved', 'post']);
        assert.equal(f.requests.length, 1);
        assert.equal(f.requests[0].body.rowVersion, 'after-save');
        assert.equal(f.requests[0].body.note, 'Nhập bù hàng thiếu');
        assert.equal(f.requests[0].url, kind === 'reject'
            ? '/admin/api/stock-documents/4460/reject'
            : '/admin/api/stock-documents/4460/resolve-revision-request');
        if (kind === 'return') assert.equal(f.requests[0].body.returnToEdit, true);
        assert.equal(f.events.filter(x => x === 'release').length, 1);
        assert.equal(f.button.disabled, false);
        assert.equal(f.message.textContent, '');
        assert.ok(kind === 'reject' ? f.location.href : f.events.includes('reload'));
    });

    test(`${kind}: failed price save keeps the receipt open and does not send the return`, async () => {
        const f = fixture(kind, { saveFails: true });
        await f.button.onclick();
        assert.equal(f.requests.length, 0);
        assert.match(f.message.textContent, /chưa được lưu/);
        assert.equal(f.button.disabled, false);
        assert.equal(f.location.href, '');
        assert.ok(!f.events.includes('reload'));
    });

    for (const failure of ['requestFails', 'networkFails']) test(`${kind}: ${failure} releases price inputs and permits retry`, async () => {
        const controls = { [failure]: true };
        const f = fixture(kind, controls);
        await f.button.onclick();
        assert.equal(f.requests.length, 1);
        assert.equal(f.events.filter(x => x === 'release').length, 1);
        assert.equal(f.button.disabled, false);
        assert.match(f.message.textContent, failure === 'requestFails' ? /đã thay đổi/ : /Không kết nối/);
        assert.equal(f.location.href, '');
        assert.ok(!f.events.includes('reload'));
        controls[failure] = false;
        await f.button.onclick();
        assert.equal(f.requests.length, 2);
        assert.equal(f.message.textContent, '');
    });

    test(`${kind}: receipts without commercial price editing can still be returned`, async () => {
        const f = fixture(kind, { noPriceDrafts: true });
        await f.button.onclick();
        assert.equal(f.requests.length, 1);
        assert.equal(f.requests[0].body.rowVersion, 'before-save');
        assert.equal(f.button.disabled, false);
    });
}
