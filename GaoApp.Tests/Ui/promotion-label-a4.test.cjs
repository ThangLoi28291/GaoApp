const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const {randomUUID}=require('node:crypto');
const root=path.resolve(__dirname,'../..');
const html=fs.readFileSync(path.join(root,'GaoApp.Web/Areas/Admin/Views/PromotionLabels/Index.cshtml'),'utf8');
const source=fs.readFileSync(path.join(root,'GaoApp.Web/wwwroot/Admin/js/printing/promotion-labels.js'),'utf8');
const tick=()=>new Promise(setImmediate);
function harness(a4Support=true){
 const elements=new Map(),calls=[],storage=new Map(),timers=new Map();let timerId=0;
 class Element{
  constructor(){this.value='';this.dataset={};this.options=[];this.handlers=new Map();this.type='';this.checked=false;}
  addEventListener(name,fn){this.handlers.set(name,fn);} async emit(name,event={}){await this.handlers.get(name)?.({preventDefault(){},...event});}
  replaceChildren(...items){this.options=items;}append(...items){this.options.push(...items);}reportValidity(){return true;}
  querySelector(){return {value:'csrf-test'};}querySelectorAll(){return [];}hasAttribute(){return false;}
 }
 const attr=(tag,name)=>tag.match(new RegExp('(?:^|\\s)'+name+'="([^"]*)"'))?.[1]||'';
 for(const match of html.matchAll(/<[^>]*\sid="([^"]+)"[^>]*>/g)){const el=new Element();el.value=attr(match[0],'value');elements.set(match[1],el);}
 const el=id=>{assert.ok(elements.has(id),'Missing real UI element '+id);return elements.get(id);};
 for(const id of ['plDesign','plPrinterForm']){
  const markup=html.match(new RegExp('<form id="'+id+'"[\\s\\S]*?<\\/form>'))[0],fields=[];
  for(const match of markup.matchAll(/<(input|select)\b[^>]*\bname="([^"]+)"[^>]*>/g)){
   const field=new Element();field.name=match[2];field.type=attr(match[0],'type');field.value=attr(match[0],'value');field.checked=/\bchecked\b/.test(match[0]);
   if(match[1]==='select')field.value=markup.slice(match.index).match(/<option value="([^"]*)"/)?.[1]||'';
   fields.push(field);
  }
  el(id).elements=Object.fromEntries(fields.map(x=>[x.name,x]));el(id).querySelectorAll=()=>fields;
 }
 el('promotionLabels').dataset={storeId:'1',api:'/admin/promotion-labels/',labelApi:'/admin/label-printing/'};
 el('plPaperPreset').value='50x30';
 storage.set('gao-label-client-v1:1',JSON.stringify({templates:[],printers:[],token:'test-client-token-at-least-32-characters'}));
 const products=Array.from({length:5},(_,i)=>({conversionId:i+1,name:'Sản phẩm '+(i+1),barcode:'LABEL-'+(i+1),unit:'Hộp',sku:'SKU'+i,retailPrice:39000,salePrice:32100,minimumQuantity:1,startAtUtc:'2026-10-13T00:00:00Z',endAtUtc:'2026-11-02T00:00:00Z',fingerprint:'fixture-'+i}));
 async function fetch(url,opts={}){
  const body=opts.body?JSON.parse(opts.body):null;calls.push({url,body});const json=data=>new Response(JSON.stringify(data),{status:200});
  if(url.endsWith('/health'))return json({ok:true,protocol:'gao-label-client-v1',features:a4Support?['promotion-a4-v1']:[]});
  if(url.endsWith('/printers'))return json(url.includes(':17881')?{ok:true,protocol:'gao-label-client-v1',printers:['Brother DCP-B7640DW']}:[]);
  if(url.endsWith('/templates'))return json([]);
  if(url.endsWith('/campaigns'))return json([{id:1,name:'Chương trình mẫu'}]);
  if(url.includes('/products?'))return json({items:products,total:5,page:1});
  if(url.endsWith('/preview')||url.endsWith('/preview-a4'))return new Response(new Blob(['preview']),{status:200});
  if(url.endsWith('/prepare'))return json(body.lines.map(x=>({product:products.find(p=>p.conversionId===x.conversionId),quantity:x.quantity})));
  if(url.endsWith('/print'))return json({ok:true,protocol:'gao-label-client-v1',submitted:true,requestId:body.requestId,quantity:body.payload.items.reduce((s,x)=>s+x.quantity,0)});
  throw new Error('Unexpected request '+url);
 }
 const context={console,URL,URLSearchParams,AbortController,TypeError,Blob,Response,Date,crypto:{randomUUID},fetch,
  Option:class{constructor(text,value){this.text=text;this.value=String(value);}},
  localStorage:{getItem:k=>storage.get(k),setItem:(k,v)=>storage.set(k,v),removeItem:k=>storage.delete(k)},
  setTimeout:(fn,ms)=>{timers.set(++timerId,{fn,ms});return timerId;},clearTimeout:id=>timers.delete(id),
  location:{hostname:'localhost',search:''},navigator:{permissions:{query:async()=>({state:'granted'})}},
  document:{getElementById:el},window:{addEventListener(){}}};
 vm.runInNewContext(source,context,{filename:'promotion-labels.js'});
 return {el,calls,storage,async settle(){for(let i=0;i<8;i++)await tick();},async flush(){for(const [id,t]of[...timers])if(t.ms===350){timers.delete(id);await t.fn();}for(let i=0;i<8;i++)await tick();}};
}

