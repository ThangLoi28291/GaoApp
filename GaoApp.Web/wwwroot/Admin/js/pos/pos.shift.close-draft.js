/* A closing count is a browser draft, scoped to its store, cashier, terminal and shift. */
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else root.PosShiftCloseDraft = api;
})(typeof window === 'undefined' ? globalThis : window, function () {
    'use strict';
    const denoms = [500000, 200000, 100000, 50000, 20000, 10000, 5000, 2000, 1000];
    function key(context) {
        const ids = ['storeId', 'userId', 'terminalId', 'shiftId'].map(k => Number(context?.[k]));
        if (ids.some(id => !Number.isSafeInteger(id) || id <= 0)) throw Error('Chưa xác định đủ thông tin ca để lưu kiểm đếm.');
        return 'pos:shift-close:' + ids.join(':');
    }
    function validate(draft) {
        return draft?.version === 1 && Number.isSafeInteger(draft.actual) && draft.actual >= 0 && draft.actual < 1e16 &&
            typeof draft.note === 'string' && draft.note.length <= 300 && Array.isArray(draft.denominations) &&
            draft.denominations.length <= denoms.length && new Set(draft.denominations.map(x => x.value)).size === draft.denominations.length &&
            draft.denominations.every(x => denoms.includes(x.value) && Number.isSafeInteger(x.quantity) && x.quantity >= 0 &&
                Number.isSafeInteger(x.value * x.quantity)) && Number.isFinite(draft.savedAt);
    }
    function save(context, count, storage = sessionStorage) {
        const draft = { version: 1, actual: Number(count.actual), note: String(count.note || ''),
            denominations: count.denominations.map(x => ({ value: Number(x.value), quantity: Number(x.quantity) })), savedAt: Date.now() };
        if (!validate(draft)) throw Error('Bảng kiểm đếm không hợp lệ. Vui lòng kiểm tra số tiền và số tờ.');
        storage.setItem(key(context), JSON.stringify(draft));
        return draft;
    }
    function read(context, storage = sessionStorage) {
        const raw = storage.getItem(key(context));
        if (!raw) return null;
        const draft = JSON.parse(raw);
        if (!validate(draft)) throw Error('Không đọc được bảng kiểm đếm đã lưu. Vui lòng kiểm đếm lại.');
        return Date.now() - draft.savedAt <= 86400000 ? draft : null;
    }
    function clear(context, storage = sessionStorage) { storage.removeItem(key(context)); }
    return { key, save, read, clear };
});
