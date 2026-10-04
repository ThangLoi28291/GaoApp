const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const renderer=require('../../GaoApp.Web/wwwroot/Admin/js/printing/shift.cash-printing.js');
const source=fs.readFileSync(path.resolve(__dirname,'../../GaoApp.Web/wwwroot/Admin/js/pos/pos.shift.page.js'),'utf8');
const saved={id:12,posShiftId:5,type:'CashIn',amount:100000,reason:'Bổ sung tiền lẻ',note:'Ghi chú',createdAtUtc:'2026-09-29T03:00:00Z'};
const context={storeId:1,terminalId:2,storeName:'Cửa hàng thử',terminalName:'Quầy 2',userName:'Nhân viên thử'};
for(const type of ['CashIn','CashOut'])test(`renders saved ${type} voucher at 80mm`,()=>{
    const rendered=renderer.render({...saved,type},context);
    assert.equal(rendered.size.width,80);assert.equal(rendered.size.height,null);
    assert.ok(rendered.html.includes(type==='CashIn'?'PHIẾU THU TIỀN MẶT':'PHIẾU CHI TIỀN MẶT'));
    for(const text of ['Bổ sung tiền lẻ','Ghi chú','100.000','Người in','Cửa hàng thử','Quầy 2'])assert.ok(rendered.html.includes(text),text);
});
test('voucher escapes persisted text and rejects unsaved/invalid transactions',()=>{
    const html=renderer.render({...saved,reason:'<img src=x onerror=evil()>',note:'<script>evil()</script>'},{...context,storeName:'<svg onload=evil()>'}).html;
    assert.ok(!html.includes('<script>'));assert.ok(!html.includes('<img'));assert.ok(html.includes('&lt;svg'));
    for(const patch of [{id:0},{type:'Unknown'},{amount:0},{amount:'100'},{amount:Infinity}])assert.throws(()=>renderer.render({...saved,...patch},context));
});
function harness(){
    const calls=[],errors=[],feedback={};let pendingPost;
    const env={document:{getElementById:()=>feedback},printContext:context,activeShift:{id:5},
        drawerReason:{value:'  Đổi tiền lẻ  ',focus(){}},drawerError:{textContent:''},drawerModal:{hide(){calls.push('hide-drawer')}},
        btnOpenCashDrawer:{disabled:false,innerHTML:'Mở'},btnShowDrawerModal:{disabled:false},btnAddCashTxn:{disabled:false,innerHTML:'Lưu'},
        cashTxnType:{value:'1'},cashTxnAmount:{value:'100000'},cashTxnReason:{value:saved.reason},cashTxnNote:{value:saved.note},
        cashTxnModal:{hide(){calls.push('hide-voucher')}},showSuccess(){},loadCurrentShift:async()=>{},loadCashTransactions:async()=>{},switchShiftTab(){},
        postJson:async(url,body)=>{calls.push({url,body});return pendingPost?await pendingPost():url.endsWith('cash-drawer')?{auditId:42,shiftId:5}:saved},
        window:{PosError:{handle:error=>errors.push(error.message)},
            PosShiftCashPrinting:{print:async(...args)=>{calls.push({print:args});return {mode:'helper'}}},
            PosPrinting:{drawerReady:async()=>{calls.push('ready')},openDrawer:async()=>{calls.push('pulse')}}}};
    vm.runInNewContext(source.slice(source.indexOf('    function parseCashTxnAmount('),source.indexOf('    function setOpenDirectInput('))+
        source.slice(source.indexOf('    async function withButtonLoading('),source.indexOf('    function setText('))+
        source.slice(source.indexOf('    async function addCashTransaction()'),source.indexOf('    function confirmCloseShiftWithDiff(')),env);
    return {env,calls,errors,feedback,post:fn=>{pendingPost=fn}};
}
test('save then print/drawer uses persisted transaction; reprint never opens drawer',async()=>{
    const h=harness();await h.env.addCashTransaction();
    const creation=h.calls.find(x=>x.url),print=h.calls.find(x=>x.print);
    assert.equal(creation.url,'/admin/pos/shift/cash-transaction');assert.equal(print.print[0],saved);assert.equal(print.print[2],true);
    await h.env.printCashTransaction(saved,false);assert.equal(h.calls.at(-1).print[2],false);
});
test('cash submission preserves grouped amounts and decimal commas; invalid amounts cannot save or print',async()=>{
    for (const [value,amount] of [['30.000.000',30000000],['100.000,50',100000.5]]) {
        const h=harness();h.env.cashTxnAmount.value=value;await h.env.addCashTransaction();
        assert.equal(h.calls.find(x=>x.url).body.amount,amount);
    }
    for (const value of ['', '0', '-100.000', 'abc', '1,2,3', 'Infinity']) {
        const h=harness();h.env.cashTxnAmount.value=value;await h.env.addCashTransaction();
        assert.equal(h.calls.length,0,value);assert.match(h.errors[0],/số tiền lớn hơn 0/);
    }
});
test('failed save cannot print; failed print preserves one saved voucher for reprint',async()=>{
    const fail=harness();fail.post(async()=>{throw Error('not saved')});await fail.env.addCashTransaction();
    assert.equal(fail.calls.filter(x=>x.print).length,0);
    const h=harness();h.env.window.PosShiftCashPrinting.print=async()=>{throw Error('helper unavailable')};
    await h.env.addCashTransaction();assert.equal(h.calls.filter(x=>x.url).length,1);
    assert.match(h.feedback.textContent,/Phiếu #12 đã được lưu/);assert.match(h.feedback.textContent,/in lại không mở két/);
});
test('blank/long drawer reason and no shift cannot call server or helper',async()=>{
    for(const reason of ['', '   ', 'a'.repeat(301)]){const h=harness();h.env.drawerReason.value=reason;await h.env.openCashDrawer();assert.deepEqual(h.calls,[]);}
    const h=harness();h.env.activeShift=null;await h.env.openCashDrawer();assert.deepEqual(h.calls,[]);
});
test('standalone drawer persists trimmed reason before pulse and never creates a cash voucher',async()=>{
    const h=harness();await h.env.openCashDrawer();
    assert.equal(h.calls[0],'ready');assert.equal(h.calls[1].url,'/admin/pos/shift/cash-drawer');
    assert.equal(h.calls[1].body.reason,'Đổi tiền lẻ');assert.equal(h.calls[1].body.shiftId,5);assert.equal(h.calls[2],'pulse');
    assert.equal(h.calls.filter(x=>x.print).length,0);
});
test('missing helper capability, denied audit, wrong ACK and audit write failure cannot pulse',async()=>{
    for(const kind of ['old-helper','permission','audit-write','invalid-ack']){
        const h=harness();
        if(kind==='old-helper')h.env.window.PosPrinting.drawerReady=async()=>{throw Error('update helper')};
        else h.post(async()=>{if(kind==='invalid-ack')return {auditId:0,shiftId:5};throw Error(kind)});
        await h.env.openCashDrawer();assert.ok(!h.calls.includes('pulse'),kind);assert.ok(h.env.drawerError.textContent);
    }
});
test('rapid clicks submit one voucher or one standalone drawer request',async()=>{
    for(const action of ['addCashTransaction','openCashDrawer']){
        const h=harness();let release;
        h.post(()=>new Promise(resolve=>{release=()=>resolve(action==='openCashDrawer'?{auditId:42,shiftId:5}:saved)}));
        const first=h.env[action]();await new Promise(setImmediate);await h.env[action]();release();await first;
        assert.equal(h.calls.filter(x=>x.url).length,1,action);
    }
});
