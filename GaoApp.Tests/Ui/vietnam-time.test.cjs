const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const templates = require('../../GaoApp.Web/wwwroot/Admin/js/printing/receipt.templates.js');
const root = path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js');
function functions(file, names) {
    const source = fs.readFileSync(path.join(root, file), 'utf8').replace(/\r\n/g, '\n');
    const code = names.map(name => {
        const match = source.match(new RegExp(`^([ \\t]*)function ${name}\\([\\s\\S]*?^\\1}`, 'm'));
        assert.ok(match, `${file}: ${name}`);
        return match[0];
    }).join('\n');
    return vm.runInNewContext(code + `\n({${names.join(',')}})`, { Date, Intl, Number, String });
}
const equivalent = ['2026-10-11T06:26:08', '2026-10-11T06:26:08Z', '2026-10-11T13:26:08+07:00'];

test('every receipt preset uses Vietnam completion time, with SQL timestamps and explicit offsets', () => {
    for (const preset of templates.builtIns()) {
        for (const stamp of equivalent) {
            const receipt = { createdAtUtc: '2026-10-11T06:03:00', finalizedAtUtc: stamp, grandTotal: 239040 };
            for (const offline of [false, true]) {
                const body = templates.render(receipt, preset.design, { offline }).body;
                assert.match(body, /<td>Ngày bán<\/td><td>13:26:08 11\/10\/2026<\/td>/);
                assert.doesNotMatch(body, /06:26:08/);
            }
        }
    }
});

test('receipt date rolls over at Vietnam midnight and QR expiry uses the same timezone', () => {
    const body = templates.render({ createdAtUtc: '2026-10-10T18:26:08' }, {}, {
        invoiceBuyerQrDataUrl: 'data:image/png;base64,AA==',
        invoiceBuyerQrExpiresAtUtc: '2026-10-11T08:26:08'
    }).body;
    assert.match(body, /01:26:08 11\/10\/2026/);
    assert.match(body, /15:26:08 11\/10\/2026/);
    assert.match(templates.render({ completedAtUtc: equivalent[0] }, {}).body, /13:26:08 11\/10\/2026/);
});

test('POS detail, shift and audit formatters agree for UTC, SQL and already offset timestamps', () => {
    for (const [file, name, dependencies] of [
        ['pos/pos.order.detail.page.js', 'formatDateTime', []],
        ['pos/pos.shift-handover-slip.page.js', 'formatDateTime', []],
        ['pos/pos-shift-manager-dashboard.js', 'dt', []],
        ['pos/pos.dashboard.page.js', 'formatDateTime', ['parseDate']],
        ['stock-count.js', 'stockCountFormatDateTime', []],
        ['audit-log-index.js', 'auditVietnamTime', []]
    ]) {
        const api = functions(file, [...dependencies, name]);
        const expected = api[name](equivalent[0]);
        assert.match(expected, /13:26/);
        assert.match(expected, /11\/10\/2026/);
        for (const value of equivalent) assert.equal(api[name](value), expected, `${file}: ${value}`);
        assert.match(api[name]('2026-10-10T18:26:08'), /11\/10\/2026/);
    }
});

test('Vietnam calendar filters include the whole local day, independent of computer timezone', () => {
    const manager = functions('pos/pos-shift-manager-dashboard.js', ['dateInput', 'fromDateUtc', 'toDateUtcExclusive']);
    const handover = functions('pos/pos.shift-handover-slip.page.js', ['toUtcStart', 'toUtcEndExclusive']);
    assert.equal(manager.dateInput(new Date('2026-10-10T18:26:08Z')), '2026-10-11');
    for (const [start, end] of [[manager.fromDateUtc, manager.toDateUtcExclusive], [handover.toUtcStart, handover.toUtcEndExclusive]]) {
        assert.equal(start('2026-10-11'), '2026-10-10T17:00:00.000Z');
        assert.equal(end('2026-10-11'), '2026-10-11T17:00:00.000Z');
        assert.equal(start('2026-12-31'), '2026-12-30T17:00:00.000Z');
        assert.equal(end('2026-12-31'), '2026-12-31T17:00:00.000Z');
    }
});

test('datetime inputs show Vietnam wall time, while calendar-only transfer dates retain their date', () => {
    for (const [file, name] of [['promotions.page.js','toDateTimeLocal'], ['stock-count.js','stockCountToLocalInputValue']]) {
        const api = functions(file, [name]);
        for (const value of equivalent) assert.equal(api[name](value), '2026-10-11T13:26');
        assert.equal(api[name]('2026-10-10T18:26:08Z'), '2026-10-11T01:26');
    }
    const display = functions('display-promotion.js', ['toDateTimeLocal']);
    assert.equal(display.toDateTimeLocal('2026-10-11T13:26:08'), '2026-10-11T13:26');
    assert.equal(display.toDateTimeLocal(equivalent[0], true), '2026-10-11T13:26');
    const transfer = functions('stock-transfer.js', ['transferToLocalDateTime']);
    assert.equal(transfer.transferToLocalDateTime('2026-10-11T00:00:00'), '2026-10-11T00:00');
});

test('POS chart hour and day labels use Vietnam boundaries', () => {
    const api = functions('pos/pos.dashboard.page.js', ['formatBucketLabel', 'formatBucketDetailLabel', 'floorToLocalHour']);
    const date = new Date('2026-10-10T18:26:08Z');
    assert.equal(api.formatBucketLabel(date, false), '01h');
    assert.equal(api.formatBucketLabel(date, true), '01h · 11/10');
    assert.equal(api.formatBucketDetailLabel(date), '01h · 11/10');
    assert.equal(api.floorToLocalHour(date).toISOString(), '2026-10-10T18:00:00.000Z');
});
