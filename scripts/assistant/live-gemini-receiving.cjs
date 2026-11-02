// Authorized real Gemini + live GaoApp catalog. No receipt/product write endpoint is called.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {call,evidence}=require('./live-ux.cjs');const details=[];
async function check(text,validate){const requestId=crypto.randomUUID();const reply=await call('/api/v1/receiving/resolve',{originalText:text,text,inputSource:'speech',generation:1},false,{requestId});validate(reply);details.push({text,requestId,status:reply.status,candidates:reply.candidates.map(c=>c.product.name),match:reply.match||null});console.log('PASS '+text+' → '+reply.status);}
(async()=>{
 await check('Sữa tươi TH loại có đường',r=>{assert.equal(r.status,'ambiguous');assert.ok(r.candidates.length>1);assert.ok(r.candidates.every(c=>c.attributes.sugar==='sweetened'));assert.ok(r.candidates.some(c=>c.product.variantId==='156272'));assert.equal(r.match,undefined);});
 await check('Sữa tươi TH loại có đường 180ml',r=>{assert.equal(r.status,'needs_clarification');assert.equal(r.resolvedProduct.variantId,'156272');assert.equal(r.match,undefined);});
 await check('4 lốc TH có đường 180ml',r=>{assert.equal(r.status,'matched');assert.equal(r.match.product.variantId,'156272');assert.equal(r.match.unit.name,'Lốc');assert.equal(r.match.unit.factor,'4');assert.equal(r.match.quantity,'4');assert.equal(r.match.equivalentBaseQuantity,'16');});
 await check('Nhận bốn lốc sữa tươi TH loại có đường một trăm tám mươi mi li lít',r=>{assert.equal(r.status,'matched');assert.equal(r.match.quantity,'4');assert.equal(r.match.unit.factor,'4');assert.equal(r.match.equivalentBaseQuantity,'16');});
 await check('pepsy',r=>{assert.equal(r.status,'ambiguous');assert.ok(r.candidates.length>=5);assert.ok(r.candidates.every(c=>/pepsi/i.test(c.product.name)));});
 await check('pep si',r=>{assert.equal(r.status,'ambiguous');assert.ok(r.candidates.length>=5);assert.ok(r.candidates.every(c=>/pepsi/i.test(c.product.name)));});
 await check('4 chai Pepsi 330ml',r=>{assert.equal(r.status,'not_found');assert.equal(r.match,undefined);});
 await check('0.75 lốc TH có đường 180ml',r=>{assert.equal(r.status,'matched');assert.equal(r.match.quantity,'0.75');assert.equal(r.match.equivalentBaseQuantity,'3');});
 await check('4 lốc TH có đường 180ml và 2 thùng Vinamilk có đường 1L',r=>{assert.equal(r.status,'multiple_items_not_supported');assert.equal(r.match,undefined);});
 const report={realGemini:true,source:'http://localhost:7051',assistant:'http://127.0.0.1:5219',businessWrites:0,details};fs.writeFileSync(path.join(evidence,'receiving-ai-results/live-gemini-receiving-results.json'),JSON.stringify(report,null,2));console.log('PASS '+details.length+' real receiving cases.');
})().catch(e=>{fs.writeFileSync(path.join(evidence,'receiving-ai-results/live-gemini-receiving-failure.json'),JSON.stringify({details,error:e.message},null,2));console.error(e.message);process.exitCode=1;});