for(const quantity of [4,5,11])test('A4 UI previews and submits exactly '+quantity+' labels in row order',async()=>{
 const h=harness();await h.settle();
 h.el('plPaperPreset').value='a4';await h.el('plPaperPreset').emit('change');await h.flush();
 assert.equal(h.el('plDesign').elements.columns.value,2);assert.equal(h.el('plDesign').elements.widthMm.value,90);
 h.el('plPrinter').value='Brother DCP-B7640DW';await h.el('plPrinter').emit('change');await h.flush();
 await h.el('plAddPage').emit('click');await h.settle();
 if(quantity===4)await h.el('plLines').emit('click',{target:{closest:()=>({dataset:{remove:'5'}})}});
 if(quantity===11)await h.el('plLines').emit('input',{target:{matches:()=>true,value:'7',closest:()=>({dataset:{line:'1'}})}});
 await h.flush();
 const previews=h.calls.filter(c=>c.url.endsWith('/preview-a4'));
 assert.ok(previews.length);const preview=previews.at(-1).body;
 assert.equal(preview.payload.items.reduce((s,x)=>s+x.quantity,0),quantity);
 assert.deepEqual(preview.payload.items.map(x=>x.product.name),Array.from({length:quantity===4?4:5},(_,i)=>'Sản phẩm '+(i+1)));
 if(quantity===11){assert.equal(h.el('plSheetNext').disabled,false);await h.el('plSheetNext').emit('click');await h.settle();assert.equal(h.calls.filter(c=>c.url.endsWith('/preview-a4')).at(-1).body.page,2);}
 await h.el('plSavePrinter').emit('click');await h.el('plPrint').emit('click');await h.settle();
 const prints=h.calls.filter(c=>c.url.endsWith('/print'));assert.equal(prints.length,1);
 const payload=prints[0].body.payload;assert.equal(payload.design.paperMode,'a4');assert.equal(payload.printer.dpi,300);assert.equal(payload.printer.printableWidthMm,210);
 assert.equal(payload.printer.windowsPrinterName,'Brother DCP-B7640DW');assert.equal(payload.items.reduce((s,x)=>s+x.quantity,0),quantity);
 assert.equal(h.storage.get('gao-label-client-v1:1:pending'),undefined);
 const saved=JSON.parse(h.storage.get('gao-label-client-v1:1'));assert.equal(saved.templates[0].design.paperMode,'a4');
});

test('an older client can preview A4 through Web but cannot submit a sheet',async()=>{
 const h=harness(false);await h.settle();h.el('plPaperPreset').value='a4';await h.el('plPaperPreset').emit('change');await h.flush();
 h.el('plPrinter').value='Brother DCP-B7640DW';await h.el('plPrinter').emit('change');await h.flush();
 await h.el('plAddPage').emit('click');await h.settle();await h.el('plSavePrinter').emit('click');
 assert.equal(h.el('plPrint').disabled,true);assert.equal(h.el('plA4ClientNotice').hidden,false);
 await h.el('plPrint').emit('click');assert.equal(h.calls.filter(c=>c.url.endsWith('/print')).length,0);
 assert.match(h.el('plMessage').textContent,/Cập nhật/);
 assert.ok(h.calls.some(c=>c.url==='/admin/promotion-labels/preview-a4'));
});

test('switching back to 80x50 removes A4 mode and restores thermal width',async()=>{
 const h=harness();await h.settle();h.el('plPaperPreset').value='a4';await h.el('plPaperPreset').emit('change');await h.flush();
 h.el('plPaperPreset').value='80x50';await h.el('plPaperPreset').emit('change');await h.flush();
 assert.equal(h.el('plDesign').elements.paperMode.value,'');assert.equal(h.el('plDesign').elements.columns.value,1);
 assert.equal(h.el('plPrinterForm').elements.printableWidthMm.value,108);assert.equal(h.el('plDesign').elements.widthMm.readOnly,false);
 assert.equal(h.el('plSheetPaging').hidden,true);assert.equal(h.el('plTestPrint').textContent,'In thử một hàng');
});
