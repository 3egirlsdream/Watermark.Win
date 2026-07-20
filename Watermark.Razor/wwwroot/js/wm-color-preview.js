// Shared by Android WebView, WKWebView and Windows WebView2. Color math is
// emitted by OpenColorIO; this module only owns WebGL resources and scheduling.
export const colorPipelineVersion = 3;

export function getRecommendedProxyEdge() {
  const viewport = Math.max(window.innerWidth || 0, window.innerHeight || 0);
  const pixels = viewport * Math.max(1, window.devicePixelRatio || 1);
  return Math.max(1600, Math.min(2560, Math.ceil(pixels / 128) * 128));
}

const vertexSource = `#version 300 es
in vec2 a_position;
out vec2 v_uv;
void main() {
  v_uv = vec2((a_position.x + 1.0) * 0.5, 1.0 - (a_position.y + 1.0) * 0.5);
  gl_Position = vec4(a_position, 0.0, 1.0);
}`;

function compile(gl, type, source) {
  const shader = gl.createShader(type);
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
    const reason = gl.getShaderInfoLog(shader) || "Shader compilation failed";
    gl.deleteShader(shader);
    throw new Error(reason);
  }
  return shader;
}

function createProgram(gl, fragmentSource) {
  const vertex = compile(gl, gl.VERTEX_SHADER, vertexSource);
  const fragment = compile(gl, gl.FRAGMENT_SHADER, fragmentSource);
  const program = gl.createProgram();
  gl.attachShader(program, vertex);
  gl.attachShader(program, fragment);
  gl.linkProgram(program);
  gl.deleteShader(vertex);
  gl.deleteShader(fragment);
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
    const reason = gl.getProgramInfoLog(program) || "Shader linking failed";
    gl.deleteProgram(program);
    throw new Error(reason);
  }
  return program;
}

function asFloat32(values) {
  return values instanceof Float32Array ? values : new Float32Array(values || []);
}

function asUint8(values) {
  if (values instanceof Uint8Array) return values;
  if (typeof values === "string") {
    const binary = atob(values);
    const result = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++) result[index] = binary.charCodeAt(index);
    return result;
  }
  return new Uint8Array(values || []);
}

function rgbaTextureData(values, width, height, depth, channels) {
  const source = asFloat32(values);
  if (channels === 4) return source;
  const pixelCount = width * Math.max(1, height) * Math.max(1, depth);
  const target = new Float32Array(pixelCount * 4);
  for (let pixel = 0; pixel < pixelCount; pixel++) {
    const sourceOffset = pixel * channels;
    const targetOffset = pixel * 4;
    target[targetOffset] = source[sourceOffset] ?? 0;
    target[targetOffset + 1] = channels > 1 ? source[sourceOffset + 1] : 0;
    target[targetOffset + 2] = channels > 2 ? source[sourceOffset + 2] : 0;
    target[targetOffset + 3] = 1;
  }
  return target;
}

function transposeMat3(values) {
  const value = asFloat32(values);
  return new Float32Array([
    value[0], value[3], value[6],
    value[1], value[4], value[7],
    value[2], value[5], value[8]
  ]);
}

