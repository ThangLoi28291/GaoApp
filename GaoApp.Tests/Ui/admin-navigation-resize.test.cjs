'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/gaoapp.admin-navigation.js'), 'utf8');

// Minimal DOM/event doubles run the entire production controller, including its
// real search recursion and media listeners. No browser, server or menu writes.
function fixture({ width = 390, drawer = false, expanded = false } = {}) {
    const style = () => {
        const values = new Map();
        return {
            getPropertyValue: name => values.get(name)?.value || '',
            getPropertyPriority: name => values.get(name)?.priority || '',
            setProperty: (name, value, priority = '') => values.set(name, { value, priority }),
            removeProperty: name => values.delete(name)
        };
    };
    let activeElement = null, focusCalls = 0;
    class Element {
        constructor(selectors = [], children = [], data = {}) {
            this.selectors = new Set(selectors); this.children = children; this.dataset = data;
            this.attributes = new Map(); this.listeners = new Map(); this.hidden = false;
            this.value = ''; this.inert = false; this.style = style(); this.isConnected = true;
            children.forEach(child => child.parentElement = this);
            const classes = new Set(selectors.filter(s => s.startsWith('.')).map(s => s.slice(1)));
            this.classList = {
                contains: name => classes.has(name),
                add: name => classes.add(name), remove: name => classes.delete(name),
                toggle(name, force) {
                    const enabled = force === undefined ? !classes.has(name) : force;
                    if (enabled) classes.add(name); else classes.delete(name);
                    return enabled;
                }
            };
        }
        matches(selector) { return selector.split(',').some(item => this.selectors.has(item)); }
        hasAttribute(name) { return this.selectors.has('[' + name + ']') || this.attributes.has(name); }
        setAttribute(name, value) { this.attributes.set(name, value); }
        getAttribute(name) { return this.attributes.get(name) ?? null; }
        removeAttribute(name) { this.attributes.delete(name); }
        querySelector(selector) {
            for (const child of this.children) {
                if (child.matches(selector)) return child;
                const nested = child.querySelector(selector); if (nested) return nested;
            }
            return null;
        }
        closest(selector) { return this.matches(selector) ? this : this.parentElement?.closest(selector) || null; }
        contains(element) { return element === this || this.children.some(child => child.contains(element)); }
        addEventListener(name, callback) {
            if (!this.listeners.has(name)) this.listeners.set(name, []);
            this.listeners.get(name).push(callback);
        }
        emit(name) { for (const callback of this.listeners.get(name) || []) callback({ target: this }); }
        focus() { activeElement = this; focusCalls++; }
    }
    const destination = title => new Element(['[data-nav-node]'], [new Element(['[data-nav-destination]'])], { navTitle: title });
    const purchase = destination('Đặt hàng nhà cung cấp'), receiving = destination('Phiếu nhận hàng');
    const branchLink = new Element(['[data-nav-branch]']);
    const branch = new Element(['[data-nav-node]'], [branchLink, new Element(['.menu-sub'], [purchase, receiving])], { navTitle: 'Nhập hàng' });
    const sale = destination('Bán hàng');
    const list = new Element(['.menu-inner'], [branch, sale]);
    const input = new Element(['#admin-nav-search']);
    const searchRegion = new Element(['[data-nav-search-region]'], [input]);
    const status = new Element(['[data-nav-status]']), noResults = new Element(['[data-nav-no-results]']);
    const clear = new Element(['[data-nav-clear]']), close = new Element(['[data-nav-close]']);
    const root = new Element(['[data-admin-navigation]'], [close, searchRegion, status, list, clear, noResults]);
    if (drawer) new Element(['.offcanvas'], [root]);
    const html = new Element(), body = new Element(), page = new Element(['.layout-page']);
    if (expanded) html.classList.add('layout-menu-expanded');
    const media = new Map([
        ['(max-width: 1199.98px)', { matches: width < 1200, listeners: [] }],
        ['(max-width: 767.98px)', { matches: width < 768, listeners: [] }]
    ]);
    for (const item of media.values()) item.addEventListener = (_, callback) => item.listeners.push(callback);
    const scrolls = [], mutations = [];
    const document = {
        body, documentElement: html,
        querySelector: selector => selector === '[data-admin-navigation]' ? root : selector === '.layout-page' ? page : null,
        addEventListener() {}, get activeElement() { return activeElement; }
    };
    vm.runInNewContext(source, {
        document, Element, matchMedia: query => media.get(query),
        window: { scrollX: 0, scrollY: 215, scrollTo: (...args) => scrolls.push(args) },
        MutationObserver: class { constructor(callback) { mutations.push(callback); } observe() {} }
    }, { filename: 'gaoapp.admin-navigation.js' });
    return {
        root, input, list, status, noResults, clear, branch, branchLink, purchase, receiving, sale, html, body, page, scrolls,
        focusCalls: () => focusCalls,
        search(query) { input.value = query; input.emit('input'); },
        resize(nextWidth) {
            const changed = [];
            for (const [query, item] of media) {
                const next = nextWidth < (query.includes('1199') ? 1200 : 768);
                if (item.matches !== next) { item.matches = next; changed.push(item); }
            }
            changed.forEach(item => item.listeners.forEach(callback => callback()));
        }
    };
}

