const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), { chromium } = require('playwright');
(async () => {
 const info = JSON.parse(await new Promise(r => { let s = ''; process.stdin.on('data', x => s += x); process.stdin.on('end', () => r(s)); }));
 const browser = await chromium.launch({ channel: 'chrome', headless: true });
 const context = await browser.newContext({ viewport: { width: 1600, height: 1000 } }), page = await context.newPage(), errors = [];
 page.on('pageerror', e => errors.push(e.message)); page.on('dialog', d => d.accept());
 const out = path.resolve('TestResults/menu-visibility'); fs.mkdirSync(out, { recursive: true });
 const settled = () => page.waitForFunction(() => document.querySelector('.mv-editor')?.getAttribute('aria-busy') === 'false');
 const select = async (type, id) => { await page.locator(`.mv-tabs [data-type=${type}]`).click(); await page.locator(`.mv-subject[data-type=${type}][data-id="${id}"]`).click(); await settled(); };
 const save = async () => { await page.locator('#mvSave').click(); await settled(); assert.match(await page.locator('#mvMessage').innerText(), /Đã lưu/); };
 try {
  await page.goto(info.baseUrl + '/admin/account/login');
  await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
  if (await page.locator('[name=SelectedTerminalId]').count()) await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
  await Promise.all([page.waitForURL(u => !u.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
  await page.goto(info.baseUrl + '/Admin/AdminMenus'); await page.getByRole('link', { name: 'Menu theo nhân viên / nhóm' }).click(); await settled();
  await select('role', info.roleId);
  await page.screenshot({ path: path.join(out, 'desktop.png'), fullPage: true });
  const checkbox = page.locator(`[data-menu-id="${info.menuId}"]`);
  await checkbox.uncheck(); assert.ok(await page.locator('#mvSave').isEnabled()); await save();
  await page.reload(); await settled(); await select('role', info.roleId); assert.equal(await checkbox.isChecked(), false);
  assert.equal(await page.locator('#layout-menu').getByText('Sản phẩm', { exact: true }).count(), 0);
  await select('employee', info.employeeId); assert.equal(await checkbox.isChecked(), false); assert.equal((await page.locator('#mvMode').innerText()).trim(), 'Theo nhóm');
  await checkbox.check(); await save(); assert.equal((await page.locator('#mvMode').innerText()).trim(), 'Cá nhân');
  await page.reload(); await settled(); await select('employee', info.employeeId);
  assert.equal(await page.locator('#layout-menu').getByText('Sản phẩm', { exact: true }).count(), 1);
  await page.screenshot({ path: path.join(out, 'employee.png'), fullPage: true });
  await page.locator('#mvReset').click(); assert.equal(await checkbox.isChecked(), false); await save();
  await select('role', info.roleId); await page.locator('#mvHideAll').click(); assert.match(await page.locator('#mvPreview').innerText(), /Không có menu/);
  await page.locator('#mvShowAll').click(); assert.ok(await checkbox.isChecked());
  await page.locator('#mvReload').click(); await settled(); assert.equal(await checkbox.isChecked(), false);
  await page.locator('#mvMenuSearch').fill('san pham'); assert.ok(await checkbox.isVisible());
  await page.locator('#mvMenuSearch').fill('zzzz'); assert.match(await page.locator('#mvTree').innerText(), /Không tìm thấy/); await page.locator('#mvMenuSearch').fill('');
  // Simulate failed save: edits must remain available for retry.
  await checkbox.check(); await page.route('**/AdminMenuVisibility/Save', route => route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Máy chủ tạm gián đoạn' }) }));
  await page.locator('#mvSave').click(); await settled(); assert.match(await page.locator('#mvMessage').innerText(), /gián đoạn/); assert.ok(await checkbox.isChecked()); assert.ok(await page.locator('#mvSave').isEnabled());
  await page.unroute('**/AdminMenuVisibility/Save'); await save();
  for (const viewport of [{ width: 1366, height: 768 }, { width: 1024, height: 768 }, { width: 390, height: 844 }]) {
   await page.setViewportSize(viewport); await page.locator('#mvSave').scrollIntoViewIfNeeded();
   assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `Overflow at ${viewport.width}`);
   assert.ok(await page.locator('#mvSave').isVisible());
   await page.screenshot({ path: path.join(out, `${viewport.width}.png`), fullPage: true });
  }
  assert.deepEqual(errors, []); console.log('PASS: group bulk save, reload, personal override, reset inheritance, preview, search, discard, failed save retry, desktop/tablet/mobile layout.');
 } catch (error) { console.error(JSON.stringify({url:page.url(),errors,body:(await page.locator("body").innerText()).slice(0,2000)})); await page.screenshot({path:path.join(out,"failure.png"),fullPage:true}); throw error; } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });

