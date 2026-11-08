// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Tiny element builder. Text is always inserted as text, never as HTML, because
// website names and messages are untrusted
export function h(tag, props, ...kids) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(props || {})) {
    if (v == null || v === false) continue;
    if (k === 'class') el.className = v;
    else if (k.startsWith('on')) el.addEventListener(k.slice(2).toLowerCase(), v);
    else if (k === 'value') el.value = v;
    else if (k === 'checked' || k === 'selected' || k === 'disabled') el[k] = !!v;
    else el.setAttribute(k, v === true ? '' : v);
  }
  for (const kid of kids.flat(Infinity)) {
    if (kid == null || kid === false) continue;
    el.append(kid.nodeType ? kid : document.createTextNode(String(kid)));
  }
  return el;
}

export function mount(root, ...nodes) {
  root.replaceChildren(...nodes.flat(Infinity).filter(Boolean));
}
