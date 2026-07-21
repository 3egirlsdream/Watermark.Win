import assert from "node:assert/strict";
import test from "node:test";
import {
  createPreviewInteractionVisual,
  shouldFinishReleasedPointer,
  updatePreviewInteractionVisual
} from "../../Watermark.Razor/wwwroot/js/mac-template-canvas.js";

function createElement(tagName, ownerDocument) {
  return {
    tagName,
    ownerDocument,
    style: {},
    children: [],
    appendChild(child) { this.children.push(child); child.parentElement = this; },
    removeAttribute() {}
  };
}

function createVisualHarness() {
  const document = { createElement: tagName => createElement(tagName, document) };
  let targetRect = { left: 140, top: 90, width: 80, height: 40 };
  const stage = createElement("div", document);
  stage.getBoundingClientRect = () => ({ left: 100, top: 50, width: 400, height: 300 });
  const target = createElement("div", document);
  target.getBoundingClientRect = () => targetRect;
  const preview = createElement("img", document);
  preview.currentSrc = "blob:mobile-preview";
  preview.src = "blob:mobile-preview";
  preview.getBoundingClientRect = () => ({ left: 100, top: 50, width: 400, height: 300 });
  preview.cloneNode = () => createElement("img", document);
  return {
    preview,
    stage,
    target,
    setTargetRect(value) { targetRect = value; }
  };
}

test("interaction visual reuses the decoded preview as an image instead of a CSS blob background", () => {
  const harness = createVisualHarness();
  const visual = createPreviewInteractionVisual(harness.target, harness.preview, harness.stage);

  assert.ok(visual);
  assert.equal(visual.image.src, "blob:mobile-preview");
  assert.equal(visual.element.style.backgroundImage, undefined);
  assert.equal(visual.element.style.left, "40px");
  assert.equal(visual.element.style.top, "40px");
  assert.equal(visual.image.style.left, "-40px");
  assert.equal(visual.image.style.top, "-40px");
  assert.equal(harness.stage.children[0], visual.element);
  assert.equal(visual.element.children[0], visual.image);
});

test("interaction visual moves and scales its preview crop with the container", () => {
  const harness = createVisualHarness();
  const visual = createPreviewInteractionVisual(harness.target, harness.preview, harness.stage);
  harness.setTargetRect({ left: 180, top: 120, width: 160, height: 80 });

  updatePreviewInteractionVisual(visual);

  assert.equal(visual.element.style.left, "80px");
  assert.equal(visual.element.style.top, "70px");
  assert.equal(visual.element.style.width, "160px");
  assert.equal(visual.element.style.height, "80px");
  assert.equal(visual.image.style.width, "800px");
  assert.equal(visual.image.style.height, "600px");
  assert.equal(visual.image.style.left, "-80px");
  assert.equal(visual.image.style.top, "-80px");
});

test("touch pointer moves never masquerade as pointer release in WebKit", () => {
  assert.equal(shouldFinishReleasedPointer({ pointerType: "touch", pointerId: 7, buttons: 0 }, 7), false);
  assert.equal(shouldFinishReleasedPointer({ pointerType: "mouse", pointerId: 7, buttons: 0 }, 7), true);
  assert.equal(shouldFinishReleasedPointer({ pointerType: "mouse", pointerId: 8, buttons: 0 }, 7), false);
  assert.equal(shouldFinishReleasedPointer({ pointerType: "mouse", pointerId: 7, buttons: 1 }, 7), false);
});
