'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const templates = require('../../GaoApp.Web/wwwroot/Admin/js/printing/receipt.templates.js');
const qr1 = 'QR-1799607-9a700d6ab6c0402099b673909da876fe';
const qr2 = 'QR-1799607-b6fbd01dee4e44e669f6a2acf971eede';
const freezePayments = payments => Object.freeze(payments.map(Object.freeze));

test('one transfer has a short QR reference even when paid partly by cash', () => {
    const payments = freezePayments([{ method: 'Cash', amount: 30000 }, { method: 'BankTransfer', amount: 50000, reference: qr1 }]);
    assert.deepEqual(templates.paymentDisplayReferences(payments), ['', 'QR-1799607']);
    assert.equal(payments[1].reference, qr1);
});

test('multiple transfers get consecutive suffixes; cash and cards do not consume numbers', () => {
    const payments = freezePayments([
        { method: 'Cash', amount: 30000 },
        { method: 'BankTransfer', amount: 50000, reference: qr1 },
        { method: 'Card', amount: 10000, reference: 'CARD-123' },
        { method: 1, amount: 320000, reference: qr2 },
        { method: '1', amount: 20000, reference: qr1.replace('9a700d6ab6c0402099b673909da876fe', 'A'.repeat(32)) }
    ]);
    const before = JSON.stringify(payments);
    for (let repeat = 0; repeat < 2; repeat++) {
        assert.deepEqual(templates.paymentDisplayReferences(payments), ['', 'QR-1799607-1', 'CARD-123', 'QR-1799607-2', 'QR-1799607-3']);
    }
    assert.equal(JSON.stringify(payments), before);
});

test('manual bank references and non-transfer references keep their full value', () => {
    const payments = freezePayments([
        { method: 'BankTransfer', reference: 'ACB-20261010-123456', amount: 50000 },
        { method: 'BankTransfer', reference: qr1, amount: 50000 },
        { method: 'Cash', reference: qr2, amount: 10000 },
        { method: 'BankTransfer', reference: 'QR-1799607-manual-reference', amount: 5000 },
        { method: 'BankTransfer', reference: null, amount: 5000 }
    ]);
    assert.deepEqual(templates.paymentDisplayReferences(payments), ['ACB-20261010-123456', 'QR-1799607-2', qr2, 'QR-1799607-manual-reference', '']);
    assert.deepEqual(templates.paymentDisplayReferences(), []);
});

test('every bill design uses the same short references without changing stored payment data', () => {
    const payments = freezePayments([
        { method: 'Cash', amount: 30000 },
        { method: 'BankTransfer', amount: 50000, reference: qr1 },
        { method: 'BankTransfer', amount: 320000, reference: qr2 }
    ]);
    const receipt = Object.freeze({ orderId: 1799607, lines: [], payments, subtotal: 400000, grandTotal: 400000, paidTotal: 400000 });
    const before = JSON.stringify(receipt);
    for (const option of templates.builtIns()) {
        for (const offline of [false, true]) {
            const result = templates.render(receipt, option.design, { offline });
            assert.match(result.body, /QR-1799607-1<\/small>/);
            assert.match(result.body, /QR-1799607-2<\/small>/);
            assert.ok(!result.html.includes(qr1) && !result.html.includes(qr2));
            assert.match(result.body, /<span>50\.000<\/span>/);
            assert.match(result.body, /<span>320\.000<\/span>/);
        }
    }
    assert.equal(JSON.stringify(receipt), before);
    const hidden = templates.render(receipt, { showPayments: false });
    assert.ok(!hidden.body.includes('QR-1799607'));
});

test('unrecognized references still render as escaped text', () => {
    const result = templates.render({ lines: [], payments: [{ method: 'BankTransfer', amount: 50000, reference: '<img src=x onerror=alert(1)>' }] }, {});
    assert.match(result.body, /&lt;img src=x onerror=alert\(1\)&gt;/);
    assert.ok(!result.body.includes('<img src=x'));
});
