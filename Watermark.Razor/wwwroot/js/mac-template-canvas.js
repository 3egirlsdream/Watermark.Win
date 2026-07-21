const vendorUrl = "../vendor/moveable/moveable.min.js";
let vendorPromise;

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
  rotation = item?.rotation) {
  if (!item?.parentId || item.parentWidth <= 0 || item.parentHeight <= 0) {
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
  const centerX = clampCenter(desiredCenterX, halfWidth, item.parentWidth);
  const centerY = clampCenter(desiredCenterY, halfHeight, item.parentHeight);
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

function relativeResizeSize(size, startSize) {
  if (!Number.isFinite(size) || size < 0 || !Number.isFinite(startSize) || startSize <= 0) return null;
  return size / startSize;
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

export async function createEditor(root, callback) {
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
  let pointerStart = null;
  let pendingInteractionVisual = null;
  let pendingInteractionVisualTimer = null;

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
    if (panMode) return;
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
      clientY: event.clientY
    };
    if (!deferSelection && hitControlId && hitControlId !== selectedId) {
      select(hitControlId);
      void invoke("SelectCanvasControl", hitControlId);
    }
  }

  function finishActivePointerInteraction(event) {
    const pendingSelection = interaction?.kind === "drag"
      ? interaction.pendingSelection
      : null;
    if (pendingSelection?.hitControlId
      && Math.hypot(event.clientX - pendingSelection.clientX, event.clientY - pendingSelection.clientY) < 4) {
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
        && Math.hypot(event.clientX - pending.clientX, event.clientY - pending.clientY) < 4) {
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
    if (interaction && event.buttons === 0)
      finishActivePointerInteraction(event);
  }

  function cancelActivePointerInteraction() {
    if (!interaction) return;
    if (hasInteractionChange(interaction))
      void completeInteraction(interaction.kind, { lastEvent: true });
    else
      void cancelInteraction();
  }

  function applyActivePointerPosition(event) {
    if (interaction?.kind === "drag") updateDrag(event);
  }

  root.addEventListener("pointerdown", rememberPointerStart, true);
  root.addEventListener("pointerdown", stopCanvasPointerPropagation);
  root.addEventListener("pointermove", applyActivePointerPosition, true);
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
    const target = overlays.get(item.id);
    const preview = root.parentElement?.querySelector?.(".mac-canvas-preview");
    if (!target || !preview?.currentSrc && !preview?.src) return null;
    const targetRect = target.getBoundingClientRect();
    const stageRect = stage.getBoundingClientRect();
    const previewRect = preview.getBoundingClientRect();
    if (targetRect.width <= 0 || targetRect.height <= 0 || previewRect.width <= 0 || previewRect.height <= 0)
      return null;

    const ghost = document.createElement("div");
    ghost.className = "canvas-interaction-ghost canvas-container-drag-ghost";
    ghost.style.left = `${targetRect.left - stageRect.left}px`;
    ghost.style.top = `${targetRect.top - stageRect.top}px`;
    ghost.style.width = `${targetRect.width}px`;
    ghost.style.height = `${targetRect.height}px`;
    ghost.style.backgroundImage = `url(${JSON.stringify(preview.currentSrc || preview.src)})`;
    ghost.style.backgroundSize = `${previewRect.width}px ${previewRect.height}px`;
    ghost.style.backgroundPosition = `${previewRect.left - targetRect.left}px ${previewRect.top - targetRect.top}px`;
    stage.appendChild(ghost);
    return {
      element: ghost,
      target,
      startRect: targetRect,
      stage,
      previewWidth: previewRect.width,
      previewHeight: previewRect.height,
      backgroundOffsetX: previewRect.left - targetRect.left,
      backgroundOffsetY: previewRect.top - targetRect.top
    };
  }

  function updateInteractionVisual(active) {
    const ghost = active?.dragGhost;
    if (!ghost) return;
    const rect = ghost.target.getBoundingClientRect();
    const stageRect = ghost.stage.getBoundingClientRect();
    const scaleX = ghost.startRect.width > 0 ? rect.width / ghost.startRect.width : 1;
    const scaleY = ghost.startRect.height > 0 ? rect.height / ghost.startRect.height : 1;
    ghost.element.style.left = `${rect.left - stageRect.left}px`;
    ghost.element.style.top = `${rect.top - stageRect.top}px`;
    ghost.element.style.width = `${rect.width}px`;
    ghost.element.style.height = `${rect.height}px`;
    ghost.element.style.backgroundSize = `${ghost.previewWidth * scaleX}px ${ghost.previewHeight * scaleY}px`;
    ghost.element.style.backgroundPosition = `${ghost.backgroundOffsetX * scaleX}px ${ghost.backgroundOffsetY * scaleY}px`;
  }

  function applyCommittedInteraction(active, committed) {
    const controlId = committed?.controlId;
    if (!controlId || controlId !== active.start.id) return false;

    const current = itemsById.get(controlId);
    if (!current) return false;
    const next = {
      ...current,
      offsetXPercent: asFinite(committed.offsetXPercent, current.offsetXPercent),
      offsetYPercent: asFinite(committed.offsetYPercent, current.offsetYPercent),
      scaleX: asFinite(committed.scaleX, current.scaleX),
      scaleY: asFinite(committed.scaleY, current.scaleY),
      rotation: asFinite(committed.rotation, current.rotation)
    };
    itemsById.set(controlId, next);

    const target = overlays.get(controlId);
    if (target) target.style.transform = compose(next, viewScale);
    updateInteractionVisual(active);
    moveable.updateRect();
    return true;
  }

  function restoreInteractionStart(active) {
    const target = overlays.get(active.start.id);
    if (target) target.style.transform = compose(active.start, viewScale);
    active.keepVisualUntilScene = false;
  }

  function removeInteractionVisual(active) {
    active?.dragGhost?.element?.remove();
    if (active) active.dragGhost = null;
  }

  function clearPendingInteractionVisual() {
    if (pendingInteractionVisualTimer != null) {
      clearTimeout(pendingInteractionVisualTimer);
      pendingInteractionVisualTimer = null;
    }
    pendingInteractionVisual?.element?.remove();
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
      || (kind !== "drag" && !isAbsolute)) {
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
      scaleX: item.scaleX,
      scaleY: item.scaleY,
      rotation: item.rotation
    };
    const pointerId = asFinite(initialPointer.pointerId, NaN);
    if (Number.isFinite(pointerId) && root.setPointerCapture) {
      try {
        root.setPointerCapture(pointerId);
        active.pointerId = pointerId;
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
    active.beginPromise = invoke("BeginCanvasInteraction", controlId).then(accepted => accepted === true);
    interactionLifecycle = active.beginPromise;
    return true;
  }

  function updateOverlay() {
    if (!interaction || disposed) return;
    const target = overlays.get(interaction.start.id);
    if (!target) return;

    target.style.transform = compose(
      interaction.start,
      viewScale,
      interaction.deltaX,
      interaction.deltaY,
      interaction.scaleX,
      interaction.scaleY,
      interaction.rotation);
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
    const [resizeDeltaX, resizeDeltaY] = pair(event.dist);
    const [directionX, directionY] = pair(event.direction);
    const [dragDeltaX, dragDeltaY] = pair(event.drag?.beforeDist ?? event.drag?.dist, [resizeDeltaX / 2, resizeDeltaY / 2]);
    const relativeWidth = directionX === 0
      ? null
      : relativeResizeSize(interaction.layoutWidth + directionX * resizeDeltaX, interaction.layoutWidth);
    const relativeHeight = directionY === 0
      ? null
      : relativeResizeSize(interaction.layoutHeight + directionY * resizeDeltaY, interaction.layoutHeight);
    interaction.deltaX = toCanvasUnits(dragDeltaX, viewScale);
    interaction.deltaY = toCanvasUnits(dragDeltaY, viewScale);
    if (interaction.keepRatio) {
      const relativeSize = [relativeWidth, relativeHeight]
        .filter(value => value != null)
        .reduce((result, value) => Math.abs(value - 1) > Math.abs(result - 1) ? value : result, 1);
      interaction.scaleX = interaction.start.scaleX * relativeSize;
      interaction.scaleY = interaction.start.scaleY * relativeSize;
    } else {
      interaction.scaleX = interaction.start.scaleX * (relativeWidth ?? 1);
      interaction.scaleY = interaction.start.scaleY * (relativeHeight ?? 1);
    }
    [interaction.scaleX, interaction.scaleY] = clampChildScale(
      interaction.start,
      interaction.scaleX,
      interaction.scaleY,
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
    if (target) target.style.transform = compose(active.start, viewScale);
    return finishInteraction(active, () => invoke("CancelCanvasInteraction"));
  }

  function hasInteractionChange(active) {
    const epsilon = 0.0001;
    return Math.abs(active.deltaX) > epsilon
      || Math.abs(active.deltaY) > epsilon
      || Math.abs(active.scaleX - active.start.scaleX) > epsilon
      || Math.abs(active.scaleY - active.start.scaleY) > epsilon
      || Math.abs(active.rotation - active.start.rotation) > epsilon;
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
    const offsetXPercent = start.parentWidth === 0
      ? start.offsetXPercent
      : start.offsetXPercent + active.deltaX / start.parentWidth * 100;
    const offsetYPercent = start.parentHeight === 0
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
        rotation: active.rotation
      });
      if (!applyCommittedInteraction(active, committed)) restoreInteractionStart(active);
    });
  }

  const moveable = new window.Moveable(stage, {
    container: root,
    target: null,
    draggable: true,
    resizable: true,
    renderDirections: ["e", "se"],
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
      beginInteraction("resize", event);
    })
    .on("resize", updateResize)
    .on("resizeEnd", event => { void completeInteraction("resize", event); })
    .on("rotateStart", event => {
      if (beginInteraction("rotate", event)) event.set?.(0);
    })
    .on("rotate", updateRotate)
    .on("rotateEnd", event => { void completeInteraction("rotate", event); });

  function select(id) {
    selectedId = id;
    overlays.forEach((element, key) => element.classList.toggle("selected", key === id));
    const item = itemsById.get(id);
    const target = overlays.get(id) || null;
    const isAbsolute = Boolean(item?.absolute);
    const isFlowChild = Boolean(item?.parentId) && !isAbsolute;
    moveable.target = item && item.visible && !item.locked && (isAbsolute || isFlowChild) ? target : null;
    moveable.resizable = isAbsolute;
    moveable.rotatable = isAbsolute;
    moveable.renderDirections = isAbsolute ? ["e", "se"] : [];
    moveable.elementGuidelines = Array.from(overlays.entries())
      .filter(([key]) => key !== id && itemsById.get(key)?.visible)
      .map(([, element]) => element);
    if (item) moveable.keepRatio = item.type !== "WMContainer";
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
      element.style.left = `${toScenePixels(item.x, viewScale)}px`;
      element.style.top = `${toScenePixels(item.y, viewScale)}px`;
      element.style.width = `${Math.max(toScenePixels(item.width, viewScale), 1)}px`;
      element.style.height = `${Math.max(toScenePixels(item.height, viewScale), 1)}px`;
      element.style.transform = compose(item, viewScale);
    });
  }

  async function waitForPreviewImage() {
    const preview = root.parentElement?.querySelector?.(".mac-canvas-preview");
    if (!preview) return;

    if (typeof preview.decode === "function") {
      try {
        await preview.decode();
      } catch {
        // A newer preview may replace the source while this scene is queued.
      }
      return;
    }

    if (preview.complete) return;
    await new Promise(resolve => {
      preview.addEventListener("load", resolve, { once: true });
      preview.addEventListener("error", resolve, { once: true });
    });
  }

  function applyScene(canvasWidth, canvasHeight, nextViewScale, items, nextSelectedId) {
    clearPendingInteractionVisual();
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
      element.style.position = "absolute";
      element.style.transformOrigin = "center";
      element.style.display = item.visible ? "block" : "none";
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
        root.removeEventListener("pointermove", applyActivePointerPosition, true);
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
        clearPendingInteractionVisual();
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
