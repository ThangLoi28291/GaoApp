'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright'),mobile=require('./support-mobile-review.cjs');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let input='';process.stdin.on('data',part=>input+=part);process.stdin.on('end',()=>resolve(input));}));
 assert.equal(info.ownedFixture,true,'UI-20 requires an owned current-model GUID fixture');
 assert.ok(info.baseUrl&&info.accounts?.admin&&info.output&&info.ids?.waiting,'Real application fixture required');
 fs.mkdirSync(info.output,{recursive:true});
 const origin=new URL(info.baseUrl).origin,browser=await chromium.launch({channel:'chrome',headless:true});
 const contexts=[],errors=[],external=[],unexpected=[],accepted=[],responses=[],moduleResults={};
 info.ownedTempUploadTokens=[];
 const assistantReads=new Set(['Capabilities','ResumeConversation','ListConversations','History','RequestStatus']);
 async function login(role='admin'){
  const account=info.accounts[role];assert.ok(account,'Known fixture role '+role);
  const context=await browser.newContext({viewport:{width:390,height:844},reducedMotion:'reduce',serviceWorkers:'block'});contexts.push(context);
  await context.addInitScript(()=>{window.print=()=>{throw Error('Unexpected physical printing');};window.open=()=>null;});
  await context.route('**/*',route=>{const request=route.request(),url=new URL(request.url());
   if(url.origin===origin||['blob:','data:','about:'].includes(url.protocol))return route.continue();
   external.push({role,origin:url.origin,path:url.pathname});return route.abort('blockedbyclient');
  });
  const page=await context.newPage();page.setDefaultTimeout(30000);
  page.on('pageerror',error=>errors.push({role,message:error.message}));
  page.on('dialog',async dialog=>{errors.push({role,message:'Unexpected browser dialog '+dialog.type()+': '+dialog.message()});await dialog.dismiss();});
  page.on('response',response=>{const request=response.request(),url=new URL(response.url());if(url.origin===origin&&['fetch','xhr'].includes(request.resourceType()))responses.push({role,path:url.pathname,method:request.method(),status:response.status()});});
  await page.goto(info.baseUrl+'/admin/account/login?returnUrl=%2Fadmin%2Fcustomer-service');
  await page.locator('[name=UserName]').fill(account.user);await page.locator('[name=Password]').fill(account.password);
  if(await page.locator('[name=SelectedTerminalId]').count())await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
  await Promise.all([page.waitForURL(url=>!url.pathname.toLowerCase().endsWith('/login')),page.locator('#loginForm button[type=submit]').click()]);
  await context.route('**/*',route=>{
   const request=route.request(),url=new URL(request.url());if(url.origin!==origin)return route.fallback();
   if(['GET','HEAD','OPTIONS'].includes(request.method())||/^\/admin\/api\/store-activity\/(online|presence)$/.test(url.pathname))return route.fallback();
   const operation=url.pathname.match(/^\/admin\/api\/assistant\/call\/([^/]+)$/)?.[1];
   if(request.method()==='POST'&&operation&&assistantReads.has(operation)){accepted.push({role,path:url.pathname,kind:'owned-fake-assistant-only'});return route.fallback();}
   if(role==='admin'&&request.method()==='POST'&&url.pathname==='/admin/customer-service/conversations/'+info.ids.waiting+'/commands'){
    const body=request.postDataJSON();if(['claim','reply'].includes(body.action)&&body.requestId&&Number.isInteger(body.version)&&(body.action!=='reply'||body.message===info.inboxReplyText)){accepted.push({role,path:url.pathname,kind:'owned-web-conversation',requestId:body.requestId,action:body.action});return route.fallback();}
   }
   if(role==='admin'&&request.method()==='POST'&&url.pathname==='/admin/media/temp'&&/filename="ui20-browser-upload-[12]\.png"/.test(request.postData()||'')){accepted.push({role,path:url.pathname,kind:'owned-temp-image-upload'});return route.fallback();}
   if(role==='admin'&&request.method()==='DELETE'&&url.pathname==='/admin/media/temp'&&info.ownedTempUploadTokens.includes((request.postData()||'').trim())){accepted.push({role,path:url.pathname,kind:'owned-temp-image-revert'});return route.fallback();}
   unexpected.push({role,path:url.pathname,method:request.method()});return route.abort('blockedbyclient');
  });
  return page;
 }
 const review=(page,selector,label,options)=>mobile.review(page,selector,label,info.output,options);
 const contract={info,login,review,settle:mobile.settle,assert,evidence:mobile.evidence};
 try{
  for(const[name,file]of[['inbox','./customer-inbox-mobile-cases.cjs'],['administration','./customer-service-admin-mobile-cases.cjs'],['assistantMedia','./assistant-media-mobile-cases.cjs']]){
   const module=require(file);
   try{moduleResults[name]=await(typeof module==='function'?module:module.run)(contract);}
   catch(error){
    fs.writeFileSync(path.join(info.output,'action-failure.json'),JSON.stringify({module:name,error:String(error)},null,2)+'\n');
    let index=0;for(const context of contexts)for(const page of context.pages())if(!page.isClosed())await page.screenshot({path:path.join(info.output,'action-failure-'+index+++'.png'),fullPage:true,animations:'disabled'}).catch(()=>{});
    throw error;
   }
  }
  assert.deepEqual(unexpected,[],'No unapproved business writes');assert.deepEqual(errors,[],'No page errors');assert.deepEqual(external,[],'No direct external AI, provider, customer or printer request');
  assert.ok(mobile.evidence.cases.length>=30,'Required UI-20 native matrix');
  const result={passed:true,mode:'current_application_current_model_schema',mandatoryMatrixExecuted:true,migrationAcceptanceImplied:false,nativeStateCount:mobile.evidence.cases.length,phoneChecks:mobile.evidence.phoneChecks.length,desktopProofs:mobile.evidence.desktopProofs.length,printProofs:mobile.evidence.printProofs.length,dialogChecks:mobile.evidence.dialogChecks.length,dialogScrollChecks:mobile.evidence.dialogScrollChecks.length,unexpectedBusinessWrites:unexpected,pageErrors:errors,externalRequests:external,acceptedFixtureOperations:accepted,nativeResponses:responses,moduleResults,browser:'Fresh owned headless Chrome; existing user windows/tabs never enumerated, connected or closed'};
  fs.writeFileSync(path.join(info.output,'support-mobile-result.json'),JSON.stringify(result,null,2)+'\n');console.log(JSON.stringify({passed:true,states:result.nativeStateCount,phones:result.phoneChecks,desktop:result.desktopProofs,print:result.printProofs}));
 }finally{mobile.save(info.output);for(const context of contexts)await context.close();await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
