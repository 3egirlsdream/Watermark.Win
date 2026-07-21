const defaultIntentThreshold = 8;

export function resolveTouchIntent(deltaX, deltaY, threshold = defaultIntentThreshold) {
  const x = Math.abs(Number.isFinite(deltaX) ? deltaX : 0);
  const y = Math.abs(Number.isFinite(deltaY) ? deltaY : 0);
  if (Math.max(x, y) < Math.max(0, threshold)) return "pending";
  // Bias ambiguous diagonal gestures toward page scrolling. Parameter changes
  // start only after horizontal movement is clearly dominant.
  return x > y * 1.25 ? "adjust" : "scroll";
}

export function valueFromPointer(clientX, left, width, minimum, maximum, step) {
  const min = Number.isFinite(minimum) ? minimum : 0;
  const max = Number.isFinite(maximum) && maximum >= min ? maximum : min;
  const extent = Number.isFinite(width) && width > 0 ? width : 1;
  const ratio = Math.min(1, Math.max(0, (clientX - left) / extent));
  const raw = min + (max - min) * ratio;
  const normalizedStep = Number.isFinite(step) && step > 0 ? step : 1;
  const stepped = min + Math.round((raw - min) / normalizedStep) * normalizedStep;
  const precision = Math.min(12, decimalPlaces(normalizedStep));
  const stable = Number(stepped.toFixed(precision));
  return Math.min(max, Math.max(min, stable));
}

function numberAttribute(element, name, fallback) {
  const value = Number.parseFloat(element?.dataset?.[name] ?? "");
  return Number.isFinite(value) ? value : fallback;
}

function decimalPlaces(step) {
  const text = String(step);
  const exponentIndex = text.toLowerCase().indexOf("e-");
  if (exponentIndex >= 0) return Number.parseInt(text.slice(exponentIndex + 2), 10) || 0;
  const decimalIndex = text.indexOf(".");
  return decimalIndex < 0 ? 0 : text.length - decimalIndex - 1;
}

export function createTouchSlider(element, dotNetReference) {
  if (!element || !dotNetReference) return { dispose() {} };

  let gesture = null;
  let animationFrame = 0;
  let latestClientX = 0;
  let callbackQueue = Promise.resolve();

  function invoke(method, ...args) {
    callbackQueue = callbackQueue
      .then(() => dotNetReference.invokeMethodAsync(method, ...args))
      .catch(() => undefined);
  }

  function settings() {
    const minimum = numberAttribute(element, "min", 0);
    const maximum = numberAttribute(element, "max", 100);
    const step = numberAttribute(element, "step", 1);
    return { minimum, maximum, step };
  }

  function paintValue(value, step) {
    const host = element.closest(".mac-inspector-slider");
    const nativeRange = host?.querySelector('input[type="range"]');
    const numberInput = host?.querySelector(".inspector-number-input");
    const { minimum, maximum } = settings();
    const progress = maximum > minimum ? (value - minimum) / (maximum - minimum) * 100 : 0;
    if (nativeRange) {
      nativeRange.value = String(value);
      nativeRange.style.setProperty("--mac-slider-progress", `${Math.min(100, Math.max(0, progress))}%`);
    }
    if (numberInput) numberInput.value = value.toFixed(Math.min(8, decimalPlaces(step)));
  }

  function notifyValue(clientX) {
    animationFrame = 0;
    if (!gesture || gesture.intent !== "adjust") return;
    const rect = element.getBoundingClientRect();
    const { minimum, maximum, step } = settings();
    const value = valueFromPointer(clientX, rect.left, rect.width, minimum, maximum, step);
    paintValue(value, step);
    invoke("ChangeTouchValue", value);
  }

  function scheduleValue(clientX) {
    latestClientX = clientX;
    if (!animationFrame) animationFrame = requestAnimationFrame(() => notifyValue(latestClientX));
  }

  function beginAdjustment(event) {
    if (!gesture || gesture.intent === "adjust") return;
    gesture.intent = "adjust";
    element.setPointerCapture?.(event.pointerId);
    invoke("BeginTouchInteraction");
  }

  function finishAdjustment(event, updateFinalValue) {
    if (!gesture) return;
    const wasAdjusting = gesture.intent === "adjust";
    if (wasAdjusting && updateFinalValue) {
      if (animationFrame) cancelAnimationFrame(animationFrame);
      animationFrame = 0;
      latestClientX = event.clientX;
      notifyValue(latestClientX);
    } else if (animationFrame) {
      cancelAnimationFrame(animationFrame);
      animationFrame = 0;
    }
    if (element.hasPointerCapture?.(event.pointerId)) element.releasePointerCapture(event.pointerId);
    gesture = null;
    if (wasAdjusting) invoke("EndTouchInteraction");
  }

  function onPointerDown(event) {
    if (!event.isPrimary || event.button !== 0) return;
    gesture = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      pointerType: event.pointerType,
      intent: event.pointerType === "mouse" ? "adjust" : "pending"
    };

    if (gesture.intent === "adjust") {
      event.preventDefault();
      element.setPointerCapture?.(event.pointerId);
      invoke("BeginTouchInteraction");
      scheduleValue(event.clientX);
    }
  }

  function onPointerMove(event) {
    if (!gesture || gesture.pointerId !== event.pointerId) return;
    if (gesture.intent === "pending") {
      const intent = resolveTouchIntent(
        event.clientX - gesture.startX,
        event.clientY - gesture.startY);
      if (intent === "scroll") {
        gesture.intent = "scroll";
        return;
      }
      if (intent === "pending") return;
      beginAdjustment(event);
    }

    if (gesture.intent !== "adjust") return;
    event.preventDefault();
    scheduleValue(event.clientX);
  }

  function onPointerUp(event) {
    if (!gesture || gesture.pointerId !== event.pointerId) return;
    if (gesture.intent === "pending") {
      // A stationary tap is an intentional seek. A vertical gesture never
      // reaches this branch because it is classified as scroll first.
      beginAdjustment(event);
    }
    if (gesture?.intent === "adjust") event.preventDefault();
    finishAdjustment(event, true);
  }

  function onPointerCancel(event) {
    if (!gesture || gesture.pointerId !== event.pointerId) return;
    finishAdjustment(event, false);
  }

  element.addEventListener("pointerdown", onPointerDown);
  element.addEventListener("pointermove", onPointerMove);
  element.addEventListener("pointerup", onPointerUp);
  element.addEventListener("pointercancel", onPointerCancel);

  return {
    dispose() {
      if (animationFrame) cancelAnimationFrame(animationFrame);
      element.removeEventListener("pointerdown", onPointerDown);
      element.removeEventListener("pointermove", onPointerMove);
      element.removeEventListener("pointerup", onPointerUp);
      element.removeEventListener("pointercancel", onPointerCancel);
      gesture = null;
    }
  };
}
