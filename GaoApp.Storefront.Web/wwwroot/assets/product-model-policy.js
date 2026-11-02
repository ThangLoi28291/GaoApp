// Preallocation checks for the pinned static glTF subset. The media endpoint
// independently authorizes and validates the same asset on every request.
const MiB = 1024 * 1024;
const allowedExtensions = new Set(['KHR_materials_unlit', 'KHR_texture_transform', 'EXT_texture_webp']);
const need = value => { if (!value) throw new Error('Mô hình chưa phù hợp với trình xem an toàn.'); };
const integer = (value, fallback) => value === undefined ? fallback : (need(Number.isSafeInteger(value)), value);
const list = (value, max) => { if (value === undefined) return []; need(Array.isArray(value) && value.length <= max); return value; };
const sum = values => values.reduce((a, b) => { need(Number.isSafeInteger(a + b)); return a + b; }, 0);
function data(uri, types) {
  need(typeof uri === 'string');
  const match = /^data:([^;,]+);base64,([A-Za-z0-9+/]+={0,2})$/.exec(uri);
  need(match && types.includes(match[1]) && match[2].length % 4 === 0 && match[2].length <= Math.ceil(8 * MiB / 3) * 4);
  const raw = atob(match[2]); need(raw.length <= 8 * MiB);
  return { bytes: Uint8Array.from(raw, c => c.charCodeAt(0)), mime: match[1] };
}
function imageHeader(bytes) {
  const v = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const text = (a, n) => String.fromCharCode(...bytes.subarray(a, a + n));
  let width = 0, height = 0;
  if (bytes.length >= 24 && bytes[0] === 137 && text(1, 3) === 'PNG' && text(12, 4) === 'IHDR') {
    width = v.getUint32(16); height = v.getUint32(20);
    for (let p = 8, chunks = 0; p + 12 <= bytes.length && chunks++ < 4096;) {
      const length = v.getUint32(p); need(p + 12 + length <= bytes.length);
      const kind = text(p + 4, 4); need(kind !== 'acTL'); if (kind === 'IDAT') break; p += 12 + length;
    }
  } else if (bytes.length >= 30 && text(0, 4) === 'RIFF' && text(8, 4) === 'WEBP') {
    const kind = text(12, 4);
    if (kind === 'VP8X') { need(!(bytes[20] & 2)); width = 1 + bytes[24] + bytes[25] * 256 + bytes[26] * 65536; height = 1 + bytes[27] + bytes[28] * 256 + bytes[29] * 65536; }
    else if (kind === 'VP8 ' && bytes[23] === 157 && bytes[24] === 1 && bytes[25] === 42) { width = v.getUint16(26, true) & 16383; height = v.getUint16(28, true) & 16383; }
    else if (kind === 'VP8L' && bytes[20] === 47) { const bits = v.getUint32(21, true); width = 1 + (bits & 16383); height = 1 + ((bits >>> 14) & 16383); }
  } else if (bytes.length >= 4 && bytes[0] === 255 && bytes[1] === 216) {
    for (let p = 2, segments = 0; p + 4 <= bytes.length && segments++ < 4096;) {
      need(bytes[p++] === 255); while (p < bytes.length && bytes[p] === 255) p++;
      const marker = bytes[p++]; if (marker === 217 || marker === 218) break;
      if (marker === 1 || marker >= 208 && marker <= 215) continue;
      need(p + 2 <= bytes.length); const length = v.getUint16(p); need(length >= 2 && p + length <= bytes.length);
      if (marker >= 192 && marker <= 207 && ![196, 200, 204].includes(marker)) { need(length >= 8); height = v.getUint16(p + 3); width = v.getUint16(p + 5); break; }
      p += length;
    }
  }
  need(width > 0 && height > 0 && width <= 2048 && height <= 2048);
  return width * height;
}
export function inspectModel(buffer, mime) {
  need(buffer instanceof ArrayBuffer && buffer.byteLength > 0 && buffer.byteLength <= 8 * MiB);
  let jsonBytes = new Uint8Array(buffer), binary = null;
  if (mime === 'model/gltf-binary') {
    const v = new DataView(buffer); need(buffer.byteLength >= 20 && v.getUint32(0, true) === 0x46546c67 && v.getUint32(4, true) === 2 && v.getUint32(8, true) === buffer.byteLength);
    let p = 12, chunks = 0;
    while (p < buffer.byteLength) {
      need(p + 8 <= buffer.byteLength); const length = v.getUint32(p, true), kind = v.getUint32(p + 4, true);
      need(length > 0 && length % 4 === 0 && p + 8 + length <= buffer.byteLength);
      if (chunks === 0) { need(kind === 0x4e4f534a); jsonBytes = new Uint8Array(buffer, p + 8, length); }
      else { need(chunks === 1 && kind === 0x004e4942); binary = new Uint8Array(buffer, p + 8, length); }
      chunks++; p += 8 + length;
    }
    need(chunks === 1 || chunks === 2);
  } else need(mime === 'model/gltf+json');
  need(jsonBytes.byteLength <= MiB);
  const root = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(jsonBytes));
  need(root && !Array.isArray(root) && root.asset?.version === '2.0');
  const uris = new Map();
  function tree(value, depth = 0) {
    need(depth <= 32);
    if (typeof value === 'number') need(Number.isFinite(value));
    if (!value || typeof value !== 'object') return;
    for (const [key, child] of Object.entries(value)) {
      need(!['__proto__', 'constructor', 'prototype'].includes(key));
      if (key === 'uri') need(typeof child === 'string' && /^data:[^;,]+;base64,[A-Za-z0-9+/]+={0,2}$/.test(child));
      if (key === 'extensions') { need(child && !Array.isArray(child)); Object.keys(child).forEach(x => need(allowedExtensions.has(x))); }
      if (key === 'extensionsUsed' || key === 'extensionsRequired') list(child, 3).forEach(x => need(allowedExtensions.has(x)));
      tree(child, depth + 1);
    }
  }
  tree(root); need(!list(root.skins, 256).length && !list(root.animations, 256).length);
  const buffers = list(root.buffers, 256).map((b, i) => {
    const length = integer(b.byteLength, -1); need(length > 0);
    if (b.uri !== undefined) { const decoded = data(b.uri, ['application/octet-stream', 'application/gltf-buffer']); need(decoded.bytes.length === length); uris.set(b.uri, decoded); return decoded.bytes; }
    need(i === 0 && binary && binary.length >= length && binary.length - length <= 3); return binary.subarray(0, length);
  });
  need(buffers.length && sum(buffers.map(x => x.length)) <= 8 * MiB);
  const views = list(root.bufferViews, 4096).map(b => {
    const index = integer(b.buffer, -1), offset = integer(b.byteOffset, 0), length = integer(b.byteLength, -1), stride = integer(b.byteStride, 0);
    need(index >= 0 && index < buffers.length && offset >= 0 && length > 0 && offset + length <= buffers[index].length && (stride === 0 || stride >= 4 && stride <= 252 && stride % 4 === 0));
    return { bytes: buffers[index].subarray(offset, offset + length), stride };
  });
  const viewBytes = sum(views.map(x => x.bytes.length)); need(viewBytes <= 64 * MiB);
  let geometryBytes = 0;
  const accessors = list(root.accessors, 4096).map(a => {
    need(a.sparse === undefined);
    const index = integer(a.bufferView, -1), offset = integer(a.byteOffset, 0), count = integer(a.count, -1);
    const size = ({ 5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4 })[a.componentType], dimensions = ({ SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4 })[a.type];
    need(index >= 0 && index < views.length && count > 0 && offset >= 0 && size && dimensions && offset % size === 0);
    const view = views[index], stride = view.stride || size * dimensions;
    need(stride >= size * dimensions && stride % size === 0 && offset + (count - 1) * stride + size * dimensions <= view.bytes.length);
    // WebGL uploads the interleaved backing array including stride/padding.
    geometryBytes += Math.max(count * dimensions * 4, view.stride ? view.bytes.length : 0); need(geometryBytes <= 64 * MiB);
    const dv = new DataView(view.bytes.buffer, view.bytes.byteOffset, view.bytes.length);
    if (a.componentType === 5126) for (let n = 0; n < count; n++) for (let d = 0; d < dimensions; d++) need(Number.isFinite(dv.getFloat32(offset + n * stride + d * 4, true)));
    return { count, component: a.componentType, dimensions, offset, stride, dv };
  });
  let imageBytes = 0;
  const images = list(root.images, 256).map(image => {
    let bytes;
    if (image.uri !== undefined) { need(image.bufferView === undefined); const decoded = data(image.uri, ['image/png', 'image/jpeg', 'image/webp']); need(image.mimeType === undefined || image.mimeType === decoded.mime); uris.set(image.uri, decoded); bytes = decoded.bytes; }
    else { const index = integer(image.bufferView, -1); need(index >= 0 && index < views.length && ['image/png', 'image/jpeg', 'image/webp'].includes(image.mimeType)); bytes = views[index].bytes; }
    imageBytes += bytes.length; return imageHeader(bytes);
  });
  need(sum(images) <= 8000000);
  const samplers = list(root.samplers, 256); samplers.forEach(s => { need([9728, 9729].includes(integer(s.magFilter, 9729)) && [9728, 9729, 9984, 9985, 9986, 9987].includes(integer(s.minFilter, 9987)) && [33071, 33648, 10497].includes(integer(s.wrapS, 10497)) && [33071, 33648, 10497].includes(integer(s.wrapT, 10497))); });
  const textures = list(root.textures, 256).map(t => {
    const source = integer(t.extensions?.EXT_texture_webp?.source ?? t.source, -1), sampler = integer(t.sampler, -1);
    need(source >= 0 && source < images.length && (sampler === -1 || sampler >= 0 && sampler < samplers.length)); return images[source];
  });
  const pixels = sum(textures); need(pixels <= 8000000 && geometryBytes + Math.ceil(pixels * 16 / 3) <= 64 * MiB);
  const working = buffer.byteLength + 2 * jsonBytes.length + sum(buffers.map(x => x.length)) + viewBytes + 2 * geometryBytes + imageBytes + 4 * sum(images);
  need(working <= 64 * MiB);
  const materials = list(root.materials, 32);
  function materialRefs(obj) { if (!obj || typeof obj !== 'object') return; for (const [k, v] of Object.entries(obj)) { if (k.endsWith('Texture')) need(v && Number.isSafeInteger(v.index) && v.index >= 0 && v.index < textures.length); materialRefs(v); } }
  materials.forEach(materialRefs);
  let primitives = 0;
  const meshes = list(root.meshes, 128).map(mesh => {
    need(!list(mesh.weights, 256).length);
    const parts = list(mesh.primitives, 128); need(parts.length); let vertices = 0, triangles = 0;
    for (const p of parts) {
      need(++primitives <= 128 && integer(p.mode, 4) === 4 && !list(p.targets, 256).length);
      const position = accessors[p.attributes?.POSITION]; need(position && position.component === 5126 && position.dimensions === 3 && position.count <= 100000);
      Object.values(p.attributes).forEach(i => need(Number.isSafeInteger(i) && accessors[i]?.count === position.count));
      const mat = integer(p.material, -1); need(mat === -1 || mat >= 0 && mat < materials.length);
      let count = position.count;
      if (p.indices !== undefined) {
        const i = integer(p.indices, -1), a = accessors[i]; need(i >= 0 && a && a.dimensions === 1 && [5121, 5123, 5125].includes(a.component)); count = a.count;
        for (let n = 0; n < count; n++) { const at = a.offset + n * a.stride; const value = a.component === 5121 ? a.dv.getUint8(at) : a.component === 5123 ? a.dv.getUint16(at, true) : a.dv.getUint32(at, true); need(value < position.count); }
      }
      need(count % 3 === 0); vertices += position.count; triangles += count / 3;
    }
    return { vertices, triangles };
  });
  const cameras = list(root.cameras, 256);
  cameras.forEach(camera => {
    need(camera && ['perspective', 'orthographic'].includes(camera.type) && (camera.name === undefined || typeof camera.name === 'string'));
    const p = camera[camera.type]; need(p && typeof p === 'object' && !Array.isArray(p));
    const near = p.znear, far = p.zfar === undefined ? Infinity : p.zfar;
    if (camera.type === 'perspective') need(Number.isFinite(near) && near > 0 && (p.zfar === undefined || Number.isFinite(far)) && far > near && Number.isFinite(p.yfov) && p.yfov > 0 && p.yfov < Math.PI && (p.aspectRatio === undefined || Number.isFinite(p.aspectRatio) && p.aspectRatio > 0));
    else need(Number.isFinite(near) && near >= 0 && Number.isFinite(far) && far > near && Number.isFinite(p.xmag) && p.xmag > 0 && Number.isFinite(p.ymag) && p.ymag > 0);
  });
  const nodes = list(root.nodes, 256), incoming = nodes.map(() => 0), marks = nodes.map(() => 0); need(nodes.length);
  nodes.forEach(n => {
    need(n.skin === undefined && !list(n.weights, 256).length);
    const mesh = integer(n.mesh, -1); need(mesh === -1 || mesh >= 0 && mesh < meshes.length);
    const camera = integer(n.camera, -1); need(camera === -1 || camera >= 0 && camera < cameras.length);
    list(n.children, 256).forEach(id => { need(Number.isSafeInteger(id) && id >= 0 && id < nodes.length && ++incoming[id] <= 1); });
    for (const [key, length] of [['matrix', 16], ['translation', 3], ['rotation', 4], ['scale', 3]]) if (n[key] !== undefined) need(Array.isArray(n[key]) && n[key].length === length && n[key].every(Number.isFinite));
  });
  function cycle(id) { need(marks[id] !== 1); if (marks[id] === 2) return; marks[id] = 1; list(nodes[id].children, 256).forEach(cycle); marks[id] = 2; }
  nodes.forEach((_, i) => cycle(i));
  const scenes = list(root.scenes, 256), selected = integer(root.scene, 0); need(scenes.length && selected >= 0 && selected < scenes.length);
  let vertices = 0, triangles = 0, instances = 0, selectedMeshes = 0;
  scenes.forEach((s, sceneIndex) => {
    const seen = new Set();
    function visit(id) {
      need(Number.isSafeInteger(id) && id >= 0 && id < nodes.length && !seen.has(id) && ++instances <= 256); seen.add(id);
      const n = nodes[id]; if (n.mesh !== undefined) { vertices += meshes[n.mesh].vertices; triangles += meshes[n.mesh].triangles; if (sceneIndex === selected) selectedMeshes++; }
      need(vertices <= 100000 && triangles <= 100000); list(n.children, 256).forEach(visit);
    }
    list(s.nodes, 256).forEach(id => { need(incoming[id] === 0); visit(id); });
  });
  need(selectedMeshes > 0 && triangles > 0);
  return { uris, estimatedGpuBytes: geometryBytes + Math.ceil(pixels * 16 / 3), estimatedWorkingBytes: working };
}
