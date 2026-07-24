const vendorUrl = "../vendor/moveable/moveable.min.js";
let vendorPromise;
const sceneBitmaps = new Map();
export const absoluteResizeDirections = Object.freeze([
  "n", "ne", "e", "se", "s", "sw", "w", "nw"
]);
export const coarseResizeDirections = Object.freeze(["nw", "ne", "se", "sw"]);

export async function publishSceneBitmap(key, streamReference, mimeType) {
  if (!key || typeof globalThis.createImageBitmap !== "function") return false;
  const buffer = await streamReference.arrayBuffer();
  const blob = new Blob([buffer], { type: mimeType || "application/octet-stream" });
  const bitmap = await createImageBitmap(blob);
  const previous = sceneBitmaps.get(key);
  previous?.close?.();
  sceneBitmaps.set(key, bitmap);
  return true;
}

export function bindSceneBitmaps(root) {
  if (!root) return;
  root.querySelectorAll?.("[data-scene-bitmap-key]")?.forEach(canvas => {
    const key = canvas.dataset.sceneBitmapKey;
    const bitmap = sceneBitmaps.get(key);
    if (!bitmap || canvas.dataset.sceneBitmapBound === key) return;
    canvas.width = bitmap.width;
    canvas.height = bitmap.height;
    const context = canvas.getContext?.("2d", { alpha: true });
    context?.clearRect(0, 0, canvas.width, canvas.height);
    context?.drawImage(bitmap, 0, 0);
    canvas.dataset.sceneBitmapBound = key;
  });
}

export function releaseSceneBitmap(key) {
  const bitmap = sceneBitmaps.get(key);
  if (!bitmap) return;
  sceneBitmaps.delete(key);
  bitmap.close?.();
}

function loadMoveable() {
  if (window.Moveable) return Promise.resolve();
  if (vendorPromise) return vendorPromise;
  vendorPromise = new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = new URL(vendorUrl, import.meta.url).href;
    script.onload = resolve;
    script.onerror = reject;
    document.head.appendChild(script);
  });
  return vendorPromise;
}

function compose(item, viewScale = 1, deltaX = 0, deltaY = 0, scaleX = item.scaleX, scaleY = item.scaleY, rotation = item.rotation) {
  const offsetX = toScenePixels(item.parentWidth * item.offsetXPercent / 100 + deltaX, viewScale);
  const offsetY = toScenePixels(item.parentHeight * item.offsetYPercent / 100 + deltaY, viewScale);
  return `translate(${offsetX}px, ${offsetY}px) rotate(${rotation}deg) scale(${scaleX}, ${scaleY})`;
}

export function applyOverlayGeometry(element, item, viewScale = 1) {
  if (!element || !item) return;
  element.style.left = `${toScenePixels(item.x, viewScale)}px`;
  element.style.top = `${toScenePixels(item.y, viewScale)}px`;
  element.style.width = `${Math.max(toScenePixels(item.width, viewScale), 1)}px`;
  element.style.height = `${Math.max(toScenePixels(item.height, viewScale), 1)}px`;
  element.style.transform = compose(item, viewScale);
}

function asFinite(value, fallback) {
  return Number.isFinite(value) ? value : fallback;
}

function asPositiveScale(value) {
  return Number.isFinite(value) && value > 0 ? value : 1;
}

export function toCanvasUnits(cssPixels, viewScale) {
  return asFinite(cssPixels, 0) / asPositiveScale(viewScale);
}

export function toScenePixels(canvasUnits, viewScale) {
  return asFinite(canvasUnits, 0) * asPositiveScale(viewScale);
}

export function invertLinearDelta(x, y, a, b, c, d) {
  const determinant = a * d - b * c;
  if (!Number.isFinite(determinant) || Math.abs(determinant) < 1e-8) return [x, y];
  return [
    (d * x - c * y) / determinant,
    (-b * x + a * y) / determinant
  ];
}

function clampCenter(center, halfExtent, parentExtent) {
  if (!Number.isFinite(center) || !Number.isFinite(halfExtent) || !Number.isFinite(parentExtent) || parentExtent <= 0) {
    return parentExtent / 2;
  }
  if (halfExtent * 2 >= parentExtent) return parentExtent / 2;
  return Math.min(Math.max(center, halfExtent), parentExtent - halfExtent);
}

export function clampChildTranslation(
  item,
  deltaX,
  deltaY,
  scaleX = item?.scaleX,
  scaleY = item?.scaleY,
  rotation = item?.rotation,
  minimumVisible = 24) {
  if (!item || item.parentWidth <= 0 || item.parentHeight <= 0) {
    return [asFinite(deltaX, 0), asFinite(deltaY, 0)];
  }

  const radians = asFinite(rotation, 0) * Math.PI / 180;
  const cosine = Math.abs(Math.cos(radians));
  const sine = Math.abs(Math.sin(radians));
  const scaledWidth = Math.max(0, asFinite(item.width, 0)) * Math.abs(asFinite(scaleX, 1));
  const scaledHeight = Math.max(0, asFinite(item.height, 0)) * Math.abs(asFinite(scaleY, 1));
  const halfWidth = (cosine * scaledWidth + sine * scaledHeight) / 2;
  const halfHeight = (sine * scaledWidth + cosine * scaledHeight) / 2;
  const baseOffsetX = item.parentWidth * asFinite(item.offsetXPercent, 0) / 100;
  const baseOffsetY = item.parentHeight * asFinite(item.offsetYPercent, 0) / 100;
  const baseCenterX = asFinite(item.x, 0) + asFinite(item.width, 0) / 2;
  const baseCenterY = asFinite(item.y, 0) + asFinite(item.height, 0) / 2;
  const desiredCenterX = baseCenterX + baseOffsetX + asFinite(deltaX, 0);
  const desiredCenterY = baseCenterY + baseOffsetY + asFinite(deltaY, 0);
  const rootNode = !item.parentId;
  const visibleX = Math.min(Math.max(0, minimumVisible), halfWidth);
  const visibleY = Math.min(Math.max(0, minimumVisible), halfHeight);
  const centerX = rootNode
    ? Math.min(
      Math.max(desiredCenterX, visibleX - halfWidth),
      item.parentWidth - visibleX + halfWidth)
    : clampCenter(desiredCenterX, halfWidth, item.parentWidth);
  const centerY = rootNode
    ? Math.min(
      Math.max(desiredCenterY, visibleY - halfHeight),
      item.parentHeight - visibleY + halfHeight)
    : clampCenter(desiredCenterY, halfHeight, item.parentHeight);
  return [centerX - baseCenterX - baseOffsetX, centerY - baseCenterY - baseOffsetY];
}

