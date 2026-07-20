import Cropper, {
    EVENT_ACTION_END,
    EVENT_ACTION_START,
    EVENT_CHANGE,
    EVENT_TRANSFORM
} from '../vendor/cropperjs/cropper.esm.min.js';

const MINIMUM_NORMALIZED_EXTENT = .025;
const EPSILON = .000001;
const CANDIDATE_TOLERANCE = .00002;
const DISPLAY_TOLERANCE = .01;

// Cropper.js owns all pointer, touch, resize and wheel interaction. The
// adapter only translates its selection/image matrices to WMCropSettings so
// the existing SkiaSharp preview and export pipeline remains authoritative.
const CROPPER_TEMPLATE = `
<cropper-canvas>
  <cropper-image scalable translatable></cropper-image>
  <cropper-shade theme-color="rgba(3, 6, 10, 0.66)"></cropper-shade>
  <cropper-handle action="move" plain data-image-pan></cropper-handle>
  <cropper-selection movable resizable keyboard precise outlined theme-color="#ffffff">
    <cropper-grid role="grid" covered rows="3" columns="3" theme-color="rgba(255, 255, 255, 0.82)"></cropper-grid>
    <cropper-handle action="move" plain></cropper-handle>
    <cropper-handle action="n-resize" theme-color="#ffffff"></cropper-handle>
    <cropper-handle action="e-resize" theme-color="#ffffff"></cropper-handle>
    <cropper-handle action="s-resize" theme-color="#ffffff"></cropper-handle>
    <cropper-handle action="w-resize" theme-color="#ffffff"></cropper-handle>
    <cropper-handle action="ne-resize" theme-color="#ffffff"></cropper-handle>
    <cropper-handle action="nw-resize" theme-color="#ffffff"></cropper-handle>
    <cropper-handle action="se-resize" theme-color="#ffffff"></cropper-handle>
    <cropper-handle action="sw-resize" theme-color="#ffffff"></cropper-handle>
  </cropper-selection>
</cropper-canvas>`;

const SELECTION_THEME = `
:host {
  outline: rgba(255, 255, 255, .98) solid 1px !important;
}
`;

const GRID_THEME = `
:host([bordered]) { border: 0 !important; }
:host > span + span { border-top: 1px solid var(--theme-color) !important; }
:host > span > span + span { border-left: 1px solid var(--theme-color) !important; }
`;

const HANDLE_THEME = `
:host([action$=-resize]) {
  background: transparent !important;
  box-sizing: border-box;
  z-index: 3;
}
:host([action$=-resize])::after {
  background: #fff !important;
  box-sizing: border-box;
  opacity: 1;
}
:host([action=n-resize]), :host([action=s-resize]) {
  height: 44px !important;
}
:host([action=n-resize]) { top: -22px !important; }
:host([action=s-resize]) { bottom: -22px !important; }
:host([action=n-resize])::after, :host([action=s-resize])::after {
  border-radius: 3px;
  height: 6px !important;
  width: 30px !important;
}
:host([action=e-resize]), :host([action=w-resize]) {
  width: 44px !important;
}
:host([action=e-resize]) { right: -22px !important; }
:host([action=w-resize]) { left: -22px !important; }
:host([action=e-resize])::after, :host([action=w-resize])::after {
  border-radius: 3px;
  height: 30px !important;
  width: 6px !important;
}
:host([action=ne-resize]), :host([action=nw-resize]),
:host([action=se-resize]), :host([action=sw-resize]) {
  height: 44px !important;
  width: 44px !important;
}
:host([action=ne-resize]), :host([action=se-resize]) { right: -22px !important; }
:host([action=nw-resize]), :host([action=sw-resize]) { left: -22px !important; }
:host([action=ne-resize]), :host([action=nw-resize]) { top: -22px !important; }
:host([action=se-resize]), :host([action=sw-resize]) { bottom: -22px !important; }
:host([action=ne-resize])::after, :host([action=nw-resize])::after,
:host([action=se-resize])::after, :host([action=sw-resize])::after {
  background: transparent !important;
  border-color: #fff;
  border-radius: 2px;
  border-style: solid;
  border-width: 0;
  height: 28px !important;
  width: 28px !important;
}
:host([action=nw-resize])::after {
  border-left-width: 4px;
  border-top-width: 4px;
  transform: translate(-4px, -4px) !important;
}
:host([action=ne-resize])::after {
  border-right-width: 4px;
  border-top-width: 4px;
  transform: translate(calc(-100% + 4px), -4px) !important;
}
:host([action=sw-resize])::after {
  border-bottom-width: 4px;
  border-left-width: 4px;
  transform: translate(-4px, calc(-100% + 4px)) !important;
}
:host([action=se-resize])::after {
  border-bottom-width: 4px;
  border-right-width: 4px;
  transform: translate(calc(-100% + 4px), calc(-100% + 4px)) !important;
}
`;

