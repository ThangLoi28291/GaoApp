const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require('../../GaoApp.Tests.Browser/node_modules/playwright');
const root=path.resolve(__dirname,'../..'),output=process.env.GAO_UI24_REVIEW_ROOT?path.join(process.env.GAO_UI24_REVIEW_ROOT,'node-store-monitor-3d'):path.join(root,'Logs/store-monitor-3d-20261009');
function fixture() {
 const now=new Date().toISOString();
 const employees=[{userId:7,person:'Lan <script>bad()</script>'},{userId:8,person:'Nam'},{userId:9,person:'Hạnh'}];
 return {serverTimeUtc:now,startedAtUtc:now,employees,people:[
  {...employees[0],module:'pos',state:'editing',terminal:'Q1',editedAtUtc:now},
  {...employees[1],module:'receipt',state:'viewing',editedAtUtc:null},
  {...employees[2],module:'warehouse',state:'editing',editedAtUtc:now}],
  events:[],checkouts:[{terminal:'Q1',name:'Quầy 1',orderId:42,code:'BH-42',state:'Draft',customerName:'Khách <img src=x onerror=bad()>',
   grandTotal:132000,paidTotal:50000,balanceDue:82000,changeDue:0,lines:[{itemName:'Gạo ST25 <b>thật</b>',unitName:'Túi',quantity:2,unitPrice:66000,lineTotal:132000}]}],
  delivery:{orders:[{orderId:19,code:'GH-19',recipient:'Khách B',state:'Delivering',tripId:5,tripCode:'CG-5',courier:'Hạnh',courierUserId:9,
   departedAtUtc:new Date(Date.now()-30000).toISOString(),updatedAtUtc:now,totalLines:1,checkedLines:1,shortageLines:0,lastAction:'Đã xuất phát',
   lines:[{itemName:'Gạo ST25',planned:2,prepared:2,unitName:'Túi'}]}],activeCount:1,deliveringCount:1}
 };
}
async function setup(initialView='3d',webgl=true,payload=fixture()) {
 const browser=await chromium.launch({channel:'chrome',headless:true,args:webgl?['--use-angle=swiftshader','--enable-unsafe-swiftshader']:['--disable-webgl']});
 const page=await browser.newPage({viewport:{width:1366,height:768}}),errors=[],requests=[];
 page.on('pageerror',e=>errors.push(e.message));
 const view=fs.readFileSync(path.join(root,'GaoApp.Web/Areas/Admin/Views/StoreMonitor/Index.cshtml'),'utf8');
 const html=view.slice(view.indexOf('<!DOCTYPE html>'))
  .replace('data-scene-url="@sceneUrl"','data-scene-url="/Admin/js/store-monitor.3d.bundle.js"')
  .replace(/data-stream-url=.*? role=/,'data-stream-url="/admin/api/store-activity/stream" role=')
  .replace(/<script src="~\/Admin\/js\/store-activity.online.js".*?<\/script>/,'')
  .replace('@ViewData["StoreName"]','Gao Mart').replaceAll('~/','/');
 await page.addInitScript(({payload,initialView})=>{
  localStorage.setItem('gao.monitor.view',initialView);window.payload=payload;window.streams=[];
  window.EventSource=class{constructor(){this.handlers={};window.streams.push(this);setTimeout(()=>this.emit('snapshot',window.payload),0);}
   addEventListener(n,f){this.handlers[n]=f;}emit(n,p){if(!this.closed)this.handlers[n]?.({data:JSON.stringify(p)});}close(){this.closed=true;}};
  window.publish=p=>{window.payload=p;window.streams.at(-1).emit('snapshot',p);};
 },{payload,initialView});
 await page.route('**/*',route=>{
  const request=route.request(),url=new URL(request.url());requests.push(url.pathname);
  assert.equal(request.method(),'GET','Monitor must not write business data');
  if(url.pathname.startsWith('/Admin/'))return route.fulfill({contentType:url.pathname.endsWith('.js')?'text/javascript':'text/css',
   body:fs.readFileSync(path.join(root,'GaoApp.Web/wwwroot',url.pathname),'utf8')});
  if(url.pathname.endsWith('/snapshot'))return route.fulfill({json:fixture()});
  return route.fulfill({contentType:'text/html',body:html});
 });
 await page.goto('http://monitor3d.test/admin/store-monitor');
 page.setDefaultTimeout(5000);
 return {browser,page,errors,requests};
}
test('real WebGL scene renders, scales uniformly, displays real cart facts safely, stops on disconnect and clears on revoke',async()=>{
 fs.mkdirSync(output,{recursive:true});const {browser,page,errors}=await setup();
 try {
  await page.waitForFunction(()=>window.GaoStoreLiveMonitor?.scene,{timeout:45000});
  await page.waitForTimeout(800);
  await page.screenshot({path:path.join(output,'initial.png')});
  assert.deepEqual(errors,[]);
  const scene=await page.evaluate(()=>({employees:window.GaoStoreLiveMonitor.scene.world.employees.size,customers:window.GaoStoreLiveMonitor.scene.world.customers.size,trips:window.GaoStoreLiveMonitor.scene.world.trips.size}));
  assert.equal(scene.employees,3);assert.equal(scene.trips,1);assert.ok(scene.customers>=5);
  await page.locator('#wm3-detail [data-select="terminal:Q1"]').click();
  const text=await page.locator('#wm3-detail').innerText();
  assert.match(text,/Gạo ST25 <b>thật<\/b>/);assert.match(text,/132.000/);assert.match(text,/82.000/);
  assert.equal(await page.locator('#wm3-detail img,#wm3-detail script').count(),0);
  for(const [width,height] of [[1920,1080],[1366,768],[1280,650],[1024,768],[800,600],[390,844]]) {
   await page.setViewportSize({width,height});await page.waitForTimeout(120);
   const rect=await page.locator('#wm-3d').boundingBox();
   assert.ok(rect.x>=-1&&rect.y>=0&&rect.x+rect.width<=width+1,'width fit '+width+': '+JSON.stringify(rect));
   if(width>=768)assert.ok(rect.y+rect.height<=height+1,'desktop fit '+width+': '+JSON.stringify(rect));
   else {
    const stage=await page.locator('.wm3-stage').boundingBox();
    assert.ok(stage.height>=340&&stage.height<=height,'phone stage retains the UI-22 camera viewport');
    assert.equal(await page.locator('body').evaluate(e=>getComputedStyle(e).overflowY),'auto');
    assert.ok(await page.locator('.wm3-panel').boundingBox().then(r=>r.y>=stage.y+stage.height-1),'phone details stack below the 3D stage');
   }
   const geometry=await page.evaluate(()=>({width:document.documentElement.scrollWidth,height:document.documentElement.scrollHeight}));
   assert.ok(geometry.width<=width+1&&(width<768||geometry.height<=height+1),JSON.stringify(geometry));
   await page.screenshot({path:path.join(output,'monitor-'+width+'x'+height+'.png')});
  }
  await page.setViewportSize({width:1366,height:768});
  await page.locator('[data-view-area="delivery"]').click();await page.waitForTimeout(200);
  await page.screenshot({path:path.join(output,'delivery-road.png')});
  await page.locator('[data-view-area="overview"]').click();
  await page.evaluate(()=>{
   const p=structuredClone(window.payload);p.serverTimeUtc=new Date().toISOString();
   p.employees[0].person='Lan';p.people[0].person='Lan';
   p.checkouts[0].customerName='Nguyễn Minh';p.checkouts[0].lines[0].itemName='Gạo ST25';
   p.delivery.orders[0].courier='Bảo';p.delivery.orders[0].courierUserId=10;
   window.publish(p);
  });
  await page.waitForTimeout(150);await page.screenshot({path:path.join(output,'monitor-preview.png')});
  await page.evaluate(()=>window.dispatchEvent(new Event('offline')));await page.waitForTimeout(80);
  const time=await page.evaluate(()=>window.GaoStoreLiveMonitor.scene.world.time);
  await page.waitForTimeout(300);assert.equal(await page.evaluate(()=>window.GaoStoreLiveMonitor.scene.world.time),time);
  await page.evaluate(()=>window.streams.at(-1).closed=false);await page.evaluate(()=>window.streams.at(-1).emit('revoked',{}));
  assert.equal(await page.locator('#wm3-detail').innerText().then(t=>t.includes('Gạo ST25')),false);
  assert.equal(await page.evaluate(()=>window.GaoStoreLiveMonitor.scene.world.employees.size),0);
  assert.deepEqual(errors,[]);
 } finally {await browser.close();}
});

