const test=require('node:test');
const assert=require('node:assert/strict');
const {buttons}=require('../../GaoApp.Web/wwwroot/Admin/js/receipt-document-actions.js');
const none={edit:false,manage:false,delete:false};
const employee={inventory:{edit:true,manage:false,delete:true},purchase:none};
test('employee draft has rename/delete while submitted drafts and locked states cannot be deleted',()=>{
    assert.match(buttons({id:3,status:1},employee),/Đổi tên/);assert.match(buttons({id:3,status:1},employee),/Xóa phiếu/);
    for(const status of [2,3,5])assert.doesNotMatch(buttons({id:3,status},employee),/receipt-delete/);
    assert.equal(buttons({id:3,status:1,submittedAtUtc:'2026-09-29'},employee),'');
});
test('returned receipts can be deleted after submission with the source-specific delete permission',()=>{
    const returned={id:3,status:4,submittedAtUtc:'2026-09-29'};
    assert.match(buttons(returned,employee),/data-receipt-delete="3"/);
    assert.doesNotMatch(buttons(returned,{inventory:{...none,edit:true},purchase:none}),/receipt-delete/);
    assert.equal(buttons({...returned,purchaseOrderId:9},employee),'');
    assert.match(buttons({...returned,purchaseOrderId:9},{inventory:none,purchase:employee.inventory}),/receipt-delete/);
    for(const field of ['approvedAtUtc','confirmedAtUtc'])
        assert.doesNotMatch(buttons({...returned,[field]:'2026-09-30'},employee),/receipt-delete/);
});
test('pending staff requests a name, while managers can rename without financial editing',()=>{
    assert.match(buttons({id:3,status:2},employee),/Yêu cầu đổi tên/);
    const manager={inventory:{...none,manage:true},purchase:none};
    for(const status of [1,2,3,4])assert.match(buttons({id:3,status},manager),/Đổi tên/);
    assert.doesNotMatch(buttons({id:3,status:2},manager),/Xóa phiếu/);
});
test('purchase receipts use purchase permissions, never inventory permission fallback',()=>{
    assert.equal(buttons({id:3,status:1,purchaseOrderId:9},employee),'');
    assert.match(buttons({id:3,status:1,purchaseOrderId:9},{inventory:none,purchase:employee.inventory}),/Đổi tên/);
});
test('invalid identities and read-only roles never get mutation buttons',()=>{
    for(const id of [0,-1,NaN,'<img>',2.5])assert.equal(buttons({id,status:1},employee),'');
    assert.equal(buttons({id:3,status:1},{inventory:none,purchase:none}),'');
});
