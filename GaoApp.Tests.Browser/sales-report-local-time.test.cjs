'use strict';
// Executes the actual native formatter and renderers. --source accepts an unapplied candidate.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { spawnSync } = require('node:child_process');
const { test } = require('node:test');

const sourceFlag = process.argv.indexOf('--source');
const sourcePath = path.resolve(sourceFlag >= 0 ? process.argv[sourceFlag + 1] : path.join(__dirname, '../GaoApp.Web/wwwroot/Admin/js/reports/sales-report.page.js'));

function actualNativeFunctions() {
    const source = fs.readFileSync(sourcePath, 'utf8');
    const formatter = source.match(/^    const dateTimeLocal = value => \{[\s\S]*?^    \};/m)?.[0];
    assert.ok(formatter, 'Actual native dateTimeLocal formatter exists in selected source');
    const first = source.indexOf('    const renderers = {');
    const last = source.indexOf('\n    const meta = {', first);
    assert.ok(first >= 0 && last > first, 'Actual native Sales detail renderers exist');
    const renderers = source.slice(first, last);
    const rows = [];
    const element = tag => ({ tag, children: [], textContent: '', className: '', innerHTML: '', append(...children) { this.children.push(...children); } });
    const context = {
        Intl, Date, Number,
        document: { createElement: element },
        td: (text, className) => ({ ...element('td'), text, className }),
        setHead: () => {},
        money: value => String(value ?? ''),
        number: value => String(value ?? ''),
        orderLink: orderId => ({ orderId }),
        els: { body: { append(row) { rows.push(row); } } }
    };
    vm.runInNewContext(formatter + '\n' + renderers + '\nglobalThis.native = { dateTimeLocal, renderers };', context, { filename: sourcePath });
    return { ...context.native, rows, source };
}

const probeValues = [
    '2026-10-27T08:29:33.7417138', '2026-10-27T01:29:33Z',
    '2026-10-27T08:29:33+07:00', '2026-10-27T23:59:59.9999999'
];
if (process.argv.includes('--probe')) {
    const { dateTimeLocal } = actualNativeFunctions();
    console.log(JSON.stringify(probeValues.map(dateTimeLocal)));
} else {
    test('already-local DTO wall time stays 08:29 instead of being shifted to 15:29', () => {
        const { dateTimeLocal } = actualNativeFunctions();
        const actual = dateTimeLocal('2026-10-27T08:29:33.7417138');
        assert.equal(actual, '08:29 27/10/26');
        assert.notEqual(actual, '15:29 27/10/26');
    });

    test('explicit UTC Z input is still converted exactly once to report local time', () => {
        const { dateTimeLocal } = actualNativeFunctions();
        assert.equal(dateTimeLocal('2026-10-27T01:29:33Z'), '08:29 27/10/26');
        assert.equal(dateTimeLocal('2026-10-27T01:29:33+00:00'), '08:29 27/10/26');
    });

    test('SQL fractional seconds retain local hour and minute for zero through seven digits', () => {
        const { dateTimeLocal } = actualNativeFunctions();
        for (const fraction of ['', '.7', '.74', '.741', '.7417', '.74171', '.741713', '.7417138']) {
            assert.equal(dateTimeLocal('2026-10-27T08:29:33' + fraction), '08:29 27/10/26');
        }
    });

    test('explicit +07:00 and +0700 offsets remain authoritative and are not appended twice', () => {
        const { dateTimeLocal } = actualNativeFunctions();
        assert.equal(dateTimeLocal('2026-10-27T08:29:33+07:00'), '08:29 27/10/26');
        assert.equal(dateTimeLocal('2026-10-27T08:29:33+0700'), '08:29 27/10/26');
        assert.equal(dateTimeLocal('2026-10-27T09:29:33+08:00'), '08:29 27/10/26');
    });

    test('null, missing and invalid local timestamps show the native unavailable placeholder', () => {
        const { dateTimeLocal } = actualNativeFunctions();
        for (const value of [null, undefined, '', 'not-a-date', '2026-13-27T08:29:33', '2026-10-27T99:29:33']) {
            assert.equal(dateTimeLocal(value), '—');
        }
    });

    test('wall time near midnight keeps its date while explicit UTC crosses the date once', () => {
        const { dateTimeLocal } = actualNativeFunctions();
        assert.equal(dateTimeLocal('2026-10-27T23:59:59.9999999'), '23:59 27/10/26');
        assert.equal(dateTimeLocal('2026-10-28T00:03:00'), '00:03 28/10/26');
        assert.equal(dateTimeLocal('2026-10-27T17:03:00Z'), '00:03 28/10/26');
    });

    test('actual formatter is independent of the browser or Node host timezone', () => {
        const expected = ['08:29 27/10/26', '08:29 27/10/26', '08:29 27/10/26', '23:59 27/10/26'];
        for (const timezone of ['UTC', 'Asia/Ho_Chi_Minh', 'America/Los_Angeles', 'Asia/Tokyo']) {
            const child = spawnSync(process.execPath, [__filename, '--source', sourcePath, '--probe'], { encoding: 'utf8', env: { ...process.env, TZ: timezone } });
            assert.equal(child.status, 0, timezone + ': ' + child.stderr);
            assert.deepEqual(JSON.parse(child.stdout), expected, timezone);
        }
    });

    test('all four actual CompletedAtLocal renderers use the local formatter', () => {
        const { renderers, rows, source } = actualNativeFunctions();
        assert.equal((source.match(/dateTimeLocal\(x\.completedAtLocal\)/g) || []).length, 4);
        assert.equal((source.match(/dateTime\(x\.completedAtLocal\)/g) || []).length, 0);
        const value = { orderId: 1, orderNumber: 'LOCAL-1', returnNumber: 'RETURN-1', completedAtLocal: '2026-10-27T08:29:33.7417138',
            customerName: 'Khách kiểm thử', returnCount: 0, isReconciled: true, typeLabel: 'Trả hàng' };
        for (const name of ['orders', 'discounts', 'returns', 'void']) {
            rows.length = 0;
            renderers[name]({ items: [value] });
            assert.equal(rows.length, 1, name + ' actual row');
            assert.equal(rows[0].children[1].text, '08:29 27/10/26', name + ' actual local timestamp cell');
        }
    });
}
