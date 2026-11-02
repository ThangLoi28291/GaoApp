const fs=require('node:fs'),path=require('node:path');const {call,evidence}=require('./live-ux.cjs');
(async()=>{const results=[];
    for(const query of ['nước ngọt','pepsi','pepsi 300ml','pepsi 330ml','pepsi chai 330ml','sữa tươi TH']){
        const r=await call('/api/v1/assistant/catalog/search',{query,limit:100},true);
        const item={query,nextCursor:r.nextCursor,variants:r.variants.map(v=>({product:v.product,attributes:v.attributes,units:v.units.map(u=>u.unit)}))};results.push(item);
        console.log(JSON.stringify(item));
    }
    fs.writeFileSync(path.join(evidence,'catalog-discovery.json'),JSON.stringify(results,null,2));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