export function createColorPreview(canvas, callback) {
  let gl;
  let capability;
  try {
    gl = canvas.getContext("webgl2", {
      alpha: true,
      antialias: false,
      depth: false,
      preserveDrawingBuffer: false,
      premultipliedAlpha: false
    });
    if (!gl) throw new Error("当前WebView不支持WebGL2");
    if (!gl.getExtension("OES_texture_float_linear"))
      throw new Error("当前GPU不支持浮点纹理线性采样");
    // The standard renderer string is sufficient for cache partitioning and
    // diagnostics. Some Android emulator/WebView combinations expose
    // WEBGL_debug_renderer_info but reject its enum with GL_INVALID_ENUM.
    const renderer = gl.getParameter(gl.RENDERER);
    capability = {
      supported: true,
      max3DTextureSize: gl.getParameter(gl.MAX_3D_TEXTURE_SIZE),
      maxTextureUnits: gl.getParameter(gl.MAX_TEXTURE_IMAGE_UNITS),
      pipelineVersion: colorPipelineVersion,
      validated: false,
      renderer,
      environmentKey: `${renderer}|${navigator.userAgent}`
    };
  } catch (error) {
    capability = {
      supported: false,
      reason: error?.message || String(error),
      max3DTextureSize: 0,
      pipelineVersion: colorPipelineVersion,
      validated: true
    };
  }

  if (!capability.supported) {
    return {
      getCapability: () => capability,
      getPipelineVersion: () => colorPipelineVersion,
      setSource: () => {},
      setProgram: () => {},
      render: () => {},
      dispose: () => {}
    };
  }

  const supportedCapability = { ...capability };
  let program = null;
  let programCacheId = null;
  let positionBuffer = null;
  let sourceTexture = null;
  let latestSnapshot = null;
  let pendingDynamicSnapshot = null;
  let latestSource = null;
  let latestSourceImage = null;
  let sourceEpoch = 0;
  let frame = 0;
  let drawCount = 0;
  let disposed = false;
  let restoreFailures = 0;
  const ocioTextures = new Map();

  function initializePersistentResources() {
    positionBuffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, positionBuffer);
    gl.bufferData(gl.ARRAY_BUFFER,
      new Float32Array([-1, -1, 1, -1, -1, 1, -1, 1, 1, -1, 1, 1]),
      gl.STATIC_DRAW);
    sourceTexture = gl.createTexture();
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, sourceTexture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  }

  function deleteProgramResources() {
    if (program) gl.deleteProgram(program);
    program = null;
    programCacheId = null;
    for (const item of ocioTextures.values()) gl.deleteTexture(item.texture);
    ocioTextures.clear();
  }

  function bindGeometry() {
    const position = gl.getAttribLocation(program, "a_position");
    if (position < 0) throw new Error("OCIO Shader缺少 a_position 输入");
    gl.bindBuffer(gl.ARRAY_BUFFER, positionBuffer);
    gl.enableVertexAttribArray(position);
    gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 0, 0);
  }

  function setUniform(uniform) {
    const location = gl.getUniformLocation(program, uniform.name);
    if (location === null) return;
    const values = asFloat32(uniform.values);
    switch (uniform.type) {
      case 1:
        gl.uniform1f(location, values[0] ?? 0);
        break;
      case 2:
        gl.uniform1i(location, values[0] ? 1 : 0);
        break;
      case 3:
        gl.uniform3fv(location, values);
        break;
      case 4:
        gl.uniform1fv(location, values);
        break;
      case 5:
        gl.uniform1iv(location, Int32Array.from(values));
        break;
      case 6:
        gl.uniformMatrix3fv(location, false, transposeMat3(values));
        break;
      default:
        throw new Error(`不支持的OCIO Uniform类型：${uniform.type}`);
    }
  }

  function uploadTexture(description, unit) {
    const target = description.dimension === 3 ? gl.TEXTURE_3D : gl.TEXTURE_2D;
    const dynamic = description.samplerName?.startsWith("wm_dynamic_") === true;
    if (unit >= capability.maxTextureUnits)
      throw new Error(`OCIO纹理数量超过GPU上限 ${capability.maxTextureUnits}`);
    let item = ocioTextures.get(description.samplerName);
    if (!item || item.target !== target) {
      if (item) gl.deleteTexture(item.texture);
      item = { texture: gl.createTexture(), target, cacheId: null, width: 0, height: 0, depth: 0 };
      ocioTextures.set(description.samplerName, item);
    }
    gl.activeTexture(gl.TEXTURE0 + unit);
    gl.bindTexture(target, item.texture);
    gl.texParameteri(target, gl.TEXTURE_MIN_FILTER,
      description.interpolation === 1 ? gl.NEAREST : gl.LINEAR);
    gl.texParameteri(target, gl.TEXTURE_MAG_FILTER,
      description.interpolation === 1 ? gl.NEAREST : gl.LINEAR);
    gl.texParameteri(target, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(target, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    if (target === gl.TEXTURE_3D) gl.texParameteri(target, gl.TEXTURE_WRAP_R, gl.CLAMP_TO_EDGE);

    if (dynamic || item.cacheId !== programCacheId) {
      const width = Math.max(1, description.width || 1);
      const height = Math.max(1, description.height || 1);
      const depth = Math.max(1, description.depth || 1);
      const rgba = rgbaTextureData(description.values, width, height, depth,
        Math.max(1, description.channels || 1));
      const sameSize = item.width === width && item.height === height && item.depth === depth;
      if (target === gl.TEXTURE_3D) {
        if (width > capability.max3DTextureSize)
          throw new Error(`OCIO 3D LUT ${width} 超过GPU上限 ${capability.max3DTextureSize}`);
        if (sameSize)
          gl.texSubImage3D(target, 0, 0, 0, 0, width, height, depth, gl.RGBA, gl.FLOAT, rgba);
        else
          gl.texImage3D(target, 0, gl.RGBA32F, width, height, depth, 0, gl.RGBA, gl.FLOAT, rgba);
      } else {
        if (sameSize)
          gl.texSubImage2D(target, 0, 0, 0, width, height, gl.RGBA, gl.FLOAT, rgba);
        else
          gl.texImage2D(target, 0, gl.RGBA32F, width, height, 0, gl.RGBA, gl.FLOAT, rgba);
      }
      item.cacheId = programCacheId;
      item.width = width;
      item.height = height;
      item.depth = depth;
    }
    const sampler = gl.getUniformLocation(program, description.samplerName);
    if (sampler !== null) gl.uniform1i(sampler, unit);
  }

  function setProgram(snapshot) {
    if (disposed || !snapshot) return;
    latestSnapshot = snapshot;
    pendingDynamicSnapshot = null;
    if (snapshot.pipelineVersion !== colorPipelineVersion)
      throw new Error(`调色Pipeline版本不一致：JS=${colorPipelineVersion}，OCIO=${snapshot.pipelineVersion}`);

    if (!program || programCacheId !== snapshot.shaderCacheId) {
      deleteProgramResources();
      program = createProgram(gl, snapshot.fragmentProgram);
      programCacheId = snapshot.shaderCacheId;
      gl.useProgram(program);
      bindGeometry();
      const source = gl.getUniformLocation(program, "wm_source");
      if (source === null) throw new Error("OCIO Shader缺少 wm_source 纹理");
      gl.uniform1i(source, 0);
    } else {
      gl.useProgram(program);
    }

    let unit = 1;
    for (const texture of snapshot.textures || []) uploadTexture(texture, unit++);
    for (const uniform of snapshot.uniforms || []) setUniform(uniform);
    requestRender();
  }

  function isDynamicTexture(texture) {
    return texture?.samplerName?.startsWith("wm_dynamic_") === true;
  }

  function setDynamicState(snapshot) {
    if (disposed || !snapshot) return;
    if (!program || !latestSnapshot)
      throw new Error("OCIO静态Shader尚未初始化");
    const uniforms = snapshot.uniforms ?? snapshot.Uniforms ?? [];
    const dynamicTextures = snapshot.textures ?? snapshot.Textures ?? [];
    const staticTextures = (latestSnapshot.textures ?? latestSnapshot.Textures ?? [])
      .filter(texture => !isDynamicTexture(texture));
    latestSnapshot = {
      ...latestSnapshot,
      uniforms,
      textures: [...staticTextures, ...dynamicTextures]
    };
    pendingDynamicSnapshot = latestSnapshot;
    requestRender();
  }

  function applyPendingDynamicState() {
    const snapshot = pendingDynamicSnapshot;
    pendingDynamicSnapshot = null;
    if (!snapshot) return;
    gl.useProgram(program);
    let unit = 1;
    for (const texture of snapshot.textures || []) uploadTexture(texture, unit++);
    for (const uniform of snapshot.uniforms || []) setUniform(uniform);
  }

  function draw() {
    frame = 0;
    if (disposed || !program || gl.isContextLost() || canvas.width === 0 || canvas.height === 0) return;
    const startedAt = performance.now();
    applyPendingDynamicState();
    gl.useProgram(program);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, sourceTexture);
    gl.viewport(0, 0, canvas.width, canvas.height);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT);
    gl.drawArrays(gl.TRIANGLES, 0, 6);
    drawCount += 1;
    if (drawCount === 1 || drawCount % 60 === 0)
      callback?.invokeMethodAsync?.("OnFrameMeasured", performance.now() - startedAt);
  }

  function requestRender() {
    if (!frame && !disposed) frame = requestAnimationFrame(draw);
  }

  function uploadSourceImage(image) {
    const startedAt = performance.now();
    canvas.width = image.naturalWidth;
    canvas.height = image.naturalHeight;
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, sourceTexture);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, image);
    requestRender();
    callback?.invokeMethodAsync?.("OnSourceReady", latestSource, performance.now() - startedAt);
  }

  function setSource(url) {
    if (url === latestSource && latestSourceImage?.naturalWidth > 0) return;
    latestSource = url;
    const epoch = ++sourceEpoch;
    const image = new Image();
    image.decoding = "async";
    image.onload = () => {
      if (disposed || epoch !== sourceEpoch) return;
      latestSourceImage = image;
      uploadSourceImage(image);
    };
    image.onerror = () => {
      if (epoch === sourceEpoch)
        callback?.invokeMethodAsync?.("OnContextStateChanged", false, "无法加载调色预览代理图");
    };
    image.src = url;
  }

  function validate(request) {
    if (disposed || gl.isContextLost()) throw new Error("WebGL上下文不可用于OCIO校验");
    const width = Number(request?.width ?? request?.Width ?? 0);
    const height = Number(request?.height ?? request?.Height ?? 0);
    const snapshot = request?.program ?? request?.Program;
    const source = asUint8(request?.sourceRgba ?? request?.SourceRgba);
    if (width < 1 || height < 1 || source.length !== width * height * 4)
      throw new Error("OCIO GPU校验图尺寸或像素数据无效");

    setProgram(snapshot);
    if (frame) cancelAnimationFrame(frame);
    frame = 0;
    canvas.width = width;
    canvas.height = height;
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, sourceTexture);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0,
      gl.RGBA, gl.UNSIGNED_BYTE, source);
    gl.useProgram(program);
    gl.viewport(0, 0, width, height);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT);
    gl.drawArrays(gl.TRIANGLES, 0, 6);
    gl.finish();

    const raw = new Uint8Array(source.length);
    gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, raw);
    const result = new Uint8Array(raw.length);
    const stride = width * 4;
    for (let row = 0; row < height; row++)
      result.set(raw.subarray((height - row - 1) * stride, (height - row) * stride), row * stride);
    return result;
  }

  function restore() {
    try {
      capability = { ...supportedCapability, validated: false, reason: null };
      initializePersistentResources();
      const snapshot = latestSnapshot;
      latestSnapshot = null;
      if (snapshot) setProgram(snapshot);
      if (latestSourceImage?.naturalWidth > 0) uploadSourceImage(latestSourceImage);
      else if (latestSource) setSource(latestSource);
      restoreFailures = 0;
    } catch (error) {
      restoreFailures += 1;
      capability = {
        ...capability,
        supported: restoreFailures < 2,
        reason: error?.message || String(error),
        validated: true
      };
    }
  }

  initializePersistentResources();

  canvas.addEventListener("webglcontextlost", event => {
    event.preventDefault();
    if (frame) cancelAnimationFrame(frame);
    frame = 0;
    program = null;
    pendingDynamicSnapshot = null;
    positionBuffer = null;
    sourceTexture = null;
    ocioTextures.clear();
    capability = { ...capability, supported: false, reason: "WebGL上下文已丢失", validated: false };
    callback?.invokeMethodAsync?.("OnContextStateChanged", false, capability.reason);
  });

  canvas.addEventListener("webglcontextrestored", () => {
    restore();
    if (capability.supported !== false) {
      capability = { ...capability, supported: true, reason: null, validated: false };
      callback?.invokeMethodAsync?.("OnContextStateChanged", true, null);
    } else {
      callback?.invokeMethodAsync?.("OnContextStateChanged", false, capability.reason);
    }
  });

  return {
    getCapability: () => capability,
    getPipelineVersion: () => colorPipelineVersion,
    setSource,
    setProgram,
    setDynamicState,
    validate,
    render: requestRender,
    dispose() {
      disposed = true;
      sourceEpoch += 1;
      latestSourceImage = null;
      if (frame) cancelAnimationFrame(frame);
      deleteProgramResources();
      if (sourceTexture) gl.deleteTexture(sourceTexture);
      if (positionBuffer) gl.deleteBuffer(positionBuffer);
    }
  };
}

export function coalesceFrameState(current, next) {
  return { ...(current || {}), ...(next || {}) };
}
