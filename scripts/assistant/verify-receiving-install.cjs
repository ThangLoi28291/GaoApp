const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../..'),evidence=path.join(root,'Logs/assistant-integration-20261012');
const hash=buffer=>crypto.createHash('sha256').update(buffer).digest('hex');
(async()=>{
 const health=await fetch('http://127.0.0.1:5219/health/ready');assert.equal(health.status,200);
 const app=await fetch('http://localhost:7051/');assert.equal(app.status,200);
 const built=fs.readFileSync(path.join(evidence,'chatbot-receiving-tests/ChatbotLocal.Web.dll'));
 const installed=fs.readFileSync(path.join(evidence,'chatbot-build/bin/ChatbotLocal.Web/debug/ChatbotLocal.Web.dll'));
 assert.equal(hash(built),hash(installed));
 for(const script of ['assistant-forms.js','warehouse-receiving-mobile.js','warehouse-receiving-detail.js']){
  const response=await fetch('http://localhost:7051/Admin/js/'+script,{cache:'no-store'});assert.equal(response.status,200);
  assert.equal(hash(Buffer.from(await response.arrayBuffer())),hash(fs.readFileSync(path.join(root,'GaoApp.Web/wwwroot/Admin/js',script))),script+' served stale content');
 }
 const report={gaoAppStatus:app.status,chatbotReady:health.status,installedMatchesTestedBuild:true,servedScriptsMatchSource:3};
 fs.writeFileSync(path.join(evidence,'receiving-ai-results/installation-verification.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
