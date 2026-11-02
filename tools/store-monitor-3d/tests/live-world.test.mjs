import test from 'node:test';
import assert from 'node:assert/strict';
import { LiveStoreWorld } from '../src/live-world.mjs';
import { planRoute, segmentClear, STATIONS, shelfVisitPoint, SHOP_SHELVES } from '../src/store-layout.ts';
import { DELIVERY_PARKING, ROAD_POINTS, ROAD_LENGTH, ROAD_SPEED, BOARDING_SECONDS } from '../src/delivery-road.mjs';
const at=Date.parse('2026-10-09T10:00:00Z');
const iso=delta=>new Date(at+delta).toISOString();
const employee=(id=7)=>({userId:id,person:'Nhân viên '+id});
const person=(module,delta=0,extra={})=>({...employee(),module,editedAtUtc:iso(delta),state:'editing',terminal:'Q1',...extra});
const snapshot=(delta=0,extra={})=>({serverTimeUtc:iso(delta),startedAtUtc:iso(-60000),employees:[employee()],people:[],events:[],checkouts:[],...extra});
const event=(id,action,module,delta=0,extra={})=>({...employee(),id,action,module,occurredAtUtc:iso(delta),workKey:'receipt:42',...extra});
const advance=(world,seconds)=>{for(let i=0;i<seconds*10;i++)world.advance(.1);};
test('one employee per identity; navigating a different screen does not switch work',()=>{
 const w=new LiveStoreWorld();w.apply(snapshot(0,{people:[person('pos')]}));
 w.apply(snapshot(1000,{employees:[employee(),employee()],people:[person('pos'),{...person('receipt'),editedAtUtc:null}]}));
 assert.equal(w.employees.size,1);assert.equal(w.employees.get('employee:7').module,'pos');
 w.apply(snapshot(2000,{people:[person('pos'),person('receipt',2000)]}));
 assert.equal(w.employees.get('employee:7').module,'receipt');
});
test('switches start from the current position and retain only one active job',()=>{
 const w=new LiveStoreWorld();w.apply(snapshot(0,{people:[person('pos')]}));advance(w,3);
 const a=w.employees.get('employee:7'),before=[...a.position];
 w.apply(snapshot(1000,{people:[person('receipt',1000)]}));
 assert.deepEqual(a.position,before);assert.equal(a.module,'receipt');
 advance(w,.2);assert.notDeepEqual(a.position,before);
});
test('print command runs printer, then carries labels to a retail shelf exactly once',()=>{
 const w=new LiveStoreWorld();w.apply(snapshot());const e=event(1,'LabelPrinting.Print','label',1000,{workKey:'label:12'});
 w.apply(snapshot(1000,{events:[e]}));const a=w.employees.get('employee:7');
 assert.equal(a.sequence,'labels');assert.deepEqual(a.steps[0].point,STATIONS.print.point);
 assert.equal(a.steps[1].action,'print');assert.equal(a.steps[2].action,'paperWalk');assert.equal(a.steps[3].action,'label');
 advance(w,1);const steps=a.steps,position=[...a.position];w.apply(snapshot(2000,{events:[e]}));
 assert.equal(a.steps,steps);assert.deepEqual(a.position,position);
 advance(w,40);assert.equal(a.sequence,null);
});
test('submitted receipt moves to retail shelves; a new job interrupts it',()=>{
 const w=new LiveStoreWorld();w.apply(snapshot());
 w.apply(snapshot(1000,{events:[event(1,'StockDocuments.SubmitApproval','review',1000)]}));
 const a=w.employees.get('employee:7');assert.equal(a.sequence,'putaway');assert.equal(a.module,'receipt');
 assert.equal(a.steps[2].action,'carry');assert.ok(SHOP_SHELVES.some(s=>JSON.stringify(shelfVisitPoint(s.id))===JSON.stringify(a.steps[2].point)));
 advance(w,2);const position=[...a.position];
 w.apply(snapshot(2000,{people:[person('warehouse',2000)]}));
 assert.deepEqual(a.position,position);assert.equal(a.sequence,null);assert.equal(a.module,'warehouse');
});
const cart=(orderId=42,customerName=null)=>({terminal:'Q1',name:'Quầy 1',orderId,state:'Draft',customerName,lines:[{itemName:'Gạo',quantity:1}],grandTotal:100});

