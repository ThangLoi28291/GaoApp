// The user explicitly approved TH/Pepsi catalog data sent to Gemini on 2026-10-12.
// Only assistant history and suggestions are created. No business save endpoint is called.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {call,evidence}=require('./live-ux.cjs');const results=[],details=[];
const save=()=>fs.writeFileSync(path.join(evidence,'live-gemini-all-results.json'),JSON.stringify({realGemini:true,realGaoApp:true,businessSaves:0,physicalMicrophone:false,results,details},null,2));
async function test(name,fn){try{const info=await fn();results.push({name,passed:true,...info});console.log('PASS '+name);}catch(error){results.push({name,passed:false,error:error.message});console.log('FAIL '+name+': '+error.message);}save();}
async function conversation(title){return(await call('/api/v1/conversations',{title:'TEST Gemini · '+title})).conversationId;}
async function chat(id,message){const r=await call('/api/v1/chat/messages',{conversationId:id,message});details.push({conversationId:id,message,reply:r.reply,requestId:r.requestId});console.log(message+': '+r.reply);assert.ok(r.reply?.length);return r;}
function choices(reply){return reply.split('\n').map(line=>/^(\d+)\. (.+)$/.exec(line)).filter(Boolean).map(m=>({number:Number(m[1]),name:m[2]}));}
async function sourceVariants(query){return(await call('/api/v1/assistant/catalog/search',{query,limit:100},true)).variants;}
async function exactQuote(reply,variant,tiers=['retail']){const items=variant.units.filter(u=>u.unit.canSell).map(u=>({productId:variant.product.productId,variantId:variant.product.variantId,unitId:u.unit.unitId,conversionId:u.unit.conversionId}));
    const quotes=(await call('/api/v1/assistant/quotes/lookup',{items,tiers},true)).quotes;
    assert.match(reply,new RegExp(variant.product.name.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')));
    for(const q of quotes)for(const p of q.prices){const label=p.requestedTier==='retail'?'giá lẻ':'giá sỉ';if(p.status==='available')assert.ok(reply.includes(label+' '+Number(p.amount).toLocaleString('vi-VN',{maximumFractionDigits:2})+' đ/'+q.unit.name),JSON.stringify(q));if(p.fallbackReason)assert.ok(reply.includes('chưa có giá sỉ, dùng giá lẻ'));}
}
(async()=>{
    const th=(await sourceVariants('sữa TH có đường 180ml'))[0],pepsi=await sourceVariants('Pepsi');assert.ok(th&&pepsi.length);
    const chai=pepsi.find(v=>v.product.name==='Pepsi chai 390ml');assert.ok(chai);
    let thId,pepsiId,last,moreId,missingId;
    await test('TH short question asks only needed attribute',async()=>{thId=await conversation('TH');last=await chat(thId,'sữa tuoi th');assert.match(last.reply,/loại đường|sản phẩm bên dưới/);});
    await test('TH sugar and capacity automatically produce real quote',async()=>{last=await chat(thId,'có đường 180ml');await exactQuote(last.reply,th);});
    await test('TH loc only and tat ca restore all units',async()=>{last=await chat(thId,'giá lốc');assert.match(last.reply,/Lốc: giá lẻ/);assert.doesNotMatch(last.reply,/đ\/Hộp/);last=await chat(thId,'tất cả');await exactQuote(last.reply,th);});
    await test('TH retail and wholesale agree with fresh source',async()=>{last=await chat(thId,'cả giá lẻ và sỉ');await exactQuote(last.reply,th,['retail','wholesale']);});
    await test('Pepsi shows five actual selectable products',async()=>{pepsiId=await conversation('chọn Pepsi');last=await chat(pepsiId,'Pepsi');const rows=choices(last.reply);assert.equal(rows.length,5);assert.ok(rows.every(row=>pepsi.some(v=>v.product.name===row.name)));assert.ok(last.reply.includes('Xem thêm sản phẩm.'));});
    await test('Choosing Pepsi gives correct live price without retyping',async()=>{const row=choices(last.reply).find(row=>row.name===chai.product.name);assert.ok(row);last=await chat(pepsiId,`chọn ${row.number}. ${row.name}`);await exactQuote(last.reply,chai);});
    await test('General drink question then Pepsi retains usable choices',async()=>{moreId=await conversation('nước ngọt');await chat(moreId,'có nước ngọt không');last=await chat(moreId,'pepsi');assert.equal(choices(last.reply).length,5);});
    await test('Xem them shows remaining actual products',async()=>{last=await chat(moreId,'xem thêm');const rows=choices(last.reply);assert.ok(rows.length>0&&rows.length<=5);assert.ok(rows.every(row=>row.number>5&&pepsi.some(v=>v.product.name===row.name)));});
    await test('Choose from second page gives fresh matching quote',async()=>{const row=choices(last.reply)[0],variant=pepsi.find(v=>v.product.name===row.name);last=await chat(moreId,`chọn ${row.number}. ${row.name}`);await exactQuote(last.reply,variant);});
    await test('Pepsi wrong capacities present choices and never auto substitute',async()=>{missingId=await conversation('sai dung tích');await chat(missingId,'pepsi');for(const message of ['300ml','330ml','chai 330ml','pepsi chai 330ml']){last=await chat(missingId,message);assert.ok(choices(last.reply).length);assert.ok(choices(last.reply).some(row=>/Pepsi/.test(row.name)));assert.doesNotMatch(last.reply,/đ\//);}});
    await test('Confirm actual alternative produces correct quote',async()=>{const row=choices(last.reply).find(row=>row.name===chai.product.name);assert.ok(row);last=await chat(missingId,`chọn ${row.number}. ${row.name}`);await exactQuote(last.reply,chai);});
    for(const typed of ['peps','pep si','pepsy'])await test('Gemini understands approximate name: '+typed,async()=>{const id=await conversation('tên gần đúng '+typed),r=await chat(id,typed);const rows=choices(r.reply);assert.ok(rows.length&&rows.some(row=>/Pepsi/.test(row.name)));assert.doesNotMatch(r.reply,/đ\//);});
    await test('Reloaded history preserves choice message',async()=>{const r=await call('/api/v1/conversations/'+pepsiId+'/messages',undefined,false,{method:'GET'});assert.ok(r.items.some(m=>m.content.includes('Mình chọn sản phẩm bên dưới nhé:')));assert.ok(r.items.some(m=>m.content.includes('đ/Chai')));});
    save();console.log('Real Gemini cases: '+results.filter(r=>r.passed).length+'/'+results.length);process.exitCode=results.some(r=>!r.passed)?1:0;
})().catch(error=>{results.push({name:'startup',passed:false,error:error.message});save();console.error(error.message);process.exitCode=1;});
