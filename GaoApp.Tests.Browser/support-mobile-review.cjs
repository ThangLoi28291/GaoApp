'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const assets=['customer-inbox','customer-service-admin','assistant-admin','assistant-widget','media-workspace'].map(stem=>'link[href*="'+stem+'.mobile.css"]').join(',');
const phoneSizes=[[320,568],[360,780],[390,844],[430,932],[600,900],[767,900],[390,440]];
const evidence={cases:[],phoneChecks:[],desktopProofs:[],printProofs:[],dialogChecks:[],dialogScrollChecks:[],assistantChecks:[],inboxLauncherChecks:[]};
async function settle(page){
 await page.evaluate(()=>document.fonts.ready);
 await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
 await page.waitForFunction(()=>!document.getAnimations().some(a=>{const t=a.effect?.getComputedTiming();return a.playState==='running'&&t?.iterations!==Infinity&&Number(t?.endTime)<2000;}),null,{timeout:10000,polling:50});
}
function save(folder){fs.mkdirSync(folder,{recursive:true});fs.writeFileSync(path.join(folder,'mobile-review.json'),JSON.stringify(evidence,null,2)+'\n');}
async function presentation(page,selector,label,kind,width){
 await settle(page);
 const proof=await page.evaluate(({selector,assets})=>{
  const roots=[...document.querySelectorAll(selector)],nodes=[...new Set(roots.flatMap(root=>[root,...root.querySelectorAll('*')]))];
  if(!roots.length)throw Error('No original UI-20 root');const links=[...document.querySelectorAll(assets)];if(!links.length)throw Error('No UI-20 stylesheets');
  const closed=node=>{for(let n=node.parentElement;n;n=n.parentElement)if(n.tagName==='DETAILS'&&!n.open&&!n.querySelector(':scope>summary')?.contains(node))return true;return false;};
  const snap=node=>{const c=getComputedStyle(node),r=node.getBoundingClientRect(),p=[];for(let i=0;i<c.length;i++)p.push(c.item(i),c.getPropertyValue(c.item(i)));return{css:JSON.stringify(p),box:closed(node)?[0,0,0,0]:[r.x,r.y,r.width,r.height]};};
  const before=nodes.map(snap),differences=[];links.forEach(link=>link.disabled=true);
  for(let i=0;i<nodes.length;i++){const after=snap(nodes[i]);if(before[i].css!==after.css||before[i].box.some((v,k)=>v!==after.box[k]))differences.push({tag:nodes[i].tagName,id:nodes[i].id,class:nodes[i].className,before:before[i].box,after:after.box});}
  links.forEach(link=>link.disabled=false);return{equal:!differences.length,elements:nodes.length,stylesheets:links.length,differenceCount:differences.length,differences:differences.slice(0,6)};
 },{selector,assets});
 assert.equal(proof.equal,true,JSON.stringify({label,kind,width,...proof}));evidence[kind==='print'?'printProofs':'desktopProofs'].push({label,width,...proof});
}
async function review(page,selector,label,folder,options={}){
 const {dialog=false,assistant=false,inbox=false,dynamic=false,nativeDialog=null}=options,initial=page.viewportSize(),reads=[],writes=[];
 const record=request=>{const url=new URL(request.url());if(['GET','HEAD'].includes(request.method())){if(['fetch','xhr'].includes(request.resourceType()))reads.push(url.pathname+url.search);}else if(!/^\/admin\/api\/store-activity\/(online|presence)$/.test(url.pathname))writes.push({method:request.method(),path:url.pathname});};
 console.log('[UI-20 review]',label);
 try{
  await settle(page);
  await page.evaluate(({selector,dynamic})=>{
   const roots=[...document.querySelectorAll(selector)],unique=s=>[...new Set(roots.flatMap(root=>[...root.querySelectorAll(s)]))];
   window.__ui20Fields=unique('input,textarea,select').map(node=>({node,value:node.value,checked:node.checked,disabled:node.disabled,readOnly:node.readOnly,required:node.required,name:node.name,type:node.type,form:node.form,files:node.files?[...node.files].map(f=>[f.name,f.size,f.type,f.lastModified]):null}));
   window.__ui20Native=dynamic?[]:unique('[hidden],.d-none,details').map(node=>({node,hidden:node.hidden,dNone:node.classList.contains('d-none'),open:node.tagName==='DETAILS'?node.open:null}));
   window.__ui20Text=dynamic?[]:unique('td').map(node=>({node,text:node.textContent}));
  },{selector,dynamic});page.on('request',record);
  for(const[width,height]of phoneSizes){
   await page.setViewportSize({width,height});await settle(page);
   const state=await page.evaluate(({selector,dialog,assistant,inbox,nativeDialog})=>{
    const roots=[...document.querySelectorAll(selector)],hidden=node=>{for(let n=node;n;n=n.parentElement){const c=getComputedStyle(n);if(c.display==='none'||c.visibility==='hidden'||n.hidden)return true;if(n.tagName==='DETAILS'&&!n.open&&!n.querySelector(':scope>summary')?.contains(node))return true;}return false;};
    const fields=[...new Set(roots.flatMap(root=>[...root.querySelectorAll('input:not([type=hidden]):not([type=checkbox]):not([type=radio]):not([type=range]),textarea,select')]))].filter(node=>!hidden(node)&&node.getClientRects().length&&!node.matches('.filepond--browser,.select2-hidden-accessible')&&parseFloat(getComputedStyle(node).opacity)!==0&&node.getBoundingClientRect().width>3).map(node=>({id:node.id,name:node.name,font:parseFloat(getComputedStyle(node).fontSize),height:node.getBoundingClientRect().height}));
    const ids=[...document.querySelectorAll('[id]')].map(n=>n.id);
    const candidates=roots.flatMap(n=>[n,...n.querySelectorAll('dialog[open],.modal.show,[role=dialog]:not([hidden]),#gaPanel:not([hidden])')]);
    const layer=dialog||assistant?(nativeDialog?document.querySelector(nativeDialog):candidates.find(n=>n.matches('dialog[open],.modal.show')&&!hidden(n))||(assistant?candidates.find(n=>n.id==='gaPanel'&&!hidden(n)):candidates.find(n=>n.matches('[role=dialog]:not([hidden])')&&!hidden(n)))):null;
    const boxNode=layer?.matches('.modal')?layer.querySelector('.modal-content')||layer:layer;
    const rect=boxNode?.getBoundingClientRect();
    const close=layer?[...layer.querySelectorAll('.btn-close,#gaClose,.ga-correction-close,#cqProposalCancel,[data-close-preview],[data-dialog-close]')].filter(n=>!hidden(n)&&n.getClientRects().length).map(n=>{const r=n.getBoundingClientRect();return{id:n.id,class:n.className,left:r.left,right:r.right,top:r.top,bottom:r.bottom,width:r.width,height:r.height};}):[];
    const footer=assistant?layer?.querySelector('.ga-footer'):layer?.querySelector('.modal-footer');
    const foot=footer&&!hidden(footer)?footer.getBoundingClientRect():null;
    const launcher=document.getElementById('gaOpen'),widget=document.getElementById('gaPanel'),ci=document.getElementById('customerInbox');
    let inboxLauncher={applicable:false};
    const visible=n=>n&&!hidden(n)&&!n.closest('[inert]')&&n.getClientRects().length;
    if(inbox&&ci?.dataset.ciPhonePane==='conversation'&&visible(launcher)&&widget?.hidden){
     const launch=launcher.getBoundingClientRect();
     const fields=['ciSend','ciReply','ciAttach','ciRemoveAsset'].map(id=>document.getElementById(id)).filter(visible).map(n=>{const r=n.getBoundingClientRect();return{id:n.id,left:r.left,right:r.right,top:r.top,bottom:r.bottom,overlap:Math.min(r.right,launch.right)>Math.max(r.left,launch.left)&&Math.min(r.bottom,launch.bottom)>Math.max(r.top,launch.top)};});
     inboxLauncher={applicable:true,launcher:{left:launch.left,right:launch.right,top:launch.top,bottom:launch.bottom},fields};
    }
    return{width:innerWidth,height:innerHeight,pageWidth:document.documentElement.scrollWidth,duplicates:ids.filter((id,i)=>ids.indexOf(id)!==i),fields,inboxLauncher,layer:rect?{id:layer.id,tag:layer.tagName,top:rect.top,bottom:rect.bottom,left:rect.left,right:rect.right,close,footer:foot?{top:foot.top,bottom:foot.bottom,left:foot.left,right:foot.right}:null}:null};
   },{selector,dialog,assistant,inbox,nativeDialog});
   assert.ok(state.pageWidth<=width+1,JSON.stringify({label,...state}));assert.deepEqual(state.duplicates,[],label+' original IDs remain unique');
   assert.ok(state.fields.every(f=>f.font>=16&&f.height>=44),JSON.stringify({label,fields:state.fields}));
   if(dialog||assistant)assert.ok(state.layer,label+' requested native layer is visible');
   if(state.layer){
    assert.ok(state.layer.top>=-1&&state.layer.bottom<=height+1&&state.layer.left>=-1&&state.layer.right<=width+1,JSON.stringify({label,layer:state.layer}));
    assert.ok(state.layer.close.length>0,label+' native layer retains a close control');
    assert.ok(state.layer.close.every(b=>b.width>=44&&b.height>=44&&b.top>=0&&b.bottom<=height+1&&b.left>=0&&b.right<=width+1),JSON.stringify({label,layer:state.layer}));
    if(state.layer.footer)assert.ok(state.layer.footer.top>=state.layer.top-1&&state.layer.footer.bottom<=state.layer.bottom+1,JSON.stringify({label,layer:state.layer}));
    evidence[assistant?'assistantChecks':'dialogChecks'].push({label,...state});
   }
   if(inbox){assert.ok(!state.inboxLauncher.fields?.some(field=>field.overlap),'Assistant launcher overlaps native inbox controls: '+JSON.stringify({label,...state}));evidence.inboxLauncherChecks.push({label,...state.inboxLauncher,width,height});}
   evidence.phoneChecks.push({label,...state});
  }
  for(const width of[768,1440]){await page.setViewportSize({width,height:1000});await presentation(page,selector,label,'desktop',width);}
  await page.setViewportSize({width:390,height:844});await settle(page);
  try{await page.evaluate(()=>dispatchEvent(new Event('beforeprint')));await page.emulateMedia({media:'print'});await presentation(page,selector,label,'print',390);}finally{await page.emulateMedia({media:'screen'});await page.evaluate(()=>dispatchEvent(new Event('afterprint')));await settle(page);}
  if(dialog||assistant){
   const scroll=await page.evaluate(({selector,assistant,nativeDialog})=>{
    const hidden=n=>{for(let p=n;p;p=p.parentElement){const css=getComputedStyle(p);if(p.hidden||css.display==='none'||css.visibility==='hidden')return true;if(p.tagName==='DETAILS'&&!p.open&&!p.querySelector(':scope>summary')?.contains(n))return true;}return false;};
    const roots=[...document.querySelectorAll(selector)],nodes=roots.flatMap(root=>[root,...root.querySelectorAll('dialog[open],.modal.show,#gaPanel:not([hidden])')]);
    const layer=nativeDialog?document.querySelector(nativeDialog):nodes.find(n=>n.matches('dialog[open],.modal.show')&&!hidden(n))||(assistant?nodes.find(n=>n.id==='gaPanel'&&!hidden(n)):null);
    if(!layer)throw Error('Missing native scroll layer');
    const body=layer.matches('.modal')?layer.querySelector('.modal-body'):assistant?[...layer.querySelectorAll('.ga-history,.ga-screen-view,.ga-messages')].find(n=>!hidden(n)):layer;
    if(!body)throw Error('Missing native scroll owner');
    const visible=n=>!hidden(n)&&n.getClientRects().length&&!n.disabled;
    const target=[...body.querySelectorAll('input:not([type=hidden]),select,textarea')].filter(visible).at(-1)||[...body.querySelectorAll('button,a')].filter(visible).at(-1)||body.lastElementChild||body;
    const positions=[];for(let n=target;n;n=n.parentElement)positions.push({node:n,top:n.scrollTop,left:n.scrollLeft});
    window.__ui20Scroll={positions};target.scrollIntoView({block:'nearest',inline:'nearest',behavior:'instant'});
    const b=body.getBoundingClientRect(),f=target.getBoundingClientRect();
    const visiblePixels=Math.max(0,Math.min(b.bottom,f.bottom)-Math.max(b.top,f.top));
    const result={kind:assistant?'assistant':'dialog',layer:layer.id||layer.className,lastField:target.id||target.name||target.dataset.field||target.tagName,reachable:visiblePixels>=Math.min(f.height,b.height,44)-1,body:{top:b.top,bottom:b.bottom,height:b.height},field:{top:f.top,bottom:f.bottom,height:f.height},scrollHeight:body.scrollHeight,clientHeight:body.clientHeight,overflowY:getComputedStyle(body).overflowY};
    body.scrollTop=body.scrollHeight;return result;
   },{selector,assistant,nativeDialog});
   try{assert.ok(scroll.reachable,label+' last native control remains reachable');if(scroll.scrollHeight>scroll.clientHeight+1)assert.ok(['auto','scroll'].includes(scroll.overflowY),label+' native body owns scrolling');await settle(page);await page.screenshot({path:path.join(folder,label+'-editor-bottom.png'),animations:'disabled'});evidence.dialogScrollChecks.push({label,...scroll});}
   finally{await page.evaluate(()=>{for(const p of window.__ui20Scroll?.positions||[])if(p.node.isConnected){p.node.scrollTop=p.top;p.node.scrollLeft=p.left;}delete window.__ui20Scroll;});await settle(page);}
  }
  const changed=await page.evaluate(()=>({fields:window.__ui20Fields.filter(f=>!f.node.isConnected||f.node.form!==f.form||['value','checked','disabled','readOnly','required','name','type'].some(p=>f.node[p]!==f[p])||JSON.stringify(f.node.files?[...f.node.files].map(x=>[x.name,x.size,x.type,x.lastModified]):null)!==JSON.stringify(f.files)).map(f=>({id:f.node.id,name:f.name})),native:window.__ui20Native.filter(s=>!s.node.isConnected||s.node.hidden!==s.hidden||s.node.classList.contains('d-none')!==s.dNone||(s.open!==null&&s.node.open!==s.open)).map(s=>({id:s.node.id,tag:s.node.tagName})),text:window.__ui20Text.filter(s=>!s.node.isConnected||s.node.textContent!==s.text).map(s=>({id:s.node.id,before:s.text,after:s.node.textContent}))}));
  assert.deepEqual(changed,{fields:[],native:[],text:[]},label+' native nodes, form owners, fields, files and validation preserved');
  assert.deepEqual(writes,[],label+' viewport/print presentation emits no commands');
  await page.evaluate(()=>scrollTo({top:0,left:0,behavior:'instant'}));await settle(page);
  await page.screenshot({path:path.join(folder,label+'-viewport.png'),animations:'disabled'});await page.screenshot({path:path.join(folder,label+'-full.png'),fullPage:true,animations:'disabled'});
  evidence.cases.push({label,selector,dialog,assistant,inbox,dynamic,originalFieldsFilesAndFormOwnersPreserved:true,nativePollReadsObserved:reads,presentationWrites:0});save(folder);
 }catch(error){evidence.failures||=[];evidence.failures.push({label,error:String(error),viewport:page.viewportSize()});await page.screenshot({path:path.join(folder,label+'-failure.png'),fullPage:true,animations:'disabled'}).catch(()=>{});save(folder);throw error;}
 finally{page.off('request',record);if(initial)await page.setViewportSize(initial);await settle(page);}
}
module.exports={review,settle,evidence,save};
