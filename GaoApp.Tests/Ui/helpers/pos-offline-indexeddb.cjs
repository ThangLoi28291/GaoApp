'use strict';
// Async transactions with rollback; actual IndexedDB isolation is additionally exercised in Chrome.
module.exports = function memoryDatabase(stores, controls = {}) {
    return { close() {}, transaction(names, mode) {
        const selected = Array.isArray(names) ? names : [names];
        const staged = new Map(selected.map(name => [name, structuredClone(stores.get(name) || new Map())]));
        let pending = 0, ended = false, scheduled = false;
        const tx = { abort() { if (!ended) { ended = true; queueMicrotask(() => tx.onabort?.()); } } };
        function finish() {
            if (scheduled || ended) return;
            scheduled = true;
            setImmediate(() => {
                scheduled = false;
                if (ended || pending) return;
                if (mode === 'readwrite' && controls.failSave) { tx.error = new Error('Disk full'); tx.abort(); return; }
                ended = true;
                if (mode === 'readwrite') for (const [name, values] of staged) stores.set(name, values);
                tx.oncomplete?.();
            });
        }
        function request(work) {
            const value = {}; pending++;
            queueMicrotask(() => {
                if (ended) return;
                try { value.result = structuredClone(work()); value.onsuccess?.(); }
                catch (error) { tx.error = error; tx.abort(); }
                pending--; finish();
            });
            return value;
        }
        tx.objectStore = name => ({
            get: key => request(() => staged.get(name).get(key)),
            getAll: () => request(() => [...staged.get(name).values()]),
            getAllKeys: () => request(() => [...staged.get(name).keys()]),
            put: (value, key) => request(() => { staged.get(name).set(key, structuredClone(value)); return key; }),
            delete: key => request(() => staged.get(name).delete(key))
        });
        finish(); return tx;
    } };
};