test('cashier remains at a live cart between scans; a newer task and ending the cart release the cashier',()=>{
 const w=new LiveStoreWorld();w.apply(snapshot(0,{people:[person('pos')],checkouts:[cart()]}));advance(w,80);
 const a=w.employees.get('employee:7');w.apply(snapshot(80000,{people:[person('pos')],checkouts:[cart()]}));
 assert.equal(a.module,'pos');assert.equal(a.action,'scan');assert.deepEqual(a.position,w.counter('Q1'));
 w.apply(snapshot(81000,{people:[person('pos'),person('receipt',81000)],checkouts:[cart()]}));assert.equal(a.module,'receipt');
 advance(w,40);w.apply(snapshot(121000,{people:[person('pos'),person('receipt',81000)],checkouts:[cart()]}));
 assert.equal(a.module,'idle','an older open POS tab cannot take over after the newer job ends');
 w.apply(snapshot(122000,{people:[person('pos',122000)],checkouts:[cart()]}));assert.equal(a.module,'pos');
 w.apply(snapshot(150000,{people:[person('pos',122000)],checkouts:[]}));assert.equal(a.module,'idle');
});

test('last scan uses the actual event for the current order and line, including repeat scans; old carts and removed lines do not leak',()=>{
 const w=new LiveStoreWorld(),q={...cart(),lines:[{lineId:3,itemName:'Gạo',quantity:4},{lineId:9,itemName:'Muối',quantity:1}]};
 const scan=(id,delta)=>event(id,'POS.ScanToCurrentCart','pos',delta,{terminal:'Q1',workKey:'order:42',posScan:{lineId:3,itemName:'Gạo',addedQuantity:1,quantity:id}});
 w.apply(snapshot(2000,{checkouts:[q],events:[scan(1,1000),scan(2,2000)]}));assert.equal(w.lastScan('Q1').id,2);
 w.apply(snapshot(3000,{checkouts:[{...q,orderId:43}],events:[scan(2,2000)]}));assert.equal(w.lastScan('Q1'),null);
 w.apply(snapshot(4000,{checkouts:[{...q,lines:[q.lines[1]]}],events:[scan(2,2000)]}));assert.equal(w.lastScan('Q1'),null);
 w.clear();assert.equal(w.lastScan('Q1'),null);
});
test('customer appears at the correct checkout; linked name is cleared when holding; same customer resumes',()=>{
 const w=new LiveStoreWorld();w.apply(snapshot(0,{checkouts:[cart(42,'Khách <A>')]}));
 const c=[...w.customers.values()].find(c=>c.orderId===42);
 assert.equal(c.name,'Khách <A>');assert.equal(c.phase,'checkout');
 w.apply(snapshot(1000,{events:[event(1,'POS.HoldCurrentCart','pos',1000,{workKey:'order:42'})]}));
 assert.equal(c.phase,'held');assert.equal(c.name,'');
 w.apply(snapshot(2000,{checkouts:[cart(42,'Khách <A>')]}));
 assert.equal([...w.customers.values()].find(s=>s.orderId===42),c);assert.equal(c.phase,'checkout');
});
test('completed customer gets a bag and exits; cancel exits without a bag; partial payment stays',()=>{
 for(const [action,bag] of [['POS.FinalizeCurrentCart',true],['POS.CancelCurrentCart',false]]) {
  const w=new LiveStoreWorld();w.apply(snapshot(0,{checkouts:[cart()]}));const c=[...w.customers.values()].find(c=>c.orderId===42);
  w.apply(snapshot(1000,{events:[event(1,action,'pos',1000,{workKey:'order:42'})]}));
  assert.equal(c.exiting,true);advance(w,18);assert.equal(c.bag,bag);advance(w,20);assert.equal(w.customers.has(c.id),false);
 }
 const w=new LiveStoreWorld();w.apply(snapshot(0,{checkouts:[cart()]}));
 w.apply(snapshot(1000,{checkouts:[cart()],events:[event(1,'POS.AddPaymentToCurrentCart','pos',1000,{workKey:'order:42'})]}));
 assert.equal([...w.customers.values()].find(c=>c.orderId===42).exiting,false);
});
test('initial state and restart do not replay old commands',()=>{
 const w=new LiveStoreWorld();const e=event(1,'LabelPrinting.Print','label',-100000);
 w.apply(snapshot(0,{events:[e]}));assert.equal(w.employees.get('employee:7').sequence,undefined);
 w.apply(snapshot(1000,{startedAtUtc:iso(500),events:[event(2,'LabelPrinting.Print','label',1000)]}));
 assert.notEqual(w.employees.get('employee:7').sequence,'labels');
});
test('logout exits; reconnect uses the same actor while leaving',()=>{
 const w=new LiveStoreWorld();w.apply(snapshot());const a=w.employees.get('employee:7');advance(w,3);
 w.apply(snapshot(1000,{employees:[]}));assert.equal(a.exiting,true);
 w.apply(snapshot(2000));assert.equal(w.employees.get(a.id),a);assert.equal(a.exiting,false);
 w.apply(snapshot(3000,{employees:[]}));advance(w,30);assert.equal(w.employees.size,0);
});
test('bike waits for the real courier to mount, cruises continuously, returns on saved completion, then dismounts and walks inside',()=>{
 const w=new LiveStoreWorld();w.apply(snapshot());
 const o={orderId:10,tripId:9,tripCode:'CG-9',courierUserId:7,courier:'Nhân viên 7',state:'Delivering',departedAtUtc:iso(1000)};
 w.apply(snapshot(1000,{delivery:{orders:[o]}}));assert.equal(w.trips.size,1);
 const trip=w.trips.get(9),actor=w.employees.get('employee:7');
 for(let i=0;i<600 && actor.action!=='mount';i++) {
  assert.deepEqual(trip.position,DELIVERY_PARKING);assert.equal(trip.speed,0);w.advance(.1);
 }
 assert.equal(actor.action,'mount');advance(w,BOARDING_SECONDS/2);
 assert.deepEqual(trip.position,DELIVERY_PARKING);assert.equal(trip.hasRider,false);
 advance(w,BOARDING_SECONDS);assert.equal(actor.action,'ride');assert.equal(trip.hasRider,true);
 for(let i=0;i<900;i++) {
  w.advance(.1);assert.deepEqual(actor.position,trip.position);assert.equal(actor.rotation,trip.rotation);
  assert.ok(['outbound','cruising'].includes(trip.state));
 }
 assert.ok(ROAD_LENGTH/ROAD_SPEED>29 && ROAD_LENGTH/ROAD_SPEED<32);
 const position=[...trip.position];advance(w,1);assert.notDeepEqual(trip.position,position);
 const saved={...o,state:'AwaitingReconciliation'};
 w.apply(snapshot(100000,{delivery:{orders:[saved]}}));assert.equal(trip.state,'returning');
 for(let i=0;i<450 && trip.state!=='dismounting';i++)w.advance(.1);
 assert.equal(trip.state,'dismounting');assert.deepEqual(trip.position,DELIVERY_PARKING);assert.equal(actor.action,'dismount');
 advance(w,1);assert.equal(trip.state,'dismounting');assert.equal(trip.speed,0);
 advance(w,2);assert.equal(trip.state,'returned');assert.equal(actor.tripId,null);assert.equal(trip.hasRider,false);
 advance(w,12);assert.ok(actor.position[0]<17 && actor.position[0]>-13,'courier walks back inside');
 assert.equal(saved.returnedAtUtc,undefined,'animation cannot invent a saved return');
});