function appendShadowTheme(element, css, key) {
    const shadow = element?.shadowRoot;
    if (!shadow || element.dataset.wmCropTheme === key) return;
    element.dataset.wmCropTheme = key;
    if ('adoptedStyleSheets' in shadow
        && typeof CSSStyleSheet !== 'undefined'
        && typeof CSSStyleSheet.prototype.replaceSync === 'function') {
        const sheet = new CSSStyleSheet();
        sheet.replaceSync(css);
        shadow.adoptedStyleSheets = [...shadow.adoptedStyleSheets, sheet];
        return;
    }
    const style = document.createElement('style');
    style.dataset.wmCropTheme = key;
    style.textContent = css;
    shadow.appendChild(style);
}

function installCropperTheme(selection) {
    appendShadowTheme(selection, SELECTION_THEME, 'selection');
    appendShadowTheme(selection.querySelector('cropper-grid'), GRID_THEME, 'grid');
    selection.querySelectorAll('cropper-handle[action$="-resize"]')
        .forEach(handle => appendShadowTheme(handle, HANDLE_THEME, 'handle'));
}

function finite(value, fallback) {
    const number = Number(value);
    return Number.isFinite(number) ? number : fallback;
}

function clampFinite(value, minimum, maximum, fallback) {
    return Math.min(maximum, Math.max(minimum, finite(value, fallback)));
}

function readSettings(value) {
    return {
        centerX: finite(value?.centerX ?? value?.CenterX, .5),
        centerY: finite(value?.centerY ?? value?.CenterY, .5),
        visibleWidth: finite(value?.visibleWidth ?? value?.VisibleWidth, 1),
        visibleHeight: finite(value?.visibleHeight ?? value?.VisibleHeight, 1),
        quarterTurns: finite(value?.quarterTurns ?? value?.QuarterTurns, 0),
        flipHorizontal: Boolean(value?.flipHorizontal ?? value?.FlipHorizontal),
        flipVertical: Boolean(value?.flipVertical ?? value?.FlipVertical),
        straightenDegrees: finite(value?.straightenDegrees ?? value?.StraightenDegrees, 0),
        aspectPreset: finite(value?.aspectPreset ?? value?.AspectPreset, 1),
        aspectPortrait: Boolean(value?.aspectPortrait ?? value?.AspectPortrait)
    };
}

function resolveAspect(settings, planeWidth, planeHeight) {
    let aspect = settings.aspectPreset === 0
        ? null
        : settings.aspectPreset === 1
            ? planeWidth / planeHeight
            : settings.aspectPreset === 2
                ? 1
                : settings.aspectPreset === 3
                    ? 4 / 3
                    : settings.aspectPreset === 4
                        ? 3 / 2
                        : settings.aspectPreset === 5
                            ? 16 / 9
                            : null;
    if (aspect !== null && settings.aspectPortrait && Math.abs(aspect - 1) > EPSILON)
        aspect = 1 / aspect;
    return aspect;
}

