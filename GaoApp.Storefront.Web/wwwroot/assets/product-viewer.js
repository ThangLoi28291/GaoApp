(() => {
  'use strict';
  const root = document.querySelector('[data-product-viewer]');
  const openButton = document.getElementById('product-viewer-open');
  if (!root || !openButton) return;
  const stage = document.getElementById('product-viewer-stage');
  const photo = document.getElementById('detail-image');
  const choice = document.getElementById('product-model-choice');
  const closeButton = document.getElementById('product-viewer-close');
  const tools = document.getElementById('product-viewer-tools');
  const toolButtons = [...tools.querySelectorAll('button')];
  const status = document.getElementById('product-viewer-status');
  const allowed = new Set([...choice.options].map(x => x.value));
  const maxBytes = 8 * 1024 * 1024, maxGpu = 64 * 1024 * 1024;
  let modules, generation = 0, current = null, parseSettlement = Promise.resolve(), footprint = false;
  closeButton.textContent = 'Thu gọn phần xem 3D';
  function getModules() {
    if (!modules) {
      const shared = Promise.all([import('./vendor/three-r187/build/three.module.js'), import('./vendor/three-r187/controls/OrbitControls.js'), import('./vendor/three-r187/loaders/GLTFLoader.js'), import('./product-model-policy.js')]).catch(error => { if (modules === shared) modules = null; throw error; });
      modules = shared;
    }
    return modules;
  }
  function visible() {
    const rect = root.querySelector('.detail-main-image').getBoundingClientRect();
    return rect.width > 0 && rect.height > 0 && rect.bottom > 0 && rect.top < innerHeight && rect.right > 0 && rect.left < innerWidth;
  }
  function state(value, message, mediaId) {
    root.dataset.state = status.dataset.state = value; status.textContent = message;
    root.dispatchEvent(new CustomEvent('storefront-viewer-state', { bubbles: true, detail: Object.freeze({ state: value, mediaId: mediaId || null, generation }) }));
  }
  function disposeScene(scene) {
    if (!scene) return;
    const geometries = new Set(), materials = new Set(), textures = new Set(), images = new Set();
    scene.traverse(object => {
      if (object.geometry) geometries.add(object.geometry);
      for (const material of Array.isArray(object.material) ? object.material : [object.material]) if (material) {
        materials.add(material); Object.values(material).forEach(value => { if (value?.isTexture) textures.add(value); });
      }
    });
    textures.forEach(texture => { if (texture.image) images.add(texture.image); texture.dispose(); });
    images.forEach(image => { if (typeof image.close === 'function') image.close(); });
    materials.forEach(material => material.dispose()); geometries.forEach(geometry => geometry.dispose());
  }
  function destroy(load) {
    if (!load) return;
    load.controller.abort(); clearTimeout(load.timer); cancelAnimationFrame(load.frame);
    load.resize?.disconnect(); load.controls?.dispose();
    for (const scene of load.scenes || []) disposeScene(scene);
    if (load.renderer) { load.renderer.setAnimationLoop(null); load.renderer.dispose(); load.renderer.forceContextLoss(); }
    load.canvas?.remove(); load.urls.forEach(url => URL.revokeObjectURL(url)); load.urls.clear();
    disposePending(load);
  }
  function disposePending(load) {
    // Inspect only dependencies the loader already started, without requesting
    // unused assets. Settled and late partial resources also get disposed on error.
    const parser = load.parser; if (!parser) return;
    const later = (promise, dispose) => { if (promise) Promise.resolve(promise).then(dispose, () => {}); };
    for (const [type, plural] of [['scene', 'scenes'], ['mesh', 'meshes'], ['node', 'nodes']]) for (let i = 0; i < (parser.json[plural]?.length || 0); i++) later(parser.cache.get(`${type}:${i}`), disposeScene);
    for (let i = 0; i < (parser.json.materials?.length || 0); i++) later(parser.cache.get(`material:${i}`), material => material?.dispose());
    for (let i = 0; i < (parser.json.textures?.length || 0); i++) later(parser.cache.get(`texture:${i}`), texture => { texture?.dispose(); texture?.image?.close?.(); });
    Object.values(parser.sourceCache).forEach(promise => later(promise, texture => { texture?.dispose(); texture?.image?.close?.(); }));
    Object.values(parser.primitiveCache).forEach(entry => later(entry.promise, geometry => geometry?.dispose()));
  }
  async function settleStarted(load) {
    const parser = load.parser; if (!parser) return;
    const settled = new Set();
    while (true) {
      const pending = [];
      const add = promise => { if (promise && typeof promise.then === 'function' && !settled.has(promise)) { settled.add(promise); pending.push(promise); } };
      for (const [type, plural] of [['scene', 'scenes'], ['mesh', 'meshes'], ['node', 'nodes'], ['material', 'materials'], ['texture', 'textures'], ['accessor', 'accessors'], ['bufferView', 'bufferViews'], ['buffer', 'buffers'], ['camera', 'cameras']])
        for (let i = 0; i < (parser.json[plural]?.length || 0); i++) add(parser.cache.get(`${type}:${i}`));
      Object.values(parser.sourceCache).forEach(add); Object.values(parser.nodeCache).forEach(add);
      Object.values(parser.primitiveCache).forEach(entry => add(entry.promise));
      if (!pending.length) break;
      // No dependency getter is called: only work the loader already started.
      // A rejected Promise.all can leave siblings running, so repeat after all
      // captured promises settle to include dependencies they started meanwhile.
      await Promise.allSettled(pending);
    }
    if (current !== load) disposePending(load);
  }
  function close(message = 'Đang xem ảnh sản phẩm. Chọn xem 3D khi bạn cần.', nextState = 'closed', clearFootprint = false) {
    const old = current; current = null; generation++; destroy(old);
    if (clearFootprint) footprint = false;
    root.dataset.viewerFootprint = footprint ? 'expanded' : 'compact';
    stage.hidden = true; photo.hidden = false; tools.hidden = !footprint; closeButton.hidden = !footprint;
    toolButtons.forEach(button => { button.disabled = true; });
    openButton.removeAttribute('aria-busy'); openButton.textContent = 'Xem 3D';
    state(nextState, message, old?.mediaId);
  }
  function fresh(load) {
    return current === load && generation === load.generation && !load.controller.signal.aborted && !document.hidden && visible() && performance.now() < load.deadline;
  }
  function requireFresh(load) { if (!fresh(load)) throw new DOMException('Viewer load ended', 'AbortError'); }
  async function readModel(response, load) {
    if (!response.ok || response.redirected) throw new Error('Chưa tải được mô hình. Bạn có thể tiếp tục chọn mua bằng ảnh.');
    const length = Number(response.headers.get('Content-Length'));
    if (Number.isFinite(length) && length > maxBytes) throw new Error('Mô hình vượt giới hạn tải trên thiết bị.');
    const reader = response.body?.getReader(); if (!reader) throw new Error('Trình duyệt chưa hỗ trợ đọc mô hình.');
    const chunks = []; let total = 0;
    try {
      while (true) {
        requireFresh(load); const { value, done } = await reader.read(); requireFresh(load); if (done) break;
        total += value.byteLength; if (total > maxBytes) throw new Error('Mô hình vượt giới hạn tải trên thiết bị.'); chunks.push(value);
      }
    } catch (error) { await reader.cancel().catch(() => {}); throw error; }
    finally { reader.releaseLock(); }
    if (!total) throw new Error('Mô hình chưa có dữ liệu.');
    const bytes = new Uint8Array(total); let offset = 0; chunks.forEach(chunk => { bytes.set(chunk, offset); offset += chunk.length; }); return bytes.buffer;
  }
  function loadedBudget(scene) {
    let vertices = 0, triangles = 0, bytes = 0, pixels = 0;
    const geometry = new Set(), textures = new Set();
    scene.traverse(object => {
      if (!object.isMesh) return;
      const g = object.geometry, position = g?.getAttribute('position');
      if (!position || object.isSkinnedMesh || object.isInstancedMesh) throw new Error('Mô hình có cấu trúc chưa hỗ trợ.');
      vertices += position.count; triangles += (g.index?.count ?? position.count) / 3;
      if (vertices > 100000 || triangles > 100000) throw new Error('Mô hình quá chi tiết cho trình xem.');
      if (!geometry.has(g)) {
        geometry.add(g);
        for (const attribute of Object.values(g.attributes)) bytes += attribute.isInterleavedBufferAttribute ? attribute.data.array.byteLength : attribute.array.byteLength;
        if (g.index) bytes += g.index.isInterleavedBufferAttribute ? g.index.data.array.byteLength : g.index.array.byteLength;
      }
      for (const material of Array.isArray(object.material) ? object.material : [object.material]) if (material)
        Object.values(material).forEach(texture => { if (texture?.isTexture) textures.add(texture); });
    });
    textures.forEach(texture => {
      const image = texture.image, width = image?.width, height = image?.height;
      if (!Number.isSafeInteger(width) || !Number.isSafeInteger(height) || width < 1 || height < 1 || width > 2048 || height > 2048) throw new Error('Ảnh bề mặt chưa phù hợp.');
      pixels += width * height;
    });
    if (!vertices || !triangles || pixels > 8000000 || bytes + Math.ceil(pixels * 16 / 3) > maxGpu) throw new Error('Mô hình vượt ngân sách hiển thị.');
  }
  function dimensions(load) {
    const rect = stage.getBoundingClientRect();
    const width = Math.max(1, Math.floor(rect.width)), height = Math.max(1, Math.floor(rect.height));
    const mobile = matchMedia('(max-width: 700px)').matches;
    const ratio = Math.min(devicePixelRatio || 1, mobile ? 1.5 : 2, Math.sqrt(1000000 / (width * height)));
    load.renderer.setPixelRatio(ratio); load.renderer.setSize(width, height, false);
    load.camera.aspect = width / height; load.camera.updateProjectionMatrix();
  }
  function render(load) {
    if (current !== load || root.dataset.state !== 'ready' || document.hidden || load.frame) return;
    load.frame = requestAnimationFrame(() => {
      load.frame = 0;
      if (current !== load || root.dataset.state !== 'ready' || document.hidden) return;
      try { load.renderer.render(load.scene, load.camera); } catch { close('Thiết bị chưa hiển thị được 3D. Ảnh và phần chọn mua vẫn dùng được.', 'fallback'); }
    });
  }
  function move(action) {
    const load = current; if (!load?.controls || root.dataset.state !== 'ready') return;
    if (action === 'reset') load.controls.reset();
    else {
      const offset = load.camera.position.clone().sub(load.controls.target);
      const spherical = new load.THREE.Spherical().setFromVector3(offset);
      if (action === 'left') spherical.theta += Math.PI / 12;
      if (action === 'right') spherical.theta -= Math.PI / 12;
      if (action === 'up') spherical.phi -= Math.PI / 12;
      if (action === 'down') spherical.phi += Math.PI / 12;
      if (action === 'in') spherical.radius *= 0.8;
      if (action === 'out') spherical.radius *= 1.25;
      spherical.makeSafe(); spherical.radius = Math.max(load.controls.minDistance, Math.min(load.controls.maxDistance, spherical.radius));
      load.camera.position.copy(load.controls.target).add(offset.setFromSpherical(spherical)); load.controls.update();
    }
    render(load);
  }
  async function open() {
    const mediaId = choice.value;
    if (!allowed.has(mediaId) || !/^[1-9]\d{0,9}$/.test(mediaId) || Number(mediaId) > 2147483647) return;
    close();
    if (!visible()) root.querySelector('.detail-main-image').scrollIntoView({ block: 'nearest', behavior: 'instant' });
    const load = { mediaId, generation, controller: new AbortController(), deadline: performance.now() + 15000, urls: new Set(), scenes: [], frame: 0 };
    current = load; footprint = true; root.dataset.viewerFootprint = 'expanded'; closeButton.hidden = false; openButton.setAttribute('aria-busy', 'true'); openButton.textContent = 'Đang tải 3D…';
    // Expand the final control footprint during the trusted click, not after
    // a long module/model load. Loading buttons remain legible but inactive.
    tools.hidden = false; toolButtons.forEach(button => { button.disabled = true; });
    state('loading', 'Đang tải mô hình. Bạn vẫn có thể xem ảnh và chọn mua.', mediaId);
    load.timer = setTimeout(() => { if (current === load) close('Tải 3D hơi lâu. Hãy thử lại khi kết nối ổn định; phần chọn mua vẫn dùng được.', 'fallback'); }, 15000);
    try {
      // Shared module downloads can finish after close; they cannot apply stale work.
      const [THREE, { OrbitControls }, { GLTFLoader }, { inspectModel }] = await getModules(); requireFresh(load);
      // Native parse/decode cannot be interrupted. Serialize it across generations
      // so repeated open/close cannot accumulate concurrent model decoders.
      await parseSettlement; requireFresh(load);
      const response = await fetch(`/shop/media/${mediaId}`, { signal: load.controller.signal, credentials: 'same-origin', cache: 'no-store', redirect: 'error' }); requireFresh(load);
      const buffer = await readModel(response, load); requireFresh(load);
      const mime = (response.headers.get('Content-Type') || '').split(';')[0].trim();
      const inspection = inspectModel(buffer, mime); requireFresh(load);
      const manager = new THREE.LoadingManager(), dataUrls = new Map();
      manager.setURLModifier(url => {
        if (inspection.uris.has(url)) {
          requireFresh(load);
          if (!dataUrls.has(url)) { const item = inspection.uris.get(url), blob = URL.createObjectURL(new Blob([item.bytes], { type: item.mime })); dataUrls.set(url, blob); load.urls.add(blob); }
          return dataUrls.get(url);
        }
        // The inspected payload cannot contain blob URIs. These are generated
        // internally by this loader from already inspected embedded image bytes.
        if (url.startsWith('blob:') && new URL(url).origin === location.origin) {
          load.urls.add(url); if (!fresh(load)) { URL.revokeObjectURL(url); throw new DOMException('Viewer load ended', 'AbortError'); } return url;
        }
        throw new Error('Mô hình yêu cầu tài nguyên bên ngoài chưa được cho phép.');
      });
      const loader = new GLTFLoader(manager);
      loader.register(parser => { load.parser = parser; return { name: 'StorefrontResourceOwnership' }; });
      const parsed = new Promise((resolve, reject) => loader.parse(buffer, '', resolve, reject));
      parseSettlement = parsed.then(() => settleStarted(load), () => settleStarted(load));
      const gltf = await parsed;
      load.scenes = gltf.scenes || [gltf.scene];
      if (!fresh(load)) { load.scenes.forEach(disposeScene); return; }
      loadedBudget(gltf.scene); requireFresh(load);
      load.THREE = THREE; load.scene = gltf.scene; load.scene.updateMatrixWorld(true);
      const box = new THREE.Box3().setFromObject(load.scene), center = box.getCenter(new THREE.Vector3()), size = box.getSize(new THREE.Vector3());
      const extent = Math.max(size.x, size.y, size.z);
      if (!Number.isFinite(extent) || extent <= 0 || ![center.x, center.y, center.z].every(Number.isFinite)) throw new Error('Kích thước mô hình chưa phù hợp.');
      load.scene.add(new THREE.HemisphereLight(0xffffff, 0x4a6054, 2.6));
      const light = new THREE.DirectionalLight(0xffffff, 3); light.position.set(center.x + extent, center.y + extent * 2, center.z + extent); load.scene.add(light);
      load.camera = new THREE.PerspectiveCamera(40, 1, extent / 1000, extent * 100);
      load.camera.position.copy(center).add(new THREE.Vector3(extent * 1.3, extent * 0.8, extent * 1.8));
      const canvas = document.createElement('canvas'); canvas.id = 'product-viewer-canvas'; canvas.tabIndex = 0; canvas.setAttribute('role', 'img'); canvas.setAttribute('aria-label', 'Mô hình sản phẩm 3D. Mũi tên để xoay, cộng trừ để thu phóng, Home đặt lại, Escape về ảnh.');
      load.canvas = canvas; stage.append(canvas);
      canvas.addEventListener('webglcontextlost', event => { event.preventDefault(); if (current === load) close('Kết nối đồ họa đã dừng. Đang trở về ảnh sản phẩm.', 'fallback'); });
      load.renderer = new THREE.WebGLRenderer({ canvas, alpha: false, antialias: false, powerPreference: 'low-power' });
      load.renderer.setClearColor('#f1f6f2', 1);
      load.renderer.outputColorSpace = THREE.SRGBColorSpace;
      load.controls = new OrbitControls(load.camera, canvas); load.controls.target.copy(center);
      load.controls.enableDamping = false; load.controls.autoRotate = false; load.controls.enablePan = false;
      load.controls.minDistance = extent * 0.4; load.controls.maxDistance = extent * 8; load.controls.update(); load.controls.saveState();
      load.controls.addEventListener('change', () => render(load));
      canvas.addEventListener('keydown', event => {
        const action = ({ ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'up', ArrowDown: 'down', '+': 'in', '=': 'in', '-': 'out', Home: 'reset' })[event.key];
        if (event.key === 'Escape') { event.preventDefault(); close(undefined, 'closed', true); openButton.focus(); }
        else if (action) { event.preventDefault(); move(action); }
      });
      stage.hidden = false; photo.hidden = true; dimensions(load); requireFresh(load);
      load.renderer.render(load.scene, load.camera); requireFresh(load);
      clearTimeout(load.timer); toolButtons.forEach(button => { button.disabled = false; }); openButton.removeAttribute('aria-busy'); openButton.textContent = 'Tải lại 3D';
      state('ready', 'Kéo để xoay hoặc dùng các nút điều khiển. Chọn mua theo biến thể và đơn vị bên cạnh.', mediaId);
      load.resize = new ResizeObserver(() => { if (current === load) { dimensions(load); render(load); } }); load.resize.observe(stage);
    } catch (error) {
      if (current === load) close(error?.name === 'AbortError' ? 'Đã dừng tải 3D. Bạn vẫn có thể chọn mua bằng ảnh.' : 'Chưa hiển thị được mô hình này. Ảnh sản phẩm và phần chọn mua vẫn dùng được.', 'fallback');
    }
  }
  openButton.addEventListener('click', open); closeButton.addEventListener('click', () => { close(undefined, 'closed', true); openButton.focus(); });
  choice.addEventListener('change', () => { const active = current !== null; close('Đã đổi góc xem. Chọn xem 3D để tải mô hình.'); if (active) open(); });
  for (const [suffix, action] of [['rotate-left', 'left'], ['rotate-right', 'right'], ['zoom-in', 'in'], ['zoom-out', 'out'], ['reset', 'reset']]) document.getElementById(`product-viewer-${suffix}`).addEventListener('click', () => move(action));
  root.addEventListener('storefront-gallery-photo', () => close(undefined, 'closed', true));
  document.addEventListener('visibilitychange', () => { if (document.hidden && current) close('Đã dừng 3D khi chuyển tab. Bạn có thể mở lại khi cần.'); });
  window.addEventListener('pagehide', () => close());
  const observer = new IntersectionObserver(() => { if (current && !visible()) close('Đã dừng 3D khi ảnh ra ngoài màn hình. Chọn xem 3D để mở lại.'); });
  observer.observe(root.querySelector('.detail-main-image'));
})();
