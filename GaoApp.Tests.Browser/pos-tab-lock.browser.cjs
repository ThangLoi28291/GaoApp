const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
 const browser = await chromium.launch({ channel: 'chrome', headless: true });
 try {
  const context = await browser.newContext();
  const errors = []; context.on('page', p => p.on('pageerror', e => errors.push(e.message)));
  const boot = { storeId: 1, terminalId: 2, userId: 3, shiftId: 4, antiForgeryToken: 'test',
   expiresAtUtc: new Date(Date.now()+3600000).toISOString(), permissions: [],
   screen: { currentDraft: null, draftOrders: [], heldOrders: [] } };
  await context.route('http://localhost/**', async route => {
   const url = new URL(route.request().url());
   const json = data => route.fulfill({ contentType: 'application/json', body: JSON.stringify(data) });
   if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: `<div id="posShell" data-store-id="1" data-terminal-id="2" data-user-id="3"></div>
    <script src="/pos.offline.core.js"></script><script src="/pos.offline.js"></script>
    <script>window.booted=Promise.all([PosOffline.init(),PosOffline.init(),PosOffline.init()]);</script>` });
   if (['/pos.offline.core.js','/pos.offline.js'].includes(url.pathname)) return route.fulfill({ path: path.resolve(__dirname, '../GaoApp.Web/wwwroot/Admin/js/pos',url.pathname.slice(1)), contentType: 'text/javascript' });
   if (/offline\/(bootstrap|status)$/.test(url.pathname)) return json(boot);
   if (/offline\/(catalog|customers)$/.test(url.pathname)) return json({ items: [], nextAfterId: null });
   if (url.pathname.endsWith('/promotions')) return json([]);
   if (url.pathname.endsWith('/screen')) return json(boot.screen);
   return route.fulfill({status:404,body:''});
  });
  const first = await context.newPage(); await first.goto('http://localhost/');
  await first.waitForFunction(() => PosOffline.status().ready);
  assert.equal(await first.evaluate(async () => { await PosOffline.init(); return PosOffline.status().writer; }),true);
  assert.equal(await first.evaluate(async () => (await navigator.locks.query()).held.filter(x=>x.name==='gao-pos-terminal:1:2').length),1);
  console.log('PASS: repeated/concurrent init in one tab retains exactly one writer lock');
  for(let i=0;i<3;i++) { await first.reload(); await first.waitForFunction(()=>PosOffline.status().ready); }
  console.log('PASS: reloading a single tab releases and reacquires its lock');
  const second = await context.newPage(); await second.goto('http://localhost/');
  await second.waitForFunction(()=>PosOffline.status().message.includes('tab khÃ¡c'));
  assert.equal(await second.evaluate(()=>PosOffline.status().writer),false);
  assert.equal(await second.evaluate(async()=> (await fetch('/admin/pos/cart/current/scan',{method:'POST',body:'{}'})).status),409);
  console.log('PASS: actual second tab cannot write');
  await first.close();
  await second.waitForFunction(()=>PosOffline.status().ready && PosOffline.status().writer,{},{timeout:10000});
  assert.doesNotMatch(await second.evaluate(()=>PosOffline.status().message),/tab khÃ¡c/);
  console.log('PASS: remaining tab automatically recovers after owner closes');
  assert.deepEqual(errors,[]); await context.close();
 } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
