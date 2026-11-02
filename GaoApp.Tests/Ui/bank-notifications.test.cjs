const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const code = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.bank-notifications.js'), 'utf8');
function fixture(items, status = null, approvals = [], replayPending = false, behavior = {}) {
    const map = new Map();
    class Element {
        constructor(tag) { this.tag = tag; this.children = []; this.events = {}; this.textContent = ''; }
        set id(value) { this._id = value; map.set(value, this); } get id() { return this._id; }
        setAttribute() { }
        appendChild(child) { child.parentElement = this; this.children.push(child); return child; }
        replaceChildren(...children) { this.children = []; for (const child of children) this.appendChild(child); }
        before(child) { this.parentElement.appendChild(child); }
        remove() { this.parentElement.children = this.parentElement.children.filter(c => c !== this); }
        addEventListener(event, handler) { this.events[event] = handler; }
    }
    const root = new Element('root'); const modal = new Element('modal'); modal.id = 'paymentQrModal'; root.appendChild(modal);
    const modalBody = new Element('modal-body'); modal.appendChild(modalBody);
    modal.classList = { contains: () => false }; modal.querySelector = () => modalBody;
    const reviewModal = new Element('modal'); reviewModal.id = 'bankTransferReviewModal'; root.appendChild(reviewModal);
    let reviewVisible = false, reviewTransitioning = false, reviewBusy = false;
    const reviewCloses = [];
    reviewModal.classList = { contains: name => name === 'show' && reviewVisible };
    reviewModal.getAttribute = name => name === 'aria-hidden' ? String(!reviewVisible && !reviewTransitioning) : null;
    const posted = []; const emitted = []; const gets = []; let connected = true, offlinePending = 0, serverArmed = false;
    let now = Date.now(); class FakeDate extends Date { static now() { return now; } }
    const document = { readyState: 'loading', addEventListener() { }, createElement: tag => new Element(tag), getElementById: id => map.get(id), querySelector: () => ({ value: 'test-csrf' }) };
    let review = null, callbacks;
    const window = { PosOffline: { status: () => ({ connected, pending: offlinePending }) },
        PosBankTransferReview: { active: () => review, busy: () => reviewBusy,
            show: async (item, actions) => { review = item; reviewVisible = true; callbacks = actions; },
            close: async restore => { reviewCloses.push(restore); review = null; reviewVisible = false; } },
        addEventListener() { }, dispatchEvent: event => emitted.push(event) };
    const context = { window, document, Date: FakeDate, navigator: { onLine: true }, AbortController, setTimeout, clearTimeout, setInterval() { },
        CustomEvent: class { constructor(name, opts) { this.type = name; this.detail = opts.detail; } },
        fetch: async (url, options) => {
            if (options.method === 'POST') {
                const body = JSON.parse(options.body); posted.push({ url, body, headers: options.headers });
                if (url.endsWith('/confirm') && behavior.confirmFailures?.length) {
                    const failure = behavior.confirmFailures.shift();
                    if (failure === 'network') throw new TypeError('Synthetic lost connection');
                    return { ok: false, status: failure, json: async () => ({ message: 'Synthetic rejection ' + failure }) };
                }
                if (url.endsWith('/confirm') && behavior.confirmGate) await behavior.confirmGate;
                if (url.includes('/arm/')) {
                    const reply = behavior.armReplies?.length ? await behavior.armReplies.shift() : { enabled: true };
                    if (typeof reply === 'number') return { ok: false, status: reply, json: async () => ({ message: 'Synthetic arm rejection ' + reply }) };
                    serverArmed = reply.enabled === true;
                    return { ok: true, json: async () => ({ success: true, ...reply }) };
                }
                if (!replayPending && (url.endsWith('/confirm') || url.endsWith('/dismiss'))) items = items.filter(i => i.receiptId !== body.receiptId);
                if (url.includes('/complete-admin/')) { approvals = []; items = []; }
                return { ok: true, json: async () => url.endsWith('/confirm') || url.includes('/complete-admin/') ? { orderId: 100, qrId: 5, finalized: false, remainingAmount: 50000, paidAmount: 20000 } : { success: true, enabled: true, manual: url.includes('/manual-review/') } };
            }
            gets.push(url);
            if (url.endsWith('/admin-approvals')) return { ok: true, json: async () => approvals };
            if (url.includes('/status/') && status) return { ok: true, json: async () => status };
            return { ok: true, json: async () => behavior.pendingOnlyArmed && !serverArmed ? [] : items };
        }
    };
    vm.runInNewContext(code, context);
    return { window, posted, emitted, gets, map, modalBody, reviewCloses, review: () => review, confirm: () => callbacks.confirm(review), reject: () => callbacks.reject(review),
        advance: milliseconds => { now += milliseconds; }, approve: ids => { approvals = ids; },
        setItems: value => { items = value; }, setStatus: value => { status = value; }, unsynced: value => { offlinePending = value; },
        hideReviewExternally: transitioning => { reviewVisible = false; reviewTransitioning = transitioning === true; },
        finishReviewHide: () => { reviewTransitioning = false; }, setReviewBusy: value => { reviewBusy = value; },
        openModal() { modal.classList.contains = () => true; }, offline() { connected = false; } };
}
const sample = { receiptId: 1, waitId: 2, qrId: 5, orderId: 100, amount: 20000, senderName: 'KHACH HANG THU', transactionCode: 'GD:TEST12345678', postedAtUtc: '2026-10-14T12:05:38Z', automatic: false };
(async () => {
    const manual = fixture([{ ...sample }]);
    await manual.window.PosBankNotifications.refresh();
    assert.equal(manual.posted.length, 0, 'amount matching never auto confirms');
    assert.equal(manual.review().senderName, 'KHACH HANG THU');
    assert.equal(manual.review().amount, 20000);
    await manual.confirm();
    assert.equal(manual.posted[0].body.automatic, false);
    assert.equal(manual.posted[0].headers.RequestVerificationToken, 'test-csrf');
    assert.equal(manual.emitted[0].type, 'bank-notification:completed');
    assert.equal(manual.emitted[0].detail.finalized, false, 'partial transfer keeps server decision to leave order open');
    const skip = fixture([{ ...sample }]);
    await skip.window.PosBankNotifications.refresh();
    await skip.reject();
    assert.match(skip.posted[0].url, /\/dismiss$/);
    assert.equal(skip.emitted[0].type, 'bank-notification:manual-fallback');
    assert.equal(skip.window.PosBankNotifications.isManual(5), true);
    const automatic = fixture([{ ...sample, automatic: true }]);
    await automatic.window.PosBankNotifications.refresh(); await automatic.window.PosBankNotifications.refresh();
    assert.equal(automatic.posted.length, 1); assert.equal(automatic.posted[0].body.automatic, true);
    const offline = fixture([{ ...sample, automatic: true }]); offline.offline();
    await offline.window.PosBankNotifications.refresh(); assert.equal(offline.posted.length, 0);
    const stale = fixture([{ ...sample, automatic: true }, { ...sample, waitId: 3, qrId: 6, orderId: 101 }], null, [], true);
    await stale.window.PosBankNotifications.refresh();
    await stale.window.PosBankNotifications.refresh();
    assert.equal(stale.posted.length, 1, 'one consumed receipt cannot be submitted for a second waiting order');
    assert.equal(stale.review(), null, 'a successful confirmation discards stale suggestions from that poll');
    const escaped = fixture([{ ...sample, senderName: '<img src=x onerror=alert(1)>' }]);
    await escaped.window.PosBankNotifications.refresh();
    assert.equal(escaped.review().senderName, '<img src=x onerror=alert(1)>');
    const inModal = fixture([{ ...sample }]); inModal.openModal(); await inModal.window.PosBankNotifications.refresh();
    assert.equal(inModal.map.get('bankNotificationSuggestions').parentElement, inModal.modalBody, 'cashier can confirm while the QR modal is open');
    const tracked = fixture([]);
    await tracked.window.PosBankNotifications.track({ id: 5 });
    assert.equal(tracked.window.PosBankNotifications.enabled(5), true);
    assert.equal(tracked.emitted[0].type, 'bank-notification:tracking');
    const waiting = fixture([], { qrId: 5, orderId: 100, startedAtUtc: new Date().toISOString(), serverNowUtc: new Date().toISOString(), warningAfterSeconds: 60, manualAfterSeconds: 120, ended: false, manual: false, phones: [] });
    await waiting.window.PosBankNotifications.track({ id: 5, orderId: 100 });
    assert.equal(waiting.window.PosBankNotifications.waitingState(5).warning, false);
    waiting.advance(61000); assert.equal(waiting.window.PosBankNotifications.waitingState(5).warning, true);
    assert.equal(waiting.window.PosBankNotifications.waitingState(5).canManual, false);
    waiting.advance(60000); assert.equal(waiting.window.PosBankNotifications.waitingState(5).canManual, true);
    await waiting.window.PosBankNotifications.beginManual({ id: 5, orderId: 100 });
    assert.equal(waiting.window.PosBankNotifications.isManual(5), true);
    assert.match(waiting.posted.at(-1).url, /manual-review\/5$/);
    assert.ok(!waiting.posted.some(p => p.url.endsWith('/confirm')), 'timeout/manual takeover itself never records money');
    const admin = fixture([{ ...sample }]);
    await admin.window.PosBankNotifications.refresh(); // A no-POS suggestion is currently displayed.
    // Admin command must be received even while the cashier suggestion is open.
    admin.approve([5]); await admin.window.PosBankNotifications.refresh();
    assert.equal(admin.posted.length, 1); assert.match(admin.posted[0].url, /complete-admin\/5$/);
    assert.equal(admin.emitted[0].type, 'bank-notification:completed');
    assert.equal(admin.emitted[0].detail.finalized, false); assert.equal(admin.review(), null);
    await admin.window.PosBankNotifications.refresh(); assert.equal(admin.posted.length, 1);

    for (const failure of [429, 408, 503, 'network']) {
        const retried = fixture([{ ...sample, automatic: true }], null, [], false, { confirmFailures: [failure] });
        await retried.window.PosBankNotifications.refresh();
        assert.equal(retried.posted.length, 1);
        await retried.window.PosBankNotifications.refresh();
        assert.equal(retried.posted.length, 1, 'transient failures respect a bounded retry cooldown');
        retried.advance(3000); await retried.window.PosBankNotifications.refresh();
        assert.equal(retried.posted.length, 2, `${failure}: a fresh automatic pending candidate retries instead of waiting for a cashier forever`);
        assert.deepEqual(retried.posted.map(p => p.body), [{ receiptId: 1, waitId: 2, automatic: true }, { receiptId: 1, waitId: 2, automatic: true }]);
        assert.equal(retried.emitted.filter(e => e.type === 'bank-notification:completed').length, 1);
        await retried.window.PosBankNotifications.refresh(); assert.equal(retried.posted.length, 2);
        assert.ok(retried.gets.filter(url => url.endsWith('/pending')).length >= 3, 'each retry uses fresh server candidates');
    }
    const exhausted = fixture([{ ...sample, automatic: true }], null, [], false, { confirmFailures: [500, 500, 500] });
    await exhausted.window.PosBankNotifications.refresh();
    exhausted.advance(2999); await exhausted.window.PosBankNotifications.refresh(); assert.equal(exhausted.posted.length, 1);
    exhausted.advance(1); await exhausted.window.PosBankNotifications.refresh(); assert.equal(exhausted.posted.length, 2);
    exhausted.advance(5999); await exhausted.window.PosBankNotifications.refresh(); assert.equal(exhausted.posted.length, 2);
    exhausted.advance(1); await exhausted.window.PosBankNotifications.refresh(); assert.equal(exhausted.posted.length, 3);
    exhausted.advance(60000); await exhausted.window.PosBankNotifications.refresh(); assert.equal(exhausted.posted.length, 3, 'three automatic attempts cannot turn into an endless retry loop');
    const retryCard = exhausted.map.get('bankNotificationSuggestions').children[0];
    assert.match(retryCard.children[1].textContent, /Synthetic rejection 500/);
    assert.equal(retryCard.children[2].textContent, 'Thử xác nhận lại');
    await retryCard.children[2].events.click();
    assert.equal(exhausted.posted.length, 4, 'an explicit cashier retry remains available after the bounded automatic attempts');
    assert.equal(exhausted.emitted.filter(e => e.type === 'bank-notification:completed').length, 1);

    for (const failure of [400, 401, 403, 409, 422]) {
        const permanent = fixture([{ ...sample, automatic: true }], null, [], false, { confirmFailures: [failure] });
        await permanent.window.PosBankNotifications.refresh();
        permanent.advance(60000); await permanent.window.PosBankNotifications.refresh();
        assert.equal(permanent.posted.length, 1, `${failure}: permanent server rejection is not retried automatically`);
        assert.match(permanent.map.get('bankNotificationSuggestions').children[0].children[1].textContent, new RegExp('Synthetic rejection ' + failure));
    }
    const requalified = fixture([{ ...sample, automatic: true }], null, [], false, { confirmFailures: [429] });
    await requalified.window.PosBankNotifications.refresh();
    requalified.setItems([{ ...sample, automatic: false }]); requalified.advance(3000);
    await requalified.window.PosBankNotifications.refresh(); assert.equal(requalified.posted.length, 1, 'amount-only candidates cannot inherit an automatic retry');
    assert.equal(requalified.review().receiptId, 1);
    const disappeared = fixture([{ ...sample, automatic: true }], null, [], false, { confirmFailures: ['network'] });
    await disappeared.window.PosBankNotifications.refresh();
    disappeared.setItems([]); disappeared.advance(3000); await disappeared.window.PosBankNotifications.refresh();
    assert.equal(disappeared.posted.length, 1, 'a lost ACK never triggers a second POST after the candidate disappears');
    const moved = fixture([{ ...sample, automatic: true }], null, [], false, { confirmFailures: ['network'] });
    await moved.window.PosBankNotifications.refresh();
    moved.setItems([{ ...sample, automatic: true, waitId: 99, qrId: 99 }]); moved.advance(3000);
    await moved.window.PosBankNotifications.refresh(); assert.equal(moved.posted.length, 1, 'a possibly submitted receipt cannot be retried against a different wait');

    for (const firstArm of [409, { enabled: false }]) {
        const recovering = fixture([{ ...sample, automatic: true }], null, [], false,
            { armReplies: [firstArm, { enabled: true }], pendingOnlyArmed: true });
        await recovering.window.PosBankNotifications.track({ id: 5, orderId: 100 });
        assert.equal(recovering.posted.filter(p => p.url.includes('/arm/')).length, 1);
        assert.equal(recovering.posted.filter(p => p.url.endsWith('/confirm')).length, 0);
        recovering.advance(14999); await recovering.window.PosBankNotifications.refresh();
        assert.equal(recovering.posted.filter(p => p.url.includes('/arm/')).length, 1);
        recovering.advance(1); await recovering.window.PosBankNotifications.refresh();
        assert.equal(recovering.posted.filter(p => p.url.includes('/arm/')).length, 2, 'a missed registration or newly connected phone is retried after fifteen seconds');
        assert.equal(recovering.posted.filter(p => p.url.endsWith('/confirm')).length, 1);
        assert.equal(recovering.window.PosBankNotifications.enabled(5), true);
        await recovering.window.PosBankNotifications.refresh();
        assert.equal(recovering.posted.filter(p => p.url.includes('/arm/')).length, 2, 'a successful registration is retained');
    }
    for (const stopped of ['manual', 'ended']) {
        const stoppedQr = fixture([], null, [], false, { armReplies: [{ enabled: false }, { enabled: true }] });
        await stoppedQr.window.PosBankNotifications.track({ id: 5, orderId: 100 });
        stoppedQr.setStatus({ qrId: 5, orderId: 100, [stopped]: true });
        stoppedQr.advance(15000); await stoppedQr.window.PosBankNotifications.refresh();
        assert.equal(stoppedQr.posted.filter(p => p.url.includes('/arm/')).length, 1, stopped + ' QR never rearms');
    }
    let releaseOldArm;
    const oldArmReply = new Promise(resolve => { releaseOldArm = resolve; });
    const staleActive = fixture([], null, [], false, { armReplies: [oldArmReply, { enabled: true }] });
    const oldTrack = staleActive.window.PosBankNotifications.track({ id: 5, orderId: 100 });
    await staleActive.window.PosBankNotifications.track({ id: 6, orderId: 101 });
    releaseOldArm({ enabled: true }); await oldTrack;
    assert.equal(staleActive.window.PosBankNotifications.enabled(5), undefined, 'an old in-flight arm response cannot restore a stale active QR');
    assert.equal(staleActive.window.PosBankNotifications.enabled(6), true);
    assert.ok(!staleActive.emitted.some(e => e.type === 'bank-notification:tracking' && e.detail.qrId === 5));
    let releaseManualArm;
    const lateManual = fixture([], null, [], false, { armReplies: [new Promise(resolve => { releaseManualArm = resolve; })] });
    const manualTrack = lateManual.window.PosBankNotifications.track({ id: 5, orderId: 100 });
    await lateManual.window.PosBankNotifications.beginManual({ id: 5, orderId: 100 });
    releaseManualArm({ enabled: true }); await manualTrack;
    lateManual.advance(15000); await lateManual.window.PosBankNotifications.refresh();
    assert.equal(lateManual.window.PosBankNotifications.enabled(5), undefined);
    assert.equal(lateManual.posted.filter(p => p.url.includes('/arm/')).length, 1, 'an in-flight reply cannot rearm a QR after manual takeover');
    let releaseConfirm;
    const held = fixture([{ ...sample, automatic: true }], null, [], false,
        { confirmFailures: [429], confirmGate: new Promise(resolve => { releaseConfirm = resolve; }) });
    await held.window.PosBankNotifications.refresh();
    const visibleRetry = held.map.get('bankNotificationSuggestions').children[0].children[2];
    held.advance(3000);
    const automaticRetry = held.window.PosBankNotifications.refresh();
    await new Promise(setImmediate);
    const explicitRetry = visibleRetry.events.click();
    await new Promise(setImmediate);
    assert.equal(held.posted.length, 2, 'cashier retry coalesces with the same automatic POST already in flight');
    releaseConfirm(); await Promise.all([automaticRetry, explicitRetry]);
    assert.equal(held.emitted.filter(e => e.type === 'bank-notification:completed').length, 1);
    const unsynced = fixture([]); await unsynced.window.PosBankNotifications.track({ id: 5 });
    unsynced.unsynced(1); await unsynced.window.PosBankNotifications.refresh();
    assert.match(unsynced.map.get('bankNotificationSuggestions').children[0].textContent, /kết nối|đồng bộ/);
    assert.equal(unsynced.posted.filter(p => p.url.endsWith('/confirm')).length, 0);
    const hiddenReview = fixture([{ ...sample }]);
    await hiddenReview.window.PosBankNotifications.refresh();
    hiddenReview.setItems([{ ...sample, receiptId: 2, waitId: 3, automatic: true }]);
    await hiddenReview.window.PosBankNotifications.refresh();
    assert.equal(hiddenReview.posted.length, 0, 'a visible cashier review keeps automatic confirmation paused');
    assert.equal(hiddenReview.reviewCloses.length, 0);
    hiddenReview.hideReviewExternally(true);
    await hiddenReview.window.PosBankNotifications.refresh();
    assert.equal(hiddenReview.posted.length, 0, 'an externally closing modal keeps its current review until the hide transition finishes');
    assert.equal(hiddenReview.review().receiptId, 1);
    hiddenReview.finishReviewHide(); hiddenReview.setReviewBusy(true);
    await hiddenReview.window.PosBankNotifications.refresh();
    assert.equal(hiddenReview.posted.length, 0, 'a hidden modal cannot interrupt a decision already in flight');
    hiddenReview.setReviewBusy(false);
    await hiddenReview.window.PosBankNotifications.refresh();
    assert.equal(hiddenReview.review(), null, 'a fully hidden stale review releases only its UI owner state');
    assert.equal(hiddenReview.reviewCloses[0], false, 'recovering stale UI does not restore another payment modal');
    assert.deepEqual(hiddenReview.posted.map(p => p.body), [{ receiptId: 2, waitId: 3, automatic: true }], 'only a fresh server candidate can be confirmed, never the stale hidden suggestion');
    assert.ok(!hiddenReview.posted.some(p => p.url.endsWith('/dismiss')));
    assert.equal(hiddenReview.emitted.filter(e => e.type === 'bank-notification:completed').length, 1);
    const hiddenEmpty = fixture([{ ...sample }]); await hiddenEmpty.window.PosBankNotifications.refresh();
    hiddenEmpty.hideReviewExternally(); hiddenEmpty.setItems([]);
    await hiddenEmpty.window.PosBankNotifications.refresh();
    assert.equal(hiddenEmpty.review(), null); assert.equal(hiddenEmpty.posted.length, 0, 'hidden UI recovery alone cannot record or dismiss money');
    const missingReview = fixture([{ ...sample }]); await missingReview.window.PosBankNotifications.refresh();
    missingReview.map.delete('bankTransferReviewModal'); missingReview.setItems([{ ...sample, automatic: true }]);
    await missingReview.window.PosBankNotifications.refresh();
    assert.equal(missingReview.posted.length, 0, 'missing DOM is not proof that an active review was closed');
    console.log('PASS: POS notification UI — manual name/amount confirmation, skip, automatic match, partial payment, offline pause and safe text rendering.');
})().catch(error => { console.error(error); process.exitCode = 1; });
