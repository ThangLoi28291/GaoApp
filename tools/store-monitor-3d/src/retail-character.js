/**
 * Original, procedural retail character for Babylon.js.
 *
 * Usage:
 *   import { createRetailCharacter } from './retail-character.js';
 *   const actor = createRetailCharacter(BABYLON, scene, {
 *     id: 'receiving-1', name: 'Maya', style: 'receiver',
 *     color: '#3B82F6', skin: '#BD805C', hair: '#2C241F', variant: 1,
 *   });
 *   actor.root.position.set(2, 0, 3);
 *   // Local +Z is forward. Time is seconds; speed is normalized 0..1.
 *   actor.setMotion({ time: seconds, speed: 0.7, action: 'carry' });
 *   actor.setSelected(true);
 *
 * Height: approximately 1.66 units, with soles at y = 0. Meshes carry
 * metadata.actorId for picking. Add root.getChildMeshes() to your own shadow
 * generator if desired. Materials are shared per scene and owned by the scene.
 * `dispose()` removes only this character; it leaves shared materials intact.
 *
 * Supported actions: idle, walk, scan, carry, count, pack, handOver, ride, pick.
 * `phase` is an optional stable phase offset in radians for crowd variation.
 * `joints` exposes the named transform nodes, but setMotion owns their poses.
 * Limitations: no navigation, physics, individual finger rig, facial lip-sync,
 * badge text, or automatic ground/slope following. The carrying carton is a
 * character prop, not an inventory object. Foot targets assume a flat floor.
 */

const SCENE_MATERIALS = new WeakMap();
const TAU = Math.PI * 2;
const UPPER_LEG = 0.330;
const LOWER_LEG = 0.325;
const HIP_HEIGHT = 0.770;
const ANKLE_HEIGHT = 0.118;
const ROLE_COLORS = {
  cashier: '#19A67B',
  receiver: '#3888DD',
  counter: '#EC9644',
  delivery: '#9764D7',
  customer: '#B6876B',
};

const clamp = (n, lo, hi) => Math.min(hi, Math.max(lo, n));
const lerp = (a, b, t) => a + (b - a) * t;
const finite = (n, fallback = 0) => Number.isFinite(n) ? n : fallback;
const hex = (value, fallback) => /^#[0-9a-f]{6}$/i.test(value || '') ? value.toUpperCase() : fallback;

function phaseFromId(id) {
  let hash = 0;
  for (const c of String(id)) hash = ((hash * 31) + c.charCodeAt(0)) >>> 0;
  return (hash % 1009) / 1009 * TAU;
}

