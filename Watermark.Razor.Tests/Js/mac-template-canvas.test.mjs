import assert from "node:assert/strict";
import test from "node:test";
import {
  absoluteResizeDirections,
  applyOverlayGeometry,
  coarseResizeDirections,
  configureResizeGesture,
  createLayerInteractionVisual,
  createSceneInteractionVisual,
  isPointerTap,
  resizeAspectRatio,
  resizeCenterDelta,
  resolveMoveableRootContainer,
  resolveConstrainedResizeTranslation,
  resolveResizeDimensions,
  resolveScaleInteraction,
  resolveTextCornerResize,
  resolveTextCornerResizeFromMovement,
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

test("nested controls measure Moveable against the untransformed canvas viewport", () => {
  const viewport = {};
  const documentRoot = {};
  const root = {
    closest: selector => selector === ".mac-canvas-viewport" ? viewport : null,
    ownerDocument: { documentElement: documentRoot }
  };

  assert.equal(resolveMoveableRootContainer(root), viewport);
  assert.equal(resolveMoveableRootContainer({
    closest: () => null,
    ownerDocument: { documentElement: documentRoot }
  }), documentRoot);
  assert.equal(resolveMoveableRootContainer(null), null);
});

test("locked image ratio applies only to corner resize handles", () => {
  const start = { width: 100, height: 50 };

  assert.deepEqual(
    resolveResizeDimensions("ratio", start, [1, 0], 160, 50),
    { width: 160, height: 50, keepAspectRatio: false });
  assert.deepEqual(
    resolveResizeDimensions("ratio", start, [0, 1], 100, 90),
    { width: 100, height: 90, keepAspectRatio: false });
  assert.deepEqual(
    resolveResizeDimensions("ratio", start, [1, 1], 160, 70),
    { width: 160, height: 80, keepAspectRatio: true });
});

test("corner ratio resize keeps shrinking monotonically when only one pointer axis moves", () => {
  const start = { width: 100, height: 50 };

  assert.deepEqual(
    resolveResizeDimensions("ratio", start, [1, 1], 40, 50),
    { width: 40, height: 20, keepAspectRatio: true });
  assert.deepEqual(
    resolveResizeDimensions("ratio", start, [1, 1], 20, 50),
    { width: 20, height: 10, keepAspectRatio: true });
});

test("resize gesture gives Moveable the same ratio and minimum as the live proxy", () => {
  const calls = [];
  const event = {
    direction: [1, 1],
    setMin: value => calls.push(["min", value]),
    setRatio: value => calls.push(["ratio", value])
  };
  const active = {
    start: {
      resizeMode: "ratio",
      width: 100,
      height: 50
    }
  };

  assert.equal(resizeAspectRatio("ratio", active.start, [1, 0]), 0);
  assert.equal(configureResizeGesture(event, active), true);
  assert.deepEqual(calls, [
    ["min", [8, 8]],
    ["ratio", 2]
  ]);
  assert.deepEqual(
    resolveResizeDimensions("ratio", active.start, [1, 1], 40, 20, true),
    { width: 40, height: 20, keepAspectRatio: true });
});

test("text corner resize scales content while keeping decoration inset fixed", () => {
  const start = {
    width: 300,
    height: 100,
    resizeInset: 20,
    resizeFontSize: 10
  };

  const resized = resolveTextCornerResize(start, 300, 180);
  const resolved = resolveResizeDimensions("text", start, [1, 1], 300, 180);

  assert.ok(Math.abs(
    Math.hypot(resized.width - start.width, resized.height - start.height)
      - 80) < 0.000001);
  assert.ok(Math.abs(resized.width - 376.9219158112658) < 0.000001);
  assert.ok(Math.abs(resized.height - 121.97769023179023) < 0.000001);
  assert.deepEqual(resolved, {
    ...resized,
    keepAspectRatio: false
  });
});

test("text corner shrinking keeps the same one-to-one pointer travel", () => {
  const start = {
    width: 300,
    height: 100,
    resizeInset: 20,
    resizeFontSize: 10
  };

  const resized = resolveTextCornerResize(start, 300, 20);

  assert.ok(resized.resizeRatio < 1);
  assert.ok(Math.abs(
    Math.hypot(resized.width - start.width, resized.height - start.height)
      - 80) < 0.000001);
});

test("all text corners stay under the pointer after component scale and rotation", () => {
  const start = {
    width: 300,
    height: 100,
    resizeInset: 20,
    resizeFontSize: 10,
    scaleX: 1.5,
    scaleY: 0.75,
    rotation: 30
  };
  const radians = start.rotation * Math.PI / 180;
  const cosine = Math.cos(radians);
  const sine = Math.sin(radians);

  [[-1, -1], [1, -1], [1, 1], [-1, 1]].forEach(direction => {
    const outwardX = direction[0] * 280 * start.scaleX;
    const outwardY = direction[1] * 80 * start.scaleY;
    const rotatedX = outwardX * cosine - outwardY * sine;
    const rotatedY = outwardX * sine + outwardY * cosine;
    const length = Math.hypot(rotatedX, rotatedY);
    const pointerX = rotatedX / length * 80;
    const pointerY = rotatedY / length * 80;
    const resized = resolveTextCornerResizeFromMovement(
      start,
      direction,
      pointerX,
      pointerY);
    const deltaWidth = direction[0]
      * (resized.width - start.width) * start.scaleX;
    const deltaHeight = direction[1]
      * (resized.height - start.height) * start.scaleY;
    const handleX = deltaWidth * cosine - deltaHeight * sine;
    const handleY = deltaWidth * sine + deltaHeight * cosine;

    assert.ok(Math.abs(handleX - pointerX) < 0.000001);
    assert.ok(Math.abs(handleY - pointerY) < 0.000001);
  });
});

test("text resize does not give Moveable the old whitespace box ratio", () => {
  const calls = [];
  const managed = configureResizeGesture({
    direction: [1, 1],
    setMin: value => calls.push(["min", value]),
    setRatio: value => calls.push(["ratio", value])
  }, {
    start: {
      resizeMode: "text",
      width: 300,
      height: 100
    }
  });

  assert.equal(managed, false);
  assert.deepEqual(calls, [["min", [8, 8]]]);
});

test("text corner center delta preserves the opposite transformed anchor", () => {
  assert.deepEqual(
    resizeCenterDelta(
      { width: 100, height: 50, scaleX: 1, scaleY: 1, rotation: 0 },
      [-1, -1],
      160,
      80),
    [-30, -15]);
  const rotated = resizeCenterDelta(
    { width: 100, height: 50, scaleX: 1, scaleY: 1, rotation: 90 },
    [1, 1],
    160,
    80);
  assert.ok(Math.abs(rotated[0] + 15) < 0.000001);
  assert.ok(Math.abs(rotated[1] - 30) < 0.000001);
});

test("line resize derives its translation from the active handle without perpendicular drift", () => {
  assert.deepEqual(
    resolveConstrainedResizeTranslation(
      "horizontal",
      { width: 100, height: 4, scaleX: 1, scaleY: 1, rotation: 0 },
      [1, 0],
      160,
      4),
    {
      centerDeltaX: 30,
      centerDeltaY: 0,
      deltaX: 0,
      deltaY: 0
    });

  const rotated = resolveConstrainedResizeTranslation(
    "horizontal",
    { width: 100, height: 4, scaleX: 1, scaleY: 1, rotation: 90 },
    [1, 0],
    160,
    4);
  assert.ok(Math.abs(rotated.centerDeltaX) < 0.000001);
  assert.ok(Math.abs(rotated.centerDeltaY - 30) < 0.000001);
  assert.ok(Math.abs(rotated.deltaX + 30) < 0.000001);
  assert.ok(Math.abs(rotated.deltaY - 30) < 0.000001);
});

test("committed overlay geometry updates position and size together", () => {
  const element = { style: {} };
  applyOverlayGeometry(element, {
    x: 70,
    y: 50,
    width: 40,
    height: 20,
    parentWidth: 400,
    parentHeight: 200,
    offsetXPercent: 5,
    offsetYPercent: -5,
    scaleX: 1,
    scaleY: 1,
    rotation: 0
  }, 0.5);

  assert.deepEqual(element.style, {
    left: "35px",
    top: "25px",
    width: "20px",
    height: "10px",
    transform: "translate(10px, -5px) rotate(0deg) scale(1, 1)"
  });
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

test("logical container resize transforms its DOM group and keeps descendant surfaces with the proxy", () => {
  const childLayer = { style: { transform: "" } };
  const logicalGroup = {
    dataset: { sceneGroupId: "container" },
    style: { transform: "translate(4px, 3px)" },
    childLayer
  };
  const visual = createSceneInteractionVisual(
    "container",
    [],
    [],
    [logicalGroup]);
  const active = {
    kind: "resize",
    start: {
      width: 50,
      height: 40,
      rotation: 0,
      scaleX: 1,
      scaleY: 1
    },
    width: 150,
    height: 80,
    deltaX: 0,
    deltaY: 0,
    centerDeltaX: 50,
    centerDeltaY: 20,
    rotation: 0,
    scaleX: 1,
    scaleY: 1
  };

  updateLayerInteractionVisual(visual, active, 1);

  assert.equal(
    logicalGroup.style.transform,
    "translate(4px, 3px) translate(50px, 20px) rotate(0deg) scale(3, 2)");
  assert.equal(childLayer.style.transform, "");
  restoreLayerInteractionVisual(visual);
  assert.equal(logicalGroup.style.transform, "translate(4px, 3px)");
});

test("resize proxy follows the resized frame center instead of drifting toward the old bounds", () => {
  const layer = { style: { transform: "" } };
  const visual = createLayerInteractionVisual(layer);
  const active = {
    kind: "resize",
    start: {
      width: 40,
      height: 30,
      rotation: 0,
      scaleX: 1,
      scaleY: 1
    },
    width: 140,
    height: 90,
    deltaX: 0,
    deltaY: 0,
    centerDeltaX: 50,
    centerDeltaY: 30,
    rotation: 0,
    scaleX: 1,
    scaleY: 1
  };

  updateLayerInteractionVisual(visual, active, 1);

  assert.equal(
    layer.style.transform,
    " translate(50px, 30px) rotate(0deg) scale(3.5, 3)");
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
