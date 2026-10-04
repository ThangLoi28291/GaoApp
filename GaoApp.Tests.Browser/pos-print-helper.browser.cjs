'use strict';
// Hermetic HTTPS-origin browser test + real loopback HTTP transport. No physical printer or database.
const {chromium} = require('playwright');
const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const web = path.resolve(__dirname, '../GaoApp.Web');
const templates = require(path.join(web, 'wwwroot/Admin/js/printing/receipt.templates.js'));
const origin = 'https://gao-helper.test';
const output = process.env.GAO_HELPER_TEST_OUTPUT;
const receipt = {orderId:123,orderNumber:'TEST-HELPER-123',storeName:'CỬA HÀNG THỬ NGHIỆM',createdAtUtc:'2026-09-29T00:00:00Z',
    cashierName:'Thu ngân thử',customerName:'Khách thử nghiệm',subtotal:50000,grandTotal:50000,paidTotal:50000,status:'Completed',changeDue:0,
    lines:[{itemName:'Gạo thơm đặc biệt — tiếng Việt đầy đủ',quantity:1.25,sellingUnitName:'Kg',unitPrice:40000,lineTotal:50000}],payments:[{method:'Cash',amount:50000}]};
let printReceipt=receipt;
let pending, behavior='hold', health=true;
const posts=[],errors=[],preflights=[];
const server=http.createServer((req,res)=>{
    if(req.headers.origin!==origin){res.writeHead(403);res.end();return;}
    res.setHeader('Access-Control-Allow-Origin',origin);res.setHeader('Vary','Origin');
    if(req.method==='OPTIONS'){preflights.push(req.url);res.setHeader('Access-Control-Allow-Methods','GET, POST, OPTIONS');res.setHeader('Access-Control-Allow-Headers','Content-Type');res.setHeader('Access-Control-Allow-Private-Network','true');res.writeHead(204);res.end();return;}
    res.setHeader('Content-Type','application/json');
    if(req.url==='/health'){res.end(JSON.stringify({ok:health,protocol:'gao-raster-v1',width:576}));return;}
    assert.equal(req.url,'/print');assert.equal(req.method,'POST');
    let body='';req.on('data',chunk=>body+=chunk);req.on('end',()=>{
        const payload=JSON.parse(body);posts.push(payload);
        const finish=()=>{res.statusCode=behavior==='fail'?503:200;res.end(JSON.stringify({ok:behavior!=='fail',protocol:'gao-raster-v1',printed:true,copies:payload.copies}));};
        if(behavior==='hold')pending=finish;else finish();
    });
});
(async()=>{
    await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(17880,'127.0.0.1',resolve)});
    const browser=await chromium.launch({channel:'chrome',headless:true});
    try{
        const context=await browser.newContext({deviceScaleFactor:576/(80*96/25.4)});
        // Ordinary site permission, not a disabled browser security flag.
        await context.grantPermissions(['local-network-access'],{origin});
        context.on('page',page=>page.on('pageerror',error=>errors.push(error.message)));
        await context.addInitScript(()=>{
            window.__browserPrints=0;window.__closed=0;window.print=()=>window.__browserPrints++;
            try{localStorage.setItem('gao-pos-print-v1:123:456',JSON.stringify({mode:'helper',copies:1,printer:'',templateKey:'itemwide-80'}))}catch{}
        });
        const routeSource=async route=>{
            const url=new URL(route.request().url());
            if(url.pathname.startsWith('/Admin/')){
                const source=fs.readFileSync(path.join(web,'wwwroot',url.pathname),'utf8');
                await route.fulfill({contentType:'text/javascript',body:source});return;
            }
            const setup={receipt:printReceipt,storeId:123,terminalId:456,templates:[],autoPrint:url.searchParams.get('auto')==='1',
                receiptDefault:{template:templates.builtIns().find(x=>x.key==='itemwide-80')},
                invoiceBuyerSelfServiceUrl:'https://example.invalid/synthetic-buyer',invoiceBuyerSelfServiceExpiresAtUtc:'2026-09-29T02:00:00Z'};
            if(url.pathname==='/studio'){
                const studioSetup={...setup,canManage:true,templates:templates.builtIns(),storeInfo:{storeName:'Cửa hàng thử',storeAddress:'',storePhone:''}};
                const raw=fs.readFileSync(path.join(web,'Areas/Admin/Views/ReceiptTemplates/Index.cshtml'),'utf8');
                const body='<!doctype html><meta charset="utf-8">'+raw.slice(raw.indexOf('<main'))
                    .replace('@Html.Raw(JsonSerializer.Serialize(setup, ReceiptTemplateService.Json))',JSON.stringify(studioSetup))
                    .replaceAll('src="~/','src="/').replaceAll('disabled="@(!setup.canManage)"','').replaceAll('hidden="@(!setup.canManage)"','');
                await route.fulfill({contentType:'text/html',body});return;
            }
            const scripts=['pos/qrcodegen','printing/receipt.templates','printing/print.lifecycle','printing/pos.printing']
                .map(name=>`<script src="/Admin/js/${name}.js"></script>`).join('');
            const body=url.pathname==='/parent'?`<!doctype html><p>POS thử nghiệm</p>${scripts}`:
                fs.readFileSync(path.join(web,'Areas/Admin/Views/ReceiptTemplates/Print.cshtml'),'utf8').split('<!doctype')[1]
                .replace('@Model.Receipt.OrderNumber',receipt.orderNumber)
                .replace('@Html.Raw(JsonSerializer.Serialize(Model, ReceiptTemplateService.Json))',JSON.stringify(setup))
                .replaceAll('src="~/','src="/')
                .replace('<script src="/Admin/js/printing/receipt.print-page.js"', '<script>GaoPrintLifecycle.close=()=>window.__closed++; const gate=new Promise(resolve=>window.__allowPrint=resolve), originalSend=PosPrinting.send; PosPrinting.send=async(...args)=>{await gate;return originalSend(...args)};</script><script src="/Admin/js/printing/receipt.print-page.js"');
            await route.fulfill({contentType:'text/html',headers:{'Content-Security-Policy':"frame-ancestors 'self'; object-src 'none'; base-uri 'self'"},body:url.pathname==='/parent'?body:'<!doctype'+body});
        };
        async function navigate(page,url){
            await page.goto('about:blank'); // Exercise a fresh receipt document, including hash-only handoff changes.
            await context.route(origin+'/**',routeSource);await page.goto(origin+url);
            // Remove interception before localhost requests: Fetch interception may bypass native CORS preflights.
            await context.unroute(origin+'/**',routeSource);await page.evaluate(()=>window.__allowPrint?.());
        }
        const page=await context.newPage();page.setDefaultTimeout(10000);
        await navigate(page,'/receipt?auto=1');
        assert.equal(await page.evaluate(()=>isSecureContext),true);
        await page.waitForFunction(()=>document.getElementById('printFeedback').textContent.includes('Đang gửi'));
        await waitFor(()=>posts.length===1);
        assert.equal(await page.evaluate(()=>__closed),0,'Do not close before ACK');
        await page.evaluate(()=>{document.getElementById('printReceipt').dispatchEvent(new Event('click'));document.getElementById('receiptPaper').onload();document.getElementById('receiptPaper').onload();});
        assert.equal(await page.locator('#printReceipt').isDisabled(),true);
        assert.equal(posts.length,1);pending();
        await page.waitForFunction(()=>__closed===1);
        await page.evaluate(()=>document.getElementById('printReceipt').dispatchEvent(new Event('click')));
        assert.equal(posts.length,1);assert.equal(await page.locator('#printReceipt').isDisabled(),true);
        assert.equal(posts[0].width,576);assert.equal(posts[0].cut,true);assert.equal(posts[0].cashDrawer,false);
        assert.ok(preflights.includes('/print'),'Real HTTP JSON POST must pass CORS preflight');
        const paper=page.frameLocator('#receiptPaper').locator('.receipt');
        const screenshot=await paper.screenshot({scale:'device'});
        const comparison=await page.evaluate(async({bitmap,png})=>{
            const image=new Image();image.src='data:image/png;base64,'+png;await image.decode();
            const canvas=document.createElement('canvas');canvas.width=bitmap.width;canvas.height=bitmap.height;
            const ctx=canvas.getContext('2d');ctx.fillStyle='#fff';ctx.fillRect(0,0,canvas.width,canvas.height);ctx.drawImage(image,0,0,canvas.width,canvas.height);
            const data=ctx.getImageData(0,0,canvas.width,canvas.height), bytes=atob(bitmap.rasterBase64);
            let differences=0,ink=0,referenceInk=0;
            for(let i=0;i<canvas.width*canvas.height;i++){
                const actual=!!(bytes.charCodeAt(i>>3)&(128>>(i&7))), expected=data.data[i*4]<128;
                if(actual!==expected)differences++;if(actual)ink++;if(expected)referenceInk++;
                data.data[i*4]=data.data[i*4+1]=data.data[i*4+2]=actual?0:255;data.data[i*4+3]=255;
            }
            ctx.putImageData(data,0,0);
            return {differences,ink,referenceInk,pixels:canvas.width*canvas.height,referenceWidth:image.width,referenceHeight:image.height,bitmapPng:canvas.toDataURL()};
        },{bitmap:posts[0],png:screenshot.toString('base64')});
        // Element screenshots round CSS clip bounds before scaling to device pixels.
        assert.ok(Math.abs(comparison.referenceWidth-576)<=4);
        assert.ok(Math.abs(comparison.referenceHeight-posts[0].height)<=4);
        assert.ok(comparison.differences/comparison.pixels<0.035,JSON.stringify(comparison).slice(0,250));
        assert.ok(comparison.ink/comparison.referenceInk>0.9 && comparison.ink/comparison.referenceInk<1.1);
        if(output){fs.mkdirSync(output,{recursive:true});fs.writeFileSync(path.join(output,'helper-preview.png'),screenshot);fs.writeFileSync(path.join(output,'helper-bitmap.png'),Buffer.from(comparison.bitmapPng.split(',')[1],'base64'));fs.writeFileSync(path.join(output,'synthetic-print-payload.json'),JSON.stringify(posts[0]));}
        const qr=await page.frameLocator('#receiptPaper').locator('.invoice-buyer-qr img').evaluate(img=>({loaded:img.complete&&img.naturalWidth>0,src:img.src}));
        assert.equal(qr.loaded,true);assert.ok(qr.src.startsWith('data:image/png;base64,'));
        const qrBounds=await page.frameLocator('#receiptPaper').locator('.invoice-buyer-qr img').evaluate(img=>{
            const a=img.getBoundingClientRect(),b=img.closest('.receipt').getBoundingClientRect(),scale=576/b.width;
            return {x:(a.left-b.left)*scale,y:(a.top-b.top)*scale,width:a.width*scale,height:a.height*scale};
        });
        const bitmap=Buffer.from(posts[0].rasterBase64,'base64');let qrInk=0;
        for(let y=Math.ceil(qrBounds.y);y<Math.floor(qrBounds.y+qrBounds.height);y++)for(let x=Math.ceil(qrBounds.x);x<Math.floor(qrBounds.x+qrBounds.width);x++)
            if(bitmap[y*72+(x>>3)]&(128>>(x&7)))qrInk++;
        assert.ok(qrInk>qrBounds.width*qrBounds.height*0.2,'Raster must contain QR modules, not just the QR caption');
        // Explicit failure: one POST, page open, no browser dialog. Manual retry is a new request.
        behavior='fail';await navigate(page,'/receipt');await paper.waitFor();await page.locator('#printReceipt').click();
        await page.locator('#printFeedback').filter({hasText:'Kiểm tra giấy đã ra'}).waitFor();
        assert.equal(posts.length,2);assert.equal(await page.evaluate(()=>__closed),0);
        assert.equal(await page.evaluate(()=>__browserPrints),0);
        assert.equal(await page.locator('#printReceipt').isEnabled(),true);
        behavior='ok';await page.locator('#printReceipt').click();await page.waitForFunction(()=>__closed===1);assert.equal(posts.length,3);
        // Helper stopped/incompatible: no print request at all.
        health=false;await navigate(page,'/receipt');await paper.waitFor();await page.locator('#printReceipt').click();
        await page.locator('#printFeedback').filter({hasText:'Không kết nối được Print Helper'}).waitFor();assert.equal(posts.length,3);health=true;
        // Offline/template-test popup: a manual click cancels the pending autoPrint timer.
        const parent=await context.newPage();await navigate(parent,'/parent');
        behavior='hold';const ready=context.waitForEvent('page');
        await parent.evaluate(({receipt,design})=>{
            window.__localClosed=0;GaoPrintLifecycle.close=()=>window.__localClosed++;
            const popup=PosPrinting.openLocal(receipt,{storeId:123,terminalId:456,receiptDefault:{template:{design}}},[],true);
            popup.document.querySelector('button').click();popup.document.querySelector('button').dispatchEvent(new Event('click'));
        },{receipt,design:templates.builtIns().find(x=>x.key==='itemwide-80').design});
        const popup=await ready;await waitFor(()=>posts.length===4);assert.equal(await parent.evaluate(()=>__localClosed),0);
        pending();await parent.waitForFunction(()=>__localClosed===1);
        await new Promise(resolve=>setTimeout(resolve,200));assert.equal(posts.length,4);
        assert.equal(posts[3].cashDrawer,false,'Template/test popup cannot open drawer');
        assert.equal(parent.isClosed(),false);await popup.close();
        // Existing settings view and studio JS: helper hides QZ controls, checks health and persists copies.
        const studio=await context.newPage();studio.setDefaultTimeout(10000);await navigate(studio,'/studio');
        await studio.frameLocator('#receiptPreview').locator('.itemwide').waitFor();
        assert.equal(await studio.locator('#helperSettings').isVisible(),true);assert.equal(await studio.locator('#qzSettings').isHidden(),true);
        await studio.locator('#checkHelper').click();await studio.locator('#studioMessage').waitFor();
        assert.match(await studio.locator('#studioMessage').textContent(),/sẵn sàng/);
        await studio.locator('#printCopies').fill('2');await studio.locator('#applyTemplate').click();
        const saved=await studio.evaluate(()=>JSON.parse(localStorage.getItem('gao-pos-print-v1:123:456')));
        assert.equal(saved.mode,'helper');assert.equal(saved.copies,2);assert.equal(saved.printer,'');
        assert.equal(saved.templateKey,'itemwide-80');
        await studio.locator('#printMode').selectOption('qz');assert.equal(await studio.locator('#qzSettings').isVisible(),true);
        await studio.locator('#applyTemplate').click();await studio.locator('#studioMessage').filter({hasText:'Chọn máy in'}).waitFor();
        await studio.locator('#printMode').selectOption('browser');await studio.locator('#applyTemplate').click();
        assert.equal(await studio.evaluate(()=>JSON.parse(localStorage.getItem('gao-pos-print-v1:123:456')).mode),'browser');
        assert.equal(await studio.locator('#printCopySettings').isHidden(),true);assert.equal(posts.length,4);
        // Cash-drawer policy runs against finalized numeric receipt state; no bill text is inspected.
        const drawerResults=[];
        const transfer={method:'BankTransfer',amount:50000},cash={method:'Cash',amount:50000};
        const scenarios=[
            ['cash only',[cash],0,true,true],
            ['mixed cash and transfer',[{...cash,amount:10000},{...transfer,amount:40000}],0,true,true],
            ['transfer exact',[transfer],0,true,false],
            ['QR exact',[{...transfer,reference:'QR-TEST'}],0,true,false],
            ['transfer overpay',[{...transfer,amount:60000}],10000,true,true],
            ['QR overpay',[{...transfer,amount:60000,reference:'QR-TEST'}],10000,true,true],
            ['cash reprint',[cash],0,false,false],
            ['overpaid transfer reprint',[{...transfer,amount:60000}],10000,false,false]
        ];
        behavior='ok';
        await parent.evaluate(()=>PosPrinting.savePreferences({storeId:123,terminalId:456},{mode:'helper',copies:1}));
        const ticket=async(auto=true)=>parent.evaluate(auto=>PosPrinting.postPaymentUrl('/admin/pos/receipt/123?auto='+(auto?'1':'0'),123),auto);
        for(const [name,payments,changeDue,immediate,expected] of scenarios){
            printReceipt={...receipt,payments,changeDue,paidTotal:payments.reduce((total,p)=>total+p.amount,0)};
            const before=posts.length;
            await navigate(page,immediate?await ticket():'/receipt?auto=1');
            await page.waitForFunction(()=>__closed===1);
            assert.equal(posts.length,before+1);assert.equal(posts.at(-1).cashDrawer,expected,name);
            assert.equal(typeof posts.at(-1).cashDrawer,'boolean');
            assert.equal(new URL(page.url()).hash,'');drawerResults.push({name,cashDrawer:expected});
        }
        printReceipt=receipt;
        // Preview consumes even a handed-off ticket without opening the drawer on manual print.
        await navigate(page,await ticket(false));await paper.waitFor();await page.locator('#printReceipt').click();
        await page.waitForFunction(()=>__closed===1);assert.equal(posts.at(-1).cashDrawer,false);
        // A failed first print can be retried manually, but cannot re-open the drawer.
        behavior='fail';await navigate(page,await ticket());
        await page.locator('#printFeedback').filter({hasText:'Kiểm tra giấy đã ra'}).waitFor();
        assert.equal(posts.at(-1).cashDrawer,true);
        behavior='ok';await page.locator('#printReceipt').click();await page.waitForFunction(()=>__closed===1);
        assert.equal(posts.at(-1).cashDrawer,false);
        // Preserve the real noopener popup path. Reopening its URL must be a reprint.
        const popupUrl=await ticket(),popupReady=context.waitForEvent('page');
        await context.route(origin+'/**',routeSource);
        await parent.evaluate(url=>{window.open(url,'_blank','noopener,noreferrer');},popupUrl);
        const cashPopup=await popupReady;await cashPopup.waitForLoadState('load');
        await context.unroute(origin+'/**',routeSource);await cashPopup.evaluate(()=>__allowPrint());
        await cashPopup.waitForFunction(()=>__closed===1);
        assert.equal(posts.at(-1).cashDrawer,true);assert.equal(await cashPopup.evaluate(()=>opener),null);
        await navigate(cashPopup,popupUrl);await cashPopup.waitForFunction(()=>__closed===1);
        assert.equal(posts.at(-1).cashDrawer,false);await cashPopup.close();
        // Real QR-style iframe gets the same one-use handoff.
        const frameUrl=await parent.evaluate(()=>PosPrinting.postPaymentUrl('/admin/pos/orders/123/print?auto=1',123));
        await context.route(origin+'/**',routeSource);
        await parent.evaluate(url=>{const frame=document.createElement('iframe');frame.id='qrReceipt';frame.src=url;document.body.appendChild(frame);},frameUrl);
        const receiptFrame=await (await parent.locator('#qrReceipt').elementHandle()).contentFrame();
        await receiptFrame.waitForLoadState('load');await context.unroute(origin+'/**',routeSource);
        await receiptFrame.evaluate(()=>__allowPrint());await receiptFrame.waitForFunction(()=>__closed===1);
        assert.equal(posts.at(-1).cashDrawer,true);
        await parent.locator('#qrReceipt').evaluate(frame=>frame.remove());
        // Concurrent copies request the same live POS intent; exactly one can open the drawer.
        for(let attempt=0;attempt<5;attempt++){
            const sharedUrl=await ticket(),twins=await Promise.all([context.newPage(),context.newPage()]);
            await context.route(origin+'/**',routeSource);
            await Promise.all(twins.map(tab=>tab.goto(origin+sharedUrl)));
            await context.unroute(origin+'/**',routeSource);
            const beforeTwins=posts.length;
            await Promise.all(twins.map(tab=>tab.evaluate(()=>__allowPrint())));
            await Promise.all(twins.map(tab=>tab.waitForFunction(()=>__closed===1)));
            assert.deepEqual(posts.slice(beforeTwins).map(p=>p.cashDrawer).sort(),[false,true]);
            await Promise.all(twins.map(tab=>tab.close()));
        }
        // Offline uses the already-finalized state; template/test calls keep the default false.
        for(const immediate of [true,false]){
            const ready=context.waitForEvent('page');
            await parent.evaluate(({receipt,immediate})=>{
                window.__localClosed=0;
                PosPrinting.openLocal({...receipt,status:2},{storeId:123,terminalId:456},[],true,immediate);
            },{receipt,immediate});
            const local=await ready;await parent.waitForFunction(()=>__localClosed===1);
            assert.equal(posts.at(-1).cashDrawer,immediate);await local.close();
        }
        const abandonedUrl=await ticket();await navigate(parent,'/parent');
        await navigate(page,abandonedUrl);await page.waitForFunction(()=>__closed===1);
        assert.equal(posts.at(-1).cashDrawer,false,'Reloaded POS has no surviving drawer intent');
        assert.deepEqual(errors,[]);
        console.log(JSON.stringify({status:'PASS',browser:browser.version(),httpsOrigin:origin,realLoopbackCors:true,
            permission:'local-network-access granted for synthetic test origin',posts:posts.length,
            nativeRasterDifference:comparison.differences/comparison.pixels,qrIncluded:true,drawerResults,
            scenarios:'canonical itemwide/QR, ACK lifecycle, failure/retry, studio, drawer policy matrix, preview/reprint, noopener popup, QR iframe, five concurrent-claim pairs, offline immediate/history, POS reload'}));
    }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
})().catch(error=>{console.error(error);process.exitCode=1;server.close();});
async function waitFor(predicate){const end=Date.now()+10000;while(!predicate()){if(Date.now()>end)throw Error('Timed out waiting for helper request');await new Promise(resolve=>setTimeout(resolve,25));}}
