const test=require('node:test'),assert=require('node:assert/strict');
const {createStore,commandAllowed}=require('../../GaoApp.Web/wwwroot/Admin/js/receiving-draft-recovery.js');
function storage(){const data=new Map();return{get length(){return data.size;},key:i=>[...data.keys()][i],getItem:key=>data.get(key)??null,setItem:(key,value)=>data.set(key,value),removeItem:key=>data.delete(key),data};}
const scope={user:12,store:7,document:34};
const commandId='b9e925bc-d66d-456a-b7dc-8b2f5b95b04d';
const spec={url:'/admin/api/stock-documents/34/intake/known',method:'POST',body:JSON.stringify({commandId,quantity:2,productUnitConversionId:5,documentRowVersion:'AAAAAAAAAAE='})};
test('receipt drafts survive a new instance and stay scoped to account, store and document',()=>{
 const disk=storage(),draft=createStore(disk,scope);draft.put('command:'+commandId,'command',spec,'Sữa · 2 hộp');
 assert.deepEqual(createStore(disk,scope).list()[0].data,spec);
 for(const other of [{...scope,user:13},{...scope,store:8},{...scope,document:35}])assert.deepEqual(createStore(disk,other).list(),[]);
});
test('independent saved items are retained when another item is updated or acknowledged',()=>{
 const disk=storage(),first=createStore(disk,scope),second=createStore(disk,scope);
 first.put('quantity:line-5','quantity',{value:'10',baseline:'1'},'Món 5');
 second.put('quantity:line-6','quantity',{value:'12',baseline:'2'},'Món 6');
 first.put('quantity:line-5','quantity',{value:'11',baseline:'1'},'Món 5');
 assert.equal(first.list().length,2);first.remove('quantity:line-5');assert.equal(second.list()[0].data.value,'12');
});
test('unchanged forms keep their identity across reload while a newer unsent edit gets a separate identity',()=>{
 const disk=storage(),first=createStore(disk,scope);const original=first.put('form:known','form',{quantity:'2'},'Món 2');
 const reopened=createStore(disk,scope);assert.equal(reopened.put('form:known','form',{quantity:'2'},'Món 2').generation,original.generation);
 assert.notEqual(reopened.put('form:known','form',{quantity:'3'},'Món 3').generation,original.generation);
});
test('recovery permits only additive intake and quantity endpoints of the current receipt',()=>{
 assert.equal(commandAllowed(spec,34),true);assert.equal(commandAllowed(spec,35),false);
 for(const url of ['/admin/api/stock-documents/34/submit-approval','/admin/api/stock-documents/34/intake/1/review','https://other.example/admin/api/stock-documents/34/intake/known'])assert.equal(commandAllowed({...spec,url},34),false);
 assert.equal(commandAllowed({...spec,method:'DELETE'},34),false);
 assert.equal(commandAllowed({...spec,body:JSON.stringify({quantity:2})},34),false);
 assert.equal(commandAllowed({url:'/admin/api/stock-documents/34/lines/9',method:'PUT',body:JSON.stringify({quantity:4,unitCost:null})},34),true);
 assert.equal(commandAllowed({url:'/admin/api/stock-documents/34/lines/9',method:'PUT',body:JSON.stringify({quantity:4,unitCost:100})},34),false);
});
test('corrupt and forged stored commands are ignored without deleting valid drafts',()=>{
 const disk=storage(),draft=createStore(disk,scope);draft.put('command:valid','command',spec,'Món đã nhập');
 draft.put('command:bad','command',{...spec,url:'/admin/roles/delete'},'Không thực thi');
 const key=[...disk.data.keys()].find(x=>x.endsWith('command:bad'));disk.data.set(key,'not json');
 assert.equal(draft.list().length,1);
 const valid=[...disk.data.keys()].find(x=>x.endsWith('command:valid'));const record=JSON.parse(disk.data.get(valid));record.user='999';disk.data.set(valid,JSON.stringify(record));assert.equal(draft.list().length,0);
});
test('storage failure and oversize photos do not silently replace a previous draft',()=>{
 const disk=storage(),draft=createStore(disk,scope);draft.put('form:intake','form',{name:'Món chưa lưu'},'Bản nháp');
 assert.throws(()=>draft.put('form:intake','form',{photo:'x'.repeat(460001)},'Ảnh quá lớn'));
 assert.equal(draft.get('form:intake').data.name,'Món chưa lưu');disk.setItem=()=>{throw new Error('QuotaExceeded');};
 assert.throws(()=>draft.put('form:intake','form',{name:'Món khác'},'Món khác'));assert.equal(draft.get('form:intake').data.name,'Món chưa lưu');
});
