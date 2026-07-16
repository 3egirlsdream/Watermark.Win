// Shared by Android WebView, WKWebView and Windows WebView2.
export const colorPipelineVersion = 2;

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

const fragmentSource = `#version 300 es
precision highp float;
precision highp sampler3D;
in vec2 v_uv;
out vec4 outColor;
uniform sampler2D u_source;
uniform sampler3D u_lut;
uniform sampler2D u_autoCurves;
uniform sampler2D u_userCurves;
uniform float u_lutSize;
uniform float u_autoGrade[10];
uniform float u_userGrade[10];
uniform vec3 u_autoHsl[8];
uniform vec3 u_userHsl[8];

float clamp01(float value) { return clamp(value, 0.0, 1.0); }
float luminance(vec3 color) { return dot(color, vec3(0.2126, 0.7152, 0.0722)); }
vec3 srgbToLinear(vec3 color) {
  return mix(color / 12.92, pow((color + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), color));
}
vec3 linearToSrgb(vec3 color) {
  color = clamp(color, 0.0, 1.0);
  return mix(color * 12.92, 1.055 * pow(color, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), color));
}

vec3 rgbToHsl(vec3 color) {
  float maxValue = max(color.r, max(color.g, color.b));
  float minValue = min(color.r, min(color.g, color.b));
  float light = (maxValue + minValue) * 0.5;
  float delta = maxValue - minValue;
  if (delta < 0.00001) return vec3(0.0, 0.0, light);
  float saturation = light > 0.5 ? delta / (2.0 - maxValue - minValue) : delta / (maxValue + minValue);
  float hue;
  if (maxValue == color.r) hue = (color.g - color.b) / delta + (color.g < color.b ? 6.0 : 0.0);
  else if (maxValue == color.g) hue = (color.b - color.r) / delta + 2.0;
  else hue = (color.r - color.g) / delta + 4.0;
  return vec3(hue / 6.0, saturation, light);
}

float hueToRgb(float p, float q, float t) {
  if (t < 0.0) t += 1.0;
  if (t > 1.0) t -= 1.0;
  if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
  if (t < 0.5) return q;
  if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
  return p;
}

vec3 hslToRgb(vec3 hsl) {
  if (hsl.y <= 0.00001) return vec3(hsl.z);
  float q = hsl.z < 0.5 ? hsl.z * (1.0 + hsl.y) : hsl.z + hsl.y - hsl.z * hsl.y;
  float p = 2.0 * hsl.z - q;
  return vec3(hueToRgb(p, q, hsl.x + 1.0 / 3.0), hueToRgb(p, q, hsl.x), hueToRgb(p, q, hsl.x - 1.0 / 3.0));
}

float curveValue(bool automatic, int channel, float value) {
  float coordinate = (clamp01(value) * 4095.0 + 0.5) / 4096.0;
  vec4 curves = automatic
    ? texture(u_autoCurves, vec2(coordinate, 0.5))
    : texture(u_userCurves, vec2(coordinate, 0.5));
  if (channel == 0) return curves.r;
  if (channel == 1) return curves.g;
  if (channel == 2) return curves.b;
  return curves.a;
}

float gradeValue(bool automatic, int index) {
  return automatic ? u_autoGrade[index] : u_userGrade[index];
}

vec3 hslValue(bool automatic, int index) {
  return automatic ? u_autoHsl[index] : u_userHsl[index];
}

vec3 applyGrade(vec3 color, bool automatic) {
  float exposure = exp2(gradeValue(automatic, 0));
  float contrast = 1.0 + gradeValue(automatic, 1) / 100.0;
  float temperature = gradeValue(automatic, 6) / 100.0;
  float tint = gradeValue(automatic, 7) / 100.0;
  color *= exposure * exp2(vec3(temperature * 0.5 + tint * 0.125, -tint * 0.25, -temperature * 0.5 + tint * 0.125));
  float lum = clamp01(luminance(color));
  float tonal = gradeValue(automatic, 3) / 100.0 * pow(1.0 - lum, 2.0) * 0.35
    + gradeValue(automatic, 2) / 100.0 * pow(lum, 2.0) * 0.35
    + gradeValue(automatic, 5) / 100.0 * pow(1.0 - lum, 4.0) * 0.22
    + gradeValue(automatic, 4) / 100.0 * pow(lum, 4.0) * 0.22;
  color += vec3(tonal);
  color = (color - vec3(0.18)) * contrast + vec3(0.18);
  float gray = luminance(color);
  float maxValue = max(color.r, max(color.g, color.b));
  float minValue = min(color.r, min(color.g, color.b));
  float existingSaturation = maxValue <= 0.0 ? 0.0 : (maxValue - minValue) / maxValue;
  float saturation = max(0.0, (1.0 + gradeValue(automatic, 9) / 100.0)
    * (1.0 + gradeValue(automatic, 8) / 100.0 * (1.0 - existingSaturation)));
  color = vec3(gray) + (color - vec3(gray)) * saturation;
  vec3 hsl = rgbToHsl(clamp(color, 0.0, 1.0));
  float centers[8] = float[8](0.0, 30.0, 60.0, 120.0, 180.0, 240.0, 285.0, 330.0);
  float degrees = hsl.x * 360.0;
  float weightSum = 0.0;
  float hueShift = 0.0;
  float saturationShift = 0.0;
  float luminanceShift = 0.0;
  for (int index = 0; index < 8; index++) {
    float distance = abs(degrees - centers[index]);
    distance = min(distance, 360.0 - distance);
    float weight = max(0.0, 1.0 - distance / 45.0);
    vec3 adjustment = hslValue(automatic, index);
    weightSum += weight;
    hueShift += adjustment.x * 0.3 * weight;
    saturationShift += adjustment.y / 100.0 * weight;
    luminanceShift += adjustment.z / 100.0 * weight;
  }
  if (weightSum > 0.0) {
    hsl.x = mod(mod(degrees + hueShift / weightSum, 360.0) + 360.0, 360.0) / 360.0;
    hsl.y = clamp01(hsl.y + saturationShift / weightSum * max(0.2, hsl.y));
    hsl.z = clamp01(hsl.z + luminanceShift / weightSum * 0.35);
  }
  color = hslToRgb(hsl);
  color = vec3(
    curveValue(automatic, 0, color.r),
    curveValue(automatic, 1, color.g),
    curveValue(automatic, 2, color.b));
  return vec3(
    curveValue(automatic, 3, color.r),
    curveValue(automatic, 3, color.g),
    curveValue(automatic, 3, color.b));
}

void main() {
  vec4 source = texture(u_source, v_uv);
  vec3 color = applyGrade(srgbToLinear(source.rgb), true);
  vec3 encoded = linearToSrgb(color);
  vec3 lutCoordinate = (clamp(encoded, 0.0, 1.0) * (u_lutSize - 1.0) + 0.5) / u_lutSize;
  color = srgbToLinear(texture(u_lut, lutCoordinate).rgb);
  color = applyGrade(color, false);
  outColor = vec4(linearToSrgb(color), source.a);
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

function createProgram(gl) {
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

function floatArray(value, expected, fallback = 0) {
  const result = new Float32Array(expected);
  result.fill(fallback);
  if (Array.isArray(value) || ArrayBuffer.isView(value)) result.set(Array.from(value).slice(0, expected));
  return result;
}

function curveTextureData(parameters) {
  const master = floatArray(parameters?.masterCurve, 4096);
  const red = floatArray(parameters?.redCurve, 4096);
  const green = floatArray(parameters?.greenCurve, 4096);
  const blue = floatArray(parameters?.blueCurve, 4096);
  const result = new Float32Array(4096 * 4);
  for (let index = 0; index < 4096; index++) {
    result[index * 4] = red[index];
    result[index * 4 + 1] = green[index];
    result[index * 4 + 2] = blue[index];
    result[index * 4 + 3] = master[index];
  }
  return result;
}

function lutTextureData(values, size) {
  const source = floatArray(values, size * size * size * 3);
  const result = new Float32Array(size * size * size * 4);
  for (let sourceIndex = 0, targetIndex = 0; sourceIndex < source.length; sourceIndex += 3, targetIndex += 4) {
    result[targetIndex] = source[sourceIndex];
    result[targetIndex + 1] = source[sourceIndex + 1];
    result[targetIndex + 2] = source[sourceIndex + 2];
    result[targetIndex + 3] = 1;
  }
  return result;
}

function createTexture(gl, target, unit) {
  const texture = gl.createTexture();
  gl.activeTexture(gl.TEXTURE0 + unit);
  gl.bindTexture(target, texture);
  gl.texParameteri(target, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
  gl.texParameteri(target, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  gl.texParameteri(target, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(target, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  if (target === gl.TEXTURE_3D) gl.texParameteri(target, gl.TEXTURE_WRAP_R, gl.CLAMP_TO_EDGE);
  return texture;
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
      premultipliedAlpha: true
    });
    if (!gl) throw new Error("当前WebView不支持WebGL2");
    if (!gl.getExtension("OES_texture_float_linear")) throw new Error("当前GPU不支持浮点纹理线性采样");
    const rendererExtension = gl.getExtension("WEBGL_debug_renderer_info");
    const renderer = rendererExtension
      ? gl.getParameter(rendererExtension.UNMASKED_RENDERER_WEBGL)
      : gl.getParameter(gl.RENDERER);
    capability = {
      supported: true,
      max3DTextureSize: gl.getParameter(gl.MAX_3D_TEXTURE_SIZE),
      pipelineVersion: colorPipelineVersion,
      validated: false,
      renderer,
      environmentKey: `${renderer}|${navigator.userAgent}`
    };
  } catch (error) {
    capability = { supported: false, reason: error?.message || String(error), max3DTextureSize: 0 };
  }

  if (!capability.supported) {
    return {
      getCapability: () => capability,
      getPipelineVersion: () => colorPipelineVersion,
      setSource: () => {},
      setGeneratedLook: () => {},
      setAdjustments: () => {},
      setGradeAndHsl: () => {},
      setCurve: () => {},
      render: () => {},
      dispose: () => {}
    };
  }

  const supportedCapability = { ...capability };

  let program;
  let buffer;
  let sourceTexture;
  let lutTexture;
  let autoCurveTexture;
  let userCurveTexture;

  function initializeResources() {
    program = createProgram(gl);
    gl.useProgram(program);
    const position = gl.getAttribLocation(program, "a_position");
    buffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, -1, 1, 1, -1, 1, 1]), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(position);
    gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 0, 0);
    sourceTexture = createTexture(gl, gl.TEXTURE_2D, 0);
    lutTexture = createTexture(gl, gl.TEXTURE_3D, 1);
    autoCurveTexture = createTexture(gl, gl.TEXTURE_2D, 2);
    userCurveTexture = createTexture(gl, gl.TEXTURE_2D, 3);
    gl.uniform1i(gl.getUniformLocation(program, "u_source"), 0);
    gl.uniform1i(gl.getUniformLocation(program, "u_lut"), 1);
    gl.uniform1i(gl.getUniformLocation(program, "u_autoCurves"), 2);
    gl.uniform1i(gl.getUniformLocation(program, "u_userCurves"), 3);
  }

  try {
    initializeResources();
  } catch (error) {
    capability = { supported: false, reason: error?.message || String(error), max3DTextureSize: capability.max3DTextureSize };
    return {
      getCapability: () => capability,
      getPipelineVersion: () => colorPipelineVersion,
      setSource: () => {},
      setGeneratedLook: () => {},
      setAdjustments: () => {},
      setGradeAndHsl: () => {},
      setCurve: () => {},
      render: () => {},
      dispose: () => {}
    };
  }

  let frame = 0;
  let disposed = false;
  let sourceEpoch = 0;
  let latestLook = null;
  let latestAdjustments = null;
  let latestSource = null;
  let latestSourceImage = null;
  let drawCount = 0;

  function uploadCurves(texture, parameters, unit) {
    gl.activeTexture(gl.TEXTURE0 + unit);
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA16F, 4096, 1, 0, gl.RGBA, gl.FLOAT, curveTextureData(parameters));
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  }

  function uploadParameters(prefix, parameters, curveTexture, unit) {
    gl.useProgram(program);
    gl.uniform1fv(gl.getUniformLocation(program, `u_${prefix}Grade[0]`), floatArray(parameters?.grade, 10));
    gl.uniform3fv(gl.getUniformLocation(program, `u_${prefix}Hsl[0]`), floatArray(parameters?.hsl, 24));
    uploadCurves(curveTexture, parameters, unit);
  }

  function draw() {
    frame = 0;
    if (disposed || gl.isContextLost() || canvas.width === 0 || canvas.height === 0) return;
    const startedAt = performance.now();
    gl.viewport(0, 0, canvas.width, canvas.height);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT);
    gl.drawArrays(gl.TRIANGLES, 0, 6);
    drawCount += 1;
    if (drawCount === 1 || drawCount % 60 === 0) {
      callback?.invokeMethodAsync?.("OnFrameMeasured", performance.now() - startedAt);
    }
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
    const uploadMilliseconds = performance.now() - startedAt;
    requestRender();
    callback?.invokeMethodAsync?.("OnSourceReady", latestSource, uploadMilliseconds);
  }

  function setSource(url) {
    latestSource = url;
    const epoch = ++sourceEpoch;
    const image = new Image();
    image.decoding = "async";
    image.onload = () => {
      if (disposed || epoch !== sourceEpoch) return;
      latestSourceImage = image;
      uploadSourceImage(image);
    };
    image.src = url;
  }

  function setSourcePixels(width, height, rgba) {
    canvas.width = width;
    canvas.height = height;
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, sourceTexture);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    const bytes = rgba instanceof Uint8Array ? rgba : new Uint8Array(rgba);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, bytes);
  }

  function setGeneratedLook(look) {
    latestLook = look;
    uploadParameters("auto", look?.automatic, autoCurveTexture, 2);
    const size = Math.max(2, Math.min(capability.max3DTextureSize, look?.lutSize || 2));
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_3D, lutTexture);
    gl.texImage3D(gl.TEXTURE_3D, 0, gl.RGBA16F, size, size, size, 0, gl.RGBA, gl.FLOAT, lutTextureData(look?.lutValues, size));
    gl.texParameteri(gl.TEXTURE_3D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_3D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.uniform1f(gl.getUniformLocation(program, "u_lutSize"), size);
    requestRender();
  }

  function setAdjustments(parameters) {
    latestAdjustments = parameters;
    uploadParameters("user", parameters, userCurveTexture, 3);
    requestRender();
  }

  function setGradeAndHsl(parameters) {
    latestAdjustments = parameters;
    gl.useProgram(program);
    gl.uniform1fv(gl.getUniformLocation(program, "u_userGrade[0]"), floatArray(parameters?.grade, 10));
    gl.uniform3fv(gl.getUniformLocation(program, "u_userHsl[0]"), floatArray(parameters?.hsl, 24));
    requestRender();
  }

  function setCurve(name, values) {
    if (!latestAdjustments || !["masterCurve", "redCurve", "greenCurve", "blueCurve"].includes(name)) return;
    latestAdjustments = { ...latestAdjustments, [name]: floatArray(values, 4096) };
    uploadCurves(userCurveTexture, latestAdjustments, 3);
    requestRender();
  }

  function restore() {
    try {
      capability = { ...supportedCapability, validated: false, reason: null };
      initializeResources();
      if (latestLook) setGeneratedLook(latestLook);
      if (latestAdjustments) setAdjustments(latestAdjustments);
      if (latestSourceImage?.naturalWidth > 0) uploadSourceImage(latestSourceImage);
      else if (latestSource) setSource(latestSource);
    } catch (error) {
      capability = { supported: false, reason: error?.message || String(error), max3DTextureSize: capability.max3DTextureSize };
    }
  }

  canvas.addEventListener("webglcontextlost", event => {
    event.preventDefault();
    if (frame) cancelAnimationFrame(frame);
    frame = 0;
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

  function readPixels() {
    const raw = new Uint8Array(canvas.width * canvas.height * 4);
    gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, raw);
    const topDown = new Uint8Array(raw.length);
    const rowBytes = canvas.width * 4;
    for (let y = 0; y < canvas.height; y++) {
      topDown.set(raw.subarray((canvas.height - 1 - y) * rowBytes, (canvas.height - y) * rowBytes), y * rowBytes);
    }
    return topDown;
  }

  function validatePipeline(request) {
    if (request?.pipelineVersion !== colorPipelineVersion) throw new Error("调色Pipeline版本不一致");
    setSourcePixels(request.width, request.height, request.sourceRgba);
    setGeneratedLook(request.look);
    setAdjustments(request.adjustments);
    if (frame) cancelAnimationFrame(frame);
    frame = 0;
    draw();
    gl.finish();
    return readPixels();
  }

  return {
    getCapability: () => capability,
    getPipelineVersion: () => colorPipelineVersion,
    setSource,
    setGeneratedLook,
    setAdjustments,
    setGradeAndHsl,
    setCurve,
    validatePipeline,
    readPixels,
    render: requestRender,
    dispose() {
      disposed = true;
      sourceEpoch++;
      latestSourceImage = null;
      if (frame) cancelAnimationFrame(frame);
      gl.deleteTexture(sourceTexture);
      gl.deleteTexture(lutTexture);
      gl.deleteTexture(autoCurveTexture);
      gl.deleteTexture(userCurveTexture);
      gl.deleteBuffer(buffer);
      gl.deleteProgram(program);
    }
  };
}

export function coalesceFrameState(current, next) {
  return { ...(current || {}), ...(next || {}) };
}
