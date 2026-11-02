'use strict';
// Optional UI-12 evidence observes native controls; it never relays a business command.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const roots = '[data-stock-count-index], .sc-page, .scd-page, [data-stock-transfer-index], .st-page, [data-inventory-adjustment-index], .adj-page, .inventory-issue-index-page, .inventory-issue-detail-page';
const assets = 'link[href*="stock-count.mobile.css"],link[href*="inventory-adjustment-documents.mobile.css"],link[href*="inventory-operations.mobile.css"]';
const guarded = new WeakSet(), results = new Map();
const enabled = () => !!process.env.GAO_UI12_REVIEW_OUTPUT;
const rawAssetMode = () => process.env.GAO_UI12_RAW_ASSETS === '1';
const output = name => { const folder = path.join(process.env.GAO_UI12_REVIEW_OUTPUT || 'TestResults/mobile-ui-12/browser', name); fs.mkdirSync(folder, {recursive:true}); return folder; };
async function protect(context, baseUrl) {
    if (guarded.has(context)) return;
    guarded.add(context);
    const origin = new URL(baseUrl).origin;
    if(rawAssetMode())console.log('[UI-12 transport] Own-origin stylesheet/script requests use Accept-Encoding: identity; actual HTTP responses retained.');
    await context.addInitScript(() => { window.print = () => {}; window.open = () => null; });
    await context.route('**/*', async route => {
        const address = new URL(route.request().url());
        if(address.origin===origin){
            if(rawAssetMode()&&['stylesheet','script'].includes(route.request().resourceType()))return route.continue({headers:{...route.request().headers(),'accept-encoding':'identity'}});
            return route.continue();
        }
        if (['data:', 'blob:', 'about:'].includes(address.protocol)) return route.continue();
        // A disposable fixture may not contact a physical device or another running app.
        return route.abort('blockedbyclient');
    });
}
async function settle(page) {
    let timer,stage='fonts';
    try { await Promise.race([(async()=>{
        await page.evaluate(() => document.fonts.ready);
        stage='two layout frames';
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        stage='current finite animations';
        await page.waitForFunction(()=>!document.getAnimations().some(animation=>{const timing=animation.effect?.getComputedTiming();return animation.playState==='running'&&timing?.iterations!==Infinity&&Number(timing?.endTime)<1500;}),null,{timeout:30000,polling:100});
    })(),new Promise((_,reject)=>timer=setTimeout(()=>reject(new Error('UI-12 presentation settle exceeded 30 seconds during '+stage+': '+page.url())),30000))]);
    }catch(error){
        let diagnosticTimer;const diagnostics=await Promise.race([page.evaluate(()=>({visibility:document.visibilityState,fonts:document.fonts.status,loadingFonts:[...document.fonts].filter(font=>font.status==='loading').map(font=>({family:font.family,status:font.status})),animations:document.getAnimations().map(animation=>{const timing=animation.effect?.getComputedTiming(),node=animation.effect?.target;return{playState:animation.playState,currentTime:String(animation.currentTime),endTime:String(timing?.endTime),iterations:String(timing?.iterations),duration:String(timing?.duration),element:node?{tag:node.tagName,id:node.id,class:typeof node.className==='string'?node.className:''}:null};})})).catch(failure=>({error:String(failure)})),new Promise(resolve=>diagnosticTimer=setTimeout(()=>resolve({error:'Browser did not answer settle diagnostic within 2 seconds'}),2000))]);clearTimeout(diagnosticTimer);console.error('[UI-12 settle diagnostic]',stage,JSON.stringify(diagnostics));throw error;
    } finally { clearTimeout(timer); }
}
function evidence(folder) {
    if (!results.has(folder)) results.set(folder, {transport:{ownOriginStylesheetsAndScripts:rawAssetMode()?'Accept-Encoding: identity':'Standard browser negotiation',actualServerResponses:true,externalOriginsBlocked:true,printingAndNewWindowsStubbed:true},cases:[],viewports:[],desktopProofs:[],dialogs:[]});
    return results.get(folder);
}
function save(folder) { fs.mkdirSync(folder,{recursive:true}); fs.writeFileSync(path.join(folder,'mobile-review.json'),JSON.stringify(evidence(folder),null,2)+'\n'); }
async function review(page, label, folder, options = {}) {
    if (!enabled()) return;
    const selector = options.root || roots, originalViewport = page.viewportSize(), writes = [], calculations = [];
    const record = request => {
        const pathname = new URL(request.url()).pathname;
        
        if (!['GET','HEAD','OPTIONS'].includes(request.method()) && !/^\/admin\/api\/store-activity\/(?:online|presence)$/.test(pathname))
            writes.push({method:request.method(),pathname});
    };
    try {
        console.log('[UI-12 review]',label,'waiting for original owners');
        if(await page.locator('#QuickLookupInput,#QuickTransferLookupInput,#ProductQuickSearch').count()){
            // The original owners reopen Select2 on 200/450/650ms focus timers.
            // Let those finite focus callbacks finish before taking a closed-picker baseline.
            await page.waitForTimeout(1100);await page.keyboard.press('Escape');
        }
        await settle(page);
        page.on('request',record);
        await page.evaluate(selector => {
            const root = document.querySelector(selector); if (!root) throw new Error('UI-12 native view root is missing');
            window.__ui12OriginalFields = [...root.querySelectorAll('input,textarea,select')].map(node => ({node,value:node.value,checked:node.checked,disabled:node.disabled,readOnly:node.readOnly,required:node.required,name:node.name,type:node.type,form:node.form}));
            window.__ui12TransferQuantities = [...root.querySelectorAll('#stockTransferLineTableBody > tr')].filter(row=>row.cells.length>=7).flatMap(row=>[[4,'Hệ số quy đổi'],[6,'SL gốc']].map(([index,label])=>({node:row.cells[index],label,value:row.cells[index].textContent.trim()})));
        },selector);
        for (const [width,height] of [[320,568],[360,780],[390,844],[430,932],[600,900],[767,900],[390,440]]) {
            await page.setViewportSize({width,height}); await settle(page);
            const state = await page.evaluate(selector => {
                const root = document.querySelector(selector), ids=[...document.querySelectorAll('[id]')].map(node=>node.id);
                return {width:innerWidth,height:innerHeight,pageWidth:document.documentElement.scrollWidth,
                    duplicates:ids.filter((id,index)=>ids.indexOf(id)!==index),
                    fields:[...root.querySelectorAll('input:not([type=hidden]):not([type=checkbox]):not([type=radio]),textarea,select')]
                        .filter(node=>!node.classList.contains('select2-hidden-accessible')&&node.getClientRects().length&&getComputedStyle(node).visibility!=='hidden')
                        .map(node=>({id:node.id,name:node.name,font:parseFloat(getComputedStyle(node).fontSize),height:node.getBoundingClientRect().height})),
                    sensitive:[...root.querySelectorAll('[readonly],[disabled],[hidden]')].length,
                    transferPhysicalCells:window.__ui12TransferQuantities.map(({node,label,value})=>({label,actualLabel:node.getAttribute('data-inventory-phone-label'),value,actualValue:node.textContent.trim(),visible:node.isConnected&&node.getBoundingClientRect().width>0&&node.getBoundingClientRect().height>0&&getComputedStyle(node).display!=='none'&&getComputedStyle(node).visibility!=='hidden'})),
                    overflow:document.documentElement.scrollWidth>innerWidth+1?[...root.querySelectorAll('*')].map(node=>({node,bounds:node.getBoundingClientRect()})).filter(({node,bounds})=>bounds.width&&bounds.right>innerWidth+1&&getComputedStyle(node).position!=='fixed').slice(0,12).map(({node,bounds})=>({id:node.id,class:node.className,width:bounds.width,right:bounds.right})):[]};
            },selector);
            assert.ok(state.pageWidth<=width+1,JSON.stringify({label,...state}));
            assert.deepEqual(state.duplicates,[],label+' duplicate IDs');
            assert.ok(state.fields.every(field=>field.font>=16&&field.height>=44),JSON.stringify({label,fields:state.fields}));
            assert.ok(state.transferPhysicalCells.every(cell=>cell.visible&&cell.actualLabel===cell.label&&cell.actualValue===cell.value),JSON.stringify({label,width,transferPhysicalCells:state.transferPhysicalCells}));
            evidence(folder).viewports.push({label,...state});
        }
        console.log('[UI-12 review]',label,'phone matrix complete');
        for (const width of [768,1440]) {
            await page.setViewportSize({width,height:1000}); await settle(page);
            const state = await page.evaluate(({selector,assets}) => {
                const root=document.querySelector(selector), nodes=[...new Set([root,...root.querySelectorAll('*'),...document.querySelectorAll('.gao-admin-page-heading-content,.gao-admin-page-heading-content *,.modal,.modal *,dialog,dialog *')])];
                const links=[...document.querySelectorAll(assets)]; if(!links.length)throw new Error('UI-12 mobile stylesheet missing');
                const behindClosedDetails=node=>{for(let ancestor=node.parentElement;ancestor;ancestor=ancestor.parentElement){if(ancestor.tagName!=='DETAILS'||ancestor.open)continue;const summary=[...ancestor.children].find(child=>child.tagName==='SUMMARY');if(!summary||!summary.contains(node))return true;}return false;};
                const warm=()=>nodes.forEach(node=>{getComputedStyle(node).getPropertyValue('display');node.getBoundingClientRect();});
                const snapshot=node=>{const css=getComputedStyle(node),bounds=node.getBoundingClientRect(),hiddenNativeDetails=behindClosedDetails(node),properties=[];for(let index=0;index<css.length;index++){const property=css.item(index);properties.push(property,css.getPropertyValue(property));}return{css:JSON.stringify(properties),box:hiddenNativeDetails?[0,0,0,0]:[bounds.x,bounds.y,bounds.width,bounds.height],hiddenNativeDetails};};
                warm();const current=nodes.map(snapshot);links.forEach(link=>link.disabled=true);warm();const differences=[];let differenceCount=0;
                for(let index=0;index<nodes.length;index++){
                    const baseline=snapshot(nodes[index]),value=current[index];
                    if(value.css===baseline.css&&value.hiddenNativeDetails===baseline.hiddenNativeDetails&&value.box.every((number,coordinate)=>number===baseline.box[coordinate]))continue;
                    differenceCount++;if(differences.length>=12)continue;
                    const before=JSON.parse(baseline.css),after=JSON.parse(value.css),previous=new Map();for(let property=0;property<before.length;property+=2)previous.set(before[property],before[property+1]);const css=[];for(let property=0;property<after.length;property+=2)if(previous.get(after[property])!==after[property+1])css.push([after[property],after[property+1],previous.get(after[property])]);
                    differences.push({id:nodes[index].id,class:nodes[index].className,currentBox:value.box,baselineBox:baseline.box,css:css.slice(0,12)});
                }
                links.forEach(link=>link.disabled=false);
                return {equal:differenceCount===0,elements:nodes.length,stylesheets:links.length,differenceCount,
                    closedNativeDetailsHiddenGeometry:current.filter(value=>value.hiddenNativeDetails).length,hiddenGeometryPolicy:'Only contents behind native closed DETAILS; summary and all computed CSS retained',
                    differences,
                    phoneAttributes:[...document.querySelectorAll('*')].flatMap(node=>[...node.attributes].filter(a=>/^data-(?:(?:count|adjustment|inventory)-phone|stock-count-phone)/.test(a.name))).length};
            },{selector,assets});
            assert.ok(state.equal,JSON.stringify({label,width,...state}));
            assert.equal(state.phoneAttributes,0,label+' desktop presentation attributes removed');
            evidence(folder).desktopProofs.push({label,width,...state});
        }
        await page.setViewportSize({width:390,height:844}); await settle(page);
        const fieldChanges=await page.evaluate(()=>window.__ui12OriginalFields.map(field=>{const changes=[];for(const property of ['value','checked','disabled','readOnly','required','name','type'])if(field.node[property]!==field[property])changes.push({property,expected:field[property],actual:field.node[property]});if(!field.node.isConnected)changes.push({property:'isConnected',expected:true,actual:false});if(field.node.form!==field.form)changes.push({property:'form',expected:field.form?.id||null,actual:field.node.form?.id||null});return changes.length?{id:field.node.id,class:field.node.className,name:field.name,changes}:null;}).filter(Boolean));
        if(fieldChanges.length){evidence(folder).fieldChanges||=[];evidence(folder).fieldChanges.push({label,fieldChanges});}
        assert.deepEqual(fieldChanges,[],label+' original controls/values/disabled/readonly/validation/native form ownership');
        await page.evaluate(()=>scrollTo({top:0,left:0,behavior:'instant'}));await settle(page);
        await page.screenshot({path:path.join(folder,label+'-phone.png'),fullPage:true,animations:'disabled'});
        await page.screenshot({path:path.join(folder,label+'-viewport.png'),animations:'disabled'});
        const panel=options.root?page.locator(options.root).first():page.locator('[data-workbench-panel]:not([hidden])').first();
        if(await panel.count()){await panel.evaluate(node=>node.scrollIntoView({block:'start',behavior:'instant'}));await settle(page);await page.screenshot({path:path.join(folder,label+'-workspace.png'),animations:'disabled'});await page.evaluate(()=>scrollTo({top:0,left:0,behavior:'instant'}));}
        const lineTable=page.locator(selector).first().locator('table[data-count-phone-table],table[data-adjustment-phone-table],table[data-inventory-phone-table]').filter({has:page.locator('tbody td')}).first();
        if(await lineTable.count()){
            await lineTable.evaluate(node=>node.scrollIntoView({block:'start',behavior:'instant'}));await page.evaluate(()=>scrollBy({top:-80,left:0,behavior:'instant'}));await settle(page);
            await page.screenshot({path:path.join(folder,label+'-lines.png'),animations:'disabled'});
        }
        const grouped=page.locator('#stockCountGroupedBody');
        if(await grouped.count()){
            await grouped.evaluate(node=>node.closest('table').scrollIntoView({block:'start',behavior:'instant'}));await page.evaluate(()=>scrollBy({top:-80,left:0,behavior:'instant'}));await settle(page);
            await page.screenshot({path:path.join(folder,label+'-grouped.png'),animations:'disabled'});
        }
        await page.evaluate(()=>scrollTo({top:0,left:0,behavior:'instant'}));
        assert.deepEqual(writes,[],label+' presentation must not write stock, documents or store data');
        evidence(folder).cases.push({label,baselineAfterOriginalOwnersReady:true,baselineReadiness:'Native inventory controllers and renderers ready before inspection',originalFieldsValuesDisabledAndNativeFormsPreserved:true,presentationWrites:0,originalReadOnlyCalculations:calculations});
        console.log('[UI-12 review]',label,'complete');
    } catch(error) { evidence(folder).failures||=[];evidence(folder).failures.push({label,error:String(error),viewport:page.viewportSize()});
        const overflow=await page.evaluate(()=>{
            const concealed=node=>{for(let ancestor=node.parentElement;ancestor;ancestor=ancestor.parentElement)if(ancestor.tagName==='DETAILS'&&!ancestor.open){const summary=[...ancestor.children].find(child=>child.tagName==='SUMMARY');if(!summary?.contains(node))return true;}return false;};
            const describe=node=>{const css=getComputedStyle(node),box=node.getBoundingClientRect();return{tag:node.tagName,id:node.id,class:typeof node.className==='string'?node.className:'',box:{left:box.left,top:box.top,right:box.right,bottom:box.bottom,width:box.width,height:box.height},overflowX:css.overflowX,overflowY:css.overflowY,minWidth:css.minWidth,maxWidth:css.maxWidth,position:css.position,clientWidth:node.clientWidth,scrollWidth:node.scrollWidth};};
            const result={width:innerWidth,height:innerHeight,pageWidth:document.documentElement.scrollWidth,visible:[],containedHorizontalScroll:[],closedDetails:[]};
            for(const node of document.querySelectorAll('body *')){const box=node.getBoundingClientRect(),css=getComputedStyle(node);if(!box.width||box.right<=innerWidth+1||css.visibility==='hidden'||css.display==='none')continue;const item={...describe(node),ancestors:[node.parentElement,node.parentElement?.parentElement].filter(Boolean).map(describe)};if(concealed(node)){result.closedDetails.push(item);continue;}
                let scrollParent=null;for(let ancestor=node.parentElement;ancestor&&ancestor!==document.body&&ancestor!==document.documentElement;ancestor=ancestor.parentElement){const overflow=getComputedStyle(ancestor).overflowX,bounds=ancestor.getBoundingClientRect();if(['auto','scroll','hidden','clip'].includes(overflow)&&bounds.width&&bounds.left>=-1&&bounds.right<=innerWidth+1){scrollParent=ancestor;break;}}
                if(scrollParent){item.horizontalBoundary=describe(scrollParent);result.containedHorizontalScroll.push(item);}else result.visible.push(item);
            }return result;
        }).catch(failure=>({diagnosticError:String(failure)}));
        fs.writeFileSync(path.join(folder,label+'-failure-overflow.json'),JSON.stringify(overflow,null,2)+'\n');
        await page.screenshot({path:path.join(folder,label+'-failure.png'),fullPage:true,animations:'disabled'}).catch(()=>{});await page.screenshot({path:path.join(folder,label+'-failure-viewport.png'),animations:'disabled'}).catch(()=>{});throw error;
    } finally { page.off('request',record); save(folder);if(originalViewport)await page.setViewportSize(originalViewport);await settle(page); }
}
async function dialog(page,selector,label,folder) {
    if(!enabled())return;
    const originalViewport=page.viewportSize(),writes=[];let originalFocus,originalFields;
    const record=request=>{const pathname=new URL(request.url()).pathname;if(!['GET','HEAD','OPTIONS'].includes(request.method())&&!/^\/admin\/api\/store-activity\/(?:online|presence)$/.test(pathname))writes.push({method:request.method(),pathname});};
    try{
        await page.locator(selector).waitFor({state:'visible'});
        await settle(page);originalFocus=await page.evaluateHandle(()=>document.activeElement);
        originalFields=await page.locator(selector).evaluateHandle(node=>[...node.querySelectorAll('input,select,textarea')].map(node=>({node,value:node.value,checked:node.checked,disabled:node.disabled,readOnly:node.readOnly,required:node.required,name:node.name,type:node.type,form:node.form})));
        page.on('request',record);
        for(const[width,height]of[[320,568],[390,440],[430,932]]){
            await page.setViewportSize({width,height});await settle(page);
            const state=await page.locator(selector).evaluate(node=>{
                const content=node.querySelector('.modal-content')||node,footer=node.querySelector('.modal-footer,.swal2-actions,.offcanvas-footer,footer'),bounds=content.getBoundingClientRect(),foot=footer?.getBoundingClientRect();
                const closeButtons=[...node.querySelectorAll('.btn-close')].filter(button=>button.getClientRects().length).map(button=>{const box=button.getBoundingClientRect();return{left:box.left,top:box.top,right:box.right,bottom:box.bottom,width:box.width,height:box.height};});
                const quickRows=[...node.querySelectorAll('.st-index-quick-line')].map(row=>{const image=row.querySelector('.st-index-line-image'),product=row.querySelector('.st-index-quick-product'),photo=image.getBoundingClientRect(),text=product.getBoundingClientRect();return{imageRight:photo.right,productLeft:text.left,productRight:text.right,productWidth:product.clientWidth,productScrollWidth:product.scrollWidth};});
                return{width:innerWidth,height:innerHeight,pageWidth:document.documentElement.scrollWidth,top:bounds.top,bottom:bounds.bottom,footer:foot?{top:foot.top,bottom:foot.bottom}:null,closeButtons,quickRows};
            });
            assert.ok(state.pageWidth<=width+1&&state.top>=-1&&state.bottom<=height+1,JSON.stringify({label,...state}));
            if(state.footer)assert.ok(state.footer.top>=0&&state.footer.bottom<=height+1,JSON.stringify({label,...state}));
            assert.ok(state.closeButtons.every(button=>button.left>=0&&button.top>=state.top-1&&button.right<=width+1&&button.bottom<=state.bottom+1&&button.width>=44&&button.height>=44),JSON.stringify({label,...state}));
            assert.ok(state.quickRows.every(row=>row.productLeft>=row.imageRight+7&&row.productRight<=width+1&&row.productScrollWidth<=row.productWidth+1),JSON.stringify({label,...state}));
            evidence(folder).dialogs.push({label,selector,...state});
        }
        await page.setViewportSize({width:390,height:844});await settle(page);await page.screenshot({path:path.join(folder,label+'-sheet.png'),animations:'disabled'});
        assert.equal(await originalFields.evaluate(fields=>fields.every(field=>field.node.isConnected&&field.node.form===field.form&&['value','checked','disabled','readOnly','required','name','type'].every(property=>field.node[property]===field[property]))),true,label+' native dialog controls, values and forms preserved');
        assert.deepEqual(writes,[],label+' presentation must not send commands');
    }finally{page.off('request',record);if(originalFields)await originalFields.dispose();save(folder);if(originalViewport)await page.setViewportSize(originalViewport);await settle(page);if(originalFocus){await originalFocus.evaluate(node=>{if(node.isConnected&&!node.disabled&&node.getClientRects().length)node.focus({preventScroll:true});});await originalFocus.dispose();}}
}
module.exports={protect,review,dialog,settle,output,enabled};

