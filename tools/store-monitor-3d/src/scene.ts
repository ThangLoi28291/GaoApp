import * as B from './engine';
import { createRetailCharacter } from './retail-character.js';
import { createDeliveryMotorbike } from './retail-motorbike';
import { LiveStoreWorld } from './live-world.mjs';
import { SHOP_SHELVES, WAREHOUSE_RACKS, counterPoint } from './store-layout';
import { DELIVERY_PARKING, ROAD_POINTS, ROAD_ENTRY, BOARDING_SECONDS, RIDER_SEAT } from './delivery-road.mjs';

const colors:any={pos:'#23896a',receipt:'#4c88b2',warehouse:'#c78748',label:'#ab8852',review:'#8581b1',delivery:'#9673bd',idle:'#648d8b'};
export function createLiveStoreScene(canvas:HTMLCanvasElement,labels:HTMLElement,options:any) {
    let engine:B.Engine;
    try { engine=new B.Engine(canvas,true,{preserveDrawingBuffer:false,stencil:false,powerPreference:'high-performance'},false); }
    catch { throw new Error('Máy chưa bật WebGL. Anh có thể xem bằng chế độ 2D.'); }
    engine.setHardwareScalingLevel(Math.max(1,window.devicePixelRatio || 1));
    const scene=new B.Scene(engine); scene.clearColor=B.Color4.FromHexString('#edf3f5ff');
    const camera=new B.ArcRotateCamera('store',-1.12,.73,58,new B.Vector3(12,.3,5),scene);
    camera.lowerRadiusLimit=7; camera.upperRadiusLimit=95; camera.lowerBetaLimit=.3; camera.upperBetaLimit=1.25;
    camera.wheelDeltaPercentage=.015; camera.minZ=.1; camera.maxZ=160; camera.attachControl(canvas,true);
    const sky=new B.HemisphericLight('sky',new B.Vector3(0,1,0),scene); sky.intensity=.72;
    sky.groundColor=B.Color3.FromHexString('#c5d3ce');
    const sun=new B.DirectionalLight('sun',new B.Vector3(-.4,-1,.3),scene); sun.intensity=.48;
    scene.imageProcessingConfiguration.exposure=.9;scene.imageProcessingConfiguration.contrast=1.1;
    const materials=new Map<string,B.StandardMaterial>(), statics:B.Mesh[]=[];
    let serial=0, disposed=false, connected=false, motion=true, visible=true, lastFrame=0, lastTick=0, frames=0, fpsAt=0, interacted=0;
    const world=new LiveStoreWorld(), actors=new Map<string,any>(), bikes=new Map<string,any>();
    let counters:B.TransformNode|null=null, counterKey='', selected:string|null=null, dirty=true;
    function mat(color:string,alpha=1) {
        const key=color+alpha; if(materials.has(key)) return materials.get(key)!;
        const m=new B.StandardMaterial(key,scene); m.diffuseColor=B.Color3.FromHexString(color);
        m.specularColor=new B.Color3(.04,.04,.04); m.alpha=alpha; materials.set(key,m); return m;
    }
    function box(x:number,y:number,z:number,w:number,h:number,d:number,color:string,zone='overview',parent?:B.TransformNode) {
        const m=B.MeshBuilder.CreateBox('fixture-'+serial++,{width:w,height:h,depth:d},scene);
        m.position.set(x,y,z); m.material=mat(color); m.metadata={zone};
        if(parent)m.parent=parent; else statics.push(m); return m;
    }
    function sign(text:string,x:number,y:number,z:number,w:number,h:number,zone:string) {
        const t=new B.DynamicTexture('sign-'+serial++,{width:512,height:96},scene,false),ctx=t.getContext();
        ctx.fillStyle=colors[zone] || '#4e7968'; ctx.fillRect(0,0,512,96);
        ctx.fillStyle='#ffffff';ctx.font='bold 39px Arial';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(text,256,50,490);t.update();
        const m=new B.StandardMaterial('sign-ink',scene); m.diffuseTexture=t; m.emissiveColor=B.Color3.White();m.disableLighting=true;
        const p=B.MeshBuilder.CreatePlane('sign-'+text,{width:w,height:h,sideOrientation:B.Mesh.DOUBLESIDE},scene);
        p.position.set(x,y,z);p.material=m;p.metadata={zone};return p;
    }
    // Accepted floor plan: receiving left, warehouse rear centre, office rear right, labels left, packing right.
    box(2,-.22,4.7,31,.4,27.6,'#cad6d9');
    box(2,.015,4.7,31,.05,27.6,'#faf8ef');
    box(-7.2,.048,13.2,10.5,.025,9.3,'#e3edf4','receipt');
    box(2.4,.055,13.3,10.2,.028,9.0,'#edf0e5','warehouse');
    box(13.1,.062,14,8,.03,8.5,'#eeebf4','review');
    box(-11,.065,4.8,4,.04,4,'#f5ecd9','label');
    box(11.5,.06,3.5,8,.035,7,'#efe7f4','delivery');
    box(2,1.2,18.4,31,2.4,.18,'#d8e3dd');
    box(-13.4,.8,4.7,.18,1.6,27.5,'#dce5de');
    for(let x=-12;x<17;x+=2)box(x,.045,4.4,.012,.008,27,'#e5e6dc');
    for(let z=-8;z<18;z+=2)box(2,.045,z,30,.008,.012,'#e5e6dc');
    // Rear receiving entrance, scanner, cartons.
    box(-6.1,1.1,18.25,3.2,2.2,.1,'#a9beca','receipt');
    box(-6.1,.65,14.6,3,1.3,2.3,'#85a4b6','receipt');
    box(-6.1,1.34,14.6,3.1,.1,2.4,'#e5eceb','receipt');
    box(-6.5,1.46,13.8,.5,.2,.42,'#35565b','receipt');
    for(let i=0;i<3;i++)box(-6.9+i*.8,1.62,14.8,.65,.55,.8,'#d4b083','receipt');
    sign('NHẬP HÀNG',-6.1,2.65,18.1,4,.65,'receipt');
    for(const rack of WAREHOUSE_RACKS) {
        for(const dx of [-.68,.68])for(const dz of [-1.94,1.94])box(rack.x+dx,1.25,rack.z+dz,.08,2.5,.08,'#718795','warehouse');
        for(const y of [.15,1,1.85]) {
            box(rack.x,y,rack.z,1.5,.1,4,'#a3b4b4','warehouse');
            for(let i=0;i<3;i++)box(rack.x,y+.35,rack.z-1.25+i*1.25,1.05,.6,.9,'#c5a57c','warehouse');
        }
    }
    sign('KHO · KIỂM KHO',2.2,2.65,18.1,6,.65,'warehouse');
    // Low partition and glass leave the employee visible at the office computer.
    box(9.5,.42,14.25,.16,.84,7.9,'#c4c9d8','review');
    const glass=box(9.5,1.42,14.25,.05,1.1,7.9,'#bbd8e1','review');glass.material=mat('#bbd8e1',.22);
    box(10.65,.42,10.3,2.45,.84,.16,'#c4c9d8','review');
    box(15.95,.42,10.3,2.9,.84,.16,'#c4c9d8','review');
    box(13.4,.65,15.7,3,1.3,1.3,'#aaa7bd','review');
    box(13.4,1.34,15.7,3.1,.1,1.4,'#f3ead8','review');
    box(13.9,1.82,15.7,.9,.65,.12,'#3e5260','review');
    box(13.9,1.82,15.62,.75,.52,.025,'#a4d8d4','review');
    box(12.9,1.42,15.45,.5,.025,.6,'#fffdf2','review');
    box(13.4,.5,14.05,.65,.12,.65,'#69788d','review');
    box(13.4,.82,13.75,.65,.65,.1,'#69788d','review');
    box(13.4,.24,14.05,.1,.45,.1,'#566773','review');
    sign('VĂN PHÒNG · DUYỆT',13.4,2.65,18.1,6,.65,'review');
    // Printer station on the left of the ten retail shelves.
    box(-11,.65,5.15,3,1.3,1.4,'#bea783','label');
    box(-11,1.34,5.15,3.1,.1,1.5,'#f1e1c6','label');
    box(-11,1.64,5.15,1.4,.6,1,'#546772','label');
    box(-11,1.98,5.15,1.42,.08,1,'#e5ebea','label');
    box(-11,1.66,4.64,.8,.08,.025,'#273f43','label');
    sign('IN TEM',-11,2.3,5.7,2.8,.55,'label');
    const products=['#e1b76a','#97b47e','#d9977b','#81adbf','#b099c4','#e8d7ac'];
    for(const [i,s] of SHOP_SHELVES.entries()) {
        box(s.x,.15,s.z,s.width,.3,s.depth,'#becebc','shopping');
        box(s.x,.95,s.z,.09,1.7,s.depth,'#d5e0cc','shopping');
        for(let level=0;level<3;level++) {
            const y=.35+level*.48;
            box(s.x,y,s.z,s.width,.08,s.depth,'#faf4df','shopping');
            for(const side of [-1,1])for(let j=0;j<5;j++) {
                box(s.x+side*.32,y+.2,s.z-1.2+j*.6,.3,.33,.28,products[(i+j+level)%6],'shopping');
                box(s.x+side*.5,y-.02,s.z-1.2+j*.6,.012,.09,.2,'#fffefa','shopping');
            }
        }
        box(s.x,1.82,s.z,s.width,.1,s.depth,s.color,'shopping');
        sign(String(i+1).padStart(2,'0')+' · KỆ HÀNG',s.x,1.57,s.z-1.57,1.12,.23,'shopping');
    }
    // Packing, side exit, and a longer illustrative road circuit; no destination building.
    box(8,1,4.1,1.7,.15,2.7,'#dcc29a','delivery');
    for(const dx of [-.65,.65])for(const dz of [-1.13,1.13])box(8+dx,.48,4.1+dz,.08,.98,.08,'#8e8598','delivery');
    box(8,1.37,4.05,.8,.61,.8,'#d1ad86','delivery');
    for(let i=0;i<3;i++)box(8,.3+i*.55,1.52,1.1,.52,.91,'#caa47c','delivery');
    sign('SOẠN · GIAO HÀNG',12,2.1,6.4,4.5,.65,'delivery');
    box(31,-.12,3,19,.18,27,'#d9e5d3','delivery');
    box(19.5,-.05,2,4,.14,3.6,'#adbabd','delivery');
    const roadSpur=[DELIVERY_PARKING,[ROAD_ENTRY[0]-1.75,ROAD_ENTRY[1]]];
    for(const points of [ROAD_POINTS,roadSpur]) {
        const closed=points[0][0]===points.at(-1)![0] && points[0][1]===points.at(-1)![1];
        const edges=[-1,1].map(side=>points.map((p,i)=>{
            const previous=points[i-1] || (closed?points.at(-2)!:p),next=points[i+1] || (closed?points[1]:p);
            const dx=next[0]-previous[0],dz=next[1]-previous[1],length=Math.hypot(dx,dz);
            return new B.Vector3(p[0]-side*dz/length*1.8,.042,p[1]+side*dx/length*1.8);
        }));
        const surface=B.MeshBuilder.CreateRibbon('delivery-road-'+serial++,{pathArray:edges,sideOrientation:B.Mesh.DOUBLESIDE},scene);
        surface.material=mat('#7b8d94');surface.metadata={zone:'delivery'};statics.push(surface);
        for(let i=1;i<points.length;i++) {
            const a=points[i-1],b=points[i],dx=b[0]-a[0],dz=b[1]-a[1],length=Math.hypot(dx,dz),angle=Math.atan2(dx,dz);
            for(let d=.6;d<length;d+=2.1) {
                const stripe=box(a[0]+dx*d/length,.051,a[1]+dz*d/length,.08,.015,Math.min(.7,length-d),'#eff0d7','delivery');stripe.rotation.y=angle;
            }
        }
    }
    box(31,.05,3,9,.025,16,'#bfd0b2','delivery');
    for(const z of [-2,8]) {
        box(31,.65,z,.19,1.3,.19,'#92775b','delivery');
        box(31,1.65,z,1.25,1.45,1.25,'#83a375','delivery');
        box(31,2.35,z,.85,.7,.85,'#95b480','delivery');
    }
    sign('ĐƯỜNG GIAO HÀNG',31,.75,3.5,6,.7,'delivery');
    box(6,.08,-8,3,.1,1.2,'#58907d');
    sign('LỐI VÀO / RA',6,.45,-8.5,3,.45,'pos');
    // Merge static meshes by material/zone. Actors have no per-mesh realtime shadow pass.
    const groups=new Map<string,B.Mesh[]>();
    for(const m of statics) { const key=m.material!.uniqueId+':'+m.metadata.zone;const g=groups.get(key)||[];g.push(m);groups.set(key,g); }
    for(const meshes of groups.values()) {
        const metadata=meshes[0].metadata;
        const merged=meshes.length>1?B.Mesh.MergeMeshes(meshes,true,true,undefined,false,false):meshes[0];
        if(merged){merged.metadata=metadata;merged.freezeWorldMatrix();}
    }
    const printSheet=box(-11,1.61,4.53,.65,.016,.75,'#fffef1','label'); printSheet.setEnabled(false);
    const parkedBike=createDeliveryMotorbike(scene,false);parkedBike.root.position.set(DELIVERY_PARKING[0],.05,DELIVERY_PARKING[1]);parkedBike.root.rotation.y=Math.PI/2;
    for(const m of parkedBike.root.getChildMeshes())m.metadata={zone:'delivery'};
    const printedTags=SHOP_SHELVES.map(s=>{const m=box(s.x+.61,.85,s.z-.61,.025,.16,.28,'#fff9bf','label');m.setEnabled(false);return m;});
    function buildCounters() {
        const key=world.checkouts.map((q:any)=>q.terminal).join('|');
        if(counters && key===counterKey)return; counterKey=key; counters?.dispose();
        counters=new B.TransformNode('checkout-fixtures',scene);
        const count=world.counterCount, width=Math.min(3.22,18/count);
        for(let i=0;i<count;i++) {
            const x=counterPoint(i,count)[0], name=world.checkouts[i]?.terminal || 'QUẦY '+String(i+1).padStart(2,'0');
            box(x,.65,-4.55,width,1.3,1.2,'#729f86','pos',counters);
            box(x,1.34,-4.55,width+.06,.12,1.3,'#f6f0e3','pos',counters);
            box(x+width*.27,1.77,-4.55,width*.22,.5,.1,'#35505b','pos',counters);
            box(x+width*.27,1.78,-4.61,width*.18,.4,.02,'#a4e0ca','pos',counters);
            box(x-width*.25,1.44,-4.55,width*.3,.12,.65,'#45655f','pos',counters);
            const s=sign(name,x,.73,-5.18,width*.85,.35,'pos');s.parent=counters;s.metadata={terminal:world.checkouts[i]?.terminal,zone:'pos'};
        }
    }
    const characterEngine={...B,MeshBuilder:{...B.MeshBuilder,
        CreateSphere:(name:string,o:any,s:any)=>B.MeshBuilder.CreateSphere(name,{...o,segments:8},s),
        CreateCapsule:(name:string,o:any,s:any)=>B.MeshBuilder.CreateCapsule(name,{...o,tessellation:8,capSubdivisions:3,subdivisions:1},s),
        CreateTorus:(name:string,o:any,s:any)=>B.MeshBuilder.CreateTorus(name,{...o,tessellation:12},s)}};
    function syncModels() {
        const all=[...world.employees.values(),...world.customers.values()];
        const ids=new Set(all.map(a=>a.id));
        for(const [id,c] of actors)if(!ids.has(id)){c.model.dispose();c.label.remove();actors.delete(id);}
        for(const a of all) {
            if(!actors.has(a.id)) {
                const model=createRetailCharacter(characterEngine,scene,{id:a.id,name:a.name,style:a.kind==='customer'?'customer':'cashier',
                    color:a.kind==='customer'?products[Number(a.id.split(':')[1])%6]:colors[a.module] || colors.idle,variant:Number(a.userId||world.customerId)%7});
                const label=document.createElement('button');label.type='button';label.className='wm3-actor-label';
                const name=document.createElement('b'),caption=document.createElement('small');label.append(name,caption);
                label.addEventListener('click',()=>options.onSelect(a.id));labels.append(label);
                const paper=B.MeshBuilder.CreateBox('paper-'+a.id,{width:.29,height:.38,depth:.013},scene);
                paper.parent=model.joints.leftWrist;paper.position.set(-.01,-.03,.13);paper.material=mat('#fffef1');paper.setEnabled(false);
                const bag=B.MeshBuilder.CreateBox('bag-'+a.id,{width:.35,height:.4,depth:.18},scene);
                bag.parent=model.joints.rightWrist;bag.position.set(0,-.25,.07);bag.material=mat('#e8caa1');bag.setEnabled(false);
                const basket=B.MeshBuilder.CreateBox('basket-'+a.id,{width:.48,height:.28,depth:.34},scene);
                basket.parent=model.joints.leftWrist;basket.position.set(0,-.18,.12);basket.material=mat('#77a28a');basket.setEnabled(false);
                actors.set(a.id,{model,label,name,caption,paper,bag,basket});
            }
        }
        for(const [id,b] of bikes)if(!world.trips.has(id)){
            for(const [actorId,c] of actors)if(c.model.root.parent===b.root){
                const a=world.employees.get(actorId);c.model.root.parent=null;
                if(a)c.model.root.position.set(a.position[0],.055,a.position[1]);
            }
            b.rider.dispose();b.root.dispose();b.label.remove();bikes.delete(id);
        }
        for(const [id,t] of world.trips)if(!bikes.has(id)){
            const b=createDeliveryMotorbike(scene,false);for(const m of b.root.getChildMeshes())m.metadata={tripId:id};
            const label=document.createElement('button');label.type='button';label.className='wm3-actor-label wm3-bike-label';
            label.dataset.tripId=String(id);
            const name=document.createElement('b'),phase=document.createElement('small'),caption=document.createElement('small');label.append(name,phase,caption);
            label.addEventListener('click',()=>options.onSelect(t.id));labels.append(label);
            const rider=createRetailCharacter(characterEngine,scene,{id:'trip-rider:'+id,style:'delivery',color:colors.delivery,variant:id%2});
            rider.root.parent=b.root;for(const m of rider.root.getChildMeshes())m.metadata={tripId:id};
            bikes.set(id,{...b,label,name,phase,caption,rider});
        }
    }
    function project(label:HTMLElement,position:number[],height:number) {
        const p=B.Vector3.Project(new B.Vector3(position[0],height,position[1]),B.Matrix.Identity(),scene.getTransformMatrix(),camera.viewport.toGlobal(canvas.clientWidth,canvas.clientHeight));
        const show=p.z>0 && p.z<1 && p.x>4 && p.x<canvas.clientWidth-4 && p.y>10 && p.y<canvas.clientHeight-4;
        label.hidden=!show;if(show){label.style.left=p.x+'px';label.style.top=p.y+'px';}
        return p;
    }
    function renderModels() {
        let printing=false;
        for(const a of [...world.employees.values(),...world.customers.values()]) {
            const c=actors.get(a.id);if(!c)continue;
            const trip=a.tripId?world.trips.get(a.tripId):null,bike=trip?bikes.get(trip.tripId):null;
            const mounted=bike && !trip.suppressed && ['mount','ride','dismount'].includes(a.action);
            const seatProgress=a.action==='mount'?(a.steps[0]?.action==='mount'?Math.min(1,a.elapsed/BOARDING_SECONDS):1):
                a.action==='dismount'?Math.max(0,1-a.elapsed/BOARDING_SECONDS):1;
            c.model.root.parent=mounted?bike.root:null;
            if(mounted){c.model.root.position.set(-.75*(1-seatProgress),RIDER_SEAT[1]*seatProgress,RIDER_SEAT[2]*seatProgress);c.model.root.rotation.y=0;}
            else {c.model.root.position.set(a.position[0],.055,a.position[1]);c.model.root.rotation.y=a.rotation;}
            c.model.setMotion({time:world.time,speed:a.speed?Math.min(1,a.speed/2):0,action:a.action,seatProgress,
                snap:!motion || !connected,basket:a.kind==='customer'&&!a.bag});
            c.model.setSelected(selected===a.id); c.paper.setEnabled(['paperWalk','label','stamp'].includes(a.action));
            c.bag.setEnabled(a.kind==='customer'&&a.bag);c.basket.setEnabled(a.kind==='customer'&&!a.bag);
            const isCustomer=a.kind==='customer';c.label.hidden=isCustomer && !a.name || !!mounted;
            if(!c.label.hidden) { c.name.textContent=a.name;c.caption.textContent=isCustomer?'Khách tại '+(a.terminal||'quầy'):a.caption;
                c.label.dataset.actorId=a.id;c.label.setAttribute('aria-label','Xem '+a.name);
                c.label.style.setProperty('--actor-color',colors[a.module] || colors.idle); project(c.label,a.position,isCustomer?2:2.13); }
            if(a.action==='print'&&a.sequence==='labels')printing=true;
            if(a.action==='label'){
                let closest=0;SHOP_SHELVES.forEach((s,i)=>{if(Math.hypot(s.x-a.position[0],s.z-a.position[1])<Math.hypot(SHOP_SHELVES[closest].x-a.position[0],SHOP_SHELVES[closest].z-a.position[1]))closest=i;});
                printedTags[closest].setEnabled(true);
            }
        }
        printSheet.setEnabled(printing);
        if(printing)printSheet.scaling.z=.4+(world.time%4)/4*.6;
        parkedBike.root.setEnabled(world.trips.size===0);
        const phases:any={'waiting-rider':'Chờ nhân viên lên xe',boarding:'Đang lên xe',outbound:'Đang giao trên đường',cruising:'Đang giao trên đường',
            returning:'Đang chạy về tiệm',dismounting:'Đang xuống xe',returned:'Đã về tiệm'};
        for(const [id,t] of world.trips){const b=bikes.get(id);if(b){
            b.root.setEnabled(!t.suppressed);b.label.hidden=!!t.suppressed;if(t.suppressed)continue;
            b.name.textContent=t.name;b.phase.textContent=phases[t.state] || 'Giao hàng';b.caption.textContent=t.caption;
            b.root.position.set(t.position[0],.05,t.position[1]);b.root.rotation.y=t.rotation;
            const employee=world.employees.get(t.courierId),hasEmployee=employee && !employee.exiting && employee.tripId===id;
            const showRider=!hasEmployee && (t.hasRider || t.state==='boarding');
            b.rider.root.setEnabled(showRider);
            if(showRider) {
                const progress=t.state==='boarding'?Math.min(1,t.elapsed/BOARDING_SECONDS):t.state==='dismounting'?Math.max(0,1-t.elapsed/BOARDING_SECONDS):1;
                b.rider.root.position.set(-.75*(1-progress),RIDER_SEAT[1]*progress,RIDER_SEAT[2]*progress);
                b.rider.setMotion({time:world.time,speed:0,action:'ride',seatProgress:progress,snap:!motion || !connected});
            }
            if(motion&&connected)b.updateWheel(t.speed/30);project(b.label,t.position,2.25);
        }}
        // Keep projected employee names apart. If crowded, all names remain accessible in the side list.
        const placed:any[]=[...bikes.values()].filter(b=>!b.label.hidden).map(b=>b.label.getBoundingClientRect());
        for(const c of actors.values())if(!c.label.hidden){
            const r=c.label.getBoundingClientRect();
            const bounds=labels.getBoundingClientRect(),scale=bounds.width/canvas.clientWidth || 1;
            let shift=0;
            const overlaps=(dy:number)=>placed.some(p=>r.left<p.right&&r.right>p.left&&r.top+dy<p.bottom&&r.bottom+dy>p.top);
            if(overlaps(0))for(let amount=26*scale;amount<=260*scale;amount+=26*scale) {
                const candidate=[amount,-amount].find(dy=>r.top+dy>=bounds.top+58*scale &&
                    r.bottom+dy<=bounds.bottom-30*scale && !overlaps(dy));
                if(candidate!==undefined){shift=candidate;break;}
            }
            if(shift)c.label.style.top=(parseFloat(c.label.style.top)+shift/scale)+'px';
            placed.push({left:r.left,right:r.right,top:r.top+shift,bottom:r.bottom+shift});
        }
    }
    scene.onPointerObservable.add(info=>{
        if(info.type!==B.PointerEventTypes.POINTERTAP || !info.pickInfo?.hit)return;
        const m=info.pickInfo.pickedMesh?.metadata;
        if(m?.actorId) options.onSelect(m.actorId);
        else if(m?.tripId)options.onSelect('trip:'+m.tripId);
        else if(m?.terminal)options.onSelect('terminal:'+m.terminal);
        else if(m?.zone)options.onSelect('zone:'+m.zone);
    });
    const views:any={overview:[12,.3,5,58],shopping:[-1,.5,2.6,28],pos:[2,.7,-4.3,29],receipt:[-6.1,.7,13,17],
        warehouse:[2,.7,13,21],label:[-10.5,.7,5,12],review:[13,.7,14,15],delivery:[27,.4,3,38]};
    function setView(view:string){const v=views[view]||views.overview;camera.setTarget(new B.Vector3(v[0],v[1],v[2]));camera.radius=v[3];camera.alpha=-1.12;camera.beta=.73;dirty=true;}
    function tick(now:number) {
        if(disposed)return;
        if(document.hidden || !visible)return;
        if(now-lastFrame<1000/30)return;lastFrame=now;
        const dt=Math.min(.1,(now-(lastTick||now))/1000);lastTick=now;
        if((!connected||!motion) && !dirty && now-interacted>900)return;
        if(connected&&motion)world.advance(dt);
        scene.updateTransformMatrix(true);syncModels();renderModels();scene.render();dirty=false;
        frames++;if(now-fpsAt>1000){options.onFps?.(Math.round(frames*1000/(now-fpsAt)));fpsAt=now;frames=0;}
    }
    // Explicit 30 FPS cap. Hidden tabs/2D stop the engine loop completely.
    const loop=()=>tick(performance.now());
    const interaction=()=>{interacted=performance.now();dirty=true;};
    ['pointerdown','pointermove','wheel','keydown'].forEach(name=>canvas.addEventListener(name,interaction,{passive:true}));
    function schedule(){engine.stopRenderLoop();lastTick=0;if(!disposed && visible && !document.hidden)engine.runRenderLoop(loop);}
    const visibility=()=>schedule();document.addEventListener('visibilitychange',visibility);
    const resize=new ResizeObserver(()=>{if(!disposed){engine.resize();dirty=true;}});resize.observe(canvas.parentElement!);
    const lost=(e:Event)=>{e.preventDefault();engine.stopRenderLoop();options.onError('Mất khả năng hiển thị 3D. Đã chuyển về 2D; có thể bấm mở lại 3D.');};
    canvas.addEventListener('webglcontextlost',lost);
    buildCounters();schedule();
    return {
        apply(snapshot:any){world.apply(snapshot);if(!motion)world.settle();buildCounters();syncModels();dirty=true;},
        status(state:any){connected=!!state.connected;motion=!!state.motion;if(!motion)world.settle();scene.clearColor=B.Color4.FromHexString(state.theme==='dark'?'#142b35ff':'#edf3f5ff');dirty=true;},
        select(id:string){selected=id;dirty=true;},setView,
        focus(id:string){const a=world.employees.get(id)||world.customers.get(id)||[...world.trips.values()].find(t=>t.id===id);if(a){camera.setTarget(new B.Vector3(a.position[0],.8,a.position[1]));camera.radius=10;dirty=true;}},
        zoom(delta:number){camera.radius=Math.max(7,Math.min(95,camera.radius+delta));dirty=true;},
        visible(show:boolean){visible=show;schedule();if(show){engine.resize();dirty=true;}},
        clear(){world.clear();world.counterCount=3;syncModels();buildCounters();selected=null;printSheet.setEnabled(false);
            printedTags.forEach(tag=>tag.setEnabled(false));scene.render();engine.clear(scene.clearColor,true,true,true);dirty=true;},world,
        inspectDelivery(){return [...world.trips.values()].map(t=>{
            const b=bikes.get(t.tripId),actor=t.courierId?actors.get(t.courierId):null;
            return {tripId:t.tripId,state:t.state,visible:!t.suppressed,employeeAttached:!!b && actor?.model.root.parent===b.root,
                employeeDisposed:actor?.model.root.isDisposed() ?? null,
                knee:actor?.model.root.parent===b?.root?actor.model.joints.leftKnee.rotation.x:b?.rider.joints.leftKnee.rotation.x,
                illustratedRider:!!b?.rider.root.isEnabled(),riderSeat:actor?.model.root.parent===b?.root?actor.model.root.position.asArray():null};
        });},
        dispose(){disposed=true;engine.stopRenderLoop();resize.disconnect();document.removeEventListener('visibilitychange',visibility);
            ['pointerdown','pointermove','wheel','keydown'].forEach(name=>canvas.removeEventListener(name,interaction));
            canvas.removeEventListener('webglcontextlost',lost);for(const a of actors.values())a.label.remove();for(const b of bikes.values())b.label.remove();scene.dispose();engine.dispose();}
    };
}
