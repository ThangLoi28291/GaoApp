const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.customer.js'), 'utf8');

for (const hasSwal of [true, false]) {
    test(`customer selection automatically reprices and synchronizes the server draft (Swal ${hasSwal ? 'available' : 'absent'})`, async () => {
        const originalDraft = { customerId: null, lines: [{ lineId: 1, quantity: 24 }], grandTotal: 100000 };
        const posState = { business: { currentDraft: originalDraft } };
        const requests = [];
        const displayed = [];
        let modalHidden = 0;
        let reply;
        const window = { PosCommon: {
            debounce: fn => fn,
            createTtlCache: () => new Map(),
            clearInlineError() {},
            registerUiLock() {}
        } };
        const context = { window, document: { getElementById: () => null } };
        if (hasSwal) context.Swal = { fire() { assert.fail('Customer selection must not ask for price confirmation'); } };
        vm.runInNewContext(source, context);

        const customer = window.PosCustomer.create({
            posState,
            customerSearchState: {},
            elements: {},
            modals: { customerModal: { hide() { modalHidden++; } } },
            helpers: {
                async postJson(url, body) {
                    requests.push({ url, repriceExistingLines: body.repriceExistingLines });
                    return reply;
                },
                async runPosAction(state, key, action, options) {
                    assert.equal(state, posState);
                    assert.match(key, /^customer:set:\d+:reprice$/);
                    assert.equal(options.requireOnline, true);
                    const result = await action();
                    options.onSuccess(result);
                    return result;
                },
                applyDraftActionSuccess(options) {
                    posState.business.currentDraft = options.draft;
                    displayed.push(options.draft);
                    options.afterSync?.();
                }
            }
        });

        // Selecting wholesale, switching back to retail, and selecting with an empty cart
        // all request repricing without a dialog and render the returned server prices.
        for (const selection of [
            { customerId: 7, customerPriceTier: 'WHOLESALE', lines: originalDraft.lines, grandTotal: 96000 },
            { customerId: 8, customerPriceTier: 'RETAIL', lines: originalDraft.lines, grandTotal: 100000 },
            { customerId: 9, customerPriceTier: 'RETAIL', lines: [], grandTotal: 0 }
        ]) {
            if (!selection.lines.length) posState.business.currentDraft = { lines: [], grandTotal: 0 };
            reply = selection;
            assert.equal(await customer.setCustomerToCurrentCart(selection.customerId), selection);
            assert.equal(posState.business.currentDraft, selection);
            assert.equal(displayed.at(-1), selection);
        }
        assert.deepEqual(requests, [7, 8, 9].map(id => ({
            url: `/admin/pos/cart/current/customer/${id}`,
            repriceExistingLines: true
        })));
        assert.equal(modalHidden, 3);
        assert.equal(originalDraft.customerId, null);
        assert.equal(originalDraft.grandTotal, 100000);
    });
}
