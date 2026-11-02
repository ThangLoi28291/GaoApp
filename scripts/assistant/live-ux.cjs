// Authorized local read-only business acceptance. Creates assistant history only.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const evidence=path.resolve(__dirname,'../../Logs/assistant-integration-20261012');
const metadata=JSON.parse(fs.readFileSync(path.join(evidence,'live-metadata.json'),'utf8'));
const rights=['assistant.use','assistant.chat','assistant.receiving.resolve','assistant.products.normalize_name','catalog.read','quotes.retail.read','quotes.wholesale.read'];
const key=fs.readFileSync(path.join(evidence,'probe-runtime/integration-private.pem'));
function token(context){const now=Math.floor(Date.now()/1000),header={alg:'RS256',typ:context?'gaoapp-assistant-context+jwt':'gaoapp-assistant-delegation+jwt',kid:metadata.kid};
    const payload={iss:metadata.issuer,sub:String(metadata.user.id),display_name:metadata.user.name,store_id:String(metadata.stores[0].id),permissions:rights,iat:now,nbf:now,exp:now+120,jti:crypto.randomUUID(),aud:context?'chatbotlocal-api':'gaoapp-assistant-data',azp:context?'gaoapp-backend':'chatbotlocal'};
    const data=Buffer.from(JSON.stringify(header)).toString('base64url')+'.'+Buffer.from(JSON.stringify(payload)).toString('base64url');return data+'.'+crypto.sign('RSA-SHA256',Buffer.from(data),key).toString('base64url');}
async function call(route,body,source=false,options={}){const headers={Authorization:'Bearer '+token(!source),'X-Request-Id':options.requestId||crypto.randomUUID(),'Content-Type':'application/json'};if(!source)headers['X-GaoApp-Delegation']=token(false);
    const response=await fetch((source?'http://localhost:7051':'http://127.0.0.1:5219')+route,{method:options.method||'POST',headers,body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(115000)});
    const reply=await response.json();assert.equal(response.status,200,JSON.stringify(reply));return reply;}
module.exports={call,evidence};
if(require.main===module)(async()=>{const authorization=await call('/api/v1/assistant/context/authorize',{requiredPermissions:rights},true);assert.equal(authorization.allowed,true);
    const started=await call('/api/v1/conversations',{});const conversationId=started.conversationId,replies=[];
    for(const message of ['giá sữa tươi th','có đường','180ml']){const reply=await call('/api/v1/chat/messages',{conversationId,message});replies.push({message,reply:reply.reply});
        assert.ok(reply.reply?.length);if(message!=='180ml'){assert.ok(!/\n\d+\. /.test(reply.reply));assert.ok(reply.reply.length<350);}
        else {assert.match(reply.reply,/Hộp: giá lẻ/);assert.match(reply.reply,/Lốc: giá lẻ/);assert.match(reply.reply,/Thùng: giá lẻ/);}
        console.log(message+': '+reply.reply);
    }
    fs.writeFileSync(path.join(evidence,'live-ux-results.json'),JSON.stringify({source:'localhost:7051',assistant:'127.0.0.1:5219',realGemini:true,businessSaves:0,conversationId,replies},null,2));
    console.log('PASS real Gemini natural clarification and live quote.');
})().catch(e=>{console.error(e.message);process.exitCode=1;});
