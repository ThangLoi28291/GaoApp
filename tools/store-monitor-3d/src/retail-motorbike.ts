import * as B from "./engine";

/** One recognizable delivery motorbike. Local forward is +Z, matching the rider. */
export function createDeliveryMotorbike(scene:B.Scene, software:boolean) {
  const root=new B.TransformNode("delivery-motorbike-XM01",scene);
  const mats=new Map<string,B.StandardMaterial>();
  function mat(color:string){if(!mats.has(color)){const m=new B.StandardMaterial(`bike-${color}`,scene);m.diffuseColor=B.Color3.FromHexString(color);m.specularColor=new B.Color3(.12,.12,.12);mats.set(color,m);}return mats.get(color)!;}
  function part(mesh:B.Mesh,x:number,y:number,z:number,color:string){mesh.parent=root;mesh.position.set(x,y,z);mesh.material=mat(color);mesh.metadata={actorId:"nv04",vehicle:"XM-01"};return mesh;}
  function box(name:string,x:number,y:number,z:number,w:number,h:number,d:number,color:string){return part(B.MeshBuilder.CreateBox(name,{width:w,height:h,depth:d},scene),x,y,z,color);}
  const wheels:B.TransformNode[]=[];
  for(const z of [-.86,.88]){
    const axle=new B.TransformNode(`bike-axle-${z}`,scene);axle.parent=root;axle.position.set(0,.33,z);wheels.push(axle);
    const tire=part(B.MeshBuilder.CreateCylinder("bike-tire",{diameter:.64,height:.19,tessellation:software?12:24},scene),0,0,0,"#283a40");tire.parent=axle;tire.rotation.z=Math.PI/2;
    for(const side of [-1,1]){
      const rim=part(B.MeshBuilder.CreateCylinder("bike-rim",{diameter:.36,height:.022,tessellation:12},scene),side*.105,0,0,"#d5dee2");rim.parent=axle;rim.rotation.z=Math.PI/2;
      for(const angle of [0,Math.PI/3,Math.PI*2/3]){const spoke=box("bike-spoke",side*.122,0,0,.025,.32,.032,"#647981");spoke.parent=axle;spoke.rotation.x=angle;}
    }
  }
  box("bike-frame",0,.51,-.02,.25,.14,1.65,"#445861");
  box("bike-engine",0,.48,-.42,.42,.38,.44,"#5a6f78");
  box("bike-seat",0,.83,-.37,.52,.16,.9,"#293c45");
  box("bike-rear-body",0,.65,-.69,.63,.27,.62,"#8a60ba");
  const fairing=box("bike-front-fairing",0,.75,.69,.50,.72,.25,"#9772c5");fairing.rotation.x=-.19;
  box("bike-footrest",0,.31,.08,.77,.055,.66,"#566772");
  for(const x of [-.14,.14]){const fork=box("bike-fork",x,.62,.88,.07,.64,.07,"#b9c6ce");fork.rotation.x=-.18;}
  const bar=box("bike-handlebar",0,1.13,.59,.85,.065,.07,"#344a54");
  box("bike-headlamp",0,1.04,.88,.38,.20,.08,"#fff3be");
  box("bike-tail-light",0,.69,-1.04,.3,.1,.025,"#d67665");
  for(const x of [-.43,.43]){
    box("bike-grip",x,1.13,.60,.19,.09,.11,"#2e454e");
    const stem=box("bike-mirror-stem",x,1.29,.62,.025,.28,.025,"#728c94");stem.rotation.z=x<0?.18:-.18;
    box("bike-mirror",x,1.44,.61,.21,.12,.04,"#b4d3de");
  }
  box("bike-cargo-base",0,.92,-1.05,.88,.08,.68,"#536771");
  box("bike-delivery-box",0,1.20,-1.07,.87,.54,.67,"#a487c6");
  box("bike-delivery-lid",0,1.49,-1.07,.92,.08,.71,"#72539b");
  for(const x of [-.446,.446]){
    box("bike-box-label",x,1.21,-1.07,.016,.27,.43,"#f5eefc");
    box("bike-box-mark",x*1.03,1.21,-1.07,.017,.09,.21,"#8b6caf");
  }
  return {root,updateWheel(distance:number){for(const w of wheels)w.rotation.x+=distance/.32;},handlebar:bar};
}
