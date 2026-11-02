// User-authorized real Gemini, catalog/price reads and name suggestions. No business save.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {call,evidence}=require('./live-ux.cjs');const details=[];const out=path.join(evidence,'natural-ai-results');fs.mkdirSync(out,{recursive:true});
async function variants(query){return(await call('/api/v1/assistant/catalog/search',{query,limit:100},true)).variants;}
async function chat(id,message){const r=await call('/api/v1/chat/messages',{conversationId:id,message});details.push({operation:'chat',message,requestId:r.requestId,reply:r.reply});console.log(message+' → '+r.reply);return r.reply;}
async function exact(reply,variant){assert.ok(variant);assert.ok(reply.includes(variant.product.name));const items=variant.units.filter(u=>u.unit.canSell).map(u=>({productId:variant.product.productId,variantId:variant.product.variantId,unitId:u.unit.unitId,conversionId:u.unit.conversionId}));const quotes=(await call('/api/v1/assistant/quotes/lookup',{items,tiers:['retail']},true)).quotes;for(const q of quotes)for(const p of q.prices)if(p.status==='available')assert.ok(reply.includes(Number(p.amount).toLocaleString('vi-VN')+' đ/'+q.unit.name));}
async function normalize(text,validate){const r=await call('/api/v1/products/normalize-name',{originalText:text,inputSource:'speech',generation:1});details.push({operation:'normalizeName',message:text,requestId:r.requestId,status:r.status,proposedName:r.proposedName});console.log(text+' → '+r.proposedName+' ('+r.status+')');validate(r);}
(async()=>{
 const th=(await variants('sữa tươi TH không đường 180ml'))[0],vm=(await variants('sữa tươi Vinamilk không đường 180ml'))[0],less=(await variants('sữa tươi TH ít đường 110ml'))[0];
 assert.match(th.product.name,/TH nguyên chất hộp 180ml/);assert.equal(th.attributes.sugar,'unsweetened');
 const id=(await call('/api/v1/conversations',{title:'TEST hội thoại tự nhiên và TH không đường'})).conversationId;
 const first=await chat(id,'sữa tươi Vinamilk');assert.match(first,/loại đường|sản phẩm bên dưới|dung tích/);
 await exact(await chat(id,'180 ml không đường'),vm);
 for(const message of ['có th thì sao','Trà Sữa tươi TH','tôi hỏi sữa tươi TH không đường']){
  const reply=await chat(id,message);await exact(reply,th);assert.doesNotMatch(reply,/viết tắt|chưa tìm thấy|Vinamilk/);
 }
 await exact(await chat(id,'còn Vinamilk'),vm);
 await exact(await chat(id,'đổi sang TH ít đường 110ml'),less);
 const pack=await chat(id,'giá lốc');assert.match(pack,/đ\/Lốc/);assert.doesNotMatch(pack,/đ\/Hộp/);
 await exact(await chat(id,'tất cả'),less);
 await normalize('bánh ngot kinh do 500g loại hộp',r=>{assert.equal(r.status,'suggested');assert.match(r.proposedName,/Bánh ngọt Kinh Đô/);assert.match(r.proposedName,/500g/);assert.match(r.proposedName,/hộp/);});
 await normalize('sữa tươi th không đường 180',r=>{assert.equal(r.status,'needs_clarification');assert.doesNotMatch(r.proposedName,/180\s*ml/i);assert.ok(r.clarifications.some(c=>c.field==='capacity'));});
 const text='4 lốc TH không đường 180ml',received=await call('/api/v1/receiving/resolve',{originalText:text,text,inputSource:'speech',generation:1});
 assert.equal(received.status,'matched');assert.equal(received.match.product.variantId,th.product.variantId);assert.equal(received.match.quantity,'4');assert.equal(received.match.unit.factor,'4');assert.equal(received.match.equivalentBaseQuantity,'16');details.push({operation:'receiving',message:text,requestId:received.requestId,status:received.status});console.log('PASS receiving TH không đường → actual TH nguyên chất, 4 lốc = 16 hộp');
 const report={realGemini:true,realGaoApp:true,businessWrites:0,physicalMicrophone:false,details};fs.writeFileSync(path.join(out,'live-natural-language-results.json'),JSON.stringify(report,null,2));console.log('PASS '+details.length+' real natural-language turns.');
})().catch(e=>{fs.writeFileSync(path.join(out,'live-natural-language-failure.json'),JSON.stringify({details,error:e.message},null,2));console.error(e.message);process.exitCode=1;});