export function clampChildScale(item, scaleX, scaleY, rotation = item?.rotation) {
  if (!item?.parentId || item.parentWidth <= 0 || item.parentHeight <= 0) {
    return [asFinite(scaleX, item?.scaleX ?? 1), asFinite(scaleY, item?.scaleY ?? 1)];
  }

  const minimumScale = 0.05;
  const maximumScale = 20;
  let nextScaleX = Math.min(maximumScale, Math.max(minimumScale, Math.abs(asFinite(scaleX, item.scaleX))));
  let nextScaleY = Math.min(maximumScale, Math.max(minimumScale, Math.abs(asFinite(scaleY, item.scaleY))));
  const radians = asFinite(rotation, item.rotation) * Math.PI / 180;
  const cosine = Math.abs(Math.cos(radians));
  const sine = Math.abs(Math.sin(radians));
  const rotatedWidth = cosine * asFinite(item.width, 0) * nextScaleX
    + sine * asFinite(item.height, 0) * nextScaleY;
  const rotatedHeight = sine * asFinite(item.width, 0) * nextScaleX
    + cosine * asFinite(item.height, 0) * nextScaleY;
  if (rotatedWidth <= 0 || rotatedHeight <= 0) return [nextScaleX, nextScaleY];

  const fit = Math.min(1, item.parentWidth / rotatedWidth, item.parentHeight / rotatedHeight);
  if (!Number.isFinite(fit) || fit <= 0) return [nextScaleX, nextScaleY];
  nextScaleX = Math.max(minimumScale, nextScaleX * fit);
  nextScaleY = Math.max(minimumScale, nextScaleY * fit);
  return [nextScaleX, nextScaleY];
}

function pair(value, fallback = [0, 0]) {
  return Array.isArray(value)
    ? [asFinite(value[0], fallback[0]), asFinite(value[1], fallback[1])]
    : fallback;
}

function layoutDimension(size) {
  return Number.isFinite(size) && size > 0 ? size : null;
}

export function resolveScaleInteraction(event, fallbackScale = [1, 1]) {
  const [scaleX, scaleY] = pair(event?.scale, fallbackScale);
  const [deltaX, deltaY] = pair(
    event?.drag?.beforeDist ?? event?.drag?.dist);
  return {
    scaleX,
    scaleY,
    deltaX,
    deltaY
  };
}

export function resizeAspectRatio(mode, start, direction) {
  const [directionX, directionY] = pair(direction, [0, 0]);
  const corner = directionX !== 0 && directionY !== 0;
  if (!corner || mode !== "ratio") return 0;
  const width = asFinite(start?.width, 0);
  const height = asFinite(start?.height, 0);
  return width > 0 && height > 0 ? width / height : 0;
}

export function resolveTextCornerResize(start, requestedWidth, requestedHeight) {
  const startWidth = Math.max(0.000001, asFinite(start?.width, 0));
  const startHeight = Math.max(0.000001, asFinite(start?.height, 0));
  const inset = Math.min(
    Math.max(0, asFinite(start?.resizeInset, 0)),
    Math.max(0, Math.min(startWidth, startHeight) - 0.000001));
  const widthRatio = asFinite(requestedWidth, startWidth) / startWidth;
  const heightRatio = asFinite(requestedHeight, startHeight) / startHeight;
  let ratio = Math.abs(widthRatio - 1) >= Math.abs(heightRatio - 1)
    ? widthRatio
    : heightRatio;
  const fontSize = asFinite(start?.resizeFontSize, 0);
  if (fontSize > 0) ratio = Math.min(25 / fontSize, Math.max(0.1 / fontSize, ratio));
  ratio = Math.max(0.000001, ratio);

  return {
    width: Math.max(0.000001, (startWidth - inset) * ratio + inset),
    height: Math.max(0.000001, (startHeight - inset) * ratio + inset),
    resizeRatio: ratio
  };
}

export function resizeCenterDelta(start, direction, width, height) {
  const [directionX, directionY] = pair(direction, [0, 0]);
  const deltaWidth = asFinite(width, start?.width ?? 0) - asFinite(start?.width, 0);
  const deltaHeight = asFinite(height, start?.height ?? 0) - asFinite(start?.height, 0);
  const localX = directionX * deltaWidth / 2 * asFinite(start?.scaleX, 1);
  const localY = directionY * deltaHeight / 2 * asFinite(start?.scaleY, 1);
  const radians = asFinite(start?.rotation, 0) * Math.PI / 180;
  const cosine = Math.cos(radians);
  const sine = Math.sin(radians);
  return [
    localX * cosine - localY * sine,
    localX * sine + localY * cosine
  ];
}

export function resolveConstrainedResizeTranslation(
  mode,
  start,
  direction,
  width,
  height) {
  const [directionX, directionY] = pair(direction, [0, 0]);
  const textCorner = mode === "text"
    && directionX !== 0
    && directionY !== 0;
  const lineLength = mode === "horizontal" || mode === "vertical";
  if (!textCorner && !lineLength) return null;

  const [centerDeltaX, centerDeltaY] = resizeCenterDelta(
    start,
    [directionX, directionY],
    width,
    height);
  return {
    centerDeltaX,
    centerDeltaY,
    deltaX: centerDeltaX
      - (width - asFinite(start?.width, 0)) / 2,
    deltaY: centerDeltaY
      - (height - asFinite(start?.height, 0)) / 2
  };
}

export function configureResizeGesture(event, active, minimumCssPixels = 8) {
  const minimum = Math.max(1, asFinite(minimumCssPixels, 8));
  event?.setMin?.([minimum, minimum]);
  const ratio = resizeAspectRatio(
    active?.start?.resizeMode,
    active?.start,
    event?.direction);
  const ratioManagedByMoveable = ratio > 0
    && typeof event?.setRatio === "function";
  if (ratioManagedByMoveable) event.setRatio(ratio);
  return ratioManagedByMoveable;
}

export function resolveResizeDimensions(
  mode,
  start,
  direction,
  requestedWidth,
  requestedHeight,
  ratioManagedByMoveable = false) {
  const [directionX, directionY] = pair(direction, [0, 0]);
  let width = asFinite(requestedWidth, start?.width ?? 0);
  let height = asFinite(requestedHeight, start?.height ?? 0);
  const corner = directionX !== 0 && directionY !== 0;

  if (mode === "horizontal") {
    height = start.height;
  } else if (mode === "vertical") {
    width = start.width;
  } else if (mode === "text" && !corner) {
    height = start.height;
  } else if (mode === "text" && corner) {
    const textResize = resolveTextCornerResize(start, width, height);
    width = textResize.width;
    height = textResize.height;
    return {
      width,
      height,
      keepAspectRatio: false,
      resizeRatio: textResize.resizeRatio
    };
  } else if (!ratioManagedByMoveable
    && mode === "ratio" && corner) {
    const aspect = start.width / Math.max(start.height, 0.000001);
    const widthRatio = width / Math.max(start.width, 0.000001);
    const heightRatio = height / Math.max(start.height, 0.000001);
    // Pick the axis the pointer changed most, rather than the numerically
    // larger ratio. Math.max(widthRatio, heightRatio) blocks shrinking when
    // one pointer axis has not moved and can oscillate as the ratios cross.
    if (Math.abs(widthRatio - 1) >= Math.abs(heightRatio - 1))
      height = width / Math.max(aspect, 0.000001);
    else width = height * aspect;
  }

  return {
    width,
    height,
    keepAspectRatio: mode === "ratio" && corner
  };
}

function inputEventFor(event) {
  return event?.inputEvent || event?.lastEvent?.inputEvent || null;
}

function snapMovement(value, event) {
  return inputEventFor(event)?.shiftKey ? Math.round(value / 10) * 10 : value;
}

export function getFitScale(element, canvasWidth, canvasHeight, padding = 56) {
  if (!element || canvasWidth <= 0 || canvasHeight <= 0) return 1;
  const availableWidth = Math.max(1, element.clientWidth - padding);
  const availableHeight = Math.max(1, element.clientHeight - padding);
  return Math.min(availableWidth / canvasWidth, availableHeight / canvasHeight);
}

