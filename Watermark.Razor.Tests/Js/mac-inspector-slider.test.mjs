import assert from "node:assert/strict";
import test from "node:test";
import {
  createTouchSlider,
  resolveTouchIntent,
  valueFromPointer
} from "../../Watermark.Razor/wwwroot/js/mac-inspector-slider.js";

function createGestureHarness() {
  const listeners = new Map();
  const calls = [];
  const range = { value: "0", style: { setProperty() {} } };
  const number = { value: "0" };
  const element = {
    dataset: { min: "0", max: "100", step: "1" },
    addEventListener(name, listener) { listeners.set(name, listener); },
    removeEventListener(name) { listeners.delete(name); },
    closest() {
      return {
        querySelector(selector) {
          return selector.includes("range") ? range : number;
        }
      };
    },
    getBoundingClientRect() { return { left: 0, width: 200 }; },
    setPointerCapture() {},
    hasPointerCapture() { return false; },
    releasePointerCapture() {}
  };
  const dotNetReference = {
    invokeMethodAsync(method, ...args) {
      calls.push([method, ...args]);
      return Promise.resolve();
    }
  };
  const dispatch = (name, overrides = {}) => {
    const event = {
      isPrimary: true,
      button: 0,
      pointerId: 1,
      pointerType: "touch",
      clientX: 50,
      clientY: 50,
      preventDefault() {},
      ...overrides
    };
    listeners.get(name)?.(event);
  };
  return { calls, dispatch, dotNetReference, element };
}

test("vertical touch intent keeps the page scroll gesture away from parameter changes", () => {
  assert.equal(resolveTouchIntent(2, 18), "scroll");
  assert.equal(resolveTouchIntent(-5, -24), "scroll");
  assert.equal(resolveTouchIntent(12, 10), "scroll");
});

test("horizontal touch intent activates slider adjustment", () => {
  assert.equal(resolveTouchIntent(18, 2), "adjust");
  assert.equal(resolveTouchIntent(-24, -5), "adjust");
});

test("small movement stays pending until intent is clear", () => {
  assert.equal(resolveTouchIntent(4, 6), "pending");
});

test("pointer values clamp and snap to the configured step", () => {
  assert.equal(valueFromPointer(150, 100, 200, 0, 100, 1), 25);
  assert.equal(valueFromPointer(175, 100, 200, -25, 25, 0.1), -6.2);
  assert.equal(valueFromPointer(50, 100, 200, 0, 100, 1), 0);
  assert.equal(valueFromPointer(350, 100, 200, 0, 100, 1), 100);
});

test("a vertical page swipe over the rail never opens an edit transaction", async () => {
  const harness = createGestureHarness();
  const controller = createTouchSlider(harness.element, harness.dotNetReference);

  harness.dispatch("pointerdown");
  harness.dispatch("pointermove", { clientX: 54, clientY: 78 });
  harness.dispatch("pointerup", { clientX: 54, clientY: 96 });
  await Promise.resolve();

  assert.deepEqual(harness.calls, []);
  controller.dispose();
});

test("a clearly horizontal swipe starts, changes and closes one edit transaction", async () => {
  const originalRequestAnimationFrame = globalThis.requestAnimationFrame;
  const originalCancelAnimationFrame = globalThis.cancelAnimationFrame;
  globalThis.requestAnimationFrame = callback => { callback(); return 1; };
  globalThis.cancelAnimationFrame = () => {};
  const harness = createGestureHarness();
  const controller = createTouchSlider(harness.element, harness.dotNetReference);

  harness.dispatch("pointerdown");
  harness.dispatch("pointermove", { clientX: 90, clientY: 52 });
  harness.dispatch("pointerup", { clientX: 110, clientY: 53 });
  await new Promise(resolve => setImmediate(resolve));

  assert.equal(harness.calls[0][0], "BeginTouchInteraction");
  assert.equal(harness.calls.at(-1)[0], "EndTouchInteraction");
  assert.ok(harness.calls.some(call => call[0] === "ChangeTouchValue"));

  controller.dispose();
  globalThis.requestAnimationFrame = originalRequestAnimationFrame;
  globalThis.cancelAnimationFrame = originalCancelAnimationFrame;
});
