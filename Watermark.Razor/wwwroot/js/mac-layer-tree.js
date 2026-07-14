function asInteger(value, fallback = 0) {
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function parentIdOf(element) {
  return element?.dataset.layerParentId || "";
}

function clearCandidate(active) {
  active?.candidateElement?.classList.remove(
    "pointer-drop-active",
    "pointer-drop-target",
    "pointer-drop-before",
    "pointer-drop-after");
  if (!active) return;
  active.candidateElement = null;
  active.candidate = null;
}

function candidateFromPoint(root, clientX, clientY) {
  const hit = root.ownerDocument.elementFromPoint(clientX, clientY);
  if (!hit || !root.contains(hit)) return null;

  const slot = hit.closest?.("[data-layer-drop-slot]");
  if (slot && root.contains(slot)) {
    return {
      parentId: parentIdOf(slot),
      index: asInteger(slot.dataset.layerIndex),
      element: slot,
      targetClass: "pointer-drop-active"
    };
  }

  const node = hit.closest?.("[data-layer-control-id]");
  const row = hit.closest?.(".layer-row");
  if (!node || !row || !root.contains(node)) return null;
  const rect = row.getBoundingClientRect();
  const ratio = rect.height > 0 ? (clientY - rect.top) / rect.height : 0.5;
  const parentId = parentIdOf(node);
  const index = asInteger(node.dataset.layerIndex);
  const childCount = asInteger(node.dataset.layerContainerCount, -1);

  if (childCount >= 0 && ratio >= 0.3 && ratio <= 0.7) {
    return {
      parentId: node.dataset.layerControlId,
      index: childCount,
      element: node,
      targetClass: "pointer-drop-target"
    };
  }

  return {
    parentId,
    index: index + (ratio > 0.5 ? 1 : 0),
    element: node,
    targetClass: ratio > 0.5 ? "pointer-drop-after" : "pointer-drop-before"
  };
}

export function createLayerTree(root, callback) {
  let active = null;
  let disposed = false;
  const scrollHost = root.querySelector(".layer-tree-scroll");

  function invoke(method, ...args) {
    try {
      return Promise.resolve(callback.invokeMethodAsync(method, ...args));
    } catch (error) {
      return Promise.reject(error);
    }
  }

  function finishVisuals(ending) {
    clearCandidate(ending);
    root.classList.remove("pointer-dragging");
    void invoke("CancelLayerContainerHover");
  }

  function onPointerDown(event) {
    if (disposed || event.button !== 0) return;
    const handle = event.target.closest?.("[data-layer-drag-id]");
    const controlId = handle?.dataset.layerDragId;
    if (!handle || !controlId || !root.contains(handle)) return;

    event.preventDefault();
    event.stopPropagation();
    active = {
      pointerId: event.pointerId,
      controlId,
      handle,
      startX: event.clientX,
      startY: event.clientY,
      dragging: false,
      allowed: false,
      candidate: null,
      candidateElement: null,
      candidateKey: null,
      validationToken: 0
    };
    try {
      handle.setPointerCapture?.(event.pointerId);
    } catch {
      // WKWebView can reject pointer capture for synthesized mouse drags. The
      // document-level move/up listeners below keep the gesture alive.
    }
    const starting = active;
    starting.beginPromise = invoke("BeginLayerPointerDrag", controlId)
      .then(allowed => {
        if (active === starting) starting.allowed = Boolean(allowed);
      })
      .catch(error => console.error("Layer pointer drag failed to start.", error));
  }

  function updateAutoScroll(clientY) {
    if (!scrollHost) return;
    const rect = scrollHost.getBoundingClientRect();
    if (clientY < rect.top + 28) scrollHost.scrollTop -= 10;
    else if (clientY > rect.bottom - 28) scrollHost.scrollTop += 10;
  }

  function showValidatedCandidate(gesture, next, key, token) {
    if (active !== gesture || token !== gesture.validationToken) return;
    clearCandidate(gesture);
    gesture.candidateKey = key;
    gesture.candidate = next;
    gesture.candidateElement = next.element;
    next.element.classList.add(next.targetClass);
  }

  function onPointerMove(event) {
    const gesture = active;
    if (!gesture || gesture.pointerId !== event.pointerId) return;
    const distance = Math.hypot(event.clientX - gesture.startX, event.clientY - gesture.startY);
    if (!gesture.dragging && distance < 4) return;
    gesture.dragging = true;
    root.classList.add("pointer-dragging");
    event.preventDefault();
    event.stopPropagation();
    updateAutoScroll(event.clientY);

    const next = candidateFromPoint(root, event.clientX, event.clientY);
    if (!next) {
      gesture.candidateKey = null;
      clearCandidate(gesture);
      void invoke("CancelLayerContainerHover");
      return;
    }
    const key = `${next.parentId}:${next.index}:${next.targetClass}`;
    if (key === gesture.candidateKey) return;
    gesture.candidateKey = key;
    const token = ++gesture.validationToken;
    invoke("CanDropLayerPointer", gesture.controlId, next.parentId, next.index)
      .then(valid => {
        if (valid) {
          showValidatedCandidate(gesture, next, key, token);
          if (next.targetClass === "pointer-drop-target")
            void invoke("HoverLayerContainer", next.parentId);
          else
            void invoke("CancelLayerContainerHover");
        }
        else if (active === gesture && token === gesture.validationToken) clearCandidate(gesture);
      })
      .catch(error => console.error("Layer pointer drop validation failed.", error));
  }

  async function finish(event, cancelled) {
    const ending = active;
    if (!ending || ending.pointerId !== event.pointerId) return;
    const finalCandidate = ending.dragging
      ? candidateFromPoint(root, event.clientX, event.clientY) || ending.candidate
      : null;
    event.preventDefault();
    event.stopPropagation();
    active = null;
    finishVisuals(ending);
    try {
      await ending.beginPromise;
      const valid = !cancelled && ending.allowed && finalCandidate
        ? await invoke(
          "CanDropLayerPointer",
          ending.controlId,
          finalCandidate.parentId,
          finalCandidate.index)
        : false;
      if (valid) {
        await invoke(
          "CommitLayerPointerDrop",
          ending.controlId,
          finalCandidate.parentId,
          finalCandidate.index);
      } else {
        await invoke("EndLayerPointerDrag");
      }
    } catch (error) {
      console.error("Layer pointer drop failed.", error);
      await invoke("EndLayerPointerDrag");
    }
  }

  function onPointerUp(event) { void finish(event, false); }
  function onPointerCancel(event) { void finish(event, true); }

  root.addEventListener("pointerdown", onPointerDown, true);
  root.ownerDocument.addEventListener("pointermove", onPointerMove, true);
  root.ownerDocument.addEventListener("pointerup", onPointerUp, true);
  root.ownerDocument.addEventListener("pointercancel", onPointerCancel, true);

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      finishVisuals(active);
      active = null;
      root.removeEventListener("pointerdown", onPointerDown, true);
      root.ownerDocument.removeEventListener("pointermove", onPointerMove, true);
      root.ownerDocument.removeEventListener("pointerup", onPointerUp, true);
      root.ownerDocument.removeEventListener("pointercancel", onPointerCancel, true);
    }
  };
}