test('delivery renders one mounted employee, keeps riding until saved completion, returns, dismounts and enters the store',async()=>{
 const payload=fixture(),now=payload.serverTimeUtc;
 payload.employees=[{userId:7,person:'Bảo'}];payload.people=[];payload.events=[];payload.checkouts=[];
 payload.delivery.orders=[{orderId:19,code:'GH-19',recipient:'Khách nhận hàng',state:'Delivering',tripId:5,tripCode:'CG-5',
  courier:'Bảo',courierUserId:7,departedAtUtc:now,updatedAtUtc:now,lines:[]}];
 const capture=path.join(root,'Logs/store-monitor-3d-road-20261009');fs.mkdirSync(capture,{recursive:true});
 const {browser,page,errors}=await setup('3d',true,payload);
 const render=async()=>{
  await page.evaluate(()=>window.GaoStoreLiveMonitor.scene.status({connected:false,motion:true,theme:'light'}));
  await page.waitForTimeout(220);
 };
 try {
  await page.waitForFunction(()=>window.GaoStoreLiveMonitor?.scene,{timeout:45000});
  await page.evaluate(()=>{
   const scene=window.GaoStoreLiveMonitor.scene;scene.status({connected:false,motion:true,theme:'light'});
   for(let i=0;i<2000&&scene.world.employees.get('employee:7').action!=='mount';i++)scene.world.advance(.1);
   for(let i=0;i<5;i++)scene.world.advance(.1);
   scene.setView('delivery');
  });
  await render();
  let facts=await page.evaluate(()=>{
   const scene=window.GaoStoreLiveMonitor.scene,trip=scene.world.trips.get(5);
   return {position:trip.position,speed:trip.speed,render:scene.inspectDelivery()[0]};
  });
  assert.deepEqual(facts.position,[19,2]);assert.equal(facts.speed,0);
  assert.equal(facts.render.state,'boarding');assert.equal(facts.render.employeeAttached,true);assert.equal(facts.render.illustratedRider,false);
  assert.ok(facts.render.riderSeat[1]>0&&facts.render.riderSeat[1]<.27);
  await page.screenshot({path:path.join(capture,'boarding.png')});
  await page.evaluate(()=>{
   const scene=window.GaoStoreLiveMonitor.scene;
   for(let i=0;i<2000&&scene.world.trips.get(5).state!=='cruising';i++)scene.world.advance(.1);
   for(let i=0;i<90;i++)scene.world.advance(.1);
  });
  await render();
  facts=await page.evaluate(()=>{
   const scene=window.GaoStoreLiveMonitor.scene,trip=scene.world.trips.get(5),actor=scene.world.employees.get('employee:7');
   return {position:trip.position,employeePosition:actor.position,rotation:trip.rotation,employeeRotation:actor.rotation,render:scene.inspectDelivery()[0]};
  });
  assert.equal(facts.render.state,'cruising');assert.equal(facts.render.employeeAttached,true);assert.equal(facts.render.illustratedRider,false);
  assert.equal(facts.render.knee,1.55,'the employee has the seated pose, including when the simulation is paused');
  assert.ok(facts.render.riderSeat.every((v,i)=>Math.abs(v-[0,.27,-.2][i])<.000001));
  assert.deepEqual(facts.employeePosition,facts.position);assert.equal(facts.employeeRotation,facts.rotation);
  assert.equal(await page.locator('[data-actor-id="employee:7"]:visible').count(),0);
  assert.match(await page.locator('[data-trip-id="5"]').innerText(),/Bảo.*Đang giao trên đường/s);
  assert.equal(await page.locator('[data-view-area="delivery"]').innerText(),'Giao hàng');
  await page.screenshot({path:path.join(capture,'on-road.png')});
  await page.evaluate(()=>window.GaoStoreLiveMonitor.scene.focus('trip:5'));await render();
  await page.screenshot({path:path.join(capture,'rider.png')});
  await page.evaluate(()=>{
   const scene=window.GaoStoreLiveMonitor.scene;
   for(let i=0;i<650;i++)scene.world.advance(.1);
   const p=structuredClone(window.payload);p.serverTimeUtc=new Date().toISOString();
   p.delivery.orders[0].state='AwaitingReconciliation';p.delivery.orders[0].updatedAtUtc=p.serverTimeUtc;
   window.publish(p);scene.status({connected:false,motion:true,theme:'light'});scene.setView('delivery');
  });
  await render();assert.equal(await page.evaluate(()=>window.GaoStoreLiveMonitor.scene.world.trips.get(5).state),'returning');
  await page.screenshot({path:path.join(capture,'returning.png')});
  await page.evaluate(()=>{
   const scene=window.GaoStoreLiveMonitor.scene;
   for(let i=0;i<2000&&scene.world.trips.get(5).state!=='dismounting';i++)scene.world.advance(.1);
  });
  await render();
  facts=await page.evaluate(()=>{const scene=window.GaoStoreLiveMonitor.scene,t=scene.world.trips.get(5);
   return {position:t.position,speed:t.speed,render:scene.inspectDelivery()[0]};});
  assert.deepEqual(facts.position,[19,2]);assert.equal(facts.speed,0);assert.equal(facts.render.employeeAttached,true);
  await page.evaluate(()=>{
   const scene=window.GaoStoreLiveMonitor.scene;
   for(let i=0;i<100&&scene.world.trips.get(5).state!=='returned';i++)scene.world.advance(.1);
   scene.focus('employee:7');
  });
  await render();
  facts=await page.evaluate(()=>{
   const scene=window.GaoStoreLiveMonitor.scene,a=scene.world.employees.get('employee:7');
   return {tripId:a.tripId,action:a.action,caption:a.caption,render:scene.inspectDelivery()[0]};
  });
  assert.equal(facts.render.state,'returned');assert.equal(facts.render.employeeAttached,false);assert.equal(facts.render.illustratedRider,false);
  assert.equal(facts.tripId,null);assert.equal(facts.action,'walk');assert.equal(facts.caption,'Đi vào cửa hàng');
  await page.screenshot({path:path.join(capture,'dismounted.png')});
  await page.evaluate(()=>{
   const scene=window.GaoStoreLiveMonitor.scene;for(let i=0;i<40;i++)scene.world.advance(.1);scene.setView('overview');
  });
  await render();
  assert.ok(await page.evaluate(()=>window.GaoStoreLiveMonitor.scene.world.employees.get('employee:7').position[0]<16.5));
  await page.screenshot({path:path.join(capture,'back-in-store.png')});
  assert.deepEqual(errors,[]);
 } finally {await browser.close();}
});
test('offline courier illustration mounts before departure, hands over to the online employee, and vehicle removal preserves that employee model',async()=>{
 const payload=fixture();payload.employees=[];payload.people=[];payload.events=[];payload.checkouts=[];
 payload.delivery.orders[0].courier='Bảo';payload.delivery.orders[0].courierUserId=7;
 const {browser,page,errors}=await setup('3d',true,payload);
 const render=async()=>{
  await page.evaluate(()=>window.GaoStoreLiveMonitor.scene.status({connected:false,motion:true,theme:'light'}));
  await page.waitForTimeout(220);
 };
 try {
  await page.waitForFunction(()=>window.GaoStoreLiveMonitor?.scene,{timeout:45000});
  await page.evaluate(()=>{
   const scene=window.GaoStoreLiveMonitor.scene;scene.status({connected:false,motion:true,theme:'light'});
   for(let i=0;i<5;i++)scene.world.advance(.1);
  });
  await render();
  let facts=await page.evaluate(()=>({render:window.GaoStoreLiveMonitor.scene.inspectDelivery()[0],
   position:window.GaoStoreLiveMonitor.scene.world.trips.get(5).position,employees:window.GaoStoreLiveMonitor.scene.world.employees.size}));
  assert.equal(facts.render.state,'boarding');assert.equal(facts.render.illustratedRider,true);assert.deepEqual(facts.position,[19,2]);assert.equal(facts.employees,0);
  await page.evaluate(()=>{for(let i=0;i<100;i++)window.GaoStoreLiveMonitor.scene.world.advance(.1);});await render();
  facts=await page.evaluate(()=>window.GaoStoreLiveMonitor.scene.inspectDelivery()[0]);
  assert.equal(facts.state,'cruising');assert.equal(facts.illustratedRider,true);assert.equal(facts.employeeAttached,false);
  assert.ok(Math.abs(facts.knee-1.55)<1e-9,'illustrated rider is seated on the moving vehicle');
  await page.evaluate(()=>{
   const p=structuredClone(window.payload);p.serverTimeUtc=new Date().toISOString();p.employees=[{userId:7,person:'Bảo'}];
   window.publish(p);window.GaoStoreLiveMonitor.scene.status({connected:false,motion:true,theme:'light'});
  });await render();
  facts=await page.evaluate(()=>window.GaoStoreLiveMonitor.scene.inspectDelivery()[0]);
  assert.equal(facts.employeeAttached,true);assert.equal(facts.employeeDisposed,false);assert.equal(facts.illustratedRider,false);
  // Delete the vehicle while its employee model is parented to it, then use the same employee in the next trip.
  await page.evaluate(()=>{
   const scene=window.GaoStoreLiveMonitor.scene,p=structuredClone(window.payload),order={...p.delivery.orders[0],tripId:6,tripCode:'CG-6'};
   p.delivery.orders=[];window.publish(p);p.delivery.orders=[order];window.publish(p);
   scene.status({connected:false,motion:false,theme:'light'});
  });
  await page.waitForTimeout(220);
  facts=await page.evaluate(()=>window.GaoStoreLiveMonitor.scene.inspectDelivery()[0]);
  assert.equal(facts.tripId,6);assert.equal(facts.employeeAttached,true);assert.equal(facts.employeeDisposed,false);assert.equal(facts.illustratedRider,false);
  assert.equal(facts.knee,1.55,'effects-off updates the seated pose without advancing world time');
  assert.equal(await page.evaluate(()=>window.GaoStoreLiveMonitor.scene.world.employees.size),1);assert.deepEqual(errors,[]);
 } finally {await browser.close();}
});