export function detectInteractionProfile() {
  const hasHover = globalThis.matchMedia?.("(hover: hover)")?.matches === true;
  const finePointer = globalThis.matchMedia?.("(pointer: fine)")?.matches === true;
  const probe = document.createElement("div");
  probe.style.cssText = [
    "position:fixed",
    "visibility:hidden",
    "pointer-events:none",
    "padding-top:env(safe-area-inset-top)",
    "padding-right:env(safe-area-inset-right)",
    "padding-bottom:env(safe-area-inset-bottom)",
    "padding-left:env(safe-area-inset-left)"
  ].join(";");
  document.body.appendChild(probe);
  const style = getComputedStyle(probe);
  const number = value => Number.parseFloat(value) || 0;
  const result = {
    pointerType: finePointer ? "mouse" : "touch",
    hasHover,
    finePointer,
    hasHardwareKeyboard: finePointer || (navigator.maxTouchPoints || 0) === 0,
    supportsImageBitmap: typeof globalThis.createImageBitmap === "function",
    supportsBlob: typeof globalThis.Blob === "function" && typeof globalThis.URL?.createObjectURL === "function",
    supportsPointerCapture: typeof globalThis.Element?.prototype?.setPointerCapture === "function",
    safeAreaTop: number(style.paddingTop),
    safeAreaRight: number(style.paddingRight),
    safeAreaBottom: number(style.paddingBottom),
    safeAreaLeft: number(style.paddingLeft),
    displayDensity: globalThis.devicePixelRatio || 1
  };
  probe.remove();
  return result;
}

export function capturePointer(element, pointerId) {
  if (element?.setPointerCapture && Number.isFinite(pointerId)) {
    element.setPointerCapture(pointerId);
  }
}

export function releasePointer(element, pointerId) {
  if (element?.releasePointerCapture && Number.isFinite(pointerId) && element.hasPointerCapture?.(pointerId)) {
    element.releasePointerCapture(pointerId);
  }
}

export function shouldFinishReleasedPointer(event, activePointerId) {
  const eventPointerId = event?.pointerId;
  if (Number.isFinite(activePointerId)
    && Number.isFinite(eventPointerId)
    && activePointerId !== eventPointerId) return false;

  // WebKit may expose buttons === 0 for an active touch pointer. Treating that
  // like a released mouse button commits the interaction on its first move.
  return event?.pointerType !== "touch" && event?.buttons === 0;
}

export function shouldIgnoreSynthesizedMouse(
  pointerType,
  eventTime,
  lastTouchTime,
  suppressionWindow = 500) {
  return pointerType === "mouse"
    && Number.isFinite(eventTime)
    && Number.isFinite(lastTouchTime)
    && eventTime - lastTouchTime >= 0
    && eventTime - lastTouchTime < suppressionWindow;
}

export function isPointerTap(start, end, threshold = 6) {
  if (!start || !end) return false;
  const deltaX = asFinite(end.clientX, 0) - asFinite(start.clientX, 0);
  const deltaY = asFinite(end.clientY, 0) - asFinite(start.clientY, 0);
  return Math.hypot(deltaX, deltaY) < threshold;
}

export function touchPairMetrics(points) {
  if (!Array.isArray(points) || points.length < 2) return null;
  const [first, second] = points;
  return {
    x: (asFinite(first?.x, 0) + asFinite(second?.x, 0)) / 2,
    y: (asFinite(first?.y, 0) + asFinite(second?.y, 0)) / 2,
    distance: Math.max(
      1,
      Math.hypot(
        asFinite(second?.x, 0) - asFinite(first?.x, 0),
        asFinite(second?.y, 0) - asFinite(first?.y, 0)))
  };
}

export function viewportGestureChange(start, latest) {
  if (!start || !latest) return { deltaX: 0, deltaY: 0, scale: 1 };
  return {
    deltaX: latest.x - start.x,
    deltaY: latest.y - start.y,
    scale: latest.distance / Math.max(1, start.distance)
  };
}

export function createLayerInteractionVisual(layer, backdrop = null) {
  if (!layer && !backdrop) return null;
  return {
    layer,
    backdrop,
    layerTransform: layer?.style?.transform || "",
    backdropTransform: backdrop?.style?.transform || ""
  };
}

export function createSceneInteractionVisual(
  controlId,
  layers = [],
  backdrops = [],
  logicalGroups = []) {
  if (!controlId) return null;
  const layer = Array.from(layers)
    .find(element => element?.dataset?.sceneControlId === controlId) || null;
  const logicalGroup = Array.from(logicalGroups)
    .find(element => element?.dataset?.sceneGroupId === controlId) || null;
  const backdrop = Array.from(backdrops)
    .find(element => element?.dataset?.sceneControlId === controlId) || null;
  // Containers without an isolation surface are represented by a logical DOM
  // group. Transform that group during the gesture so all descendant surfaces
  // stay inside the live selection proxy until the authoritative rerender.
  return createLayerInteractionVisual(layer || logicalGroup, backdrop);
}

export function updateLayerInteractionVisual(visual, active, viewScale = 1) {
  if (!visual || !active) return;
  const resizeCenterDeltaX = asFinite(
    active.centerDeltaX,
    asFinite(active.deltaX, 0)
      + (asFinite(active.width, active.start.width) - asFinite(active.start.width, 0)) / 2);
  const resizeCenterDeltaY = asFinite(
    active.centerDeltaY,
    asFinite(active.deltaY, 0)
      + (asFinite(active.height, active.start.height) - asFinite(active.start.height, 0)) / 2);
  const translateX = toScenePixels(
    active.kind === "resize" ? resizeCenterDeltaX : active.deltaX,
    viewScale);
  const translateY = toScenePixels(
    active.kind === "resize" ? resizeCenterDeltaY : active.deltaY,
    viewScale);
  const rotation = asFinite(active.rotation, active.start.rotation) - asFinite(active.start.rotation, 0);
  const widthRatio = active.kind === "resize"
    ? asPositiveScale(active.width) / asPositiveScale(active.start.width)
    : 1;
  const heightRatio = active.kind === "resize"
    ? asPositiveScale(active.height) / asPositiveScale(active.start.height)
    : 1;
  const scaleX = widthRatio * asPositiveScale(active.scaleX) / asPositiveScale(active.start.scaleX);
  const scaleY = heightRatio * asPositiveScale(active.scaleY) / asPositiveScale(active.start.scaleY);
  const delta = ` translate(${translateX}px, ${translateY}px) rotate(${rotation}deg) scale(${scaleX}, ${scaleY})`;
  if (visual.layer) visual.layer.style.transform = `${visual.layerTransform}${delta}`;
  if (visual.backdrop) visual.backdrop.style.transform = `${visual.backdropTransform}${delta}`;
}

export function restoreLayerInteractionVisual(visual) {
  if (!visual) return;
  if (visual.layer) visual.layer.style.transform = visual.layerTransform;
  if (visual.backdrop) visual.backdrop.style.transform = visual.backdropTransform;
}

export function focusSelectionName() {
  const input = document.querySelector(".mac-selection-inspector .selection-name-input:not(:disabled)");
  if (!input) return false;
  input.focus();
  input.select?.();
  return true;
}

export function scrollSelectedLayerIntoView() {
  document.querySelector(".mac-layer-tree .layer-row.selected")
    ?.scrollIntoView({ block: "nearest", inline: "nearest" });
}

