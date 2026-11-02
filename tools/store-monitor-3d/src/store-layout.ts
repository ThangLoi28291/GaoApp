/** Shared R05 fixture dimensions and walkable routes. Coordinates are illustrative. */
export type Point = [number, number];
export interface Fixture {id:string;x:number;z:number;width:number;depth:number}
export const COUNTERS = [
 {id:"q01",name:"Quầy 01",actorId:"nv01",x:-6.5},
 {id:"q02",name:"Quầy 02",actorId:"nv05",x:-2.7},
 {id:"q03",name:"Quầy 03",actorId:"nv06",x:1.1},
];
export const SHOP_SHELVES = [
 {id:"a01",name:"Gạo & mì",sign:"GẠO & MÌ",x:-6.7,z:-.2,width:1.18,depth:3.10,color:"#e1b460"},
 {id:"a02",name:"Sữa & đồ uống",sign:"SỮA & ĐỒ UỐNG",x:-3.8,z:-.2,width:1.18,depth:3.10,color:"#79a6ca"},
 {id:"a03",name:"Bánh & ngũ cốc",sign:"BÁNH & NGŨ CỐC",x:-.9,z:-.2,width:1.18,depth:3.10,color:"#cf8868"},
 {id:"a04",name:"Gia vị",sign:"GIA VỊ",x:2,z:-.2,width:1.18,depth:3.10,color:"#8bac74"},
 {id:"a05",name:"Chăm sóc nhà",sign:"CHĂM SÓC NHÀ",x:4.9,z:-.2,width:1.18,depth:3.10,color:"#b58bc4"},
 {id:"b01",name:"Trà & cà phê",sign:"TRÀ & CÀ PHÊ",x:-6.7,z:5,width:1.18,depth:3.10,color:"#a3b467"},
 {id:"b02",name:"Đồ hộp",sign:"ĐỒ HỘP",x:-3.8,z:5,width:1.18,depth:3.10,color:"#75aaba"},
 {id:"b03",name:"Thực phẩm khô",sign:"THỰC PHẨM KHÔ",x:-.9,z:5,width:1.18,depth:3.10,color:"#d7a566"},
 {id:"b04",name:"Chăm sóc cá nhân",sign:"CÁ NHÂN",x:2,z:5,width:1.18,depth:3.10,color:"#aa99bf"},
 {id:"b05",name:"Giấy & gia dụng",sign:"GIẤY & GIA DỤNG",x:4.9,z:5,width:1.18,depth:3.10,color:"#83b399"},
];
export const WAREHOUSE_RACKS:Fixture[]=[
 {id:"k01",x:-1.6,z:13,width:1.5,depth:4},
 {id:"k02",x:2.1,z:13,width:1.5,depth:4},
 {id:"k03",x:5.8,z:13,width:1.5,depth:4},
];
export const STATIONS={
 receive:{name:"Khu nhận hàng",point:[-6.1,12.9] as Point,facing:0},
 office:{name:"Văn phòng duyệt",point:[13.4,14.05] as Point,facing:0},
 print:{name:"Bàn in tem",point:[-11,4] as Point,facing:0},
 counter:{name:"Gần quầy tính tiền",point:[-6.5,-2.65] as Point,facing:Math.PI},
 floor:{name:"Giữa hai hàng kệ",point:[3.06,2.35] as Point,facing:Math.PI/2},
 warehouse:{name:"Trong kho hàng",point:[.25,12.2] as Point,facing:0},
};
export type StationId=keyof typeof STATIONS;
export const STORE_FIXTURES:Fixture[]=[
 ...SHOP_SHELVES,...WAREHOUSE_RACKS,
 ...COUNTERS.map(q=>({id:q.id,x:q.x,z:-4.55,width:3.22,depth:1.27})),
 {id:"receive-table",x:-6.1,z:14.6,width:3,depth:2.3},
 {id:"print-table",x:-11,z:5.15,width:3,depth:1.4},
 {id:"office-desk",x:13.4,z:15.7,width:3,depth:1.3},
 {id:"office-west",x:9.5,z:14.25,width:.16,depth:7.9},
 {id:"office-front-left",x:10.65,z:10.3,width:2.45,depth:.16},
 {id:"office-front-right",x:15.95,z:10.3,width:2.9,depth:.16},
 {id:"packing-table",x:8,z:4.1,width:1.7,depth:2.7},
 {id:"packing-boxes",x:8,z:1.52,width:1.1,depth:.91},
];
const clearance=.28,edgeGap=.025;
let obstacles=STORE_FIXTURES.map(f=>({minX:f.x-f.width/2-clearance,maxX:f.x+f.width/2+clearance,minZ:f.z-f.depth/2-clearance,maxZ:f.z+f.depth/2+clearance}));
const distance=(a:Point,b:Point)=>Math.hypot(a[0]-b[0],a[1]-b[1]);
function inBounds(p:Point){return p[0]>-13.0&&p[0]<17.2&&p[1]>-8.95&&p[1]<18.05;}
function occupied(p:Point){return obstacles.some(r=>p[0]>r.minX&&p[0]<r.maxX&&p[1]>r.minZ&&p[1]<r.maxZ);}
export function segmentClear(a:Point,b:Point):boolean{
 return !obstacles.some(r=>{
  let lo=0,hi=1;
  for(const [origin,delta,min,max] of [[a[0],b[0]-a[0],r.minX+1e-7,r.maxX-1e-7],[a[1],b[1]-a[1],r.minZ+1e-7,r.maxZ-1e-7]]){
   if(Math.abs(delta)<1e-9){if(origin<=min||origin>=max)return false;}
   else{const t1=(min-origin)/delta,t2=(max-origin)/delta;lo=Math.max(lo,Math.min(t1,t2));hi=Math.min(hi,Math.max(t1,t2));if(lo>=hi)return false;}
  }
  return lo<hi;
 });
}
let corners:Point[]=obstacles.flatMap(r=>([
 [r.minX-edgeGap,r.minZ-edgeGap],[r.minX-edgeGap,r.maxZ+edgeGap],
 [r.maxX+edgeGap,r.minZ-edgeGap],[r.maxX+edgeGap,r.maxZ+edgeGap],
] as Point[])).filter(p=>inBounds(p)&&!occupied(p));
let cornerEdges=corners.map((a,i)=>corners.flatMap((b,j)=>i!==j&&segmentClear(a,b)?[{to:j+2,length:distance(a,b)}]:[]));

