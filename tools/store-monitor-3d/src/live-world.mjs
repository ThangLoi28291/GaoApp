import { planRoute, pathLength, poseAlong, SHOP_SHELVES, STATIONS, counterPoint, configureCounters, shelfVisitPoint } from './store-layout.ts';
import { DELIVERY_DOOR, DELIVERY_PARKING, BOARDING_SECONDS, ROAD_SPEED, ROAD_POINTS, ROAD_LENGTH, ROAD_ENTRY, ROAD_CONNECTOR, besideBike, returnRoad } from './delivery-road.mjs';

export const moduleNames = { pos: 'Tính tiền', receipt: 'Nhập hàng', warehouse: 'Kiểm kho', label: 'In tem', review: 'Duyệt phiếu', delivery: 'Giao hàng', idle: 'Đi trong tiệm' };
const date = value => value ? new Date(value).getTime() : 0;
const distance = (a,b) => Math.hypot(a[0]-b[0],a[1]-b[1]);
const entrance = [6,-8.5];
const WALK = 2.1;
function entity(id, kind, position) {
    return { id, kind, position: [...position], rotation: 0, action: 'idle', speed: 0, route: null, steps: [], elapsed: 0,
        name: '', caption: '', module: 'idle', exiting: false, token: null, work: null };
}
function move(point, action = 'walk') { return { point, action }; }
function act(action, duration, caption) { return { action, duration, caption }; }