test('old departures are staged at the parked bike; reload does not teleport an unmounted courier to a destination',()=>{
 const w=new LiveStoreWorld();w.apply(snapshot(0,{delivery:{orders:[{orderId:10,tripId:9,courierUserId:7,courier:'Lan',state:'Delivering',departedAtUtc:iso(-600000)}]}}));
 const trip=w.trips.get(9);assert.equal(trip.state,'waiting-rider');assert.deepEqual(trip.position,DELIVERY_PARKING);
 advance(w,2);assert.deepEqual(trip.position,DELIVERY_PARKING);assert.equal(trip.speed,0);
});

test('one completed order does not return a multi-order trip; offline courier illustration mounts before departure',()=>{
 const o={orderId:10,tripId:9,courierUserId:7,courier:'Lan',state:'Delivering',departedAtUtc:iso(-100000)};
 const w=new LiveStoreWorld();w.apply(snapshot(0,{employees:[],delivery:{orders:[o,{...o,orderId:11}]}}));
 const trip=w.trips.get(9);advance(w,1);assert.deepEqual(trip.position,DELIVERY_PARKING);assert.equal(trip.hasRider,false);
 advance(w,8);assert.equal(trip.state,'cruising');assert.equal(trip.hasRider,true);
 w.apply(snapshot(10000,{employees:[],delivery:{orders:[{...o,state:'AwaitingReconciliation'},{...o,orderId:11}]}}));
 assert.equal(trip.state,'cruising');
 w.apply(snapshot(11000,{employees:[],delivery:{orders:[{...o,state:'AwaitingReconciliation'},{...o,orderId:11,state:'ReadyToSettle'}]}}));
 assert.equal(trip.state,'returning');advance(w,45);assert.equal(trip.state,'returned');assert.deepEqual(trip.position,DELIVERY_PARKING);
});

