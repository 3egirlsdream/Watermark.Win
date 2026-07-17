export function scrollActiveToolIntoView(root, tool) {
  const rail = root?.querySelector(".wm-dock-tools > div");
  const active = root?.querySelector(`[data-mobile-tool="${CSS.escape(tool)}"]`);
  if (!rail || !active) return;

  const railBounds = rail.getBoundingClientRect();
  const activeBounds = active.getBoundingClientRect();
  const fullyVisible = activeBounds.left >= railBounds.left + 8
    && activeBounds.right <= railBounds.right - 8;
  if (fullyVisible) return;

  active.scrollIntoView({
    behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth",
    block: "nearest",
    inline: "center"
  });
}