test('phone search is cleared at desktop width and restores all configured destinations and branch ARIA', () => {
    const f = fixture({ expanded: true });
    f.search('dat hang');
    assert.equal(f.purchase.hidden, false);
    assert.equal(f.receiving.hidden, true); assert.equal(f.sale.hidden, true);
    assert.equal(f.branch.classList.contains('gao-nav-search-open'), true);
    assert.equal(f.branchLink.getAttribute('aria-expanded'), 'true');
    const focusBefore = f.focusCalls();
    f.resize(1440);
    assert.equal(f.input.value, '');
    for (const node of [f.branch, f.purchase, f.receiving, f.sale]) assert.equal(node.hidden, false);
    assert.equal(f.list.hidden, false); assert.equal(f.clear.hidden, true); assert.equal(f.noResults.hidden, true);
    assert.equal(f.status.textContent, '3 chức năng trong menu của bạn');
    assert.equal(f.branch.classList.contains('gao-nav-search-open'), false);
    assert.equal(f.branch.classList.contains('open'), false);
    assert.equal(f.branchLink.getAttribute('aria-expanded'), 'false');
    assert.equal(f.focusCalls(), focusBefore, 'Desktop transition must not focus its hidden search field');
    assert.equal(f.root.inert, false); assert.equal(f.root.getAttribute('aria-hidden'), null);
    assert.equal(f.root.getAttribute('aria-modal'), null); assert.equal(f.page.inert, false);
    assert.equal(f.body.style.getPropertyValue('position'), '');
    assert.deepEqual(f.scrolls, [[0, 215]]);
});

test('phone to tablet keeps the visible search, while desktop also clears a no-results filter', () => {
    const f = fixture();
    f.search('Không có chức năng này');
    assert.equal(f.noResults.hidden, false); assert.equal(f.list.hidden, true);
    f.resize(1024);
    assert.equal(f.input.value, 'Không có chức năng này');
    assert.equal(f.noResults.hidden, false); assert.equal(f.list.hidden, true);
    f.resize(1280);
    assert.equal(f.input.value, ''); assert.equal(f.noResults.hidden, true); assert.equal(f.list.hidden, false);
    assert.equal(f.status.textContent, '3 chức năng trong menu của bạn');
});

test('Bootstrap POS drawer retains its own search and does not acquire page focus or scroll ownership on resize', () => {
    const f = fixture({ drawer: true, expanded: true });
    f.search('dat hang');
    f.resize(1440);
    assert.equal(f.input.value, 'dat hang');
    assert.equal(f.purchase.hidden, false); assert.equal(f.receiving.hidden, true); assert.equal(f.sale.hidden, true);
    assert.equal(f.branchLink.getAttribute('aria-expanded'), 'true');
    assert.equal(f.status.textContent, '1 chức năng phù hợp');
    assert.equal(f.focusCalls(), 0); assert.equal(f.root.getAttribute('aria-modal'), null);
    assert.equal(f.body.style.getPropertyValue('position'), ''); assert.equal(f.page.inert, false);
    assert.deepEqual(f.scrolls, []);
});