test('2D choice loads no 3D engine until requested and retains 2D work cards',async()=>{
 const {browser,page,errors,requests}=await setup('2d');
 try {
  await page.waitForSelector('#wm-pos .wm-station');assert.ok(!requests.some(p=>p.endsWith('.3d.bundle.js')));
  assert.equal(await page.locator('#gao-store-wall').getAttribute('data-view'),'2d');
  await page.locator('#wm-view').click();await page.waitForFunction(()=>window.GaoStoreLiveMonitor.scene,{timeout:45000});
  assert.ok(requests.some(p=>p.endsWith('.3d.bundle.js')));await page.locator('#wm-view').click();
  const time=await page.evaluate(()=>window.GaoStoreLiveMonitor.scene.world.time);
  await page.waitForTimeout(200);assert.equal(await page.evaluate(()=>window.GaoStoreLiveMonitor.scene.world.time),time);
  assert.deepEqual(errors,[]);
 } finally {await browser.close();}
});
test('WebGL unavailable falls back to the existing 2D screen',async()=>{
 const {browser,page,errors}=await setup('3d',false);
 try {
  await page.waitForFunction(()=>document.querySelector('#gao-store-wall').dataset.view==='2d');
  assert.equal(await page.locator('#wm-pos .wm-station').count()>0,true);
  assert.match(await page.locator('#wm-view').getAttribute('title'),/WebGL/);assert.deepEqual(errors,[]);
 } finally {await browser.close();}
});

