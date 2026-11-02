'use strict';
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const selector='[data-purchase-request-index], [data-purchase-order-index], #purchaseRequestPage, #purchaseRequestDetailsPage, #purchaseRequestPreparePage, #purchaseOrderPage, #purchaseOrderDetailsPage, #purchaseOrderCommercialPage, #receivingWorkbench';
const result={cases:[],viewports:[],desktopProofs:[],dialogs:[]};
async function settle(page){await page.evaluate(()=>document.fonts.ready);await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));}
async function review(page,label,output){
 const writes=[];const record=r=>{const p=new URL(r.url()).pathname;if(r.method()==='POST'&&!/^\/admin\/api\/store-activity\/(?:online|presence)$/.test(p)&&!/^\/admin\/purchase-receiving\/\d+\/heartbeat$/.test(p))writes.push(p);};page.on('request',record);
 try{
  await settle(page);await page.evaluate(s=>{const root=document.querySelector(s);window.__purchaseReviewInputs=[...root.querySelectorAll('input,textarea,select')].map(node=>({node,value:node.value,checked:node.checked,disabled:node.disabled,form:node.form}));},selector);
  for(const [width,height]of [[320,568],[360,780],[390,844],[430,932],[600,900],[767,900],[390,440]]){
   await page.setViewportSize({width,height});await settle(page);
   const r=await page.evaluate(s=>{const root=document.querySelector(s),ids=[...document.querySelectorAll('[id]')].map(n=>n.id);return{width:innerWidth,height:innerHeight,scroll:document.documentElement.scrollWidth,duplicates:ids.filter((id,i)=>ids.indexOf(id)!==i),fields:[...root.querySelectorAll('input:not([type=hidden]):not([type=checkbox]):not([type=radio]),textarea,select')].filter(n=>n.getClientRects().length).map(n=>({id:n.id,font:parseFloat(getComputedStyle(n).fontSize),height:n.getBoundingClientRect().height})),tables:root.querySelectorAll('[data-purchase-phone-table]').length};},selector);
   assert.ok(r.scroll<=width+1,JSON.stringify({label,...r}));assert.deepEqual(r.duplicates,[]);assert.ok(r.fields.every(f=>f.font>=16&&f.height>=44),JSON.stringify({label,fields:r.fields}));result.viewports.push({label,...r});
  }
  for(const width of [768,1440]){
   await page.setViewportSize({width,height:1000});await settle(page);
   const r=await page.evaluate(s=>{const root=document.querySelector(s),nodes=[root,...root.querySelectorAll('*'),...document.querySelectorAll('.gao-admin-page-heading-content,.gao-admin-page-heading-content *,.modal,.modal *')],link=document.querySelector('link[href*="purchasing.mobile.css"]');const snap=()=>nodes.map(n=>{const css=getComputedStyle(n),b=n.getBoundingClientRect();return{css:[...css].map(p=>[p,css.getPropertyValue(p)]),box:[b.x,b.y,b.width,b.height]};});const current=snap();link.disabled=true;const original=snap();link.disabled=false;return{equal:JSON.stringify(current)===JSON.stringify(original),elements:nodes.length,phoneAttributes:document.querySelectorAll('[data-purchase-phone],[data-purchase-phone-root],[data-purchase-phone-table],[data-purchase-label],[data-purchase-product]').length};},selector);
   assert.ok(r.equal,JSON.stringify({label,width,...r}));assert.equal(r.phoneAttributes,0);result.desktopProofs.push({label,width,...r});
  }
  await page.setViewportSize({width:390,height:844});await settle(page);
  assert.ok(await page.evaluate(()=>__purchaseReviewInputs.every(x=>x.node.isConnected&&x.node.value===x.value&&x.node.checked===x.checked&&x.node.disabled===x.disabled&&x.node.form===x.form)),'Original fields/values/form ownership survive breakpoints');
  await page.evaluate(()=>scrollTo({top:0,left:0,behavior:'instant'}));await settle(page);await page.screenshot({path:path.join(output,label+'-phone.png'),fullPage:true});assert.deepEqual(writes,[],'Presentation must not issue commands');result.cases.push({label,originalNodesValuesDisabledAndNativeFormsPreserved:true,presentationWrites:0});
 }finally{page.off('request',record);fs.writeFileSync(path.join(output,'mobile-review.json'),JSON.stringify(result,null,2)+'\n');}
}
async function dialog(page,id,output){
 await page.locator(id+'.show').waitFor();
 for(const [width,height]of [[320,568],[390,440],[430,932]]){await page.setViewportSize({width,height});await settle(page);const r=await page.locator(id).evaluate(n=>{const content=n.querySelector('.modal-content')||n,body=n.querySelector('.modal-body,.offcanvas-body'),footer=n.querySelector('.modal-footer,.offcanvas-footer'),b=content.getBoundingClientRect(),f=footer?.getBoundingClientRect();return{width:innerWidth,height:innerHeight,scroll:document.documentElement.scrollWidth,top:b.top,bottom:b.bottom,bodyScrollable:body?getComputedStyle(body).overflowY:null,footer:f?{top:f.top,bottom:f.bottom}:null};});assert.ok(r.scroll<=width+1&&r.top>=-1&&r.bottom<=height+1,JSON.stringify({id,...r}));if(r.footer)assert.ok(r.footer.top>=0&&r.footer.bottom<=height+1,JSON.stringify({id,...r}));result.dialogs.push({id,...r});}
 await page.setViewportSize({width:390,height:844});await settle(page);await page.screenshot({path:path.join(output,id.slice(1)+'-sheet.png')});fs.writeFileSync(path.join(output,'mobile-review.json'),JSON.stringify(result,null,2)+'\n');
}
module.exports={review,dialog,settle};
