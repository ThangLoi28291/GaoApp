'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const assets='link[href*="promotions.mobile.css"],link[href*="display-promotion.mobile.css"],link[href*="promotion-labels.mobile.css"]';
const phoneSizes=[[320,568],[360,780],[390,844],[430,932],[600,900],[767,900],[390,440]];
const evidence={cases:[],phoneChecks:[],desktopProofs:[],printProofs:[],dialogChecks:[],dialogScrollChecks:[]};
async function settle(page){
  await page.evaluate(()=>document.fonts.ready);
  await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
  await page.waitForFunction(()=>!document.getAnimations().some(a=>{const t=a.effect?.getComputedTiming();return a.playState==='running'&&t?.iterations!==Infinity&&Number(t?.endTime)<2000;}),null,{timeout:10000,polling:50});
}
function save(folder){fs.mkdirSync(folder,{recursive:true});fs.writeFileSync(path.join(folder,'mobile-review.json'),JSON.stringify(evidence,null,2)+'\n');}
async function presentationProof(page,selector,label,kind,width){
  await settle(page);
  const result=await page.evaluate(({selector,assets})=>{
    const roots=[...document.querySelectorAll(selector)],nodes=[...new Set([...roots.flatMap(root=>[root,...root.querySelectorAll('*')]),...document.querySelectorAll('.gao-admin-page-heading-content,.gao-admin-page-heading-content *')])];
    if(!roots.length)throw new Error('Missing native UI-19 roots');
    const links=[...document.querySelectorAll(assets)];if(!links.length)throw new Error('Mobile CSS is missing');
    const closed=node=>{for(let current=node.parentElement;current;current=current.parentElement)if(current.tagName==='DETAILS'&&!current.open&&!current.querySelector(':scope > summary')?.contains(node))return true;return false;};
    const snapshot=node=>{const css=getComputedStyle(node),rect=node.getBoundingClientRect(),properties=[];for(let i=0;i<css.length;i++){const p=css.item(i);properties.push(p,css.getPropertyValue(p));}return{css:JSON.stringify(properties),box:closed(node)?[0,0,0,0]:[rect.x,rect.y,rect.width,rect.height]};};
    const before=nodes.map(snapshot);links.forEach(link=>link.disabled=true);const differences=[];let total=0;
    for(let i=0;i<nodes.length;i++){const after=snapshot(nodes[i]);if(before[i].css===after.css&&before[i].box.every((v,index)=>v===after.box[index]))continue;total++;if(differences.length<6)differences.push({id:nodes[i].id,tag:nodes[i].tagName,class:nodes[i].className,before:before[i].box,after:after.box});}
    links.forEach(link=>link.disabled=false);
    return{equal:total===0,elements:nodes.length,stylesheets:links.length,differenceCount:total,differences};
  },{selector,assets});
  assert.ok(result.equal,JSON.stringify({label,kind,width,...result}));
  evidence[kind==='print'?'printProofs':'desktopProofs'].push({label,width,...result});
}
async function review(page,selector,label,folder,{dialog=false}={}){
  console.log('[UI-19 review]',label); const initialViewport=page.viewportSize(),reads=[],writes=[];
  const record=request=>{const url=new URL(request.url());if(['GET','HEAD'].includes(request.method())){if(request.resourceType()==='fetch'||request.resourceType()==='xhr')reads.push(url.pathname+url.search);}else if(!/^\/admin\/api\/store-activity\/(online|presence)$/.test(url.pathname))writes.push({method:request.method(),path:url.pathname});};
  try{
    await settle(page);
    await page.evaluate(selector=>{
      const roots=[...document.querySelectorAll(selector)];
      window.__ui19Fields=roots.flatMap(root=>[...root.querySelectorAll('input,textarea,select')]).map(node=>({node,value:node.value,checked:node.checked,disabled:node.disabled,readOnly:node.readOnly,required:node.required,name:node.name,type:node.type,form:node.form}));
      window.__ui19Native=roots.flatMap(root=>[...root.querySelectorAll('[hidden],.d-none,details')]).map(node=>({node,hidden:node.hidden,dNone:node.classList.contains('d-none'),open:node.tagName==='DETAILS'?node.open:null}));
      window.__ui19Values=roots.flatMap(root=>[...root.querySelectorAll('td,.promo-stat-value,.promo-status-badge,.dp-count,.dp-state-text,#plResultSummary,#plTotal,#plPaperSize')]).map(node=>({node,text:node.textContent}));
    },selector);
    page.on('request',record);
    for(const[width,height]of phoneSizes){
      await page.setViewportSize({width,height});await settle(page);
      const state=await page.evaluate(({selector,dialog})=>{
        const roots=[...document.querySelectorAll(selector)],ids=[...document.querySelectorAll('[id]')].map(n=>n.id);
        const hidden=node=>{for(let n=node;n;n=n.parentElement){const css=getComputedStyle(n);if(css.display==='none'||css.visibility==='hidden'||n.hidden)return true;if(n.tagName==='DETAILS'&&!n.open&&!n.querySelector(':scope > summary')?.contains(node))return true;}return false;};
        const fields=roots.flatMap(root=>[...root.querySelectorAll('input:not([type=hidden]):not([type=checkbox]):not([type=radio]),textarea,select')]).filter(node=>node.getClientRects().length&&!hidden(node)&&!node.classList.contains('select2-hidden-accessible')).map(node=>({id:node.id,name:node.name,font:parseFloat(getComputedStyle(node).fontSize),height:node.getBoundingClientRect().height}));
        const sheet=dialog?roots.find(node=>node.matches('.modal')):null,content=sheet?.querySelector('.modal-content'),footer=sheet?.querySelector('.modal-footer'),bounds=content?.getBoundingClientRect(),foot=footer?.getBoundingClientRect();
        return{width:innerWidth,height:innerHeight,pageWidth:document.documentElement.scrollWidth,duplicates:ids.filter((id,index)=>ids.indexOf(id)!==index),fields,
          sheet:bounds?{top:bounds.top,bottom:bounds.bottom,footerTop:foot?.top,footerBottom:foot?.bottom,close:[...sheet.querySelectorAll('.btn-close')].filter(n=>n.getClientRects().length).map(n=>{const r=n.getBoundingClientRect();return{top:r.top,bottom:r.bottom,left:r.left,right:r.right,width:r.width,height:r.height};})}:null,
          overflow:[...document.querySelectorAll('body *')].filter(node=>!hidden(node)).map(node=>({node,rect:node.getBoundingClientRect()})).filter(({rect})=>rect.width&&rect.right>innerWidth+1).slice(0,8).map(({node,rect})=>({id:node.id,class:node.className,right:rect.right,width:rect.width}))};
      },{selector,dialog});
      assert.ok(state.pageWidth<=width+1,JSON.stringify({label,...state}));assert.deepEqual(state.duplicates,[],label+' unique native IDs');
      assert.ok(state.fields.every(field=>field.font>=16&&field.height>=44),JSON.stringify({label,fields:state.fields}));
      if(state.sheet){assert.ok(state.sheet.top>=-1&&state.sheet.bottom<=height+1,JSON.stringify({label,sheet:state.sheet}));if(state.sheet.footerBottom!==undefined)assert.ok(state.sheet.footerTop>=0&&state.sheet.footerBottom<=height+1,JSON.stringify({label,sheet:state.sheet}));assert.ok(state.sheet.close.every(button=>button.width>=44&&button.height>=44&&button.left>=0&&button.right<=width+1&&button.top>=0&&button.bottom<=height+1),JSON.stringify({label,sheet:state.sheet}));evidence.dialogChecks.push({label,...state});}
      evidence.phoneChecks.push({label,...state});
    }
    for(const width of[768,1440]){await page.setViewportSize({width,height:1000});await presentationProof(page,selector,label,'desktop',width);}
    await page.setViewportSize({width:390,height:844});await settle(page);
    try{await page.evaluate(()=>dispatchEvent(new Event('beforeprint')));await page.emulateMedia({media:'print'});await presentationProof(page,selector,label,'print',390);}finally{await page.emulateMedia({media:'screen'});await page.evaluate(()=>dispatchEvent(new Event('afterprint')));await settle(page);}
    if(dialog){
      const scroll=await page.evaluate(selector=>{
        const sheet=[...document.querySelectorAll(selector)].find(node=>node.matches('.modal')),body=sheet?.querySelector('.modal-body');if(!body)throw Error('Missing native modal scroll body');
        const visible=node=>{for(let parent=node;parent&&parent!==body.parentElement;parent=parent.parentElement){const css=getComputedStyle(parent);if(parent.hidden||css.display==='none'||css.visibility==='hidden')return false;}return node.getClientRects().length>0;};
        const controls=[...body.querySelectorAll('input:not([type=hidden]),textarea,select')].filter(node=>!node.disabled&&visible(node)),target=controls.at(-1);if(!target)throw Error('Missing last native form control');
        window.__ui19DialogScroll={body,top:body.scrollTop};target.scrollIntoView({block:'nearest',inline:'nearest',behavior:'instant'});
        const bounds=body.getBoundingClientRect(),field=target.getBoundingClientRect(),result={lastField:target.id||target.name||target.dataset.field,body:{top:bounds.top,bottom:bounds.bottom,height:bounds.height},field:{top:field.top,bottom:field.bottom,height:field.height},scrollHeight:body.scrollHeight,clientHeight:body.clientHeight,overflowY:getComputedStyle(body).overflowY,reachable:field.bottom>bounds.top&&field.top<bounds.bottom};
        body.scrollTop=body.scrollHeight;return result;
      },selector);
      try{assert.ok(scroll.reachable,label+' last form control is reachable in native scroll body');assert.ok(['auto','scroll'].includes(scroll.overflowY),label+' native modal owns scrolling');await settle(page);await page.screenshot({path:path.join(folder,label+'-editor-bottom.png'),animations:'disabled'});evidence.dialogScrollChecks.push({label,...scroll});}
      finally{await page.evaluate(()=>{const original=window.__ui19DialogScroll;if(original?.body.isConnected)original.body.scrollTop=original.top;delete window.__ui19DialogScroll;});await settle(page);}
    }
    const changed=await page.evaluate(()=>({fields:window.__ui19Fields.filter(field=>!field.node.isConnected||field.node.form!==field.form||['value','checked','disabled','readOnly','required','name','type'].some(p=>field.node[p]!==field[p])).map(f=>({id:f.node.id,name:f.name})),native:window.__ui19Native.filter(state=>!state.node.isConnected||state.node.hidden!==state.hidden||state.node.classList.contains('d-none')!==state.dNone||(state.open!==null&&state.node.open!==state.open)).map(s=>({id:s.node.id,tag:s.node.tagName})),values:window.__ui19Values.filter(value=>!value.node.isConnected||value.node.textContent!==value.text).map(v=>({id:v.node.id,before:v.text,after:v.node.textContent}))}));
    assert.deepEqual(changed,{fields:[],native:[],values:[]},label+' native identity, fields, values, validation, form owner and text preserved');
    assert.deepEqual(reads,[],label+' viewport and print do not load extra business data');assert.deepEqual(writes,[],label+' presentation sends no commands or preview requests');
    await page.evaluate(()=>scrollTo({top:0,left:0,behavior:'instant'}));await settle(page);
    await page.screenshot({path:path.join(folder,label+'-viewport.png'),animations:'disabled'});await page.screenshot({path:path.join(folder,label+'-full.png'),fullPage:true,animations:'disabled'});
    evidence.cases.push({label,selector,dialog,originalFieldsAndRowsPreserved:true,extraDataReads:0,presentationWrites:0});save(folder);
  }catch(error){evidence.failures||=[];evidence.failures.push({label,error:String(error),viewport:page.viewportSize()});await page.screenshot({path:path.join(folder,label+'-failure.png'),fullPage:true,animations:'disabled'}).catch(()=>{});save(folder);throw error;}
  finally{page.off('request',record);if(initialViewport)await page.setViewportSize(initialViewport);await settle(page);}
}
module.exports={review,settle,evidence,save};
