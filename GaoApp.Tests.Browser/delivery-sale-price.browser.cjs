const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const path=require('node:path');
(async()=>{
    const info=JSON.parse(await new Promise(resolve=>{let value='';process.stdin.on('data',x=>value+=x);process.stdin.on('end',()=>resolve(value));}));
    const browser=await chromium.launch({channel:'chrome',headless:true});
    const page=await browser.newPage({viewport:{width:1280,height:960}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
    try{
        await page.goto(info.baseUrl+'/admin/account/login');await page.locator('[name=UserName]').fill(info.user);await page.locator('[name=Password]').fill(info.password);await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminal));
        await Promise.all([page.waitForURL(u=>!u.pathname.endsWith('/login')),page.locator('#loginForm button[type=submit]').click()]);
        await page.goto(info.baseUrl+'/admin/deliveries');
        // Lookup accepts the persisted delivery code/token, so obtain the code through the authorized endpoint.
        const detail=await page.evaluate(async id=>(await fetch('/admin/api/deliveries/'+id,{cache:'no-store'})).json(),info.orderId);
        await page.goto(info.baseUrl+'/admin/deliveries?key='+encodeURIComponent(detail.code));
        await page.locator('.dd-price-editor>summary').waitFor();await page.locator('.dd-price-editor>summary').click();
        await page.getByLabel(/Đơn giá mới/).fill('17500');await page.getByLabel('Lý do sửa giá').fill('Điều chỉnh trên máy tính');
        assert.ok((await page.locator('.dd-price-impact').innerText()).includes('70.000'));
        await page.screenshot({path:path.join(info.output,'desktop-edit.png'),fullPage:true});
        let requests=[];
        await page.route('**/admin/api/deliveries/*/sale-prices',async route=>{requests.push(route.request().postDataJSON());const response=await route.fetch();assert.equal(response.status(),200);await route.abort('failed');});
        await page.getByRole('button',{name:'Lưu giá bán',exact:true}).click();
        await page.waitForFunction(()=>document.querySelector('#ddPending')&&!document.querySelector('#ddPending').hidden);
        await page.unroute('**/admin/api/deliveries/*/sale-prices');
        page.on('request',request=>{if(request.url().endsWith('/sale-prices'))requests.push(request.postDataJSON());});
        await page.locator('#ddPending button').first().click();
        await page.waitForFunction(()=>document.querySelector('#ddMessage')?.textContent.includes('Đã cập nhật giá bán'));
        await page.waitForFunction(()=>document.querySelector('#ddDetail')?.textContent.includes('Điều chỉnh trên máy tính'));
        await page.locator('.dd-price-editor>summary').waitFor();
        assert.equal(requests.length,2);assert.deepEqual(requests[0],requests[1]);
        await page.screenshot({path:path.join(info.output,'desktop.png'),fullPage:true});
        await page.setViewportSize({width:390,height:844});
        await page.locator('.dd-price-editor>summary').click();await page.getByLabel(/Đơn giá mới/).fill('16500');await page.getByLabel('Lý do sửa giá').fill('Điều chỉnh trên điện thoại');
        assert.ok((await page.locator('.dd-price-impact').innerText()).includes('66.000'));
        await page.screenshot({path:path.join(info.output,'phone.png'),fullPage:true});
        assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
        await page.getByRole('button',{name:'Lưu giá bán',exact:true}).click();
        await page.waitForFunction(()=>document.querySelector('.dd-help')&&document.querySelector('#ddDetail').textContent.includes('Điều chỉnh trên điện thoại'));
        const updated=await page.evaluate(async id=>(await fetch('/admin/api/deliveries/'+id+'/picking',{cache:'no-store'})).json(),info.orderId);
        assert.equal(updated.salePrices.totalText,'66000');assert.equal(updated.lines.find(x=>x.isActive).unitPriceText,'16500');
        assert.deepEqual(errors,[]);console.log('Desktop, phone, exact price preview and lost-response replay passed.');
    }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
