'use strict';
// Native UI-20 inbox cases. The caller owns fresh browser contexts and isolated SQL/SQLite fixtures.
async function run({info,login,review,settle,assert,evidence}) {
  for(const key of ['waiting','closed','zalo'])assert.ok(info.ids?.[key],'Real owned inbox fixture required: '+key);
  const result={cases:[],commands:[],drafts:[],launcherChecks:[],pendingRequests:0,realProviderMessagesSent:0};
  const page=await login('admin'),base=info.baseUrl.replace(/\/$/,'');
  const path=id=>'/admin/customer-service/conversations/'+id+'/commands';
  async function inspect(target,label) {
    await review(target,'#customerInbox',label,{dynamic:true,inbox:true});result.cases.push(label);
    const launch=await target.evaluate(()=>{
      const root=document.getElementById('customerInbox'),button=document.getElementById('gaOpen'),panel=document.getElementById('gaPanel');
      const visible=node=>{if(!node||!node.getClientRects().length)return false;for(let n=node;n;n=n.parentElement){const css=getComputedStyle(n);if(n.hidden||n.inert||css.display==='none'||css.visibility==='hidden')return false;}return true;};
      if(root?.dataset.ciPhonePane!=='conversation'||!visible(button)||panel&&!panel.hidden)return{applicable:false};
      const box=node=>{const r=node.getBoundingClientRect();return{left:r.left,right:r.right,top:r.top,bottom:r.bottom};},launcher=box(button);
      const fields=['ciSend','ciReply','ciAttach','ciRemoveAsset'].map(id=>document.getElementById(id)).filter(visible).map(node=>{const bounds=box(node);return{id:node.id,bounds,overlap:Math.min(bounds.right,launcher.right)>Math.max(bounds.left,launcher.left)&&Math.min(bounds.bottom,launcher.bottom)>Math.max(bounds.top,launcher.top)};});
      return{applicable:true,width:innerWidth,height:innerHeight,launcher,fields};
    });
    assert.ok(!launch.fields?.some(field=>field.overlap),'Assistant launcher must not cover native inbox controls: '+JSON.stringify({label,...launch}));
    result.launcherChecks.push({label,...launch});
  }
  async function ready(target) {
    await target.locator('#customerInbox[data-ci-phone]').waitFor();
    await target.waitForFunction(()=>document.querySelector('#ciList')?.textContent.trim().length>0);
    await settle(target);
  }
  async function queue(target) {
    if(await target.locator('#customerInbox').getAttribute('data-ci-phone-pane')==='conversation')await target.locator('.cim-back').click();
    await target.waitForFunction(()=>document.querySelector('#customerInbox').dataset.ciPhonePane==='queue');
  }
  async function open(target,id) {
    await queue(target);
    if(await target.locator('#ciSource').inputValue()!=='all')await target.locator('#ciSource').selectOption('all');
    if(await target.locator('#ciFilter').inputValue()!=='all')await target.locator('#ciFilter').selectOption('all');
    await target.locator('#ciList .ci-item[data-id="'+id+'"]').waitFor({state:'visible'});
    const response=target.waitForResponse(response=>new URL(response.url()).pathname==='/admin/customer-service/conversations/'+id&&response.request().method()==='GET');
    await target.locator('#ciList .ci-item[data-id="'+id+'"]').click();await(await response).finished();
    await target.waitForFunction(()=>document.querySelector('#customerInbox').dataset.ciPhonePane==='conversation'&&!document.querySelector('#ciConversation').hidden);
    await target.waitForFunction(()=>[...document.querySelectorAll('#ciMessages .cq-rating select')].every(select=>!select.disabled));
    await settle(target);
  }
  const isCommand=(response,id,action)=>{
    if(new URL(response.url()).pathname!==path(id)||response.request().method()!=='POST')return false;
    try{return response.request().postDataJSON().action===action;}catch{return false;}
  };
  await page.goto(base+'/admin/customer-service');await ready(page);
  assert.equal(await page.locator('#customerInbox').getAttribute('data-ci-phone-pane'),'queue');
  if(info.ids.foreign)assert.equal(await page.locator('#ciList .ci-item[data-id="'+info.ids.foreign+'"]').count(),0,'Foreign store thread never enters this queue');
  await inspect(page,'inbox-queue');
  await page.locator('.cim-options').click();assert.equal(await page.locator('.cim-options').getAttribute('aria-expanded'),'true');
  await inspect(page,'inbox-options-native-settings');await page.locator('.cim-options').click();
  await open(page,info.ids.waiting);
  assert.ok((await page.locator('#ciState').textContent()).startsWith('Web'),'Only the known owned web fixture may receive test commands');
  assert.equal(await page.locator('#ciReply').isDisabled(),true);assert.equal(await page.locator('#ciClaim').isDisabled(),false);
  await inspect(page,'inbox-waiting-conversation');
  const claimResponse=page.waitForResponse(response=>isCommand(response,info.ids.waiting,'claim'));
  await page.locator('#ciClaim').click();const claimed=await claimResponse;assert.equal(claimed.status(),200);await claimed.finished();
  await page.waitForFunction(()=>!document.querySelector('#ciReply').disabled&&document.querySelector('#ciClaim').textContent==='Đã tiếp nhận');
  result.commands.push({conversation:info.ids.waiting,action:'claim',status:200});
  await inspect(page,'inbox-owned-web-compose');
  await page.locator('.cim-info').click();assert.equal(await page.locator('.cim-info').getAttribute('aria-expanded'),'true');
  if(await page.locator('#ciIdentity').count())await page.locator('#ciIdentity > summary').click();
  await inspect(page,'inbox-native-profile-actions');
  if(await page.locator('#ciIdentity[open]').count())await page.locator('#ciIdentity > summary').click();
  await page.locator('.cim-info').click();
  const draft='UI20 bản nháp chưa gửi · giữ nguyên khi về hàng chờ, đổi kích thước và tiếp tục hội thoại.';
  await page.locator('#ciReply').fill(draft);await page.locator('.cim-back').click();
  assert.equal(await page.locator('#ciReply').inputValue(),draft);assert.equal(await page.locator('#customerInbox').getAttribute('data-ci-phone-pane'),'queue');
  assert.equal(await page.locator('.cim-resume').isVisible(),true);
  await inspect(page,'inbox-draft-back-to-queue');
  await page.locator('.cim-resume').click();assert.equal(await page.locator('#ciReply').inputValue(),draft);
  await inspect(page,'inbox-resumed-draft');
  result.drafts.push({conversation:info.ids.waiting,kind:'text',backResumeResizePrintRetained:true});

  const reply=info.inboxReplyText||'UI20 browser · trả lời đúng một lần';await page.locator('#ciReply').fill(reply);
  let release,observed,replyCount=0;
  const releasePromise=new Promise(resolve=>{release=resolve;}),observedPromise=new Promise(resolve=>{observed=resolve;});
  const matcher='**'+path(info.ids.waiting);
  const intercept=async route=>{
    let body;try{body=route.request().postDataJSON();}catch{return route.fallback();}
    if(route.request().method()!=='POST'||body?.action!=='reply')return route.fallback();
    replyCount++;assert.equal(body.message,reply);assert.match(body.requestId,/^[a-f0-9-]{36}$/i);
    assert.equal(typeof body.version,'number');
    observed({body});
    // Continue the original request through the caller's scope/write guard after latency.
    await releasePromise;await route.fallback();
  };
  await page.route(matcher,intercept);
  const replyResponse=page.waitForResponse(response=>isCommand(response,info.ids.waiting,'reply'));
  try {
    await page.locator('#ciSend').dblclick();await observedPromise;
    assert.equal(await page.locator('#ciReply').isDisabled(),true);assert.equal(await page.locator('#ciSend').isDisabled(),true);
    assert.equal(await page.locator('.cim-pending').isVisible(),true,'Pending copy reflects the original pending state, before an acknowledgement');
    assert.equal(replyCount,1,'Two physical taps must enter the native pending guard, not create two writes');
    await inspect(page,'inbox-pending-idempotent-reply');
  } finally { release(); }
  const sent=await replyResponse;assert.equal(sent.status(),200);await sent.finished();
  await page.waitForFunction(()=>!document.querySelector('#ciReply').disabled&&document.querySelector('#ciReply').value==='');
  assert.equal(await page.locator('.cim-pending').isVisible(),false);
  assert.equal(await page.locator('#ciMessages .ci-bubble[data-role="agent"] p').filter({hasText:reply}).count(),1);
  assert.equal(replyCount,1);result.pendingRequests=replyCount;
  result.commands.push({conversation:info.ids.waiting,action:'reply',status:200,requestCount:replyCount,text:reply});
  await page.unroute(matcher,intercept);

  await open(page,info.ids.closed);assert.equal(await page.locator('#ciReply').isDisabled(),true);assert.equal(await page.locator('#ciSend').isDisabled(),true);
  await inspect(page,'inbox-closed-conversation');
  await open(page,info.ids.zalo);
  assert.equal(await page.locator('#ciUpload').isVisible(),true);assert.equal(await page.locator('#ciAsset').isDisabled(),false,'Owned fake Zalo thread is required for a local file draft');
  const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=','base64');
  await page.locator('#ciReply').fill('UI20 ảnh đính kèm chưa gửi; chỉ kiểm tra bản nháp trên thiết bị.');
  await page.locator('#ciAsset').setInputFiles({name:'UI20-anh-ban-nhap.png',mimeType:'image/png',buffer:png});
  await page.waitForFunction(()=>!document.querySelector('#ciAssetPreview').hidden&&!document.querySelector('#ciAssetImage').hidden&&document.querySelector('#ciAssetImage').complete&&document.querySelector('#ciAssetImage').naturalWidth>0);
  const fileState=await page.locator('#ciAssetImage').getAttribute('src'),fileName=await page.locator('#ciAssetName').textContent(),fileDraft=await page.locator('#ciReply').inputValue();
  await page.locator('.cim-back').click();assert.equal(await page.locator('#ciAssetImage').getAttribute('src'),fileState);
  await page.locator('.cim-resume').click();assert.equal(await page.locator('#ciAssetImage').getAttribute('src'),fileState);assert.equal(await page.locator('#ciAssetName').textContent(),fileName);assert.equal(await page.locator('#ciReply').inputValue(),fileDraft);
  assert.equal(await page.locator('#ciReply').evaluate(node=>node.required),false);
  await inspect(page,'inbox-local-file-draft-retained');
  assert.equal(await page.locator('#ciAssetImage').getAttribute('src'),fileState);assert.equal(await page.locator('#ciAssetName').textContent(),fileName);
  result.drafts.push({conversation:info.ids.zalo,kind:'local-image',backResumeResizePrintRetained:true,fileName:'UI20-anh-ban-nhap.png',uploaded:false,sent:false});

  const reader=await login('reader');await reader.goto(base+'/admin/customer-service');await ready(reader);await open(reader,info.ids.human||info.ids.waiting);
  assert.equal(await reader.locator('#ciReply').isDisabled(),true);assert.equal(await reader.locator('#ciSend').isDisabled(),true);assert.equal(await reader.locator('#ciClaim').isDisabled(),true);
  assert.equal(await reader.locator('.ci-settings').count(),0);
  await inspect(reader,'inbox-view-only-conversation');
  result.passed=true;result.nativeSelectionBehavior='Selecting another thread or reselecting the native row still invokes the original open() draft reset; Back/Resume are presentation-only and preserve the current draft.';
  evidence.inbox=result;return result;
}
module.exports=run;
module.exports.run=run;
