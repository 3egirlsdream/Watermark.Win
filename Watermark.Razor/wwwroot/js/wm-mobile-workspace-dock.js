function clamp(value, minimum, maximum) {
  return Math.min(maximum, Math.max(minimum, value));
}

function panelGeometry(root) {
  const rootHeight = Math.max(1, root.getBoundingClientRect().height);
  const toolbarHeight = root.querySelector(".workspace-toolbar")?.getBoundingClientRect().height || 64;
  const safeBottomProbe = root.querySelector(".wm-dock-safe-bottom-probe")?.getBoundingClientRect().height || 0;
  const safeBottom = Math.max(0, safeBottomProbe);
  const modeHeight = root.querySelector(".wm-dock-modes")?.getBoundingClientRect().height || 52 + safeBottom;
  const collapsed = Math.max(52 + safeBottom, modeHeight);
  const half = Math.max(collapsed, Math.min(340, Math.max(248, rootHeight * 0.32)) + safeBottom);
  const expanded = Math.max(half, Math.min(rootHeight * 0.72, rootHeight - toolbarHeight - 28));

  return {
    rootHeight,
    minimum: collapsed,
    maximum: expanded,
    points: [
      { name: "Collapsed", height: collapsed },
      { name: "Half", height: half },
      { name: "Expanded", height: expanded }
    ]
  };
}

function nearestPoint(points, height) {
  return points.reduce((nearest, point) =>
    Math.abs(point.height - height) < Math.abs(nearest.height - height) ? point : nearest,
  points[0]);
}

export function attachDockResize(root, dotNetReference) {
  const handle = root?.querySelector(".wm-dock-handle");
  const dock = root?.querySelector(".workspace-mobile-dock");
  if (!root || !handle || !dock) return { dispose() {} };

  let pointerId = null;
  let startY = 0;
  let startHeight = 0;
  let moved = false;
  let suppressClick = false;

  const currentHeight = () => dock.getBoundingClientRect().height;

  const updateAria = height => {
    const geometry = panelGeometry(root);
    const percent = clamp(Math.round(height / geometry.rootHeight * 100), 1, 100);
    handle.setAttribute("aria-valuemin", `${Math.round(geometry.minimum / geometry.rootHeight * 100)}`);
    handle.setAttribute("aria-valuemax", `${Math.round(geometry.maximum / geometry.rootHeight * 100)}`);
    handle.setAttribute("aria-valuenow", `${percent}`);
    handle.setAttribute("aria-valuetext", `工具面板高度 ${percent}%`);
  };

  const applyHeight = height => {
    const geometry = panelGeometry(root);
    const next = clamp(height, geometry.minimum, geometry.maximum);
    root.style.setProperty("--panel-height", `${Math.round(next)}px`);
    updateAria(next);
    return next;
  };

  const clearTransientHeight = () => {
    requestAnimationFrame(() => {
      root.style.removeProperty("--panel-height");
      root.classList.remove("is-panel-dragging");
      updateAria(currentHeight());
    });
  };

  const commitPoint = point => {
    root.style.setProperty("--panel-height", `${Math.round(point.height)}px`);
    updateAria(point.height);
    Promise.resolve(dotNetReference.invokeMethodAsync("OnDockResizeCommitted", point.name))
      .finally(clearTransientHeight);
  };

  const onPointerDown = event => {
    if (pointerId !== null || (event.isPrimary === false)) return;
    pointerId = event.pointerId;
    startY = event.clientY;
    startHeight = currentHeight();
    moved = false;
    root.classList.add("is-panel-dragging");
    handle.classList.add("is-dragging");
    handle.setPointerCapture?.(pointerId);
    updateAria(startHeight);
    event.preventDefault();
  };

  const onPointerMove = event => {
    if (event.pointerId !== pointerId) return;
    const delta = event.clientY - startY;
    if (!moved && Math.abs(delta) < 4) return;
    moved = true;
    applyHeight(startHeight - delta);
    event.preventDefault();
  };

  const finish = event => {
    if (event.pointerId !== pointerId) return;
    handle.releasePointerCapture?.(pointerId);
    handle.classList.remove("is-dragging");
    pointerId = null;

    if (!moved) {
      root.classList.remove("is-panel-dragging");
      return;
    }

    suppressClick = true;
    const geometry = panelGeometry(root);
    commitPoint(nearestPoint(geometry.points, currentHeight()));
    setTimeout(() => { suppressClick = false; }, 0);
    event.preventDefault();
  };

  const onClickCapture = event => {
    if (!suppressClick) return;
    event.preventDefault();
    event.stopImmediatePropagation();
  };

  updateAria(currentHeight());
  handle.addEventListener("pointerdown", onPointerDown);
  handle.addEventListener("pointermove", onPointerMove);
  handle.addEventListener("pointerup", finish);
  handle.addEventListener("pointercancel", finish);
  handle.addEventListener("click", onClickCapture, true);

  return {
    dispose() {
      handle.removeEventListener("pointerdown", onPointerDown);
      handle.removeEventListener("pointermove", onPointerMove);
      handle.removeEventListener("pointerup", finish);
      handle.removeEventListener("pointercancel", finish);
      handle.removeEventListener("click", onClickCapture, true);
      root.style.removeProperty("--panel-height");
      root.classList.remove("is-panel-dragging");
      handle.classList.remove("is-dragging");
    }
  };
}
