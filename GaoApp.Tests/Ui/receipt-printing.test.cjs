const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const templates = require('../../GaoApp.Web/wwwroot/Admin/js/printing/receipt.templates.js');
const printing = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/printing/pos.printing.js'), 'utf8');
const order = { orderId: 15, storeName: 'Gạo Việt', subtotal: 120000, discountTotal: 10000, grandTotal: 110000, paidTotal: 110000,
    lines: [{ itemName: 'Gạo thơm đặc biệt', quantity: 2, unitPrice: 60000, lineTotal: 120000 }], payments: [{ method: 1, amount: 110000 }] };
function harness() {
    const storage = new Map(), jobs = [], browserPrints = [];
    const qz = { websocket: { isActive: () => true }, printers: { find: async () => ['EPSON TM-T82', 'Linux_CUPS_HP'] },
        configs: { create: (printer, options) => ({ printer, options }) }, print: async (config, data) => jobs.push({ config, data }) };
    const window = { qz }, localStorage = { getItem: key => storage.get(key) || null, setItem: (key, value) => storage.set(key, value) };
    vm.runInNewContext(printing, { window, qz, localStorage, ReceiptTemplates: templates });
    const printWindow = { document: { querySelector: () => ({ getBoundingClientRect: () => ({ height: 600 }) }),
        getElementById: () => null, createElement: () => ({}), head: { appendChild() {} } }, focus() {}, print: () => browserPrints.push(true) };
    return { api: window.PosPrinting, jobs, qz, printWindow, browserPrints, storage };
}
test('all 16 preset designs use the requested physical width and retain authoritative receipt totals', () => {
    const options = templates.builtIns(); assert.equal(options.length, 16);
    for (const option of options) {
        const result = templates.render(order, option.design);
        assert.ok(result.css.includes(`width:${templates.sizes[option.design.paperSize].width}mm`));
        assert.match(result.body, /110\.000/); assert.match(result.body, /120\.000/);
        assert.match(result.body, /Gạo thơm đặc biệt/);
    }
});
test('full-width product preset is exclusively 80 mm and remains available for offline printing', async () => {
    const options = templates.builtIns().filter(x => x.design.layout === 'itemwide');
    assert.deepEqual(options.map(x => x.key), ['itemwide-80']);
    for (const paperSize of ['45', '80', 'A4']) assert.equal(templates.normalize({layout:'itemwide',paperSize}).paperSize, '80');
    const h = harness(), context = {storeId:1,terminalId:1};
    h.api.savePreferences(context, {mode:'qz',printer:'EPSON TM-T82',templateKey:'itemwide-80',template:options[0].design});
    const selected = h.api.selected(context, []);
    assert.equal(selected.layout, 'itemwide');
    const result = templates.render({...order,lines:[{productVariantName:'Gạo <đặc biệt>',quantity:1.25,sellingUnitName:'Kg',unitPrice:40000,lineTotal:49000,lineDiscount:1000,sku:'G<01>'}]}, {...selected,showSku:true}, {offline:true});
    assert.match(result.body, /Gạo &lt;đặc biệt&gt;/); assert.match(result.body, /G&lt;01&gt;/);
    assert.match(result.body, /1,25/); assert.match(result.body, /Kg/); assert.match(result.body, /40\.000/); assert.match(result.body, /49\.000/); assert.match(result.body, /Giảm 1\.000/);
    assert.match(result.body, /110\.000/); // Keep the server's receipt totals; do not recalculate from the displayed line.
    await h.api.send(result, context, h.printWindow);
    assert.equal(h.jobs[0].config.options.size.width, 80);
    assert.equal(h.jobs[0].config.options.colorType, 'grayscale');
    assert.match(h.jobs[0].data[0].data, /itemwide/);
});
test('template/customer text cannot inject scripts or styles into a receipt document', () => {
    const payload = '</style><script>alert(1)</script><img src=x onerror=alert(1)>';
    const html = templates.render({ ...order, storeName: payload, storeAddress: payload, storePhone: payload, customerName: payload }, { title: payload, footerText: payload,
        accentColor: 'red;}body{display:none}', layout: 'modern" onload="x' }).html;
    assert.doesNotMatch(html, /<script|<img|body\{display:none\}/);
    assert.match(html, /&lt;script&gt;/);
});
test('every layout ignores legacy accent colors and prints only black ink with bold settlement figures', () => {
    for (const option of templates.builtIns()) {
        const rendered = templates.render({...order, balanceDue:5000, changeDue:10000}, {...option.design, accentColor:'#dd3399'});
        assert.equal(rendered.design.accentColor, '#000000');
        assert.deepEqual([...new Set(rendered.css.match(/#[0-9a-f]+/gi))].sort(), ['#000','#fff']);
        assert.match(rendered.css, /font-weight:600/);
        assert.match(rendered.css, /\.settlement td\{font-weight:800/);
        assert.match(rendered.body, /class="settlement"><td[^>]*>(?:<div[^>]*>)?Đã thanh toán/);
        assert.match(rendered.body, /class="settlement"><td[^>]*>(?:<div[^>]*>)?Còn thiếu/);
        assert.match(rendered.body, /class="settlement"><td[^>]*>(?:<div[^>]*>)?Tiền thừa/);
        assert.match(rendered.body, /110\.000/);
    }
});
test('old client color preferences and offline templates are rendered and sent in monochrome without changing the selected printer', async () => {
    const h = harness(), context = {storeId:1, terminalId:1};
    h.storage.set('gao-pos-print-v1:1:1', JSON.stringify({mode:'qz',printer:'EPSON TM-T82',copies:2,color:true,
        templateKey:'custom-9',template:{name:'Mẫu cũ',paperSize:'45',accentColor:'#ff8800',footerText:'Giữ nguyên nội dung'}}));
    assert.equal(h.api.preferences(context).color, false);
    const rendered = templates.render(order, h.api.selected(context), {offline:true});
    assert.equal(rendered.design.accentColor, '#000000');
    assert.match(rendered.body, /Giữ nguyên nội dung/);
    await h.api.send(rendered, context, h.printWindow);
    assert.equal(h.jobs[0].config.options.colorType, 'grayscale');
    assert.equal(h.jobs[0].config.printer, 'EPSON TM-T82');
    assert.equal(h.jobs[0].config.options.copies, 2);
    assert.equal(h.api.savePreferences(context, {color:true}).color, false);
});
test('offline transfer annotation never claims bank evidence and cash receipts do not claim a manual transfer', () => {
    assert.match(templates.render(order, {}, { offline: true }).body, /nhân viên xác nhận thủ công/);
    assert.doesNotMatch(templates.render({ ...order, payments: [{ method: 0, amount: 110000 }] }, {}, { offline: true }).body, /Chuyển khoản/);
    assert.doesNotMatch(templates.render(order, {}, { offline: false }).body, /Chờ đồng bộ/);
});
test('drafts and cancelled/refunded receipts retain their status and payment references', () => {
    for (const [status, label] of [['Draft', 'Đơn đang lập'], ['Voided', 'Đã hủy sau chốt'], [5, 'Đã hoàn trả']]) {
        const result = templates.render({ ...order, status, payments: [{ method: 1, amount: 110000, reference: 'ACB-001' }] });
        assert.ok(result.body.includes(label)); assert.match(result.body, /ACB-001/);
    }
});
test('printer and saved template selections are isolated by store and terminal and survive catalog changes', () => {
    const h = harness(), a = { storeId: 1, terminalId: 1 }, b = { storeId: 1, terminalId: 2 }, c = { storeId: 2, terminalId: 1 };
    h.api.savePreferences(a, { mode: 'qz', printer: 'Linux_CUPS_HP', templateKey: 'custom-5', template: { paperSize: 'A5', footerText: 'Mẫu đã chọn' } });
    assert.equal(h.api.preferences(b).printer, ''); assert.equal(h.api.preferences(c).printer, '');
    assert.equal(h.api.selected(a, []).paperSize, 'A5');
    assert.equal(h.api.selected(a, [{ key: 'custom-5', design: { paperSize: '80' } }]).paperSize, 'A5');
});
test('QZ receives the exact installed printer, paper dimensions and copy count without browser printing', async () => {
    const h = harness(), context = { storeId: 1, terminalId: 1 };
    h.api.savePreferences(context, { mode: 'qz', printer: 'Linux_CUPS_HP', copies: 2, color: false });
    await h.api.send(templates.render(order, { paperSize: 'A6' }), context, h.printWindow);
    assert.equal(h.jobs.length, 1); assert.equal(h.jobs[0].config.printer, 'Linux_CUPS_HP');
    assert.equal(h.jobs[0].config.options.size.width, 105); assert.equal(h.jobs[0].config.options.size.height, 148);
    assert.equal(h.jobs[0].config.options.copies, 2); assert.equal(h.jobs[0].config.options.scaleContent, false);
    assert.equal(h.jobs[0].data[0].flavor, 'plain'); assert.deepEqual(h.browserPrints, []);
});
test('missing printer and rejected spool jobs fail without rerouting or automatic retries', async () => {
    const h = harness(), context = { storeId: 1, terminalId: 1 };
    h.api.savePreferences(context, { mode: 'qz', printer: 'Deleted Printer' });
    await assert.rejects(h.api.send(templates.render(order), context, h.printWindow), /không còn/);
    assert.equal(h.jobs.length, 0);
    h.api.savePreferences(context, { mode: 'qz', printer: 'EPSON TM-T82' });
    let calls = 0; h.qz.print = async () => { calls++; throw new Error('Printer unavailable'); };
    await assert.rejects(h.api.send(templates.render(order), context, h.printWindow), /Printer unavailable/);
    assert.equal(calls, 1); assert.deepEqual(h.browserPrints, []);
});
