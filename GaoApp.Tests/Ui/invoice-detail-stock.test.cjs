const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/invoice-detail-stock.js'), 'utf8');
const flush = async () => { await new Promise(setImmediate); await new Promise(setImmediate); };
function element(dataset = {}) {
    const classes = new Set();
    const listeners = {};
    return { dataset, textContent: '', innerHTML: '', title: '', disabled: false,
        classList: { remove: name => classes.delete(name), contains: name => classes.has(name),
            toggle: (name, on) => on ? classes.add(name) : classes.delete(name) },
        addEventListener: (name, callback) => { listeners[name] = callback; },
        fire: name => listeners[name]?.() };
}
function fixture(fetch) {
    const status = element(), refresh = element(), checked = element();
    const rows = [element(), element(), element()];
    const cells = ['401', '402', '403'].map((id, index) => {
        const cell = element({ invoiceStockDetailId: id, stockProductId: index < 2 ? '31' : '' });
        cell.closest = () => rows[index];
        return cell;
    });
    const page = element({ invoiceHeadId: '104568' });
    page.querySelectorAll = () => cells;
    const elements = { invoiceDetailPage: page, invoiceStockStatus: status,
        btnRefreshInvoiceStock: refresh, invoiceStockCheckedAt: checked };
    const events = {};
    const requests = [];
    vm.runInNewContext(source, {
        document: { getElementById: id => elements[id], addEventListener: (name, callback) => { events[name] = callback; } },
        fetch: (url, options) => { requests.push({ url, options }); return fetch(url, options); },
        AbortController, Date, Number, String, Map
    });
    return { status, refresh, checked, rows, cells, requests, changed: () => events['invoice:lines-changed']() };
}
function response(data, ok = true) { return { ok, json: async () => data }; }
function data({ shortage = 1.5, warehouse = 11, canViewStock = true, available = 2.5 } = {}) {
    return { success: true, checkRequired: true, canViewStock, checkedAtUtc: '2026-10-11T06:26:08Z',
        availability: { lines: [{ warehouseId: warehouse, isSufficient: shortage <= 0 }],
            detailLines: [401, 402].map(invoiceDetailId => ({ invoiceDetailId, productVariantId: 31,
                warehouseId: warehouse, warehouseName: 'Kho chính', unitName: 'lốc', baseUnitName: 'hộp',
                unitFactor: 4, availableQuantity: available, requiredQuantity: 4, shortageQuantity: shortage, sharedLineCount: 2 })) } };
}

test('shows current quantity, aggregate shortage and trace links using the invoice line unit', async () => {
    const view = fixture(async () => response(data()));
    await flush();
    assert.equal(view.status.textContent, '1 sản phẩm còn thiếu tồn để phát hành');
    assert.equal(view.status.className, 'fw-semibold text-danger');
    for (const [index, cell] of view.cells.slice(0, 2).entries()) {
        assert.match(cell.innerHTML, /2,5/);
        assert.match(cell.innerHTML, /Thiếu 1,5 lốc/);
        assert.match(cell.innerHTML, /Cần 4 lốc \/ 2 dòng/);
        assert.match(cell.innerHTML, /productVariantId=31&amp;warehouseId=11/);
        assert.match(cell.title, /1 lốc = 4 hộp/);
        assert.equal(view.rows[index].classList.contains('invoice-stock-shortage'), true);
    }
    assert.equal(view.cells[2].textContent, '—');
    assert.match(view.checked.textContent, /13:26:08/);
    assert.equal(view.refresh.disabled, false);
    assert.equal(view.requests[0].url, '/Admin/Invoice/StockAvailability?id=104568');
    assert.equal(view.requests[0].options.cache, 'no-store');
});

test('sufficient stock is green and stock links follow the granted permission', async () => {
    const view = fixture(async () => response(data({ shortage: 0, canViewStock: false })));
    await flush();
    assert.equal(view.status.className, 'fw-semibold text-success');
    assert.match(view.cells[0].innerHTML, /Đủ số lượng/);
    assert.doesNotMatch(view.cells[0].innerHTML, /<a|Thiếu/);
    assert.equal(view.rows[0].classList.contains('invoice-stock-shortage'), false);
});

test('unknown warehouse is explicit and does not present an invented zero stock', async () => {
    const view = fixture(async () => response(data({ warehouse: 0 })));
    await flush();
    assert.match(view.status.textContent, /1 sản phẩm chưa xác định kho/);
    assert.match(view.cells[0].innerHTML, /Chưa xác định kho/);
    assert.doesNotMatch(view.cells[0].innerHTML, /2,5|Tra tồn/);
});

test('editing manual lines refreshes both tables and clears a previous shortage', async () => {
    let attempt = 0;
    const view = fixture(async () => response(data({ shortage: attempt++ ? 0 : 1.5 })));
    await flush();
    await view.changed();
    assert.equal(view.requests.length, 2);
    assert.equal(view.status.className, 'fw-semibold text-success');
    assert.equal(view.rows[0].classList.contains('invoice-stock-shortage'), false);
    assert.equal(view.rows[1].classList.contains('invoice-stock-shortage'), false);
});

test('refresh failures clear old quantities and show a retry action instead of saying stock is sufficient', async () => {
    let attempt = 0;
    const view = fixture(async () => response(data({ shortage: 0 }), attempt++ === 0));
    await flush();
    await view.refresh.fire('click');
    assert.match(view.status.textContent, /Chưa tải được tồn/);
    assert.equal(view.status.className, 'fw-semibold text-warning');
    assert.match(view.cells[0].innerHTML, /Chưa tải được/);
    assert.doesNotMatch(view.cells[0].innerHTML, /Đủ số lượng|2,5/);
    assert.equal(view.refresh.disabled, false);
});

test('a slower old response cannot replace stock after a manual edit', async () => {
    let completeOld, attempt = 0;
    const view = fixture(async () => attempt++ === 0
        ? new Promise(resolve => { completeOld = resolve; })
        : response(data({ shortage: 0, available: 8 })));
    await view.changed();
    completeOld(response(data()));
    await flush();
    assert.equal(view.requests[0].options.signal.aborted, true);
    assert.equal(view.status.className, 'fw-semibold text-success');
    assert.match(view.cells[0].innerHTML, />8 /);
    assert.doesNotMatch(view.cells[0].innerHTML, /Thiếu/);
});

test('issued and correction invoices do not present a misleading pre-issue stock quantity', async () => {
    const view = fixture(async () => response({ success: true, checkRequired: false, message: 'Đã phát hành' }));
    await flush();
    assert.equal(view.status.textContent, 'Đã phát hành');
    assert.match(view.cells[0].innerHTML, /—/);
    assert.doesNotMatch(view.cells[0].innerHTML, /Đủ số lượng/);
});

test('escapes product unit and warehouse text in generated HTML', async () => {
    const payload = data();
    payload.availability.detailLines[0].warehouseName = '<img src=x onerror=alert(1)>';
    payload.availability.detailLines[0].unitName = '<script>bad()</script>';
    const view = fixture(async () => response(payload));
    await flush();
    assert.doesNotMatch(view.cells[0].innerHTML, /<img|<script>/);
    assert.match(view.cells[0].innerHTML, /&lt;img/);
    assert.match(view.cells[0].innerHTML, /&lt;script&gt;/);
});