/** Create a softly sculpted, articulated employee or shopper. */
export function createRetailCharacter(B, scene, options = {}) {
  if (!B || !scene) throw new Error('createRetailCharacter requires Babylon and a scene.');

  const id = String(options.id ?? `retail-${scene.getUniqueId()}`);
  const actorName = String(options.name ?? 'Retail character');
  const style = Object.hasOwn(ROLE_COLORS, options.style) ? options.style : 'cashier';
  const variant = options.variant === 1 ? 1 : 0;
  const uniformHex = hex(options.color, ROLE_COLORS[style]);
  const skinHex = hex(options.skin, variant ? '#B77C58' : '#E0AC87');
  const hairHex = hex(options.hair, variant ? '#332621' : '#44362D');
  const prefix = `retail/${id}`;
  const actorMetadata = { actorId: id, actorName, actorRole: style, isRetailCharacter: true };
  let disposed = false;
  let selected = false;

  let materials = SCENE_MATERIALS.get(scene);
  if (!materials) {
    materials = new Map();
    SCENE_MATERIALS.set(scene, materials);
  }

  const tint = (color, factor = 1, white = 0) => {
    const c = B.Color3.FromHexString(color).scale(factor);
    return new B.Color3(
      clamp(c.r + white, 0, 1),
      clamp(c.g + white, 0, 1),
      clamp(c.b + white, 0, 1),
    );
  };

  function material(key, color, { specular = 0.09, power = 48, emissive = 0 } = {}) {
    const cacheKey = `${key}/${color.toHexString()}/${specular}/${emissive}`;
    if (materials.has(cacheKey)) return materials.get(cacheKey);
    const m = new B.StandardMaterial(`retail/shared/${cacheKey}`, scene);
    m.diffuseColor = color;
    m.specularColor = new B.Color3(specular, specular, specular);
    m.specularPower = power;
    m.emissiveColor = color.scale(emissive);
    materials.set(cacheKey, m);
    return m;
  }

  const fabric = material('fabric', tint(uniformHex, 0.94, 0.045), { specular: 0.055 });
  const apron = material('apron', tint(uniformHex, 0.70, 0.015), { specular: 0.035 });
  const trim = material('trim', tint(uniformHex, 0.47, 0.015), { specular: 0.045 });
  const fabricLight = material('fabricLight', tint(uniformHex, 0.65, 0.27), { specular: 0.05 });
  const skin = material('skin', tint(skinHex), { specular: 0.105, power: 38 });
  const hair = material('hair', tint(hairHex), { specular: 0.14, power: 64 });
  const pants = material('pants', tint('#273D51'), { specular: 0.04 });
  const sole = material('sole', tint('#304052'), { specular: 0.045 });
  const shoe = material('shoe', tint('#EDF1EB'), { specular: 0.13 });
  const white = material('white', tint('#FFFDF7'), { specular: 0.13 });
  const ink = material('ink', tint('#26343B'), { specular: 0.13 });
  const pupil = material('pupil', tint('#212B2E'), { specular: 0.30, power: 96 });
  const mouth = material('mouth', tint('#824F40'), { specular: 0.04 });
  const cardboard = material('cardboard', tint('#D5AC73'), { specular: 0.025 });
  const tape = material('tape', tint('#EBCB94'), { specular: 0.11 });
  const scannerLens = material('scannerLens', tint('#78E5D0'), { specular: 0.2, emissive: 0.50 });
  const selectionMaterial = material('selection', tint(uniformHex, 0.9, 0.1), { emissive: 0.65 });

  function node(name, parent, x = 0, y = 0, z = 0) {
    const n = new B.TransformNode(`${prefix}/${name}`, scene);
    n.parent = parent ?? null;
    n.position.set(x, y, z);
    n.metadata = { ...actorMetadata };
    return n;
  }

  function finish(mesh, parent, mat, x = 0, y = 0, z = 0) {
    mesh.parent = parent;
    mesh.position.set(x, y, z);
    mesh.material = mat;
    mesh.isPickable = true;
    mesh.receiveShadows = true;
    mesh.metadata = { ...actorMetadata };
    return mesh;
  }

  function ellipsoid(name, parent, mat, size, at = [0, 0, 0], segments = 14) {
    const mesh = B.MeshBuilder.CreateSphere(`${prefix}/${name}`, { diameter: 1, segments }, scene);
    mesh.scaling.set(size[0], size[1], size[2]);
    return finish(mesh, parent, mat, ...at);
  }

  function capsule(name, parent, mat, length, radius, at = [0, 0, 0]) {
    const mesh = B.MeshBuilder.CreateCapsule(`${prefix}/${name}`, {
      height: length,
      radius,
      tessellation: 12,
      capSubdivisions: 4,
      subdivisions: 1,
    }, scene);
    return finish(mesh, parent, mat, ...at);
  }

  function box(name, parent, mat, size, at = [0, 0, 0]) {
    const mesh = B.MeshBuilder.CreateBox(`${prefix}/${name}`, {
      width: size[0], height: size[1], depth: size[2],
    }, scene);
    return finish(mesh, parent, mat, ...at);
  }

  function torus(name, parent, mat, diameter, thickness, at = [0, 0, 0], tessellation = 36) {
    const mesh = B.MeshBuilder.CreateTorus(`${prefix}/${name}`, { diameter, thickness, tessellation }, scene);
    return finish(mesh, parent, mat, ...at);
  }

  const root = node('root');
  const motion = node('motion', root);
  const pelvis = node('pelvis', motion, 0, 0.785, 0);
  const torso = node('torso', pelvis, 0, 0.220, 0);
  const head = node('head', torso, 0, 0.424, 0.005);

  // The softly overlapping ellipsoids read as clothing rather than a rigid shell.
  const shirtMesh = ellipsoid('shirt', torso, fabric, [0.438, 0.478, 0.333], [0, 0.010, 0]);
  ellipsoid('trousers-waist', pelvis, pants, [0.365, 0.205, 0.282], [0, 0.014, -0.007]);
  capsule('neck', torso, skin, 0.130, 0.062, [0, 0.270, 0]);
  const collar = torus('soft-collar', torso, trim, 0.151, 0.022, [0, 0.219, 0]);
  collar.scaling.z = 0.86;

  if (style === 'customer') {
    const strap = capsule('bag-strap', torso, trim, 0.432, 0.012, [0.006, 0.031, 0.174]);
    strap.rotation.z = 0.59;
    const bag = ellipsoid('crossbody-bag', torso, apron, [0.190, 0.186, 0.081], [0.137, -0.155, 0.191]);
    bag.rotation.z = 0.08;
    box('bag-clasp', torso, tape, [0.034, 0.027, 0.009], [0.137, -0.128, 0.235]);
  } else {
    ellipsoid('apron-bib', torso, apron, [0.250, 0.267, 0.038], [0, 0.045, 0.165]);
    ellipsoid('apron-skirt', torso, apron, [0.356, 0.249, 0.047], [0, -0.093, 0.160]);
    for (const s of [-1, 1]) {
      const strap = capsule(`apron-strap-${s}`, torso, apron, 0.167, 0.012, [s * 0.078, 0.169, 0.137]);
      strap.rotation.z = s * 0.24;
    }
    box('apron-pocket', torso, trim, [0.122, 0.061, 0.013], [0.024, -0.105, 0.185]);
    box('pocket-stitch', torso, fabricLight, [0.123, 0.007, 0.014], [0.024, -0.078, 0.188]);
    box('name-badge', torso, white, [0.075, 0.035, 0.010], [-0.080, 0.133, 0.181]);
    box('badge-line', torso, trim, [0.047, 0.005, 0.003], [-0.080, 0.132, 0.187]);
  }

  // Face: softly protruding whites and dark pupils preserve readability at game distance.
  ellipsoid('face', head, skin, [0.337, 0.393, 0.302], [0, 0, 0], 18);
  for (const s of [-1, 1]) {
    ellipsoid(`ear-${s}`, head, skin, [0.047, 0.073, 0.043], [s * 0.168, -0.008, -0.004]);
    ellipsoid(`eye-white-${s}`, head, white, [0.050, 0.060, 0.026], [s * 0.060, 0.026, 0.135]);
    ellipsoid(`pupil-${s}`, head, pupil, [0.023, 0.034, 0.014], [s * 0.059, 0.024, 0.153], 12);
    ellipsoid(`eye-glint-${s}`, head, white, [0.007, 0.009, 0.005], [s * 0.059 - 0.004, 0.033, 0.160], 8);
    const brow = capsule(`eyebrow-${s}`, head, hair, 0.052, 0.007, [s * 0.061, 0.074, 0.132]);
    brow.rotation.z = Math.PI / 2 + s * 0.11;
  }
  ellipsoid('nose', head, skin, [0.048, 0.054, 0.042], [0, -0.026, 0.153]);
  const smilePoints = [[-0.040, -0.078], [-0.019, -0.090], [0.019, -0.090], [0.040, -0.078]];
  for (let i = 0; i < 3; i++) {
    const [x1, y1] = smilePoints[i];
    const [x2, y2] = smilePoints[i + 1];
    const stroke = capsule(`smile-${i}`, head, mouth, Math.hypot(x2 - x1, y2 - y1) + 0.005, 0.0037,
      [(x1 + x2) / 2, (y1 + y2) / 2, 0.141]);
    stroke.rotation.z = Math.atan2(-(x2 - x1), y2 - y1);
  }

  ellipsoid('hair-crown', head, hair, [0.351, 0.222, 0.288], [0, 0.102, -0.024], 16);
  const fringe = ellipsoid('swept-fringe', head, hair, [0.245, 0.084, 0.079], [-0.043, 0.123, 0.103]);
  fringe.rotation.z = variant ? -0.17 : 0.13;
  ellipsoid('hair-temple-left', head, hair, [0.043, 0.136, 0.163], [-0.143, 0.060, -0.013]);
  ellipsoid('hair-temple-right', head, hair, [0.043, 0.124, 0.158], [0.145, 0.063, -0.018]);
  if (variant) {
    const tail = ellipsoid('ponytail', head, hair, [0.111, 0.221, 0.114], [0.031, -0.010, -0.182]);
    tail.rotation.x = -0.18;
  }

  function makeArm(side, s) {
    const shoulder = node(`${side}-shoulder`, torso, s * 0.246, 0.188, 0);
    const elbow = node(`${side}-elbow`, shoulder, 0, -0.248, 0);
    const wrist = node(`${side}-wrist`, elbow, 0, -0.242, 0);
    capsule(`${side}-sleeve`, shoulder, fabric, 0.274, 0.069, [0, -0.113, 0]);
    const cuff = capsule(`${side}-cuff`, shoulder, trim, 0.145, 0.070, [0, -0.223, 0]);
    // A slightly compressed capsule makes a soft rolled cuff.
    cuff.scaling.y = 0.40;
    capsule(`${side}-forearm`, elbow, skin, 0.260, 0.050, [0, -0.119, 0]);
    ellipsoid(`${side}-hand`, wrist, skin, [0.085, 0.123, 0.080], [0, -0.037, 0.009]);
    const thumb = ellipsoid(`${side}-thumb`, wrist, skin, [0.038, 0.069, 0.041], [-s * 0.035, -0.022, 0.032]);
    thumb.rotation.z = s * 0.24;
    return { shoulder, elbow, wrist };
  }

  const leftArm = makeArm('left', -1);
  const rightArm = makeArm('right', 1);

  function makeLeg(side, s) {
    const hip = node(`${side}-hip`, pelvis, s * 0.118, -0.015, -0.005);
    const knee = node(`${side}-knee`, hip, 0, -UPPER_LEG, 0);
    const ankle = node(`${side}-ankle`, knee, 0, -LOWER_LEG, 0);
    capsule(`${side}-trouser-thigh`, hip, pants, 0.365, 0.082, [0, -0.149, 0]);
    capsule(`${side}-trouser-shin`, knee, pants, 0.348, 0.067, [0, -0.150, 0]);
    ellipsoid(`${side}-trainer`, ankle, shoe, [0.155, 0.143, 0.276], [0, -0.036, 0.057]);
    ellipsoid(`${side}-trainer-sole`, ankle, sole, [0.158, 0.039, 0.280], [0, -0.096, 0.059]);
    return { hip, knee, ankle };
  }

  const leftLeg = makeLeg('left', -1);
  const rightLeg = makeLeg('right', 1);

  // The carrying prop is body-parented so its center stays stable between both hands.
  const carton = node('carton', torso, 0, -0.055, 0.370);
  box('carton-body', carton, cardboard, [0.365, 0.225, 0.280]);
  box('carton-tape', carton, tape, [0.041, 0.228, 0.283]);
  box('carton-label', carton, white, [0.117, 0.057, 0.004], [-0.067, 0.014, 0.143]);
  box('carton-label-mark', carton, ink, [0.076, 0.007, 0.003], [-0.067, 0.020, 0.146]);
  carton.setEnabled(false);

  const scanner = node('scanner', rightArm.wrist, 0, -0.018, 0.042);
  capsule('scanner-handle', scanner, ink, 0.087, 0.020, [0, -0.015, 0]);
  const scannerHead = box('scanner-head', scanner, ink, [0.071, 0.043, 0.071], [0, 0.035, 0.019]);
  scannerHead.rotation.x = -0.18;
  box('scanner-window', scanner, scannerLens, [0.052, 0.023, 0.004], [0, 0.040, 0.056]);
  scanner.setEnabled(false);

  const selectionRing = torus('selection-ring', root, selectionMaterial, 0.72, 0.018, [0, 0.021, 0], 48);
  selectionRing.receiveShadows = false;
  selectionRing.setEnabled(false);

  const joints = {
    motion, pelvis, torso, head,
    leftShoulder: leftArm.shoulder, leftElbow: leftArm.elbow, leftWrist: leftArm.wrist,
    rightShoulder: rightArm.shoulder, rightElbow: rightArm.elbow, rightWrist: rightArm.wrist,
    leftHip: leftLeg.hip, leftKnee: leftLeg.knee, leftAnkle: leftLeg.ankle,
    rightHip: rightLeg.hip, rightKnee: rightLeg.knee, rightAnkle: rightLeg.ankle,
    carton, scanner,
  };

  const pose = {
    bodyY: 0, bodyX: 0, bodyLean: 0, bodyYaw: 0, bodyRoll: 0,
    headX: 0, headY: 0, headZ: 0,
    leftShoulderX: -0.04, rightShoulderX: -0.04,
    leftShoulderZ: -0.08, rightShoulderZ: 0.08,
    leftElbowX: -0.17, rightElbowX: -0.17,
    leftWristX: 0, rightWristX: 0, leftWristZ: 0, rightWristZ: 0,
    leftHipX: 0, rightHipX: 0,
    leftKneeX: 0, rightKneeX: 0,
    leftAnkleX: 0, rightAnkleX: 0,
    cartonY: -0.055, cartonZ: 0.370, cartonTilt: 0,
    cartonAmount: 0, scannerAmount: 0,
  };
  let lastTime = null;
  let gait = phaseFromId(id);
  let moving = 0;
  const identityPhase = phaseFromId(`${id}/idle`);

  // Sagittal two-bone IK provides a flat stance and actual swing-foot clearance.
  function legAngles(forward, lift, bodyY) {
    const down = HIP_HEIGHT + bodyY - (ANKLE_HEIGHT + lift);
    const distance = clamp(Math.hypot(down, forward), 0.05, UPPER_LEG + LOWER_LEG - 0.0005);
    const aim = Math.atan2(forward, down);
    const hipTriangle = Math.acos(clamp(
      (UPPER_LEG ** 2 + distance ** 2 - LOWER_LEG ** 2) / (2 * UPPER_LEG * distance), -1, 1,
    ));
    const knee = Math.acos(clamp(
      (distance ** 2 - UPPER_LEG ** 2 - LOWER_LEG ** 2) / (2 * UPPER_LEG * LOWER_LEG), -1, 1,
    ));
    const hip = -aim - hipTriangle;
    return { hip, knee, ankle: -(hip + knee) };
  }

  function setMotion(input = {}) {
    if (disposed) return;
    const time = finite(input.time, lastTime ?? 0);
    const dt = lastTime === null ? 1 / 60 : clamp(time - lastTime, 0, 0.080);
    lastTime = time;
    const action = typeof input.action === 'string' ? input.action : 'idle';
    const targetMoving = clamp(finite(input.speed, action === 'walk' ? 0.7 : 0), 0, 1);
    const smoothing = input.snap ? 1 : 1 - Math.exp(-dt * 11);
    moving = lerp(moving, targetMoving, smoothing);
    gait = (gait + dt * (5.5 + 2.2 * moving) * Math.sqrt(moving)) % TAU;
    const offset = finite(input.phase);
    const cycle = gait + offset;
    const breath = Math.sin(time * 2.1 + identityPhase + offset);
    const gesture = Math.sin(time * 4.3 + identityPhase + offset);
    const step = Math.cos(cycle);
    const desired = {
      bodyY: -0.025 * moving + 0.007 * moving * Math.cos(cycle * 2) + 0.0015 * (1 - moving) * breath,
      bodyX: 0.009 * moving * Math.sin(cycle),
      bodyLean: 0.033 * moving,
      bodyYaw: 0.032 * moving * Math.sin(cycle),
      bodyRoll: 0.010 * moving * Math.sin(cycle),
      headX: -0.012 + 0.014 * breath,
      headY: 0.030 * (1 - moving) * Math.sin(time * 0.73 + identityPhase),
      headZ: 0.012 * (1 - moving) * breath,
      leftShoulderX: -0.045 - 0.36 * moving * step + 0.018 * breath,
      rightShoulderX: -0.045 + 0.36 * moving * step - 0.018 * breath,
      leftShoulderZ: -0.075,
      rightShoulderZ: 0.075,
      leftElbowX: -0.17 - 0.06 * moving * (1 - step),
      rightElbowX: -0.17 - 0.06 * moving * (1 + step),
      leftWristX: 0,
      rightWristX: 0,
      leftWristZ: 0,
      rightWristZ: 0,
      leftHipX: 0, rightHipX: 0,
      leftKneeX: 0, rightKneeX: 0,
      leftAnkleX: 0, rightAnkleX: 0,
      cartonY: -0.055,
      cartonZ: 0.370,
      cartonTilt: 0,
      cartonAmount: 0,
      scannerAmount: 0,
    };

    if (action === 'carry' || action === 'pack' || action === 'handOver') {
      Object.assign(desired, {
        leftShoulderX: -0.70,
        rightShoulderX: -0.70,
        leftShoulderZ: 0.22,
        rightShoulderZ: -0.22,
        leftElbowX: -0.88,
        rightElbowX: -0.88,
        leftWristX: 0.06,
        rightWristX: 0.06,
        leftWristZ: -0.07,
        rightWristZ: 0.07,
        bodyLean: -0.010 + 0.024 * moving,
        headX: 0.045 + 0.012 * breath,
        cartonAmount: 1,
      });
    }

    if (action === 'scan') {
      Object.assign(desired, {
        rightShoulderX: -0.82 + 0.045 * gesture,
        rightShoulderZ: -0.14,
        rightElbowX: -0.68 + 0.055 * gesture,
        rightWristX: -0.25,
        rightWristZ: -0.09,
        leftShoulderX: -0.32,
        leftShoulderZ: 0.15,
        leftElbowX: -0.67,
        leftWristX: -0.10,
        headX: 0.12,
        headY: 0.04 + 0.035 * gesture,
        bodyLean: 0.03,
        scannerAmount: 1,
      });
    } else if (action === 'count') {
      Object.assign(desired, {
        leftShoulderX: -0.50,
        leftShoulderZ: 0.23,
        leftElbowX: -1.06,
        leftWristX: -0.17,
        rightShoulderX: -0.72 + 0.055 * gesture,
        rightShoulderZ: -0.26,
        rightElbowX: -0.88 + 0.11 * gesture,
        rightWristX: 0.17 + 0.10 * gesture,
        rightWristZ: -0.15,
        headX: 0.10 + 0.026 * gesture,
        headY: -0.04,
        bodyLean: 0.015,
      });
    } else if (action === 'pack') {
      Object.assign(desired, {
        leftShoulderX: -0.63 + 0.048 * gesture,
        rightShoulderX: -0.63 - 0.048 * gesture,
        leftElbowX: -0.83 - 0.095 * gesture,
        rightElbowX: -0.83 + 0.095 * gesture,
        leftShoulderZ: 0.17,
        rightShoulderZ: -0.17,
        headX: 0.12,
        bodyLean: 0.026,
        cartonY: -0.085,
        cartonZ: 0.365,
        cartonTilt: 0.014 * gesture,
      });
    } else if (action === 'handOver') {
      const reach = 0.5 + 0.5 * Math.sin(time * 2.15 + identityPhase);
      Object.assign(desired, {
        leftShoulderX: -0.89 - 0.19 * reach,
        rightShoulderX: -0.89 - 0.19 * reach,
        leftElbowX: -0.55 + 0.06 * reach,
        rightElbowX: -0.55 + 0.06 * reach,
        leftShoulderZ: 0.18,
        rightShoulderZ: -0.18,
        headX: -0.035,
        bodyLean: 0.025 * reach,
        cartonY: 0.014 + 0.060 * reach,
        cartonZ: 0.415 + 0.068 * reach,
      });
    }

    if (['paperWalk','print','label','stamp'].includes(action)) {
      Object.assign(desired, {leftShoulderX:-.46,leftShoulderZ:.08,leftElbowX:-1.02,leftWristX:-.12,headX:.08,cartonAmount:0,scannerAmount:0});
      if(action==='print')Object.assign(desired,{rightShoulderX:-.96+.05*gesture,rightElbowX:-.4,rightWristX:-.18,headX:.13});
      if(action==='label')Object.assign(desired,{rightShoulderX:-1.15+.09*gesture,rightShoulderZ:-.05,rightElbowX:-.22+.08*gesture,rightWristX:.15,bodyLean:.05});
      if(action==='stamp')Object.assign(desired,{rightShoulderX:-.77+.14*gesture,rightElbowX:-.58-.12*gesture,rightWristX:.05,bodyLean:.04});
    }

    for (const [side, phase] of [['left', cycle], ['right', cycle + Math.PI]]) {
      const swing = Math.max(0, Math.sin(phase));
      const forward = -0.133 * moving * Math.cos(phase);
      const lift = 0.067 * moving * Math.pow(swing, 1.12);
      const angles = legAngles(forward, lift, desired.bodyY);
      desired[`${side}HipX`] = angles.hip;
      desired[`${side}KneeX`] = angles.knee;
      desired[`${side}AnkleX`] = angles.ankle - 0.10 * moving * swing;
    }

    if (action === 'office') {
      Object.assign(desired, {
        bodyY: -.32, bodyLean: .04, headX: .12,
        leftShoulderX: -.9, rightShoulderX: -.92 + .03 * gesture,
        leftElbowX: -.85, rightElbowX: -.82,
        leftHipX: -1.45, rightHipX: -1.45,
        leftKneeX: 1.50, rightKneeX: 1.50,
        leftAnkleX: -.05, rightAnkleX: -.05,
        cartonAmount: 0, scannerAmount: 0,
      });
    }
    if (['ride','mount','dismount'].includes(action)) {
      const seated=clamp(finite(input.seatProgress,1),0,1);
      const ridePose={
        bodyY: -0.10, bodyX: 0, bodyLean: 0.10, bodyYaw: 0, bodyRoll: 0,
        headX: -0.03, headY: 0, headZ: 0,
        leftShoulderX: -0.98, rightShoulderX: -0.98,
        leftShoulderZ: -0.08, rightShoulderZ: 0.08,
        leftElbowX: -0.32, rightElbowX: -0.32,
        leftHipX: -1.18, rightHipX: -1.18,
        leftKneeX: 1.55, rightKneeX: 1.55,
        leftAnkleX: -0.32, rightAnkleX: -0.32,
        cartonAmount: 0, scannerAmount: 0,
      };
      for(const [key,value] of Object.entries(ridePose))desired[key]=lerp(desired[key],value,seated);
    }

    if (style === 'customer') {
      desired.cartonAmount = 0;
      desired.scannerAmount = 0;
      if (input.basket) Object.assign(desired, {
        leftShoulderX: -0.24 + 0.035 * moving * step,
        leftShoulderZ: -0.18,
        leftElbowX: -0.43,
        leftWristX: 0.12,
      });
      if (action === 'pick') {
        const progress = clamp(finite(input.pickProgress), 0, 1);
        const smooth = value => { const t = clamp(value, 0, 1); return t * t * (3 - 2 * t); };
        const reach = smooth(progress / 0.40), lower = smooth((progress - 0.48) / 0.46);
        Object.assign(desired, {
          rightShoulderX: -0.16 - 1.24 * reach + 0.88 * lower,
          rightShoulderZ: -0.06 - 0.13 * reach - 0.43 * lower,
          rightElbowX: -0.18 - 0.16 * reach - 0.97 * lower,
          rightWristX: 0.05 + 0.13 * lower,
          headX: 0.05 + 0.17 * lower,
          headY: -0.07 * lower,
          bodyLean: 0.07 * reach * (1 - lower),
        });
      }
    }

    // Shared exponential damping makes action changes and motion stops continuous.
    for (const key of Object.keys(pose)) pose[key] = lerp(pose[key], desired[key], smoothing);
    motion.position.set(pose.bodyX, pose.bodyY, 0);
    torso.rotation.set(pose.bodyLean, pose.bodyYaw, pose.bodyRoll);
    head.rotation.set(pose.headX, pose.headY, pose.headZ);
    shirtMesh.scaling.y = 0.478 * (1 + 0.006 * breath);
    for (const [side, arm, leg] of [['left', leftArm, leftLeg], ['right', rightArm, rightLeg]]) {
      arm.shoulder.rotation.set(pose[`${side}ShoulderX`], 0, pose[`${side}ShoulderZ`]);
      arm.elbow.rotation.set(pose[`${side}ElbowX`], 0, 0);
      arm.wrist.rotation.set(pose[`${side}WristX`], 0, pose[`${side}WristZ`]);
      leg.hip.rotation.set(pose[`${side}HipX`], 0, 0);
      leg.knee.rotation.set(pose[`${side}KneeX`], 0, 0);
      leg.ankle.rotation.set(pose[`${side}AnkleX`], 0, 0);
    }
    carton.position.set(0, pose.cartonY, pose.cartonZ);
    carton.rotation.x = pose.cartonTilt;
    carton.scaling.setAll(Math.max(0.001, pose.cartonAmount));
    carton.setEnabled(pose.cartonAmount > 0.015);
    scanner.scaling.setAll(Math.max(0.001, pose.scannerAmount));
    scanner.setEnabled(pose.scannerAmount > 0.015);
    if (selected) selectionRing.scaling.setAll(1 + 0.022 * Math.sin(time * 3.4));
  }

  function setSelected(value) {
    if (disposed) return;
    selected = Boolean(value);
    selectionRing.setEnabled(selected);
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    root.dispose(false, false);
  }

  // Establish a natural standing pose immediately, before a render loop exists.
  const standing = legAngles(0, 0, 0);
  pose.leftHipX = pose.rightHipX = standing.hip;
  pose.leftKneeX = pose.rightKneeX = standing.knee;
  pose.leftAnkleX = pose.rightAnkleX = standing.ankle;
  setMotion({ time: 0, speed: 0, action: 'idle' });
  lastTime = null;

  return { root, joints, setMotion, setSelected, dispose };
}