/** Visibility graph around inflated fixture corners; always starts at the live position. */
export function planRoute(from:Point,to:Point):Point[]{
 if(!from.every(Number.isFinite)||!to.every(Number.isFinite)||occupied(from)||occupied(to))throw new Error("Vị trí nằm trong vật cản.");
 if(distance(from,to)<.001)return [[...from],[...to]];
 if(segmentClear(from,to))return [[...from],[...to]];
 const points:Point[]=[[...from],[...to],...corners],edges=points.map((_,i)=>i<2?[]:cornerEdges[i-2].slice());
 for(let i=0;i<2;i++)for(let j=2;j<points.length;j++)if(segmentClear(points[i],points[j])){const length=distance(points[i],points[j]);edges[i].push({to:j,length});edges[j].push({to:i,length});}
 const cost=points.map(()=>Infinity),previous=points.map(()=>-1),visited=new Set<number>();cost[0]=0;
 while(visited.size<points.length){
  let best=-1;for(let i=0;i<points.length;i++)if(!visited.has(i)&&(best<0||cost[i]<cost[best]))best=i;
  if(best<0||!Number.isFinite(cost[best]))break;if(best===1)break;visited.add(best);
  for(const edge of edges[best])if(cost[best]+edge.length<cost[edge.to]){cost[edge.to]=cost[best]+edge.length;previous[edge.to]=best;}
 }
 if(!Number.isFinite(cost[1]))throw new Error("Chưa tìm được đường đi trong cửa hàng.");
 const route:Point[]=[];for(let node=1;node!==-1;node=previous[node])route.unshift([...points[node]]);
 return route;
}
export function pathLength(path:Point[]){return path.slice(1).reduce((sum,p,i)=>sum+distance(p,path[i]),0);}
export function poseAlong(path:Point[],progress:number):{position:Point;rotation:number}{
 let remaining=pathLength(path)*Math.max(0,Math.min(1,progress));
 for(let i=1;i<path.length;i++){
  const a=path[i-1],b=path[i],length=distance(a,b);
  if(remaining<=length||i===path.length-1){const t=length>.0001?Math.min(1,remaining/length):1;return {position:[a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t],rotation:Math.atan2(b[0]-a[0],b[1]-a[1])};}
  remaining-=length;
 }
 return {position:[...path[0]],rotation:0};
}
export function shelfVisitPoint(id:string):Point{const s=SHOP_SHELVES.find(s=>s.id===id);if(!s)throw new Error("Kệ không hợp lệ.");return [s.x+1.06,s.z-.61];}
export function positionName(p:Point){
 if(p[0]>9.4&&p[1]>10.2)return "Văn phòng";
 if(p[1]>11&&p[0]>4)return "Khu nhận hàng";
 if(p[1]>8.4)return "Kho hàng";
 if(p[0]>10)return "Khu in tem";
 if(p[1]<-2.2)return "Khu quầy tính tiền";
 return p[1]>2?"Hàng kệ phía sau":"Hàng kệ phía trước";
}

/** Rebuild only when the actual terminal count changes; routes still start at the live position. */
export function counterPoint(index:number,count=3):Point{return [-7+index*20/Math.max(2,count-1),-3.2];}
export function configureCounters(count:number){
 const total=Math.max(3,count),width=Math.min(3.22,18/total);
 const fixtures=STORE_FIXTURES.filter(f=>!COUNTERS.some(q=>q.id===f.id));
 for(let i=0;i<total;i++)fixtures.push({id:'live-counter-'+i,x:counterPoint(i,total)[0],z:-4.55,width,depth:1.27});
 obstacles=fixtures.map(f=>({minX:f.x-f.width/2-clearance,maxX:f.x+f.width/2+clearance,minZ:f.z-f.depth/2-clearance,maxZ:f.z+f.depth/2+clearance}));
 corners=obstacles.flatMap(r=>([[r.minX-edgeGap,r.minZ-edgeGap],[r.minX-edgeGap,r.maxZ+edgeGap],[r.maxX+edgeGap,r.minZ-edgeGap],[r.maxX+edgeGap,r.maxZ+edgeGap]] as Point[])).filter(p=>inBounds(p)&&!occupied(p));
 cornerEdges=corners.map((a,i)=>corners.flatMap((b,j)=>i!==j&&segmentClear(a,b)?[{to:j+2,length:distance(a,b)}]:[]));
}
