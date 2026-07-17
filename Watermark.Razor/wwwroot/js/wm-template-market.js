function resolveElement(selector) {
  if (!selector) return null;
  return typeof selector === "string" ? document.querySelector(selector) : selector;
}

export function getScrollTop(selector) {
  const element = resolveElement(selector);
  return element?.scrollTop ?? 0;
}

export function setScrollTop(selector, value) {
  const element = resolveElement(selector);
  if (!element) return;
  const top = Number.isFinite(value) ? Math.max(0, value) : 0;
  element.scrollTo({ top, behavior: "auto" });
}
