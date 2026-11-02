'use strict';
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const {review,settle,evidence,save}=require('./security-workspace-mobile-review.cjs');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let s='';process.stdin.on('data',v=>s+=v);process.stdin.on('end',()=>resolve(s));}));
 const browser=await chromium.launch({channel:'chrome',headless:true});
 const context=await browser.newContext({viewport:{width:390,height:844},reducedMotion:'reduce'});
 const page=await context.newPage(),errors=[],external=[],writes=[];
 page.on('pageerror',e=>errors.push(e.message));
 await context.route('**/*',route=>{const u=new URL(route.request().url());if(u.origin!==new URL(info.baseUrl).origin&&!['data:','blob:'].includes(u.protocol)){external.push(u.origin+u.pathname);return route.abort();}return route.continue();});
 page.on('request',r=>{const u=new URL(r.url());if(!['GET','HEAD'].includes(r.method())&&!/^\/admin\/api\/store-activity\/(?:online|presence)$/.test(u.pathname))writes.push({method:r.method(),path:u.pathname});});
 const state=async(label,options={})=>{
  await review(page,'[data-mobile-shell-content]',label,info.output,options);
  const contrast=await page.evaluate(()=>{
   // Let the browser convert rgb(), color-mix() and CSS Color 4 values to sRGB pixels.
   const canvas=document.createElement('canvas');canvas.width=canvas.height=1;
   const ctx=canvas.getContext('2d',{colorSpace:'srgb',willReadFrequently:true});
   const rgb=s=>{ctx.clearRect(0,0,1,1);ctx.fillStyle=s;ctx.fillRect(0,0,1,1);const c=[...ctx.getImageData(0,0,1,1).data];return[c[0],c[1],c[2],c[3]/255];};
   const luminance=c=>c.slice(0,3).map(v=>v/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4).reduce((n,v,i)=>n+v*[.2126,.7152,.0722][i],0);
   return [...document.querySelectorAll('[data-mobile-shell-content] .alert-warning,[data-mobile-shell-content] .badge.bg-success,[data-mobile-shell-content] .badge.text-success,[data-mobile-shell-content] .badge.text-info,[data-mobile-shell-content] .badge.bg-label-success,[data-mobile-shell-content] .badge.bg-label-warning')].filter(n=>n.checkVisibility()).map(n=>{
    const chain=[];for(let p=n;p;p=p.parentElement)chain.unshift(p);let bg=[255,255,255];
    for(const p of chain){const c=rgb(getComputedStyle(p).backgroundColor);bg=bg.map((v,i)=>c[i]*c[3]+v*(1-c[3]));}
    const foreground=getComputedStyle(n).color,a=luminance(rgb(foreground)),b=luminance(bg);return{text:n.textContent.trim().slice(0,100),foreground,compositedBackground:bg,ratio:(Math.max(a,b)+.05)/(Math.min(a,b)+.05)};
   });
  });
  assert.ok(contrast.every(c=>c.ratio>=4.5),JSON.stringify({label,contrast}));
  evidence.contrastChecks||=[];evidence.contrastChecks.push({label,checks:contrast});save(info.output);
 };
 const go=async route=>{await page.goto(info.baseUrl+route);await page.locator('[data-ui21-view]').waitFor({state:'attached'});await settle(page);};
 const login=async account=>{
  await page.goto(info.baseUrl+'/admin/account/login');await page.locator('[name=UserName]').fill(account.user);await page.locator('[name=Password]').fill(account.password);
  if(await page.locator('[name=SelectedTerminalId]').count())await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
  await Promise.all([page.waitForURL(u=>!u.pathname.endsWith('/login')),page.locator('#loginForm button[type=submit]').click()]);
 };
 try{
  await login(info.accounts.admin);
  await go('/Admin/UserInStores');await page.locator('#employeeMobileList [data-employee-row]').first().waitFor();
  await state('employees-list');
  await page.locator('#employeeOpenMobileFilters').click();await page.locator('#employeeMobileFilterSheet.show').waitFor();
  await state('employees-filter-sheet',{dialog:true,nativeDialog:'#employeeMobileFilterSheet'});
  await page.locator('#employeeMobileFilterSheet [data-bs-dismiss=offcanvas]').click();await page.locator('#employeeMobileFilterSheet.show').waitFor({state:'hidden'});
  await page.locator('#employeeMobileList [data-employee-quick="'+info.mappingId+'"]').click();await page.locator('#employeeQuickView.show').waitFor();
  await state('employee-native-profile',{dialog:true,nativeDialog:'#employeeQuickView'});
  await page.locator('#employeeQuickView .btn-close').click();await page.locator('#employeeQuickView.show').waitFor({state:'hidden'});
  await page.locator('#employeeMobileList [data-employee-reset="'+info.mappingId+'"]').click();await page.locator('#employeeResetPasswordModal.show').waitFor();
  await page.locator('#employeeNewPassword').fill('UI21 unsaved new password!');await page.locator('#employeeResetPasswordModal [name=Reason]').fill('Bản nháp chưa gửi');
  await state('employee-password-challenge-draft',{dialog:true,nativeDialog:'#employeeResetPasswordModal'});
  await page.locator('#employeeResetPasswordModal .btn-close').click();await page.locator('#employeeResetPasswordModal.show').waitFor({state:'hidden'});
  await go('/Admin/UserInStores/Create');
  await page.locator('#newPassword').fill('UI21 unsaved password!');await page.locator('#togglePasswordBtn').click();assert.equal(await page.locator('#newPassword').getAttribute('type'),'text');
  await page.locator('#togglePasswordBtn').click();assert.equal(await page.locator('#newPassword').getAttribute('type'),'password');
  await state('employee-create');
  await page.locator('[name=UserName]').first().fill('ui21_unsaved_employee');await page.locator('[name=FullName]').first().fill('Bản nháp nhân viên chưa lưu');await state('employee-create-draft');
  await go('/Admin/UserInStores/Edit/'+info.mappingId);await page.locator('[name=Note]').fill('UI21 ghi chú chưa lưu · kiểm tra đổi kích thước');await state('employee-edit-draft');
  await go('/Admin/Roles');await page.locator('#roleMobileList a').first().waitFor();await state('roles-list');
  await go('/Admin/Roles/Create');await page.locator('[name=Code]').fill('ui21_draft_role');await page.locator('[name=Name]').fill('Vai trò chưa lưu · ca tối');await state('role-create-draft');
  await go('/Admin/Roles/Edit/'+info.targetRoleId);await page.locator('[name=Name]').fill('UI21 tên vai trò chưa lưu');await state('role-edit-draft');
  await go('/Admin/RolePermissions');await state('permissions-landing');
  await go('/Admin/RolePermissions?roleId='+info.targetRoleId);await page.locator('.permission-checkbox').first().waitFor({state:'attached'});await state('permissions-matrix');
  await page.locator('#permissionSearch').fill('in tem');await state('permissions-native-search');await page.locator('#selectVisible').click();assert.notEqual(await page.locator('#changedPermissionCount').innerText(),'0');
  await page.locator('#reviewChanges').click();await page.locator('#changesDialog').waitFor();
  await page.locator('#changesDialog [name=Reason]').fill('UI21 bản nháp kiểm tra, chưa cấp quyền');
  await state('permissions-review-challenge',{dialog:true,nativeDialog:'#changesDialog'});
  await page.locator('#changesDialog .rp-close').click();await page.locator('#undoChanges').click();assert.equal(await page.locator('#changedPermissionCount').innerText(),'0');
  await go('/admin/pos-terminals');await state('pos-terminals-list');
  await go('/admin/pos-terminals/create');await page.locator('[name=Code]').fill('UI21-PHONE');await page.locator('[name=Name]').fill('UI21 quầy điện thoại · tên dài kiểm tra bố cục');await state('pos-terminal-create-draft');
  await Promise.all([page.waitForURL(u=>u.pathname==='/admin/pos-terminals'),page.getByRole('button',{name:'Tạo máy POS',exact:true}).click()]);
  const section=page.locator('section.card').filter({hasText:'UI21-PHONE'});const keyUrl=await section.locator('a[href*="/keys/create"]').getAttribute('href');assert.ok(keyUrl);
  await go(keyUrl);await page.locator('[name=DeviceName]').fill('UI21 thiết bị điện thoại');await state('pos-device-key-draft');
  await Promise.all([page.waitForNavigation(),page.getByRole('button',{name:'Tạo khóa máy',exact:true}).click()]);
  const deviceKey=await page.locator('#createdDeviceKey').inputValue();assert.match(deviceKey,/^[a-f0-9]{64}$/);await state('pos-device-key-once');
  await go('/admin/pos-terminals');assert.ok(!(await page.locator('[data-mobile-shell-content]').innerText()).includes(deviceKey));await state('pos-device-masked-list');
  page.once('dialog',d=>d.dismiss());await page.locator('section.card').filter({hasText:'UI21-PHONE'}).locator('[data-revoke-key] button').click();
  assert.ok(await page.locator('section.card').filter({hasText:'UI21-PHONE'}).locator('[data-revoke-key]').count());
  page.once('dialog',d=>d.accept());await Promise.all([page.waitForNavigation(),page.locator('section.card').filter({hasText:'UI21-PHONE'}).locator('[data-revoke-key] button').click()]);await state('pos-device-revoked');
  await go('/admin/kiosks');await state('kiosk-empty');
  const term=page.locator('[name=TerminalId] option').filter({hasText:'UI21-PHONE'});await page.locator('[name=TerminalId]').selectOption(await term.getAttribute('value'));await state('kiosk-creation-draft');
  await Promise.all([page.waitForNavigation(),page.getByRole('button',{name:'Tạo và cấp khóa kích hoạt',exact:true}).click()]);
  assert.ok((await page.locator('textarea[readonly]').inputValue()).length>=20);await state('kiosk-activation-once');
  await go('/admin/kiosks');await page.locator('[data-kiosk-id]').waitFor();await state('kiosk-waiting-activation',{dynamic:true});
  await page.locator('form[action$="/pause"] button').click();await page.waitForFunction(()=>document.querySelector('[data-kiosk-id] [data-status]')?.textContent==='Tạm dừng');await state('kiosk-paused',{dynamic:true});
  await go('/admin/audit-logs');await page.locator('#auditLogTable tbody button').first().waitFor();
  await page.locator('#keyword').fill('UI21');
  await Promise.all([page.waitForResponse(r=>r.request().method()==='GET'&&new URL(r.url()).pathname==='/admin/api/audit-logs'&&new URL(r.url()).searchParams.get('keyword')==='UI21'),page.locator('#btnSearch').click()]);
  await page.waitForFunction(()=>{const rows=[...document.querySelectorAll('#auditLogTable tbody tr')];return rows.length===1&&rows[0].textContent.includes('UI21 Kiểm thử');});
  await state('audit-filtered-results');
  await page.locator('#auditLogTable tbody button').first().click();await page.locator('#auditLogDetailModal.show').waitFor();assert.ok(!(await page.locator('#auditLogDetailModal').innerText()).includes('UI21_FOREIGN_PRIVATE'));await state('audit-native-detail',{dialog:true,nativeDialog:'#auditLogDetailModal'});
  await page.locator('#auditLogDetailModal .btn-close').click();await page.locator('#auditLogDetailModal.show').waitFor({state:'hidden'});
  await page.locator('#keyword').fill('UI21-no-matching-event');await page.locator('#btnSearch').click();await page.getByText('Không có dữ liệu',{exact:true}).waitFor();await state('audit-empty-filter');
  await context.clearCookies();await login(info.accounts.reader);
  await go('/Admin/UserInStores');await page.locator('#employeeMobileList [data-employee-row]').first().waitFor();assert.equal(await page.locator('a[href*="UserInStores/Create" i]').count(),0);await state('employees-view-only');
  await go('/Admin/RolePermissions?roleId='+info.targetRoleId);assert.equal(await page.locator('#rolePermissionsPage').getAttribute('data-can-manage'),'false');await state('permissions-view-only');
  assert.deepEqual(errors,[]);assert.deepEqual(external,[]);
  save(info.output);fs.writeFileSync(path.join(info.output,'security-workspace-result.json'),JSON.stringify({passed:true,schemaMode:'strict_full_migration',nativeStateCount:evidence.cases.length,phoneChecks:evidence.phoneChecks.length,desktopProofs:evidence.desktopProofs.length,printProofs:evidence.printProofs.length,pageErrors:errors,externalRequests:external,nativeCommands:writes},null,2)+'\n');
  console.log('PASS UI-21 native phone matrix, desktop/print parity, drafts/challenges, device lifecycle, kiosk and scoped audit.');
 }catch(error){await page.screenshot({path:path.join(info.output,'native-failure.png'),fullPage:true}).catch(()=>{});save(info.output);throw error;}
 finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
