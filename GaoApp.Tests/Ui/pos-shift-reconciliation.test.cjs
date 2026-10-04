const test = require('node:test');
const assert = require('node:assert/strict');
const draft = require('../../GaoApp.Web/wwwroot/Admin/js/pos/pos.shift.close-draft.js');
const recon = require('../../GaoApp.Web/wwwroot/Admin/js/pos/pos.shift.reconciliation.js');
const context = {storeId:1,userId:2,terminalId:3,shiftId:4};
function storage() {const values=new Map();return {getItem:k=>values.get(k)||null,setItem:(k,v)=>values.set(k,v),removeItem:k=>values.delete(k)};}
test('closing cash, denomination counts and note survive navigation only for the exact cashier and shift',()=>{
    const store=storage();
    draft.save(context,{actual:888000,note:'Kiểm đếm cần đối chiếu',denominations:[{value:500000,quantity:1},{value:200000,quantity:1}]},store);
    const restored=draft.read(context,store);
    assert.equal(restored.actual,888000);assert.equal(restored.note,'Kiểm đếm cần đối chiếu');assert.equal(restored.denominations[0].quantity,1);
    for(const field of ['storeId','userId','terminalId','shiftId']) assert.equal(draft.read({...context,[field]:99},store),null);
    draft.clear(context,store);assert.equal(draft.read(context,store),null);
});
test('unavailable storage and malformed counts cannot silently drop a closing count',()=>{
    assert.throws(()=>draft.save(context,{actual:100,note:'',denominations:[]},{setItem:()=>{throw Error('quota')}}),/quota/);
    assert.throws(()=>draft.save(context,{actual:-1,note:'',denominations:[]},storage()),/không hợp lệ/);
    assert.throws(()=>draft.save(context,{actual:100,note:'',denominations:[{value:500000,quantity:0.5}]},storage()),/không hợp lệ/);
    const store=storage();store.setItem(draft.key(context),'{}');assert.throws(()=>draft.read(context,store),/kiểm đếm/);
});
const payment=(method,amount,bankVerified=false)=>({id:1,method,amount,bankVerified,cancelled:false,isDebtCollection:false});
const order=overrides=>({id:1,number:'POS-001',status:'Completed',cashReceived:0,bankReceived:0,otherReceived:0,grandTotal:215000,
    remaining:0,surplus:0,payments:[],lines:[],createdAtUtc:'2026-10-01T10:00:00Z',...overrides});
test('surplus, mixed tender, manual transfer and unfinalized receipts are visible without claiming they are errors',()=>{
    const over=order({bankReceived:250000,surplus:35000,payments:[payment('BankTransfer',250000)]});
    assert.deepEqual(recon.orderFlags(over),['Nhận dư 35.000 đ','Chuyển khoản cần đối chiếu']);
    const paid=order({bankReceived:215000,payments:[payment('BankTransfer',215000,true)]});assert.deepEqual(recon.orderFlags(paid),[]);
    const mixed=order({status:'Draft',cashReceived:100000,bankReceived:115000,payments:[payment('Cash',100000),payment('BankTransfer',115000,true)]});
    assert.ok(recon.orderFlags(mixed).includes('Đã nhận tiền, chưa chốt'));assert.ok(recon.orderFlags(mixed).includes('Nhiều phương thức'));
});
test('day and amount search filter only detail rows; cancelled payments and QR creation are not active cash receipts',()=>{
    const data={orders:[order({cashReceived:215000,payments:[payment('Cash',215000)]}),order({id:2,number:'POS-002',createdAtUtc:'2026-10-04T10:00:00Z',bankReceived:250000,surplus:35000,payments:[payment('BankTransfer',250000)]})]};
    const rows=recon.rowsFor(data,'orders');assert.equal(recon.filterRows(rows,{query:'250.000'}).length,1);
    assert.equal(recon.filterRows(rows,{scope:'bank'}).length,1);assert.equal(recon.filterRows(rows,{day:'2026-10-04'}).length,1);
    assert.equal(data.orders.length,2);assert.equal(data.orders[0].grandTotal,215000);
    const cancelled=recon.rowsFor({orders:[order({payments:[{...payment('Cash',100000),cancelled:true}]})]},'orders');
    assert.equal(cancelled[0].cash,false);
});
test('receipt contents render as text and cannot become executable HTML',()=>{
    const html=recon.renderRow({shiftId:4},'cash',{value:{id:1,type:'CashOut',amount:100,reason:'<img src=x onerror=alert(1)>',note:'<script>bad</script>',actor:'<b>User</b>',canRequest:true},flags:[],attention:false});
    assert.equal(html.includes('<script>bad</script>'),false);assert.equal(html.includes('<img src=x'),false);assert.ok(html.includes('&lt;img'));
});
