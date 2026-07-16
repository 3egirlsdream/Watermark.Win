const mobileQuery = window.matchMedia("(max-width: 860px)");
const snapPoints = [0.36, 0.5, 0.72];

function clamp(value, minimum, maximum) {
  return Math.min(maximum, Math.max(minimum, value));
}

function nearestSnap(value) {
  return snapPoints.reduce((nearest, candidate) =>
    Math.abs(candidate - value) < Math.abs(nearest - value) ? candidate : nearest,
  snapPoints[0]);
}

export function attachDrawerResize(root, handle) {
  if (!root || !handle) return { dispose() {} };

  let pointerId = null;
  let startY = 0;
  let startHeight = 0;

  const setHeight = (height, snap = false) => {
    const rootHeight = Math.max(1, root.getBoundingClientRect().height);
    const minimum = Math.min(220, rootHeight * 0.52);
    const maximum = Math.max(minimum, rootHeight * 0.72);
    let next = clamp(height, minimum, maximum);
    if (snap) next = nearestSnap(next / rootHeight) * rootHeight;
    const percent = clamp(Math.round(next / rootHeight * 100), 1, 100);
    root.style.setProperty("--mobile-designer-drawer-height", `${Math.round(next)}px`);
    handle.setAttribute("aria-valuenow", `${percent}`);
    handle.setAttribute("aria-valuetext", `工具面板高度 ${percent}%`);
  };

  const currentHeight = () => {
    const rootRect = root.getBoundingClientRect();
    const panel = root.querySelector(".mobile-panel-visible");
    return panel?.getBoundingClientRect().height || rootRect.height * 0.5;
  };

  const onPointerDown = event => {
    if (!mobileQuery.matches || pointerId !== null) return;
    pointerId = event.pointerId;
    startY = event.clientY;
    startHeight = currentHeight();
    handle.classList.add("is-dragging");
    handle.setPointerCapture?.(pointerId);
    event.preventDefault();
  };

  const onPointerMove = event => {
    if (event.pointerId !== pointerId) return;
    setHeight(startHeight - (event.clientY - startY));
    event.preventDefault();
  };

  const finish = event => {
    if (event.pointerId !== pointerId) return;
    setHeight(currentHeight(), true);
    handle.releasePointerCapture?.(pointerId);
    handle.classList.remove("is-dragging");
    pointerId = null;
    event.preventDefault();
  };

  const reset = event => {
    if (!mobileQuery.matches) return;
    setHeight(root.getBoundingClientRect().height * 0.5);
    event.preventDefault();
  };

  handle.setAttribute("aria-valuemin", "36");
  handle.setAttribute("aria-valuemax", "72");
  handle.addEventListener("pointerdown", onPointerDown);
  handle.addEventListener("pointermove", onPointerMove);
  handle.addEventListener("pointerup", finish);
  handle.addEventListener("pointercancel", finish);
  handle.addEventListener("dblclick", reset);

  return {
    dispose() {
      handle.removeEventListener("pointerdown", onPointerDown);
      handle.removeEventListener("pointermove", onPointerMove);
      handle.removeEventListener("pointerup", finish);
      handle.removeEventListener("pointercancel", finish);
      handle.removeEventListener("dblclick", reset);
    }
  };
}