test('checkout shows the actual latest repeated scan in the side panel without covering the model; saved delivery orders have separate statuses',async()=>{
 const payload=fixture(),now=payload.serverTimeUtc;
 payload.employees[0].person='Lan';payload.people[0].person='Lan';
 payload.checkouts[0].customerName='Nguyễn Minh';
 payload.checkouts[0].lines=[{lineId:3,itemName:'Gạo ST25',unitName:'Túi',quantity:2,unitPrice:66000,lineTotal:132000},
  {lineId:4,itemName:'Muối',unitName:'Gói',quantity:1,unitPrice:10000,lineTotal:10000}];
 payload.checkouts[0].grandTotal=142000;payload.checkouts[0].balanceDue=92000;
 payload.events=[{id:1,userId:7,person:'Lan',module:'pos',terminal:'Q1',action:'POS.ScanToCurrentCart',
  text:'vừa quét mã hàng vào giỏ',workKey:'order:42',occurredAtUtc:now,
  posScan:{lineId:3,itemName:'Gạo ST25',unitName:'Túi',addedQuantity:1,quantity:2,unitPrice:66000,lineTotal:132000}}];
 payload.delivery.orders=[{orderId:20,code:'GH-20',recipient:'a thanh',state:'Created',updatedAtUtc:'2026-10-06T03:00:00Z',lines:[]},
  {orderId:21,code:'GH-21',recipient:'A Nghĩa',state:'ReadyToSettle',updatedAtUtc:'2026-10-08T03:00:00Z',lines:[]}];
 const {browser,page,errors}=await setup('3d',true,payload);
 try {
  await page.waitForFunction(()=>window.GaoStoreLiveMonitor?.scene,{timeout:45000});
  await page.waitForSelector('#wm3-labels [data-actor-id="employee:7"]');
  const overview=await page.locator('#wm3-detail').innerText();
  assert.match(overview,/Đơn giao hàng đã lưu/);assert.match(overview,/Giao cho a thanh/);assert.match(overview,/Chờ soạn/);
  assert.match(overview,/Giao cho A Nghĩa/);assert.match(overview,/Chờ chốt tại quầy/);
  await page.locator('[data-view-area="pos"]').click();
  const detail=await page.locator('#wm3-detail').innerText();
  assert.match(detail,/Vừa quét sản phẩm/);assert.match(detail,/Gạo ST25/);assert.match(detail,/Thêm 1 Túi/);assert.match(detail,/66.000/);
  assert.doesNotMatch(detail,/a thanh|A Nghĩa/);
  assert.match(await page.locator('.wm3-scanned-row').innerText(),/Gạo ST25/);
  assert.equal(await page.locator('#wm3-labels .wm3-scan-label,#wm3-labels [data-checkout]').count(),0);
  assert.doesNotMatch(await page.locator('#wm3-labels').innerText(),/Gạo ST25|66.000/);
  await page.evaluate(()=>{
   const p=structuredClone(window.payload);p.serverTimeUtc=new Date().toISOString();
   p.checkouts[0].lines[0].quantity=3;p.checkouts[0].lines[0].lineTotal=198000;p.checkouts[0].grandTotal=208000;p.checkouts[0].balanceDue=158000;
   p.events.unshift({...p.events[0],id:2,occurredAtUtc:p.serverTimeUtc,posScan:{...p.events[0].posScan,quantity:3,lineTotal:198000}});
   window.publish(p);
  });
  assert.match(await page.locator('#wm3-detail').innerText(),/3 Túi/);
  assert.match(await page.locator('#wm3-detail').innerText(),/208.000/);
  assert.equal(await page.locator('#wm3-labels .wm3-scan-label,#wm3-labels [data-checkout]').count(),0);
  const capture=path.join(root,'Logs/store-monitor-3d-scan-20261009');fs.mkdirSync(capture,{recursive:true});
  await page.screenshot({path:path.join(capture,'checkout-side-panel.png')});
  // The feed contains earlier events, but a new current order must not inherit the old scan details.
  await page.evaluate(()=>{const p=structuredClone(window.payload);p.serverTimeUtc=new Date().toISOString();
   p.checkouts[0].orderId=43;p.checkouts[0].lines=[{lineId:8,itemName:'Đường',unitName:'Kg',quantity:1,unitPrice:20000,lineTotal:20000}];window.publish(p);});
  assert.match(await page.locator('#wm3-detail').innerText(),/Chưa ghi nhận lần quét/);
  assert.doesNotMatch(await page.locator('#wm3-detail').innerText(),/Gạo ST25/);
  await page.evaluate(()=>{const p=structuredClone(window.payload);p.serverTimeUtc=new Date().toISOString();p.checkouts=[];window.publish(p);});
  await page.waitForFunction(()=>document.querySelectorAll('[data-checkout]').length===0);
  assert.deepEqual(errors,[]);
 } finally {await browser.close();}
});
