import { pathLength } from './store-layout.ts';

export const DELIVERY_DOOR=[16.5,2];
export const DELIVERY_PARKING=[19,2];
export const BOARDING_SECONDS=2;
export const ROAD_SPEED=2.3;
export const RIDER_SEAT=[0,.27,-.2];

// A rounded road circuit outside the store. A lap takes about 30 seconds and never confirms delivery.
const arc=(x,z,start)=>Array.from({length:8},(_,i)=>{
    const angle=start+(i+1)*Math.PI/16;
    return [x+2*Math.cos(angle),z+2*Math.sin(angle)];
});
export const ROAD_POINTS=[
    [24,2],[24,-6],...arc(26,-6,Math.PI),[36,-8],...arc(36,-6,Math.PI*1.5),
    [38,12],...arc(36,12,0),[26,14],...arc(26,12,Math.PI*.5),[24,2],
];
export const ROAD_LENGTH=pathLength(ROAD_POINTS);
export const ROAD_ENTRY=ROAD_POINTS[0];
export const ROAD_CONNECTOR=[DELIVERY_PARKING,ROAD_ENTRY];

export function besideBike(trip) {
    return [trip.position[0]-.75*Math.cos(trip.rotation),trip.position[1]+.75*Math.sin(trip.rotation)];
}

// Stay on the circuit to its store junction, then take the spur home; no diagonal shortcut through grass/store.
export function returnRoad(trip) {
    if(trip.state!=='cruising' || trip.roadDistance<.01)return [[...trip.position],[...DELIVERY_PARKING]];
    let travelled=0;
    const rest=[];
    for(let i=1;i<ROAD_POINTS.length;i++) {
        travelled+=Math.hypot(ROAD_POINTS[i][0]-ROAD_POINTS[i-1][0],ROAD_POINTS[i][1]-ROAD_POINTS[i-1][1]);
        if(travelled>trip.roadDistance+.001)rest.push([...ROAD_POINTS[i]]);
    }
    return [[...trip.position],...rest,[...DELIVERY_PARKING]];
}
