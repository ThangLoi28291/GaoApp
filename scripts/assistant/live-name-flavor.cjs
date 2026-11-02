// Actual Gemini with synthetic TEST issuer/catalog. Sends only the user's supplied name text.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../../Logs/assistant-integration-20261012/name-flavor-results/gemini-isolated');
const metadata=JSON.parse(fs.readFileSync(path.join(root,'process.json'),'utf8'));
const privateKey=fs.readFileSync(metadata.PrivateKey),rights=['assistant.use','assistant.chat','catalog.read','quotes.retail.read','quotes.wholesale.read','assistant.receiving.resolve','assistant.products.normalize_name'];
function token(delegation){const now=Math.floor(Date.now()/1000);const header={alg:'RS256',typ:delegation?'gaoapp-assistant-delegation+jwt':'gaoapp-assistant-context+jwt',kid:'name-test'};
const payload={iss:metadata.Issuer,sub:'staff-a',store_id:'1',warehouse_id:'10',legal_entity_id:'100',display_name:'Minh An · TEST',permissions:rights,iat:now,nbf:now,exp:now+120,jti:crypto.randomUUID(),aud:delegation?'gaoapp-assistant-data':'chatbotlocal-api',azp:delegation?'chatbotlocal':'gaoapp-backend'};
const data=Buffer.from(JSON.stringify(header)).toString('base64url')+'.'+Buffer.from(JSON.stringify(payload)).toString('base64url');return data+'.'+crypto.sign('RSA-SHA256',Buffer.from(data),privateKey).toString('base64url');}
const details=[];
async function request(text){const requestId=crypto.randomUUID();const r=await fetch('http://127.0.0.1:'+metadata.Port+'/api/v1/products/normalize-name',{method:'POST',headers:{Authorization:'Bearer '+token(false),'X-GaoApp-Delegation':token(true),'X-Request-Id':requestId,'Content-Type':'application/json'},body:JSON.stringify({originalText:text,inputSource:'speech',generation:1}),signal:AbortSignal.timeout(115000)});
const body=await r.json();assert.equal(r.status,200,JSON.stringify(body));assert.equal(body.source.isTest,true);details.push({requestId,text,proposedName:body.proposedName,status:body.status,clarifications:body.clarifications});console.log(text+' → '+body.proposedName+' ('+body.status+')');return body;}
(async()=>{
for(let attempt=0;attempt<40;attempt++){try{const health=await fetch('http://127.0.0.1:'+metadata.Port+'/health/ready',{signal:AbortSignal.timeout(1000)});if(health.ok)break;}catch{}if(attempt===39)throw Error('Isolated name test did not become ready.');await new Promise(resolve=>setTimeout(resolve,100));}
for(const text of ['sưa tuoi của th 180 vị bưởi','sưa tuoi của th 180ml vị bưởi','bánh ngot kinh do 500g loại hộp']){
 const r=await request(text);assert.doesNotMatch(r.proposedName,/Sưa tuoi/);assert.ok(!r.clarifications.some(c=>c.field==='name'));
 if(text.includes('bưởi')){assert.match(r.proposedName,/Sữa tươi TH/);assert.match(r.proposedName,/bưởi/);assert.match(r.proposedName,/180/);
  if(!text.includes('ml')){assert.doesNotMatch(r.proposedName,/ml/);assert.deepEqual(r.clarifications.map(c=>c.field),['capacity']);}
  else{assert.equal(r.status,'suggested');assert.match(r.proposedName,/180\s*ml/);}
 }else{assert.equal(r.status,'suggested');assert.match(r.proposedName,/Bánh ngọt Kinh Đô/);assert.match(r.proposedName,/500g/);}
}
fs.writeFileSync(path.join(root,'real-name-flavor-results.json'),JSON.stringify({realGemini:true,catalog:'synthetic TEST',businessWrites:0,details},null,2));console.log('PASS '+details.length+' real Gemini name cases.');
})().catch(e=>{fs.writeFileSync(path.join(root,'failure.json'),JSON.stringify({details,error:e.message},null,2));console.error(e.message);process.exitCode=1;});
