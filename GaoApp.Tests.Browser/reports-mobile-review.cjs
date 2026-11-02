'use strict';
// UI-18 reuses the UI-17 bank-mobile-review.cjs mandatory matrix and exact desktop/print proof.
// Native report owners keep all data, fields, charts and commands; this helper only observes presentation.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const roots = '[data-management-report],[data-sales-executive-report],[data-sales-detail-report],[data-profit-report]';
const assets = 'link[href*="reports-workspace.mobile.css"],link[href*="report-detail.mobile.css"],link[href*="reports-operations.mobile.css"]';
const guarded = new WeakSet(), results = new Map();
const enabled = () => true;
const rawAssetMode = () => process.env.GAO_UI18_RAW_ASSETS === '1';
const output = name => { const folder = path.join(process.env.GAO_UI18_REVIEW_OUTPUT || 'TestResults/mobile-ui-18-20261027/browser', name); fs.mkdirSync(folder, {recursive:true}); return folder; };
async function protect(context, baseUrl) {
    if (guarded.has(context)) return;
    guarded.add(context);
    const origin = new URL(baseUrl).origin;
    if(rawAssetMode())console.log('[UI-18 transport] Own-origin stylesheet/script requests use Accept-Encoding: identity; actual HTTP responses retained.');
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
        stage='actual Apex owner chart geometry and paths';
        await page.evaluate(()=>{window.__ui18ChartSettle={started:performance.now(),signature:'',stable:0};});
        await page.waitForFunction(()=>{
            const state=window.__ui18ChartSettle;
            const signature=[...document.querySelectorAll('.apexcharts-svg')].map(svg=>({width:svg.getAttribute('width'),height:svg.getAttribute('height'),paths:[...svg.querySelectorAll('path')].map(path=>path.getAttribute('d'))}));
            const next=JSON.stringify(signature);
            if(next===state.signature)state.stable++;else{state.signature=next;state.stable=0;}
            return performance.now()-state.started>=800&&state.stable>=3;
        },null,{timeout:30000,polling:100});
    })(),new Promise((_,reject)=>timer=setTimeout(()=>reject(new Error('UI-18 presentation settle exceeded 30 seconds during '+stage+': '+page.url())),30000))]);
    }catch(error){
        let diagnosticTimer;const diagnostics=await Promise.race([page.evaluate(()=>({visibility:document.visibilityState,fonts:document.fonts.status,loadingFonts:[...document.fonts].filter(font=>font.status==='loading').map(font=>({family:font.family,status:font.status})),animations:document.getAnimations().map(animation=>{const timing=animation.effect?.getComputedTiming(),node=animation.effect?.target;return{playState:animation.playState,currentTime:String(animation.currentTime),endTime:String(timing?.endTime),iterations:String(timing?.iterations),duration:String(timing?.duration),element:node?{tag:node.tagName,id:node.id,class:typeof node.className==='string'?node.className:''}:null};})})).catch(failure=>({error:String(failure)})),new Promise(resolve=>diagnosticTimer=setTimeout(()=>resolve({error:'Browser did not answer settle diagnostic within 2 seconds'}),2000))]);clearTimeout(diagnosticTimer);console.error('[UI-18 settle diagnostic]',stage,JSON.stringify(diagnostics));throw error;
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
        console.log('[UI-18 review]',label,'waiting for original owners');
        await settle(page);
        page.on('request',record);
        await page.evaluate(selector => {
            const root = document.querySelector(selector); if (!root) throw new Error('UI-18 native view root is missing');
            window.__ui18OriginalFields = [...root.querySelectorAll('input,textarea,select')].map(node => ({node,value:node.value,checked:node.checked,disabled:node.disabled,readOnly:node.readOnly,required:node.required,name:node.name,type:node.type,form:node.form}));
            window.__ui18OriginalNativeState = [...root.querySelectorAll('[hidden],.d-none,details')].filter(node=>!node.closest('.apexcharts-canvas')).map(node=>({node,hidden:node.hidden,dNone:node.classList.contains('d-none'),open:node.tagName==='DETAILS'?node.open:null}));
            window.__ui18OriginalValues = [...root.querySelectorAll('[data-report-kpis],[data-kpis],[data-profit-kpis],[data-report-quality],[data-quality],[data-profit-quality],[data-table],[data-profit-detail-table],#salesDetailTableBody,#salesReportContent .sales-report-kpi-grid')].map(node=>({node,text:node.textContent}));
        },selector);
        for (const [width,height] of [[320,568],[360,780],[390,844],[430,932],[600,900],[767,900],[390,440]]) {
            await page.setViewportSize({width,height}); await settle(page);
            const state = await page.evaluate(selector => {
                const root = document.querySelector(selector), ids=[...document.querySelectorAll('[id]')].map(node=>node.id);
                const nav=root.matches('[data-management-report]:not([data-operations-report])')?root.querySelector('.mr-nav'):null;
                const selected=nav?.querySelector('a[aria-current="page"]'),normal=nav?.querySelector('a:not([aria-current="page"])');
                const appearance=node=>{const css=getComputedStyle(node);return{text:node.textContent.trim(),color:css.color,background:css.backgroundColor,border:css.borderColor};};
                let primaryKpiContrast=null;
                if(root.matches('[data-sales-executive-report]')){
                    const card=root.querySelector('.sales-report-kpi-card.is-primary');
                    if(!card||!card.getClientRects().length)throw new Error('Native Sales fallback primary KPI must be visible for contrast inspection');
                    const parseColor=value=>{
                        const match=value.match(/^rgba?\(([^)]+)\)$/i);
                        if(!match)throw new Error('Unsupported computed color in contrast proof: '+value);
                        const parts=match[1].replace(/\s*\/\s*/g,',').split(/[\s,]+/).filter(Boolean);
                        const rgb=parts.slice(0,3).map(part=>part.endsWith('%')?parseFloat(part)*2.55:parseFloat(part));
                        const alpha=parts[3]===undefined?1:parts[3].endsWith('%')?parseFloat(parts[3])/100:parseFloat(parts[3]);
                        if(rgb.length!==3||![...rgb,alpha].every(Number.isFinite))throw new Error('Invalid computed contrast color: '+value);
                        return[...rgb,alpha];
                    };
                    const over=(foreground,background)=>{const alpha=foreground[3]+background[3]*(1-foreground[3]);return[...foreground.slice(0,3).map((channel,index)=>(channel*foreground[3]+background[index]*background[3]*(1-foreground[3]))/(alpha||1)),alpha];};
                    const luminance=color=>color.slice(0,3).map(channel=>{const value=channel/255;return value<=.04045?value/12.92:Math.pow((value+.055)/1.055,2.4);}).reduce((sum,value,index)=>sum+value*[.2126,.7152,.0722][index],0);
                    const ratio=(a,b)=>{const first=luminance(a),second=luminance(b);return(Math.max(first,second)+.05)/(Math.min(first,second)+.05);};
                    const ancestry=[];for(let node=card;node;node=node.parentElement)ancestry.push(node);
                    let base=[255,255,255,1];ancestry.reverse().forEach(node=>{base=over(parseColor(getComputedStyle(node).backgroundColor),base);});
                    const cardStyle=getComputedStyle(card),gradient=cardStyle.backgroundImage;
                    const stopColors=gradient.match(/rgba?\([^)]+\)/g)||[];
                    if(!/^linear-gradient\(/.test(gradient)||stopColors.length<2)throw new Error('Native primary KPI gradient stops unavailable: '+gradient);
                    const stops=stopColors.map(color=>over(parseColor(color),base));
                    const checks=[['label','.sales-report-kpi-topline > span'],['value','.sales-report-kpi-value'],['delta','.sales-report-kpi-delta']].map(([role,selector])=>{
                        const node=card.querySelector(selector);if(!node||!node.getClientRects().length)throw new Error('Native primary KPI '+role+' must be visible');
                        const css=getComputedStyle(node),foreground=parseColor(css.color),layers=[];let opacity=1;
                        for(let current=node;current&&current!==card;current=current.parentElement){layers.push(current);opacity*=Number(getComputedStyle(current).opacity);}
                        foreground[3]*=opacity;
                        const contrastsByStop=stops.map((stop,index)=>{let background=stop;[...layers].reverse().forEach(layer=>{background=over(parseColor(getComputedStyle(layer).backgroundColor),background);});const displayed=over(foreground,background);return{gradientStop:stopColors[index],backgroundRgb:background.slice(0,3),foregroundRgb:displayed.slice(0,3),ratio:ratio(displayed,background)};});
                        return{role,text:node.textContent.trim(),color:css.color,opacity,minimumContrast:Math.min(...contrastsByStop.map(stop=>stop.ratio)),contrastsByStop};
                    });
                    primaryKpiContrast={backgroundImage:gradient,gradientStopColors:stopColors,minimumRequired:4.5,formula:'WCAG sRGB relative luminance and (lighter + .05)/(darker + .05), including computed alpha compositing',checks};
                }
                return {width:innerWidth,height:innerHeight,pageWidth:document.documentElement.scrollWidth,
                    primaryKpiContrast,
                    activeNavigation:innerWidth===390&&nav?{activeCount:nav.querySelectorAll('a[aria-current="page"]').length,selected:selected?appearance(selected):null,normal:normal?appearance(normal):null}:null,
                    duplicates:ids.filter((id,index)=>ids.indexOf(id)!==index),
                    fields:[...root.querySelectorAll('input:not([type=hidden]):not([type=checkbox]):not([type=radio]),textarea,select')]
                        .filter(node=>{if(node.classList.contains('select2-hidden-accessible')||!node.getClientRects().length||getComputedStyle(node).visibility==='hidden')return false;for(let ancestor=node.parentElement;ancestor;ancestor=ancestor.parentElement){if(ancestor.tagName==='DETAILS'&&!ancestor.open){const summary=[...ancestor.children].find(child=>child.tagName==='SUMMARY');if(!summary?.contains(node))return false;}}return true;})
                        .map(node=>({id:node.id,name:node.name,font:parseFloat(getComputedStyle(node).fontSize),height:node.getBoundingClientRect().height})),
                    sensitive:[...root.querySelectorAll('[readonly],[disabled],[hidden]')].length,
                    overflow:document.documentElement.scrollWidth>innerWidth+1?[...root.querySelectorAll('*')].map(node=>({node,bounds:node.getBoundingClientRect()})).filter(({node,bounds})=>bounds.width&&bounds.right>innerWidth+1&&getComputedStyle(node).position!=='fixed').slice(0,12).map(({node,bounds})=>({id:node.id,class:node.className,width:bounds.width,right:bounds.right})):[]};
            },selector);
            assert.ok(state.pageWidth<=width+1,JSON.stringify({label,...state}));
            assert.deepEqual(state.duplicates,[],label+' duplicate IDs');
            assert.ok(state.fields.every(field=>field.font>=16&&field.height>=44),JSON.stringify({label,fields:state.fields}));
            if(state.activeNavigation){
                assert.equal(state.activeNavigation.activeCount,1,label+' native management current report');
                assert.ok(state.activeNavigation.selected&&state.activeNavigation.normal,label+' original selected and other report links exist');
                assert.notEqual(state.activeNavigation.selected.color,state.activeNavigation.normal.color,label+' selected report text is visually distinct at 390px');
                assert.notEqual(state.activeNavigation.selected.background,state.activeNavigation.normal.background,label+' selected report background is visually distinct at 390px');
            }
            if(state.primaryKpiContrast){
                evidence(folder).primaryKpiContrast||=[];
                evidence(folder).primaryKpiContrast.push({label,width:state.width,height:state.height,...state.primaryKpiContrast});
                assert.ok(state.primaryKpiContrast.checks.every(check=>check.minimumContrast>=4.5),JSON.stringify({label,width:state.width,height:state.height,primaryKpiContrast:state.primaryKpiContrast}));
            }
            evidence(folder).viewports.push({label,...state});
        }
        console.log('[UI-18 review]',label,'phone matrix complete');
        for (const width of [768,1440]) {
            await page.setViewportSize({width,height:1000}); await settle(page);
            const state = await page.evaluate(({selector,assets}) => {
                const root=document.querySelector(selector), nodes=[...new Set([root,...root.querySelectorAll('*'),...document.querySelectorAll('.gao-admin-page-heading-content,.gao-admin-page-heading-content *,.sales-report-page-header,.sales-report-page-header *,.modal,.modal *,dialog,dialog *')])];
                const links=[...document.querySelectorAll(assets)]; if(!links.length)throw new Error('UI-18 mobile stylesheet missing');
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
                    phoneAttributes:[...document.querySelectorAll('*')].flatMap(node=>[...node.attributes].filter(a=>/^data-(?:reports-workspace-phone|report-detail-phone|reports-operations-phone|rmo-)/.test(a.name))).length};
            },{selector,assets});
            assert.ok(state.equal,JSON.stringify({label,width,...state}));
            assert.equal(state.phoneAttributes,0,label+' desktop presentation attributes removed');
            evidence(folder).desktopProofs.push({label,width,...state});
        }
        await page.setViewportSize({width:390,height:844}); await settle(page);
        await printProof(page, selector, label, folder);
        const fieldChanges=await page.evaluate(()=>window.__ui18OriginalFields.map(field=>{const changes=[];for(const property of ['value','checked','disabled','readOnly','required','name','type'])if(field.node[property]!==field[property])changes.push({property,expected:field[property],actual:field.node[property]});if(!field.node.isConnected)changes.push({property:'isConnected',expected:true,actual:false});if(field.node.form!==field.form)changes.push({property:'form',expected:field.form?.id||null,actual:field.node.form?.id||null});return changes.length?{id:field.node.id,class:field.node.className,name:field.name,changes}:null;}).filter(Boolean));
        if(fieldChanges.length){evidence(folder).fieldChanges||=[];evidence(folder).fieldChanges.push({label,fieldChanges});}
        assert.deepEqual(fieldChanges,[],label+' original controls/values/disabled/readonly/validation/native form ownership');
        const nativeStateChanges=await page.evaluate(()=>window.__ui18OriginalNativeState.flatMap(state=>!state.node.isConnected||state.node.hidden!==state.hidden||state.node.classList.contains('d-none')!==state.dNone||(state.open!==null&&state.node.open!==state.open)?[{tag:state.node.tagName,id:state.node.id}]:[]));
        assert.deepEqual(nativeStateChanges,[],label+' native hidden/d-none and disclosure states');
        const valueChanges=await page.evaluate(()=>window.__ui18OriginalValues.flatMap(value=>!value.node.isConnected||value.node.textContent!==value.text?[{id:value.node.id,expected:value.text,actual:value.node.textContent}]:[]));
        assert.deepEqual(valueChanges,[],label+' native KPI, quality, cost/null and detail text unchanged by phone presentation');
        await page.evaluate(()=>scrollTo({top:0,left:0,behavior:'instant'}));await settle(page);
        await page.screenshot({path:path.join(folder,label+'-phone.png'),fullPage:true,animations:'disabled'});
        await page.screenshot({path:path.join(folder,label+'-viewport.png'),animations:'disabled'});
        const panel=options.root?page.locator(options.root).first():page.locator('[data-workbench-panel]:not([hidden])').first();
        if(await panel.count()){await panel.evaluate(node=>node.scrollIntoView({block:'start',behavior:'instant'}));await settle(page);await page.screenshot({path:path.join(folder,label+'-workspace.png'),animations:'disabled'});await page.evaluate(()=>scrollTo({top:0,left:0,behavior:'instant'}));}
        const lineTable=page.locator(selector).first().locator('table:visible').filter({has:page.locator('tbody td')}).first();
        if(await lineTable.count()){
            await lineTable.evaluate(node=>node.scrollIntoView({block:'start',behavior:'instant'}));await page.evaluate(()=>scrollBy({top:-80,left:0,behavior:'instant'}));await settle(page);
            await page.screenshot({path:path.join(folder,label+'-lines.png'),animations:'disabled'});
        }
        await page.evaluate(()=>scrollTo({top:0,left:0,behavior:'instant'}));
        assert.deepEqual(writes,[],label+' presentation must not send any report, expense, treasury, menu or store command');
        evidence(folder).cases.push({label,baselineAfterOriginalOwnersReady:true,baselineReadiness:'Native report aria-busy=false and real Apex chart geometry stable before inspection',originalFieldsValuesDisabledAndNativeFormsPreserved:true,presentationWrites:0,originalReadOnlyCalculations:calculations});
        console.log('[UI-18 review]',label,'complete');
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
                const content=node.querySelector('.modal-content')||node,footer=node.querySelector('.modal-footer,.swal2-actions,.offcanvas-footer,.mr-dialog-actions,footer'),bounds=content.getBoundingClientRect(),foot=footer?.getBoundingClientRect();
                const closeButtons=[...node.querySelectorAll('.btn-close,.mr-dialog-heading [data-close],.mr-dialog-heading [data-expense-close],.mr-dialog-heading [data-action-close]')].filter(button=>button.getClientRects().length).map(button=>{const box=button.getBoundingClientRect();return{left:box.left,top:box.top,right:box.right,bottom:box.bottom,width:box.width,height:box.height};});
                const fields=[...node.querySelectorAll('input:not([type=hidden]):not([type=checkbox]):not([type=radio]),select,textarea')].filter(field=>!field.classList.contains('select2-hidden-accessible')&&field.getClientRects().length&&getComputedStyle(field).visibility!=='hidden').map(field=>({id:field.id,font:parseFloat(getComputedStyle(field).fontSize),height:field.getBoundingClientRect().height}));const footerButtons=[...(footer?.querySelectorAll('button,.btn')||[])].filter(button=>button.getClientRects().length).map(button=>({height:button.getBoundingClientRect().height,text:button.textContent.trim()}));
                return{width:innerWidth,height:innerHeight,pageWidth:document.documentElement.scrollWidth,top:bounds.top,bottom:bounds.bottom,footer:foot?{top:foot.top,bottom:foot.bottom}:null,closeButtons,fields,footerButtons};
            });
            assert.ok(state.pageWidth<=width+1&&state.top>=-1&&state.bottom<=height+1,JSON.stringify({label,...state}));
            if(state.footer)assert.ok(state.footer.top>=0&&state.footer.bottom<=height+1,JSON.stringify({label,...state}));
            assert.ok(state.closeButtons.every(button=>button.left>=0&&button.top>=state.top-1&&button.right<=width+1&&button.bottom<=state.bottom+1&&button.width>=44&&button.height>=44),JSON.stringify({label,...state}));
            assert.ok(state.fields.every(field=>field.font>=16&&field.height>=44),JSON.stringify({label,...state}));assert.ok(state.footerButtons.every(button=>button.height>=44),JSON.stringify({label,...state}));
            evidence(folder).dialogs.push({label,selector,...state});
        }
        await page.setViewportSize({width:390,height:844});await settle(page);await page.screenshot({path:path.join(folder,label+'-sheet.png'),animations:'disabled'});
        assert.equal(await originalFields.evaluate(fields=>fields.every(field=>field.node.isConnected&&field.node.form===field.form&&['value','checked','disabled','readOnly','required','name','type'].every(property=>field.node[property]===field[property]))),true,label+' native dialog controls, values and forms preserved');
        assert.deepEqual(writes,[],label+' presentation must not send commands');
    }finally{page.off('request',record);if(originalFields)await originalFields.dispose();save(folder);if(originalViewport)await page.setViewportSize(originalViewport);await settle(page);if(originalFocus){await originalFocus.evaluate(node=>{if(node.isConnected&&!node.disabled&&node.getClientRects().length)node.focus({preventScroll:true});});await originalFocus.dispose();}}
}
async function printProof(page,selector,label,folder) {
    try {
        await page.evaluate(()=>dispatchEvent(new Event('beforeprint')));
        await page.emulateMedia({media:'print'}); await settle(page);
        const state=await page.evaluate(({selector,assets})=>{
            const root=document.querySelector(selector),nodes=[root,...root.querySelectorAll('*'),...document.querySelectorAll('.gao-admin-page-heading-content,.gao-admin-page-heading-content *,.sales-report-page-header,.sales-report-page-header *')],links=[...document.querySelectorAll(assets)];
            const snapshot=node=>{const style=getComputedStyle(node),box=node.getBoundingClientRect(),properties=[];for(let i=0;i<style.length;i++){const p=style.item(i);properties.push(p,style.getPropertyValue(p));}return JSON.stringify({properties,box:[box.x,box.y,box.width,box.height]});};
            const current=nodes.map(snapshot); links.forEach(link=>link.disabled=true);
            const differences=nodes.flatMap((node,index)=>snapshot(node)===current[index]?[]:[{id:node.id,class:node.className}]);
            links.forEach(link=>link.disabled=false);
            return{equal:!differences.length,elements:nodes.length,differences:differences.slice(0,8),phoneNavigationDuringPrint:document.querySelectorAll('.rwm-dialog-scroll,.rmo-dialog-scroll').length};
        },{selector,assets});
        assert.ok(state.equal,JSON.stringify({label,print:state}));assert.equal(state.phoneNavigationDuringPrint,0,label+' phone-only navigation and disclosure restored before print');
        evidence(folder).printProofs||=[];evidence(folder).printProofs.push({label,...state});
    } finally {
        await page.emulateMedia({media:'screen'});await page.evaluate(()=>dispatchEvent(new Event('afterprint')));await settle(page);
    }
}
async function reportMenu(page,label,folder) {
    await settle(page);
    const originalLinks=await page.locator('#layout-menu a[href]').evaluateAll(nodes=>nodes.map(node=>({href:node.getAttribute('href'),text:node.textContent.trim()})));
    const opener=page.locator('[data-report-menu]');
    assert.equal(await opener.count(),1,label+' original native report menu control');
    await opener.click();
    await page.waitForFunction(()=>document.documentElement.classList.contains('layout-menu-expanded'));
    const close=page.locator('[data-nav-close]');
    assert.equal(await close.count(),1,label+' original native menu close control');
    assert.equal(await close.isVisible(),true,label+' native menu close is visible');
    await page.screenshot({path:path.join(folder,label+'-menu-open.png'),animations:'disabled'});
    await close.click();
    await page.waitForFunction(()=>!document.documentElement.classList.contains('layout-menu-expanded'));
    const currentLinks=await page.locator('#layout-menu a[href]').evaluateAll(nodes=>nodes.map(node=>({href:node.getAttribute('href'),text:node.textContent.trim()})));
    assert.deepEqual(currentLinks,originalLinks,label+' native configured menu links unchanged');
    evidence(folder).navigation||=[];
    evidence(folder).navigation.push({label,width:page.viewportSize().width,nativeReportMenuOpened:true,nativeCloseButtonClosed:true,configuredLinksUnchanged:true,links:currentLinks.length,extraReviewState:false});
    save(folder);
    await settle(page);
}
module.exports={protect,review,dialog,reportMenu,settle,output,enabled};








