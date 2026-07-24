import assert from "node:assert/strict";
import test from "node:test";
import {
  absoluteResizeDirections,
  coarseResizeDirections,
  createLayerInteractionVisual,
  isPointerTap,
  resolveScaleInteraction,
  restoreLayerInteractionVisual,
  shouldFinishReleasedPointer,
  shouldIgnoreSynthesizedMouse,
  touchPairMetrics,
  viewportGestureChange,
  updateLayerInteractionVisual
} from "../../Watermark.Razor/wwwroot/js/mac-template-canvas.js";

test("resize handles follow desktop and touch editing conventions", () => {
  assert.deepEqual(
    absoluteResizeDirections,
    ["n", "ne", "e", "se", "s", "sw", "w", "nw"]);
  assert.deepEqual(coarseResizeDirections, ["nw", "ne", "se", "sw"]);
});

test("scale interaction trusts Moveable's mature scale and anchored translation", () => {
  const interaction = resolveScaleInteraction({
    direction: [-1, -1],
    scale: [0.8, 0.75],
    drag: {
      beforeDist: [20, 10],
      dist: [200, 100]
    }
  }, [1, 1]);

  assert.deepEqual(interaction, {
    scaleX: 0.8,
    scaleY: 0.75,
    deltaX: 20,
    deltaY: 10
  });
  assert.equal("directionX" in interaction, false);
});

test("interaction visual transforms only the selected layer and never clones the flattened preview", () => {
  const layer = { style: { transform: "" } };
  const visual = createLayerInteractionVisual(layer);
  const active = {
    start: { rotation: 0, scaleX: 1, scaleY: 1 },
    deltaX: 20,
    deltaY: 10,
    rotation: 15,
    scaleX: 1.5,
    scaleY: 2
  };

  updateLayerInteractionVisual(visual, active, 2);

  assert.equal(layer.style.transform, " translate(40px, 20px) rotate(15deg) scale(1.5, 2)");
  assert.equal("element" in visual, false);
  assert.equal("image" in visual, false);
});

test("backdrop interaction restores both the foreground surface and live material", () => {
  const layer = { style: { transform: "translateX(3px)" } };
  const backdrop = { style: { transform: "rotate(4deg)" } };
  const visual = createLayerInteractionVisual(layer, backdrop);
  const active = {
    start: { rotation: 4, scaleX: 1, scaleY: 1 },
    deltaX: 5,
    deltaY: -2,
    rotation: 9,
    scaleX: 1,
    scaleY: 1
  };

  updateLayerInteractionVisual(visual, active, 1);
  assert.match(layer.style.transform, /translate\(5px, -2px\)/);
  assert.match(backdrop.style.transform, /translate\(5px, -2px\)/);

  restoreLayerInteractionVisual(visual);
  assert.equal(layer.style.transform, "translateX(3px)");
  assert.equal(backdrop.style.transform, "rotate(4deg)");
});

test("touch pointer moves never masquerade as pointer release in WebKit", () => {
  assert.equal(shouldFinishReleasedPointer({ pointerType: "touch", pointerId: 7, buttons: 0 }, 7), false);
  assert.equal(shouldFinishReleasedPointer({ pointerType: "mouse", pointerId: 7, buttons: 0 }, 7), true);
  assert.equal(shouldFinishReleasedPointer({ pointerType: "mouse", pointerId: 8, buttons: 0 }, 7), false);
  assert.equal(shouldFinishReleasedPointer({ pointerType: "mouse", pointerId: 7, buttons: 1 }, 7), false);
});

test("synthetic mouse events are ignored only inside the 500ms touch window", () => {
  assert.equal(shouldIgnoreSynthesizedMouse("mouse", 1499, 1000), true);
  assert.equal(shouldIgnoreSynthesizedMouse("mouse", 1500, 1000), false);
  assert.equal(shouldIgnoreSynthesizedMouse("touch", 1200, 1000), false);
  assert.equal(shouldIgnoreSynthesizedMouse("pen", 1200, 1000), false);
});

test("tap and drag share the six CSS pixel threshold", () => {
  const start = { clientX: 10, clientY: 20 };
  assert.equal(isPointerTap(start, { clientX: 13, clientY: 24 }), true);
  assert.equal(isPointerTap(start, { clientX: 16, clientY: 20 }), false);
});

test("two-touch metrics produce viewport-only pan and zoom deltas", () => {
  const start = touchPairMetrics([{ x: 0, y: 0 }, { x: 10, y: 0 }]);
  const latest = touchPairMetrics([{ x: 5, y: 4 }, { x: 25, y: 4 }]);

  assert.deepEqual(start, { x: 5, y: 0, distance: 10 });
  assert.deepEqual(latest, { x: 15, y: 4, distance: 20 });
  assert.deepEqual(viewportGestureChange(start, latest), {
    deltaX: 10,
    deltaY: 4,
    scale: 2
  });
});
