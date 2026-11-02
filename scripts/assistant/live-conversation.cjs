const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');const {call,evidence}=require('./live-ux.cjs');
(async()=>{const cases=[];
    const scenarios=[
        {name:'TH automatic quote and all units',messages:['sữa tuoi th','có đường 180ml','giá lốc','tất cả']},
        {name:'Pepsi incorrect capacity recovery',messages:['có nước ngọt không','pepsi','300ml','330ml','chai 330ml','pepsi chai 330ml','chai 390ml','tất cả']}
    ];
    for(const scenario of scenarios){const started=await call('/api/v1/conversations',{}),conversationId=started.conversationId;
        for(const message of scenario.messages){const r=await call('/api/v1/chat/messages',{conversationId,message});const item={scenario:scenario.name,message,reply:r.reply};cases.push(item);console.log(message+': '+r.reply);
            assert.ok(r.reply?.length);assert.ok(!/\n\d+\. /.test(r.reply));
            if(scenario.name.startsWith('TH')){
                if(message==='có đường 180ml'||message==='tất cả'){assert.match(r.reply,/Hộp: giá lẻ/);assert.match(r.reply,/Lốc: giá lẻ/);assert.match(r.reply,/Thùng: giá lẻ/);}
                if(message==='giá lốc'){assert.match(r.reply,/Lốc: giá lẻ/);assert.doesNotMatch(r.reply,/đ\/Hộp/);}
            }else{
                if(/330ml|300ml/.test(message)){assert.match(r.reply,/Pepsi/i);assert.doesNotMatch(r.reply,/đ\//);assert.match(r.reply,/320ml|390ml/);}
                if(message==='chai 390ml'||message==='tất cả'){assert.match(r.reply,/Pepsi chai 390ml/);assert.match(r.reply,/đ\/Chai/);assert.match(r.reply,/đ\/Lốc/);assert.doesNotMatch(r.reply,/330ml/);}
            }
            fs.writeFileSync(path.join(evidence,'live-conversation-results.json'),JSON.stringify({realGemini:true,businessSaves:0,cases},null,2));
        }
    }
    console.log('PASS real Gemini user conversations, no automatic substitute for missing sizes.');
})().catch(e=>{console.error(e.message);process.exitCode=1;});