function normalize(value, sourceWidth, sourceHeight) {
    const next = { ...value };
    next.quarterTurns = ((Math.round(next.quarterTurns) % 4) + 4) % 4;
    const planeWidth = next.quarterTurns % 2 === 0 ? sourceWidth : sourceHeight;
    const planeHeight = next.quarterTurns % 2 === 0 ? sourceHeight : sourceWidth;
    let width = clampFinite(next.visibleWidth, MINIMUM_NORMALIZED_EXTENT, 1, 1);
    let height = clampFinite(next.visibleHeight, MINIMUM_NORMALIZED_EXTENT, 1, 1);
    let centerX = clampFinite(next.centerX, 0, 1, .5);
    let centerY = clampFinite(next.centerY, 0, 1, .5);
    let degrees = clampFinite(next.straightenDegrees, -45, 45, 0);
    if (Math.abs(degrees) < .05) degrees = 0;

    const aspect = resolveAspect(next, planeWidth, planeHeight);
    if (aspect !== null) {
        let pixelWidth = width * planeWidth;
        let pixelHeight = height * planeHeight;
        const area = Math.max(1, pixelWidth * pixelHeight);
        pixelWidth = Math.sqrt(area * aspect);
        pixelHeight = pixelWidth / aspect;
        const fit = Math.min(1, planeWidth / pixelWidth, planeHeight / pixelHeight);
        width = pixelWidth * fit / planeWidth;
        height = pixelHeight * fit / planeHeight;
    }

    const radians = Math.abs(degrees) * Math.PI / 180;
    const cosine = Math.abs(Math.cos(radians));
    const sine = Math.abs(Math.sin(radians));
    let extentX = cosine * width * planeWidth / 2 + sine * height * planeHeight / 2;
    let extentY = sine * width * planeWidth / 2 + cosine * height * planeHeight / 2;
    const coverageScale = Math.min(
        1,
        planeWidth / Math.max(EPSILON, extentX * 2),
        planeHeight / Math.max(EPSILON, extentY * 2));
    if (coverageScale < 1) {
        width *= coverageScale;
        height *= coverageScale;
        extentX *= coverageScale;
        extentY *= coverageScale;
    }

    const minimumWidth = Math.min(1, Math.max(MINIMUM_NORMALIZED_EXTENT, 44 / planeWidth));
    const minimumHeight = Math.min(1, Math.max(MINIMUM_NORMALIZED_EXTENT, 44 / planeHeight));
    if (width < minimumWidth || height < minimumHeight) {
        const minimumScale = Math.max(minimumWidth / width, minimumHeight / height);
        const maximumScale = Math.min(
            planeWidth / Math.max(EPSILON, extentX * 2),
            planeHeight / Math.max(EPSILON, extentY * 2));
        const scale = Math.min(minimumScale, maximumScale);
        width *= scale;
        height *= scale;
        extentX *= scale;
        extentY *= scale;
    }

    const centerPixelsX = Math.min(planeWidth - extentX, Math.max(extentX, centerX * planeWidth));
    const centerPixelsY = Math.min(planeHeight - extentY, Math.max(extentY, centerY * planeHeight));
    next.centerX = centerPixelsX / planeWidth;
    next.centerY = centerPixelsY / planeHeight;
    next.visibleWidth = Math.min(1, Math.max(MINIMUM_NORMALIZED_EXTENT, width));
    next.visibleHeight = Math.min(1, Math.max(MINIMUM_NORMALIZED_EXTENT, height));
    next.straightenDegrees = degrees;
    return next;
}

function multiply(outer, inner) {
    return [
        outer[0] * inner[0] + outer[2] * inner[1],
        outer[1] * inner[0] + outer[3] * inner[1],
        outer[0] * inner[2] + outer[2] * inner[3],
        outer[1] * inner[2] + outer[3] * inner[3],
        outer[0] * inner[4] + outer[2] * inner[5] + outer[4],
        outer[1] * inner[4] + outer[3] * inner[5] + outer[5]
    ];
}

function invert(matrix) {
    const determinant = matrix[0] * matrix[3] - matrix[1] * matrix[2];
    if (Math.abs(determinant) < EPSILON) return null;
    return [
        matrix[3] / determinant,
        -matrix[1] / determinant,
        -matrix[2] / determinant,
        matrix[0] / determinant,
        (matrix[2] * matrix[5] - matrix[3] * matrix[4]) / determinant,
        (matrix[1] * matrix[4] - matrix[0] * matrix[5]) / determinant
    ];
}

function transformPoint(matrix, x, y) {
    return {
        x: matrix[0] * x + matrix[2] * y + matrix[4],
        y: matrix[1] * x + matrix[3] * y + matrix[5]
    };
}

function sourceToPlane(settings, sourceWidth, sourceHeight, planeWidth, planeHeight) {
    const rotate = settings.quarterTurns === 1
        ? [0, 1, -1, 0, sourceHeight, 0]
        : settings.quarterTurns === 2
            ? [-1, 0, 0, -1, sourceWidth, sourceHeight]
            : settings.quarterTurns === 3
                ? [0, -1, 1, 0, 0, sourceWidth]
                : [1, 0, 0, 1, 0, 0];
    const flip = [
        settings.flipHorizontal ? -1 : 1,
        0,
        0,
        settings.flipVertical ? -1 : 1,
        settings.flipHorizontal ? planeWidth : 0,
        settings.flipVertical ? planeHeight : 0
    ];
    return multiply(flip, rotate);
}

