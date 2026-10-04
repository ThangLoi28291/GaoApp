const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), { chromium } = require('playwright');
(async () => {
 const info = JSON.parse(await new Promise(r => {let s=''; process.stdin.on('data', x=>s+=x); process.stdin.on('end',()=>r(s));}));
 const browser = await chromium.launch({channel:'chrome',headless:true});
 const page = await browser.newPage({viewport:{width:1440,height:900}}), errors=[];
 page.on('pageerror', e=>errors.push(e.message));
 const out = path.resolve('TestResults/input-invoice-library'); fs.mkdirSync(out,{recursive:true});
 try {
  await page.goto(info.baseUrl+'/admin/account/login'); await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
  if(await page.locator('[name=SelectedTerminalId]').count()) await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
  await Promise.all([page.waitForURL(u=>!u.pathname.endsWith('/login')),page.locator('button[type=submit]').click()]);
  await page.goto(info.baseUrl+'/admin/input-invoices'); await page.waitForFunction(()=>document.getElementById('ilTotal')?.textContent==='8');
  assert.equal(await page.locator('#ilRows [data-open]').count(),8);
  assert.match(await page.locator('#ilRows').innerText(),/MST khác thư mục nguồn/);
  await page.screenshot({path:path.join(out,'desktop.png'),fullPage:true});
  await page.locator('#ilRows [data-open]').first().click(); await page.waitForFunction(()=>document.querySelector('#ilPreviewContent article'));
  assert.match(await page.locator('#ilDetailMeta').innerText(),/MST thư mục nguồn/);
  await page.locator('#ilReviewStatus').selectOption('3'); await page.locator('#ilSave').click(); assert.match(await page.locator('#ilDetailMessage').innerText(),/ghi lý do/);
  await page.locator('#ilReviewNote').fill('Cần đối chiếu với nhà cung cấp, cửa hàng chưa nhận hàng.');
  await page.locator('#ilSave').click(); await page.waitForFunction(()=>document.getElementById('ilDetailMessage').textContent==='Đã lưu xác nhận.');
  assert.match(await page.locator('#ilHistory').innerText(),/chưa nhận hàng/);
  for(const v of [{width:1366,height:600},{width:1024,height:600},{width:390,height:700}]) {
   await page.setViewportSize(v);
   const bounds=await page.locator('#ilSave').boundingBox(); assert.ok(bounds.y>=0 && bounds.y+bounds.height<=v.height,`footer clipped at ${v.width}`);
   const box=await page.locator('#ilDetail').boundingBox(); assert.ok(box.width<=v.width);
   await page.screenshot({path:path.join(out,`review-${v.width}.png`),fullPage:true});
  }
  await page.locator('#ilDetail footer [data-close]').click(); await page.setViewportSize({width:1440,height:900});
  await page.locator('[name=review]').selectOption('3'); await page.locator('#ilFilters button[type=submit]').click();
  await page.waitForFunction(()=>document.getElementById('ilTotal').textContent==='1');
  await page.reload(); await page.waitForFunction(()=>document.getElementById('ilTotal').textContent==='8');
  await page.locator('[name=kind]').selectOption('1'); await page.locator('#ilFilters button[type=submit]').click(); await page.waitForFunction(()=>document.getElementById('ilTotal').textContent==='1');
  await page.locator('#ilUpload').click(); assert.ok(await page.locator('#ilUploadForm input[name=xml]').isVisible()); await page.locator('#ilUploadDialog [data-close]').first().click();
  assert.deepEqual(errors,[]); console.log('PASS: real SQL catalog, filters, XML preview, required reason, save/reload audit history, fixed footer at 1366x600/1024x600/390x700.');
 } catch(e) { console.error((await page.locator('body').innerText()).slice(-2400)); await page.screenshot({path:path.join(out,'failure.png'),fullPage:true}); throw e; }
 finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
