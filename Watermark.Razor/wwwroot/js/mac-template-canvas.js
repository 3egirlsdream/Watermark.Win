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

function compose(item, deltaX = 0, deltaY = 0, scaleX = item.scaleX, scaleY = item.scaleY, rotation = item.rotation) {
  const offsetX = item.parentWidth * item.offsetXPercent / 100 + deltaX;
  const offsetY = item.parentHeight * item.offsetYPercent / 100 + deltaY;
  return `translate(${offsetX}px, ${offsetY}px) rotate(${rotation}deg) scale(${scaleX}, ${scaleY})`;
}

function asFinite(value, fallback) {
  return Number.isFinite(value) ? value : fallback;
}

function asPositiveScale(value) {
  return Number.isFinite(value) && value > 0 ? value : 1;
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
    return event.target || moveable.target || null;
  }

  function beginInteraction(kind, event) {
    if (disposed || scenePending || interactionBusy || panMode) {
      event.stop?.();
      return false;
    }

    const target = targetFor(event);
    const controlId = target?.dataset.controlId;
    const item = controlId ? itemsById.get(controlId) : null;
    if (!item || !item.visible || item.locked) {
      event.stop?.();
      return false;
    }

    const active = {
      token: ++nextInteractionToken,
      kind,
      start: { ...item },
      layoutWidth: layoutDimension(item.width),
      layoutHeight: layoutDimension(item.height),
      keepRatio: item.type !== "WMContainer",
      deltaX: 0,
      deltaY: 0,
      scaleX: item.scaleX,
      scaleY: item.scaleY,
      rotation: item.rotation
    };
    interaction = active;
    interactionBusy = true;
    active.beginPromise = invoke("BeginCanvasInteraction", controlId);
    interactionLifecycle = active.beginPromise;
    return true;
  }

  function updateOverlay() {
    if (!interaction || disposed) return;
    const target = overlays.get(interaction.start.id);
    if (!target) return;

    target.style.transform = compose(
      interaction.start,
      interaction.deltaX,
      interaction.deltaY,
      interaction.scaleX,
      interaction.scaleY,
      interaction.rotation);
  }

  function updateDrag(event) {
    if (!interaction) return;
    moveable.snappable = snappingEnabled && !inputEventFor(event)?.altKey;
    const [cssDeltaX, cssDeltaY] = pair(event.beforeDist ?? event.dist);
    interaction.deltaX = snapMovement(cssDeltaX / viewScale, event);
    interaction.deltaY = snapMovement(cssDeltaY / viewScale, event);
    updateOverlay();
  }

  function updateResize(event) {
    if (!interaction) return;
    moveable.snappable = snappingEnabled && !inputEventFor(event)?.altKey;
    const [cssDeltaX, cssDeltaY] = pair(event.drag?.beforeDist ?? event.drag?.dist);
    const relativeWidth = relativeResizeSize(event.width, interaction.layoutWidth);
    const relativeHeight = relativeResizeSize(event.height, interaction.layoutHeight);
    interaction.deltaX = cssDeltaX / viewScale;
    interaction.deltaY = cssDeltaY / viewScale;
    if (interaction.keepRatio) {
      const relativeSize = relativeWidth != null && relativeHeight != null ? relativeWidth : 1;
      interaction.scaleX = interaction.start.scaleX * relativeSize;
      interaction.scaleY = interaction.start.scaleY * relativeSize;
    } else {
      interaction.scaleX = interaction.start.scaleX * (relativeWidth ?? 1);
      interaction.scaleY = interaction.start.scaleY * (relativeHeight ?? 1);
    }
    updateOverlay();
  }

  function updateRotate(event) {
    if (!interaction) return;
    moveable.snappable = snappingEnabled && !inputEventFor(event)?.altKey;
    const rotation = interaction.start.rotation + asFinite(event.beforeRotate ?? event.rotate, 0);
    interaction.rotation = inputEventFor(event)?.shiftKey ? Math.round(rotation / 15) * 15 : rotation;
    updateOverlay();
  }

  function finishInteraction(active, callback) {
    if (active.finishPromise) return active.finishPromise;

    active.finishPromise = Promise.resolve(active.beginPromise)
      .then(callback)
      .catch(error => {
        console.error("Mac template canvas interaction callback failed.", error);
      })
      .finally(() => {
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
    if (target) target.style.transform = compose(active.start);
    return finishInteraction(active, () => invoke("CancelCanvasInteraction"));
  }

  function completeInteraction(kind, event) {
    const active = interaction;
    if (!active || active.kind !== kind) return interactionLifecycle;
    if (!event.lastEvent) {
      return cancelInteraction();
    }

    if (kind === "drag") updateDrag(event.lastEvent);
    if (kind === "resize") updateResize(event.lastEvent);
    if (kind === "rotate") updateRotate(event.lastEvent);
    moveable.snappable = snappingEnabled;

    interaction = null;
    const { start } = active;
    const offsetXPercent = start.parentWidth === 0
      ? start.offsetXPercent
      : start.offsetXPercent + active.deltaX / start.parentWidth * 100;
    const offsetYPercent = start.parentHeight === 0
      ? start.offsetYPercent
      : start.offsetYPercent + active.deltaY / start.parentHeight * 100;
    return finishInteraction(active, () => invoke("CommitCanvasInteraction", {
      controlId: start.id,
      kind,
      offsetXPercent,
      offsetYPercent,
      scaleX: active.scaleX,
      scaleY: active.scaleY,
      rotation: active.rotation
    }));
  }

  const moveable = new window.Moveable(stage, {
    container: root,
    target: null,
    draggable: true,
    resizable: true,
    rotatable: true,
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
    moveable.target = item && item.visible && !item.locked ? target : null;
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

  function applyScene(canvasWidth, canvasHeight, nextViewScale, items, nextSelectedId) {
    viewScale = asPositiveScale(nextViewScale);
    const sceneItems = Array.isArray(items) ? items : [];
    itemsById = new Map(sceneItems.map(item => [item.id, item]));
    overlays = new Map();
    stage.replaceChildren();
    stage.style.width = `${canvasWidth}px`;
    stage.style.height = `${canvasHeight}px`;
    stage.style.transform = `scale(${viewScale})`;
    moveable.verticalGuidelines = [0, canvasWidth / 2, canvasWidth];
    moveable.horizontalGuidelines = [0, canvasHeight / 2, canvasHeight];

    sceneItems.forEach(item => {
      const element = document.createElement("div");
      element.className = `canvas-control ${item.type.toLowerCase()}`;
      element.dataset.controlId = item.id;
      element.style.position = "absolute";
      element.style.left = `${item.x}px`;
      element.style.top = `${item.y}px`;
      element.style.width = `${Math.max(item.width, 1)}px`;
      element.style.height = `${Math.max(item.height, 1)}px`;
      element.style.transformOrigin = "center";
      element.style.transform = compose(item);
      element.style.display = item.visible ? "block" : "none";
      element.addEventListener("pointerdown", event => {
        if (panMode) return;
        event.stopPropagation();
        root.closest(".mac-canvas-viewport")?.focus({ preventScroll: true });
        void invoke("SelectCanvasControl", item.id);
      });
      overlays.set(item.id, element);
    });

    sceneItems.forEach(item => {
      const element = overlays.get(item.id);
      const parent = item.parentId ? overlays.get(item.parentId) : stage;
      (parent || stage).appendChild(element);
    });
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
      stage.style.transform = `scale(${viewScale})`;
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
        moveable.destroy();
        root.replaceChildren();
        itemsById.clear();
        overlays.clear();
      });
      return disposePromise;
    }
  };
}
