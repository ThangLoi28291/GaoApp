'use strict';
// Native UI-20 administration cases. The parent owns login, route guards, fixtures and the common matrix.
module.exports = async function run({info,login,review,settle,assert,evidence}) {
  assert.ok(info?.baseUrl&&Number(info.variantId)>0,'Owned native application and variant required');
  const result={cases:[],nativeReadResponses:[],localDrafts:[]};
  async function inspect(page,selector,label,options) {
    await settle(page); await review(page,selector,label,options);
    result.cases.push(label);
  }
  async function sensitive(page) {
    const source=await page.content();
    for(const marker of info.secretMarkers||[])assert.ok(!source.includes(marker),'No provider credential or file path in native admin HTML');
    if(info.foreignMarker)assert.ok(!source.includes(info.foreignMarker),'No other-store source record in native admin view');
  }
  async function go(page,route,selector) {
    const response=await page.goto(new URL(route,info.baseUrl).href);
    assert.ok(response?.ok(),'Native page '+route+' responds successfully');
    await page.locator(selector).waitFor({state:'visible'}); await settle(page); await sensitive(page);
  }
  const page=await login('admin');
  await go(page,'/admin/customer-service/channels','#customerChannels');
  assert.ok(await page.locator('#customerChannels [data-connection]').count(),'Configured fixture channel is rendered by native owner');
  await inspect(page,'#customerChannels','channels-configured');
  const channel=page.locator('#customerChannels [data-connection]').first(),load=channel.locator('[data-load]');
  const [deliveries]=await Promise.all([
    page.waitForResponse(response=>new URL(response.url()).pathname.endsWith('/deliveries')&&response.request().method()==='GET'),load.click()
  ]);
  await deliveries.finished();assert.equal(deliveries.status(),200);
  await page.waitForFunction(()=>[...document.querySelectorAll('#customerChannels [data-load]')].every(button=>!button.disabled));
  await channel.locator('.cc-deliveries').waitFor({state:'visible'});
  assert.ok((await channel.locator('.cc-deliveries').innerText()).trim(),'Native delivery result or empty state is rendered');
  result.nativeReadResponses.push({path:new URL(deliveries.url()).pathname,status:200});
  await inspect(page,'#customerChannels','channels-delivery-results');
  const reader=await login('reader');
  await go(reader,'/admin/customer-service/channels','#customerChannels');
  assert.equal(await reader.locator('#customerChannels [data-pause],#customerChannels [data-sync-retry]').count(),0,'Native view-only channels expose no manage command');
  await inspect(reader,'#customerChannels','channels-view-only');

  await go(page,'/admin/customer-service/quality','#customerQuality');
  await page.waitForFunction(()=>document.querySelectorAll('#cqMetrics .cq-metric').length===4&&document.querySelector('#cqCases')?.children.length>0&&document.querySelector('#cqEvaluation')?.children.length>0);
  assert.ok(await page.locator('#cqFeedback .cq-feedback').count(),'Native seeded feedback is available for evidence and proposal dialog');
  await inspect(page,'#customerQuality','customer-quality-report');
  const feedback=page.locator('#cqFeedback .cq-feedback').first();
  await feedback.locator('summary').click();
  assert.equal(await feedback.locator('details').evaluate(node=>node.open),true);
  assert.ok(await feedback.locator('pre').count()>=2,'Native question/reply evidence precedes review actions');
  await inspect(page,'#customerQuality','customer-quality-evidence');
  await feedback.getByRole('button',{name:'Soạn cách gọi đúng',exact:true}).click();
  await page.locator('#cqProposal').waitFor({state:'visible'});
  await page.locator('#cqProposalQuery').fill('Cách gọi chưa gửi · dữ liệu được kiểm tra trên cửa hàng thử nghiệm');
  await page.locator('#cqProposalVariant').fill(String(info.variantId));
  await inspect(page,'#customerQuality','customer-quality-proposal-draft',{dialog:true,nativeDialog:'#cqProposal'});
  await page.locator('#cqProposalCancel').click();await page.locator('#cqProposal').waitFor({state:'hidden'});
  await page.locator('#cqQuery').fill('Câu kiểm tra chưa lưu, không chứa thông tin riêng');
  await page.locator('#cqExpected').fill(String(info.variantId));
  await page.locator('#cqSplit').selectOption('holdout');
  await page.locator('#cqIndependent').check();
  await inspect(page,'#customerQuality','customer-quality-case-draft');
  result.localDrafts.push({case:'customer-quality-case-draft',saved:false});
  const [resolved,filteredReport]=await Promise.all([
    page.waitForResponse(response=>{const url=new URL(response.url());return url.pathname.endsWith('/quality/feedback')&&url.searchParams.get('status')==='resolved';}),
    page.waitForResponse(response=>new URL(response.url()).pathname.endsWith('/quality/report')),
    page.locator('#cqStatus').selectOption('resolved')
  ]);
  await Promise.all([resolved.finished(),filteredReport.finished()]);assert.equal(resolved.status(),200);assert.equal(filteredReport.status(),200);
  const resolvedJson=await resolved.json();
  await page.waitForFunction(expected=>document.querySelectorAll('#cqFeedback .cq-feedback').length===expected&&[...document.querySelectorAll('#cqFeedback summary')].every(summary=>summary.textContent.includes('Đã xử lý')),resolvedJson.queue.items.length);
  await settle(page);
  assert.equal(await page.locator('#cqQuery').inputValue(),'Câu kiểm tra chưa lưu, không chứa thông tin riêng','Native filter leaves unrelated case draft intact');
  await inspect(page,'#customerQuality','customer-quality-resolved-filter');
  result.nativeReadResponses.push({path:new URL(resolved.url()).pathname,status:200,items:resolvedJson.queue.items.length});

  await go(page,'/admin/assistant/settings','#aiEnabled');
  await inspect(page,'.mx-auto:has(> form #aiEnabled)','assistant-settings');
  const enabled=await page.locator('#aiChat').isChecked();
  await page.locator('#aiChat').setChecked(!enabled);
  await inspect(page,'.mx-auto:has(> form #aiEnabled)','assistant-settings-unsaved-draft');
  result.localDrafts.push({case:'assistant-settings-unsaved-draft',saved:false});

  await go(page,'/admin/assistant/knowledge?topic=delivery','#assistantKnowledgeAdmin');
  await inspect(page,'#assistantKnowledgeAdmin','assistant-knowledge');
  await page.locator('#policyTitle').fill('Chính sách giao hàng chưa lưu · bản nháp từ ca kiểm thử');
  const policyBody='Nhân viên xác nhận khu vực và phí giao với khách trước khi chốt.\nĐây là bản nháp chưa duyệt; không được đưa vào câu trả lời đang áp dụng.';
  await page.locator('#policyBody').fill(policyBody);
  await page.locator('#policyPreview').click();await page.locator('#policyPreviewBox').waitFor({state:'visible'});
  assert.equal(await page.locator('#policyPreviewBody').textContent(),policyBody,'Native local preview preserves exact draft text');
  await inspect(page,'#assistantKnowledgeAdmin','assistant-knowledge-local-preview');
  result.localDrafts.push({case:'assistant-knowledge-local-preview',saved:false,previewMatches:true});

  const catalogReply=await page.request.get(new URL('/admin/assistant/search/catalog.json',info.baseUrl).href);
  assert.equal(catalogReply.status(),200);const catalog=await catalogReply.json();
  assert.equal(String(catalog.storeId),String(info.storeId),'Native exported catalog belongs to the fixture store');
  const document=catalog.documents.find(item=>Number(item.id)===Number(info.variantId));
  assert.ok(document?.sku,'Native catalog contains the expected own-store variant SKU');
  await go(page,'/admin/assistant/search?query='+encodeURIComponent(document.sku),'#assistantSearchAdmin');
  assert.ok((await page.locator('#assistantSearchAdmin table').first().innerText()).includes(document.sku),'Native exact-SKU search result matches catalog');
  await inspect(page,'#assistantSearchAdmin','assistant-search-results');
  await page.locator('#teachQuery').fill('Cách gọi nhân viên chưa đề xuất · giữ nguyên mã hàng và phạm vi');
  await page.locator('#teachVariant').fill(String(info.variantId));
  await inspect(page,'#assistantSearchAdmin','assistant-search-teaching-draft');
  result.localDrafts.push({case:'assistant-search-teaching-draft',saved:false});
  const sample=page.locator('#assistantSearchAdmin details').filter({has:page.locator('summary').filter({hasText:'Mẫu dữ liệu tìm kiếm'})});
  await sample.locator('summary').click();assert.equal(await sample.evaluate(node=>node.open),true);
  await inspect(page,'#assistantSearchAdmin','assistant-search-native-catalog-details');
  const absent=info.catalogMissingQuery||'zzqvpxlmnnbbxxcc';
  await go(page,'/admin/assistant/search?query='+encodeURIComponent(absent),'#assistantSearchAdmin');
  assert.ok((await page.locator('#assistantSearchAdmin').innerText()).includes('Chưa tìm thấy mã phù hợp.'),'Native empty search remains an explicit empty state');
  await inspect(page,'#assistantSearchAdmin','assistant-search-empty');

  await go(page,'/admin/assistant/quality?days=1','.aq-page');
  assert.ok(await page.locator('.aq-page table').count(),'Native fixture upstream provides real validated report rows');
  assert.ok(await page.locator('.aq-feedback-item').count(),'Native fixture report includes a feedback reference');
  const statusContrast=await page.locator('.aq-page .alert-success').evaluate(node=>{
    const css=getComputedStyle(node),rgb=value=>(value.match(/[\d.]+/g)||[]).slice(0,3).map(Number);
    const luminance=value=>rgb(value).map(n=>{const c=n/255;return c<=.04045?c/12.92:((c+.055)/1.055)**2.4;}).reduce((sum,n,index)=>sum+n*[.2126,.7152,.0722][index],0);
    const foreground=luminance(css.color),background=luminance(css.backgroundColor);
    return{color:css.color,background:css.backgroundColor,contrast:(Math.max(foreground,background)+.05)/(Math.min(foreground,background)+.05)};
  });
  assert.ok(statusContrast.contrast>=4.5,JSON.stringify({case:'assistant-quality-report-status',...statusContrast}));
  result.statusContrast=statusContrast;
  await inspect(page,'.aq-page','assistant-quality-report');
  const reference=page.locator('.aq-feedback-item details').first();
  await reference.locator('summary').click();assert.equal(await reference.evaluate(node=>node.open),true);
  await inspect(page,'.aq-page','assistant-quality-reference');
  await go(page,'/admin/assistant/quality?days=7','.aq-page');
  assert.equal(await page.locator('#aqDays').inputValue(),'7');
  await inspect(page,'.aq-page','assistant-quality-seven-days');
  if(evidence)evidence.adminCases=result;
  return result;
};
