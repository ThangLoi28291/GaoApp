const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(require.resolve('../../GaoApp.Web/wwwroot/pos-offline-worker.js'), 'utf8');

function worker(cached = new Map(), network = async () => new Response('from server'), generations = null) {
    const listeners = new Map(), requests = [];
    vm.runInNewContext(source, {
        URL, Response, AbortController, setTimeout, clearTimeout,
        self: { location: { origin: 'https://pos.local' }, addEventListener: (name, callback) => listeners.set(name, callback) },
        caches: { open: async name => ({ match: async request => (generations?.get(name) || cached).get(request.url)?.clone() }) },
        fetch: async request => { requests.push(request.url); return network(request); },
        indexedDB: { open() { throw new Error('Asset loading must not touch pending POS transactions.'); } }
    });
    return {
        requests,
        load(url, method = 'GET') {
            let result;
            listeners.get('fetch')({ request: new Request(url, { method }), respondWith: response => { result = response; } });
            return result;
        }
    };
}

test('a cached script returns a Response without a network request', async () => {
    const url = 'https://pos.local/Admin/js/pos/pos.render.js?v=old';
    const fixture = worker(new Map([[url, new Response('cached script')]]));
    const response = await fixture.load(url);
    assert.ok(response instanceof Response); assert.equal(await response.text(), 'cached script');
    assert.equal(fixture.requests.length, 0);
});

test('a new script version absent from cache loads the exact URL from the server', async () => {
    const old = 'https://pos.local/Admin/js/pos/pos.offline.js?v=old', next = old.replace('old', 'new');
    const fixture = worker(new Map([[old, new Response('old version')]]));
    const response = await fixture.load(next);
    assert.ok(response instanceof Response, 'respondWith must receive a Response even on a cache miss');
    assert.equal(await response.text(), 'from server'); assert.deepEqual(fixture.requests, [next]);
});

test('uncached CSS and fonts also return the network response', async () => {
    const fixture = worker();
    for (const name of ['pos.css?v=1', 'icons.woff2?v=2']) {
        const response = await fixture.load('https://pos.local/Admin/' + name);
        assert.ok(response instanceof Response); assert.equal(response.status, 200);
    }
    assert.equal(fixture.requests.length, 2);
});

test('a server 404 remains a valid HTTP response instead of resolving undefined', async () => {
    const fixture = worker(new Map(), async () => new Response('not found', { status: 404 }));
    const response = await fixture.load('https://pos.local/missing.js');
    assert.ok(response instanceof Response); assert.equal(response.status, 404);
});

test('a cache miss while offline preserves the actual network failure', async () => {
    const error = new TypeError('Network unavailable');
    const fixture = worker(new Map(), async () => { throw error; });
    await assert.rejects(fixture.load('https://pos.local/uncached.js'), e => e === error);
    assert.equal(fixture.requests.length, 1);
});

test('POS APIs, writes, uploads and external assets remain outside the asset handler', () => {
    const fixture = worker();
    for (const [url, method] of [
        ['https://pos.local/admin/pos/screen', 'GET'], ['https://pos.local/admin/pos/cart/current/payments', 'POST'],
        ['https://pos.local/uploads/photo.svg', 'GET'], ['https://another.local/script.js', 'GET']
    ]) assert.equal(fixture.load(url, method), undefined);
    assert.equal(fixture.requests.length, 0);
});


test('invoice intent cache upgrade retains exact legacy assets until the new bundle is prepared without touching journals', async () => {
    const old = 'https://pos.local/Admin/js/pos/pos.offline.js?v=old';
    const fresh = old.replace('old', 'new');
    const generations = new Map([['gao-pos-assets-v1', new Map([[old, new Response('old complete bundle')]])],
        ['gao-pos-assets-v2', new Map([[fresh, new Response('new intent bundle')]])]]);
    const fixture = worker(new Map(), async () => { throw new Error('Offline'); }, generations);
    assert.equal(await (await fixture.load(old)).text(), 'old complete bundle');
    assert.equal(await (await fixture.load(fresh)).text(), 'new intent bundle');
    assert.equal(fixture.requests.length, 0);
    assert.doesNotMatch(source, /deleteDatabase|deleteObjectStore/);
});
