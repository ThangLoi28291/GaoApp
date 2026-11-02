const test = require('node:test');
const assert = require('node:assert/strict');
const ui = require('../../GaoApp.Web/wwwroot/Admin/js/delivery.workspace.js');
const key = '12345678-1234-1234-1234-123456789abc';

test('sale prices retain exact decimal values and whole-dong totals without floating point rounding',()=>{
    assert.equal(ui.salePriceText('17500,50'),'17500.5');
    assert.equal(ui.saleLineTotal('8','17500','2000'),138000n);
    assert.equal(ui.saleLineTotal('0.0001','10000.25','0'),1n);
    assert.equal(ui.saleLineTotal('1','99999999999999.99','0'),100000000000000n);
    for(const value of ['-1','0','17.500','1,234','NaN'])assert.throws(()=>ui.salePriceText(value));
    assert.throws(()=>ui.saleLineTotal('1','10','11'));
});

test('price-change retry remains bound to the intended order before or after dispatch',()=>{
    const intent={kind:'sale-price-change',context:{orderId:7},body:{clientRequestId:key}};
    assert.equal(ui.intentPath(intent),'/admin/api/deliveries/7/sale-prices');
    const ack={clientRequestId:key,operation:'sale-price-change',deliveryOrderId:7,appliedRevision:2,appliedVersion:'AAAAAAAAAAE='};
    assert.equal(ui.checkAck(intent,ack),ack);
    assert.throws(()=>ui.checkAck(intent,{...ack,deliveryOrderId:8}));
});

test('bill revision survives both a QR URL and the initial direct lookup link', () => {
    assert.deepEqual(ui.parseLookup('https://shop.test/admin/deliveries?key=old-bill&revision=2','https://shop.test'),{key:'old-bill',revision:'2'});
    assert.deepEqual(ui.parseLookup('old-bill','https://shop.test','2'),{key:'old-bill',revision:'2'});
    assert.equal(ui.parseLookup('old-bill','https://shop.test','0').revision,null);
    assert.throws(()=>ui.parseLookup('https://other.test/admin/deliveries?key=x','https://shop.test'));
});

test('10 carried, 8 delivered yields exactly 2 returned and a partial outcome', () => {
    const result = ui.counterLines([{lineId:7,dispatchedBaseQuantityText:'10',acceptedReturnedBaseQuantityText:'0'}],new Map([[7,'8']]));
    assert.deepEqual(result,[{lineId:7,deliveredBaseQuantityText:'8',actualReturnedBaseQuantityText:'2'}]);
    assert.equal(ui.outcomeOf(result),'Partial');
});
test('already received returns cannot be delivered a second time', () => {
    const source=[{lineId:7,dispatchedBaseQuantityText:'10',acceptedReturnedBaseQuantityText:'2'}];
    assert.throws(()=>ui.counterLines(source,new Map([[7,'9']])));
    assert.equal(ui.counterLines(source,new Map([[7,'8']]))[0].actualReturnedBaseQuantityText,'2');
});
test('full delivery and no delivery retain all dispatched quantity', () => {
    const source=[{lineId:7,dispatchedBaseQuantityText:'10',acceptedReturnedBaseQuantityText:'0'}];
    assert.equal(ui.outcomeOf(ui.counterLines(source,new Map([[7,'10']]))),'Full');
    assert.equal(ui.outcomeOf(ui.counterLines(source,new Map([[7,'0']]))),'NotDelivered');
});
test('selling units and Vietnamese decimal input convert exactly without rounding', () => {
    assert.equal(ui.toBase('2,5','12'),'30');
    assert.equal(ui.toSelling('30','12'),'2.5');
    assert.equal(ui.toSelling('1','3'),null);
    assert.throws(()=>ui.toBase('0.0001','0.333333'));
    assert.equal(ui.quantityText(ui.qty('99999999999999.9999')),'99999999999999.9999');
    for(const bad of ['-1','1e2','1.00001','','NaN',' 1'])assert.throws(()=>ui.qty(bad));
});
test('money remains whole dong and exact above Number safe integer precision', () => {
    assert.equal(ui.moneyValue('9999999999999999'),9999999999999999n);
    for(const bad of ['1.5','-1','1e2','160.000','160,000',''])assert.throws(()=>ui.moneyValue(bad));
});
test('persisted request recovery is restricted to known same-origin delivery commands', () => {
    assert.equal(ui.intentPath({kind:'complete',context:{orderId:7},body:{clientRequestId:key}}),'/admin/api/deliveries/7/picking/complete');
    assert.throws(()=>ui.intentPath({kind:'https://evil.test',context:{orderId:7},body:{clientRequestId:key}}));
    assert.throws(()=>ui.intentPath({kind:'handover',context:{orderId:7,tripId:'../../x'},body:{clientRequestId:key}}));
});
test('acknowledgement must prove every selected order departed before clearing recovery', () => {
    const intent={kind:'dispatch',context:{orderId:7},body:{clientRequestId:key,orders:[{deliveryOrderId:7},{deliveryOrderId:8}]}};
    const ack={clientRequestId:key,operation:'dispatch',appliedRevision:4,tripId:3,orders:[{deliveryOrderId:7,state:'Delivering'},{deliveryOrderId:8,state:'Delivering'}]};
    assert.equal(ui.checkAck(intent,ack),ack);
    assert.throws(()=>ui.checkAck(intent,{...ack,orders:ack.orders.slice(0,1)}));
    assert.throws(()=>ui.checkAck(intent,{...ack,orders:[...ack.orders.slice(0,1),{deliveryOrderId:8,state:'ReadyForHandover'}]}));
    assert.throws(()=>ui.checkAck(intent,{...ack,clientRequestId:'other'}));
});
test('counter acknowledgement must belong to the original order and contain a settlement', () => {
    const intent={kind:'handover',context:{orderId:7,tripId:3},body:{clientRequestId:key}};
    const ack={clientRequestId:key,operation:'handover',appliedOrderRevision:9,tripId:3,deliveryOrderId:7,state:'Settled',settlement:{deliveryOrderId:7}};
    assert.equal(ui.checkAck(intent,ack),ack);
    assert.throws(()=>ui.checkAck(intent,{...ack,deliveryOrderId:8}));
    assert.throws(()=>ui.checkAck(intent,{...ack,state:'Delivering'}));
});