export async function createEditor(root, callback, interactionProfile = null) {
  await loadMoveable();

  const stage = document.createElement("div");
  stage.className = "mac-template-canvas-stage";
  stage.style.position = "relative";
  stage.style.transformOrigin = "top left";
  root.replaceChildren(stage);

  let itemsById = new Map();
  let overlays = new Map();
  let selectedId = null;
  let viewScale = 1;
  let sceneWidth = 0;
  let sceneHeight = 0;
  let interaction = null;
  let interactionBusy = false;
  let interactionLifecycle = Promise.resolve();
  let nextInteractionToken = 0;
  let sceneQueue = Promise.resolve();
  let sceneEpoch = 0;
  let scenePending = false;
  let disposePromise = null;
  let disposed = false;
  let panMode = false;
  let snappingEnabled = true;
  let finePointerMode = interactionProfile?.finePointer !== false;
  let pointerStart = null;
  let pendingInteractionVisual = null;
  let pendingInteractionVisualTimer = null;
  let pointerAnimationFrame = null;
  let pendingPointerEvent = null;
  let lastTouchPointerAt = Number.NEGATIVE_INFINITY;
  const activeTouches = new Map();
  let viewportGesture = null;
  let viewportGestureFrame = null;

  function clearDocumentSelection() {
    root.ownerDocument.getSelection?.()?.removeAllRanges?.();
  }

  function preventCanvasSelection(event) {
    if (interaction || pointerStart || root.contains(event.target)) event.preventDefault();
  }

  function preventNativeDrag(event) {
    if (root.contains(event.target)) event.preventDefault();
  }

  function stopCanvasPointerPropagation(event) {
    if (event.target?.closest?.(".canvas-control, .moveable-control-box"))
      event.stopPropagation();
  }

  function rememberPointerStart(event) {
    const now = event.timeStamp || performance.now();
    if (event.pointerType === "touch") lastTouchPointerAt = now;
    if (shouldIgnoreSynthesizedMouse(event.pointerType, now, lastTouchPointerAt)) return;
    const observedFinePointer = event.pointerType === "mouse"
      || event.pointerType === "pen"
      || globalThis.matchMedia?.("(pointer: fine)")?.matches === true;
    if (finePointerMode !== observedFinePointer) {
      finePointerMode = observedFinePointer;
      select(selectedId);
    }
    void invoke(
      "ReportEditorPointer",
      event.pointerType || interactionProfile?.pointerType || "mouse",
      globalThis.matchMedia?.("(hover: hover)")?.matches === true,
      observedFinePointer);
    if (panMode || event.isPrimary === false || event.button !== 0) return;
    if (interaction) {
      const active = interaction;
      event.preventDefault();
      event.stopPropagation();
      void completeInteraction(active.kind, { lastEvent: true });
      return;
    }
    clearDocumentSelection();
    const hitElement = event.target?.closest?.("[data-control-id]");
    const hitControlId = hitElement?.dataset.controlId || selectedId;
    const selectedTarget = selectedId ? overlays.get(selectedId) : null;
    const deferSelection = Boolean(
      selectedId
      && hitElement
      && hitControlId !== selectedId
      && selectedTarget?.contains(hitElement));
    const controlId = deferSelection ? selectedId : hitControlId;
    if (!controlId || !itemsById.has(controlId)) return;
    pointerStart = {
      controlId,
      hitControlId,
      deferSelection,
      clientX: event.clientX,
      clientY: event.clientY,
      pointerId: event.pointerId,
      pointerType: event.pointerType
    };
    if (!deferSelection && hitControlId && hitControlId !== selectedId) {
      select(hitControlId);
      void invoke("SelectCanvasControl", hitControlId);
    }
  }

  function finishActivePointerInteraction(event) {
    const activePointerId = interaction?.pointerId ?? pointerStart?.pointerId;
    if (Number.isFinite(activePointerId)
      && Number.isFinite(event?.pointerId)
      && activePointerId !== event.pointerId) return;

    const pendingSelection = interaction?.kind === "drag"
      ? interaction.pendingSelection
      : null;
    if (pendingSelection?.hitControlId
      && isPointerTap(pendingSelection, event)) {
      pointerStart = null;
      void cancelInteraction();
      select(pendingSelection.hitControlId);
      void invoke("SelectCanvasControl", pendingSelection.hitControlId);
      return;
    }

    if (!interaction) {
      const pending = pointerStart;
      pointerStart = null;
      if (pending?.deferSelection
        && pending.hitControlId
        && isPointerTap(pending, event)) {
        select(pending.hitControlId);
        void invoke("SelectCanvasControl", pending.hitControlId);
      }
      return;
    }

    if (interaction.kind === "drag") updateDrag(event);
    const token = interaction.token;
    const kind = interaction.kind;
    setTimeout(() => {
      if (interaction?.kind === kind && interaction.token === token)
        void completeInteraction(kind, { lastEvent: true });
    }, 0);
  }

  function finishLostPointerCapture(event) {
    if (interaction?.pointerId === event.pointerId)
      finishActivePointerInteraction(event);
  }

  function finishReleasedPointerInteraction(event) {
    if (interaction && shouldFinishReleasedPointer(event, interaction.pointerId))
      finishActivePointerInteraction(event);
  }

  function cancelActivePointerInteraction(event) {
    if (!interaction) return;
    if (Number.isFinite(interaction.pointerId)
      && Number.isFinite(event?.pointerId)
      && interaction.pointerId !== event.pointerId) return;
    if (hasInteractionChange(interaction))
      void completeInteraction(interaction.kind, { lastEvent: true });
    else
      void cancelInteraction();
  }

  function scheduleActivePointerPosition(event) {
    pendingPointerEvent = event;
    if (pointerAnimationFrame != null) return;
    pointerAnimationFrame = requestAnimationFrame(() => {
      pointerAnimationFrame = null;
      const next = pendingPointerEvent;
      pendingPointerEvent = null;
      if (interaction?.kind === "drag") updateDrag(next);
    });
  }

  root.addEventListener("pointerdown", rememberPointerStart, true);
  root.addEventListener("pointerdown", stopCanvasPointerPropagation);
  root.addEventListener("pointermove", scheduleActivePointerPosition, true);
  root.addEventListener("dragstart", preventNativeDrag, true);
  root.ownerDocument.addEventListener("selectstart", preventCanvasSelection, true);
  root.ownerDocument.addEventListener("pointerup", finishActivePointerInteraction, true);
  root.ownerDocument.addEventListener("pointercancel", cancelActivePointerInteraction, true);
  root.ownerDocument.addEventListener("mouseup", finishActivePointerInteraction, true);
  root.ownerDocument.defaultView?.addEventListener("pointerup", finishActivePointerInteraction, true);
  root.ownerDocument.defaultView?.addEventListener("mouseup", finishActivePointerInteraction, true);
  root.ownerDocument.defaultView?.addEventListener("pointermove", finishReleasedPointerInteraction, true);
  root.ownerDocument.defaultView?.addEventListener("mousemove", finishReleasedPointerInteraction, true);
  root.addEventListener("lostpointercapture", finishLostPointerCapture, true);

  const viewport = root.closest(".mac-canvas-viewport");
  const surface = root.closest(".mac-canvas-surface");

  function touchPair() {
    return Array.from(activeTouches.values()).slice(0, 2);
  }

  function beginViewportGesture(event) {
    if (event.pointerType !== "touch") return;
    activeTouches.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (activeTouches.size !== 2 || viewportGesture) return;
    const start = touchPairMetrics(touchPair());
    viewportGesture = { start, latest: start };
    void cancelInteraction();
    event.preventDefault();
  }

  function updateViewportGesture(event) {
    if (event.pointerType !== "touch" || !activeTouches.has(event.pointerId)) return;
    activeTouches.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (!viewportGesture || activeTouches.size < 2) return;
    viewportGesture.latest = touchPairMetrics(touchPair());
    event.preventDefault();
    if (viewportGestureFrame != null) return;
    viewportGestureFrame = requestAnimationFrame(() => {
      viewportGestureFrame = null;
      if (!viewportGesture || !surface) return;
      const { deltaX, deltaY, scale } = viewportGestureChange(
        viewportGesture.start,
        viewportGesture.latest);
      surface.style.setProperty("--gesture-x", `${deltaX}px`);
      surface.style.setProperty("--gesture-y", `${deltaY}px`);
      surface.style.setProperty("--gesture-scale", `${scale}`);
    });
  }

  function finishViewportGesture(event) {
    if (event.pointerType !== "touch") return;
    activeTouches.delete(event.pointerId);
    if (!viewportGesture || activeTouches.size >= 2) return;
    const finished = viewportGesture;
    viewportGesture = null;
    const { deltaX, deltaY, scale } = viewportGestureChange(
      finished.start,
      finished.latest);
    void invoke("CommitViewportGesture", deltaX, deltaY, scale).finally(() => {
      surface?.style?.setProperty("--gesture-x", "0px");
      surface?.style?.setProperty("--gesture-y", "0px");
      surface?.style?.setProperty("--gesture-scale", "1");
    });
  }

  viewport?.addEventListener("pointerdown", beginViewportGesture, true);
  viewport?.addEventListener("pointermove", updateViewportGesture, true);
  viewport?.addEventListener("pointerup", finishViewportGesture, true);
  viewport?.addEventListener("pointercancel", finishViewportGesture, true);

  function invoke(method, ...args) {
    try {
      return Promise.resolve(callback.invokeMethodAsync(method, ...args)).catch(error => {
        console.error(`Mac template canvas callback '${method}' failed.`, error);
      });
    } catch (error) {
      console.error(`Mac template canvas callback '${method}' failed.`, error);
      return Promise.resolve();
    }
  }

  function targetFor(event) {
    const eventTarget = event.target;
    return eventTarget?.dataset?.controlId ? eventTarget : moveable.target || null;
  }

  function pointerMovement(event, active) {
    const input = inputEventFor(event);
    const clientX = input?.clientX ?? event?.clientX;
    const clientY = input?.clientY ?? event?.clientY;
    if (Number.isFinite(clientX) && Number.isFinite(clientY)
      && Number.isFinite(active.startClientX) && Number.isFinite(active.startClientY)) {
      return [clientX - active.startClientX, clientY - active.startClientY];
    }
    return pair(event?.beforeDist ?? event?.dist);
  }

  function parentInverseMatrices(item) {
    const parents = [];
    let parentId = item.parentId;
    while (parentId) {
      const parentElement = overlays.get(parentId);
      if (parentElement) parents.push(parentElement);
      parentId = itemsById.get(parentId)?.parentId || null;
    }

    const inverses = [];
    parents.reverse().forEach(parentElement => {
      const transform = getComputedStyle(parentElement).transform;
      if (!transform || transform === "none") return;
      const matrix = new DOMMatrixReadOnly(transform);
      const determinant = matrix.a * matrix.d - matrix.b * matrix.c;
      if (!Number.isFinite(determinant) || Math.abs(determinant) < 1e-8) return;
      inverses.push([
        matrix.d / determinant,
        -matrix.b / determinant,
        -matrix.c / determinant,
        matrix.a / determinant
      ]);
    });
    return inverses;
  }

  function movementInParentSpace(inverses, screenX, screenY) {
    let localX = screenX;
    let localY = screenY;
    inverses.forEach(([a, b, c, d]) => {
      const nextX = a * localX + c * localY;
      localY = b * localX + d * localY;
      localX = nextX;
    });
    return [localX, localY];
  }

  function createInteractionVisual(item) {
    const surfaceRoot = root.parentElement;
    const layers = Array.from(surfaceRoot?.querySelectorAll?.(".mac-canvas-scene-layer") || []);
    const backdrops = Array.from(surfaceRoot?.querySelectorAll?.(".mac-canvas-backdrop") || []);
    const logicalGroups = Array.from(surfaceRoot?.querySelectorAll?.(".mac-canvas-logical-group") || []);
    return createSceneInteractionVisual(item.id, layers, backdrops, logicalGroups);
  }

  function updateInteractionVisual(active) {
    updateLayerInteractionVisual(active?.dragGhost, active, viewScale);
  }

  function applyCommittedInteraction(active, committed) {
    const controlId = committed?.controlId;
    if (!controlId || controlId !== active.start.id) return false;

    const current = itemsById.get(controlId);
    if (!current) return false;
    const committedWidth = asFinite(committed.width, current.width);
    const committedHeight = asFinite(committed.height, current.height);
    const centerDeltaX = asFinite(committed.centerDeltaX, active.centerDeltaX || 0);
    const centerDeltaY = asFinite(committed.centerDeltaY, active.centerDeltaY || 0);
    const next = {
      ...current,
      x: active.kind === "resize"
        ? current.x + centerDeltaX - (committedWidth - current.width) / 2
        : current.x,
      y: active.kind === "resize"
        ? current.y + centerDeltaY - (committedHeight - current.height) / 2
        : current.y,
      width: committedWidth,
      height: committedHeight,
      offsetXPercent: asFinite(committed.offsetXPercent, current.offsetXPercent),
      offsetYPercent: asFinite(committed.offsetYPercent, current.offsetYPercent),
      scaleX: asFinite(committed.scaleX, current.scaleX),
      scaleY: asFinite(committed.scaleY, current.scaleY),
      rotation: asFinite(committed.rotation, current.rotation)
    };
    itemsById.set(controlId, next);

    active.deltaX = active.kind === "resize"
      ? next.x - active.start.x
      : next.parentWidth === 0
        ? 0
        : (next.offsetXPercent - active.start.offsetXPercent) / 100 * next.parentWidth;
    active.deltaY = active.kind === "resize"
      ? next.y - active.start.y
      : next.parentHeight === 0
        ? 0
        : (next.offsetYPercent - active.start.offsetYPercent) / 100 * next.parentHeight;
    active.width = next.width;
    active.height = next.height;
    active.scaleX = next.scaleX;
    active.scaleY = next.scaleY;
    active.rotation = next.rotation;
    const target = overlays.get(controlId);
    applyOverlayGeometry(target, next, viewScale);
    updateInteractionVisual(active);
    moveable.updateRect();
    return true;
  }

  function restoreInteractionStart(active) {
    const target = overlays.get(active.start.id);
    applyOverlayGeometry(target, active.start, viewScale);
    active.keepVisualUntilScene = false;
  }

  function removeInteractionVisual(active) {
    restoreLayerInteractionVisual(active?.dragGhost);
    if (active) active.dragGhost = null;
  }

  function clearPendingInteractionVisual(restore = true) {
    if (pendingInteractionVisualTimer != null) {
      clearTimeout(pendingInteractionVisualTimer);
      pendingInteractionVisualTimer = null;
    }
    if (restore) restoreLayerInteractionVisual(pendingInteractionVisual);
    pendingInteractionVisual = null;
  }

  function retainInteractionVisual(active) {
    clearPendingInteractionVisual();
    pendingInteractionVisual = active.dragGhost;
    active.dragGhost = null;
    if (!pendingInteractionVisual) return;
    pendingInteractionVisualTimer = setTimeout(clearPendingInteractionVisual, 2000);
  }

  function beginInteraction(kind, event) {
    if (disposed || scenePending || interactionBusy || panMode) {
      event.stop?.();
      return false;
    }

    const target = targetFor(event);
    const controlId = target?.dataset.controlId;
    const item = controlId ? itemsById.get(controlId) : null;
    const isAbsolute = Boolean(item?.absolute);
    const isFlowChild = Boolean(item?.parentId) && !isAbsolute;
    // Any Absolute node supports direct transform interactions. Static children
    // only support drag, which commits a flow-layout reorder plus margins.
    if (!item || !item.visible || item.locked || (!isAbsolute && !isFlowChild)
      || (kind === "rotate" && !isAbsolute)
      || (kind !== "drag" && kind !== "resize" && kind !== "rotate")) {
      event.stop?.();
      return false;
    }

    const initialPointer = pointerStart?.controlId === controlId
      ? pointerStart
      : inputEventFor(event) || event;
    const active = {
      token: ++nextInteractionToken,
      kind,
      start: { ...item },
      startClientX: asFinite(initialPointer.clientX, NaN),
      startClientY: asFinite(initialPointer.clientY, NaN),
      layoutWidth: layoutDimension(toScenePixels(item.width, viewScale)),
      layoutHeight: layoutDimension(toScenePixels(item.height, viewScale)),
      parentInverses: parentInverseMatrices(item),
      keepRatio: item.type !== "WMContainer",
      deltaX: 0,
      deltaY: 0,
      centerDeltaX: 0,
      centerDeltaY: 0,
      width: item.width,
      height: item.height,
      handle: "",
      scaleX: item.scaleX,
      scaleY: item.scaleY,
      rotation: item.rotation
    };
    const pointerId = asFinite(initialPointer.pointerId, NaN);
    if (Number.isFinite(pointerId)) active.pointerId = pointerId;
    active.pointerType = initialPointer.pointerType;
    if (Number.isFinite(pointerId) && root.setPointerCapture) {
      try {
        root.setPointerCapture(pointerId);
      } catch {
        // WKWebView may reject capture for a synthesized mouse gesture.
      }
    }
    if (initialPointer.deferSelection) active.pendingSelection = initialPointer;
    clearPendingInteractionVisual();
    active.dragGhost = createInteractionVisual(item);
    pointerStart = null;
    interaction = active;
    interactionBusy = true;
    root.classList.add("canvas-interacting");
    clearDocumentSelection();
    active.beginPromise = invoke("BeginCanvasInteraction", controlId, kind).then(accepted => accepted === true);
    interactionLifecycle = active.beginPromise;
    return true;
  }

  function updateOverlay() {
    if (!interaction || disposed) return;
    const target = overlays.get(interaction.start.id);
    if (!target) return;

    if (interaction.kind === "resize") {
      target.style.width = `${Math.max(toScenePixels(interaction.width, viewScale), 1)}px`;
      target.style.height = `${Math.max(toScenePixels(interaction.height, viewScale), 1)}px`;
    }
    target.style.transform = compose(
      interaction.start,
      viewScale,
      interaction.deltaX,
      interaction.deltaY,
      interaction.scaleX,
      interaction.scaleY,
      interaction.rotation,
      24 / asPositiveScale(viewScale));
    updateInteractionVisual(interaction);
  }

  function updateDrag(event) {
    if (!interaction) return;
    const input = inputEventFor(event);
    const clientX = input?.clientX ?? event?.clientX;
    const clientY = input?.clientY ?? event?.clientY;
    if (Number.isFinite(clientX) && Number.isFinite(clientY)) {
      if (clientX === interaction.lastClientX && clientY === interaction.lastClientY) return;
      interaction.lastClientX = clientX;
      interaction.lastClientY = clientY;
    }
    if (input) moveable.snappable = snappingEnabled && !input.altKey;
    const [screenDeltaX, screenDeltaY] = pointerMovement(event, interaction);
    const [parentDeltaX, parentDeltaY] = movementInParentSpace(
      interaction.parentInverses,
      screenDeltaX,
      screenDeltaY);
    const desiredX = snapMovement(toCanvasUnits(parentDeltaX, viewScale), event);
    const desiredY = snapMovement(toCanvasUnits(parentDeltaY, viewScale), event);
    [interaction.deltaX, interaction.deltaY] = clampChildTranslation(
      interaction.start,
      desiredX,
      desiredY,
      interaction.scaleX,
      interaction.scaleY,
      interaction.rotation);
    updateOverlay();
    if (input?.buttons === 0) finishActivePointerInteraction(input);
  }

  function updateResize(event) {
    if (!interaction) return;
    moveable.snappable = snappingEnabled && !inputEventFor(event)?.altKey;
    const direction = pair(event?.direction, [0, 0]);
    interaction.handle = `${direction[1] < 0 ? "n" : direction[1] > 0 ? "s" : ""}${direction[0] < 0 ? "w" : direction[0] > 0 ? "e" : ""}`;
    const minimum = 8 / asPositiveScale(viewScale);
    const requestedWidth = Math.max(
      minimum,
      toCanvasUnits(asFinite(event?.width, interaction.layoutWidth), viewScale));
    const requestedHeight = Math.max(
      minimum,
      toCanvasUnits(asFinite(event?.height, interaction.layoutHeight), viewScale));
    const mode = interaction.start.resizeMode;
    const resolved = resolveResizeDimensions(
      mode,
      interaction.start,
      direction,
      requestedWidth,
      requestedHeight,
      interaction.ratioManagedByMoveable === true);
    const width = resolved.width;
    const height = resolved.height;
    interaction.keepAspectRatio = resolved.keepAspectRatio;
    interaction.resizeRatio = asFinite(resolved.resizeRatio, 1);
    interaction.width = width;
    interaction.height = height;
    const constrainedTranslation = resolveConstrainedResizeTranslation(
      mode,
      interaction.start,
      direction,
      width,
      height);
    if (constrainedTranslation) {
      interaction.centerDeltaX = constrainedTranslation.centerDeltaX;
      interaction.centerDeltaY = constrainedTranslation.centerDeltaY;
      interaction.deltaX = constrainedTranslation.deltaX;
      interaction.deltaY = constrainedTranslation.deltaY;
    } else {
      const [screenDeltaX, screenDeltaY] = pair(event?.drag?.beforeDist ?? event?.drag?.dist);
      const [parentDeltaX, parentDeltaY] = movementInParentSpace(
        interaction.parentInverses,
        screenDeltaX,
        screenDeltaY);
      interaction.deltaX = toCanvasUnits(parentDeltaX, viewScale);
      interaction.deltaY = toCanvasUnits(parentDeltaY, viewScale);
    }
    if (interaction.start.absolute) {
      [interaction.deltaX, interaction.deltaY] = clampChildTranslation(
        { ...interaction.start, width, height },
        interaction.deltaX,
        interaction.deltaY,
        interaction.scaleX,
        interaction.scaleY,
        interaction.rotation,
        24 / asPositiveScale(viewScale));
    }
    interaction.centerDeltaX = interaction.deltaX + (width - interaction.start.width) / 2;
    interaction.centerDeltaY = interaction.deltaY + (height - interaction.start.height) / 2;
    updateOverlay();
    const input = inputEventFor(event);
    if (input?.buttons === 0) finishActivePointerInteraction(input);
  }

  function updateRotate(event) {
    if (!interaction) return;
    moveable.snappable = snappingEnabled && !inputEventFor(event)?.altKey;
    const rotation = interaction.start.rotation + asFinite(event.beforeRotate ?? event.rotate, 0);
    interaction.rotation = inputEventFor(event)?.shiftKey ? Math.round(rotation / 15) * 15 : rotation;
    [interaction.scaleX, interaction.scaleY] = clampChildScale(
      interaction.start,
      interaction.start.scaleX,
      interaction.start.scaleY,
      interaction.rotation);
    [interaction.deltaX, interaction.deltaY] = clampChildTranslation(
      interaction.start,
      interaction.deltaX,
      interaction.deltaY,
      interaction.scaleX,
      interaction.scaleY,
      interaction.rotation);
    updateOverlay();
    const input = inputEventFor(event);
    if (input?.buttons === 0) finishActivePointerInteraction(input);
  }

  function finishInteraction(active, callback) {
    if (active.finishPromise) return active.finishPromise;

    active.finishPromise = Promise.resolve(active.beginPromise)
      .then(callback)
      .catch(error => {
        console.error("Mac template canvas interaction callback failed.", error);
      })
      .finally(() => {
        if (active.keepVisualUntilScene) retainInteractionVisual(active);
        else removeInteractionVisual(active);
        root.classList.remove("canvas-interacting");
        clearDocumentSelection();
        if (!interaction || interaction.token === active.token) interactionBusy = false;
      });
    interactionLifecycle = active.finishPromise;
    return active.finishPromise;
  }

  function cancelInteraction() {
    const active = interaction;
    if (!active) return interactionLifecycle;
    interaction = null;
    moveable.snappable = snappingEnabled;
    const target = overlays.get(active.start.id);
    applyOverlayGeometry(target, active.start, viewScale);
    return finishInteraction(active, () => invoke("CancelCanvasInteraction"));
  }

  function hasInteractionChange(active) {
    const epsilon = 0.0001;
    return Math.abs(active.deltaX) > epsilon
      || Math.abs(active.deltaY) > epsilon
      || Math.abs(active.scaleX - active.start.scaleX) > epsilon
      || Math.abs(active.scaleY - active.start.scaleY) > epsilon
      || Math.abs(active.rotation - active.start.rotation) > epsilon
      || Math.abs(active.width - active.start.width) > epsilon
      || Math.abs(active.height - active.start.height) > epsilon;
  }

  function completeInteraction(kind, event) {
    const active = interaction;
    if (!active || active.kind !== kind) return interactionLifecycle;
    if (!hasInteractionChange(active)) {
      return cancelInteraction();
    }

    moveable.snappable = snappingEnabled;

    interaction = null;
    active.keepVisualUntilScene = true;
    const { start } = active;
    const offsetXPercent = kind === "resize" || start.parentWidth === 0
      ? start.offsetXPercent
      : start.offsetXPercent + active.deltaX / start.parentWidth * 100;
    const offsetYPercent = kind === "resize" || start.parentHeight === 0
      ? start.offsetYPercent
      : start.offsetYPercent + active.deltaY / start.parentHeight * 100;
    return finishInteraction(active, async beginAccepted => {
      if (!beginAccepted) {
        restoreInteractionStart(active);
        return;
      }
      const committed = await invoke("CommitCanvasInteraction", {
        controlId: start.id,
        kind,
        offsetXPercent,
        offsetYPercent,
        scaleX: active.scaleX,
        scaleY: active.scaleY,
        rotation: active.rotation,
        width: active.width,
        height: active.height,
        handle: active.handle,
        centerDeltaX: active.centerDeltaX,
        centerDeltaY: active.centerDeltaY,
        boundsVersion: start.boundsVersion,
        keepAspectRatio: active.keepAspectRatio === true,
        resizeRatio: asFinite(active.resizeRatio, 1)
      });
      if (!applyCommittedInteraction(active, committed)) restoreInteractionStart(active);
    });
  }

  const moveable = new window.Moveable(stage, {
    container: root,
    target: null,
    draggable: true,
    resizable: true,
    scalable: false,
    renderDirections: finePointerMode ? absoluteResizeDirections : coarseResizeDirections,
    rotatable: true,
    origin: false,
    snappable: true,
    snapCenter: true,
    snapElement: true,
    verticalGuidelines: [0],
    horizontalGuidelines: [0]
  });

  moveable
    .on("dragStart", event => {
      if (beginInteraction("drag", event)) event.set?.([0, 0]);
    })
    .on("drag", updateDrag)
    .on("dragEnd", event => { void completeInteraction("drag", event); })
    .on("resizeStart", event => {
      if (beginInteraction("resize", event)) {
        event.set?.([interaction.layoutWidth, interaction.layoutHeight]);
        // Keep Moveable's own control box on the same constrained geometry as
        // the live proxy. Post-processing only our target lets Moveable cross
        // through zero internally, which makes the handle lag and oscillate.
        interaction.ratioManagedByMoveable = configureResizeGesture(
          event,
          interaction);
        event.dragStart?.set?.([0, 0]);
      }
    })
    .on("resize", updateResize)
    .on("resizeEnd", event => { void completeInteraction("resize", event); })
    .on("rotateStart", event => {
      if (beginInteraction("rotate", event)) event.set?.(0);
    })
    .on("rotate", updateRotate)
    .on("rotateEnd", event => { void completeInteraction("rotate", event); })
    .on("snap", event => {
      const vertical = event?.guidelines?.vertical?.map(item => item.pos?.[0]).join(",") || "";
      const horizontal = event?.guidelines?.horizontal?.map(item => item.pos?.[1]).join(",") || "";
      void invoke("NotifyCanvasSnap", `${vertical}|${horizontal}`);
    });

  function select(id) {
    selectedId = id;
    overlays.forEach((element, key) => element.classList.toggle("selected", key === id));
    const item = itemsById.get(id);
    const target = overlays.get(id) || null;
    const lineDragTarget = target?.querySelector?.("[data-line-drag-target]") || null;
    const isAbsolute = Boolean(item?.absolute);
    const isFlowChild = Boolean(item?.parentId) && !isAbsolute;
    moveable.target = item && item.visible && !item.locked && (isAbsolute || isFlowChild) ? target : null;
    moveable.dragTarget = moveable.target ? lineDragTarget : null;
    moveable.resizable = Boolean(item && (isAbsolute || isFlowChild));
    moveable.scalable = false;
    moveable.rotatable = isAbsolute;
    const allDirections = finePointerMode ? absoluteResizeDirections : coarseResizeDirections;
    moveable.renderDirections = !item ? []
      : item.resizeMode === "horizontal" ? ["w", "e"]
      : item.resizeMode === "vertical" ? ["n", "s"]
      : item.resizeMode === "text"
        ? (finePointerMode ? ["nw", "n", "ne", "e", "se", "s", "sw", "w"].filter(direction => direction !== "n" && direction !== "s") : coarseResizeDirections)
        : allDirections;
    moveable.elementGuidelines = Array.from(overlays.entries())
      .filter(([key]) => key !== id && itemsById.get(key)?.visible)
      .map(([, element]) => element);
    // Ratio is enforced manually only for corner handles. Enabling Moveable's
    // global keepRatio would also make side handles resize both axes.
    moveable.keepRatio = false;
    moveable.updateRect();
  }

  function enqueueScene(task) {
    const scheduled = sceneQueue.then(task, task);
    sceneQueue = scheduled.catch(error => {
      console.error("Mac template canvas scene update failed.", error);
    });
    return scheduled;
  }

  function layoutScene() {
    stage.style.width = `${toScenePixels(sceneWidth, viewScale)}px`;
    stage.style.height = `${toScenePixels(sceneHeight, viewScale)}px`;
    stage.style.transform = "none";
    moveable.verticalGuidelines = [0, toScenePixels(sceneWidth / 2, viewScale), toScenePixels(sceneWidth, viewScale)];
    moveable.horizontalGuidelines = [0, toScenePixels(sceneHeight / 2, viewScale), toScenePixels(sceneHeight, viewScale)];

    overlays.forEach((element, id) => {
      const item = itemsById.get(id);
      if (!item) return;
      applyOverlayGeometry(element, item, viewScale);
    });
  }

  async function waitForPreviewImage() {
    const images = Array.from(root.parentElement?.querySelectorAll?.(".wm-scene-image") || []);
    await Promise.all(images.map(async image => {
      if (typeof image.decode === "function") {
        try {
          await image.decode();
        } catch {
          // A newer layer version may replace this source while queued.
        }
        return;
      }
      if (image.complete) return;
      await new Promise(resolve => {
        image.addEventListener("load", resolve, { once: true });
        image.addEventListener("error", resolve, { once: true });
      });
    }));
  }

  function applyScene(canvasWidth, canvasHeight, nextViewScale, items, nextSelectedId) {
    // Blazor has already applied this scene's authoritative layer styles.
    // Forget the temporary gesture transform without restoring the pre-gesture
    // transform over the newly committed frame.
    clearPendingInteractionVisual(false);
    viewScale = asPositiveScale(nextViewScale);
    sceneWidth = asFinite(canvasWidth, 0);
    sceneHeight = asFinite(canvasHeight, 0);
    const sceneItems = Array.isArray(items) ? items : [];
    overlays.forEach(element => element.remove());
    itemsById = new Map(sceneItems.map(item => [item.id, item]));
    overlays = new Map();

    sceneItems.forEach(item => {
      const element = document.createElement("div");
      element.className = `canvas-control ${item.type.toLowerCase()}`;
      element.dataset.controlId = item.id;
      element.dataset.resizeMode = item.resizeMode || "";
      element.style.position = "absolute";
      element.style.transformOrigin = "center";
      element.style.display = item.visible ? "block" : "none";
      // A thin line can be only one or two CSS pixels high. Keep its layout
      // bounds exact while giving pointer input a usable, invisible hit area.
      if (item.type === "WMLine"
        && (item.resizeMode === "horizontal" || item.resizeMode === "vertical")) {
        const lineDragTarget = document.createElement("div");
        lineDragTarget.className = "canvas-line-drag-target";
        lineDragTarget.dataset.controlId = item.id;
        lineDragTarget.dataset.lineDragTarget = "true";
        lineDragTarget.setAttribute("aria-hidden", "true");
        element.appendChild(lineDragTarget);
      }
      element.addEventListener("pointerdown", event => {
        if (panMode) return;
        event.stopPropagation();
        root.closest(".mac-canvas-viewport")?.focus({ preventScroll: true });
      });
      overlays.set(item.id, element);
    });

    sceneItems.forEach(item => {
      const element = overlays.get(item.id);
      const parent = item.parentId ? overlays.get(item.parentId) : stage;
      (parent || stage).appendChild(element);
    });
    layoutScene();
    select(nextSelectedId);
  }

  return {
    setScene(canvasWidth, canvasHeight, nextViewScale, items, nextSelectedId) {
      if (disposed) return Promise.resolve();
      scenePending = true;
      const epoch = ++sceneEpoch;
      const cancellation = cancelInteraction();
      return enqueueScene(async () => {
        await cancellation;
        await waitForPreviewImage();
        if (disposed || epoch !== sceneEpoch) return;
        applyScene(canvasWidth, canvasHeight, nextViewScale, items, nextSelectedId);
        scenePending = false;
      });
    },
    setSelected(id) {
      if (!disposed) select(id);
    },
    setViewScale(value) {
      if (disposed) return;
      viewScale = asPositiveScale(value);
      layoutScene();
      moveable.updateRect();
    },
    setPanMode(value) {
      panMode = Boolean(value);
      if (panMode) moveable.target = null;
      else select(selectedId);
    },
    setSnappingEnabled(value) {
      snappingEnabled = Boolean(value);
      moveable.snappable = snappingEnabled;
    },
    cancelInteraction() {
      return cancelInteraction();
    },
    hasActiveInteraction() {
      return Boolean(interaction || pointerStart || viewportGesture);
    },
    dispose() {
      if (disposePromise) return disposePromise;
      cancelInteraction();
      disposed = true;
      scenePending = true;
      sceneEpoch++;
      const cancellation = interactionLifecycle;
      disposePromise = enqueueScene(async () => {
        await cancellation;
        root.removeEventListener("pointerdown", rememberPointerStart, true);
        root.removeEventListener("pointerdown", stopCanvasPointerPropagation);
        root.removeEventListener("pointermove", scheduleActivePointerPosition, true);
        root.removeEventListener("dragstart", preventNativeDrag, true);
        root.ownerDocument.removeEventListener("selectstart", preventCanvasSelection, true);
        root.ownerDocument.removeEventListener("pointerup", finishActivePointerInteraction, true);
        root.ownerDocument.removeEventListener("pointercancel", cancelActivePointerInteraction, true);
        root.ownerDocument.removeEventListener("mouseup", finishActivePointerInteraction, true);
        root.ownerDocument.defaultView?.removeEventListener("pointerup", finishActivePointerInteraction, true);
        root.ownerDocument.defaultView?.removeEventListener("mouseup", finishActivePointerInteraction, true);
        root.ownerDocument.defaultView?.removeEventListener("pointermove", finishReleasedPointerInteraction, true);
        root.ownerDocument.defaultView?.removeEventListener("mousemove", finishReleasedPointerInteraction, true);
        root.removeEventListener("lostpointercapture", finishLostPointerCapture, true);
        viewport?.removeEventListener("pointerdown", beginViewportGesture, true);
        viewport?.removeEventListener("pointermove", updateViewportGesture, true);
        viewport?.removeEventListener("pointerup", finishViewportGesture, true);
        viewport?.removeEventListener("pointercancel", finishViewportGesture, true);
        clearPendingInteractionVisual();
        if (pointerAnimationFrame != null) cancelAnimationFrame(pointerAnimationFrame);
        pointerAnimationFrame = null;
        pendingPointerEvent = null;
        if (viewportGestureFrame != null) cancelAnimationFrame(viewportGestureFrame);
        viewportGestureFrame = null;
        activeTouches.clear();
        viewportGesture = null;
        root.classList.remove("canvas-interacting");
        moveable.destroy();
        root.replaceChildren();
        itemsById.clear();
        overlays.clear();
      });
      return disposePromise;
    }
  };
}
