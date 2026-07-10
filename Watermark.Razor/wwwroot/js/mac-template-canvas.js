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
  let disposed = false;

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
    const target = targetFor(event);
    const controlId = target?.dataset.controlId;
    const item = controlId ? itemsById.get(controlId) : null;
    if (!item || !item.visible || item.locked) {
      interaction = null;
      event.stop?.();
      return false;
    }

    interaction = {
      kind,
      start: { ...item },
      deltaX: 0,
      deltaY: 0,
      scaleX: item.scaleX,
      scaleY: item.scaleY,
      rotation: item.rotation
    };
    void invoke("BeginCanvasInteraction", controlId);
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
    const [cssDeltaX, cssDeltaY] = pair(event.beforeDist ?? event.dist);
    interaction.deltaX = cssDeltaX / viewScale;
    interaction.deltaY = cssDeltaY / viewScale;
    updateOverlay();
  }

  function updateResize(event) {
    if (!interaction) return;
    const [cssDeltaX, cssDeltaY] = pair(event.drag?.beforeDist ?? event.drag?.dist);
    const [relativeScaleX, relativeScaleY] = pair(event.scale, [1, 1]);
    interaction.deltaX = cssDeltaX / viewScale;
    interaction.deltaY = cssDeltaY / viewScale;
    interaction.scaleX = interaction.start.scaleX * relativeScaleX;
    interaction.scaleY = interaction.start.scaleY * relativeScaleY;
    updateOverlay();
  }

  function updateRotate(event) {
    if (!interaction) return;
    interaction.rotation = interaction.start.rotation + asFinite(event.beforeRotate ?? event.rotate, 0);
    updateOverlay();
  }

  function cancelInteraction() {
    if (interaction) {
      const target = overlays.get(interaction.start.id);
      if (target) target.style.transform = compose(interaction.start);
    }
    interaction = null;
    void invoke("CancelCanvasInteraction");
  }

  function completeInteraction(kind, event) {
    const active = interaction;
    if (!active || active.kind !== kind) return;
    if (!event.lastEvent) {
      cancelInteraction();
      return;
    }

    if (kind === "drag") updateDrag(event.lastEvent);
    if (kind === "resize") updateResize(event.lastEvent);
    if (kind === "rotate") updateRotate(event.lastEvent);

    interaction = null;
    const { start } = active;
    const offsetXPercent = start.parentWidth === 0
      ? start.offsetXPercent
      : start.offsetXPercent + active.deltaX / start.parentWidth * 100;
    const offsetYPercent = start.parentHeight === 0
      ? start.offsetYPercent
      : start.offsetYPercent + active.deltaY / start.parentHeight * 100;
    void invoke("CommitCanvasInteraction", {
      controlId: start.id,
      kind,
      offsetXPercent,
      offsetYPercent,
      scaleX: active.scaleX,
      scaleY: active.scaleY,
      rotation: active.rotation
    });
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
    .on("dragEnd", event => completeInteraction("drag", event))
    .on("resizeStart", event => {
      if (beginInteraction("resize", event)) event.set?.([1, 1]);
    })
    .on("resize", updateResize)
    .on("resizeEnd", event => completeInteraction("resize", event))
    .on("rotateStart", event => {
      if (beginInteraction("rotate", event)) event.set?.(0);
    })
    .on("rotate", updateRotate)
    .on("rotateEnd", event => completeInteraction("rotate", event));

  function select(id) {
    selectedId = id;
    overlays.forEach((element, key) => element.classList.toggle("selected", key === id));
    const item = itemsById.get(id);
    const target = overlays.get(id) || null;
    moveable.target = item && item.visible && !item.locked ? target : null;
    if (item) moveable.keepRatio = item.type !== "WMContainer";
    moveable.updateRect();
  }

  return {
    setScene(canvasWidth, canvasHeight, nextViewScale, items, nextSelectedId) {
      if (disposed) return;
      viewScale = asPositiveScale(nextViewScale);
      const sceneItems = Array.isArray(items) ? items : [];
      itemsById = new Map(sceneItems.map(item => [item.id, item]));
      overlays = new Map();
      interaction = null;
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
          event.stopPropagation();
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
    dispose() {
      if (disposed) return;
      disposed = true;
      interaction = null;
      moveable.destroy();
      root.replaceChildren();
      itemsById.clear();
      overlays.clear();
    }
  };
}
