const VIEWBOX_WIDTH = 320;
const VIEWBOX_HEIGHT = 240;
const PLOT_LEFT = 18;
const PLOT_TOP = 18;
const PLOT_WIDTH = 284;
const PLOT_HEIGHT = 204;
const UPDATE_INTERVAL_MS = 32;

function clamp(value, minimum, maximum) {
  return Math.min(maximum, Math.max(minimum, value));
}

export function attachCurve(element, dotNetReference) {
  if (!element) return { dispose() {} };

  let pointerId = null;
  let pointIndex = -1;
  let pending = null;
  let timer = null;
  let lastSentAt = 0;

  const coordinates = event => {
    const bounds = element.getBoundingClientRect();
    const viewX = (event.clientX - bounds.left) / Math.max(1, bounds.width) * VIEWBOX_WIDTH;
    const viewY = (event.clientY - bounds.top) / Math.max(1, bounds.height) * VIEWBOX_HEIGHT;
    return {
      x: clamp((viewX - PLOT_LEFT) / PLOT_WIDTH, 0, 1),
      y: clamp(1 - (viewY - PLOT_TOP) / PLOT_HEIGHT, 0, 1)
    };
  };

  const send = (value, isFinal) => {
    if (!value || pointIndex < 0) return;
    lastSentAt = performance.now();
    void dotNetReference.invokeMethodAsync(
      "OnCurvePointChanged", pointIndex, value.x, value.y, isFinal);
  };

  const flush = isFinal => {
    if (timer !== null) {
      clearTimeout(timer);
      timer = null;
    }
    const value = pending;
    pending = null;
    send(value, isFinal);
  };

  const schedule = value => {
    pending = value;
    const remaining = UPDATE_INTERVAL_MS - (performance.now() - lastSentAt);
    if (remaining <= 0) {
      flush(false);
      return;
    }
    if (timer !== null) return;
    timer = setTimeout(() => flush(false), remaining);
  };

  const onPointerDown = event => {
    const node = event.target.closest?.("[data-curve-index]");
    if (!node || pointerId !== null) return;
    pointerId = event.pointerId;
    pointIndex = Number.parseInt(node.dataset.curveIndex, 10);
    element.setPointerCapture?.(pointerId);
    event.preventDefault();
  };

  const onPointerMove = event => {
    if (event.pointerId !== pointerId) return;
    schedule(coordinates(event));
    event.preventDefault();
  };

  const finish = event => {
    if (event.pointerId !== pointerId) return;
    pending = coordinates(event);
    flush(true);
    element.releasePointerCapture?.(pointerId);
    pointerId = null;
    pointIndex = -1;
    event.preventDefault();
  };

  element.addEventListener("pointerdown", onPointerDown);
  element.addEventListener("pointermove", onPointerMove);
  element.addEventListener("pointerup", finish);
  element.addEventListener("pointercancel", finish);

  return {
    dispose() {
      if (timer !== null) clearTimeout(timer);
      element.removeEventListener("pointerdown", onPointerDown);
      element.removeEventListener("pointermove", onPointerMove);
      element.removeEventListener("pointerup", finish);
      element.removeEventListener("pointercancel", finish);
    }
  };
}