function geometryMatches(left, right) {
    return Math.abs(left.centerX - right.centerX) <= CANDIDATE_TOLERANCE
        && Math.abs(left.centerY - right.centerY) <= CANDIDATE_TOLERANCE
        && Math.abs(left.visibleWidth - right.visibleWidth) <= CANDIDATE_TOLERANCE
        && Math.abs(left.visibleHeight - right.visibleHeight) <= CANDIDATE_TOLERANCE;
}

function settingsMatch(left, right) {
    return geometryMatches(left, right)
        && left.quarterTurns === right.quarterTurns
        && left.flipHorizontal === right.flipHorizontal
        && left.flipVertical === right.flipVertical
        && Math.abs(left.straightenDegrees - right.straightenDegrees) <= CANDIDATE_TOLERANCE
        && left.aspectPreset === right.aspectPreset
        && left.aspectPortrait === right.aspectPortrait;
}

function createInstance(root, dotNet, initialImageUrl, initialSettings, initialWidth, initialHeight) {
    const host = root.querySelector('.wm-cropper-host');
    // Blazor may recreate the JS module while preserving the host element.
    // Remove any source/canvas nodes left by a disposed Cropper instance so a
    // hidden source image can never push the new canvas out of the viewport.
    host.replaceChildren();
    const source = document.createElement('img');
    source.src = initialImageUrl;
    source.alt = '待裁切图片';
    source.draggable = false;
    host.appendChild(source);
    let settings = readSettings(initialSettings);
    let sourceWidth = Math.max(1, initialWidth);
    let sourceHeight = Math.max(1, initialHeight);
    let sourceUrl = initialImageUrl;
    let cropper = null;
    let canvas = null;
    let cropImage = null;
    let selection = null;
    let ready = false;
    let syncing = false;
    let syncRevision = 0;
    let callbackRaf = 0;
    let forcePublishPending = false;
    let lastCallbackTime = -Infinity;
    let idleFinalizeTimer = 0;
    let gestureRevision = 0;
    let actionActive = false;
    let localInteractionActive = false;
    let disposed = false;

    function cssToCanvasMatrix(cssMatrix) {
        const width = cropImage.offsetWidth || sourceWidth;
        const height = cropImage.offsetHeight || sourceHeight;
        const originX = width / 2;
        const originY = height / 2;
        const left = cropImage.offsetLeft;
        const top = cropImage.offsetTop;
        return [
            cssMatrix[0], cssMatrix[1], cssMatrix[2], cssMatrix[3],
            left + originX - cssMatrix[0] * originX - cssMatrix[2] * originY + cssMatrix[4],
            top + originY - cssMatrix[1] * originX - cssMatrix[3] * originY + cssMatrix[5]
        ];
    }

    function canvasToCssMatrix(canvasMatrix) {
        const width = cropImage.offsetWidth || sourceWidth;
        const height = cropImage.offsetHeight || sourceHeight;
        const originX = width / 2;
        const originY = height / 2;
        const left = cropImage.offsetLeft;
        const top = cropImage.offsetTop;
        return [
            canvasMatrix[0], canvasMatrix[1], canvasMatrix[2], canvasMatrix[3],
            canvasMatrix[4] - left - originX + canvasMatrix[0] * originX + canvasMatrix[2] * originY,
            canvasMatrix[5] - top - originY + canvasMatrix[1] * originX + canvasMatrix[3] * originY
        ];
    }

    function createPlaneMapping(cssMatrix) {
        const turns = settings.quarterTurns;
        const planeWidth = turns % 2 === 0 ? sourceWidth : sourceHeight;
        const planeHeight = turns % 2 === 0 ? sourceHeight : sourceWidth;
        const discrete = sourceToPlane(settings, sourceWidth, sourceHeight, planeWidth, planeHeight);
        const planeToSource = invert(discrete);
        if (!planeToSource) return null;
        const sourceToCanvas = cssToCanvasMatrix(cssMatrix);
        const planeToCanvas = multiply(sourceToCanvas, planeToSource);
        const canvasToPlane = invert(planeToCanvas);
        if (!canvasToPlane) return null;
        const scaleX = Math.hypot(planeToCanvas[0], planeToCanvas[1]);
        const scaleY = Math.hypot(planeToCanvas[2], planeToCanvas[3]);
        if (scaleX < EPSILON || scaleY < EPSILON) return null;
        return {
            planeWidth,
            planeHeight,
            discrete,
            planeToCanvas,
            canvasToPlane,
            scaleX,
            scaleY
        };
    }

    function deriveSettings(rect, cssMatrix) {
        const mapping = createPlaneMapping(cssMatrix);
        if (!mapping) return null;
        const center = transformPoint(
            mapping.canvasToPlane,
            rect.x + rect.width / 2,
            rect.y + rect.height / 2);
        return {
            ...settings,
            centerX: center.x / mapping.planeWidth,
            centerY: center.y / mapping.planeHeight,
            visibleWidth: rect.width / (mapping.scaleX * mapping.planeWidth),
            visibleHeight: rect.height / (mapping.scaleY * mapping.planeHeight)
        };
    }

    function selectionRectForSettings(value, cssMatrix) {
        const mapping = createPlaneMapping(cssMatrix);
        if (!mapping) return null;
        const center = transformPoint(
            mapping.planeToCanvas,
            value.centerX * mapping.planeWidth,
            value.centerY * mapping.planeHeight);
        const width = value.visibleWidth * mapping.planeWidth * mapping.scaleX;
        const height = value.visibleHeight * mapping.planeHeight * mapping.scaleY;
        return {
            x: center.x - width / 2,
            y: center.y - height / 2,
            width,
            height
        };
    }

    function imageMatrixForSettingsAtSelection(value, cssMatrix) {
        const mapping = createPlaneMapping(cssMatrix);
        if (!mapping || selection.width < EPSILON || selection.height < EPSILON) return null;
        const desiredScaleX = selection.width / Math.max(
            EPSILON,
            value.visibleWidth * mapping.planeWidth);
        const desiredScaleY = selection.height / Math.max(
            EPSILON,
            value.visibleHeight * mapping.planeHeight);
        if (Math.max(desiredScaleX, desiredScaleY) / Math.max(
            EPSILON,
            Math.min(desiredScaleX, desiredScaleY)) > 1.001) return null;
        const unitX = {
            x: mapping.planeToCanvas[0] / mapping.scaleX,
            y: mapping.planeToCanvas[1] / mapping.scaleX
        };
        const unitY = {
            x: mapping.planeToCanvas[2] / mapping.scaleY,
            y: mapping.planeToCanvas[3] / mapping.scaleY
        };
        const centerPlaneX = value.centerX * mapping.planeWidth;
        const centerPlaneY = value.centerY * mapping.planeHeight;
        const centerCanvasX = selection.x + selection.width / 2;
        const centerCanvasY = selection.y + selection.height / 2;
        const planeToCanvas = [
            unitX.x * desiredScaleX,
            unitX.y * desiredScaleX,
            unitY.x * desiredScaleY,
            unitY.y * desiredScaleY,
            0,
            0
        ];
        planeToCanvas[4] = centerCanvasX
            - planeToCanvas[0] * centerPlaneX
            - planeToCanvas[2] * centerPlaneY;
        planeToCanvas[5] = centerCanvasY
            - planeToCanvas[1] * centerPlaneX
            - planeToCanvas[3] * centerPlaneY;
        return canvasToCssMatrix(multiply(planeToCanvas, mapping.discrete));
    }

    function publish(force = false) {
        if (force) forcePublishPending = true;
        if (callbackRaf) return;
        callbackRaf = requestAnimationFrame(function dispatch(time) {
            const finalPublish = forcePublishPending && !actionActive;
            if (!finalPublish && time - lastCallbackTime < 32) {
                callbackRaf = requestAnimationFrame(dispatch);
                return;
            }
            callbackRaf = 0;
            forcePublishPending = false;
            lastCallbackTime = time;
            if (disposed) return;
            const snapshot = { ...settings };
            const revision = ++gestureRevision;
            dotNet.invokeMethodAsync('OnCropGestureAsync', snapshot, revision)
                .catch(() => { })
                .finally(() => {
                    if (finalPublish && !actionActive) localInteractionActive = false;
                });
        });
    }

    function cancelIdleFinalize() {
        if (!idleFinalizeTimer) return;
        clearTimeout(idleFinalizeTimer);
        idleFinalizeTimer = 0;
    }

    function finishInteraction() {
        cancelIdleFinalize();
        publish(true);
    }

    function scheduleIdleFinalize() {
        cancelIdleFinalize();
        idleFinalizeTimer = window.setTimeout(() => {
            idleFinalizeTimer = 0;
            if (disposed || actionActive) return;
            publish(true);
        }, 80);
    }

    function commitCandidate(normalized) {
        settings = normalized;
        localInteractionActive = true;
        if (!actionActive) scheduleIdleFinalize();
        publish();
    }

    function changeSelection(rect, aspect) {
        syncing = true;
        try {
            selection.$change(rect.x, rect.y, rect.width, rect.height, aspect ?? Number.NaN);
        } finally {
            syncing = false;
        }
    }

    function selectionRectsMatch(left, right) {
        return Math.abs(left.x - right.x) <= DISPLAY_TOLERANCE
            && Math.abs(left.y - right.y) <= DISPLAY_TOLERANCE
            && Math.abs(left.width - right.width) <= DISPLAY_TOLERANCE
            && Math.abs(left.height - right.height) <= DISPLAY_TOLERANCE;
    }

    function selectionFitsCanvas(rect) {
        return rect.x >= -CANDIDATE_TOLERANCE
            && rect.y >= -CANDIDATE_TOLERANCE
            && rect.x + rect.width <= canvas.clientWidth + CANDIDATE_TOLERANCE
            && rect.y + rect.height <= canvas.clientHeight + CANDIDATE_TOLERANCE;
    }

    function onSelectionChange(event) {
        // Cropper.js emits change events while it creates and centers its
        // internal selection. They are setup work, not user gestures, and
        // must not overwrite the centered draft before the first sync.
        if (!ready || syncing || event.target !== selection) return;
        const cssMatrix = cropImage.$getTransform();
        let requested = { ...event.detail };
        let constrainedToCanvas = false;
        if (!selectionFitsCanvas(requested)) {
            const moving = Math.abs(requested.width - selection.width) <= DISPLAY_TOLERANCE
                && Math.abs(requested.height - selection.height) <= DISPLAY_TOLERANCE;
            if (!moving || requested.width > canvas.clientWidth || requested.height > canvas.clientHeight) {
                event.preventDefault();
                return;
            }
            requested.x = Math.min(canvas.clientWidth - requested.width, Math.max(0, requested.x));
            requested.y = Math.min(canvas.clientHeight - requested.height, Math.max(0, requested.y));
            constrainedToCanvas = true;
        }
        const candidate = deriveSettings(requested, cssMatrix);
        if (!candidate) {
            event.preventDefault();
            return;
        }
        const normalized = normalize(candidate, sourceWidth, sourceHeight);
        const constrainedRect = selectionRectForSettings(normalized, cssMatrix);
        if (!constrainedRect) {
            event.preventDefault();
            return;
        }
        if (constrainedToCanvas
            || !geometryMatches(candidate, normalized)
            || !selectionRectsMatch(requested, constrainedRect)) {
            event.preventDefault();
            changeSelection(
                constrainedRect,
                resolveAspect(normalized, normalized.quarterTurns % 2 === 0 ? sourceWidth : sourceHeight,
                    normalized.quarterTurns % 2 === 0 ? sourceHeight : sourceWidth));
        }
        commitCandidate(normalized);
    }

    function onImageTransform(event) {
        // The source image performs an initial contain/center transform as it
        // loads. Accept transforms only after our authoritative settings have
        // been applied, otherwise that internal matrix becomes a crop draft.
        if (!ready || syncing || event.target !== cropImage) return;
        const candidate = deriveSettings({
            x: selection.x,
            y: selection.y,
            width: selection.width,
            height: selection.height
        }, event.detail.matrix);
        if (!candidate) {
            event.preventDefault();
            return;
        }
        const normalized = normalize(candidate, sourceWidth, sourceHeight);
        if (!geometryMatches(candidate, normalized)) {
            const constrainedMatrix = imageMatrixForSettingsAtSelection(normalized, event.detail.matrix);
            event.preventDefault();
            if (!constrainedMatrix) return;
            syncing = true;
            try {
                cropImage.$setTransform(constrainedMatrix);
            } finally {
                syncing = false;
            }
        }
        commitCandidate(normalized);
    }

    function onActionStart() {
        if (syncing) return;
        cancelIdleFinalize();
        actionActive = true;
        localInteractionActive = true;
        root.focus({ preventScroll: true });
    }

    function onActionEnd() {
        if (syncing) return;
        actionActive = false;
        finishInteraction();
    }

    function applySourceCoordinateSize() {
        const width = `${sourceWidth}px`;
        const height = `${sourceHeight}px`;
        if (cropImage.style.width !== width) cropImage.style.width = width;
        if (cropImage.style.height !== height) cropImage.style.height = height;

        // Cropper.js restores the loaded proxy's natural dimensions in its
        // load handler. Force that style change through layout before matrix
        // conversion so a downscaled proxy can never offset the source-sized
        // transform on slower WebViews.
        void cropImage.offsetWidth;
    }

    function syncFromSettings(revealWhenSynchronized = false) {
        if (!ready || disposed || !root.isConnected) return;
        applySourceCoordinateSize();
        settings = normalize(settings, sourceWidth, sourceHeight);
        const turns = settings.quarterTurns;
        const planeWidth = turns % 2 === 0 ? sourceWidth : sourceHeight;
        const planeHeight = turns % 2 === 0 ? sourceHeight : sourceWidth;
        const cropWidth = settings.visibleWidth * planeWidth;
        const cropHeight = settings.visibleHeight * planeHeight;
        const canvasWidth = Math.max(1, canvas.clientWidth);
        const canvasHeight = Math.max(1, canvas.clientHeight);
        const marginX = Math.min(48, Math.max(22, canvasWidth * .08));
        const marginY = Math.min(50, Math.max(24, canvasHeight * .08));
        const scale = Math.max(.0001, Math.min(
            Math.max(1, canvasWidth - marginX * 2) / Math.max(1, cropWidth),
            Math.max(1, canvasHeight - marginY * 2) / Math.max(1, cropHeight)));
        const width = cropWidth * scale;
        const height = cropHeight * scale;
        const selectionX = (canvasWidth - width) / 2;
        const selectionY = (canvasHeight - height) / 2;
        const centerCanvasX = canvasWidth / 2;
        const centerCanvasY = canvasHeight / 2;
        const centerPlaneX = settings.centerX * planeWidth;
        const centerPlaneY = settings.centerY * planeHeight;
        const radians = settings.straightenDegrees * Math.PI / 180;
        const cosine = Math.cos(radians);
        const sine = Math.sin(radians);
        const planeToCanvas = [
            cosine * scale,
            sine * scale,
            -sine * scale,
            cosine * scale,
            centerCanvasX - cosine * scale * centerPlaneX + sine * scale * centerPlaneY,
            centerCanvasY - sine * scale * centerPlaneX - cosine * scale * centerPlaneY
        ];
        const discrete = sourceToPlane(settings, sourceWidth, sourceHeight, planeWidth, planeHeight);
        const sourceToCanvas = multiply(planeToCanvas, discrete);
        const cssMatrix = canvasToCssMatrix(sourceToCanvas);
        const aspect = resolveAspect(settings, planeWidth, planeHeight);
        const revision = ++syncRevision;
        syncing = true;
        selection.aspectRatio = aspect ?? Number.NaN;
        selection.$nextTick(() => {
            if (disposed || revision !== syncRevision) return;
            applySourceCoordinateSize();
            selection.$change(selectionX, selectionY, width, height, aspect ?? Number.NaN, true);
            cropImage.$setTransform(cssMatrix);
            syncing = false;
            if (revealWhenSynchronized) {
                root.classList.add('is-ready');
                root.classList.remove('wm-crop-canvas-failed');
            }
        });
    }

    const imageStyleObserver = new MutationObserver(() => {
        if (!ready || disposed || syncing || !cropImage) return;
        if (cropImage.offsetWidth === sourceWidth && cropImage.offsetHeight === sourceHeight) return;

        // Some Android WebViews can deliver a second cached-image load after
        // CropperImage has already reported ready. Cropper.js then writes the
        // proxy's natural dimensions back to the host. Never expose that
        // transient geometry: restore the source coordinate plane and matrix
        // before revealing the canvas again.
        root.classList.remove('is-ready');
        syncFromSettings(true);
    });

    function onKeyDown(event) {
        if (event.key === 'Enter') {
            dotNet.invokeMethodAsync('OnCropApplyAsync').catch(() => { });
            event.preventDefault();
            event.stopPropagation();
            return;
        }
        if (event.key === 'Escape') {
            dotNet.invokeMethodAsync('OnCropCancelAsync').catch(() => { });
            event.preventDefault();
            event.stopPropagation();
            return;
        }
        if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)
            || !selection) return;
        const amount = event.shiftKey ? 10 : 1;
        const x = event.key === 'ArrowLeft' ? -amount : event.key === 'ArrowRight' ? amount : 0;
        const y = event.key === 'ArrowUp' ? -amount : event.key === 'ArrowDown' ? amount : 0;
        localInteractionActive = true;
        selection.$move(x, y);
        finishInteraction();
        event.preventDefault();
        // CropperSelection also implements keyboard movement. This capture
        // handler adds Shift acceleration, so stop the same key from moving
        // the selection a second time inside the web component.
        event.stopPropagation();
    }

    function onDoubleClick(event) {
        dotNet.invokeMethodAsync('OnCropFitAsync').catch(() => { });
        event.preventDefault();
    }

    function onWindowBlur() {
        if (!actionActive) return;
        actionActive = false;
        finishInteraction();
    }

    const observer = new ResizeObserver(() => syncFromSettings());

    async function waitForCropperImageLayout() {
        await cropImage.$ready();
        // `$ready()` only means the nested HTMLImageElement has pixels. On a
        // throttled Android WebView it can resolve before CropperImage's own
        // load handler has restored the proxy's natural size and contain
        // matrix. Wait for that handler to finish before applying our
        // source-coordinate size and authoritative crop transform last.
        for (let frame = 0; frame < 60 && !cropImage.$isReady; frame++)
            await new Promise(resolve => requestAnimationFrame(resolve));
        await cropImage.$nextTick();
        await new Promise(resolve => requestAnimationFrame(resolve));
    }

    async function initialize() {
        cropper = new Cropper(source, { container: host, template: CROPPER_TEMPLATE });
        canvas = cropper.getCropperCanvas();
        cropImage = cropper.getCropperImage();
        selection = cropper.getCropperSelection();
        if (!canvas || !cropImage || !selection)
            throw new Error('Cropper.js failed to create the crop canvas.');
        installCropperTheme(selection);
        canvas.addEventListener(EVENT_ACTION_START, onActionStart);
        canvas.addEventListener(EVENT_ACTION_END, onActionEnd);
        selection.addEventListener(EVENT_CHANGE, onSelectionChange);
        cropImage.addEventListener(EVENT_TRANSFORM, onImageTransform);
        root.addEventListener('keydown', onKeyDown, true);
        root.addEventListener('dblclick', onDoubleClick);
        window.addEventListener('blur', onWindowBlur);
        observer.observe(root);
        imageStyleObserver.observe(cropImage, { attributes: true, attributeFilter: ['style'] });
        await waitForCropperImageLayout();
        if (disposed) return;
        applySourceCoordinateSize();
        ready = true;
        syncFromSettings(true);
    }

    initialize().catch(() => {
        root.classList.add('wm-crop-canvas-failed');
    });

    return {
        update(nextImageUrl, nextSettings, nextWidth, nextHeight) {
            const nextSourceWidth = Math.max(1, nextWidth);
            const nextSourceHeight = Math.max(1, nextHeight);
            const dimensionsChanged = nextSourceWidth !== sourceWidth || nextSourceHeight !== sourceHeight;
            sourceWidth = nextSourceWidth;
            sourceHeight = nextSourceHeight;
            if (cropImage && nextImageUrl && nextImageUrl !== sourceUrl) {
                sourceUrl = nextImageUrl;
                ready = false;
                root.classList.remove('is-ready');
                source.src = nextImageUrl;
                cropImage.src = nextImageUrl;
                const loadingUrl = nextImageUrl;
                waitForCropperImageLayout().then(() => {
                    if (disposed || sourceUrl !== loadingUrl) return;
                    applySourceCoordinateSize();
                    ready = true;
                    syncFromSettings(true);
                }).catch(() => { });
            }
            const incoming = readSettings(nextSettings);
            if (!localInteractionActive && (!settingsMatch(incoming, settings) || dimensionsChanged)) {
                settings = incoming;
                syncFromSettings();
            }
        },
        dispose() {
            disposed = true;
            observer.disconnect();
            imageStyleObserver.disconnect();
            if (callbackRaf) cancelAnimationFrame(callbackRaf);
            cancelIdleFinalize();
            if (canvas) {
                canvas.removeEventListener(EVENT_ACTION_START, onActionStart);
                canvas.removeEventListener(EVENT_ACTION_END, onActionEnd);
            }
            selection?.removeEventListener(EVENT_CHANGE, onSelectionChange);
            cropImage?.removeEventListener(EVENT_TRANSFORM, onImageTransform);
            root.removeEventListener('keydown', onKeyDown, true);
            root.removeEventListener('dblclick', onDoubleClick);
            window.removeEventListener('blur', onWindowBlur);
            cropper?.destroy();
            source.remove();
        }
    };
}

const instances = new WeakMap();

export function attach(root, dotNet, imageUrl, settings, sourceWidth, sourceHeight) {
    instances.get(root)?.dispose();
    instances.set(root, createInstance(root, dotNet, imageUrl, settings, sourceWidth, sourceHeight));
}

export function update(root, imageUrl, settings, sourceWidth, sourceHeight) {
    instances.get(root)?.update(imageUrl, settings, sourceWidth, sourceHeight);
}

export function dispose(root) {
    instances.get(root)?.dispose();
    instances.delete(root);
}
