'use strict';
const nodeAssert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path');

async function within(promise,milliseconds,message){
    let timer;
    try{return await Promise.race([promise,new Promise((_,reject)=>{timer=setTimeout(()=>reject(new Error(message)),milliseconds);})]);}
    finally{clearTimeout(timer);}
}

function responsesFor(page,match,count,action){
    return new Promise((resolve,reject)=>{
        const found=[];
        const timer=setTimeout(()=>finish(new Error('Timed out waiting for '+count+' native media responses.')),30000);
        function finish(error){clearTimeout(timer);page.off('response',onResponse);if(error)reject(error);else resolve(found);}
        function onResponse(response){
            if(!match(response))return;
            found.push(response);
            if(found.length===count){Promise.all(found.map(item=>item.finished())).then(()=>finish(),finish);}
        }
        page.on('response',onResponse);
        Promise.resolve().then(action).catch(finish);
    });
}

module.exports=async function run({info,login,review,settle,assert=nodeAssert,evidence={}}){
    assert.equal(info.ownedFixture,true,'UI-20 uploads require the explicitly owned disposable fixture.');
    const states=[],prefix='ui20-browser-upload-',uploads=[],reverts=[],assistant={draftPreserved:true,correctionSubmitted:false,realAiOrCustomerContact:false};
    info.ownedTempUploadTokens=[];
    async function inspect(page,selector,label,options){await review(page,selector,label,options);states.push(label);}
    const page=await login('admin');
    await page.goto(info.baseUrl+'/admin/media-library');
    await page.locator('[data-media-library]').waitFor();
    await page.locator('#gaoAssistant').waitFor({state:'attached'});
    await page.locator('#gaOpen').waitFor({state:'visible'});
    await page.locator('#gaOpen').click();
    await page.locator('#gaPanel').waitFor({state:'visible'});
    await page.waitForFunction(()=>document.querySelector('#gaConnectionState')?.dataset.state==='ready');
    const draft='Bản nháp UI-20 · giữ nội dung khi đổi kích thước, mở lịch sử và kiểm tra đề xuất.';
    await page.locator('#gaInput').fill(draft);
    assert.ok((await page.locator('#gaMessages').innerText()).includes('UI20'),'Actual native BFF history is from the owned fake upstream.');
    await inspect(page,'#gaoAssistant','assistant-chat-draft',{assistant:true});
    assert.equal(await page.locator('#gaInput').inputValue(),draft);
    await page.locator('#gaHistoryToggle').click();
    await page.locator('#gaHistoryDrawer').waitFor({state:'visible'});
    await inspect(page,'#gaoAssistant','assistant-native-history',{assistant:true});
    await page.locator('#gaRename').click();
    await page.locator('#gaTitle').fill('Tên hội thoại chưa lưu · không mất bản nháp');
    await inspect(page,'#gaoAssistant','assistant-rename-draft',{assistant:true});
    await page.locator('#gaManageCancel').click();
    await page.locator('#gaHistoryBack').click();
    await page.locator('#gaThread').waitFor({state:'visible'});
    assert.equal(await page.locator('#gaInput').inputValue(),draft);
    const correction=page.locator('.ga-correction-choice').first();
    assert.equal(await correction.count(),1,'Native history should expose its existing correction confirmation entry.');
    await correction.click();
    await page.locator('dialog.ga-correction[open]').waitFor();
    await page.locator('#gaTeachQuery').fill('Cách gọi chưa gửi để duyệt · UI20');
    await page.locator('#gaCorrectionSearch').fill(info.assistantProductQuery||'UI20');
    await Promise.all([
        page.waitForResponse(response=>new URL(response.url()).pathname==='/admin/api/assistant/search/products'&&response.status()===200),
        page.locator('.ga-correction-search button[type=submit]').click()
    ]);
    await page.locator('.ga-correction-results article button').first().waitFor();
    await page.locator('.ga-correction-results article button').first().click();
    assert.equal(await page.locator('.ga-correction-send').isEnabled(),true);
    await inspect(page,'#gaoAssistant','assistant-correction-before-confirmation',{dialog:true});
    await page.locator('.ga-correction-close').click();
    await page.locator('dialog.ga-correction').waitFor({state:'detached'});
    assert.equal(await page.locator('#gaInput').inputValue(),draft);
    if(info.assistantPendingChat===true){
        const chats=[];
        const chatRequest=request=>{if(new URL(request.url()).pathname==='/admin/api/assistant/call/Chat')chats.push(request.postDataJSON());};
        page.on('request',chatRequest);
        try{
            await page.locator('#gaInput').fill('UI20_PENDING_RESPONSE');
            const[response]=await Promise.all([
                page.waitForResponse(response=>new URL(response.url()).pathname==='/admin/api/assistant/call/Chat'&&response.status()===202),
                page.locator('#gaSend').click()
            ]);
            await response.finished();await page.waitForFunction(()=>document.querySelector('#gaConnectionState')?.dataset.state==='pending'&&document.querySelector('#gaMessages')?.getAttribute('aria-busy')==='false');
            assert.equal(chats.length,1);assert.equal(chats[0].body.message,'UI20_PENDING_RESPONSE');
            info.ownedAssistantPendingRequestId=chats[0].requestId;
            const nextDraft='Bản nháp kế tiếp · lượt trước còn đang chờ xử lý';
            await page.locator('#gaInput').fill(nextDraft);
            assert.equal(await page.locator('#gaSend').isDisabled(),true);
            await inspect(page,'#gaoAssistant','assistant-pending-native-job',{assistant:true});
            assert.equal(chats.length,1,'Viewport/history presentation must not replay a pending native job.');
            assert.equal(await page.locator('#gaInput').inputValue(),nextDraft);
            const[cancel]=await Promise.all([
                page.waitForResponse(response=>new URL(response.url()).pathname==='/admin/api/assistant/call/CancelRequest'),
                page.locator('#gaCancel').click()
            ]);
            await cancel.finished();
            assert.equal(cancel.request().postDataJSON().resourceId,chats[0].requestId,'Native cancel retains the exact pending request ID.');
            await page.locator('#gaRecovery').waitFor({state:'hidden'});
            assert.equal(await page.locator('#gaInput').inputValue(),nextDraft);
            assistant.pendingRequestId=chats[0].requestId;assistant.chatPosts=chats.length;assistant.cancelRetainedRequestId=true;
        }finally{page.off('request',chatRequest);}
    }
    await page.locator('#gaClose').click();
    await page.locator('#gaPanel').waitFor({state:'hidden'});

    await page.goto(info.baseUrl+'/admin/media-library');
    await page.locator('.media-card').first().waitFor();
    await page.waitForFunction(()=>[...document.querySelectorAll('[data-media-image]')].filter(image=>image.loading!=='lazy'||image.getBoundingClientRect().top<innerHeight).every(image=>image.complete&&image.naturalWidth>0));
    await inspect(page,'[data-media-library]','media-library-native-grid');
    const preview=page.locator('[data-media-library] [data-preview]').first();
    const previewUrl=await preview.getAttribute('data-preview');
    await preview.click();await page.locator('dialog[data-media-lightbox][open]').waitFor();
    await page.waitForFunction(()=>{const image=document.querySelector('dialog[data-media-lightbox] img');return image?.complete&&image.naturalWidth>0;});
    assert.equal(new URL(await page.locator('dialog[data-media-lightbox] img').getAttribute('src'),info.baseUrl).pathname,previewUrl);
    await inspect(page,'[data-media-library]','media-library-native-preview',{dialog:true});
    await page.locator('[data-close-preview]').click();await page.locator('dialog[data-media-lightbox]').waitFor({state:'hidden'});
    await page.locator('#mediaSearch').fill('UI20_NO_MATCHING_MEDIA');
    await Promise.all([page.waitForURL(url=>url.searchParams.get('search')==='UI20_NO_MATCHING_MEDIA'),page.locator('.media-toolbar button[type=submit]').click()]);
    await page.locator('.media-empty').waitFor();
    await inspect(page,'[data-media-library]','media-library-empty-filter');

    const reader=await login('reader');
    await reader.goto(info.baseUrl+'/admin/media-library');await reader.locator('[data-media-library]').waitFor();
    assert.equal(await reader.locator('[data-cleanup-all],[data-media-action]').count(),0,'Existing server-rendered view-only permissions retain media cleanup restrictions.');
    await inspect(reader,'[data-media-library]','media-library-view-only');

    await page.goto(info.baseUrl+'/admin/media/test');
    await page.locator('.filepond--root').waitFor();
    await inspect(page,'[data-media-workspace-phone="upload"],.filepond--root','media-test-native-empty');
    const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=','base64');
    const files=[1,2].map(index=>({name:prefix+index+'.png',mimeType:'image/png',buffer:png}));
    let release,observed,responsesReady;
    const hold=new Promise(resolve=>{release=resolve;}),bothObserved=new Promise(resolve=>{observed=resolve;}),
        completeResponses=new Promise(resolve=>{responsesReady=resolve;}),held=[],accepted=[],forwarding=[],routeErrors=[],uploadFailures=[];
    const isUpload=request=>request.method()==='POST'&&new URL(request.url()).pathname==='/admin/media/temp';
    const onUploadResponse=response=>{if(isUpload(response.request())){accepted.push(response);if(accepted.length===2)responsesReady();}};
    const onUploadFailure=request=>{if(isUpload(request))uploadFailures.push({url:request.url(),failure:request.failure()});};
    page.on('response',onUploadResponse);page.on('requestfailed',onUploadFailure);
    async function uploadDiagnostic(stage,error){
        const native=await page.evaluate(()=>({
            files:window.FilePond?.find(document.querySelector('.filepond--root'))?.getFiles().map(file=>({name:file.filename,status:file.status,serverId:file.serverId}))||[],
            items:[...document.querySelectorAll('.filepond--item')].map(item=>({id:item.id,state:item.dataset.filepondItemState,text:item.innerText})),
            inputs:[...document.querySelectorAll('.filepond--root input')].map(input=>({type:input.type,name:input.name,files:[...(input.files||[])].map(file=>({name:file.name,size:file.size,type:file.type}))}))
        })).catch(failure=>({diagnosticFailure:String(failure)}));
        const result={stage,error:error?String(error):null,held,accepted:accepted.map(response=>({status:response.status(),url:response.url()})),routeErrors,uploadFailures,native};
        fs.writeFileSync(path.join(info.output,'media-test-native-upload-'+stage+'.json'),JSON.stringify(result,null,2)+'\n');
        await page.screenshot({path:path.join(info.output,'media-test-native-upload-'+stage+'.png'),fullPage:true,animations:'disabled'}).catch(()=>{});
        return result;
    }
    const nativeUpload=async route=>{
        const request=route.request();
        if(request.method()!=='POST')return route.fallback();
        let done;
        forwarding.push(new Promise(resolve=>{done=resolve;}));
        try{
            const multipart=request.postDataBuffer();
            const names=files.filter(file=>multipart?.includes(Buffer.from('filename="'+file.name+'"'))).map(file=>file.name);
            assert.equal(names.length,1,'Only the two known fixture uploads may be delayed.');
            held.push({name:names[0],method:request.method(),path:new URL(request.url()).pathname});
            if(held.length===2)observed();
            await hold;
            await route.fallback();
        }catch(error){routeErrors.push(String(error));await route.abort('failed').catch(()=>{});}
        finally{done();}
    };
    await page.route('**/admin/media/temp',nativeUpload);
    let progressError,pendingNative;
    try{
        await page.locator('.filepond--root input[type=file]').setInputFiles(files);
        await within(bothObserved,15000,'FilePond did not initiate both native fixture upload requests.');
        // FilePond uses "busy processing" before transport progress and "processing" after it.
        await page.waitForFunction(()=>[...document.querySelectorAll('.filepond--item')].filter(item=>item.dataset.filepondItemState?.split(/\s+/).includes('processing')).length===2,null,{timeout:15000});
        assert.equal(held.length,2,'FilePond creates one native request per selected test file.');
        assert.equal(accepted.length,0,'The actual native requests remain pending before the progress review.');
        pendingNative=await uploadDiagnostic('pending');
        await inspect(page,'[data-media-workspace-phone="upload"],.filepond--root','media-test-native-upload-progress');
        assert.equal(held.length,2,'Viewport/print transitions must not replay pending uploads.');
        assert.equal(accepted.length,0,'Presentation must not acknowledge a pending native upload.');
    }catch(error){progressError=error;await uploadDiagnostic('pending-failure',error);}
    finally{release();}
    try{
        await Promise.all(forwarding);
        assert.deepEqual(routeErrors,[],'All held requests fall through the original write guard once.');
        await within(completeResponses,30000,'Timed out waiting for both released native media responses.');
        await Promise.all(accepted.map(response=>response.finished()));
        assert.deepEqual(uploadFailures,[],'Both original native uploads finish without cancellation.');
    }catch(error){await uploadDiagnostic('response-failure',error);throw progressError||error;}
    finally{await page.unroute('**/admin/media/temp',nativeUpload);page.off('response',onUploadResponse);page.off('requestfailed',onUploadFailure);}
    if(progressError)throw progressError;
    for(const response of accepted){
        assert.equal(response.status(),200,'Native multipart upload succeeded in the owned fixture.');
        const token=(await response.text()).trim();assert.match(token,/^[0-9a-f]{36}$/);
        const multipart=response.request().postDataBuffer();
        const names=files.filter(file=>multipart?.includes(Buffer.from('filename="'+file.name+'"'))).map(file=>file.name);
        assert.equal(names.length,1,'Each native upload request owns exactly one expected test file.');
        uploads.push({name:names[0],token,status:response.status()});info.ownedTempUploadTokens.push(token);
    }
    assert.equal(new Set(uploads.map(item=>item.name)).size,2);assert.equal(new Set(uploads.map(item=>item.token)).size,2);
    await page.waitForFunction(()=>document.querySelectorAll('.filepond--item[data-filepond-item-state="processing-complete"]').length===2);
    await settle(page);
    await inspect(page,'[data-media-workspace-phone="upload"],.filepond--root','media-test-native-two-files');
    for(let index=0;index<2;index++){
        const[response]=await responsesFor(page,response=>response.request().method()==='DELETE'&&new URL(response.url()).pathname==='/admin/media/temp',1,
            ()=>page.locator('.filepond--item[data-filepond-item-state="processing-complete"] .filepond--action-revert-item-processing').first().click());
        assert.equal(response.status(),200);const token=response.request().postData()?.trim();assert.ok(info.ownedTempUploadTokens.includes(token),'Only this test\'s uploaded token may be reverted.');
        reverts.push({token,status:response.status()});
        await page.waitForFunction(expected=>document.querySelectorAll('.filepond--item[data-filepond-item-state="processing-complete"]').length===expected,1-index);
    }
    assert.equal(new Set(reverts.map(item=>item.token)).size,2,'Each of the two owned uploads is reverted once.');
    await page.waitForFunction(()=>document.querySelectorAll('.filepond--item[data-filepond-item-state="processing-complete"]').length===0);
    const result={passed:true,states,assistant,media:{previewUrl,cleanupCommands:0,pendingNativeUploadRequests:held.length,pendingNativeStates:pendingNative?.native.items,uploads,reverts,preExistingMediaNeverDeleted:true},nativeFilePondUsed:true};
    evidence.assistantMedia=result;
    return result;
};
