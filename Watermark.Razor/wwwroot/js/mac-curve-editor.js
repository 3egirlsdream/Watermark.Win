export function createCurveEditor(svg, dotNet) {
  const dotNetInterval = 32;
  let active = null;
  let animationFrame = 0;
  let pendingPosition = null;
  let latestDotNetUpdate = null;
  let dotNetUpdateInFlight = false;
  let dotNetTimer = 0;
  let lastDotNetUpdate = 0;
  let disposed = false;

  function positionFromEvent(event) {
    const rect = svg.getBoundingClientRect();
    return {
      x: Math.max(0, Math.min(1, (event.clientX - rect.left) / Math.max(1, rect.width))),
      y: Math.max(0, Math.min(1, 1 - (event.clientY - rect.top) / Math.max(1, rect.height)))
    };
  }

  function constrainPosition(index, position) {
    const handles = Array.from(svg.querySelectorAll("[data-curve-handle]"));
    const previous = index > 0 ? Number(handles[index - 1]?.getAttribute("cx")) / 100 : 0;
    const next = index < handles.length - 1 ? Number(handles[index + 1]?.getAttribute("cx")) / 100 : 1;
    return {
      x: index === 0
        ? 0
        : index === handles.length - 1
          ? 1
          : Math.max(previous + .01, Math.min(next - .01, position.x)),
      y: position.y
    };
  }

  function renderPosition(index, position) {
    const x = position.x * 100;
    const y = (1 - position.y) * 100;
    const hitTarget = svg.querySelector(`[data-curve-point="${index}"]`);
    const handle = svg.querySelector(`[data-curve-handle="${index}"]`);
    hitTarget?.setAttribute("cx", String(x));
    hitTarget?.setAttribute("cy", String(y));
    handle?.setAttribute("cx", String(x));
    handle?.setAttribute("cy", String(y));

    const points = Array.from(svg.querySelectorAll("[data-curve-handle]"))
      .map(point => `${point.getAttribute("cx")},${point.getAttribute("cy")}`)
      .join(" ");
    svg.querySelector("[data-curve-polyline]")?.setAttribute("points", points);
  }

  function pumpDotNetUpdate() {
    if (disposed || dotNetUpdateInFlight || !latestDotNetUpdate) return;
    const wait = Math.max(0, dotNetInterval - (performance.now() - lastDotNetUpdate));
    if (wait > 0 && !latestDotNetUpdate.final) {
      if (!dotNetTimer) {
        dotNetTimer = window.setTimeout(() => {
          dotNetTimer = 0;
          pumpDotNetUpdate();
        }, wait);
      }
      return;
    }

    if (dotNetTimer) {
      clearTimeout(dotNetTimer);
      dotNetTimer = 0;
    }
    const update = latestDotNetUpdate;
    latestDotNetUpdate = null;
    dotNetUpdateInFlight = true;
    lastDotNetUpdate = performance.now();
    dotNet.invokeMethodAsync("MovePoint", update.index, update.x, update.y, update.final)
      .catch(() => {})
      .finally(() => {
        dotNetUpdateInFlight = false;
        pumpDotNetUpdate();
      });
  }

  function queueDotNetUpdate(index, position, final = false) {
    latestDotNetUpdate = { index, x: position.x, y: position.y, final };
    pumpDotNetUpdate();
  }

  function renderPendingPosition() {
    animationFrame = 0;
    if (!active || !pendingPosition) return;
    const position = constrainPosition(active.index, pendingPosition);
    pendingPosition = null;
    renderPosition(active.index, position);
    queueDotNetUpdate(active.index, position);
  }

  function queuePosition(event) {
    pendingPosition = positionFromEvent(event);
    if (!animationFrame) animationFrame = requestAnimationFrame(renderPendingPosition);
  }

  function onPointerDown(event) {
    if (event.button !== 0 && event.pointerType === "mouse") return;
    const point = event.target.closest?.("[data-curve-point]");
    if (!point || !svg.contains(point)) return;

    const index = Number(point.dataset.curvePoint);
    const handle = svg.querySelector(`[data-curve-handle="${index}"]`);
    active = { pointerId: event.pointerId, index, handle };
    if (handle) handle.dataset.active = "true";
    svg.classList.add("dragging");
    try { svg.setPointerCapture?.(event.pointerId); } catch { }
    event.preventDefault();
    queuePosition(event);
  }

  function onPointerMove(event) {
    if (!active || active.pointerId !== event.pointerId) return;
    event.preventDefault();
    queuePosition(event);
  }

  function finish(event) {
    if (!active || active.pointerId !== event.pointerId) return;
    const position = constrainPosition(active.index, positionFromEvent(event));
    pendingPosition = null;
    if (animationFrame) cancelAnimationFrame(animationFrame);
    animationFrame = 0;
    renderPosition(active.index, position);
    queueDotNetUpdate(active.index, position, true);
    active.handle?.removeAttribute("data-active");
    svg.classList.remove("dragging");
    try { svg.releasePointerCapture?.(event.pointerId); } catch { }
    active = null;
  }

  svg.addEventListener("pointerdown", onPointerDown);
  svg.addEventListener("pointermove", onPointerMove);
  svg.addEventListener("pointerup", finish);
  svg.addEventListener("pointercancel", finish);

  return {
    dispose() {
      disposed = true;
      if (animationFrame) cancelAnimationFrame(animationFrame);
      if (dotNetTimer) clearTimeout(dotNetTimer);
      latestDotNetUpdate = null;
      svg.removeEventListener("pointerdown", onPointerDown);
      svg.removeEventListener("pointermove", onPointerMove);
      svg.removeEventListener("pointerup", finish);
      svg.removeEventListener("pointercancel", finish);
    }
  };
}
