export function attachAndroidMouseDragScroll(scroller) {
  if (!scroller || !/Android/i.test(navigator.userAgent)) return { dispose() {} };

  let pointerId = null;
  let startX = 0;
  let startY = 0;
  let startScrollTop = 0;
  let dragging = false;
  let suppressClickUntil = 0;

  const canScroll = () => scroller.scrollHeight > scroller.clientHeight + 1;
  const isEditable = target => target instanceof Element
    && !!target.closest("input, textarea, select, label, [contenteditable='true'], [role='slider']");

  const reset = event => {
    if (pointerId === null || (event && event.pointerId !== pointerId)) return;
    try { scroller.releasePointerCapture?.(pointerId); } catch {}
    if (dragging) suppressClickUntil = performance.now() + 180;
    pointerId = null;
    dragging = false;
    scroller.classList.remove("is-mouse-dragging");
  };

  const onPointerDown = event => {
    if (event.pointerType !== "mouse" || event.button !== 0 || event.isPrimary === false
        || isEditable(event.target) || !canScroll()) return;
    pointerId = event.pointerId;
    startX = event.clientX;
    startY = event.clientY;
    startScrollTop = scroller.scrollTop;
    dragging = false;
  };

  const onPointerMove = event => {
    if (event.pointerId !== pointerId) return;
    const deltaX = event.clientX - startX;
    const deltaY = event.clientY - startY;
    if (!dragging) {
      if (Math.abs(deltaY) < 6) return;
      if (Math.abs(deltaX) > Math.abs(deltaY) * 1.2) {
        reset(event);
        return;
      }
      dragging = true;
      scroller.classList.add("is-mouse-dragging");
      scroller.setPointerCapture?.(pointerId);
    }
    scroller.scrollTop = startScrollTop - deltaY;
    event.preventDefault();
  };

  const onClickCapture = event => {
    if (performance.now() > suppressClickUntil) return;
    event.preventDefault();
    event.stopImmediatePropagation();
  };

  scroller.classList.add("supports-android-mouse-drag");
  scroller.addEventListener("pointerdown", onPointerDown);
  scroller.addEventListener("pointermove", onPointerMove, { passive: false });
  scroller.addEventListener("pointerup", reset);
  scroller.addEventListener("pointercancel", reset);
  scroller.addEventListener("click", onClickCapture, true);

  return {
    dispose() {
      scroller.removeEventListener("pointerdown", onPointerDown);
      scroller.removeEventListener("pointermove", onPointerMove);
      scroller.removeEventListener("pointerup", reset);
      scroller.removeEventListener("pointercancel", reset);
      scroller.removeEventListener("click", onClickCapture, true);
      scroller.classList.remove("supports-android-mouse-drag", "is-mouse-dragging");
    }
  };
}
