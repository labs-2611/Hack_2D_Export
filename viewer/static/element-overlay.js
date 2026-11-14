export function composeElementOverlay(base, overlay) {
  if (overlay.format !== "V2DGElementOverlay/1") throw Error("Unsupported element overlay format");
  if (base.view.id !== overlay.viewId) throw Error("Overlay view does not match base view");
  const rows = new Map(base.elements.map((element, index) => [element.key, index]));
  if (rows.size !== base.elements.length) throw Error("Base contains duplicate element keys");
  const elements = [...base.elements];
  const changed = new Set();
  for (const change of overlay.changes) {
    if (changed.has(change.key)) throw Error(`Duplicate overlay change: ${change.key}`);
    changed.add(change.key);
    const index = rows.get(change.key);
    if (change.op === "replace") {
      if (index === undefined || change.element?.key !== change.key) throw Error(`Invalid replacement: ${change.key}`);
      elements[index] = change.element;
    } else if (change.op === "delete") {
      if (index === undefined) throw Error(`Missing deletion target: ${change.key}`);
      elements[index] = null;
    } else if (change.op === "add") {
      if (index !== undefined || change.element?.key !== change.key) throw Error(`Invalid addition: ${change.key}`);
      elements.push(change.element);
    } else throw Error(`Unsupported overlay operation: ${change.op}`);
  }
  return {...base, elements: elements.filter(Boolean)};
}

export async function sha256Hex(bytes) {
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, "0")).join("");
}