/** Read-only scene state. Animation never writes or confirms any business document. */
export class LiveStoreWorld {
    constructor() {
        this.employees = new Map(); this.customers = new Map(); this.parkedCustomers = new Map(); this.trips = new Map(); this.checkouts = [];
        this.seen = new Set(); this.scans = new Map(); this.started = null; this.initialized = false; this.time = 0; this.serverNow = 0;
        this.customerId = 0; this.counterCount = 3; this.shelfCursor = 0; this.input = null; this.lastArrival = 0;
        configureCounters(3);
        for(let i=0;i<6;i++) this.addShopper(i % SHOP_SHELVES.length, true);
    }
    addShopper(index, initial = false) {
        const p = initial ? shelfVisitPoint(SHOP_SHELVES[index].id) : entrance;
        const c = entity('customer:' + (++this.customerId), 'customer', p);
        c.orderId = null; c.phase = 'shopping'; c.shelfIndex = index; c.bag = false;
        this.customers.set(c.id,c); this.browse(c); return c;
    }
    browse(c) {
        c.name = ''; c.module = 'idle'; c.phase = c.orderId ? 'held' : 'shopping'; c.bag = false;
        c.shelfIndex = (c.shelfIndex + 3) % SHOP_SHELVES.length;
        this.assign(c,[move(shelfVisitPoint(SHOP_SHELVES[c.shelfIndex].id)),act('pick',5 + c.shelfIndex % 4)]);
    }
    assign(a, steps) { a.steps = steps; a.route = null; a.elapsed = 0; a.speed = 0; }
    wander(a) {
        a.module = 'idle'; a.caption = moduleNames.idle; a.work = null; a.token = null;
        const shelf = SHOP_SHELVES[(++this.shelfCursor + Number(a.userId || 0)) % SHOP_SHELVES.length];
        this.assign(a,[move(shelfVisitPoint(shelf.id)), act('idle',4)]);
    }
    counter(terminal) {
        const i = Math.max(0,this.checkouts.findIndex(q=>q.terminal===terminal));
        return counterPoint(i,this.counterCount);
    }
    currentCart(work) {
        const order=/^order:(\d+)$/.exec(work.workKey || '')?.[1];
        return this.checkouts.find(q=>q.terminal===work.terminal && q.state==='Draft' && q.lines?.length &&
            (!order || q.orderId===Number(order)));
    }
    lastScan(terminal) {
        return this.scans.get(terminal) || null;
    }
    workPoint(work) {
        if(work.module==='pos') return this.counter(work.terminal);
        return ({ receipt: STATIONS.receive.point, warehouse: STATIONS.warehouse.point, label: STATIONS.print.point,
            review: STATIONS.office.point, delivery: [9.5,4] })[work.module] || STATIONS.floor.point;
    }
    work(a, work, animate) {
        a.lastWork=work;
        const token = work.module + ':' + (work.workKey || work.terminal || '');
        const special = animate && (work.action === 'LabelPrinting.Print' || work.action === 'StockDocuments.SubmitApproval');
        if(a.token===token && !animate) {
            a.work=work;
            const step=a.steps.at(-1);
            if(work.module==='pos' && step?.action==='scan')step.duration=this.currentCart(work)?Infinity:15;
            return;
        }
        a.tripId = null;
        a.token = token; a.work = work; a.module = work.module; a.caption = moduleNames[work.module];
        const target = this.workPoint(work);
        if(special && work.action==='LabelPrinting.Print') {
            a.sequence = 'labels';
            this.assign(a,[move(STATIONS.print.point),act('print',4,'Đang in tem'),
                move(shelfVisitPoint(SHOP_SHELVES[(++this.shelfCursor)%10].id),'paperWalk'),act('label',3,'Đang dán tem lên kệ')]);
        } else if(special) {
            a.module = 'receipt'; a.sequence = 'putaway';
            this.assign(a,[move(STATIONS.receive.point),act('pick',2,'Lấy hàng đã gửi duyệt'),
                move(shelfVisitPoint(SHOP_SHELVES[(++this.shelfCursor)%10].id),'carry'),act('handOver',3,'Đặt hàng lên kệ ngoài tiệm')]);
        } else {
            a.sequence = null;
            const action = ({pos:'scan',receipt:'scan',warehouse:'count',label:'print',review:'office',delivery:'pack'})[work.module] || 'idle';
            this.assign(a,[move(target),act(action,work.module==='pos' && this.currentCart(work)?Infinity:15,moduleNames[work.module])]);
        }
    }
    apply(snapshot) {
        const now = date(snapshot.serverTimeUtc) || Date.now();
        const fresh = !this.initialized || this.started !== snapshot.startedAtUtc;
        if(fresh) {
            this.seen.clear();
            if(this.initialized) { this.clear(); for(let i=0;i<6;i++)this.addShopper(i,true); }
        }
        this.started = snapshot.startedAtUtc; this.serverNow = now; this.inputTime=this.time; this.input = snapshot;
        this.checkouts = snapshot.checkouts || [];
        const count = Math.max(3,this.checkouts.length);
        if(count!==this.counterCount) { this.counterCount=count; configureCounters(count); }
        const online = snapshot.employees || snapshot.people || [];
        const ids = new Set(online.map(p=>Number(p.userId)));
        for(const a of this.employees.values()) {
            if(!ids.has(a.userId) && !a.exiting) {
                const alreadyOutside=a.tripId && ['ride','mount','dismount'].includes(a.action);
                a.exiting=true; a.caption='Rời cửa hàng'; a.sequence=null; a.tripId=null;
                this.assign(a,alreadyOutside?[]:[move(entrance)]);
            }
        }
        for(const p of online) {
            const id='employee:'+p.userId;
            let a = this.employees.get(id);
            if(!a) { a=entity(id,'employee',entrance); a.userId=Number(p.userId); a.caption='Vừa đăng nhập'; this.employees.set(id,a); this.wander(a); }
            a.name=p.person; if(a.exiting) { a.exiting=false; this.wander(a); }
        }
        const events=[...(snapshot.events||[])].sort((a,b)=>date(a.occurredAtUtc)-date(b.occurredAtUtc)||a.id-b.id);
        this.scans.clear();
        for(const q of this.checkouts) {
            if(!q.orderId || q.state!=='Draft')continue;
            const e=events.filter(e=>e.module==='pos' && e.posScan && e.terminal===q.terminal && e.workKey==='order:'+q.orderId &&
                q.lines?.some(l=>l.lineId===e.posScan.lineId)).at(-1);
            if(e)this.scans.set(q.terminal,e);
        }
        const newest = new Map();
        for(const p of snapshot.people || []) {
            if(!p.editedAtUtc) continue;
            const w={...p,at:date(p.editedAtUtc)};
            if(w.at>(newest.get(Number(p.userId))?.at || 0)) newest.set(Number(p.userId),w);
        }
        for(const e of events) {
            const w={...e,at:date(e.occurredAtUtc),isEvent:true};
            if(w.at>=(newest.get(Number(e.userId))?.at||0)) newest.set(Number(e.userId),w);
        }
        for(const a of this.employees.values()) {
            if(a.exiting) continue;
            const latest=newest.get(a.userId);
            if(latest && (!a.lastWork || latest.at>=a.lastWork.at))a.lastWork=latest;
            // A real current cart keeps its latest cashier at the station between scans.
            // Choose the latest interaction before checking age so an older POS tab cannot win over a newer job.
            const w=latest && (now-latest.at<=15000 || latest.module==='pos' && this.currentCart(latest)) ? latest : null;
            const committed=events.filter(e=>Number(e.userId)===a.userId && !fresh && !this.seen.has(String(e.id)) &&
                now-date(e.occurredAtUtc)<15000).at(-1);
            if(committed && (!w || w.module===committed.module || w.at<=date(committed.occurredAtUtc))) {
                this.work(a,{...committed,at:date(committed.occurredAtUtc),isEvent:true},true);
                continue;
            }
            if(w) {
                // Mere navigation/heartbeats do not replace work. Only a later interaction/save does.
                if(!a.work || w.at>=a.work.at) this.work(a,w,!!w.isEvent && !fresh && !this.seen.has(String(w.id)));
            } else if(!a.sequence && a.module!=='idle') this.wander(a);
        }
        // Apply all newly committed POS transitions in chronological order before assigning current carts.
        for(const e of events) {
            if(!fresh && !this.seen.has(String(e.id)) && now-date(e.occurredAtUtc)<15000) this.posEvent(e);
            this.seen.add(String(e.id));
        }
        if(this.seen.size>1000) this.seen=new Set(events.map(e=>String(e.id)));
        for(const q of this.checkouts) {
            if(!q.orderId || !q.lines?.length || q.state!=='Draft') continue;
            let c=[...this.customers.values()].find(c=>c.orderId===q.orderId && !c.exiting);
            if(!c && this.parkedCustomers.has(q.orderId)) {
                c=this.parkedCustomers.get(q.orderId);this.parkedCustomers.delete(q.orderId);
                c.exiting=false;c.retiring=false;c.position=[...entrance];this.customers.set(c.id,c);
            }
            if(!c) {
                c=[...this.customers.values()].find(c=>!c.orderId && !c.exiting) || this.addShopper(0);
                c.orderId=q.orderId;
            }
            c.name=q.customerName || ''; c.terminal=q.terminal;
            if(c.phase!=='checkout' || c.checkoutTerminal!==q.terminal) {
                c.phase='checkout'; c.checkoutTerminal=q.terminal; const pos=this.counter(q.terminal);
                this.assign(c,[move([pos[0],-5.85]),act('idle',Infinity)]);
            }
        }
        const activeOrders=new Set(this.checkouts.filter(q=>q.state==='Draft' && q.lines?.length).map(q=>q.orderId));
        for(const c of this.customers.values())if(c.phase==='checkout' && !c.exiting && !activeOrders.has(c.orderId))this.browse(c);
        this.syncTrips(snapshot.delivery?.orders || []);
        this.initialized = true;
    }
    posEvent(e) {
        if(e.module!=='pos') return;
        const order = /^order:(\d+)$/.exec(e.workKey || '')?.[1];
        const c=[...this.customers.values()].find(c=>order ? c.orderId===Number(order) : c.checkoutTerminal===e.terminal && c.phase==='checkout');
        if(!c) return;
        if(/\.(HoldOrder|HoldCurrentCart)$/.test(e.action||'')) this.browse(c);
        if(/\.(Cancel|CancelCurrentCart|VoidOrder)$/.test(e.action||'')) this.exitCustomer(c,false);
        if(/\.(Finalize|FinalizeCurrentCart|FinalizeCredit)$/.test(e.action||'') ||
            (e.action==='POS.AddPaymentAndMaybeFinalizeCurrentCart' && e.text?.includes('chốt'))) this.exitCustomer(c,true);
    }
    exitCustomer(c, paid) {
        c.exiting=true; c.phase=paid?'paid':'cancelled'; c.bag=false; c.name='';
        const point=this.counter(c.checkoutTerminal);
        this.assign(c,paid?[move([point[0],-5.85]),act('handOver',2),move(entrance)]:[move(entrance)]);
    }
    bindCourier(trip, actor) {
        if(actor.tripId===trip.tripId)return;
        actor.tripId=trip.tripId;actor.module='delivery';actor.sequence='delivery';trip.courierId=actor.id;
        if(trip.state==='waiting-rider' || trip.state==='boarding') {
            actor.caption='Đi đến xe giao hàng';
            this.assign(actor,[move(DELIVERY_DOOR),move(besideBike(trip)),act('mount',BOARDING_SECONDS,'Đang lên xe'),act('ride',Infinity)]);
            trip.state='waiting-rider';trip.hasRider=false;
        } else {
            actor.position=[...trip.position];actor.rotation=trip.rotation;actor.action='ride';actor.caption='Đang giao trên đường';
            this.assign(actor,[act('ride',Infinity)]);
            if(trip.state==='dismounting')this.dismount(trip,actor);
        }
    }
    dismount(trip, actor) {
        if(actor?.tripId===trip.tripId) {
            actor.caption='Đang xuống xe';
            this.assign(actor,[act('dismount',BOARDING_SECONDS)]);
            actor.action='dismount';
        }
        trip.state='dismounting';trip.elapsed=0;trip.speed=0;
    }
    finishReturn(trip, actor) {
        trip.state='returned';trip.hasRider=false;trip.speed=0;
        if(actor?.tripId===trip.tripId) {
            actor.position=besideBike(trip);actor.tripId=null;actor.sequence='return';actor.action='walk';actor.caption='Đi vào cửa hàng';
            this.assign(actor,[move(DELIVERY_DOOR),move([12,2.35])]);
        }
    }
    syncTrips(orders) {
        const groups=new Map(),latestTrips=new Map();
        for(const o of orders)if(o.tripId) {
            if(!groups.has(o.tripId))groups.set(o.tripId,[]);
            groups.get(o.tripId).push(o);
            if(o.departedAtUtc && o.courierUserId) {
                const previous=latestTrips.get(o.courierUserId);
                if(!previous || date(o.departedAtUtc)>previous.at || date(o.departedAtUtc)===previous.at && o.tripId>previous.id)
                    latestTrips.set(o.courierUserId,{id:o.tripId,at:date(o.departedAtUtc)});
            }
        }
        for(const [id,items] of groups) {
            const departed=items.find(o=>o.departedAtUtc);if(!departed)continue;
            // All orders must have saved outcomes, or the trip must have a saved return. Animation never writes these facts.
            const finished=items.some(o=>o.returnedAtUtc) || items.every(o=>['AwaitingReconciliation','ReadyToSettle','Settled','Cancelled'].includes(o.state));
            const user=items.find(o=>o.courierUserId)?.courierUserId;
            const actor=user?this.employees.get('employee:'+user):null;
            const courier=items.find(o=>o.courier)?.courier || 'Nhân viên giao hàng';
            let trip=this.trips.get(id);
            if(!trip) {
                trip=entity('trip:'+id,'bike',DELIVERY_PARKING);trip.tripId=id;trip.rotation=Math.PI/2;
                trip.state=finished?'returned':'waiting-rider';trip.hasRider=false;trip.roadDistance=0;trip.routeDistance=0;
                this.trips.set(id,trip);
            }
            const changed=trip.courierUserId!==undefined && trip.courierUserId!==user;
            if(changed && trip.courierId) {
                const previous=this.employees.get(trip.courierId);
                if(previous?.tripId===id){previous.tripId=null;previous.sequence=null;this.wander(previous);}
                trip.courierId=null;
            }
            trip.courierUserId=user;trip.name=courier;trip.caption=departed.tripCode || 'Chuyến giao';
            const lastWork=actor?.lastWork || actor?.work;
            trip.suppressed=!!(lastWork && lastWork.module!=='delivery' && lastWork.at>date(departed.departedAtUtc)) ||
                !!(user && latestTrips.get(user)?.id!==id);
            if(trip.suppressed) {
                trip.speed=0;
                if(actor?.tripId===id){actor.tripId=null;actor.sequence=null;this.wander(actor);}
                continue;
            }
            // An employee already on the road attaches to the same bike; an approaching employee must mount first.
            if(actor && !actor.exiting && trip.state!=='returned')this.bindCourier(trip,actor);
            if((!actor || actor.exiting) && trip.courierId)trip.courierId=null;
            if(finished && !['returned','returning','dismounting'].includes(trip.state)) {
                if(['waiting-rider','boarding'].includes(trip.state)) {
                    if(actor?.tripId===id && ['mount','ride'].includes(actor.action))this.dismount(trip,actor);
                    else this.finishReturn(trip,actor);
                } else {
                    trip.roadRoute=returnRoad(trip);trip.routeDistance=0;trip.state='returning';
                }
            }
        }
        for(const [id,trip] of this.trips)if(!groups.has(id)) {
            const actor=this.employees.get(trip.courierId);
            if(actor?.tripId===id){actor.tripId=null;actor.sequence=null;this.wander(actor);}
            this.trips.delete(id);
        }
    }
    advanceTrips(dt) {
        for(const trip of this.trips.values()) {
            const actor=this.employees.get(trip.courierId);
            const rider=actor && !actor.exiting && actor.tripId===trip.tripId ? actor : null;
            trip.speed=0;if(trip.suppressed)continue;
            if(['waiting-rider','boarding'].includes(trip.state)) {
                if(rider) {
                    if(rider.action==='mount')trip.state='boarding';
                    if(rider.action!=='ride')continue;
                } else {
                    // A saved trip without an online employee uses a seated illustration of its courier, never an empty moving bike.
                    trip.state='boarding';trip.elapsed+=dt;
                    if(trip.elapsed<BOARDING_SECONDS)continue;
                }
                trip.hasRider=true;trip.state='outbound';trip.roadRoute=ROAD_CONNECTOR;trip.routeDistance=0;
            }
            if(trip.state==='cruising') {
                trip.roadDistance=(trip.roadDistance+ROAD_SPEED*dt)%ROAD_LENGTH;
                const pose=poseAlong(ROAD_POINTS,trip.roadDistance/ROAD_LENGTH);
                trip.position=pose.position;trip.rotation=pose.rotation;trip.speed=ROAD_SPEED;
            } else if(['outbound','returning'].includes(trip.state) && trip.hasRider) {
                const length=pathLength(trip.roadRoute);trip.routeDistance=Math.min(length,trip.routeDistance+ROAD_SPEED*dt);
                const pose=poseAlong(trip.roadRoute,length?trip.routeDistance/length:1);
                trip.position=pose.position;trip.rotation=pose.rotation;trip.speed=ROAD_SPEED;
                if(trip.routeDistance>=length) {
                    if(trip.state==='returning') {trip.position=[...DELIVERY_PARKING];this.dismount(trip,rider);}
                    else {trip.position=[...ROAD_ENTRY];trip.state='cruising';trip.roadDistance=0;}
                }
            } else if(trip.state==='dismounting' && !rider) {
                trip.elapsed+=dt;if(trip.elapsed>=BOARDING_SECONDS)this.finishReturn(trip,null);
            }
            // Update after moving the bike, so employee and vehicle never render a frame apart.
            if(rider && ['ride','dismount'].includes(rider.action)) {
                rider.position=[...trip.position];rider.rotation=trip.rotation;rider.speed=trip.speed;
                rider.caption=trip.state==='returning'?'Đang chạy về tiệm':trip.state==='dismounting'?'Đang xuống xe':'Đang giao trên đường';
            }
        }
    }
    advance(dt) {
        dt=Math.min(.1,Math.max(0,dt)); this.time+=dt;
        for(const a of [...this.employees.values(),...this.customers.values()]) {
            if(a.kind==='employee' && a.tripId) {
                const trip=this.trips.get(a.tripId);
                if(!trip) { a.tripId=null; a.sequence=null; this.wander(a); }
                else if(a.action==='ride') {
                    continue;
                }
            }
            const step=a.steps[0]; a.speed=0;
            if(a.kind==='customer' && a.phase==='paid' && step?.action==='handOver' && a.elapsed>=1) a.bag=true;
            if(!step) {
                if(a.exiting) {
                    if(a.retiring && a.orderId) {
                        this.parkedCustomers.set(a.orderId,a);
                        if(this.parkedCustomers.size>120)this.parkedCustomers.delete(this.parkedCustomers.keys().next().value);
                    }
                    (a.kind==='employee'?this.employees:this.customers).delete(a.id); continue;
                }
                if(a.kind==='customer') {
                    if(a.orderId) this.browse(a);
                    else if(this.time>10 && a.shelfIndex%3===0) this.exitCustomer(a,false);
                    else this.browse(a);
                } else if(a.kind==='employee') { a.sequence=null; this.wander(a); }
                continue;
            }
            a.action=step.action; if(step.caption) a.caption=step.caption;
            if(step.point) {
                if(!a.route) {
                    try { const points=planRoute(a.position,step.point);
                        a.route={points,length:pathLength(points),travelled:0}; }
                    catch { a.route=null; a.steps.shift(); a.caption='Đang chờ lối đi'; continue; }
                }
                const route=a.route; const speed=WALK;
                route.travelled+=speed*dt;
                const pose=poseAlong(route.points,route.length ? route.travelled/route.length : 1);
                a.position=pose.position; a.rotation=pose.rotation; a.speed=speed;
                if(route.travelled>=route.length) { a.position=[...step.point]; a.route=null; a.steps.shift(); a.elapsed=0; }
            } else {
                a.elapsed+=dt;
                if(a.elapsed>=step.duration) {
                    if(step.action==='dismount' && a.tripId)this.finishReturn(this.trips.get(a.tripId),a);
                    else {a.steps.shift();a.elapsed=0;}
                }
            }
        }
        this.advanceTrips(dt);
        const browsing=[...this.customers.values()].filter(c=>!c.exiting).length;
        if(browsing>7) {
            const c=[...this.customers.values()].find(c=>!c.exiting && c.phase!=='checkout');
            if(c){c.retiring=true;c.exiting=true;this.assign(c,[move(entrance)]);}
        }
        if(browsing<5 && this.time>2) this.addShopper(this.customerId%10);
        if(browsing<7 && Math.floor(this.time)%17===0 && this.time-this.lastArrival>5) { this.addShopper(this.customerId%10); this.lastArrival=this.time; }
    }
    settle() {
        // Effects-off still depicts the current facts and removes people who have left.
        for(const trip of this.trips.values()) {
            if(trip.suppressed)continue;
            if(trip.state==='returned' || trip.state==='returning' || trip.state==='dismounting'){
                trip.position=[...DELIVERY_PARKING];this.finishReturn(trip,this.employees.get(trip.courierId));
            }
            else {
                trip.state='cruising';trip.hasRider=true;trip.position=poseAlong(ROAD_POINTS,trip.roadDistance/ROAD_LENGTH).position;
                trip.rotation=poseAlong(ROAD_POINTS,trip.roadDistance/ROAD_LENGTH).rotation;
            }
            trip.speed=0;
        }
        for(const [id,a] of this.employees) {
            if(a.exiting){this.employees.delete(id);continue;}
            const trip=a.tripId?this.trips.get(a.tripId):null;
            if(trip){a.position=[...trip.position];a.rotation=trip.rotation;a.action='ride';this.assign(a,[act('ride',Infinity)]);}
            else if(a.work){a.position=[...this.workPoint({...a.work,module:a.module})];a.action=a.module==='review'?'office':'idle';}
            a.speed=0;a.route=null;
        }
        for(const [id,c] of this.customers) {
            if(c.exiting){if(c.retiring&&c.orderId)this.parkedCustomers.set(c.orderId,c);this.customers.delete(id);continue;}
            if(c.phase==='checkout'){const p=this.counter(c.checkoutTerminal);c.position=[p[0],-5.85];}
            c.action='idle';c.speed=0;c.route=null;
        }
    }
    clear() { this.employees.clear(); this.customers.clear(); this.parkedCustomers.clear(); this.trips.clear(); this.checkouts=[]; this.input=null; this.seen.clear(); this.scans.clear(); }
}
