const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/stock-document-management.js'), 'utf8');
const initializer = source.slice(source.indexOf('function initStockDocumentWorkbenchTabs()'), source.indexOf('window.initStockDocumentWorkbenchTabs ='));

test('a confirmed receipt opens the XML disclosure from its deep link without an approval workbench', () => {
    const section = { open: false, scrollIntoView() { this.scrolled = true; } };
    const sandbox = { URLSearchParams, window: { location: { search: '?tab=xml' } },
        document: { getElementById: id => id === 'inputInvoicePickerSection'
            ? { closest: selector => { assert.equal(selector, 'details.sd-optional-xml'); return section; } } : null },
        requestAnimationFrame: action => action() };
    vm.runInNewContext(initializer + '\ninitStockDocumentWorkbenchTabs();', sandbox);
    assert.equal(section.open, true); assert.equal(section.scrolled, true);
});

test('a normal confirmed receipt does not open or scroll the optional XML disclosure', () => {
    const sandbox = { URLSearchParams, window: { location: { search: '' } },
        document: { getElementById: () => null }, requestAnimationFrame: () => assert.fail('No unsolicited scroll') };
    vm.runInNewContext(initializer + '\ninitStockDocumentWorkbenchTabs();', sandbox);
});