test('effects-off keeps a seated rider on the road and settles a saved return at parking; effects-on resumes without a driverless bike',()=>{
 const w=new LiveStoreWorld(),o={orderId:10,tripId:9,courierUserId:7,courier:'Lan',state:'Delivering',departedAtUtc:iso(-100000)};
 w.apply(snapshot(0,{delivery:{orders:[o]}}));w.settle();
 const trip=w.trips.get(9),actor=w.employees.get('employee:7');assert.equal(trip.state,'cruising');assert.equal(actor.action,'ride');
 assert.deepEqual(actor.position,trip.position);assert.equal(trip.hasRider,true);advance(w,1);assert.deepEqual(actor.position,trip.position);
 w.apply(snapshot(1000,{delivery:{orders:[{...o,returnedAtUtc:iso(1000)}]}}));w.settle();
 assert.equal(trip.state,'returned');assert.deepEqual(trip.position,DELIVERY_PARKING);assert.equal(actor.tripId,null);
});
test('all station paths avoid fixtures and background customers stay near 5 to 7',()=>{
 const w=new LiveStoreWorld();
 for(const s of Object.values(STATIONS)) {
  const path=planRoute([6,-8.5],s.point);
  for(let i=1;i<path.length;i++)assert.equal(segmentClear(path[i-1],path[i]),true);
 }
 advance(w,240);
 const browsing=[...w.customers.values()].filter(c=>!c.exiting).length;assert.ok(browsing>=5&&browsing<=7);
 w.clear();assert.equal(w.customers.size,0);assert.equal(w.trips.size,0);
});
test('a later task interrupts a courier and is not overwritten by the old trip snapshot',()=>{
 const w=new LiveStoreWorld();w.apply(snapshot());
 const o={orderId:10,tripId:9,courierUserId:7,courier:'Nhân viên 7',state:'Delivering',departedAtUtc:iso(1000)};
 w.apply(snapshot(1000,{delivery:{orders:[o]}}));advance(w,20);
 w.apply(snapshot(22000,{people:[person('pos',22000)],delivery:{orders:[o]}}));
 const a=w.employees.get('employee:7');assert.equal(a.module,'pos');assert.equal(a.tripId,null);
 assert.equal(w.trips.get(9).suppressed,true);
 w.apply(snapshot(50000,{people:[person('pos',22000)],delivery:{orders:[o]}}));
 assert.equal(w.trips.get(9).suppressed,true,'an old trip cannot steal the employee back after the newer task expires');
});

test('reloading after a later task has become idle still does not reclaim the employee for an old trip',()=>{
 const w=new LiveStoreWorld(),o={orderId:10,tripId:9,courierUserId:7,courier:'Lan',state:'Delivering',departedAtUtc:iso(1000)};
 w.apply(snapshot(60000,{people:[person('warehouse',22000)],delivery:{orders:[o]}}));
 const a=w.employees.get('employee:7');assert.equal(a.module,'idle');assert.equal(a.tripId,undefined);
 assert.equal(w.trips.get(9).suppressed,true);advance(w,15);assert.equal(w.trips.get(9).speed,0);
});
test('held customers can leave the visible crowd and return using the same identity',()=>{
 const w=new LiveStoreWorld();w.apply(snapshot());
 for(let i=0;i<10;i++){
  const order=100+i;w.apply(snapshot(i*2000+1000,{checkouts:[cart(order)]}));
  w.apply(snapshot(i*2000+2000,{events:[event(i+1,'POS.HoldCurrentCart','pos',i*2000+2000,{workKey:'order:'+order})]}));
 }
 advance(w,60);
 assert.ok([...w.customers.values()].filter(c=>!c.exiting).length<=7);
 assert.ok(w.parkedCustomers.size>0);
 const [order,c]=w.parkedCustomers.entries().next().value;
 w.apply(snapshot(23000,{checkouts:[cart(order)]}));
 assert.equal(w.customers.get(c.id),c);assert.equal(c.phase,'checkout');
});
test('effects-off shows work at its station and removes logged-out employees without waiting for animation',()=>{
 const w=new LiveStoreWorld();w.apply(snapshot(0,{people:[person('receipt')],checkouts:[cart()]}));w.settle();
 assert.deepEqual(w.employees.get('employee:7').position,STATIONS.receive.point);
 const c=[...w.customers.values()].find(c=>c.orderId===42);assert.deepEqual(c.position,[w.counter('Q1')[0],-5.85]);
 w.apply(snapshot(1000,{employees:[]}));w.settle();assert.equal(w.employees.size,0);
});
test('courier changes update the existing trip and expired completed cards remove it',()=>{
 const w=new LiveStoreWorld();w.apply(snapshot());
 const o={orderId:10,tripId:9,tripCode:'CG-9',courierUserId:7,courier:'Lan',state:'Delivering',departedAtUtc:iso(-30000)};
 w.apply(snapshot(0,{delivery:{orders:[o]}}));const trip=w.trips.get(9);
 w.apply(snapshot(1000,{delivery:{orders:[{...o,courier:'Bảo',courierUserId:8}]}}));
 assert.equal(w.trips.get(9),trip);assert.equal(trip.name,'Bảo');
 w.apply(snapshot(2000,{delivery:{orders:[{...o,state:'Settled'}]}}));advance(w,20);
 w.apply(snapshot(62000,{delivery:{orders:[]}}));assert.equal(w.trips.size,0);
});
